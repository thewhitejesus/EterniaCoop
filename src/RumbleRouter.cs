// Per-player rumble (PS1: each DualShock rumbled for its own character).
// FUN_8E320(type, flags, hit, attacker, victim) applies a hit. At its end it requests rumble (strength 0.6, 6 frames)
// when rumble is on in the game's options and the attacker - else the victim - is the party character in command
// block 0 (actor+0x56 = block index): player 1's. Players 2-4's characters (blocks 1-3) never get one. We leave player
// 1's to the game and apply the same rule to blocks 1-3, on that player's own controller.
using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace EterniaCoop
{
    static class RumbleRouter
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void HitFn(int type, uint flags, IntPtr hit, IntPtr attacker, IntPtr victim);

        static MonoMod.RuntimeDetour.NativeDetour detour;
        static HitFn original, hook;
        static readonly float[] pending = new float[4], until = new float[4];

        // Features.Rumble has checked the function and the addresses this uses.
        public static void Install()
        {
            if (detour != null || Native.Base == 0) return;
            hook = Hook;
            detour = new MonoMod.RuntimeDetour.NativeDetour(Native.At(Rva.ApplyHit), Marshal.GetFunctionPointerForDelegate(hook));
            original = detour.GenerateTrampoline<HitFn>();
            Plugin.Log.LogMessage("Per-player rumble hooked.");
        }

        const float Strength = 0.6f;   // the game's own (0x3F19999A, checked by Features.Rumble)

        // Battle thread: decide only; the controllers are driven from Update.
        static void Hook(int type, uint flags, IntPtr hit, IntPtr attacker, IntPtr victim)
        {
            // The game skips the whole hit while the battle is paused, rumble included.
            bool applies = Marshal.ReadByte(Native.At(Rva.BattleFlags)) == 0;
            original(type, flags, hit, attacker, victim);   // player 1's rumble stays the game's
            if (!applies) return;
            try
            {
                IntPtr options = Marshal.ReadIntPtr(Native.At(Rva.GameOptions));
                if (options == IntPtr.Zero || Marshal.ReadByte(options, 0x21) == 0) return;   // rumble off in the options
                // Same priority as the game: the attacker if a party member hit, else the victim.
                int slot = attacker != IntPtr.Zero ? Battle.PartySlot(attacker) : -1;
                if (slot < 0 && victim != IntPtr.Zero) slot = Battle.PartySlot(victim);
                if (slot >= 1 && slot <= 3 && Players.HasDevice[slot]) pending[slot] = Strength;
            }
            catch { }
        }

        // Main thread, every frame.
        public static void Update(float seconds)
        {
            if (Players.InputManager == null) return;
            for (int p = 1; p < 4; p++)
            {
                if (pending[p] > 0)
                {
                    AccessTools.Method(Players.InputManager.GetType(), "SetVibration", new[] { typeof(float), typeof(int) })
                        .Invoke(Players.InputManager, new object[] { pending[p], p });
                    until[p] = Time.unscaledTime + seconds;
                    pending[p] = 0;
                }
                else if (until[p] > 0 && Time.unscaledTime >= until[p])
                {
                    AccessTools.Method(Players.InputManager.GetType(), "StopVibration", new[] { typeof(int) })
                        .Invoke(Players.InputManager, new object[] { p });
                    until[p] = 0;
                }
            }
        }
    }
}
