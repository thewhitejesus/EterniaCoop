// Code patches inside GameNative.dll, at the sites Sites found (Features.Check has resolved every site of a feature
// before any of it is applied). Each patch also re-checks the instruction bytes it changes and is skipped (with a log
// line) if they differ. Parts that move between builds (jump distances, RIP-relative addresses) were already checked
// by the site's signature and are left as they are.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class NativePatches
    {
        static bool applied;

        // Input setup (FUN_18940) hardcodes playerCount = 1 and sizes two per-player buffers for one player.
        // Its poll loop (FUN_57768) already runs GetInputInfo/GetStickInfo for every player < playerCount.
        // Each entry: site, immediate's offset in the instruction, size in bytes, original value, value per player,
        // belongs to the remap table.
        struct Imm
        {
            public string Site; public int At, Size, Orig, PerPlayer; public bool Remap;
            public Imm(string site, int at, int size, int orig, int perPlayer, bool remap)
            { Site = site; At = at; Size = size; Orig = orig; PerPlayer = perPlayer; Remap = remap; }
            public int Rva { get { return Sites.Rva(Site, 0) + At; } }
        }

        static readonly Imm[] PlayerCountImms =
        {
            new Imm("Count", 3, 4, 1, 1, false),               // mov dword [rdi+0x10], 1       -> player count
            new Imm("StickCmp1", 3, 1, 2, 2, false),           // cmp rcx, 2                     stick vector, 2 per player
            new Imm("StickCmp2", 3, 1, 2, 2, false),           // cmp rax, 2
            new Imm("StickMovEdx", 1, 4, 2, 2, false),         // mov edx, 2
            new Imm("StickMovEax", 1, 4, 2, 2, false),         // mov eax, 2
            new Imm("RemapCmp1", 3, 1, 0x1C, 0x1C, true),      // cmp rcx, 0x1c                  remap table, 0x1c per player
            new Imm("RemapCmp2", 3, 1, 0x1C, 0x1C, true),      // cmp rax, 0x1c
            new Imm("RemapMovEdx", 1, 4, 0x1C, 0x1C, true),    // mov edx, 0x1c
            new Imm("RemapMovEax", 1, 4, 0x1C, 0x1C, true),    // mov eax, 0x1c
            new Imm("RemapLea", 3, 4, 0x2A0, 0x2A0, true),     // lea rax, [rdx+0x2a0]           (0x1c entries * 24 bytes)
        };

        static int ReadImm(Imm i) { return i.Size == 1 ? Marshal.ReadByte(Native.At(i.Rva)) : Marshal.ReadInt32(Native.At(i.Rva)); }

        // Called from UgxInputManager.Initialize, which GameNative's input setup calls before writing its player count.
        public static void Apply(int players)
        {
            if (applied || Native.Base == 0 || !Features.Core) return;
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
                foreach (string site in new[] { "LatchGuard", "RemapGuard" })
                    Native.Patch(Sites.Rva(site, 0), new byte[] { 0x3B, 0x51, 0x10 }, new byte[] { 0x83, 0xFA, 0x01 });

            // The battle-results code (FUN_84210) writes 0 (Auto) into party slots 2-4's control mode after every
            // battle: "mov byte [SlotModes+1/2/3], 0" (C6 05 disp32 00). Each 7-byte store becomes a 7-byte NOP.
            if (Settings.KeepSlotModesAfterBattle.Value && Features.KeepModes)
                foreach (string site in new[] { "ResetSlot2", "ResetSlot3", "ResetSlot4" })
                {
                    int rva = Sites.Rva(site, 0);
                    if (Native.Matches(rva, new byte[] { 0xC6, 0x05 }) && Native.Matches(rva + 6, new byte[] { 0x00 }))
                        Native.Write(rva, Nop7);
                    else Plugin.Log.LogError(string.Format("Unexpected code at GameNative+0x{0:X}, left unpatched", rva));
                }

            if (Settings.MenuModeTabForAll.Value && Features.MenuTab) ApplyMenuTab();
            Plugin.Log.LogMessage("GameNative input now handles " + players + " players.");
        }

        static readonly byte[] Nop7 = { 0x0F, 0x1F, 0x80, 0x00, 0x00, 0x00, 0x00 };

        // Skills menu: the Semi-Auto/Auto tab is gated on "selected character == 0" (r10b / ecx). The mode it
        // toggles is already mode[selected], so lifting the gates gives every party member the tab.
        static void ApplyMenuTab()
        {
            int ok = 0, total = 0;
            // Input handler FUN_228AD0: show the tab's elements (je -> jmp, keeping the jump distance). Five copies.
            foreach (int rva in Sites.Get("TabJe").Rvas)
            {
                total++;
                if (Native.Patch(rva, new byte[] { 0x74 }, new byte[] { 0xEB })) ok++;
            }
            // Cursor can move up into the tab (cmp r10b,1 -> cmp r10b,4).
            foreach (string site in new[] { "TabCursor1", "TabCursor2" })
            {
                total++;
                if (Native.Patch(Sites.Rva(site, 0), new byte[] { 0x41, 0x80, 0xFA, 0x01 }, new byte[] { 0x41, 0x80, 0xFA, 0x04 })) ok++;
            }
            // Confirming on the tab (jae rel32 "leader only" -> 6-byte NOP).
            total++;
            int confirm = Sites.Rva("TabConfirm", 0);
            if (Native.Matches(confirm, new byte[] { 0x0F, 0x83 }))
            {
                Native.Write(confirm, new byte[] { 0x66, 0x0F, 0x1F, 0x44, 0x00, 0x00 });
                ok++;
            }
            // Draw code FUN_224920: "if (selected < 1) draw Control Mode tab" -> selected < 4.
            total++;
            if (Native.Patch(Sites.Rva("TabDraw", 0), new byte[] { 0x83, 0xF9, 0x01 }, new byte[] { 0x83, 0xF9, 0x04 })) ok++;
            Plugin.Log.LogMessage("Semi-Auto/Auto menu tab enabled for all party members (" + ok + "/" + total + " patches).");
        }
    }
}
