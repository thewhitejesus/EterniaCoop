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
        public static int InputObject = 0x6449F8;

        // Emulated PSP pad (held/pressed/released words, repeat timers, per-button bools), filled from player 1.
        public static int Pad = 0x8C9180;
        public const int PadSize = 0x144;

        // Battle: one 0x20-byte command block per party slot; each party actor reads its own via actor+0xF0.
        public static int CmdBlocks = 0x12CEBA0;
        public const int CmdSize = 0x20;
        public static int ActorArray = 0x12CECD8;
        public const int ActorSize = 0x248, ActorCount = 13;
        // Battle state flags (byte); bit 0 = battle menu open / paused (set by Start in FUN_D2C80).
        public static int BattleFlags = 0x12CEB7A;
        // Battle menu owner (byte): command block index + 1 of whoever opened it with the menu button, 0 = none.
        public static int MenuOpener = 0x12CEB8C;

        // Party (menu/save side): member ids (4 x short) and control mode per slot (byte: 0 Auto, 1 Semi-Auto).
        public static int PartyIds = 0x11CAD10, SlotModes = 0x11CAD38;

        // Current area (2 shorts, e.g. 06 00 6F 00 in the field); all zero on the title screen.
        public static int Area = 0x11CAFC4;

        // Functions we call or hook.
        public static int BattleCommands = 0x1121E0;   // FUN_1121E0(uint* padHeld): pad -> slot 0's command block
        public static int BuildPad = 0x19DBC0;         // FUN_19DBC0(pad, rate, flag): input object (player 1) -> PSP pad
        public static int ApplyHit = 0x8E320;          // FUN_8E320(type, flags, hit, attacker, victim)

        // Game options object (pointer); +0x21 = rumble on.
        public static int GameOptions = 0x6449E0;

        // Battle camera: zoom setting (short, 0x200 closest .. 0x29C farthest; copied to the zoom target each frame
        // while the camera mode is 0) and camera mode (byte: 0 normal, 1, >= 2 scripted - spells, mystic artes).
        public static int CamZoom = 0x12CEB78, CamMode = 0x12CEB7F;
    }

    // Battle actor field offsets.
    static class Actor
    {
        public const int Kind = 0x3C;       // short: 1 = character
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

        // Writes bytes into code or data, lifting page protection for the write.
        public static void Write(int rva, byte[] bytes)
        {
            IntPtr p = At(rva);
            uint old;
            VirtualProtect(p, new UIntPtr((uint)bytes.Length), PAGE_EXECUTE_READWRITE, out old);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            VirtualProtect(p, new UIntPtr((uint)bytes.Length), old, out old);
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
