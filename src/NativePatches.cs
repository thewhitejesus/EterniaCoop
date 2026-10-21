// Code patches inside GameNative.dll. Every patch checks the exact original bytes first and is skipped
// (with a log line) if they differ, so a game update can't be half-patched.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class NativePatches
    {
        static bool applied;

        // Input setup (FUN_18940) hardcodes playerCount = 1 and sizes two per-player buffers for one player.
        // Its poll loop (FUN_57768) already runs GetInputInfo/GetStickInfo for every player < playerCount.
        // Each entry: rva, size in bytes, original value, value per player, belongs to the remap table.
        struct Imm
        {
            public int Rva, Size, Orig, PerPlayer; public bool Remap;
            public Imm(int rva, int size, int orig, int perPlayer, bool remap) { Rva = rva; Size = size; Orig = orig; PerPlayer = perPlayer; Remap = remap; }
        }

        static readonly Imm[] PlayerCountImms =
        {
            new Imm(0x18A44, 4, 1, 1, false),            // mov dword [rdi+0x10], 1       -> player count
            new Imm(0x18B79, 1, 2, 2, false),            // cmp rcx, 2                     stick vector, 2 per player
            new Imm(0x18B9A, 1, 2, 2, false),            // cmp rax, 2
            new Imm(0x18B9E, 4, 2, 2, false),            // mov edx, 2
            new Imm(0x18BAE, 4, 2, 2, false),            // mov eax, 2
            new Imm(0x18A6B, 1, 0x1C, 0x1C, true),       // cmp rcx, 0x1c                  remap table, 0x1c per player
            new Imm(0x18A96, 1, 0x1C, 0x1C, true),       // cmp rax, 0x1c
            new Imm(0x18A9A, 4, 0x1C, 0x1C, true),       // mov edx, 0x1c
            new Imm(0x18AAA, 4, 0x1C, 0x1C, true),       // mov eax, 0x1c
            new Imm(0x18A71, 4, 0x2A0, 0x2A0, true),     // lea rax, [rdx+0x2a0]           (0x1c entries * 24 bytes)
        };

        static int ReadImm(Imm i) { return i.Size == 1 ? Marshal.ReadByte(Native.At(i.Rva)) : Marshal.ReadInt32(Native.At(i.Rva)); }

        // Called from UgxInputManager.Initialize, which GameNative's input setup calls before writing its player count.
        public static void Apply(int players)
        {
            if (applied || Native.Base == 0) return;
            applied = true;

            foreach (var i in PlayerCountImms)
                if (ReadImm(i) != i.Orig)
                {
                    Plugin.Log.LogError(string.Format("Unexpected value at GameNative+0x{0:X}: player count not patched", i.Rva));
                    return;
                }
            foreach (var i in PlayerCountImms)
            {
                if (i.Remap && !Settings.ScaleRemapBuffer.Value) continue;
                int v = i.PerPlayer * players;
                Native.Write(i.Rva, i.Size == 1 ? new[] { (byte)v } : BitConverter.GetBytes(v));
            }

            // The per-player helpers the poll loop calls (FUN_57AA0 button latches, FUN_57F20 Switch remap) share
            // global latch flags; later players cleared player 1's and left the camera spinning. Their guard
            // "cmp edx, [rcx+0x10]" (player < count) becomes "cmp edx, 1" so they run for player 1 only.
            if (Settings.HelpersPlayer0Only.Value)
                foreach (int rva in new[] { 0x57AAE, 0x57F2F })
                    Native.Patch(rva, new byte[] { 0x3B, 0x51, 0x10 }, new byte[] { 0x83, 0xFA, 0x01 });

            // The battle-results code (FUN_84210) writes 0 (Auto) into party slots 2-4's control mode after every
            // battle: "mov byte [0x11CAD39/3A/3B], 0". Each 7-byte store becomes a 7-byte NOP.
            if (Settings.KeepSlotModesAfterBattle.Value)
            {
                Native.Patch(0x84C91, new byte[] { 0xC6, 0x05, 0xA1, 0x60, 0x14, 0x01, 0x00 }, Nop7);
                Native.Patch(0x84D31, new byte[] { 0xC6, 0x05, 0x02, 0x60, 0x14, 0x01, 0x00 }, Nop7);
                Native.Patch(0x84DD1, new byte[] { 0xC6, 0x05, 0x63, 0x5F, 0x14, 0x01, 0x00 }, Nop7);
            }

            if (Settings.MenuModeTabForAll.Value) ApplyMenuTab();
            Plugin.Log.LogMessage("GameNative input now handles " + players + " players.");
        }

        static readonly byte[] Nop7 = { 0x0F, 0x1F, 0x80, 0x00, 0x00, 0x00, 0x00 };

        // Skills menu: the Semi-Auto/Auto tab is gated on "selected character == 0" (r10b / ecx). The mode it
        // toggles is already mode[selected], so lifting the gates gives every party member the tab.
        static void ApplyMenuTab()
        {
            int ok = 0;
            // Input handler FUN_228AD0: show the tab's elements (je -> jmp).
            foreach (int rva in new[] { 0x22963D, 0x229822, 0x229E96, 0x22A0D1, 0x22B30E })
                if (Native.Patch(rva, new byte[] { 0x74, 0x14 }, new byte[] { 0xEB, 0x14 })) ok++;
            // Cursor can move up into the tab (cmp r10b,1 -> cmp r10b,4).
            foreach (int rva in new[] { 0x2292F2, 0x22B5EF })
                if (Native.Patch(rva, new byte[] { 0x41, 0x80, 0xFA, 0x01 }, new byte[] { 0x41, 0x80, 0xFA, 0x04 })) ok++;
            // Confirming on the tab (jae "leader only" -> 6-byte NOP).
            if (Native.Patch(0x229161, new byte[] { 0x0F, 0x83, 0x4C, 0x02, 0x00, 0x00 }, new byte[] { 0x66, 0x0F, 0x1F, 0x44, 0x00, 0x00 })) ok++;
            // Draw code FUN_224920: "if (selected < 1) draw Control Mode tab" -> selected < 4.
            if (Native.Patch(0x224B6A, new byte[] { 0x83, 0xF9, 0x01 }, new byte[] { 0x83, 0xF9, 0x04 })) ok++;
            Plugin.Log.LogMessage("Semi-Auto/Auto menu tab enabled for all party members (" + ok + "/9 patches).");
        }
    }
}
