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
            long block = Marshal.ReadIntPtr(actor, Actor.CmdBlock).ToInt64() - (Native.Base + Rva.CmdBlocks);
            if (block < 0 || block % Rva.CmdSize != 0 || block / Rva.CmdSize >= 4) return -1;
            return (int)(block / Rva.CmdSize);
        }

        public static IntPtr CmdBlock(int slot) { return Native.At(Rva.CmdBlocks + slot * Rva.CmdSize); }

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
