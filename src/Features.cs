// Which features are safe to turn on. Before the mod changes anything, Sites finds every GameNative spot by its code
// signature (on the supported build: at the known addresses; after a game update: wherever the code moved). A feature
// is used only if every spot it patches, hooks or reads an address from was found exactly, so the mod can run next to
// other mods that change GameNative.dll and a game update can't leave a feature half-applied.
using System.Collections.Generic;

namespace EterniaCoop
{
    static class Features
    {
        // The sites each feature needs (names in Sites.Generated.cs).
        static readonly string[] CoreSites =
        {
            "Count", "RemapCmp1", "RemapLea", "RemapCmp2", "RemapMovEdx", "RemapMovEax",
            "StickCmp1", "StickCmp2", "StickMovEdx", "StickMovEax", "LatchGuard", "RemapGuard",
            "InputObject", "PartyIds", "SlotModes", "Area",
        };
        static readonly string[] KeepModesSites = { "ResetSlot2", "ResetSlot3", "ResetSlot4" };
        static readonly string[] MenuTabSites = { "TabJe", "TabCursor1", "TabCursor2", "TabConfirm", "TabDraw" };
        static readonly string[] BattleSites =
            { "BattleCommands", "BuildPad", "Pad", "PadHeld", "BattleFlags", "MenuOpener", "CmdBlocks", "ActorArray" };
        static readonly string[] RumbleSites =
        {
            "ApplyHit", "HitPaused", "RumbleAtkBlock", "RumbleAtkSide", "GameOptions", "RumbleStrength", "RumbleOption",
            "CmdBlocks", "ActorArray",
        };

        public static bool Core, KeepModes, MenuTab, CoopBattle, Rumble;
        static bool checkedOnce;

        // Call once GameNative is loaded and before anything is patched.
        public static void Check()
        {
            if (checkedOnce || Native.Base == 0) return;
            checkedOnce = true;
            Sites.Resolve(Compatibility.Hash, Compatibility.Exact);

            Core = Verify("Co-op players", CoreSites);
            KeepModes = Core && Verify("Keep modes after battle", KeepModesSites);
            MenuTab = Core && Verify("Skills menu tab", MenuTabSites);
            // Two instructions reach the PSP pad (one at +4): they must agree.
            CoopBattle = Core && Verify("Co-op battle input", BattleSites) &&
                Agree("Co-op battle input", "Pad", "PadHeld");
            Rumble = Core && Verify("Per-player rumble", RumbleSites) &&
                (!Sites.Resolved("BattleFlags") || Agree("Per-player rumble", "BattleFlags", "HitPaused"));
            ApplyAddresses();
            Compatibility.Report(Core, Missing(), Sites.Moved);
        }

        // Rva's addresses become the ones found (unchanged on the supported build).
        static void ApplyAddresses()
        {
            if (Sites.Resolved("InputObject")) Rva.InputObject = Sites.Get("InputObject").Target;
            if (Sites.Resolved("PartyIds")) Rva.PartyIds = Sites.Get("PartyIds").Target;
            if (Sites.Resolved("SlotModes")) Rva.SlotModes = Sites.Get("SlotModes").Target;
            if (Sites.Resolved("Area")) Rva.Area = Sites.Get("Area").Target;
            if (Sites.Resolved("Pad")) Rva.Pad = Sites.Get("Pad").Target;
            if (Sites.Resolved("CmdBlocks")) Rva.CmdBlocks = Sites.Get("CmdBlocks").Target;
            if (Sites.Resolved("ActorArray")) Rva.ActorArray = Sites.Get("ActorArray").Target;
            if (Sites.Resolved("MenuOpener")) Rva.MenuOpener = Sites.Get("MenuOpener").Target;
            if (Sites.Resolved("BattleFlags")) Rva.BattleFlags = Sites.Get("BattleFlags").Target;
            else if (Sites.Resolved("HitPaused")) Rva.BattleFlags = Sites.Get("HitPaused").Target;
            if (Sites.Resolved("GameOptions")) Rva.GameOptions = Sites.Get("GameOptions").Target;
            if (Sites.Resolved("BattleCommands")) Rva.BattleCommands = Sites.Rva("BattleCommands", 0);
            if (Sites.Resolved("BuildPad")) Rva.BuildPad = Sites.Rva("BuildPad", 0);
            if (Sites.Resolved("ApplyHit")) Rva.ApplyHit = Sites.Rva("ApplyHit", 0);
        }

        // Names of the features that were turned off because their game code wasn't found.
        static List<string> Missing()
        {
            var off = new List<string>();
            if (!KeepModes) off.Add("keep modes");
            if (!MenuTab) off.Add("menu tab");
            if (!CoopBattle) off.Add("battle input");
            if (!Rumble) off.Add("rumble");
            return off;
        }

        static bool Verify(string feature, string[] sites)
        {
            var missing = new List<string>();
            foreach (var s in sites) if (!Sites.Resolved(s)) missing.Add(s);
            if (missing.Count == 0) return true;
            Plugin.Log.LogWarning(feature + ": off - game code not found for " + string.Join(", ", missing.ToArray()) +
                " (the game was updated or another mod changed this code).");
            return false;
        }

        static bool Agree(string feature, string a, string b)
        {
            if (Sites.Get(a).Target == Sites.Get(b).Target) return true;
            Plugin.Log.LogWarning(string.Format("{0}: off - {1} and {2} point at different addresses (0x{3:X} / 0x{4:X}).",
                feature, a, b, Sites.Get(a).Target, Sites.Get(b).Target));
            return false;
        }
    }
}
