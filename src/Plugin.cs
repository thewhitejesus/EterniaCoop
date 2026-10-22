// Eternia Co-op - local 4-player co-op for Tales of Eternia Remastered: controllers 2-4 drive party slots 2-4
// in battle, the way the PS1 version's multitap mode did.
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EterniaCoop
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "eterniacoop", Name = "Eternia Co-op", Version = "1.1.1";

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
        }

        void Update()
        {
            RumbleRouter.Update(Settings.RumbleSeconds.Value);
            if (CoopActive && Players.InputManager != null && Settings.AutoAssignControllers.Value) ControllerManager.Update();
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
            if (ControllerManager.KeyTakenBy() != null) { DrawUnderDropSteal(name); return; }

            if (watermarkStyle == null)
                watermarkStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold, wordWrap = false };
            watermarkStyle.fontSize = Mathf.Max(12, Screen.height / 60);

            string text = name;
            if (Compatibility.Problem != null) text += " - " + Compatibility.Problem;
            if (CoopActive && Settings.AutoAssignControllers.Value) text += "  ·  " + ControllerManager.OpenKey() + ": controllers";
            // Margin in proportion to the window: the visible area can be a little narrower than Screen.width.
            float margin = Screen.width * 0.03f;
            float width = Screen.width - 2 * margin;   // status text can be long ("partly on (off: ...)")
            var r = new Rect(margin, Screen.height * 0.012f, width, watermarkStyle.fontSize * 2);
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, watermarkStyle);
            GUI.color = Compatibility.Ok ? new Color(1, 1, 1, 0.55f) : new Color(1, 0.55f, 0.45f, 0.85f);
            GUI.Label(r, text, watermarkStyle);
            GUI.color = Color.white;
        }

        // 100% Drop & Steal Rate draws "[ToE Mod v1.0 by azan22] Drop: ... | [F1: Menu]" at (width - 520, 10) in the
        // default font at size 13, white, name in bold. Draw our line the same way, directly underneath, so the
        // two read as one block.
        GUIStyle stackedStyle;

        void DrawUnderDropSteal(string name)
        {
            if (stackedStyle == null)
                stackedStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13, wordWrap = false };
            string text = "<b>[" + name + "]</b>";
            if (Compatibility.Problem != null)
                text += " <color=" + (Compatibility.Ok ? "#FFCC44" : "#FF4444") + ">" + Compatibility.Problem + "</color>";
            if (CoopActive && Settings.AutoAssignControllers.Value)
                text += (Compatibility.Problem != null ? " |" : "") + " Controllers: <color=#00FF00>" + ControllerManager.OpenKey() + "</color> (F1 is Drop & Steal's)";
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width - 520, 28, 510, 25), text, stackedStyle);
        }
    }
}
