// Which device belongs to which player. The game's C# input (UgxInputManager) gets one UgxInputPlayer per
// player; we tell each one which devices to listen to.
//
// Each device's player comes from the controller manager's saved choice for that device (Settings.DeviceAssignments,
// keyed by device path), else the automatic rule: KeyboardPlayer gets keyboard + mouse, controllers fill the
// players in connection order (player 1 also gets the first controller when it has the keyboard).
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace EterniaCoop
{
    static class Players
    {
        public static object InputManager;   // UgxInputManager, captured in its Initialize

        // Players 2-4 with no device of their own would otherwise listen to every device and mirror player 1,
        // so their input is blanked (see InputPatches) and their party slot falls back to Auto.
        public static volatile bool[] HasDevice = { true, false, false, false };
        // While the controller manager is open the game gets no input at all.
        public static volatile bool BlockAll;
        // While the battle menu is open, player 1 also listens to every other device: the remaster's battle menu takes
        // player 1's input only, so whoever opened it (Start, or Tab on the keyboard) can use and close it - the
        // closest we get to the PS1, where each player had their own menu.
        public static bool ShareWithPlayer1;
        // Outside battle the game listens to player 1 only. With FieldPlayer 1-3 (Settings.FieldControl, applied by
        // Plugin while no battle is running), player 1's input takes that player's devices instead.
        public static int FieldPlayer;
#if COOP_DEBUG
        public static volatile bool ForceBlankExtraPlayers;   // research toggle (Numpad 0)
#endif

        // A device set to Off controls nobody (e.g. a Parsec guest's keyboard and mouse, which arrive as the host's).
        public const int Off = -1;

        static string lastAssignment = "";
        static readonly Dictionary<string, int> manual = new Dictionary<string, int>();   // device path -> player 0-3 or Off

        static System.Reflection.MethodInfo getInputPlayer, setInputDevices;   // looked up once

        public static object Get(int i)
        {
            if (InputManager == null) return null;
            if (getInputPlayer == null) getInputPlayer = AccessTools.Method(InputManager.GetType(), "GetInputPlayer");
            return getInputPlayer.Invoke(InputManager, new object[] { i });
        }

        // Keyboard (mouse follows it) first, then controllers in connection order.
        public static List<InputDevice> Devices()
        {
            var list = new List<InputDevice>();
            if (Keyboard.current != null) list.Add(Keyboard.current);
            var pads = new List<Gamepad>(Gamepad.all);
            pads.Sort((a, b) => a.deviceId.CompareTo(b.deviceId));   // stable order across hot-plugs
            foreach (var p in pads) list.Add(p);
            return list;
        }

        // ---- saved manual choices ----
        public static void LoadManual(string saved)
        {
            manual.Clear();
            foreach (var part in (saved ?? "").Split(';'))
            {
                int eq = part.LastIndexOf('=');
                int player;
                if (eq > 0 && int.TryParse(part.Substring(eq + 1), out player) && player >= Off && player < 4)
                    manual[part.Substring(0, eq)] = player;
            }
        }

        public static string SaveManual()
        {
            var parts = new List<string>();
            foreach (var kv in manual) parts.Add(kv.Key + "=" + kv.Value);
            return string.Join(";", parts.ToArray());
        }

        public static bool IsManual(InputDevice d) { return manual.ContainsKey(d.path); }
        public static void SetManual(InputDevice d, int player) { manual[d.path] = player; }
        public static void ClearManual() { manual.Clear(); }

        // Player (0-3) each listed device goes to, in Devices() order; Off (-1) for none. keyboardPlayer -1 = keyboard off.
        public static int[] PlayerOf(List<InputDevice> devices, int keyboardPlayer)
        {
            var result = new int[devices.Count];
            int nextAuto = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                int auto;
                if (d is Keyboard) auto = keyboardPlayer < 0 ? Off : keyboardPlayer;
                else
                {
                    if (nextAuto == keyboardPlayer && keyboardPlayer != 0) nextAuto++;
                    auto = nextAuto < 4 ? nextAuto : -1;
                    nextAuto++;
                }
                int chosen;
                result[i] = manual.TryGetValue(d.path, out chosen) ? chosen : auto;
            }
            return result;
        }

        public static List<InputDevice>[] Desired(int keyboardPlayer)
        {
            var devices = Devices();
            var players = PlayerOf(devices, keyboardPlayer);
            var result = new List<InputDevice>[4];
            for (int i = 0; i < 4; i++) result[i] = new List<InputDevice>();
            for (int i = 0; i < devices.Count; i++)
            {
                if (players[i] < 0) continue;
                result[players[i]].Add(devices[i]);
                if (devices[i] is Keyboard && Mouse.current != null) result[players[i]].Add(Mouse.current);
            }
            if (ShareWithPlayer1)
                for (int p = 1; p < 4; p++)
                    foreach (var d in result[p]) if (!result[0].Contains(d)) result[0].Add(d);
            // Field control handed over (only when that player has a device: player 1 must never be left with none).
            if (FieldPlayer > 0 && FieldPlayer < 4 && result[FieldPlayer].Count > 0)
                result[0] = new List<InputDevice>(result[FieldPlayer]);
            return result;
        }

        // Applies Desired() when it differs from the last assignment; called once a second and after manager changes.
        public static void AutoAssign(int keyboardPlayer)
        {
            var want = Desired(keyboardPlayer);
            string key = "";
            for (int i = 0; i < 4; i++)
            {
                key += i + ":";
                foreach (var d in want[i]) key += d.deviceId + ",";
                key += " ";
            }
            for (int i = 1; i < 4; i++) HasDevice[i] = want[i].Count > 0;
            if (key == lastAssignment) return;
            lastAssignment = key;

            var names = new System.Text.StringBuilder("Controllers:");
            for (int i = 0; i < 4; i++)
            {
                object p = Get(i);
                if (p == null) continue;
                if (setInputDevices == null) setInputDevices = AccessTools.Method(p.GetType(), "SetInputDevices");
                if (want[i].Count > 0) setInputDevices.Invoke(p, new object[] { want[i].ToArray() });
                names.Append("  P" + (i + 1) + "=");
                if (want[i].Count == 0) names.Append("(none)");
                foreach (var d in want[i]) names.Append(d.displayName + " ");
            }
            Plugin.Log.LogMessage(names.ToString());
        }

        // Manual mode (AutoAssignControllers = false): trust whatever assignment exists.
        public static void AssumeAllPresent()
        {
            for (int i = 1; i < 4; i++) HasDevice[i] = true;
        }

        public static bool ShouldBlank(int playerNo)
        {
            if (BlockAll) return true;
            if (playerNo < 1 || playerNo > 3) return false;
#if COOP_DEBUG
            if (ForceBlankExtraPlayers) return true;
#endif
            return !HasDevice[playerNo];
        }
    }
}
