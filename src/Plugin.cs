// Eternia Co-op - local 4-player co-op for Tales of Eternia Remastered: controllers 2-4 drive party slots 2-4
// in battle, the way the PS1 version's multitap mode did.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EterniaCoop
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "eterniacoop", Name = "Eternia Co-op", Version = "1.7.0";

        internal static ManualLogSource Log;
        internal static volatile bool CoopActive;   // set once the native patches and hooks are in
        float nextAssign;
        GUIStyle watermarkStyle;

        void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
#if COOP_DEBUG
            ResearchTools.Bind(Config);
            RosterUnlock.Bind(Config);
            BotPlayers.Bind(Config);
#endif
            Compatibility.Check(Settings.StrictVersionCheck.Value);
            Players.LoadManual(Settings.DeviceAssignments.Value);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo(Name + " " + Version + (Compatibility.Ok ? "" : " (inactive: " + Compatibility.Problem + ")") +
                ". Players: " + Settings.PlayerCount.Value + ", keyboard -> " + (Settings.KeyboardPlayer.Value < 0 ? "off" : "player " + (Settings.KeyboardPlayer.Value + 1)) + ".");
            string wine = Native.WineVersion();
            if (wine != null) Log.LogInfo("Running under Wine/Proton " + wine + " (Linux or Steam Deck).");
        }

        // The battle hooks (command builder, background, hits) and the menu / battle-results patches go in once a save is
        // loaded, not when the game starts: Six Person Battle Expansion checks those functions' bytes at startup and
        // switches itself off if they differ. In the field no battle or Skills menu code runs, so nothing is executing
        // the code being changed. Only the input patches must be in before the game sets up its input (InputPatches).
        static bool battleHooks;

        static void InstallBattleHooks()
        {
            if (battleHooks || !CoopActive || Native.Base == 0 || Marshal.ReadInt32(Native.At(Rva.Area)) == 0) return;
            battleHooks = true;
            NativePatches.ApplyLater();
            if (Settings.CoopBattle.Value && Features.CoopBattle) CoopBattleInput.Install();
            if (Settings.PerPlayerRumble.Value && Features.Rumble) RumbleRouter.Install();
        }

        void Update()
        {
            InstallBattleHooks();
            SixPersonCompat.Update();
            RumbleRouter.Update(Settings.RumbleSeconds.Value);
            if (CoopActive) WaterCompat.Update(CoopCamera.HumanPlayers >= 2);
            Radar.Update();
            if (CoopActive && Players.InputManager != null && Settings.AutoAssignControllers.Value) ControllerManager.Update();
            // Battle menu opened or closed: hand every device to player 1 (or back) right away, not on the next tick.
            bool share = CoopActive && Settings.AutoAssignControllers.Value && Battle.MenuOpen();
            // Field control: outside battle, player 1's controls go to the chosen player; back the moment a battle starts.
            int field = CoopActive && Settings.AutoAssignControllers.Value && Features.CoopBattle && Native.Base != 0 &&
                Battle.Actors() == IntPtr.Zero ? Mathf.Clamp(Settings.FieldControl.Value, 0, 3) : 0;
            // The battle menu belongs to whoever opened it (block + 1 in Rva.MenuOpener; 0 = paused with Start, no owner).
            int owner = share ? Marshal.ReadByte(Native.At(Rva.MenuOpener)) - 1 : -1;
            if ((share != Players.ShareWithPlayer1 || field != Players.FieldPlayer || owner != Players.MenuOwner) &&
                Players.InputManager != null)
            {
                Players.ShareWithPlayer1 = share;
                Players.MenuOwner = owner;
                Players.FieldPlayer = field;
                Players.AutoAssign(Settings.KeyboardPlayer.Value);
            }
            if (CoopActive && Players.InputManager != null && Time.unscaledTime >= nextAssign)
            {
                nextAssign = Time.unscaledTime + 1f;
                if (Settings.AutoAssignControllers.Value) Players.AutoAssign(Settings.KeyboardPlayer.Value);
                else Players.AssumeAllPresent();
                SlotModes.Update(Settings.JoinMode.Value);
            }
#if COOP_DEBUG
            ResearchTools.Update(Config);
            RosterUnlock.Update();
            BotPlayers.Update();
            CodeDiff.Update();
            SixPersonCompat.Report();
#endif
        }

        // Small corner label so players can see the mod is loaded (and why it isn't active, if it isn't).
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            ControllerManager.Draw();
            if (!Settings.ShowWatermark.Value) return;
            // Main menu only: hide once a map is loaded (the area id is 0 on the title screen).
            // Rva.Area is only known to be right on the supported build or once Features has verified it.
            bool areaKnown = Native.Base != 0 && (Compatibility.Exact || Features.Core);
            if (areaKnown && Marshal.ReadInt32(Native.At(Rva.Area)) != 0) return;
            string name = Name + " " + Version;
