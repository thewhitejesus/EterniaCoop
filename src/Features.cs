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
            CmdBlocksRef, ActorsRef,
        };

        // Per-player rumble (RumbleRouter).
        static readonly Sig[] RumbleSigs =
        {
            new Sig(Rva.ApplyHit, 0x40, 0x53, 0x56, 0x57, 0x41, 0x56),       // push rbx/rsi/rdi/r14
            new Sig(0x4E60C, 0x0F, 0xB7, 0x15, 0x5F, 0x48, 0x81, 0x00),      // movzx edx, [Rva.RumbleFrames]
            new Sig(0x4E618, 0xF3, 0x0F, 0x10, 0x0D, 0x58, 0x48, 0x81, 0x00),  // movss xmm1, [Rva.RumbleStrength]
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
