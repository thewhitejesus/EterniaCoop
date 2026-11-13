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

        static BattleCmdFn original;
        static PadBuildFn buildPad;
        static bool installed;
        static readonly IntPtr[] pads = new IntPtr[4], states = new IntPtr[4];
        static IntPtr savedPad, savedCmd;
        static bool inHook;

        // Features.CoopBattle has checked both functions and the addresses this uses.
        public static void Install()
        {
            if (installed || Native.Base == 0) return;
            installed = true;

            for (int p = 0; p < 4; p++)
            {
                pads[p] = Native.Alloc(Rva.PadSize);
                states[p] = Native.Alloc(Rva.CmdSize);
            }
            savedPad = Native.Alloc(Rva.PadSize);
            savedCmd = Native.Alloc(Rva.CmdSize);
            buildPad = (PadBuildFn)Marshal.GetDelegateForFunctionPointer(Native.At(Rva.BuildPad), typeof(PadBuildFn));

            original = NativeHook.Install<BattleCmdFn>(Rva.BattleCommands, Hook, "Co-op battle input");
            Plugin.Log.LogMessage("Co-op battle input hooked.");
            if (Features.Camera) BattleGround.Install();   // if Features.Ground
        }

        // The two patches inside the battle function itself (FUN_D2C80: the target switch call, the camera's position
        // read) go in at the first battle frame, the rest of the battle hooks when a save is loaded (Plugin): Six Person
        // Battle Expansion checks these functions' bytes and switches itself off if anything there differs.
        static bool battlePatched;

        static void PatchBattleFunction()
        {
            battlePatched = true;
            InstallTargetSwitch();
            if (targetStub != IntPtr.Zero) Plugin.Log.LogMessage("Target switching for players 2-4 installed.");
            if (Features.Camera) CoopCamera.Install();
        }

        // Command block: +0 held, +4 newly pressed, +8 raw pad bits (the rest is the combo history).
        const int CmdButtons = 0xC;

        // Runs once per battle frame, possibly on the game's own thread: no Unity API in here.
        // Battle frames seen (paused ones too): tells a running battle from a leftover actor list after one.
        public static volatile int Frames;

        static void Hook(IntPtr padHeld)
        {
            original(padHeld);   // slot 0 / player 1, exactly as the game does it
            Frames++;
            if (inHook) return;
            inHook = true;
            try
            {
                if (!battlePatched) PatchBattleFunction();
                IntPtr obj = Marshal.ReadIntPtr(Native.At(Rva.InputObject));
                if (obj == IntPtr.Zero) return;
                int players = Marshal.ReadInt32(obj, 0x10);

                // Battle menu open (paused). Like the PS1, the menu belongs to whoever opened it (Rva.MenuOpener): its
                // icon row reads the opener's command block (FUN_10F770: +8 pressed buttons/D-pad, +0 held directions),
                // while the submenus read player 1's pad. Players 2-4's own pads lack the button latches menus need
                // (those helpers run for player 1 only - the spinning-camera fix), but player 1 meanwhile listens to
                // the opener's devices only (Players.ShareWithPlayer1 / MenuOwner; everyone's when paused with Start),
                // so the opener's block gets player 1's, with clean presses. No pad swapping while the menu is up; the
                // other blocks stay empty.
                if ((Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) != 0)
                {
                    CoopCamera.Paused();
                    // Holding the target button pauses the battle too. When it's player 2-4's, their input keeps
                    // running (or the game sees the button released and ends it) and the target switch reads their pad.
                    int targeting = Battle.TargetingBlock();
                    int targetInput = targeting > 0 ? InputOf(targeting) : -1;
                    bool theirs = targetInput > 0 && targetInput < players && wanted[targeting];
                    int opener = Marshal.ReadByte(Native.At(Rva.MenuOpener)) - 1;   // -1: paused with Start, no owner
                    for (int b = 1; b < Blocks(); b++)
                    {
                        if (theirs && b == targeting) RunPlayer(obj, targetInput, b);
                        else if (b == opener) Native.Copy(Battle.CmdBlock(0), Battle.CmdBlock(b), CmdButtons);
                        else Native.Clear(Battle.CmdBlock(b), CmdButtons);
                    }
                    SetTargetPad(theirs ? pads[targetInput] : IntPtr.Zero);
                    return;
                }
                SetTargetPad(IntPtr.Zero);

                ControlledSlots();
                Radar.Mark(wanted, players);
                if (Features.Camera)
                {
                    humans[0] = true;       // player 1's character
                    humans[0] = !CharacterChoice.LeaderAuto;
                    for (int b = 1; b < humans.Length; b++) humans[b] = wanted[b] && (b >= 4 || b < players);
                    CoopCamera.Update(humans);
                }
                for (int b = 1; b < Blocks(); b++)
                {
                    int p = InputOf(b);
                    if (wanted[b] && p > 0 && p < players && p < 4) RunPlayer(obj, p, b);
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Co-op battle input: " + e); }
            finally { inHook = false; }
        }

        // Command blocks the hook fills: 1-3, and Six Person's 4-5 while character choice sends someone there.
        static int Blocks()
        {
            if (CharacterChoice.Routed) return 6;
            return 4;
        }

        // The input player whose commands go into command block b: b itself, or another with character choice.
        static int InputOf(int b)
        {
            return CharacterChoice.InputOfBlock(b);
        }

        // Builds player p's pad and runs the command builder on it with p's previous state, into command block `block`
        // (normally p's own); slot 0's pad and block are put back.
        static void RunPlayer(IntPtr obj, int p, int block)
        {
            IntPtr pad = Native.At(Rva.Pad), cmd0 = Battle.CmdBlock(0);
            BuildPad(obj, p);
            Native.Copy(pad, savedPad, Rva.PadSize);
            Native.Copy(cmd0, savedCmd, Rva.CmdSize);
            Native.Copy(pads[p], pad, Rva.PadSize);
            Native.Copy(states[p], cmd0, Rva.CmdSize);
            original(pads[p]);   // reads held/pressed via the pointer, the rest via the global pad
            Native.Copy(cmd0, states[p], Rva.CmdSize);
            Native.Copy(cmd0, Battle.CmdBlock(block), Rva.CmdSize);
            Native.Copy(savedCmd, cmd0, Rva.CmdSize);
            Native.Copy(savedPad, pad, Rva.PadSize);
#if COOP_DEBUG
            ResearchTools.OnBattleSlot(p, pads[p], states[p]);
#endif
        }

        // ---- target switching for players 2-4 ----
        // The battle's target selection calls FUN_113EA0(actor) (Sites "TargetSwitchCall"), which reads left/right
        // from the one PSP pad - player 1's. That call goes through a small stub: while a player 2-4 is choosing a
        // target (SetTargetPad), it swaps their pad into the PSP pad for the call and puts player 1's back after.
        static IntPtr targetStub, targetOn, targetSrc;

        static void SetTargetPad(IntPtr pad)
        {
            if (targetStub == IntPtr.Zero) return;
            Marshal.WriteIntPtr(targetSrc, pad);
            Marshal.WriteByte(targetOn, (byte)(pad == IntPtr.Zero ? 0 : 1));
        }

        static void InstallTargetSwitch()
        {
            int site = Sites.Rva("TargetSwitchCall", 0);
            if (Marshal.ReadByte(Native.At(site)) != 0xE8) { Plugin.Log.LogError("Target switching for players 2-4: unexpected code"); return; }
            long fn = Native.Base + site + 5 + Marshal.ReadInt32(Native.At(site + 1));   // FUN_113EA0
            IntPtr region = Native.AllocNear(0x2000);
            if (region == IntPtr.Zero) { Plugin.Log.LogError("Target switching for players 2-4: no memory near the game module"); return; }
            long S = region.ToInt64(), padAbs = Native.Base + Rva.Pad;
            // The stub's data sits on its own page, away from the code: it is written every frame.
            targetOn = new IntPtr(S + 0x1000);
            targetSrc = new IntPtr(S + 0x1008);
            long saved = S + 0x1100;

            var c = new System.Collections.Generic.List<byte>();
            Func<long, int, byte[]> rel = (target, end) => BitConverter.GetBytes((int)(target - (S + end)));
            Action<long, long> copy = (to, from) =>   // lea rdi, [to] ; lea rsi, [from] ; mov ecx, PadSize ; rep movsb
            {
                c.AddRange(new byte[] { 0x48, 0x8D, 0x3D }); c.AddRange(rel(to, c.Count + 4));
                c.AddRange(new byte[] { 0x48, 0x8D, 0x35 }); c.AddRange(rel(from, c.Count + 4));
                c.Add(0xB9); c.AddRange(BitConverter.GetBytes(Rva.PadSize));
                c.AddRange(new byte[] { 0xF3, 0xA4 });
            };
            // cmp byte [rip+on], 0 ; je plain
            c.AddRange(new byte[] { 0x80, 0x3D }); c.AddRange(rel(targetOn.ToInt64(), c.Count + 5)); c.Add(0x00);
            int je = c.Count; c.AddRange(new byte[] { 0x0F, 0x84, 0, 0, 0, 0 });
            // push rsi ; push rdi ; push rbx ; push rcx ; sub rsp, 0x28   (keeps the stack 16-aligned for the call)
            c.AddRange(new byte[] { 0x56, 0x57, 0x53, 0x51, 0x48, 0x83, 0xEC, 0x28 });
            copy(saved, padAbs);                                                       // save player 1's pad
            // mov rsi, [rip+src] ; lea rdi, [pad] ; mov ecx, PadSize ; rep movsb      (their pad in)
            c.AddRange(new byte[] { 0x48, 0x8B, 0x35 }); c.AddRange(rel(targetSrc.ToInt64(), c.Count + 4));
            c.AddRange(new byte[] { 0x48, 0x8D, 0x3D }); c.AddRange(rel(padAbs, c.Count + 4));
            c.Add(0xB9); c.AddRange(BitConverter.GetBytes(Rva.PadSize));
            c.AddRange(new byte[] { 0xF3, 0xA4 });
            // mov rcx, [rsp+0x28] ; call FUN_113EA0 ; mov ebx, eax
            c.AddRange(new byte[] { 0x48, 0x8B, 0x4C, 0x24, 0x28 });
            c.Add(0xE8); c.AddRange(rel(fn, c.Count + 4));
            c.AddRange(new byte[] { 0x89, 0xC3 });
            copy(padAbs, saved);                                                       // player 1's pad back
            // mov eax, ebx ; add rsp, 0x28 ; pop rcx ; pop rbx ; pop rdi ; pop rsi ; ret
            c.AddRange(new byte[] { 0x89, 0xD8, 0x48, 0x83, 0xC4, 0x28, 0x59, 0x5B, 0x5F, 0x5E, 0xC3 });
            int plain = c.Count;
            c.Add(0xE9); c.AddRange(rel(fn, c.Count + 4));                              // plain: jmp FUN_113EA0
            var code = c.ToArray();
            BitConverter.GetBytes(plain - (je + 6)).CopyTo(code, je + 2);
            Marshal.Copy(code, 0, region, code.Length);
            Marshal.WriteByte(targetOn, 0);

            var call = new byte[5];
            call[0] = 0xE8;
            BitConverter.GetBytes((int)(S - (Native.Base + site + 5))).CopyTo(call, 1);
            Native.Write(site, call);
            targetStub = region;
        }

        // Party slots whose actor is player-controlled this frame. With LiveModeSync, slots 2-4 also follow the
        // menu setting mid-battle (the battle copies modes only at its start) and go Auto while their player has
        // no controller - so a dropped Parsec guest's character keeps fighting on AI.
        static readonly bool[] wanted = new bool[6];   // per command block, filled every frame (the hook never runs re-entrantly)
        static readonly bool[] humans = new bool[6];   // command blocks the co-op camera keeps in view

        static void ControlledSlots()
        {
            for (int i = 0; i < wanted.Length; i++) wanted[i] = false;
            IntPtr actors = Battle.Actors();
            if (actors == IntPtr.Zero) return;
            for (int i = 0; i < Rva.ActorCount; i++)
            {
                IntPtr a = Battle.ActorAt(actors, i);
                if (ChosenSlot(a)) continue;
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

        // Character choice: a member plays as human exactly when a person picked it and their input player has a
        // device (their mode in the menu if it isn't Auto, else Semi-Auto); every other member plays on Auto. Six
        // Person's members 5-6 are put back to Auto once nobody plays them. Returns false when the choice isn't routing
        // this battle (the normal rules apply).
        static readonly bool[] forced = new bool[6];
        static volatile int leaderMode = -1;   // the leader's mode before character choice put it on Auto
        static float leaderSince = -1;

        // Main thread, every frame. The battle results copy every actor's mode back to the menu (FUN_84210), so a
        // leader that ended the battle on Auto for character choice would stay on Auto: once the battle is over and
        // that copy has written Auto, put the leader's own mode back. Given up after a minute (results never came).
        public static void LeaderAfterBattle(bool inBattle, float now)
        {
            if (inBattle || leaderMode < 0) { leaderSince = -1; return; }
            if (leaderSince < 0) leaderSince = now;
            IntPtr menu = Native.At(Rva.SlotModes);
            if (Marshal.ReadByte(menu) == 0)
            {
                Marshal.WriteByte(menu, (byte)leaderMode);
                Plugin.Log.LogMessage("Character choice: the leader's mode is back to " + (leaderMode == 1 ? "Semi-Auto" : "mode " + leaderMode) + " after the battle.");
                leaderMode = -1;
            }
            else if (now - leaderSince > 60f) leaderMode = -1;
        }

        static bool ChosenSlot(IntPtr a)
        {
            int block = Battle.BlockOf(a);
            if (!CharacterChoice.Routed)
            {
                if (block >= 4 && forced[block]) { Marshal.WriteByte(a, Actor.Control, 0); forced[block] = false; }
                if (block == 0 && leaderMode >= 0) { Marshal.WriteByte(a, Actor.Control, (byte)leaderMode); leaderMode = -1; }
                return false;
            }
            if (block == 0)
            {
                // Nobody connected plays the leader: Auto until someone does (then its own mode back).
                byte now = Marshal.ReadByte(a, Actor.Control);
                if (CharacterChoice.LeaderAuto && now != 0 && now <= 2) { leaderMode = now; Marshal.WriteByte(a, Actor.Control, 0); }
                else if (!CharacterChoice.LeaderAuto && leaderMode >= 0) { Marshal.WriteByte(a, Actor.Control, (byte)leaderMode); leaderMode = -1; }
                return true;
            }
            if (block < 1) return true;
            byte ctrl = Marshal.ReadByte(a, Actor.Control);
            if (ctrl > 2) return true;
            int p = CharacterChoice.InputOfBlock(block);
            byte want = 0;
            if (p > 0 && Players.HasDevice[p])
            {
                byte menu = block < 4 ? Marshal.ReadByte(Native.At(Rva.SlotModes + block)) : (byte)0;
                want = menu >= 1 && menu <= 2 ? menu : (byte)1;
            }
            if (want != ctrl)
            {
                Marshal.WriteByte(a, Actor.Control, want);
                if (want == 0) { Native.Clear(Battle.CmdBlock(block), Rva.CmdSize); if (p > 0) Native.Clear(states[p], Rva.CmdSize); }
                else if (p > 0) { Native.Clear(pads[p], Rva.PadSize); Native.Clear(states[p], Rva.CmdSize); }
            }
            if (block >= 4) forced[block] = want != 0;
            wanted[block] = want != 0;
            return true;
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
