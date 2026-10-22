// Which features are safe to turn on. Before the mod changes anything, each feature checks every piece of
// GameNative code it patches, hooks, or relies on for an address (a RIP-relative instruction pins the address
// it points at). A feature is used only if all of its code is exactly as expected, so the mod can run next to
// other mods that change GameNative.dll elsewhere, and a game update can't leave a feature half-applied.
using System.Collections.Generic;

namespace EterniaCoop
{
    static class Features
    {
        struct Sig
        {
            public int Rva; public byte[] Bytes;
            public Sig(int rva, params byte[] bytes) { Rva = rva; Bytes = bytes; }
        }

        // Extra players, device assignment and slot modes. Everything else needs this one.
        static readonly Sig[] CoreSigs =
        {
            // FUN_18940 input setup: player count, remap table and stick buffer sizes (NativePatches.PlayerCountImms).
            new Sig(0x18A41, 0xC7, 0x47, 0x10, 0x01, 0x00, 0x00, 0x00),      // mov dword [rdi+0x10], 1
            new Sig(0x18A68, 0x48, 0x83, 0xF9, 0x1C),                        // cmp rcx, 0x1c
            new Sig(0x18A6E, 0x48, 0x8D, 0x82, 0xA0, 0x02, 0x00, 0x00),      // lea rax, [rdx+0x2a0]
            new Sig(0x18A93, 0x48, 0x83, 0xF8, 0x1C),                        // cmp rax, 0x1c
            new Sig(0x18A99, 0xBA, 0x1C, 0x00, 0x00, 0x00),                  // mov edx, 0x1c
            new Sig(0x18AA9, 0xB8, 0x1C, 0x00, 0x00, 0x00),                  // mov eax, 0x1c
            new Sig(0x18B76, 0x48, 0x83, 0xF9, 0x02),                        // cmp rcx, 2
            new Sig(0x18B97, 0x48, 0x83, 0xF8, 0x02),                        // cmp rax, 2
            new Sig(0x18B9D, 0xBA, 0x02, 0x00, 0x00, 0x00),                  // mov edx, 2
            new Sig(0x18BAD, 0xB8, 0x02, 0x00, 0x00, 0x00),                  // mov eax, 2
            // Button-latch / Switch remap helpers: without this patch the camera spins.
            new Sig(0x57AAE, 0x3B, 0x51, 0x10),                              // cmp edx, [rcx+0x10]
            new Sig(0x57F2F, 0x3B, 0x51, 0x10),
            // Addresses: input object (FUN_5D2D0), party ids, slot modes and area (FUN_84210).
            new Sig(0x5D2FB, 0x48, 0x8B, 0x3D, 0xF6, 0x76, 0x5E, 0x00),      // mov rdi, [Rva.InputObject]
            new Sig(0x84BE3, 0x66, 0x89, 0x05, 0x26, 0x61, 0x14, 0x01),      // mov [Rva.PartyIds], ax
            new Sig(0x84BD9, 0x88, 0x0D, 0x59, 0x61, 0x14, 0x01),            // mov [Rva.SlotModes], cl
            new Sig(0x84BA4, 0x66, 0x89, 0x05, 0x19, 0x64, 0x14, 0x01),      // mov [Rva.Area], ax
        };

        // Battle-results stores that reset slots 2-4 to Auto (NativePatches).
        static readonly Sig[] KeepModesSigs =
        {
            new Sig(0x84C91, 0xC6, 0x05, 0xA1, 0x60, 0x14, 0x01, 0x00),
            new Sig(0x84D31, 0xC6, 0x05, 0x02, 0x60, 0x14, 0x01, 0x00),
            new Sig(0x84DD1, 0xC6, 0x05, 0x63, 0x5F, 0x14, 0x01, 0x00),
        };

        // Skills menu "leader only" gates (NativePatches.ApplyMenuTab).
        static readonly Sig[] MenuTabSigs =
        {
            new Sig(0x22963D, 0x74, 0x14), new Sig(0x229822, 0x74, 0x14), new Sig(0x229E96, 0x74, 0x14),
            new Sig(0x22A0D1, 0x74, 0x14), new Sig(0x22B30E, 0x74, 0x14),
            new Sig(0x2292F2, 0x41, 0x80, 0xFA, 0x01), new Sig(0x22B5EF, 0x41, 0x80, 0xFA, 0x01),
            new Sig(0x229161, 0x0F, 0x83, 0x4C, 0x02, 0x00, 0x00),
            new Sig(0x224B6A, 0x83, 0xF9, 0x01),
        };

