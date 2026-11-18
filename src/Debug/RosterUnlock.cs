// Debug build only: [Debug] UnlockRoster = true fills the empty party list entries (Rva.PartyIds, six shorts) with the
// playable characters not in it yet (1 Reid, 2 Farah, 3 Keele, 4 Meredy, 5 Ras, 6 Chat), for testing six-character
// battles (Six Person Battle Expansion) on an early save. Their records already exist in the save block. In memory
// only: the save file changes only if the game is saved afterwards.
// [Debug] HealParty = true refills every character's HP and TP while no battle runs, so test battles start fresh.
// Character records (game 2.1.0): 0xD0 bytes per character id from 1, starting 0x8F0 before the party list; +0 id,
// +2 level, +0x34 max HP, +0x38 HP, +0x3A max TP, +0x3E TP (found by the HP/TP shown in the menu).
#if COOP_DEBUG
using System.Runtime.InteropServices;
using BepInEx.Configuration;

namespace EterniaCoop
{
    static class RosterUnlock
    {
        const int Slots = 6, Playable = 6;
        static int PartyList { get { return Rva.PartyIds; } }
        static ConfigEntry<bool> unlock, heal;
        const int RecordsFromParty = -0x8F0, RecordSize = 0xD0;

        public static void Bind(ConfigFile c)
        {
            unlock = c.Bind("Debug", "UnlockRoster", false, "Add every playable character to the party list (in memory). Debug build only.");
            heal = c.Bind("Debug", "HealParty", false, "Outside battle, refill every character's HP and TP (in memory). Debug build only.");
        }

        public static void Update()
        {
            Heal();
            if (unlock == null || !unlock.Value || Native.Base == 0) return;
            if (Marshal.ReadInt16(Native.At(PartyList)) == 0) return;   // no save loaded
            for (int id = 1; id <= Playable; id++)
            {
                int free = -1;
                bool have = false;
                for (int s = 0; s < Slots; s++)
                {
                    short v = Marshal.ReadInt16(Native.At(PartyList + 2 * s));
                    if (v == id) have = true;
                    if (v == 0 && free < 0) free = s;
                }
                if (have || free < 0) continue;
                Marshal.WriteInt16(Native.At(PartyList + 2 * free), (short)id);
                Plugin.Log.LogMessage("Roster unlock: character " + id + " added to party slot " + (free + 1) + ".");
            }
        }

        static void Heal()
        {
            if (heal == null || !heal.Value || Native.Base == 0 || CharacterChoice.InBattle) return;
            if (Marshal.ReadInt16(Native.At(PartyList)) == 0) return;   // no save loaded
            for (int id = 1; id <= Playable; id++)
            {
                int rec = PartyList + RecordsFromParty + (id - 1) * RecordSize;
                if (Marshal.ReadInt16(Native.At(rec)) != id) return;   // not the layout found on 2.1.0
                Marshal.WriteInt16(Native.At(rec + 0x38), Marshal.ReadInt16(Native.At(rec + 0x34)));
                Marshal.WriteInt16(Native.At(rec + 0x3E), Marshal.ReadInt16(Native.At(rec + 0x3A)));
            }
        }
    }
}
#endif
