// Per-player rumble (PS1: each DualShock rumbled for its own character).
// FUN_8E320(type, flags, hit, attacker, victim) applies a hit and requests rumble (Rva.RumbleFrames / Strength)
// when the attacker or the victim is a party member and rumble is on in the game's options. The per-frame system
// loop then plays that request on player 1 only. We let the game decide whether to rumble, then hand requests
// caused by players 2-4's characters to their own controllers.
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

        // Battle thread: decide only; the controllers are driven from Update.
        static void Hook(int type, uint flags, IntPtr hit, IntPtr attacker, IntPtr victim)
        {
            // Same priority as the game: the attacker's rumble if a party member hit, else the victim's.
            int slot = -1;
            try
            {
                if (attacker != IntPtr.Zero && Marshal.ReadByte(attacker, Actor.Inactive) == 0) slot = Battle.PartySlot(attacker);
                if (slot < 0 && victim != IntPtr.Zero && Marshal.ReadByte(victim, Actor.Inactive) == 0) slot = Battle.PartySlot(victim);
            }
            catch { slot = -1; }

            IntPtr frames = Native.At(Rva.RumbleFrames);
            short before = Marshal.ReadInt16(frames);
            Marshal.WriteInt16(frames, 0);
            original(type, flags, hit, attacker, victim);
            short after = Marshal.ReadInt16(frames);

            if (after == 0) { Marshal.WriteInt16(frames, before); return; }   // the game didn't ask for rumble
            if (slot < 1 || slot > 3 || !Players.HasDevice[slot]) return;     // player 1's character: unchanged
            Marshal.WriteInt16(frames, before);                                // not player 1's hit
            pending[slot] = Math.Max(pending[slot], Native.ReadFloat(Native.At(Rva.RumbleStrength)));
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
