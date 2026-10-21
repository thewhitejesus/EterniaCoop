// Research tools used to reverse-engineer the game. Compiled only into the debug build (build.ps1 -DebugBuild).
//   F5/F6/F7     memory scan: find .data bytes that flip between two states (F6 = state A, F7 = state B, F5 = reset)
//   Numpad -/+   peek / poke GameNative addresses from [Research] PeekRvas / PokeList (PokeList re-read each press)
//   Numpad 1     dump battle actors (control type, side, command block, block contents)
//   Numpad / *   dump / copy the input object's per-player remap table
//   Numpad 0     toggle blank input for players 2-4
//   F8           log marker     F9  dump input devices and players     F10  re-run controller assignment
//   F11          call GameNative's DebugInfoFunctions_* exports (all stubs returning null in the release game)
// F4 is the game's own key (toggles the leader's Auto mode), so it isn't used here.
#if COOP_DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EterniaCoop
{
    static class ResearchTools
    {
        static ConfigEntry<bool> hotkeys, verbose;
        static ConfigEntry<float> logInterval;
        static ConfigEntry<string> peekRvas, pokeList;
        static float nextLog;

        public static void Bind(ConfigFile c)
        {
            hotkeys = c.Bind("Debug", "Hotkeys", true, "Research hotkeys (see ResearchTools.cs). Debug build only.");
            verbose = c.Bind("Debug", "VerboseLog", true, "Log per-player input and battle hook statistics every few seconds.");
            logInterval = c.Bind("Debug", "LogIntervalSeconds", 5f, "Interval for the verbose log.");
            peekRvas = c.Bind("Debug", "PeekRvas", "011CAD38", "Numpad- logs 256 bytes around each of these GameNative RVAs (hex, comma separated).");
            pokeList = c.Bind("Debug", "PokeList", "011CAD39=01", "Numpad+ writes these bytes (hex rva=hex value, comma separated). Don't save while testing.");
        }

        public static void Update(ConfigFile config)
        {
            var kb = Keyboard.current;
            if (kb != null && hotkeys.Value)
            {
                if (kb[Key.F8].wasPressedThisFrame) Plugin.Log.LogMessage("=============== MARKER (F8) ===============");
                if (kb[Key.F9].wasPressedThisFrame) DumpPlayers();
                if (kb[Key.F10].wasPressedThisFrame) Players.AutoAssign(Settings.KeyboardPlayer.Value);
                if (kb[Key.F11].wasPressedThisFrame) NativeDebug.Dump();
                if (kb[Key.F5].wasPressedThisFrame) MemScan.Reset();
                if (kb[Key.F6].wasPressedThisFrame) MemScan.Mark(0);
                if (kb[Key.F7].wasPressedThisFrame) MemScan.Mark(1);
                if (kb[Key.NumpadMinus].wasPressedThisFrame)
                    foreach (var s in peekRvas.Value.Split(',')) MemScan.Peek(Convert.ToInt32(s.Trim(), 16));
                if (kb[Key.NumpadPlus].wasPressedThisFrame)
                {
                    config.Reload();
                    foreach (var s in pokeList.Value.Split(','))
                    {
                        var kv = s.Split('=');
                        if (kv.Length == 2) MemScan.Poke(Convert.ToInt32(kv[0].Trim(), 16), Convert.ToInt32(kv[1].Trim(), 16));
                    }
                }
                if (kb[Key.Numpad0].wasPressedThisFrame)
                {
                    Players.ForceBlankExtraPlayers = !Players.ForceBlankExtraPlayers;
                    Plugin.Log.LogMessage("Blank input for players 2-4: " + Players.ForceBlankExtraPlayers);
                }
                if (kb[Key.Numpad1].wasPressedThisFrame) DumpActors();
                if (kb[Key.NumpadDivide].wasPressedThisFrame) RemapTable.Dump();
                if (kb[Key.NumpadMultiply].wasPressedThisFrame) RemapTable.CopyPlayer0();
            }

            if (verbose.Value && Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + logInterval.Value;
                Plugin.Log.LogInfo("Input requests by playerNo  info[" + Drain(infoCalls) + "]  stick[" + Drain(stickCalls) + "]");
                string sticks = DrainSticks();
                if (sticks.Length > 0) Plugin.Log.LogInfo("Stick peaks:" + sticks);
                string entries = DrainEntries();
                if (entries.Length > 0) Plugin.Log.LogInfo("Input entries:" + entries);
                if (battleRuns[1] + battleRuns[2] + battleRuns[3] > 0)
                {
                    Plugin.Log.LogInfo(string.Format("Co-op battle: runs P2={0} P3={1} P4={2}  cmd bits P2={3:X8} P3={4:X8} P4={5:X8}",
                        battleRuns[1], battleRuns[2], battleRuns[3], cmdBits[1], cmdBits[2], cmdBits[3]));
                    Array.Clear(battleRuns, 0, 4); Array.Clear(cmdBits, 0, 4);
                }
            }
        }

        // ---- statistics fed by the input patches and the battle hook ----
        static readonly int[] infoCalls = new int[5], stickCalls = new int[5];
        static readonly int[] battleRuns = new int[4];
        static readonly uint[] cmdBits = new uint[4];
        static readonly float[] stickPeak = new float[16];   // [player * 4 + (left ? 0 : 2) + axis]
        static readonly Dictionary<int, string>[] liveEntries = { new Dictionary<int, string>(), new Dictionary<int, string>(), new Dictionary<int, string>(), new Dictionary<int, string>() };

        public static void CountInfo(int playerNo) { Interlocked.Increment(ref infoCalls[playerNo >= 0 && playerNo < 4 ? playerNo : 4]); }
        public static void CountStick(int playerNo) { Interlocked.Increment(ref stickCalls[playerNo >= 0 && playerNo < 4 ? playerNo : 4]); }

        public static void OnBattleSlot(int p, IntPtr pad, IntPtr state)
        {
            battleRuns[p]++;
            cmdBits[p] |= (uint)Marshal.ReadInt32(state);
        }

        public static void OnStickInfo(IntPtr outParam, bool left, int playerNo)
        {
            if (playerNo < 0 || playerNo > 3 || outParam == IntPtr.Zero) return;
            for (int axis = 0; axis < 2; axis++)
            {
                float v = Native.ReadFloat(new IntPtr(outParam.ToInt64() + 4 + axis * 4));
                int i = playerNo * 4 + (left ? 0 : 2) + axis;
                if (Math.Abs(v) > Math.Abs(stickPeak[i])) stickPeak[i] = v;
            }
        }

        public static void OnInputInfo(IntPtr arr, int count, int playerNo)
        {
            if (playerNo < 0 || playerNo > 3 || arr == IntPtr.Zero) return;
            var live = liveEntries[playerNo];
            lock (live)
                for (int i = 0; i < count; i++)
                {
                    IntPtr e = new IntPtr(arr.ToInt64() + i * 20);
                    float x = Native.ReadFloat(new IntPtr(e.ToInt64() + 4)), y = Native.ReadFloat(new IntPtr(e.ToInt64() + 8)), axis = Native.ReadFloat(new IntPtr(e.ToInt64() + 12));
                    byte pressed = Marshal.ReadByte(e);
                    if (pressed != 0 || x != 0 || y != 0 || axis != 0)
                        live[i] = string.Format("#{0}{1} x={2:0.00} y={3:0.00} axis={4:0.00}", i, pressed != 0 ? " pressed" : "", x, y, axis);
                }
        }

        static string Drain(int[] counters)
        {
            var parts = new string[5];
            for (int i = 0; i < 5; i++) parts[i] = (i == 4 ? "other" : "P" + (i + 1)) + "=" + Interlocked.Exchange(ref counters[i], 0);
            return string.Join(" ", parts);
        }

        static string DrainSticks()
        {
            var sb = new System.Text.StringBuilder();
            for (int p = 0; p < 4; p++)
                if (stickPeak[p * 4] != 0 || stickPeak[p * 4 + 1] != 0 || stickPeak[p * 4 + 2] != 0 || stickPeak[p * 4 + 3] != 0)
                    sb.AppendFormat(" P{0} L({1:0.00},{2:0.00}) R({3:0.00},{4:0.00})", p + 1, stickPeak[p * 4], stickPeak[p * 4 + 1], stickPeak[p * 4 + 2], stickPeak[p * 4 + 3]);
            Array.Clear(stickPeak, 0, stickPeak.Length);
            return sb.ToString();
        }

        // Entries players 2-4 had that player 1 didn't, plus player 1's active entries.
        static string DrainEntries()
        {
            var sb = new System.Text.StringBuilder();
            Dictionary<int, string> p0;
            lock (liveEntries[0]) { p0 = new Dictionary<int, string>(liveEntries[0]); liveEntries[0].Clear(); }
            for (int p = 1; p < 4; p++)
                lock (liveEntries[p])
                {
                    foreach (var kv in liveEntries[p]) if (!p0.ContainsKey(kv.Key)) sb.Append("\n    P" + (p + 1) + " only: " + kv.Value);
                    liveEntries[p].Clear();
                }
            if (p0.Count > 0) { sb.Append("\n    P1 active:"); foreach (var v in p0.Values) sb.Append(" [" + v + "]"); }
            return sb.ToString();
        }

        // ---- dumps ----
        static void DumpPlayers()
        {
            Plugin.Log.LogMessage("---- Input devices ----");
            foreach (var d in InputSystem.devices)
                Plugin.Log.LogMessage("  #" + d.deviceId + "  " + d.layout + "  \"" + d.displayName + "\"  " + d.path);
            Plugin.Log.LogMessage("---- UgxInputPlayers ----");
            for (int i = 0; i < 4; i++)
            {
                object p = Players.Get(i);
                if (p == null) { Plugin.Log.LogMessage("  player " + (i + 1) + ": <none>"); continue; }
                string devs = "";
                var en = AccessTools.Method(p.GetType(), "GetInputDevices").Invoke(p, null) as IEnumerable;
                if (en != null) foreach (var d in en) devs += ((InputDevice)d).displayName + "(#" + ((InputDevice)d).deviceId + ") ";
                object last = AccessTools.Method(p.GetType(), "GetLastInputDevice").Invoke(p, null);
                Plugin.Log.LogMessage("  player " + (i + 1) + ": devices=[" + devs.Trim() + "] last=" + (last == null ? "null" : ((InputDevice)last).displayName) +
                    " hasDevice=" + Players.HasDevice[i]);
            }
        }

        static void DumpActors()
        {
            IntPtr arr = Battle.Actors();
            Plugin.Log.LogMessage(string.Format("---- Battle actors @ rva 0x{0:X} ----", arr == IntPtr.Zero ? 0 : arr.ToInt64() - Native.Base));
            if (arr == IntPtr.Zero) return;
            for (int i = 0; i < Rva.ActorCount; i++)
            {
                IntPtr a = Battle.ActorAt(arr, i);
                short kind = Marshal.ReadInt16(a, Actor.Kind);
                if (kind == 0) continue;
                long inPtr = Marshal.ReadIntPtr(a, Actor.CmdBlock).ToInt64();
                var words = new int[4];
                if (inPtr != 0) for (int w = 0; w < 4; w++) words[w] = Marshal.ReadInt32(new IntPtr(inPtr), w * 4);
                Plugin.Log.LogMessage(string.Format(
                    "  [{0,2}] kind={1} side={2} ctrl={3} slot={4} x={5} y={6} block={7}  words: {8:X8} {9:X8} {10:X8} {11:X8}",
                    i, kind, Marshal.ReadByte(a, Actor.Side), Marshal.ReadByte(a, Actor.Control), Battle.PartySlot(a),
                    Marshal.ReadInt16(a, Actor.X), Marshal.ReadInt16(a, Actor.Y),
                    inPtr == 0 ? "null" : string.Format("rva 0x{0:X}", inPtr - Native.Base), words[0], words[1], words[2], words[3]));
            }
        }

        // ---- F5/F6/F7 memory scan, peek/poke over GameNative's .data ----
        static class MemScan
        {
            const int DataRva = 0x54F000, DataSize = 0xD83DAC, ShowMax = 40;   // .data section of GameNative.dll
            static readonly byte[][] stateValues = new byte[2][];
            static List<int> candidates;

            static byte[] Snapshot()
            {
                var buf = new byte[DataSize];
                Marshal.Copy(Native.At(DataRva), buf, 0, DataSize);
                return buf;
            }

            public static void Reset()
            {
                stateValues[0] = null; stateValues[1] = null; candidates = null;
                Plugin.Log.LogMessage("MemScan reset. F6 in state A, F7 in state B, keep alternating.");
            }

            public static void Mark(int state)
            {
                byte[] now = Snapshot();
                string name = state == 0 ? "A (F6)" : "B (F7)";
                if (stateValues[state] == null || candidates == null)
                {
                    stateValues[state] = now;
                    if (stateValues[1 - state] == null) { Plugin.Log.LogMessage("MemScan: recorded state " + name + "."); return; }
                    candidates = new List<int>();
                    for (int i = 0; i < DataSize; i++) if (stateValues[0][i] != stateValues[1][i]) candidates.Add(i);
                }
                else
                {
                    var keep = new List<int>();
                    foreach (int i in candidates) if (now[i] == stateValues[state][i]) keep.Add(i);
                    candidates = keep;
                }
                Plugin.Log.LogMessage("MemScan after state " + name + ": " + candidates.Count + " candidate byte(s).");
                int shown = 0;
                foreach (int i in candidates)
                {
                    if (shown++ >= ShowMax) { Plugin.Log.LogMessage("  ..."); break; }
                    Plugin.Log.LogMessage(string.Format("  rva 0x{0:X8}  A={1:X2} B={2:X2}", DataRva + i, stateValues[0][i], stateValues[1][i]));
                }
            }

            public static void Peek(int rva)
            {
                int start = (rva & ~0xF) - 0x80;
                Plugin.Log.LogMessage(string.Format("---- Peek around 0x{0:X8} ----", rva));
                for (int row = start; row < start + 0x100; row += 16)
                {
                    var b = new byte[16];
                    Marshal.Copy(Native.At(row), b, 0, 16);
                    var sb = new System.Text.StringBuilder(string.Format("  0x{0:X8}: ", row));
                    for (int i = 0; i < 16; i++) sb.Append(row + i == rva ? "[" + b[i].ToString("X2") + "]" : " " + b[i].ToString("X2") + " ");
                    Plugin.Log.LogMessage(sb.ToString());
                }
            }

            public static void Poke(int rva, int value)
            {
                if (rva < DataRva || rva >= DataRva + DataSize) { Plugin.Log.LogWarning(string.Format("Poke: 0x{0:X8} is outside .data", rva)); return; }
                byte old = Marshal.ReadByte(Native.At(rva));
                Marshal.WriteByte(Native.At(rva), (byte)value);
                Plugin.Log.LogMessage(string.Format("Poke 0x{0:X8}: {1:X2} -> {2:X2}", rva, old, (byte)value));
            }
        }

        // ---- the input object's +0x18 table: 0x1C entries of 24 bytes per player ----
        static class RemapTable
        {
            const int PerPlayer = 0x1C, EntrySize = 24;

            static IntPtr Table(out int players)
            {
                players = 0;
                IntPtr obj = Marshal.ReadIntPtr(Native.At(Rva.InputObject));
                if (obj == IntPtr.Zero) return IntPtr.Zero;
                IntPtr begin = Marshal.ReadIntPtr(obj, 0x18), end = Marshal.ReadIntPtr(obj, 0x20);
                players = (int)((end.ToInt64() - begin.ToInt64()) / (PerPlayer * EntrySize));
                return begin;
            }

            public static void Dump()
            {
                int players;
                IntPtr t = Table(out players);
                if (t == IntPtr.Zero) return;
                Plugin.Log.LogMessage("---- Remap table: " + players + " player block(s) ----");
                for (int e = 0; e < PerPlayer; e++)
                {
                    var b = new byte[EntrySize];
                    Marshal.Copy(new IntPtr(t.ToInt64() + e * EntrySize), b, 0, EntrySize);
                    Plugin.Log.LogMessage(string.Format("  [{0:D2}] P1: {1}", e, BitConverter.ToString(b).Replace("-", " ")));
                }
            }

            public static void CopyPlayer0()
            {
                int players;
                IntPtr t = Table(out players);
                if (t == IntPtr.Zero) return;
                for (int p = 1; p < players; p++) Native.Copy(t, new IntPtr(t.ToInt64() + p * PerPlayer * EntrySize), PerPlayer * EntrySize);
                Plugin.Log.LogMessage("RemapTable: copied player 1's block to players 2-" + players);
            }
        }

        // ---- F11: the DebugInfoFunctions_* exports (all compiled to "return 0" in the release game) ----
        static class NativeDebug
        {
            [DllImport("GameNative")] static extern IntPtr DebugInfoFunctions_GetPartyMemberStr();
            [DllImport("GameNative")] static extern IntPtr DebugInfoFunctions_GetBattleGroupId();
            [DllImport("GameNative")] static extern IntPtr DebugInfoFunctions_GetNativeMapId();

            public static void Dump()
            {
                Plugin.Log.LogMessage("PartyMember: " + Str(DebugInfoFunctions_GetPartyMemberStr()) +
                    "  BattleGroupId: " + Str(DebugInfoFunctions_GetBattleGroupId()) + "  MapId: " + Str(DebugInfoFunctions_GetNativeMapId()));
            }

            static string Str(IntPtr p)
            {
                if (p == IntPtr.Zero) return "<null>";
                int len = 0;
                while (len < 4096 && Marshal.ReadByte(p, len) != 0) len++;
                var bytes = new byte[len];
                Marshal.Copy(p, bytes, 0, len);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
        }
    }
}
#endif
