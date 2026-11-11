// Debug build only: once, 15 seconds after a save is loaded (other mods have installed their hooks by then), compares
// GameNative's code in memory with GameNative.dll on disk and logs every changed range with the function it lies in
// (from .pdata) - who rewrote what. Our own patches show up too (their addresses are in Rva / Sites).
#if COOP_DEBUG
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace EterniaCoop
{
    static class CodeDiff
    {
        static float due = -1;
        static bool done;

        // Names the C# methods at addresses listed (hex, one per line) in BepInEx\cache\eterniacoop-lookup.txt, through
        // Mono's own JIT tables; for research\sample_game.ps1's "code@" return addresses. The file is deleted after.
        [DllImport("mono-2.0-bdwgc")] static extern IntPtr mono_get_root_domain();
        [DllImport("mono-2.0-bdwgc")] static extern IntPtr mono_jit_info_table_find(IntPtr domain, IntPtr addr);
        [DllImport("mono-2.0-bdwgc")] static extern IntPtr mono_jit_info_get_method(IntPtr ji);
        [DllImport("mono-2.0-bdwgc")] static extern IntPtr mono_method_full_name(IntPtr method, bool signature);
        static float nextLookup;

        static void Lookup()
        {
            if (Time.unscaledTime < nextLookup) return;
            nextLookup = Time.unscaledTime + 1f;
            string path = Path.Combine(BepInEx.Paths.CachePath, "eterniacoop-lookup.txt");
            if (!File.Exists(path)) return;
            var sb = new StringBuilder("Method lookup:");
            foreach (string line in File.ReadAllLines(path))
            {
                string hex = line.Trim();
                if (hex.Length == 0) continue;
                long addr = Convert.ToInt64(hex.Replace("0x", ""), 16);
                IntPtr ji = mono_jit_info_table_find(mono_get_root_domain(), new IntPtr(addr));
                string name = ji == IntPtr.Zero ? "(not managed code)" : Marshal.PtrToStringAnsi(mono_method_full_name(mono_jit_info_get_method(ji), true));
                sb.AppendFormat("\n  0x{0:X}: {1}", addr, name);
            }
            File.Delete(path);
            Plugin.Log.LogInfo(sb.ToString());
        }

        public static void Update()
        {
            try { Lookup(); } catch (Exception e) { Plugin.Log.LogWarning("Method lookup: " + e.Message); nextLookup = float.MaxValue; }
            if (done || Native.Base == 0) return;
            if (Marshal.ReadInt32(Native.At(Rva.Area)) == 0) return;
            if (due < 0) { due = Time.unscaledTime + 15f; return; }
            if (Time.unscaledTime < due) return;
            done = true;
            try { Run(); }
            catch (Exception e) { Plugin.Log.LogWarning("Code diff: " + e.Message); }
        }

        static void Run()
        {
            byte[] file = File.ReadAllBytes(Path.Combine(Application.dataPath, "Plugins/x86_64/GameNative.dll"));
            int pe = BitConverter.ToInt32(file, 0x3C);
            int sections = BitConverter.ToInt16(file, pe + 6), table = pe + 24 + BitConverter.ToInt16(file, pe + 20);
            var sb = new StringBuilder("Code diff (memory vs GameNative.dll):");
            int ranges = 0;
            for (int s = 0; s < sections; s++)
            {
                int h = table + s * 40;
                if ((BitConverter.ToUInt32(file, h + 36) & 0x20000000) == 0) continue;   // executable sections only
                int va = BitConverter.ToInt32(file, h + 12), raw = BitConverter.ToInt32(file, h + 20);
                int size = Math.Min(BitConverter.ToInt32(file, h + 8), BitConverter.ToInt32(file, h + 16));
                var mem = new byte[size];
                Marshal.Copy(Native.At(va), mem, 0, size);
                for (int i = 0; i < size; i++)
                {
                    if (mem[i] == file[raw + i]) continue;
                    int start = i;
                    while (i < size && (mem[i] != file[raw + i] || (i + 1 < size && mem[i + 1] != file[raw + i + 1]))) i++;
                    int rva = va + start, len = i - start;
                    ranges++;
                    if (ranges <= 200)
                    {
                        sb.AppendFormat("\n  0x{0:X} +{1} in func 0x{2:X}: ", rva, len, Native.FunctionStart(rva));
                        for (int k = 0; k < Math.Min(len, 12); k++) sb.Append(mem[start + k].ToString("X2")).Append(' ');
                        if (mem[start] == 0xE9 || mem[start] == 0xE8)
                            sb.AppendFormat("-> 0x{0:X}", Native.Base + rva + 5 + BitConverter.ToInt32(mem, start + 1));
                        else if (mem[start] == 0xFF && mem[start + 1] == 0x25)
                            sb.AppendFormat("-> [0x{0:X}]", BitConverter.ToInt64(mem, start + 6));
                    }
                }
            }
            sb.AppendFormat("\n  {0} changed range(s).", ranges);
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
#endif
