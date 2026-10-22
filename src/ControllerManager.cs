// In-game controller manager: choose which device controls which player.
//   Open/close:  Settings.ManagerKey (F1; F8 when another loaded mod uses F1) on the keyboard, or hold Settings.ManagerControllerShortcut (L3+R3) on any
//                controller (so Parsec guests can open it); Esc also closes.
//   Controllers: LB/RB or D-pad left/right move that controller to the previous/next player. Back/Select resets.
//   Keyboard:    Up/Down pick a row, Left/Right change its player, R resets everything to automatic.
// The game gets no input while it's open. Choices are saved per device in Settings.DeviceAssignments.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace EterniaCoop
{
    static class ControllerManager
    {
        public static bool Open { get; private set; }

        static int selected;
        static string message;
        static float messageUntil, comboSince = -1;
        static readonly Dictionary<int, float> lastActive = new Dictionary<int, float>();   // deviceId -> time
        static GUIStyle title, row, hint;
        static Texture2D white;
        static readonly Color[] PlayerColors =
            { new Color(0.95f, 0.35f, 0.35f), new Color(0.40f, 0.60f, 1f), new Color(0.40f, 0.85f, 0.45f), new Color(1f, 0.85f, 0.30f) };

        // Our own press detection ("held now, not held last time we looked"). wasPressedThisFrame depends on when
        // the Input System processes events relative to our Update, which isn't reliable in this game.
        static readonly Dictionary<string, bool> held = new Dictionary<string, bool>();
#if COOP_DEBUG
        static float nextDiag;
#endif

        static bool Pressed(string key, bool isDown)
        {
            bool was;
            held.TryGetValue(key, out was);
            held[key] = isDown;
            return isDown && !was;
        }

        static bool KeyPressed(Keyboard kb, Key k) { return kb != null && Pressed("kb" + k, kb[k].isPressed); }

        static bool PadPressed(Gamepad p, string name, bool isDown) { return Pressed(p.deviceId + name, isDown); }

        // LB, RB and Back act when released without any other shortcut button pressed during the hold, so the
        // shortcuts made of them (LB+RB+Back, Back+Start) open and close the manager without also moving the
        // controller or resetting everything.
        static readonly Dictionary<string, bool> tapClean = new Dictionary<string, bool>();

        static bool Tapped(Gamepad p, string name, bool down, bool others)
        {
            string key = p.deviceId + name;
            bool wasDown, clean;
            held.TryGetValue(key, out wasDown);
            held[key] = down;
            if (down && !wasDown) tapClean[key] = !others;
            else if (down && others) tapClean[key] = false;
            return !down && wasDown && tapClean.TryGetValue(key, out clean) && clean;
        }

        static bool ShortcutButtonsBut(Gamepad p, ButtonControl except)
        {
            foreach (var b in new[] { p.leftShoulder, p.rightShoulder, p.selectButton, p.startButton, p.leftStickButton, p.rightStickButton })
                if (b != except && b.isPressed) return true;
            return false;
        }

        static bool BackTapped(Gamepad p) { return Tapped(p, "back", p.selectButton.isPressed, ShortcutButtonsBut(p, p.selectButton)); }

        public static Key OpenKey()
        {
            Key k;
            if (!System.Enum.TryParse(Settings.ManagerKey.Value, true, out k) || k == Key.None) k = Key.F1;
            // 100% Drop & Steal Rate (azan22) opens its own menu with F1, so both would open at once.
            if (k == Key.F1 && KeyTakenBy() != null) k = Key.F8;
            return k;
        }

        // Shown wherever the key is: why it isn't F1, or "" when it's the configured key.
        public static string OpenKeyNote()
        {
            string other = OpenKey() == Key.F8 && KeyTakenBy() != null ? KeyTakenBy() : null;
            return other == null ? "" : " (F1 is used by " + other + ")";
        }

        // Name of a loaded mod that also uses F1, or null. Checked each time: plugins can load after us.
        public static string KeyTakenBy()
        {
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                if (info.Location != null && System.IO.Path.GetFileName(info.Location) == "TalesOfEternia_DropStealMod.dll")
                    return "100% Drop & Steal";
            return null;
        }

        // The configured controller shortcut as display text ("" when disabled).
        public static string ShortcutName()
        {
            switch (Settings.ManagerControllerShortcut.Value.Replace(" ", "").ToUpperInvariant())
            {
                case "NONE": return "";
                case "LB+RB+BACK": return "LB + RB + Back";
                case "BACK+START": return "Back + Start";
                default:
                    // Guests from Phantasia toggles its guest with L3+R3 (repeatedly while held), so both would fire.
                    return ShortcutTakenBy() != null ? "LB + RB + Back" : "L3 + R3";
            }
        }

        // Name of a loaded mod that also uses L3+R3, or null. Guests from Phantasia is a native mod (version.dll
        // loader), loaded a few seconds after us, so this is checked each time.
        public static string ShortcutTakenBy()
        {
            return Native.ModuleLoaded("ToE_Guest.dll") ? "Guests from Phantasia" : null;
        }

        // Why the shortcuts aren't the defaults, for the manager ("" when they are): " (F1 is X's, L3 + R3 is Y's)".
        public static string ConflictNote()
        {
            var parts = new List<string>();
            if (OpenKeyNote() != "") parts.Add("F1 is " + KeyTakenBy() + "'s");
            if (ShortcutMoved()) parts.Add("L3 + R3 is " + ShortcutTakenBy() + "'s");
            return parts.Count == 0 ? "" : " (" + string.Join(", ", parts.ToArray()) + ")";
        }

        // The same, short enough for the main-menu label: "Moved off F1 and L3 + R3 (used by other mods)", or "".
        public static string ConflictShort()
        {
            var moved = new List<string>();
            if (OpenKeyNote() != "") moved.Add("F1");
            if (ShortcutMoved()) moved.Add("L3 + R3");
            return moved.Count == 0 ? "" : "Moved off " + string.Join(" and ", moved.ToArray()) + " (used by other mods)";
        }

        static bool ShortcutMoved()
        {
            return ShortcutTakenBy() != null && Settings.ManagerControllerShortcut.Value.Replace(" ", "").ToUpperInvariant() == "L3+R3";
        }

        static bool ShortcutHeld(Gamepad p)
        {
            switch (ShortcutName())
            {
                case "": return false;
                case "LB + RB + Back": return p.leftShoulder.isPressed && p.rightShoulder.isPressed && p.selectButton.isPressed;
                case "Back + Start": return p.selectButton.isPressed && p.startButton.isPressed;
                default: return p.leftStickButton.isPressed && p.rightStickButton.isPressed;
            }
        }

        public static void Update()
        {
            var kb = Keyboard.current;
            bool openKey = KeyPressed(kb, OpenKey()), esc = KeyPressed(kb, Key.Escape);
            if (openKey || ComboHeld() || (Open && esc)) Toggle();
            if (!Open) return;

            var devices = Players.Devices();
            var players = Players.PlayerOf(devices, Settings.KeyboardPlayer.Value);
            if (selected >= devices.Count) selected = devices.Count - 1;
            if (selected < 0) selected = 0;

            for (int i = 0; i < devices.Count; i++)
            {
                var pad = devices[i] as Gamepad;
                if (pad == null) continue;
                Vector2 dpad = pad.dpad.ReadValue();
                if (Active(pad)) lastActive[pad.deviceId] = Time.unscaledTime;
                bool left = Tapped(pad, "L", pad.leftShoulder.isPressed, ShortcutButtonsBut(pad, pad.leftShoulder)) |
                    PadPressed(pad, "dL", dpad.x < -0.5f);
                bool right = Tapped(pad, "R", pad.rightShoulder.isPressed, ShortcutButtonsBut(pad, pad.rightShoulder)) |
                    PadPressed(pad, "dR", dpad.x > 0.5f);
                if (left) Move(devices, players, i, -1);
                if (right) Move(devices, players, i, +1);
                if (BackTapped(pad)) Reset();
            }
            if (kb != null)
            {
                if (kb.anyKey.isPressed) lastActive[kb.deviceId] = Time.unscaledTime;
                if (KeyPressed(kb, Key.UpArrow)) selected = (selected + devices.Count - 1) % devices.Count;
                if (KeyPressed(kb, Key.DownArrow)) selected = (selected + 1) % devices.Count;
                if (KeyPressed(kb, Key.LeftArrow)) Move(devices, players, selected, -1);
                if (KeyPressed(kb, Key.RightArrow)) Move(devices, players, selected, +1);
                if (KeyPressed(kb, Key.R)) Reset();
            }

#if COOP_DEBUG
            // Once a second while open: what the Input System reports for each controller.
            if (Time.unscaledTime >= nextDiag)
            {
                nextDiag = Time.unscaledTime + 1f;
                var sb = new System.Text.StringBuilder("Controller manager: updateMode=" + InputSystem.settings.updateMode);
                foreach (var pad in Gamepad.all)
                    sb.AppendFormat("  [{0} #{1} enabled={2} LB={3} RB={4} dpad={5} A={6} L3={7} R3={8} stick={9}]",
                        pad.displayName, pad.deviceId, pad.enabled, pad.leftShoulder.ReadValue(), pad.rightShoulder.ReadValue(),
                        pad.dpad.ReadValue(), pad.buttonSouth.ReadValue(), pad.leftStickButton.ReadValue(), pad.rightStickButton.ReadValue(),
                        pad.leftStick.ReadValue());
                Plugin.Log.LogInfo(sb.ToString());
            }
#endif
        }

        static void Toggle()
        {
            Open = !Open;
            Players.BlockAll = Open;
            comboSince = -1;
            Plugin.Log.LogMessage("Controller manager " + (Open ? "opened" : "closed"));
        }

        // Hold the controller shortcut on any controller for half a second.
        static bool ComboHeld()
        {
            bool held = false;
            foreach (var pad in Gamepad.all) if (ShortcutHeld(pad)) held = true;
            if (!held) { comboSince = -1; return false; }
            if (comboSince < 0) { comboSince = Time.unscaledTime; return false; }
            if (Time.unscaledTime - comboSince < 0.5f) return false;
            comboSince = float.MaxValue;   // fire once per hold
            return true;
        }

        static bool Active(Gamepad p)
        {
            return p.buttonSouth.isPressed || p.buttonEast.isPressed || p.buttonWest.isPressed || p.buttonNorth.isPressed ||
                p.leftShoulder.isPressed || p.rightShoulder.isPressed || p.dpad.ReadValue().sqrMagnitude > 0.25f ||
                p.leftStick.ReadValue().sqrMagnitude > 0.25f || p.rightStick.ReadValue().sqrMagnitude > 0.25f;
        }

        static void Move(List<InputDevice> devices, int[] players, int index, int step)
        {
            int from = players[index] < 0 ? 0 : players[index];
            int to = (from + step + 4) % 4;
            // Player 1 must keep a device: with none it would listen to every device and nobody could use the menus.
            if (from == 0)
            {
                int others = 0;
                for (int i = 0; i < devices.Count; i++) if (i != index && players[i] == 0) others++;
                if (others == 0) { Say("Player 1 needs at least one controller or the keyboard."); return; }
            }
            Players.SetManual(devices[index], to);
            Save();
            Say(Name(devices, index) + " -> Player " + (to + 1));
        }

        static void Reset()
        {
            Players.ClearManual();
            Save();
            Say("Back to automatic assignment.");
        }

        static void Save()
        {
            Settings.DeviceAssignments.Value = Players.SaveManual();
            Players.AutoAssign(Settings.KeyboardPlayer.Value);
        }

        static void Say(string text) { message = text; messageUntil = Time.unscaledTime + 3f; }

        // "Xbox Controller", "Xbox Controller (2)" when there are several of the same kind.
        static string Name(List<InputDevice> devices, int index)
        {
            var d = devices[index];
            if (d is Keyboard) return "Keyboard & Mouse";
            int same = 0, nth = 0;
            for (int i = 0; i < devices.Count; i++)
                if (devices[i].displayName == d.displayName) { same++; if (i <= index) nth = same; }
            return same > 1 ? d.displayName + " (" + nth + ")" : d.displayName;
        }

        public static void Draw()
        {
            if (!Open) return;
            if (title == null)
            {
                white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
                title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                row = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
                hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            }
            float u = Screen.height / 60f;   // layout unit
            title.fontSize = (int)(u * 1.6f); row.fontSize = (int)(u * 1.2f); hint.fontSize = (int)(u * 0.9f);

            var devices = Players.Devices();
            var players = Players.PlayerOf(devices, Settings.KeyboardPlayer.Value);
            string note = ConflictNote();
            float w = u * 36, rowH = u * 2.4f, h = u * 13 + rowH * devices.Count + (note == "" ? 0 : u * 1.5f);
            var box = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);

            GUI.color = new Color(0.04f, 0.06f, 0.12f, 0.92f); GUI.DrawTexture(box, white);
            GUI.color = new Color(1, 1, 1, 0.25f); GUI.DrawTexture(new Rect(box.x, box.y, box.width, 2), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x, box.y + u, w, u * 2.5f), "Controllers", title);

            float y = box.y + u * 4.5f;
            for (int i = 0; i < devices.Count; i++)
            {
                var r = new Rect(box.x + u, y, w - 2 * u, rowH - u * 0.3f);
                float since;
                bool active = lastActive.TryGetValue(devices[i].deviceId, out since) && Time.unscaledTime - since < 0.3f;
                GUI.color = i == selected ? new Color(1, 1, 1, 0.14f) : new Color(1, 1, 1, 0.05f);
                if (active) GUI.color = new Color(1, 1, 1, 0.30f);
                GUI.DrawTexture(r, white);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + u, r.y, r.width * 0.6f, r.height), Name(devices, i) + (Players.IsManual(devices[i]) ? "" : "  (auto)"), row);
                int p = players[i];
                GUI.color = p < 0 ? Color.gray : PlayerColors[p];
                GUI.Label(new Rect(r.x + r.width * 0.6f, r.y, r.width * 0.4f - u, r.height), p < 0 ? "<  none  >" : "<  Player " + (p + 1) + "  >", row);
                GUI.color = Color.white;
                y += rowH;
            }

            GUI.color = new Color(1, 1, 1, 0.75f);
            GUI.Label(new Rect(box.x + u, y + u * 0.5f, w - 2 * u, u * 6),
                "Controller: LB / RB or D-pad to change its player  ·  Back: automatic\n" +
                "Keyboard: Up / Down select, Left / Right change  ·  R: automatic\n" +
                "Close: " + OpenKey() + ", Esc" + (ShortcutName() == "" ? "" : ", or hold " + ShortcutName()) +
                (note == "" ? "" : "\nShortcuts changed to avoid other mods" + note), hint);
            if (message != null && Time.unscaledTime < messageUntil)
            {
                GUI.color = new Color(1f, 0.9f, 0.5f, 1f);
                GUI.Label(new Rect(box.x + u, box.yMax - u * 2.2f, w - 2 * u, u * 2), message, hint);
            }
            GUI.color = Color.white;
        }
    }
}
