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

        // In a battle with its menu open (the battle is paused). Rva.BattleFlags is checked by Features.CoopBattle.
        public static bool MenuOpen()
        {
            return Features.CoopBattle && Native.Base != 0 && Actors() != IntPtr.Zero &&
                (Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) != 0;
        }
    }
}
