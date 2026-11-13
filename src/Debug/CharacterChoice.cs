// Debug build only: each person picks the party member they play in battle, like Symphonia's controller setup,
// instead of player N always playing party seat N. With Six Person Battle Expansion the 5th and 6th members can be
// picked too.
//
// The game has four input players; input player N drives party seat N (command block N), and input player 1's block
// is filled by the game itself. So while a battle runs:
//   - a person who picked seat 1-4 gets that seat's input player (Players.Desired hands it their devices);
//   - a person who picked seat 5 or 6 gets an input player whose own seat nobody picked, and CoopBattleInput writes
//     that input player's commands into block 4 or 5 instead of its own;
//   - a seat nobody picked plays on Auto.
// Outside battle nothing moves, so player 1 keeps the field and the menus.
//
// [Debug] PlayerCharacters = "2,1,3,4" means player 1 plays seat 2 and player 2 plays seat 1 (the leader). Four
// different seat numbers 1-6, one per player; anything else is ignored. ChooseCharacters turns it on (off = standard).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EterniaCoop
{
    static class CharacterChoice
    {
        public const int Seats = 6;
        static ConfigEntry<string> setting;
        static ConfigEntry<bool> enabled;
        static string parsed;
        static readonly int[] seatOf = { 0, 1, 2, 3 };   // person -> party seat 0-5

        // Set by Plugin.Update: true while a battle runs.
        public static bool InBattle;

        public static void Bind(ConfigFile c)
        {
            enabled = c.Bind("Debug", "ChooseCharacters", false,
                "Let each player pick the party member they play in battle (PlayerCharacters, also set in the controller " +
                "manager). Off = the standard way: player N plays party slot N. Debug build only.");
            setting = c.Bind("Debug", "PlayerCharacters", "",
                "Party seat (1-6) each player plays in battle, in player order, e.g. 2,1,3,4: player 1 plays seat 2 and " +
                "player 2 plays the leader. Four different seats. Empty = player N plays seat N. Debug build only.");
        }

        public static bool On { get { return enabled != null && enabled.Value; } }

        public static void Toggle() { if (enabled != null) enabled.Value = !enabled.Value; }

        static void Parse()
        {
            string s = setting == null ? "" : setting.Value ?? "";
            if (s == parsed) return;
            parsed = s;
            for (int p = 0; p < 4; p++) seatOf[p] = p;
            if (s.Trim() == "") return;
            var parts = s.Split(',');
            var seen = new bool[Seats];
            var seats = new int[4];
            bool ok = parts.Length == 4;
            for (int p = 0; ok && p < 4; p++)
            {
                int n;
                ok = int.TryParse(parts[p].Trim(), out n) && n >= 1 && n <= Seats && !seen[n - 1];
                if (ok) { seen[n - 1] = true; seats[p] = n - 1; }
            }
            if (!ok) { Plugin.Log.LogWarning("PlayerCharacters \"" + s + "\" ignored: give four different seat numbers 1-6 (e.g. 2,1,3,4)."); return; }
            for (int p = 0; p < 4; p++) seatOf[p] = seats[p];
            var sb = new System.Text.StringBuilder("Character choice:");
            for (int p = 0; p < 4; p++) sb.Append(" P" + (p + 1) + " -> seat " + (seatOf[p] + 1));
            Plugin.Log.LogMessage(sb.ToString());
        }

        static void Save()
        {
            var sb = new System.Text.StringBuilder();
            for (int p = 0; p < 4; p++) sb.Append(p == 0 ? "" : ",").Append(seatOf[p] + 1);
            setting.Value = sb.ToString();
            Parse();
        }

        // The person (0-3) who picked `seat`, or -1. With the choice off: the standard (person N plays seat N).
        public static int PersonOfSeat(int seat)
        {
            Parse();
            if (!On) return seat >= 0 && seat < 4 ? seat : -1;
            for (int p = 0; p < 4; p++) if (seatOf[p] == seat) return p;
            return -1;
        }

        // Controller manager: `person` now plays `seat`; whoever played it takes that person's old seat.
        public static void SetSeatPerson(int seat, int person)
        {
            Parse();
            if (setting == null || seat < 0 || seat >= Seats || person < 0 || person > 3) return;
            int other = PersonOfSeat(seat), old = seatOf[person];
            seatOf[person] = seat;
            if (other >= 0 && other != person) seatOf[other] = old;
            Save();
        }

        // Whether the leader's person has a device (otherwise the choice waits and everyone plays their usual seat).
        public static bool LeaderReady() { int p = PersonOfSeat(0); return p >= 0 && Players.PlayerHasDevice(p); }

        // ---- routing (worked out on the main thread by Apply, read by the battle hook) ----
        // Arrays are replaced whole, never changed in place, so the battle thread always sees a consistent set.
        static readonly int[] StdInputOfBlock = { 0, 1, 2, 3, -1, -1 }, StdPersonOfBlock = { 0, 1, 2, 3, -1, -1 };
        static volatile int[] inputOfBlock = StdInputOfBlock, personOfBlock = StdPersonOfBlock;
        static volatile bool routed;
        // This battle the leader has no connected player and fights on Auto.
        public static volatile bool LeaderAuto;

        // Routing differs from the standard this battle.
        public static bool Routed { get { return routed; } }

        // The input player (0-3) whose commands go into command block `block` (0-5), or -1 (that member plays on Auto).
        public static int InputOfBlock(int block) { var a = inputOfBlock; return block >= 0 && block < a.Length ? a[block] : -1; }

        // The person (0-3) playing command block `block`, or -1: their number and colour on the radar.
        public static int PersonOfBlock(int block) { var a = personOfBlock; return block >= 0 && block < a.Length ? a[block] : -1; }

        // Rearranges per-person device lists into per-input-player ones (see the top). Called by Players.Desired.
        public static List<InputDevice>[] Apply(List<InputDevice>[] byPerson)
        {
            Parse();
            if (!On || !InBattle) { Standard(); return byPerson; }
            // Nobody connected plays the leader: it fights on Auto (as the game's own F4 allows) and input player 1, which
            // drives it, listens to nobody (Players.ShouldBlank) except while the battle menu needs it.
            int leader = PersonOfSeat(0);
            bool leaderAuto = leader < 0 || byPerson[leader].Count == 0;
            var byInput = new List<InputDevice>[4];
            for (int i = 0; i < 4; i++) byInput[i] = new List<InputDevice>();
            var input = new int[] { -1, -1, -1, -1, -1, -1 };
            var person = new int[] { -1, -1, -1, -1, -1, -1 };
            var used = new bool[4];
            used[0] = true;   // input player 1 only ever drives the leader (the game fills its block itself)
            for (int p = 0; p < 4; p++)
            {
                int s = seatOf[p];
                if (s >= 4 || (s == 0 && leaderAuto)) continue;
                byInput[s] = byPerson[p]; used[s] = true; input[s] = s; person[s] = p;
            }
            if (leaderAuto) input[0] = 0;
            for (int p = 0; p < 4; p++)
            {
                int s = seatOf[p];
                if (s < 4) continue;
                int u = 1;
                while (u < 4 && used[u]) u++;
                if (u == 4) continue;   // can't happen: four people, four input players
                byInput[u] = byPerson[p]; used[u] = true; input[s] = u; person[s] = p;
            }
            inputOfBlock = input;
            personOfBlock = person;
            LeaderAuto = leaderAuto;
            routed = true;
            var sb = new System.Text.StringBuilder("on:");
            if (leaderAuto) sb.Append(" leader on Auto;");
            for (int b = 0; b < Seats; b++) if (person[b] >= 0) sb.Append(" seat " + (b + 1) + " = P" + (person[b] + 1) + " via input " + (input[b] + 1));
            Report(sb.ToString());
            return byInput;
        }

        static string lastRouting = "";

        static void Report(string routing)
        {
            if (routing == lastRouting) return;
            lastRouting = routing;
            Plugin.Log.LogMessage("Character choice routing: " + routing);
        }

        static void Standard()
        {
            Report("standard (" + (!On ? "off" : "no battle") + ")");
            LeaderAuto = false;
            inputOfBlock = StdInputOfBlock;
            personOfBlock = StdPersonOfBlock;
            routed = false;
        }

        // ---- the party ----
        // 1 Reid, 2 Farah, 3 Keele, 4 Meredy, 5 Ras, 6 Chat (RosterUnlock's list). Others (guests, add-on characters)
        // show by id until we know theirs.
        static readonly string[] Names = { null, "Reid", "Farah", "Keele", "Meredy", "Ras", "Chat" };

        // The member id in each seat (0 = empty), read live: Six Person's six-member list when it has one, else the
        // game's four.
        public static int[] Party()
        {
            var ids = new int[Seats];
            if (Native.Base == 0) return ids;
            long six = SixPersonList();
            if (six != 0 && Marshal.ReadInt16(new IntPtr(six)) <= 0) six = 0;   // not filled in yet: the game's list
            if (six != 0) for (int s = 0; s < Seats; s++) ids[s] = Marshal.ReadInt16(new IntPtr(six + 2 * s));
            else for (int s = 0; s < 4; s++) ids[s] = Marshal.ReadInt16(Native.At(Rva.PartyIds + 2 * s));
            return ids;
        }

        // A member's name: Six Person's own (the real one, add-on characters like Cress included), else by id with
        // Artes Character Renamer's renames.
        public static string Name(int id, int seat)
        {
            if (id <= 0) return "";
            string label = SixPersonName(seat);
            if (label != null) return label;
            // Without Six Person's names: by id. (Add-on characters only appear through Six Person's list, and while it is
            // filled its names are there; until they're found, an add-on's id would show the character it took over.)
            if (id >= Names.Length) return "Character " + id;
            string renamed;
            return Renames().TryGetValue(Names[id], out renamed) ? renamed : Names[id];
        }

        // Six Person Battle Expansion keeps its own party list (six ids, the Formation order) in its own memory and
        // sends the game's reads there. Its code loads that address with "mov r11, imm64" right before reading an id
        // ("movzx edx, word [r11+rcx*2]; test edx, edx; je"): found by that code (searched every few seconds until found;
        // the address is checked on every use).
        static long sixList;
        static float nextSearch;
        static bool? sixLoaded;
        static readonly byte[] ListRead = { 0x41, 0x0F, 0xB7, 0x14, 0x4B, 0x85, 0xD2, 0x0F, 0x84 };

        static long SixPersonList()
        {
            if (sixLoaded == null)
            {
                sixLoaded = false;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = a.GetName().Name;
                    if (n == "EterniaSixPersonBattle") sixLoaded = true;
                }
            }
            if (sixLoaded != true) return 0;
            if (sixList != 0 && Plausible(sixList, true)) return sixList;
            sixList = 0;
            float now = Time.unscaledTime;
            if (now < nextSearch) return 0;
            nextSearch = now + 5f;
            sixList = Search();
            if (sixList != 0) Plugin.Log.LogMessage("Character choice: Six Person's party list at " + sixList.ToString("X") + ".");
            return sixList;
        }

        // Six ids 0-63, the leader's set, no gaps before the last member.
        static bool Plausible(long at) { return Plausible(at, false); }

        // emptyOk: Six Person fills its list in only after a while (a battle, the Party Editor); an empty one is still it.
        static bool Plausible(long at, bool emptyOk)
        {
            try
            {
                var b = new byte[2 * Seats];
                Marshal.Copy(new IntPtr(at), b, 0, b.Length);
                bool ended = false;
                for (int s = 0; s < Seats; s++)
                {
                    int id = BitConverter.ToInt16(b, 2 * s);
                    if (id < 0 || id > 63 || (s == 0 && id == 0 && !emptyOk) || (ended && id != 0)) return false;
                    if (id == 0) ended = true;
                }
                return true;
            }
            catch { return false; }
        }

        // Six Person also keeps the members' names, one 0x40-byte entry per seat in Formation order ("Reid", "Farah",
        // "Shizel(Past)", "Cress"...): the names its add-on characters really have. Found by shape - a zero entry, then
        // as many short printable names as the party has members, then empty entries - and re-checked every use.
        const int NameSize = 0x40;
        static long nameTable;
        static float nextNameSearch;

        static string SixPersonName(int seat)
        {
            if (sixLoaded != true || SixPersonList() == 0 || Marshal.ReadInt16(new IntPtr(sixList)) <= 0) return null;
            int members = 0;
            var ids = new int[Seats];
            for (int s = 0; s < Seats; s++) { ids[s] = Marshal.ReadInt16(new IntPtr(sixList + 2 * s)); if (ids[s] > 0) members++; }
            if (members < 1) return null;
            if (nameTable == 0 || !NameTableFits(ReadNames(nameTable), members, ids))
            {
                nameTable = 0;
                float now = Time.unscaledTime;
                if (members < 2 || now < nextNameSearch) return null;
                nextNameSearch = now + 5f;
                nameTable = SearchNames(members, ids);
                if (nameTable == 0) return null;
                Plugin.Log.LogMessage("Character choice: Six Person's member names at " + nameTable.ToString("X") + ".");
            }
            return NameAt(ReadNames(nameTable), NameSize * (seat + 1));
        }

        static byte[] ReadNames(long table)
        {
            var b = new byte[NameSize * (Seats + 1)];   // the zero entry before, then six
            try { Marshal.Copy(new IntPtr(table - NameSize), b, 0, b.Length); } catch { return null; }
            return b;
        }

        // The name in the entry at `at`: printable ASCII, 1-47 characters, zeros after; "" for an empty entry; null otherwise.
        static string NameAt(byte[] b, int at)
        {
            if (b == null) return null;
            int n = 0;
            while (n < 48 && b[at + n] != 0) { if (b[at + n] < 0x20 || b[at + n] > 0x7E) return null; n++; }
            if (n == 48) return null;
            for (int i = at + n; i < at + NameSize; i++) if (b[i] != 0) return null;
            return System.Text.Encoding.ASCII.GetString(b, at, n);
        }

        // Same shape as the game's own name table by character id ("Nothing", "Reid", "Farah", "Keele"...), so also: a
        // base-game name (or its Artes Character Renamer name) may only sit in the seat that holds that character.
        static bool NameTableFits(byte[] b, int members, int[] ids)
        {
            if (b == null || NameAt(b, 0) != "") return false;
            var renamed = Renames();
            int matches = 0;   // at least one base-game member must be named in its own seat (other tables: "Slot 1"...)
            for (int s = 0; s < Seats; s++)
            {
                string name = NameAt(b, NameSize * (s + 1));
                if (name == null || (name != "") != (s < members)) return false;
                for (int id = 1; id < Names.Length; id++)
                {
                    string r;
                    bool its = name == Names[id] || (renamed.TryGetValue(Names[id], out r) && name == r);
                    if (its && ids[s] != id) return false;
                    if (its) matches++;
                }
                if (name == "Nothing") return false;
            }
            return matches > 0;
        }

        static long SearchNames(int members, int[] ids)
        {
            long from = Math.Max(0x10000, Native.Base - 0x80000000L), to = Native.Base + 0x80000000L, at = from;
            MemoryInfo m;
            var size = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryInfo)));
            var window = new byte[NameSize * (Seats + 1)];
            while (at < to && VirtualQuery(new IntPtr(at), out m, size) != UIntPtr.Zero)
            {
                long b = m.Base.ToInt64(), len = m.Size.ToInt64();
                if (len <= 0) break;
                bool rw = (m.Protect & 0x44) != 0 && (m.Protect & 0x100) == 0;
                if (m.State == 0x1000 && m.Type == 0x20000 && rw && len <= (64 << 20))
                {
                    var buf = new byte[len];
                    try { Marshal.Copy(new IntPtr(b), buf, 0, (int)len); }
                    catch { buf = null; }
                    if (buf != null)
                        for (int i = NameSize; i + NameSize * Seats <= buf.Length; i += NameSize)
                        {
                            byte c = buf[i];
                            if (c < 'A' || c > 'Z' || buf[i - NameSize] != 0) continue;
                            Array.Copy(buf, i - NameSize, window, 0, window.Length);
                            if (NameTableFits(window, members, ids)) return b + i;
                        }
                }
                at = b + len;
            }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryInfo { public IntPtr Base, AllocBase; public uint AllocProtect; public IntPtr Size; public uint State, Protect, Type; }
        [DllImport("kernel32")] static extern UIntPtr VirtualQuery(IntPtr address, out MemoryInfo info, UIntPtr size);

        // Private executable memory within 2 GB of GameNative (where Six Person puts its code, to reach the game's).
        static long Search()
        {
            long from = Math.Max(0x10000, Native.Base - 0x80000000L), to = Native.Base + 0x80000000L, at = from;
            MemoryInfo m;
            var size = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryInfo)));
            while (at < to && VirtualQuery(new IntPtr(at), out m, size) != UIntPtr.Zero)
            {
                long b = m.Base.ToInt64(), len = m.Size.ToInt64();
                if (len <= 0) break;
                bool exec = (m.Protect & 0xF0) != 0 && (m.Protect & 0x100) == 0;   // executable, not a guard page
                if (m.State == 0x1000 && m.Type == 0x20000 && exec && len <= (16 << 20))
                {
                    var buf = new byte[len];
                    try { Marshal.Copy(new IntPtr(b), buf, 0, (int)len); }
                    catch { buf = null; }
                    if (buf != null)
                        for (int i = 10; i + ListRead.Length <= buf.Length; i++)
                        {
                            if (buf[i] != ListRead[0] || buf[i - 10] != 0x49 || buf[i - 9] != 0xBB) continue;
                            int k = 1;
                            while (k < ListRead.Length && buf[i + k] == ListRead[k]) k++;
                            if (k < ListRead.Length) continue;
                            long list = BitConverter.ToInt64(buf, i - 8);
                            if (Plausible(list, true)) return list;
                        }
                }
                at = b + len;
            }
            return 0;
        }

        // Artes Character Renamer's [Characters] "Original = New" lines (BepInEx\config\CharacterNames.cfg), re-read
        // when the file changes; empty when it isn't installed or is disabled.
        static Dictionary<string, string> renames = new Dictionary<string, string>();
        static DateTime renamesTime;
        static float renamesCheck;

        static Dictionary<string, string> Renames()
        {
            float now = Time.unscaledTime;
            if (now < renamesCheck) return renames;
            renamesCheck = now + 2f;
            try
            {
                string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "CharacterNames.cfg");
                if (!System.IO.File.Exists(path)) { renames.Clear(); return renames; }
                var time = System.IO.File.GetLastWriteTimeUtc(path);
                if (time == renamesTime) return renames;
                renamesTime = time;
                var map = new Dictionary<string, string>();
                bool on = true, inCharacters = false;
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[")) { inCharacters = line == "[Characters]"; continue; }
                    int eq = line.IndexOf('=');
                    if (line.StartsWith("#") || eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    if (key == "Enabled") on = value.ToLowerInvariant() != "false";
                    else if (inCharacters && value != "") map[key] = value;
                }
                renames = on ? map : new Dictionary<string, string>();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Character names: " + e.Message); }
            return renames;
        }
    }
}
