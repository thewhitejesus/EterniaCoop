// Finds every GameNative spot the mod uses by its code signature (Sites.Generated.cs, made by research/gen_sigs.py),
// so a game update that moves code doesn't break the mod.
//
// A site is one instruction (or, for repeated code, every copy of it). Its signature is that instruction with its
// neighbours, wildcarding what moves between builds (RIP-relative displacements, relative jump/call targets).
// Resolution:
//   1. at the remembered addresses (the known build's, or a previous run's for this exact GameNative.dll) - instant;
//   2. otherwise by searching .text: a site resolves only if its signature (and constraint, if any) matches exactly as
//      many places as the known build has copies. Anything else leaves the site unresolved and its feature off.
// Data sites are instructions that reach a global; the global's address is read from the instruction found.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    enum SiteKind { Code, Func, Data }

    sealed class Site
    {
        public readonly string Name, Feature, ConstraintSite;
        public readonly int[] KnownRvas;
        public readonly int Offset, DispAt, InstrLen, Addend, ConstraintOffset;
        public readonly SiteKind Kind;
        public readonly byte[] Bytes;
        public readonly bool[] Fixed;

        public int[] Rvas;    // resolved instruction addresses (one per copy), null if unresolved
        public int Target;    // data sites: the global's rva

        public Site(string name, string feature, int[] knownRvas, int offset, SiteKind kind, int dispAt, int instrLen,
            int addend, string constraintSite, int constraintOffset, string pattern)
        {
            Name = name; Feature = feature; KnownRvas = knownRvas; Offset = offset; Kind = kind; DispAt = dispAt;
            InstrLen = instrLen; Addend = addend; ConstraintSite = constraintSite; ConstraintOffset = constraintOffset;
            var parts = pattern.Split(' ');
            Bytes = new byte[parts.Length];
            Fixed = new bool[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != "??") { Bytes[i] = Convert.ToByte(parts[i], 16); Fixed[i] = true; }
        }
    }

    static partial class Sites
    {
        static readonly Dictionary<string, Site> byName = new Dictionary<string, Site>();
        static int textStart, textEnd;
        static byte[] text;           // copy of .text, only made when something has to be searched for
        public static int Moved;      // sites found somewhere other than the known build's address

        public static Site Get(string name) { return byName[name]; }
        public static int Rva(string name, int copy) { return byName[name].Rvas[copy]; }
        public static bool Resolved(string name) { return byName[name].Rvas != null; }

        // Resolves every site. Call once GameNative is loaded, before anything is patched. exact: GameNative.dll is the
        // build the signatures were made from.
        public static void Resolve(string hash, bool exact)
        {
            foreach (var s in All) byName[s.Name] = s;
            if (!ReadTextSection()) { Plugin.Log.LogError("Couldn't read GameNative's code section."); return; }

            var cached = LoadCache(hash);
            int searched = 0;
            foreach (var s in All)
            {
                int[] remembered;
                if (TryAt(s, s.KnownRvas)) continue;
                // Known build, but code next to the site differs: another mod patched it in memory (Uncrop, Guests from
                // Phantasia...). The site's own instruction is still checked exactly, as before signatures existed.
                if (exact && TryInstructionAt(s)) continue;
                if (cached.TryGetValue(s.Name, out remembered) && TryAt(s, remembered)) { Moved++; continue; }
                searched++;
                if (Search(s)) Moved++;
            }
            if (searched > 0) SaveCache(hash);
            if (Moved > 0)
            {
                Plugin.Log.LogWarning(string.Format("Game code moved (game update?): found {0} of {1} spots at new addresses{2}.",
                    Moved, All.Length, searched > 0 ? " by signature" : " (remembered from an earlier run)"));
                // For whoever updates the mod: research/gen_sigs.py's SITES table takes these new addresses.
                foreach (var s in All)
                    if (s.Rvas != null && s.Rvas[0] != s.KnownRvas[0])
                        Plugin.Log.LogInfo(string.Format("  {0}: 0x{1:X} -> 0x{2:X}{3}", s.Name, s.KnownRvas[0], s.Rvas[0],
                            s.Kind == SiteKind.Data ? string.Format(" (data 0x{0:X})", s.Target) : ""));
            }
#if COOP_DEBUG
            SelfTest();
#endif
        }

#if COOP_DEBUG
        // Debug build: search for every site from scratch, as after a game update, and compare with what was resolved.
        static void SelfTest()
        {
            var saved = new Dictionary<string, int[]>();
            foreach (var s in All) saved[s.Name] = s.Rvas;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int same = 0;
            foreach (var s in All)   // in table order, so constrained sites see their reference re-resolved first
            {
                s.Rvas = null;
                bool found = Search(s);
                bool ok = found && saved[s.Name] != null && string.Join(",", Array.ConvertAll(s.Rvas, r => r.ToString())) ==
                    string.Join(",", Array.ConvertAll(saved[s.Name], r => r.ToString()));
                if (ok) same++;
                else Plugin.Log.LogError("Site self-test: " + s.Name + " search gave " +
                    (s.Rvas == null ? "nothing" : string.Join(", ", Array.ConvertAll(s.Rvas, r => "0x" + r.ToString("X")))));
                if (saved[s.Name] != null) Accept(s, saved[s.Name]);
            }
            Plugin.Log.LogInfo(string.Format("Site self-test: full search found {0}/{1} sites at the same addresses in {2} ms.",
                same, All.Length, watch.ElapsedMilliseconds));
        }
#endif

        // ---- matching ----

        static unsafe bool Matches(Site s, int instrRva)
        {
            int start = instrRva - s.Offset;
            if (start < textStart || start + s.Bytes.Length > textEnd) return false;
            byte* p = (byte*)Native.At(start);
            for (int i = 0; i < s.Bytes.Length; i++)
                if (s.Fixed[i] && p[i] != s.Bytes[i]) return false;
            if (s.Kind == SiteKind.Data)
            {
                int target = instrRva + s.InstrLen + *(int*)(p + s.Offset + s.DispAt) - s.Addend;
                if (s.ConstraintSite != null)
                {
                    var c = byName[s.ConstraintSite];
                    if (c.Rvas == null || target != c.Target + s.ConstraintOffset) return false;
                }
            }
            return true;
        }

        static unsafe void Accept(Site s, int[] rvas)
        {
            s.Rvas = rvas;
            if (s.Kind == SiteKind.Data)
            {
                byte* p = (byte*)Native.At(rvas[0]);
                s.Target = rvas[0] + s.InstrLen + *(int*)(p + s.DispAt) - s.Addend;
            }
        }

        static unsafe bool TryInstructionAt(Site s)
        {
            foreach (int rva in s.KnownRvas)
            {
                byte* p = (byte*)Native.At(rva);
                for (int i = 0; i < s.InstrLen; i++)
                    if (s.Fixed[s.Offset + i] && p[i] != s.Bytes[s.Offset + i]) return false;
            }
            Accept(s, (int[])s.KnownRvas.Clone());
            if (s.Kind == SiteKind.Data && s.ConstraintSite != null &&
                s.Target != byName[s.ConstraintSite].Target + s.ConstraintOffset) { s.Rvas = null; return false; }
            Plugin.Log.LogInfo(s.Name + ": code next to it was changed (by another mod?); its own instruction is unchanged" + Diff(s));
            return true;
        }

        // " (differs at pattern bytes 3, 4, ...)" for the log.
        static unsafe string Diff(Site s)
        {
            var at = new List<string>();
            byte* p = (byte*)Native.At(s.KnownRvas[0] - s.Offset);
            for (int i = 0; i < s.Bytes.Length; i++)
                if (s.Fixed[i] && p[i] != s.Bytes[i]) at.Add((i - s.Offset).ToString("+0;-0"));
            return at.Count == 0 ? "" : " (differs at " + string.Join(" ", at.ToArray()) + " bytes from it)";
        }

        static bool TryAt(Site s, int[] rvas)
        {
            if (rvas == null || rvas.Length != s.KnownRvas.Length) return false;
            foreach (int r in rvas) if (!Matches(s, r)) return false;
            Accept(s, (int[])rvas.Clone());
            return true;
        }

        // Every place in .text the signature (and constraint) matches; resolved only if that's exactly as many places
        // as the known build has copies.
        static bool Search(Site s)
        {
            if (text == null)
            {
                text = new byte[textEnd - textStart];
                Marshal.Copy(Native.At(textStart), text, 0, text.Length);
            }
            // Look for the longest fixed run first, then compare the whole pattern.
            int a0 = 0, a1 = 0;
            for (int i = 0; i < s.Bytes.Length; )
            {
                if (!s.Fixed[i]) { i++; continue; }
                int j = i;
                while (j < s.Bytes.Length && s.Fixed[j]) j++;
                if (j - i > a1 - a0) { a0 = i; a1 = j; }
                i = j;
            }
            byte first = s.Bytes[a0];
            var found = new List<int>();
            for (int pos = a0; pos <= text.Length - (s.Bytes.Length - a0); pos++)
            {
                if (text[pos] != first) continue;
                int k = 1;
                while (a0 + k < a1 && text[pos + k] == s.Bytes[a0 + k]) k++;
                if (a0 + k < a1) continue;
                int instr = textStart + pos - a0 + s.Offset;
                if (!Matches(s, instr)) continue;
                found.Add(instr);
                if (found.Count > s.KnownRvas.Length) break;
            }
            if (found.Count != s.KnownRvas.Length)
            {
                Plugin.Log.LogWarning(string.Format("{0}: {1} (expected {2}).", s.Name,
                    found.Count == 0 ? "not found" : "found in " + (found.Count > s.KnownRvas.Length ? "more" : "fewer") + " places",
                    s.KnownRvas.Length));
                return false;
            }
            Accept(s, found.ToArray());
            return true;
        }

        // .text bounds from the module's own PE headers.
        static bool ReadTextSection()
        {
            if (Native.Base == 0) return false;
            IntPtr b = Native.At(0);
            int pe = Marshal.ReadInt32(b, 0x3C);
            int sections = Marshal.ReadInt16(b, pe + 6), optSize = Marshal.ReadInt16(b, pe + 20);
            int table = pe + 24 + optSize;
            for (int i = 0; i < sections; i++)
            {
                int h = table + i * 40;
                if (Marshal.ReadInt64(b, h) == 0x747865742E)   // ".text\0\0\0"
                {
                    textStart = Marshal.ReadInt32(b, h + 12);
                    textEnd = textStart + Marshal.ReadInt32(b, h + 8);
                    return true;
                }
            }
            return false;
        }

        // ---- cache: where a previous run found each site in this exact GameNative.dll (still verified on use) ----

        static string CachePath(string hash)
        {
            return hash == null ? null : Path.Combine(BepInEx.Paths.CachePath, "EterniaCoop-sites-" + hash.Substring(0, 16) + ".txt");
        }

        static Dictionary<string, int[]> LoadCache(string hash)
        {
            var result = new Dictionary<string, int[]>();
            try
            {
                string path = CachePath(hash);
                if (path == null || !File.Exists(path)) return result;
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var parts = line.Substring(eq + 1).Split(',');
                    var rvas = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++) rvas[i] = Convert.ToInt32(parts[i], 16);
                    result[line.Substring(0, eq)] = rvas;
                }
            }
            catch (Exception e) { Plugin.Log.LogDebug("Site cache unreadable: " + e.Message); }
            return result;
        }

        static void SaveCache(string hash)
        {
            try
            {
                string path = CachePath(hash);
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string>();
                foreach (var s in All)
                    if (s.Rvas != null) lines.Add(s.Name + "=" + string.Join(",", Array.ConvertAll(s.Rvas, r => r.ToString("X"))));
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e) { Plugin.Log.LogDebug("Couldn't save site cache: " + e.Message); }
        }
    }
}
