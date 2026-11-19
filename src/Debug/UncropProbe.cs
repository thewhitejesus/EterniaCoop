// Debug build only: what Uncrop's battle view makes of each batch of the game's drawing (it found why zoomed-out
// battles broke with Uncrop: see UncropCompat). Kept for the next Uncrop update.
//
// [Debug] UncropFullZoom: zoom out fully even if UncropCompat couldn't patch Uncrop, and log, while a battle runs zoomed
// out with Uncrop's battle view on, once every 2 s each kind of batch other than character sprites and known HUD
// pieces: depth, vertex count, extent on screen, texture name, the flags Uncrop set (SceneLayer, Placed = laid out as
// HUD, Effect, Overlay, ScreenCopy) and the viewport kind Classify chose for it.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;

namespace EterniaCoop
{
    static class UncropProbe
    {
        public static ConfigEntry<bool> FullZoom;
        static bool installed;
        static FieldInfo fFirst, fCount, fTexture, fScene, fPlaced, fEffect, fOverlay, fCopy, fManager, fGetVb;
        static MethodInfo textureName;
        static readonly List<string> lines = new List<string>();
        static readonly HashSet<string> seen = new HashSet<string>();
        static float nextLog;

        public static ConfigEntry<bool> WaterSlices, SkipVictoryZoom, SkipWaterHandoff;

        public static void Bind(ConfigFile c)
        {
            SkipVictoryZoom = c.Bind("Debug", "SkipVictoryZoom", false,
                "Crash hunt (Water's Edition): don't hand the game its zoom back when the last enemy falls. Debug build only.");
            SkipWaterHandoff = c.Bind("Debug", "SkipWaterHandoff", false,
                "Crash hunt (Water's Edition): leave Water's camera setting alone (no switch to Co-op). Debug build only.");
            WaterSlices = c.Bind("Debug", "InputSlicesDuringBuilder", false,
                "Experiment for Water's Edition's Overlimit on players 2-4: keep the raw input pointed at each player while " +
                "their battle commands are built. Read when a battle frame runs. Debug build only.");
            FullZoom = c.Bind("Debug", "UncropFullZoom", false,
                "Let the co-op camera zoom out fully while Uncrop's battle view is on, and log how Uncrop sorts the battle's drawing. Debug build only.");
        }

        public static void Install(Type view)
        {
            if (installed || view == null) return;
            installed = true;
            try
            {
                Type draw = view.GetNestedType("Draw", BindingFlags.NonPublic | BindingFlags.Public);
                const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                fFirst = draw.GetField("First", I); fCount = draw.GetField("Count", I); fTexture = draw.GetField("Texture", I);
                fScene = draw.GetField("SceneLayer", I); fPlaced = draw.GetField("Placed", I); fEffect = draw.GetField("Effect", I);
                fOverlay = draw.GetField("Overlay", I); fCopy = draw.GetField("ScreenCopy", I);
                fManager = view.GetField("drawerManager", S); fGetVb = view.GetField("getVertexBuffer", S);
                textureName = view.GetMethod("TextureName", S);
                // Classify(byte* vb, Draw d) -> Kind, at render time with the batch's final flags.
                MethodInfo classify = view.GetMethod("Classify", S);
                new Harmony(Plugin.Guid + ".uncropprobe").Patch(classify, null, new HarmonyMethod(typeof(UncropProbe).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
                Plugin.Log.LogInfo("Uncrop probe installed.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Uncrop probe: " + e.Message); }
        }

        static bool Logging()
        {
            return FullZoom.Value && UncropCompat.BattleView && Native.Base != 0 && Battle.Actors() != IntPtr.Zero &&
                Marshal.ReadInt16(Native.At(Rva.ZoomNow)) > BattleGround.ZoomGameMax;
        }

        static int calls;
        static string firstError;

        static readonly string[] Kinds = { "Unknown", "Plain", "Scene", "Hud", "Menu", "Skip" };

        static unsafe void Postfix(object[] __args, object __result)
        {
            calls++;
            try
            {
                if (!Logging() || lines.Count > 80) return;
                object d = __args[1];
                // Harmony hands the byte* argument over as null; the batches live in the drawer manager's vertex buffer.
                object manager = fManager.GetValue(null);
                var getVb = (MethodInfo)fGetVb.GetValue(null);
                if (manager == null || getVb == null) return;
                byte* vb = (byte*)Pointer.Unbox(getVb.Invoke(manager, null));
                int first = (int)fFirst.GetValue(d), count = (int)fCount.GetValue(d), tex = (int)fTexture.GetValue(d);
                if (count <= 0) return;
                if (vb == null) return;
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                float z = *(float*)(vb + (long)first * 48 + 8);
                for (int i = 0; i < count; i++)
                {
                    float* v = (float*)(vb + (long)(first + i) * 48);
                    if (v[0] < x0) x0 = v[0]; if (v[0] > x1) x1 = v[0];
                    if (v[1] < y0) y0 = v[1]; if (v[1] > y1) y1 = v[1];
                }
                string name = tex >= 0 ? (string)textureName.Invoke(null, new object[] { tex }) : "(none)";
                if (name.EndsWith("MX") || name.Contains("_plt_") || name.StartsWith("jef1_ui")) return;   // characters, HUD: known
                string flags = ((bool)fScene.GetValue(d) ? "S" : "-") + ((bool)fPlaced.GetValue(d) ? "P" : "-") + ((bool)fEffect.GetValue(d) ? "E" : "-") +
                    ((bool)fOverlay.GetValue(d) ? "O" : "-") + ((bool)fCopy.GetValue(d) ? "C" : "-");
                string kind = Convert.ToInt32(__result) < Kinds.Length ? Kinds[Convert.ToInt32(__result)] : __result.ToString();
                string key = name + flags + kind + (int)(z / 50);
                lock (seen) { if (!seen.Add(key)) return; }
                lines.Add(string.Format("z {0:0} n {1} x {2:0}..{3:0} y {4:0}..{5:0} ({6:0}x{7:0}) tex {8} flags {9} -> {10}",
                    z, count, x0, x1, y0, y1, x1 - x0, y1 - y0, name, flags, kind));
            }
            catch (Exception e) { if (firstError == null) firstError = e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace; }
        }

        // Main thread, every frame.
        public static void Update(float now)
        {
            if (now < nextLog) return;
            nextLog = now + 2f;
            if (firstError != null) { Plugin.Log.LogWarning("Uncrop probe error: " + firstError); firstError = ""; }
            if (lines.Count == 0) { if (calls > 0 && UncropCompat.BattleView) Plugin.Log.LogInfo("Uncrop probe: " + calls + " batches seen, none logged (logging " + Logging() + "), menu-like frames while running " + UncropCompat.washFrames + "."); calls = 0; return; }
            calls = 0;
            var sb = new StringBuilder("Uncrop probe (zoom " + Marshal.ReadInt16(Native.At(Rva.ZoomNow)) + "): ");
            foreach (string l in lines) sb.Append("\n  ").Append(l);
            Plugin.Log.LogInfo(sb.ToString());
            lines.Clear();
            seen.Clear();
        }
    }
}