        static readonly Sig CmdBlocksRef = new Sig(0x112204, 0x44, 0x8B, 0x0D, 0x95, 0xC9, 0x1B, 0x01);   // mov r9d, [Rva.CmdBlocks]
        static readonly Sig ActorsRef = new Sig(0xD3090, 0x4C, 0x8B, 0x35, 0x41, 0xBC, 0x1F, 0x01);       // mov r14, [Rva.ActorArray]

        // Co-op battle input (CoopBattleInput).
        static readonly Sig[] BattleSigs =
        {
            new Sig(Rva.BattleCommands, 0x48, 0x89, 0x5C, 0x24, 0x08),       // mov [rsp+8], rbx
            new Sig(Rva.BuildPad, 0x48, 0x89, 0x5C, 0x24, 0x08),
            new Sig(0x1121EF, 0x8B, 0x1D, 0x8F, 0x6F, 0x7B, 0x00),           // mov ebx, [Rva.Pad+4]
            new Sig(0xD2DAD, 0x8B, 0x05, 0xCD, 0x63, 0x7F, 0x00),            // mov eax, [Rva.Pad]
            new Sig(0xD2EE3, 0xF6, 0x05, 0x90, 0xBC, 0x1F, 0x01, 0x01),      // test byte [Rva.BattleFlags], 1
            new Sig(0x110130, 0x0F, 0xBE, 0x2D, 0x55, 0xEA, 0x1B, 0x01),     // movsx ebp, byte [Rva.MenuOpener] (FUN_110120)
            CmdBlocksRef, ActorsRef,
        };

        // Per-player rumble (RumbleRouter).
        static readonly Sig[] RumbleSigs =
        {
            new Sig(Rva.ApplyHit, 0x40, 0x53, 0x56, 0x57, 0x41, 0x56),       // push rbx/rsi/rdi/r14
            new Sig(0x8E32A, 0x80, 0x3D, 0x49, 0x08, 0x24, 0x01, 0x00),      // cmp byte [Rva.BattleFlags], 0 (paused: no hit)
            // The rumble rule at the end of FUN_8E320 that RumbleRouter extends to blocks 1-3:
            new Sig(0x8E895, 0x80, 0x7F, 0x56, 0x00),                        // cmp byte [attacker+0x56], 0 (block 0 only)
            new Sig(0x8E89B, 0x80, 0x7F, 0x51, 0x00),                        // cmp byte [attacker+0x51], 0 (party)
            new Sig(0x8E8A1, 0x48, 0x8B, 0x05, 0x38, 0x61, 0x5B, 0x00),      // mov rax, [Rva.GameOptions]
            new Sig(0x8E8A8, 0xC7, 0x05, 0xC6, 0x45, 0x7D, 0x00, 0x9A, 0x99, 0x19, 0x3F),  // strength = 0.6f
            new Sig(0x8E8B2, 0x80, 0x78, 0x21, 0x00),                        // cmp byte [options+0x21], 0 (rumble option)
            CmdBlocksRef, ActorsRef,
        };

        public static bool Core, KeepModes, MenuTab, CoopBattle, Rumble;
        static bool checkedOnce;

        // Call once GameNative is loaded and before anything is patched.
        public static void Check()
        {
            if (checkedOnce || Native.Base == 0) return;
            checkedOnce = true;
            Core = Verify("Co-op players", CoreSigs);
            KeepModes = Core && Verify("Keep modes after battle", KeepModesSigs);
            MenuTab = Core && Verify("Skills menu tab", MenuTabSigs);
            CoopBattle = Core && Verify("Co-op battle input", BattleSigs);
            Rumble = Core && Verify("Per-player rumble", RumbleSigs);
            Compatibility.Report(Core, Missing());
        }

        // Names of the features that were turned off because their game code is different.
        static List<string> Missing()
        {
            var off = new List<string>();
            if (!KeepModes) off.Add("keep modes");
            if (!MenuTab) off.Add("menu tab");
            if (!CoopBattle) off.Add("battle input");
            if (!Rumble) off.Add("rumble");
            return off;
        }

        static bool Verify(string feature, Sig[] sigs)
        {
            bool ok = true;
            foreach (var s in sigs)
                if (!Native.Matches(s.Rva, s.Bytes))
                {
                    Plugin.Log.LogWarning(string.Format("{0}: game code at GameNative+0x{1:X} is different", feature, s.Rva));
                    ok = false;
                }
            if (!ok) Plugin.Log.LogWarning(feature + ": off (the game was updated or another mod changed this code).");
            return ok;
        }
    }
}
