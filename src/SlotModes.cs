// Party slot control modes (menu/save side, Rva.SlotModes + slot: 0 Auto, 1 Semi-Auto, 2 Manual where the game
// allows it). Slot N (2-4) follows whether player N has a controller: gaining one sets JoinMode, losing one sets
// Auto. A mode the player picked is never overwritten: a slot that's already Semi-Auto or Manual keeps it, and a slot
// that went Auto because its controller dropped (wireless sleep, a Parsec hiccup) gets its mode back when the
// controller returns. Applied only when that changes or the party changes (loading a save, someone joining).
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class SlotModes
    {
        static readonly bool[] applied = new bool[4];
        static readonly byte[] remembered = new byte[4];   // a slot's mode before its controller dropped (0 = none)
        static readonly int[] member = new int[4];          // the character in each slot, to forget modes when it changes
        static long lastParty = -1;

        public static void Update(int joinMode)
        {
            if (Native.Base == 0) return;
            long party = Marshal.ReadInt64(Native.At(Rva.PartyIds));   // 4 member ids, 2 bytes each
            bool partyChanged = party != lastParty;
            lastParty = party;
            for (int slot = 1; slot < 4; slot++)
            {
                int id = (int)((party >> (slot * 16)) & 0xFFFF);
                if (id != member[slot]) { member[slot] = id; remembered[slot] = 0; }
                bool human = id != 0 && Players.HasDevice[slot];
                if (!partyChanged && human == applied[slot]) continue;
                applied[slot] = human;
                if (id == 0) continue;
                IntPtr p = Native.At(Rva.SlotModes + slot);
                byte now = Marshal.ReadByte(p), mode;
                if (human)
                {
                    if (now != 0) continue;   // already Semi-Auto / Manual: the player's choice stands
                    mode = remembered[slot] != 0 ? remembered[slot] : (byte)joinMode;
                }
                else
                {
                    if (now != 0) remembered[slot] = now;   // back to this when the controller returns
                    mode = 0;
                }
                if (now == mode) continue;
                Marshal.WriteByte(p, mode);
                Plugin.Log.LogMessage("Party slot " + (slot + 1) + " -> " + (mode == 0 ? "Auto" : mode == 1 ? "Semi-Auto" : mode == 2 ? "Manual" : "mode " + mode) +
                    (human ? " (player " + (slot + 1) + " has a controller)" : " (no controller)"));
            }
        }
    }
}
