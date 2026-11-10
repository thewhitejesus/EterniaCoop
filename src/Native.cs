// Access to GameNative.dll, the game's C++ engine: module base, the addresses this mod uses, and safe code patching.
// Built with the C# 5 compiler that ships with Windows, so: no $"", no ?., no nameof.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    // Every GameNative.dll address the mod uses, as RVAs (offsets from the module base). The values here are the
    // supported build's (Compatibility.KnownSha256); Features.Check replaces them with where Sites actually found
    // them, so they follow the game's code after an update. "FUN_xxxxxx" are Ghidra's names for the function there.
    static class Rva
    {
        // Input object (C++ side of the game's input). Global pointer; fields: +0x10 player count,
        // +0x18 remap table (0x1C x 24-byte entries per player), +0x30 buttons (0xAE x 20 per player),
        // +0x48 sticks (2 x 20 per player).
        public static int InputObject = 0x6459B8;

        // Emulated PSP pad (held/pressed/released words, repeat timers, per-button bools), filled from player 1.
        public static int Pad = 0x8CA140;
        public const int PadSize = 0x144;

        // Battle: one 0x20-byte command block per party slot; each party actor reads its own via actor+0xF0.
        public static int CmdBlocks = 0x12CFB60;
        public const int CmdSize = 0x20;
        public static int ActorArray = 0x12CFC98;
        public const int ActorSize = 0x248, ActorCount = 13;
        // Battle state flags (byte); bit 0 = battle menu open / paused (set by Start in FUN_D2C80).
        public static int BattleFlags = 0x12CFB3A;
        // Battle menu owner (byte): command block index + 1 of whoever opened it with the menu button, 0 = none.
        public static int MenuOpener = 0x12CFB4C;
        // Target selection (hold the target button): the targeting actor's index + 1, 0 = none. While set and its
        // button is held, the battle pauses (BattleFlags bit 0, like the menu) and FUN_113EA0 switches its target.
        public static int TargetActor = 0x12CFB4D;

        // Party (menu/save side): member ids (4 x short) and control mode per slot (byte: 0 Auto, 1 Semi-Auto).
        public static int PartyIds = 0x11CBCD0, SlotModes = 0x11CBCF8;

        // Current area (2 shorts, e.g. 06 00 6F 00 in the field); all zero on the title screen.
        public static int Area = 0x11CBF84;

        // Functions we call or hook.
        public static int BattleCommands = 0x1129C0;   // FUN_1121E0(uint* padHeld): pad -> slot 0's command block
        public static int BuildPad = 0x19E3F0;         // FUN_19DBC0(pad, rate, flag): input object (player 1) -> PSP pad
        public static int ApplyHit = 0x8EB00;          // FUN_8E320(type, flags, hit, attacker, victim)
        // Game options object (pointer); +0x21 = rumble on.
        public static int GameOptions = 0x6459A0;

        // Battle camera: zoom setting (short, 0x200 closest .. 0x29C farthest; copied to the zoom target each frame
        // while the camera mode is 0) and camera mode (byte: 0 normal, 1, >= 2 scripted - spells, mystic artes).
        public static int CamZoom = 0x12CFB38, CamMode = 0x12CFB3F;
        // Battle camera scroll (short): -(centre x >> 4), clamped to the field (FUN_D6420's result).
        public static int CamScroll = 0x12CFA78;
        // The scroll the camera shows now (short); it eases toward CamScroll each frame.
        public static int CamScrollNow = 0x12CFA58;
        // The zoom the camera shows now (short); it eases toward the zoom setting (CamZoom).
        public static int ZoomNow = 0x12CFA5C;

        // Battle background (BattleGround): FUN_10D500 dispatches the stage's background routine; FUN_11E6F0 draws one
        // textured quad (primitive struct); FUN_120F70 draws a solid rectangle from the work struct (its y at FillY).
        public static int DrawBg = 0x10DCE0, DrawQuad = 0x11EF30, FillRect = 0x1217B0, FillY = 0x11CF622;
        // Global draw state (texture binding cache, see FUN_11D1C0 / FUN_11D8C0), 0x20 bytes each.
        public static int DrawStateA = 0x5952D0, DrawStateB = 0x8669D0;
    }

    // Battle actor field offsets.
    static class Actor
    {
        public const int Kind = 0x3C;       // short: 0 = unused, 1 = in the fight (party character or enemy), 2 = knocked out
        public const int Side = 0x51;       // byte: 0 = party, 1 = enemy
        public const int Block = 0x56;      // byte: command block index (0 = player 1's); the PSP port rumbles block 0 only
        public const int Control = 0x57;    // byte: 0 Auto, 1 Semi-Auto (same values as Rva.SlotModes)
        public const int CmdBlock = 0xF0;   // pointer to this actor's command block (Rva.CmdBlocks + slot * CmdSize)
        public const int X = 0x10, Y = 0x12;
    }

    static class Native
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32")]
        static extern bool VirtualProtect(IntPtr addr, UIntPtr size, uint newProtect, out uint oldProtect);
        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr WineVersionFn();
        const uint PAGE_EXECUTE_READWRITE = 0x40;

        // Wine / Proton version (e.g. "9.0") when the game runs on Linux or a Steam Deck, null on Windows.
        // Wine's ntdll exports wine_get_version; Windows' doesn't.
        public static string WineVersion()
        {
            try
            {
                IntPtr fn = GetProcAddress(GetModuleHandle("ntdll.dll"), "wine_get_version");
                if (fn == IntPtr.Zero) return null;
                var get = (WineVersionFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(WineVersionFn));
                return Marshal.PtrToStringAnsi(get()) ?? "?";
            }
            catch { return null; }
        }

        static long moduleBase;

        // 0 until the game has loaded GameNative.dll (it does that before initializing input).
        public static long Base
        {
            get
            {
                if (moduleBase == 0) moduleBase = GetModuleHandle("GameNative.dll").ToInt64();
                return moduleBase;
            }
        }

        public static IntPtr At(int rva) { return new IntPtr(Base + rva); }

        // Whether a DLL with this name is loaded in the game (e.g. a native mod).
        public static bool ModuleLoaded(string name) { return GetModuleHandle(name) != IntPtr.Zero; }

        public static bool Matches(int rva, byte[] expected)
        {
            var now = new byte[expected.Length];
            Marshal.Copy(At(rva), now, 0, now.Length);
            for (int i = 0; i < now.Length; i++) if (now[i] != expected[i]) return false;
            return true;
        }

        [DllImport("kernel32")]
        static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32")]
        static extern bool FlushInstructionCache(IntPtr process, IntPtr addr, UIntPtr size);

        // Writes bytes into code or data, lifting page protection for the write. The instruction cache is flushed
        // afterwards: x86 processors don't need it, but translators that run the game on other processors (Android,
        // ARM) keep their own copy of the code and only learn of the change this way.
        public static void Write(int rva, byte[] bytes)
        {
            IntPtr p = At(rva);
            var size = new UIntPtr((uint)bytes.Length);
            uint old;
            VirtualProtect(p, size, PAGE_EXECUTE_READWRITE, out old);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            VirtualProtect(p, size, old, out old);
            FlushInstructionCache(GetCurrentProcess(), p, size);
        }

        // Replaces `from` with `to` only if the bytes at rva are exactly `from`; logs and skips otherwise.
        public static bool Patch(int rva, byte[] from, byte[] to)
        {
            if (!Matches(rva, from))
            {
                Plugin.Log.LogError(string.Format("Unexpected code at GameNative+0x{0:X}, left unpatched", rva));
                return false;
            }
            Write(rva, to);
            return true;
        }

        [DllImport("kernel32")]
        static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
        const uint MEM_COMMIT_RESERVE = 0x3000;

        // Executable memory within reach of a 32-bit relative call from GameNative (for small code stubs). Tries
        // 64 KB-aligned addresses just past the module's image, then just before it. IntPtr.Zero if none is free.
        public static IntPtr AllocNear(int size)
        {
            if (Base == 0) return IntPtr.Zero;
            long image = Marshal.ReadInt32(At(Marshal.ReadInt32(At(0x3C)) + 24 + 56));   // OptionalHeader.SizeOfImage
            long after = (Base + image + 0xFFFF) & ~0xFFFFL;
            for (long step = 0; step < 0x40000000L; step += 0x10000)
                foreach (long at in new[] { after + step, (Base & ~0xFFFFL) - 0x10000 - step })
                {
                    IntPtr p = VirtualAlloc(new IntPtr(at), new UIntPtr((uint)size), MEM_COMMIT_RESERVE, PAGE_EXECUTE_READWRITE);
                    if (p != IntPtr.Zero) return p;
                }
            return IntPtr.Zero;
        }

        // Start of the function containing rva, from the module's unwind table (.pdata: RUNTIME_FUNCTION entries,
        // sorted). A function split into chunks is followed back to its primary entry. 0 if rva is in no function.
        public static int FunctionStart(int rva)
        {
            if (Base == 0) return 0;
            int pe = Marshal.ReadInt32(At(0x3C));
            int dir = pe + 24 + 112 + 3 * 8;   // PE32+ optional header: data directory 3 = exception table
            int table = Marshal.ReadInt32(At(dir)), count = Marshal.ReadInt32(At(dir + 4)) / 12;
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2, e = table + mid * 12;
                int begin = Marshal.ReadInt32(At(e)), end = Marshal.ReadInt32(At(e + 4));
                if (rva < begin) hi = mid - 1;
                else if (rva >= end) lo = mid + 1;
                else
                {
                    for (int hop = 0; hop < 16; hop++)   // UNW_FLAG_CHAININFO: the primary entry follows the unwind codes
                    {
                        int unwind = Marshal.ReadInt32(At(e + 8)) & ~1;
                        if ((Marshal.ReadByte(At(unwind)) >> 3 & 4) == 0) return begin;
                        int codes = Marshal.ReadByte(At(unwind + 2));
                        e = unwind + 4 + ((codes + 1) & ~1) * 2;
                        begin = Marshal.ReadInt32(At(e));
                    }
                    return begin;
                }
            }
            return 0;
        }

        public static IntPtr Alloc(int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            Clear(p, size);
            return p;
        }

        // Clear and Copy run several times per battle frame (and Clear for every blanked player's input each frame):
        // plain memory operations, no managed allocations.
        public static unsafe void Clear(IntPtr p, int size)
        {
            byte* b = (byte*)p;
            int i = 0;
            for (; i + 8 <= size; i += 8) *(long*)(b + i) = 0;
            for (; i < size; i++) b[i] = 0;
        }

        public static unsafe void Copy(IntPtr from, IntPtr to, int size)
        {
            Buffer.MemoryCopy((void*)from, (void*)to, size, size);
        }

        public static unsafe float ReadFloat(IntPtr p) { return *(float*)p; }
    }
}
