// In-game controller manager: choose which device controls which player (or Off).
//   Open/close:  Settings.ManagerKey (F1) on the keyboard, or hold Settings.ManagerControllerShortcut (L3+R3) on any
//                controller (so Parsec guests can open it). The defaults move to F8 / LB+RB+Back while a mod that
//                uses them is loaded (100% Drop & Steal Rate / Guests from Phantasia).
//   Any controller: D-pad or left stick up/down picks a row, left/right changes its player (held: repeats),
//                A jumps to your own controller's row, LB/RB move your own controller, Y = automatic, B closes.
//   Keyboard:    arrows (or W/A/S/D) the same, R = automatic, Esc / Enter close.
//   The last row, Field control, picks who controls the field and the menus outside battle (Settings.FieldControl).
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
        static float messageUntil, comboSince = -1, openedAt;
        static readonly Dictionary<int, float> lastActive = new Dictionary<int, float>();   // deviceId -> time
        static readonly Color[] PlayerColors = ManagerView.PlayerColors;

        // Our own press detection ("held now, not held last time we looked"). wasPressedThisFrame depends on when
        // the Input System processes events relative to our Update, which isn't reliable in this game.
        static readonly Dictionary<string, bool> held = new Dictionary<string, bool>();
        static readonly Dictionary<string, float> repeatAt = new Dictionary<string, float>();
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

        // Pressed, then repeating while held (menu-style): first repeat after 0.35 s, then every 0.12 s.
        static bool Repeat(string key, bool isDown)
        {
            bool was;
            held.TryGetValue(key, out was);
            held[key] = isDown;
            if (!isDown) return false;
            float now = Time.unscaledTime, at;
            if (!was) { repeatAt[key] = now + 0.35f; return true; }
            if (repeatAt.TryGetValue(key, out at) && now >= at) { repeatAt[key] = now + 0.12f; return true; }
            return false;
        }

        static readonly Dictionary<Key, bool> keyHeld = new Dictionary<Key, bool>();   // checked every frame: no string keys

        static bool KeyPressed(Keyboard kb, Key k)
        {
            if (kb == null) return false;
            bool down = kb[k].isPressed, was;
            keyHeld.TryGetValue(k, out was);
            keyHeld[k] = down;
            return down && !was;
        }

        static bool KeyRepeat(Keyboard kb, Key a, Key b) { return kb != null && Repeat("k" + a, kb[a].isPressed || kb[b].isPressed); }

        // LB, RB, Back and B act when released without any other shortcut button pressed during the hold, so the
        // shortcuts made of them (LB+RB+Back, Back+Start) open and close the manager without also moving the
        // controller, and closing with B doesn't leave B held for the game.
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
            return Down(p.leftShoulder, except) || Down(p.rightShoulder, except) || Down(p.selectButton, except) ||
                Down(p.startButton, except) || Down(p.leftStickButton, except) || Down(p.rightStickButton, except);
        }

        static bool Down(ButtonControl b, ButtonControl except) { return b != except && b.isPressed; }

        // D-pad, else left stick (past half-way), as one direction.
        static Vector2 Direction(Gamepad p)
        {
            Vector2 d = p.dpad.ReadValue();
            if (d.sqrMagnitude < 0.25f) d = p.leftStick.ReadValue();
            return d;
        }

        // ---- shortcuts ----
        // Worked out once a second (Refresh) rather than every frame: they only change when a setting changes or another
        // mod finishes loading (plugins can load after us; Guests from Phantasia's native DLL a few seconds later).
        enum Combo { None, L3R3, LbRbBack, BackStart }

        static float nextRefresh;
        static Key openKey;
        static Combo combo;
        static string keyTakenBy, shortcutTakenBy, shortcutName, conflictNote, conflictShort;

        static void Refresh()
        {
            if (Time.unscaledTime < nextRefresh && shortcutName != null) return;
            nextRefresh = Time.unscaledTime + 1f;

            // 100% Drop & Steal Rate (azan22) opens its own menu with F1, so both would open at once.
            keyTakenBy = null;
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                if (info.Location != null && System.IO.Path.GetFileName(info.Location) == "TalesOfEternia_DropStealMod.dll")
                    keyTakenBy = "100% Drop & Steal";
            if (!System.Enum.TryParse(Settings.ManagerKey.Value, true, out openKey) || openKey == Key.None) openKey = Key.F1;
            bool keyMoved = openKey == Key.F1 && keyTakenBy != null;
            if (keyMoved) openKey = Key.F8;

            // Guests from Phantasia (native mod) toggles its guest with L3+R3, repeatedly while held, so both would fire.
            shortcutTakenBy = Native.ModuleLoaded("ToE_Guest.dll") ? "Guests from Phantasia" : null;
            bool shortcutMoved = false;
            switch (Settings.ManagerControllerShortcut.Value.Replace(" ", "").ToUpperInvariant())
            {
                case "NONE": combo = Combo.None; break;
                case "LB+RB+BACK": combo = Combo.LbRbBack; break;
                case "BACK+START": combo = Combo.BackStart; break;
                default:
                    shortcutMoved = shortcutTakenBy != null;
                    combo = shortcutMoved ? Combo.LbRbBack : Combo.L3R3;
                    break;
            }
            shortcutName = combo == Combo.None ? "" : combo == Combo.LbRbBack ? "LB + RB + Back" : combo == Combo.BackStart ? "Back + Start" : "L3 + R3";

            var parts = new List<string>();
            var moved = new List<string>();
            if (keyMoved) { parts.Add("F1 is " + keyTakenBy + "'s"); moved.Add("F1"); }
            if (shortcutMoved) { parts.Add("L3 + R3 is " + shortcutTakenBy + "'s"); moved.Add("L3 + R3"); }
            conflictNote = parts.Count == 0 ? "" : " (" + string.Join(", ", parts.ToArray()) + ")";
            conflictShort = moved.Count == 0 ? "" : "Moved off " + string.Join(" and ", moved.ToArray()) + " (used by other mods)";
        }

        public static Key OpenKey() { Refresh(); return openKey; }

        // Name of a loaded mod that also uses F1, or null.
        public static string KeyTakenBy() { Refresh(); return keyTakenBy; }

        // The controller shortcut as display text ("" when disabled).
        public static string ShortcutName() { Refresh(); return shortcutName; }

        // Name of a loaded mod that also uses L3+R3, or null.
        public static string ShortcutTakenBy() { Refresh(); return shortcutTakenBy; }

        // Why the shortcuts aren't the defaults, for the manager ("" when they are): " (F1 is X's, L3 + R3 is Y's)".
        public static string ConflictNote() { Refresh(); return conflictNote; }

        // The same, short enough for the main-menu label: "Moved off F1 and L3 + R3 (used by other mods)", or "".
        public static string ConflictShort() { Refresh(); return conflictShort; }

        static bool ShortcutHeld(Gamepad p)
        {
            switch (combo)
            {
                case Combo.None: return false;
                case Combo.LbRbBack: return p.leftShoulder.isPressed && p.rightShoulder.isPressed && p.selectButton.isPressed;
                case Combo.BackStart: return p.selectButton.isPressed && p.startButton.isPressed;
                default: return p.leftStickButton.isPressed && p.rightStickButton.isPressed;
            }
        }

        public static void Update()
        {
            var kb = Keyboard.current;
            bool key = KeyPressed(kb, OpenKey());
            bool closeKey = KeyPressed(kb, Key.Escape) | KeyPressed(kb, Key.Enter);
            Gamepad comboPad = ComboHeld();
            ManagerView.FindCast();
            if (key || comboPad != null || (Open && closeKey)) Toggle(comboPad);
            if (!Open) { ManagerView.Hide(); return; }

            var devices = Players.Devices();
            if (devices.Count == 0) return;
            var players = Players.PlayerOf(devices, Settings.KeyboardPlayer.Value);
            int rows = devices.Count + 1;   // the devices, then the Field control row
            if (selected >= rows) selected = rows - 1;
            if (selected < 0) selected = 0;
            bool justOpened = Time.unscaledTime - openedAt < 0.3f;   // buttons still held from the shortcut

            for (int i = 0; i < devices.Count; i++)
            {
                var pad = devices[i] as Gamepad;
                if (pad == null) continue;
                if (Active(pad)) lastActive[pad.deviceId] = Time.unscaledTime;
                string id = pad.deviceId.ToString();
                Vector2 d = Direction(pad);
                bool up = Repeat(id + "up", d.y > 0.5f), down = Repeat(id + "down", d.y < -0.5f);
                bool left = Repeat(id + "left", d.x < -0.5f), right = Repeat(id + "right", d.x > 0.5f);
                bool lb = Tapped(pad, "LB", pad.leftShoulder.isPressed, ShortcutButtonsBut(pad, pad.leftShoulder));
                bool rb = Tapped(pad, "RB", pad.rightShoulder.isPressed, ShortcutButtonsBut(pad, pad.rightShoulder));
                bool a = Pressed(id + "A", pad.buttonSouth.isPressed), y = Pressed(id + "Y", pad.buttonNorth.isPressed);
                bool b = Tapped(pad, "B", pad.buttonEast.isPressed, false);
                if (justOpened) continue;

                if (up) selected = (selected + rows - 1) % rows;
                if (down) selected = (selected + 1) % rows;
                if (left) Move(devices, players, selected, -1);
                if (right) Move(devices, players, selected, +1);
                if (a) selected = i;
                if (lb) { selected = i; Move(devices, players, i, -1); }
                if (rb) { selected = i; Move(devices, players, i, +1); }
                if (y) Reset();
                if (b) { Toggle(null); ManagerView.Hide(); return; }
                if (left || right || lb || rb) players = Players.PlayerOf(devices, Settings.KeyboardPlayer.Value);
            }
            if (kb != null)
            {
                if (kb.anyKey.isPressed) lastActive[kb.deviceId] = Time.unscaledTime;
                if (KeyRepeat(kb, Key.UpArrow, Key.W)) selected = (selected + rows - 1) % rows;
                if (KeyRepeat(kb, Key.DownArrow, Key.S)) selected = (selected + 1) % rows;
                if (KeyRepeat(kb, Key.LeftArrow, Key.A)) Move(devices, players, selected, -1);
                if (KeyRepeat(kb, Key.RightArrow, Key.D)) Move(devices, players, selected, +1);
                if (KeyPressed(kb, Key.R)) Reset();
            }
            Show(devices);

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

        // Opened by a controller's shortcut: start on that controller's row; by the keyboard: on the keyboard's.
        static void Toggle(Gamepad opener)
        {
            Open = !Open;
            Players.BlockAll = Open;
            comboSince = -1;
            if (Open)
            {
                openedAt = Time.unscaledTime;
                var devices = Players.Devices();
                selected = 0;
                for (int i = 0; i < devices.Count; i++)
                    if (opener != null ? devices[i] == opener : devices[i] is Keyboard) selected = i;
            }
            Plugin.Log.LogMessage("Controller manager " + (Open ? "opened" : "closed"));
        }

        // Hold the controller shortcut on any controller for half a second: that controller, once per hold.
        static Gamepad ComboHeld()
        {
            Gamepad who = null;
            foreach (var pad in Gamepad.all) if (ShortcutHeld(pad)) who = pad;
            if (who == null) { comboSince = -1; return null; }
            if (comboSince < 0) { comboSince = Time.unscaledTime; return null; }
            if (Time.unscaledTime - comboSince < 0.5f) return null;
            comboSince = float.MaxValue;   // fire once per hold
            return who;
        }

        static bool Active(Gamepad p)
        {
            return p.buttonSouth.isPressed || p.buttonEast.isPressed || p.buttonWest.isPressed || p.buttonNorth.isPressed ||
                p.leftShoulder.isPressed || p.rightShoulder.isPressed || p.dpad.ReadValue().sqrMagnitude > 0.25f ||
                p.leftStick.ReadValue().sqrMagnitude > 0.25f || p.rightStick.ReadValue().sqrMagnitude > 0.25f;
        }

        // Choices in order: Off, Player 1-4 (wrapping).
        static void Move(List<InputDevice> devices, int[] players, int index, int step)
        {
            if (index >= devices.Count)   // the Field control row: Player 1-4
            {
                int field = (Mathf.Clamp(Settings.FieldControl.Value, 0, 3) + step + 4) % 4;
                Settings.FieldControl.Value = field;
                Say("Player " + (field + 1) + " controls the field and menus outside battle.");
                return;
            }
            int from = players[index];
            int to = (from + 1 + step + 5) % 5 - 1;
            // Player 1 must keep a device: with none it would listen to every device and nobody could use the menus.
            if (from == 0)
            {
                int others = 0;
                for (int i = 0; i < devices.Count; i++) if (i != index && players[i] == 0) others++;
                if (others == 0) { Say("Player 1 needs at least one controller or the keyboard."); return; }
            }
            Players.SetManual(devices[index], to);
            Save();
            Say(Name(devices, index) + (to == Players.Off ? " turned off" : " is now Player " + (to + 1)));
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

        // ---- drawing ----
        static readonly string[][] HintList =
        {
            new[] { "Up / Down", "Choose" }, new[] { "Left / Right", "Change player" }, new[] { "A", "My controller" },
            new[] { "Y / R", "Automatic" }, new[] { "B / Esc", "Close" },
        };
        static readonly List<ManagerView.Row> viewRows = new List<ManagerView.Row>();

        static string Footer()
        {
            return "D-pad, left stick or arrow keys  ·  LB / RB move your own controller  ·  Open with " + OpenKey() +
                (ShortcutName() == "" ? "" : " or hold " + ShortcutName());
        }

        static bool RecentlyActive(InputDevice d)
        {
            float since;
            return lastActive.TryGetValue(d.deviceId, out since) && Time.unscaledTime - since < 0.3f;
        }

        // The game-styled window (ManagerView), when the game's fonts are there; else Draw's IMGUI window is used.
        static void Show(List<InputDevice> devices)
        {
            if (!ManagerView.Available) return;
            var players = Players.PlayerOf(devices, Settings.KeyboardPlayer.Value);
            viewRows.Clear();
            for (int i = 0; i < devices.Count; i++)
                viewRows.Add(new ManagerView.Row
                {
                    Name = Name(devices, i) + (Players.IsManual(devices[i]) ? "" : "  (auto)"),
                    Player = players[i], Selected = i == selected, Active = RecentlyActive(devices[i]),
                });
            viewRows.Add(new ManagerView.Row
            {
                Name = "Field control", Player = Mathf.Clamp(Settings.FieldControl.Value, 0, 3),
                Selected = selected == devices.Count, NoOff = true,
            });
            string note = ConflictNote();
            ManagerView.Show(viewRows, HintList, Footer(), note == "" ? "" : "Shortcuts changed to avoid other mods" + note,
                message != null && Time.unscaledTime < messageUntil ? message : null);
        }

        // ---- fallback drawing (IMGUI): a Tales-style window (deep blue gradient, gold frame, outlined text) ----
        static Texture2D white, gradient;
        static GUIStyle title, rowText, small, pill, glyph;
        static readonly Color Gold = ManagerView.Gold, GoldDim = ManagerView.GoldDim;
        static readonly Color Ink = new Color(0f, 0f, 0f, 0.85f);

        static void Init()
        {
            white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
            gradient = new Texture2D(1, 64) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                gradient.SetPixel(0, y, Color.Lerp(new Color(0.02f, 0.03f, 0.10f, 0.95f), new Color(0.07f, 0.13f, 0.30f, 0.95f), y / 63f));
            gradient.Apply();
            title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            rowText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
            small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            pill = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            glyph = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, white); GUI.color = Color.white; }

        static void Frame(Rect r, float t, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // Text with the dark outline the game uses on its own text.
        static void Text(Rect r, string s, GUIStyle style, Color c, float outline)
        {
            GUI.color = Ink;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx * outline, r.y + dy * outline, r.width, r.height), s, style);
            GUI.color = c;
            GUI.Label(r, s, style);
            GUI.color = Color.white;
        }

        // A button glyph ("A", "LB") in a small box, then its meaning; returns the x after it.
        static float Hint(float x, float y, float u, string button, string meaning)
        {
            var size = glyph.CalcSize(new GUIContent(button));
            var box = new Rect(x, y, Mathf.Max(size.x + u * 0.8f, u * 1.6f), u * 1.5f);
            Fill(box, new Color(1, 1, 1, 0.12f)); Frame(box, 1, GoldDim);
            Text(box, button, glyph, Gold, 1);
            var text = small.CalcSize(new GUIContent(meaning));
            Text(new Rect(box.xMax + u * 0.4f, y, text.x + 2, box.height), meaning, small, Color.white, 1);
            return box.xMax + u * 0.4f + text.x + u * 1.2f;
        }

        public static void Draw()
        {
            if (!Open || ManagerView.Available) return;
            if (white == null) Init();
            float u = Screen.height / 60f;   // layout unit
            title.fontSize = (int)(u * 1.7f); rowText.fontSize = (int)(u * 1.15f); small.fontSize = (int)(u * 0.85f);
            pill.fontSize = (int)(u * 0.9f); glyph.fontSize = (int)(u * 0.8f);

            var devices = Players.Devices();
            var players = Players.PlayerOf(devices, Settings.KeyboardPlayer.Value);
            string note = ConflictNote();
            float w = u * 46, rowH = u * 2.6f, h = u * 15.5f + rowH * devices.Count + (note == "" ? 0 : u * 1.4f);
            var box = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);

            // Dim the game behind, then the window: gradient, gold frame with a fine inner line, as the game's menus.
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.35f));
            GUI.DrawTexture(box, gradient);
            Frame(box, Mathf.Max(2, u * 0.15f), Gold);
            Frame(new Rect(box.x + u * 0.35f, box.y + u * 0.35f, box.width - u * 0.7f, box.height - u * 0.7f), 1, GoldDim);

            Text(new Rect(box.x, box.y + u * 0.9f, w, u * 2.6f), "Controllers", title, Gold, Mathf.Max(1, u * 0.12f));
            Fill(new Rect(box.x + w * 0.2f, box.y + u * 3.6f, w * 0.6f, 1), GoldDim);

            float y = box.y + u * 4.6f, pillW = u * 3.4f, gap = u * 0.35f;
            float pillsX = box.xMax - u * 1.5f - 5 * pillW - 4 * gap;
            for (int i = 0; i < devices.Count; i++)
            {
                var r = new Rect(box.x + u * 1.2f, y, w - u * 2.4f, rowH - u * 0.35f);
                float since;
                bool active = lastActive.TryGetValue(devices[i].deviceId, out since) && Time.unscaledTime - since < 0.3f;
                bool sel = i == selected;
                Fill(r, sel ? new Color(0.45f, 0.62f, 1f, 0.22f) : new Color(1, 1, 1, 0.04f));
                if (sel) Frame(r, 1, Gold);
                if (active) Fill(new Rect(r.x, r.y, u * 0.3f, r.height), Color.white);
                if (sel) Text(new Rect(r.x + u * 0.5f, r.y, u * 1.5f, r.height), "▶", rowText, Gold, 1);

                string name = Name(devices, i) + (Players.IsManual(devices[i]) ? "" : "  (auto)");
                Text(new Rect(r.x + u * 2f, r.y, pillsX - r.x - u * 2.5f, r.height), name, rowText, sel ? Color.white : new Color(0.85f, 0.88f, 0.95f), 1);

                // Off, P1..P4: the chosen one lit in that player's colour.
                for (int k = 0; k < 5; k++)
                {
                    int p = k - 1;
                    var pr = new Rect(pillsX + k * (pillW + gap), r.y + r.height * 0.18f, pillW, r.height * 0.64f);
                    bool on = players[i] == p;
                    Color c = p < 0 ? new Color(0.6f, 0.6f, 0.65f) : PlayerColors[p];
                    if (on) Fill(pr, new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.95f));
                    Frame(pr, 1, on ? c : new Color(1, 1, 1, 0.18f));
                    Text(pr, p < 0 ? "Off" : "P" + (p + 1), pill, on ? Color.white : new Color(1, 1, 1, 0.35f), 1);
                }
                y += rowH;
            }

            // Button hints, then the shortcuts.
            y += u * 0.6f;
            Fill(new Rect(box.x + w * 0.1f, y, w * 0.8f, 1), GoldDim);
            y += u * 0.8f;
            float x = box.x + u * 2f;
            foreach (var hint in HintList) x = Hint(x, y, u, hint[0], hint[1]);
            y += u * 2.1f;
            Text(new Rect(box.x + u, y, w - 2 * u, u * 1.4f), Footer(), small, new Color(1, 1, 1, 0.7f), 1);
            if (note != "")
            {
                y += u * 1.4f;
                Text(new Rect(box.x + u, y, w - 2 * u, u * 1.4f), "Shortcuts changed to avoid other mods" + note, small, new Color(1, 1, 1, 0.6f), 1);
            }
            if (message != null && Time.unscaledTime < messageUntil)
                Text(new Rect(box.x + u, box.yMax - u * 2.3f, w - 2 * u, u * 1.6f), message, small, Gold, 1);
        }
    }
}
