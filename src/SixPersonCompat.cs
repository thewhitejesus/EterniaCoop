// Six Person Battle Expansion (2.3) costs about 25 FPS (59 -> 34 on the title screen, without co-op): before each read of
// game memory it checks the range with WindowsTemplateHost.NativeDataRange(address, length), which asks Windows about
// every memory region in it (VirtualQuery, a system call) - thousands of times a frame, 93% of the game's main thread
// (research\sample_game.ps1). The check passes when every region is committed, not a guard page and plain read-write
// (PAGE_READWRITE or PAGE_WRITECOPY), after the range is screened against Six Person's own allocations.
//
// Nearly every range it checks lies in GameNative's image (the game's globals), whose layout doesn't change while the
// game runs. For those ranges this answers from a snapshot of the image's regions, taken with the same VirtualQuery
// and retaken every two seconds; anything else, Six Person's own check answers as before. Applied once Six Person's
// type is loaded (the bridge starts it after us); if a later version renames the method, nothing is patched.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace EterniaCoop
{
    static class SixPersonCompat
    {
        const string HostType = "Eternia.SixPersonBattle.WindowsTemplateHost";
        static bool done;
        static float nextTry;
        static long imageStart, imageEnd;

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryInfo { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public IntPtr RegionSize; public uint State, Protect, Type; }
        [DllImport("kernel32")] static extern UIntPtr VirtualQuery(IntPtr address, out MemoryInfo info, UIntPtr size);

        // The image's regions: start addresses (sorted), ends, and whether each passes Six Person's test. Replaced as a
        // whole, so a check running meanwhile sees one snapshot or the other, never a mix.
        sealed class Regions { public long[] Start = new long[0], End = new long[0]; public bool[] Ok = new bool[0]; }
        static volatile Regions regions = new Regions();
        static long snapshotAt = long.MinValue / 2;

        // Seconds since start (Stopwatch: the check may be called off the main thread, where Unity's Time can't be used).
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        static float Now { get { return (float)clock.Elapsed.TotalSeconds; } }

        // Main thread, every frame: looks for Six Person for the first two minutes, once a second.
        public static void Update()
        {
            if (done || Native.Base == 0 || Time.unscaledTime < nextTry) return;
            nextTry = Time.unscaledTime + 1f;
            if (Time.unscaledTime > 120f) { done = true; return; }
            Type host = AccessTools.TypeByName(HostType);
            if (host == null) return;
            done = true;
            MethodInfo check = AccessTools.Method(host, "NativeDataRange", new[] { typeof(long), typeof(int) });
            if (check == null || check.ReturnType != typeof(bool))
            {
                Plugin.Log.LogInfo("Six Person Battle Expansion: its memory check has changed, so the faster check isn't applied.");
                return;
            }
            imageStart = Native.Base;
            imageEnd = Native.Base + Marshal.ReadInt32(Native.At(Marshal.ReadInt32(Native.At(0x3C)) + 24 + 56));   // SizeOfImage
            new Harmony(Plugin.Guid + ".sixperson").Patch(check,
                new HarmonyMethod(typeof(SixPersonCompat).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)),
                new HarmonyMethod(typeof(SixPersonCompat).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
            Plugin.Log.LogMessage("Six Person Battle Expansion found: its memory checks of the game's data are answered from a snapshot (faster).");
        }

        // NativeDataRange(long address, int length): ranges inside GameNative's image from the snapshot; the rest as before.
#if COOP_DEBUG
        // Debug build: which ranges outside the image are checked, and how often (logged every 5 seconds).
        static readonly Dictionary<long, int> outside = new Dictionary<long, int>();
        static float nextReport;
        public static void Report()
        {
            if (Time.unscaledTime < nextReport) return;
            nextReport = Time.unscaledTime + 5f;
            var list = new List<KeyValuePair<long, int>>();
            lock (outside) { list.AddRange(outside); outside.Clear(); }
            if (list.Count == 0) return;
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new System.Text.StringBuilder("Six Person memory checks outside the image (5 s):");
            for (int k = 0; k < list.Count && k < 8; k++) sb.AppendFormat("  0x{0:X} x{1};", list[k].Key, list[k].Value);
            sb.Append("  distinct " + list.Count);
            Plugin.Log.LogInfo(sb.ToString());
        }
#endif

        // Ranges outside the image: Six Person's own answer, remembered for a second. In practice these are two of the
        // game's texture buffers (Six Person checks 50 MB ranges there, region by region, twice a frame), which stay put
        // for the whole session; a buffer that moved would be noticed at the next check after that second.
        struct Range { public long At; public int Length; }
        struct Answer { public bool Ok; public float Time; }
        static readonly Dictionary<Range, Answer> remembered = new Dictionary<Range, Answer>();
        const float RememberSeconds = 1f;

        static bool Prefix(long __0, int __1, ref bool __result, out bool __state)
        {
            __state = false;
            long a = __0, end = __0 + __1;
#if COOP_DEBUG
            if (a < imageStart || end > imageEnd) lock (outside) { int c; outside.TryGetValue(a, out c); outside[a] = c + 1; }
#endif
            if (a < imageStart || end > imageEnd)
            {
                Answer known;
                lock (remembered)
                    if (remembered.TryGetValue(new Range { At = a, Length = __1 }, out known) && Now - known.Time < RememberSeconds)
                    { __result = known.Ok; return false; }
                __state = true;   // Six Person checks it; Postfix remembers the answer
                return true;
            }
            if (__1 < 0 || (a & 7) != 0) return true;
            if (clock.ElapsedMilliseconds - snapshotAt > 2000) Snapshot();
            var r = regions;
            int i = Array.BinarySearch(r.Start, a);
            if (i < 0) i = ~i - 1;
            if (i < 0) return true;
            for (; i < r.Start.Length && r.Start[i] < end; i++)
                if (!r.Ok[i] || (i + 1 < r.Start.Length && r.End[i] != r.Start[i + 1] && r.End[i] < end)) { __result = false; return false; }
            __result = true;
            return false;
        }

        static void Postfix(long __0, int __1, bool __result, bool __state)
        {
            if (!__state) return;
            lock (remembered)
            {
                if (remembered.Count > 256) remembered.Clear();   // only a handful are ever checked
                remembered[new Range { At = __0, Length = __1 }] = new Answer { Ok = __result, Time = Now };
            }
        }

        // The same test Six Person makes, region by region over the whole image.
        static void Snapshot()
        {
            snapshotAt = clock.ElapsedMilliseconds;
            var starts = new List<long>(); var ends = new List<long>(); var ok = new List<bool>();
            long at = imageStart;
            MemoryInfo m;
            while (at < imageEnd && VirtualQuery(new IntPtr(at), out m, new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryInfo)))) != UIntPtr.Zero)
            {
                long next = m.BaseAddress.ToInt64() + m.RegionSize.ToInt64();
                uint protect = m.Protect & 0xFF;
                starts.Add(m.BaseAddress.ToInt64());
                ends.Add(next);
                ok.Add(m.State == 0x1000 && (m.Protect & 0x100) == 0 && (protect == 4 || protect == 8));
                if (next <= at) break;
                at = next;
            }
            regions = new Regions { Start = starts.ToArray(), End = ends.ToArray(), Ok = ok.ToArray() };
        }
    }
}
