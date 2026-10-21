// Party slot control modes (menu/save side, Rva.SlotModes + slot: 0 Auto, 1 Semi-Auto).
// Slot N (2-4) follows whether player N has a controller. Applied only when that changes or the party changes
// (loading a save, someone joining), so a mode picked in the menu in between is left alone.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class SlotModes
    {
        static readonly bool[] applied = new bool[4];
        static long lastParty = -1;

        public static void Update(int joinMode)
        {
            if (Native.Base == 0) return;
            long party = Marshal.ReadInt64(Native.At(Rva.PartyIds));   // 4 member ids, 2 bytes each
            bool partyChanged = party != lastParty;
            lastParty = party;
            for (int slot = 1; slot < 4; slot++)
            {
                bool member = ((party >> (slot * 16)) & 0xFFFF) != 0;
                bool human = member && Players.HasDevice[slot];
                if (!partyChanged && human == applied[slot]) continue;
                applied[slot] = human;
                if (!member) continue;
                byte mode = (byte)(human ? joinMode : 0);
                IntPtr p = Native.At(Rva.SlotModes + slot);
                if (Marshal.ReadByte(p) == mode) continue;
                Marshal.WriteByte(p, mode);
                Plugin.Log.LogMessage("Party slot " + (slot + 1) + " -> " + (mode == 0 ? "Auto" : mode == 1 ? "Semi-Auto" : "mode " + mode) +
                    (human ? " (player " + (slot + 1) + " has a controller)" : " (no controller)"));
            }
        }
    }
}
