// Debug build only: [Debug] UnlockRoster = true fills the empty party list entries (Rva.PartyIds, six shorts) with the
// playable characters not in it yet (1 Reid, 2 Farah, 3 Keele, 4 Meredy, 5 Ras, 6 Chat), for testing six-character
// battles (Six Person Battle Expansion) on an early save. Their records already exist in the save block. In memory
// only: the save file changes only if the game is saved afterwards.
// [Debug] HealParty = true refills every character's HP and TP while no battle runs, so test battles start fresh.
// Character records (game 2.1.0): 0xD0 bytes per character id from 1, starting 0x8F0 before the party list; +0 id,
// +2 level, +4 EXP, +8 EXP to the next level, +0x34 max HP, +0x38 HP, +0x3A max TP, +0x3E TP (found by the HP/TP
// shown in the menu), +0x7A the learned artes: a zero-ended list of arte ids, 0x20 bytes (+0x9A is the next field).
// [Debug] MaxLevels = true gives every character the EXP cap (20,000,000: level 250): the next battle won runs the game's own
// level-ups (stats included). [Debug] UnlockArtes = true adds each character's artes to their list: Reid 0x01-0x18,
// Farah 0x19-0x2A, the Craymel artes (0x36-0x5F, in Artes Renamer's order up to Distortion; 0x60-0x65 are Max's artes,
// then Spark Wave 0x70 and Prism Sword 0x71; Fear Flare, Maelstrom, Summon Demon and Shining Flare not found) split between Keele (water, wind, fire, then
// light, dark and the top spells) and Meredy (healing and support, then earth, ice, lightning), Ras 0x66-0x69, Chat
// 0x6A-0x6F (Six Person counts seats 5-6 itself, so Chat's may not show). [Debug] ExtraArtes adds hex ids per
// character ("1=70,71;4=37") for artes outside those ranges. All in memory: saving the game afterwards keeps it.
#if COOP_DEBUG
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using BepInEx.Configuration;

namespace EterniaCoop
{
    static class RosterUnlock
    {
        const int Slots = 6, Playable = 6;
        static int PartyList { get { return Rva.PartyIds; } }
        static ConfigEntry<bool> unlock, heal, maxLevels, artes;
        static ConfigEntry<string> extraArtes;
        const int RecordsFromParty = -0x8F0, RecordSize = 0xD0, ArteList = 0x7A, ArteListSize = 0x20, LevelCap = 250;
        const int MaxExp = 20000000;   // the game caps EXP here (level 250)
        static string lastExtra;
        static byte[][] wantedArtes;

        public static void Bind(ConfigFile c)
        {
            unlock = c.Bind("Debug", "UnlockRoster", false, "Add every playable character to the party list (in memory). Debug build only.");
            heal = c.Bind("Debug", "HealParty", false, "Outside battle, refill every character's HP and TP (in memory). Debug build only.");
            maxLevels = c.Bind("Debug", "MaxLevels", false, "Give every character EXP for the level cap; the next battle won levels them up (in memory - saving keeps it). Debug build only.");
            artes = c.Bind("Debug", "UnlockArtes", false, "Teach every character all their artes and spells (in memory - saving keeps it). Debug build only.");
            extraArtes = c.Bind("Debug", "ExtraArtes", "", "With UnlockArtes: more hex arte ids per character id, e.g. 1=70,71;4=37 (1 Reid, 2 Farah, 3 Keele, 4 Meredy, 5 Ras, 6 Chat). An id that is no arte crashes the Skills screen (0x7E/0x7F did).");
        }

