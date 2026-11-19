// Debug build only: [Debug] BotPlayers = n adds n virtual gamepads that play on their own, so co-op can be tested with
// more players than there are people. In battle they run back and forth across the field (in opposite directions),
// tapping attack and arte, now and then L3, L2 and R2; while the battle is paused they let go, so they never touch a menu.
// [Debug] BotDropCycle = true unplugs bot 1 (player 2) for 10 s of every 40 while a battle's actors exist, and plugs it
// back in as a new device of the same name - like a wireless controller sleeping or a Parsec guest dropping.
#if COOP_DEBUG
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace EterniaCoop
{
    static class BotPlayers
    {
        const float RunSeconds = 6f, AttackEvery = 0.8f, ArteEvery = 3.5f, TapSeconds = 0.1f;
        static ConfigEntry<int> count;
        static ConfigEntry<bool> dropCycle;
        static bool dropped;
        static readonly List<Gamepad> pads = new List<Gamepad>();

        public static void Bind(ConfigFile c)
        {
            count = c.Bind("Debug", "BotPlayers", 0, "Virtual gamepads that play battles on their own, for testing. Debug build only.");
            dropCycle = c.Bind("Debug", "BotDropCycle", false, "Unplug bot 1 (player 2) for 10 s of every 40 in battle, then plug it back in. Debug build only.");
        }

        public static void Update()
        {
            if (count == null || count.Value <= 0 || Native.Base == 0) return;
            while (pads.Count < count.Value)
            {
                var pad = InputSystem.AddDevice<Gamepad>("EterniaBot" + (pads.Count + 1));
                // Pinned to a player (in memory only): a real controller reconnecting must not move a bot onto player 1.
                Players.SetManual(pad, pads.Count == 0 ? 1 : 3);
                pads.Add(pad);
                Plugin.Log.LogMessage("Bot player " + pads.Count + " added: player " + (pads.Count == 1 ? 2 : 4) + ".");
            }
            bool play = Features.CoopBattle && Battle.Actors() != System.IntPtr.Zero &&
                (System.Runtime.InteropServices.Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) == 0;
            float t = Time.unscaledTime;
            if (dropCycle.Value && pads.Count > 0)
            {
                bool wantOut = Battle.Actors() != System.IntPtr.Zero && Mathf.Repeat(t, 40f) < 10f;
                if (wantOut && !dropped)
                {
                    InputSystem.RemoveDevice(pads[0]);
                    dropped = true;
                    Plugin.Log.LogMessage("Bot 1 unplugged (player 2).");
                }
                else if (!wantOut && dropped)
                {
                    var pad = InputSystem.AddDevice<Gamepad>("EterniaBot1");
                    Players.SetManual(pad, 1);
                    pads[0] = pad;
                    dropped = false;
                    Plugin.Log.LogMessage("Bot 1 plugged back in (player 2).");
                }
            }
            for (int i = 0; i < pads.Count; i++)
            {
                if (i == 0 && dropped) continue;
                var state = new GamepadState();
                if (play)
                {
                    bool right = ((int)(t / RunSeconds) + i) % 2 == 0;
                    state.leftStick = new Vector2(right ? 1f : -1f, 0f);
                    state = state.WithButton(right ? GamepadButton.DpadRight : GamepadButton.DpadLeft);
                    if (Mathf.Repeat(t + i * 0.3f, AttackEvery) < TapSeconds) state = state.WithButton(GamepadButton.South);
                    if (Mathf.Repeat(t + i * 1.1f, ArteEvery) < TapSeconds) state = state.WithButton(GamepadButton.East);
                    // L3 (Water's Edition Overlimit), L2 and R2 (Artesphere pages, L2/R2 shortcuts) now and then.
                    if (Mathf.Repeat(t + i * 0.7f, 5f) < TapSeconds) state = state.WithButton(GamepadButton.LeftStick);
                    if (Mathf.Repeat(t + i * 0.9f, 4f) < 0.3f) state.leftTrigger = 1f;
                    if (Mathf.Repeat(t + i * 0.9f + 2f, 4f) < 0.3f) state.rightTrigger = 1f;
                }
                InputSystem.QueueStateEvent(pads[i], state);
            }
        }
    }
}
#endif
