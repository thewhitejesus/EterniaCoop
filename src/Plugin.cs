// Eternia Co-op - local 4-player co-op for Tales of Eternia Remastered: controllers 2-4 drive party slots 2-4
// in battle, the way the PS1 version's multitap mode did.
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EterniaCoop
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "eterniacoop", Name = "Eternia Co-op", Version = "1.1.3";

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
#endif
            Compatibility.Check(Settings.StrictVersionCheck.Value);
            Players.LoadManual(Settings.DeviceAssignments.Value);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo(Name + " " + Version + (Compatibility.Ok ? "" : " (inactive: " + Compatibility.Problem + ")") +
                ". Players: " + Settings.PlayerCount.Value + ", keyboard -> player " + (Settings.KeyboardPlayer.Value + 1) + ".");
            string wine = Native.WineVersion();
            if (wine != null) Log.LogInfo("Running under Wine/Proton " + wine + " (Linux or Steam Deck).");
        }

        void Update()
        {
            RumbleRouter.Update(Settings.RumbleSeconds.Value);
            if (CoopActive && Players.InputManager != null && Settings.AutoAssignControllers.Value) ControllerManager.Update();
            // Battle menu opened or closed: hand every device to player 1 (or back) right away, not on the next tick.
            bool share = CoopActive && Settings.AutoAssignControllers.Value && Battle.MenuOpen();
            if (share != Players.ShareWithPlayer1 && Players.InputManager != null)
            {
                Players.ShareWithPlayer1 = share;
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
            if (areaKnown && System.Runtime.InteropServices.Marshal.ReadInt32(Native.At(Rva.Area)) != 0) return;
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
            float y = underDropSteal ? 28 : 10;
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, w, 25), text, watermarkStyle);
            for (int i = 0; i < extra.Count; i++) GUI.Label(new Rect(x, y + 18 * (i + 1), w, 25), extra[i], watermarkStyle);
        }
    }
}
