// Debug build only: [Debug] BotPlayers = n adds n virtual gamepads that play on their own, so co-op can be tested with
// more players than there are people. In battle they run back and forth across the field (in opposite directions),
// tapping attack and arte; while the battle is paused they let go, so they never touch a menu.
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
        static readonly List<Gamepad> pads = new List<Gamepad>();

        public static void Bind(ConfigFile c)
        {
            count = c.Bind("Debug", "BotPlayers", 0, "Virtual gamepads that play battles on their own, for testing. Debug build only.");
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
            for (int i = 0; i < pads.Count; i++)
            {
                var state = new GamepadState();
                if (play)
                {
                    bool right = ((int)(t / RunSeconds) + i) % 2 == 0;
                    state.leftStick = new Vector2(right ? 1f : -1f, 0f);
                    state = state.WithButton(right ? GamepadButton.DpadRight : GamepadButton.DpadLeft);
                    if (Mathf.Repeat(t + i * 0.3f, AttackEvery) < TapSeconds) state = state.WithButton(GamepadButton.South);
                    if (Mathf.Repeat(t + i * 1.1f, ArteEvery) < TapSeconds) state = state.WithButton(GamepadButton.East);
                }
                InputSystem.QueueStateEvent(pads[i], state);
            }
        }
    }
}
#endif
