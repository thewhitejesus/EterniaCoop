// Debug build only: [Debug] UnlockRoster = true fills the empty party list entries (0x11CAD10, six shorts) with the
// playable characters not in it yet (1 Reid, 2 Farah, 3 Keele, 4 Meredy, 5 Ras, 6 Chat), for testing six-character
// battles (Six Person Battle Expansion) on an early save. Their records already exist in the save block. In memory
// only: the save file changes only if the game is saved afterwards.
#if COOP_DEBUG
using System.Runtime.InteropServices;
using BepInEx.Configuration;

namespace EterniaCoop
{
    static class RosterUnlock
    {
        const int PartyList = 0x11CAD10, Slots = 6, Playable = 6;
        static ConfigEntry<bool> unlock;

        public static void Bind(ConfigFile c)
        {
            unlock = c.Bind("Debug", "UnlockRoster", false, "Add every playable character to the party list (in memory). Debug build only.");
        }

        public static void Update()
        {
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
    }
}
#endif