        public static void Update()
        {
            Heal();
            Records();
            if (unlock == null || !unlock.Value || Native.Base == 0) return;
            if (!InGame()) return;
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

        // A save is loaded: the title screen also has a party list (Reid), but area 0.
        static bool InGame()
        {
            return Marshal.ReadInt16(Native.At(PartyList)) != 0 && Marshal.ReadInt32(Native.At(Rva.Area)) != 0;
        }

        static int Record(int id)
        {
            int rec = PartyList + RecordsFromParty + (id - 1) * RecordSize;
            return Marshal.ReadInt16(Native.At(rec)) == id ? rec : 0;   // 0: not the layout found on 2.1.0
        }

        static void Heal()
        {
            if (heal == null || !heal.Value || Native.Base == 0 || CharacterChoice.InBattle) return;
            if (!InGame()) return;
            for (int id = 1; id <= Playable; id++)
            {
                int rec = Record(id);
                if (rec == 0) return;
                Marshal.WriteInt16(Native.At(rec + 0x38), Marshal.ReadInt16(Native.At(rec + 0x34)));
                Marshal.WriteInt16(Native.At(rec + 0x3E), Marshal.ReadInt16(Native.At(rec + 0x3A)));
            }
        }

        static void Records()
        {
            if (maxLevels == null || Native.Base == 0 || CharacterChoice.InBattle) return;
            if (!maxLevels.Value && !artes.Value) return;
            if (!InGame()) return;
            if (artes.Value && (wantedArtes == null || extraArtes.Value != lastExtra)) BuildArtes();
            for (int id = 1; id <= Playable; id++)
            {
                int rec = Record(id);
                if (rec == 0) return;
                if (maxLevels.Value && Marshal.ReadInt16(Native.At(rec + 2)) < LevelCap && Marshal.ReadInt32(Native.At(rec + 4)) < MaxExp)
                {
                    Marshal.WriteInt32(Native.At(rec + 4), MaxExp);
                    Marshal.WriteInt32(Native.At(rec + 8), 1);
                    Plugin.Log.LogMessage("Max levels: character " + id + " levels up after the next battle won.");
                }
                if (artes.Value) WriteArtes(id, rec + ArteList);
            }
        }

        // Adds the wanted artes after the ones the character knows (kept first, in their order). Nothing is written
        // once they're all there.
        static void WriteArtes(int id, int list)
        {
            var merged = new List<byte>();
            for (int i = 0; i < ArteListSize; i++)
            {
                byte b = Marshal.ReadByte(Native.At(list + i));
                if (b == 0) break;
                if (!merged.Contains(b)) merged.Add(b);
            }
            int known = merged.Count;
            foreach (byte b in wantedArtes[id])
                if (!merged.Contains(b) && merged.Count < ArteListSize - 1) merged.Add(b);   // keep the zero at the end
            if (merged.Count == known) return;
            for (int i = 0; i < ArteListSize; i++) Marshal.WriteByte(Native.At(list + i), i < merged.Count ? merged[i] : (byte)0);
            Plugin.Log.LogMessage("Unlock artes: character " + id + " now knows " + merged.Count + " artes.");
        }

        static void BuildArtes()
        {
            lastExtra = extraArtes.Value;
            var lists = new List<byte>[Playable + 1];
            for (int i = 0; i <= Playable; i++) lists[i] = new List<byte>();
            const int craymel = 0x36;   // Heal; then Artes Renamer's [Craymel Artes] order (Nurse 0x37)
            AddRange(lists[1], 0x01, 0x18);
            AddRange(lists[2], 0x19, 0x2A);
            AddRange(lists[3], craymel + 16, craymel + 24);   // Aqua Edge .. Explode
            AddRange(lists[3], craymel + 35, craymel + 41);   // Ray .. Distortion (0x60-0x65 are Max's artes)
            AddRange(lists[3], 0x70, 0x71);                   // Spark Wave, Prism Sword (0x72-0x7D: dormant Craymel artes)
            AddRange(lists[4], craymel + 0, craymel + 15);    // Heal .. Delay
            AddRange(lists[4], craymel + 25, craymel + 34);   // Grave .. Indignation
            AddRange(lists[5], 0x66, 0x69);
            AddRange(lists[6], 0x6A, 0x6F);
            foreach (string part in (extraArtes.Value ?? "").Split(';'))
            {
                string[] kv = part.Split('=');
                int id;
                if (kv.Length != 2 || !int.TryParse(kv[0].Trim(), out id) || id < 1 || id > Playable) continue;
                foreach (string h in kv[1].Split(','))
                {
                    int v;
                    if (int.TryParse(h.Trim(), NumberStyles.HexNumber, null, out v) && v > 0 && v < 256) lists[id].Add((byte)v);
                }
            }
            wantedArtes = new byte[Playable + 1][];
            for (int i = 0; i <= Playable; i++) wantedArtes[i] = lists[i].ToArray();
        }

        static void AddRange(List<byte> list, int from, int to)
        {
            for (int v = from; v <= to; v++) list.Add((byte)v);
        }
    }
}
#endif
