// Access to GameNative.dll, the game's C++ engine: module base, the addresses this mod uses, and safe code patching.
// Built with the C# 5 compiler that ships with Windows, so: no $"", no ?., no nameof.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    // Every GameNative.dll address the mod touches, as RVAs (offsets from the module base) for the version
    // Compatibility.KnownSha256 describes. "FUN_xxxxxx" names are Ghidra's names for the function at that RVA.
    static class Rva
    {
        // Input object (C++ side of the game's input). Global pointer; fields: +0x10 player count,
        // +0x18 remap table (0x1C x 24-byte entries per player), +0x30 buttons (0xAE x 20 per player),
        // +0x48 sticks (2 x 20 per player).
        public const int InputObject = 0x6449F8;

        // Emulated PSP pad (held/pressed/released words, repeat timers, per-button bools), filled from player 1.
        public const int Pad = 0x8C9180, PadSize = 0x144;

        // Battle: one 0x20-byte command block per party slot; each party actor reads its own via actor+0xF0.
        public const int CmdBlocks = 0x12CEBA0, CmdSize = 0x20;
        public const int ActorArray = 0x12CECD8, ActorSize = 0x248, ActorCount = 13;

        // Party (menu/save side): member ids (4 x short) and control mode per slot (byte: 0 Auto, 1 Semi-Auto).
        public const int PartyIds = 0x11CAD10, SlotModes = 0x11CAD38;

        // Current area (2 shorts, e.g. 06 00 6F 00 in the field); all zero on the title screen.
        public const int Area = 0x11CAFC4;

        // Functions we call or hook.
        public const int BattleCommands = 0x1121E0;   // FUN_1121E0(uint* padHeld): pad -> slot 0's command block
        public const int BuildPad = 0x19DBC0;         // FUN_19DBC0(pad, rate, flag): input object (player 1) -> PSP pad
        public const int ApplyHit = 0x8E320;          // FUN_8E320(type, flags, hit, attacker, victim)

        // Rumble request read by the per-frame system loop (FUN_4DF60) and played on player 1.
        public const int RumbleFrames = 0x862E72, RumbleStrength = 0x862E78;
    }

    // Battle actor field offsets.
    static class Actor
    {
        public const int Kind = 0x3C;       // short: 1 = character
        public const int Side = 0x51;       // byte: 0 = party, 1 = enemy
        public const int Inactive = 0x56;   // byte: non-zero when the game skips rumble for this actor
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
        const uint PAGE_EXECUTE_READWRITE = 0x40;

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

        public static IntPtr Alloc(int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            Clear(p, size);
            return p;
        }

        public static void Clear(IntPtr p, int size)
        {
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
        }

        public static void Copy(IntPtr from, IntPtr to, int size)
        {
            var tmp = new byte[size];
            Marshal.Copy(from, tmp, 0, size);
            Marshal.Copy(tmp, 0, to, size);
        }

        public static float ReadFloat(IntPtr p)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(p)), 0);
        }
    }
}
