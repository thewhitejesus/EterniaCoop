// Battle actor helpers shared by the battle input and rumble hooks.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class Battle
    {
        // The actor array (13 slots, party first then enemies), or Zero outside battle.
        public static IntPtr Actors() { return Marshal.ReadIntPtr(Native.At(Rva.ActorArray)); }

        public static IntPtr ActorAt(IntPtr actors, int i) { return new IntPtr(actors.ToInt64() + i * Rva.ActorSize); }

        // Party slot (0-3) of a party character, from which command block it reads; -1 for anything else.
        public static int PartySlot(IntPtr actor)
        {
            if (actor == IntPtr.Zero || Marshal.ReadInt16(actor, Actor.Kind) != 1 || Marshal.ReadByte(actor, Actor.Side) != 0) return -1;
            long block = Marshal.ReadIntPtr(actor, Actor.CmdBlock).ToInt64() - CmdBase();
            if (block < 0 || block % Rva.CmdSize != 0 || block / Rva.CmdSize >= 4) return -1;
            return (int)(block / Rva.CmdSize);
        }

        public static IntPtr CmdBlock(int slot) { return new IntPtr(CmdBase() + slot * Rva.CmdSize); }

        // Where the command blocks are this battle: Rva.CmdBlocks in the game, but a mod may move them (Six Person
        // Battle Expansion keeps six blocks, 0x20 apart, in its own memory and redirects the game's accesses). The party
        // actors' block pointers (+0xF0, block index at +0x56) say where they really are; worked out once per battle.
        static long cmdBase, cmdBaseActors;
        public static long CmdBase()
        {
            IntPtr actors = Actors();
            long fallback = Native.Base + Rva.CmdBlocks;
            if (actors == IntPtr.Zero) return fallback;
            if (actors.ToInt64() == cmdBaseActors && cmdBase != 0) return cmdBase;
            for (int i = 0; i < Rva.ActorCount; i++)
            {
                IntPtr a = ActorAt(actors, i);
                short kind = Marshal.ReadInt16(a, Actor.Kind);
                long ptr = Marshal.ReadIntPtr(a, Actor.CmdBlock).ToInt64();
                if ((kind != 1 && kind != 2) || Marshal.ReadByte(a, Actor.Side) != 0 || ptr == 0) continue;
                long b = ptr - Marshal.ReadByte(a, Actor.Block) * Rva.CmdSize;
                if (b != fallback && cmdBaseActors != actors.ToInt64())
                    Plugin.Log.LogMessage("Battle command blocks moved by another mod: using " + b.ToString("X") + ".");
                cmdBase = b;
                cmdBaseActors = actors.ToInt64();
                return b;
            }
            return fallback;
        }

        // In a battle with its menu open (the battle is paused). Holding the target button pauses the battle the same
        // way, but that isn't the menu. Rva.BattleFlags is checked by Features.CoopBattle.
        public static bool MenuOpen()
        {
            return Features.CoopBattle && Native.Base != 0 && Actors() != IntPtr.Zero &&
                (Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) != 0 && TargetingBlock() < 0;
        }

        // The command block (0-3) of the party member choosing a target right now (holding the target button,
        // outside the battle menu), or -1.
        public static int TargetingBlock()
        {
            IntPtr actors = Actors();
            int t = Marshal.ReadByte(Native.At(Rva.TargetActor));
            if (actors == IntPtr.Zero || t == 0 || t > Rva.ActorCount || Marshal.ReadByte(Native.At(Rva.MenuOpener)) != 0) return -1;
            int block = Marshal.ReadByte(ActorAt(actors, t - 1), Actor.Block);
            return block < 4 ? block : -1;
        }
    }
}