#if COOP_DEBUG
            name += " DEBUG";
#endif
            if (watermarkStyle == null)
                watermarkStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13, wordWrap = false };

            string text = "<b>[" + name + "]</b>";
            if (Compatibility.Problem != null)
                text += " <color=" + (Compatibility.Ok ? "#FFCC44" : "#FF4444") + ">" + Compatibility.Problem + "</color>";
            if (CoopActive && Settings.AutoAssignControllers.Value)
            {
                string pad = ControllerManager.ShortcutName();
                text += (Compatibility.Problem != null ? " |" : "") + " Controllers: <color=#00FF00>" + ControllerManager.OpenKey() + "</color>" +
                    (pad == "" ? "" : " / <color=#00FF00>" + pad + "</color>");
            }
            var others = OtherMods.Names(Time.unscaledTime);
            var extra = new List<string>();
            if (others.Count > 0) extra.Add("Also running: " + string.Join(", ", others.ToArray()));
            if (CoopActive && ControllerManager.ConflictShort() != "") extra.Add(ControllerManager.ConflictShort());

            // Same look as 100% Drop & Steal Rate's status line (default font, size 13, white, name in bold). That mod
            // draws at (width - 520, 10), left-aligned; when it's there we line up underneath it in the same column,
            // otherwise we take the top line ourselves, right-aligned to the same right edge.
            bool underDropSteal = ControllerManager.KeyTakenBy() != null;
            watermarkStyle.alignment = underDropSteal ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            float x = underDropSteal ? Screen.width - 520 : Screen.width - 1010, w = underDropSteal ? 510 : 1000;

            string dropSteal = OtherMods.DropStealWarning();
            if (dropSteal != null) AddWrapped(extra, dropSteal, w, "#FF6644");
            var unbridged = OtherMods.UnbridgedBepInEx6();
            if (unbridged.Count > 0)
            {
                AddWrapped(extra, "Not loaded (BepInEx 6 mods): " + string.Join(", ", unbridged.ToArray()), w, "#FFCC44");
                AddWrapped(extra, "Install the BepInEx 6 Bridge to load them: it comes with Eternia Co-op, or get it from " +
                    "its Nexus page (optional files).", w, "#FFCC44");
            }
            float y = underDropSteal ? 28 : 10;
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, w, 25), text, watermarkStyle);
            for (int i = 0; i < extra.Count; i++) GUI.Label(new Rect(x, y + 18 * (i + 1), w, 25), extra[i], watermarkStyle);
        }

        // Adds text as label lines no wider than width (word wrap), each in the given colour.
        void AddWrapped(List<string> lines, string text, float width, string color)
        {
            string line = "";
            foreach (string word in text.Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && watermarkStyle.CalcSize(new GUIContent(next)).x > width)
                {
                    lines.Add("<color=" + color + ">" + line + "</color>");
                    next = word;
                }
                line = next;
            }
            if (line.Length > 0) lines.Add("<color=" + color + ">" + line + "</color>");
        }
    }
}
