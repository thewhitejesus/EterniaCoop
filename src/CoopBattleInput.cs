// Co-op battle input. Each battle party slot reads its commands from its own 0x20-byte block (actor+0xF0 ->
// Rva.CmdBlocks + slot * 0x20) - the PS1's 4-player design - but FUN_1121E0 only ever fills slot 0's block from
// the one PSP pad. After it runs we build a pad for each extra player with the game's own pad builder
// (FUN_19DBC0, pointed at that player's slice of the input object), run FUN_1121E0 again on it, and store the
// result in that slot's block. Slot 0's pad and block are put back exactly as they were.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class CoopBattleInput
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void BattleCmdFn(IntPtr padHeld);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PadBuildFn(IntPtr pad, byte rate, int flag);

        static MonoMod.RuntimeDetour.NativeDetour detour;
        static BattleCmdFn original, hook;   // the hook delegate must stay referenced while native code can call it
        static PadBuildFn buildPad;
        static readonly IntPtr[] pads = new IntPtr[4], states = new IntPtr[4];
        static IntPtr savedPad, savedCmd;
        static bool inHook;

        // Features.CoopBattle has checked both functions and the addresses this uses.
        public static void Install()
        {
            if (detour != null || Native.Base == 0) return;

            for (int p = 0; p < 4; p++)
            {
                pads[p] = Native.Alloc(Rva.PadSize);
                states[p] = Native.Alloc(Rva.CmdSize);
            }
            savedPad = Native.Alloc(Rva.PadSize);
            savedCmd = Native.Alloc(Rva.CmdSize);
            buildPad = (PadBuildFn)Marshal.GetDelegateForFunctionPointer(Native.At(Rva.BuildPad), typeof(PadBuildFn));

            hook = Hook;
            detour = new MonoMod.RuntimeDetour.NativeDetour(Native.At(Rva.BattleCommands), Marshal.GetFunctionPointerForDelegate(hook));
            original = detour.GenerateTrampoline<BattleCmdFn>();
            Plugin.Log.LogMessage("Co-op battle input hooked.");
        }

        // Command block: +0 held, +4 newly pressed, +8 raw pad bits (the rest is the combo history).
        const int CmdButtons = 0xC;

        // Runs once per battle frame, possibly on the game's own thread: no Unity API in here.
        static void Hook(IntPtr padHeld)
        {
            original(padHeld);   // slot 0 / player 1, exactly as the game does it
            if (inHook) return;
            inHook = true;
            try
            {
                IntPtr obj = Marshal.ReadIntPtr(Native.At(Rva.InputObject));
                if (obj == IntPtr.Zero) return;
                int players = Marshal.ReadInt32(obj, 0x10);

                // Battle menu open (paused). Like the PS1, the menu belongs to whoever opened it (Rva.MenuOpener): its
                // icon row reads the opener's command block (FUN_10F770: +8 pressed buttons/D-pad, +0 held directions),
                // while the submenus read player 1's pad. Players 2-4's own pads lack the button latches menus need
                // (those helpers run for player 1 only - the spinning-camera fix), but player 1 meanwhile listens to
                // every device (Players.ShareWithPlayer1), so the opener's block gets player 1's: anyone's controller
                // works, with clean presses. No pad swapping while the menu is up; the other blocks stay empty.
                if ((Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) != 0)
                {
                    CoopCamera.Paused();
                    int opener = Marshal.ReadByte(Native.At(Rva.MenuOpener)) - 1;   // -1: paused with Start, no owner
                    for (int p = 1; p < 4; p++)
                    {
                        if (p == opener) Native.Copy(Battle.CmdBlock(0), Battle.CmdBlock(p), CmdButtons);
                        else Native.Clear(Battle.CmdBlock(p), CmdButtons);
                    }
                    return;
                }

                ControlledSlots();
                if (Features.Camera)
                {
                    CoopCamera.Install();   // once
                    humans[0] = true;       // player 1's character
                    for (int p = 1; p < 4; p++) humans[p] = wanted[p] && p < players;
                    CoopCamera.Update(humans);
                }
                IntPtr pad = Native.At(Rva.Pad), cmd0 = Battle.CmdBlock(0);
                for (int p = 1; p < players && p < 4; p++)
                {
                    if (!wanted[p]) continue;
                    BuildPad(obj, p);

                    // Run the command builder on player p's pad and previous state, then put slot 0's back.
                    Native.Copy(pad, savedPad, Rva.PadSize);
                    Native.Copy(cmd0, savedCmd, Rva.CmdSize);
                    Native.Copy(pads[p], pad, Rva.PadSize);
                    Native.Copy(states[p], cmd0, Rva.CmdSize);
                    original(pads[p]);   // reads held/pressed via the pointer, the rest via the global pad
                    Native.Copy(cmd0, states[p], Rva.CmdSize);
                    Native.Copy(cmd0, Battle.CmdBlock(p), Rva.CmdSize);
                    Native.Copy(savedCmd, cmd0, Rva.CmdSize);
                    Native.Copy(savedPad, pad, Rva.PadSize);
#if COOP_DEBUG
                    ResearchTools.OnBattleSlot(p, pads[p], states[p]);
#endif
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Co-op battle input: " + e); }
            finally { inHook = false; }
        }

        // Party slots whose actor is player-controlled this frame. With LiveModeSync, slots 2-4 also follow the
        // menu setting mid-battle (the battle copies modes only at its start) and go Auto while their player has
        // no controller - so a dropped Parsec guest's character keeps fighting on AI.
        static readonly bool[] wanted = new bool[4];   // filled every frame (the hook never runs re-entrantly)
        static readonly bool[] humans = new bool[4];   // slots the co-op camera keeps in view

        static void ControlledSlots()
        {
            for (int i = 0; i < 4; i++) wanted[i] = false;
            IntPtr actors = Battle.Actors();
            if (actors == IntPtr.Zero) return;
            for (int i = 0; i < Rva.ActorCount; i++)
            {
                IntPtr a = Battle.ActorAt(actors, i);
                int slot = Battle.PartySlot(a);
                if (slot < 1) continue;
                byte ctrl = Marshal.ReadByte(a, Actor.Control);
                if (Settings.LiveModeSync.Value && ctrl <= 2)
                {
                    byte menu = Marshal.ReadByte(Native.At(Rva.SlotModes + slot));
                    byte want = Players.HasDevice[slot] && menu <= 2 ? menu : (byte)0;
                    if (want != ctrl)
                    {
                        Marshal.WriteByte(a, Actor.Control, want);
                        if (want == 0) ClearPlayer(slot);   // no stale held buttons when they come back
                        ctrl = want;
                    }
                }
                if (ctrl != 0) wanted[slot] = true;
            }
        }

        static void ClearPlayer(int p)
        {
            Native.Clear(pads[p], Rva.PadSize);
            Native.Clear(states[p], Rva.CmdSize);
            Native.Clear(Battle.CmdBlock(p), Rva.CmdSize);
        }

        // FUN_19DBC0 reads player 1's slice of the input object; point the three slice starts at player p for the call.
        static void BuildPad(IntPtr obj, int p)
        {
            IntPtr remap = Marshal.ReadIntPtr(obj, 0x18), buttons = Marshal.ReadIntPtr(obj, 0x30), sticks = Marshal.ReadIntPtr(obj, 0x48);
            Marshal.WriteIntPtr(obj, 0x18, new IntPtr(remap.ToInt64() + p * 0x1C * 24));
            Marshal.WriteIntPtr(obj, 0x30, new IntPtr(buttons.ToInt64() + p * 0xAE * 20));
            Marshal.WriteIntPtr(obj, 0x48, new IntPtr(sticks.ToInt64() + p * 2 * 20));
            try { buildPad(pads[p], 1, 0); }
            finally
            {
                Marshal.WriteIntPtr(obj, 0x18, remap);
                Marshal.WriteIntPtr(obj, 0x30, buttons);
                Marshal.WriteIntPtr(obj, 0x48, sticks);
            }
        }
    }
}
