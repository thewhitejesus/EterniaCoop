// Uncrop (a BepInEx 6 mod, through the bridge) 1.7+ has a battle view ("Uncrop battles"): it shows the PS1's whole
// 320x208 battle picture, scaled to the screen height, by giving each batch of the game's drawing its own viewport
// (scene, HUD, menu...), sorted by the batch's depth and texture. With the co-op camera zoomed out past the game's own
// limit, characters floated high above their shadows and flickered (reported by zarusama; 1.9.2 kept the zoom within
// the game's range).
//
// Why (found with the debug build's Uncrop probe, Debug/UncropProbe.cs): Uncrop decides once a frame whether the frame
// shows the battle scene (BattleView.StartRenderCore: some batch at depth 300-499, where the characters are) - only in
// such a "scene frame" do the characters get the scene scaling; otherwise everything without a stage texture gets the
// menu viewport, unscaled. Zoomed out, depths drop and frames come up without anything in that band: the characters
// (and their weapons) were drawn unscaled while the stage's ground kept its scaling - floating, flickering frame to frame.
//
// So while the co-op camera is zoomed out past the game's limit in a battle with Uncrop's battle view on, a postfix on
// StartRenderCore marks the frame a scene frame (sceneFrame / lastSceneFrame). If that can't be patched (an Uncrop
// that changed inside), the co-op camera stays within the game's own zoom range instead.
using System;
using System.Reflection;

namespace EterniaCoop
{
    static class UncropCompat
    {
        static FieldInfo active;      // UncropPlugin.BattleView.Active (static bool)
        static FieldInfo sceneFrame, lastSceneFrame;
        static FieldInfo frameDraws, drawerManager, getVertexBuffer, drawFirst, drawCount;   // for MenuPanel
        static bool patched;
        static bool searched;
        static float nextSearch;
        static volatile bool battleView;
        static bool reported;

        // Uncrop's battle view is drawing this battle.
        public static bool BattleView { get { return battleView; } }

        // The co-op camera keeps to the game's zoom range: Uncrop's battle view is on and couldn't be patched (debug build:
        // [Debug] UncropFullZoom lifts that, to experiment).
        public static bool LimitZoom
        {
            get
            {
#if COOP_DEBUG
                if (UncropProbe.FullZoom != null && UncropProbe.FullZoom.Value) return false;
#endif
                return battleView && !patched;
            }
        }

        static void Patch(Type view)
        {
            try
            {
                const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                sceneFrame = view.GetField("sceneFrame", S);
                lastSceneFrame = view.GetField("lastSceneFrame", S);
                MethodInfo start = view.GetMethod("StartRenderCore", S, null, Type.EmptyTypes, null);
                if (sceneFrame == null || lastSceneFrame == null || start == null || sceneFrame.FieldType != typeof(bool) ||
                    lastSceneFrame.FieldType != typeof(bool))
                {
                    Plugin.Log.LogInfo("Uncrop's battle view has changed inside: the co-op camera keeps to the game's zoom range with it.");
                    return;
                }
                // Optional: lets a submenu window drawn in the frame keep Uncrop's own (menu) judgement (MenuPanel).
                frameDraws = view.GetField("frameDraws", S);
                drawerManager = view.GetField("drawerManager", S);
                getVertexBuffer = view.GetField("getVertexBuffer", S);
                Type draw = view.GetNestedType("Draw", BindingFlags.NonPublic | BindingFlags.Public);
                const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                if (draw != null) { drawFirst = draw.GetField("First", I); drawCount = draw.GetField("Count", I); }
                new HarmonyLib.Harmony(Plugin.Guid + ".uncrop").Patch(start,
                    new HarmonyLib.HarmonyMethod(typeof(UncropCompat).GetMethod("BeforeStartRender", BindingFlags.Static | BindingFlags.NonPublic)),
                    new HarmonyLib.HarmonyMethod(typeof(UncropCompat).GetMethod("AfterStartRender", BindingFlags.Static | BindingFlags.NonPublic)));
                patched = true;
                Plugin.Log.LogMessage("Uncrop's battle view found: the co-op camera zooms out fully with it (zoomed-out frames are kept as battle scene).");
            }
            catch (Exception e) { patched = false; Plugin.Log.LogWarning("Uncrop compatibility: " + e.Message); }
        }

        static bool menuPanel;
#if COOP_DEBUG
        public static int washFrames;   // frames that looked like a menu while the battle ran (not paused)
#endif

        // Before Uncrop's check: it empties the frame's batch list when it's done.
        static void BeforeStartRender()
        {
            try
            {
                // Only while the battle is paused: menus open from the paused battle menu. A big spell's full-screen wash
                // also makes whole-screen layers; taken for a menu, that frame went to Uncrop's own verdict, which zoomed
                // out isn't "scene", and the wash was drawn across the whole 16:9 screen - grey or green side bars.
                menuPanel = battleView && Native.Base != 0 && Battle.Actors() != IntPtr.Zero &&
                    System.Runtime.InteropServices.Marshal.ReadInt16(Native.At(Rva.ZoomNow)) > BattleGround.ZoomGameMax && MenuPanel();
#if COOP_DEBUG
                if (menuPanel && (System.Runtime.InteropServices.Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) == 0) washFrames++;
#endif
                if (menuPanel && (System.Runtime.InteropServices.Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) == 0) menuPanel = false;
            }
            catch { menuPanel = false; }
        }

        static int lastFrames;
        static float lastFrameSeen = -10f;

        // After Uncrop's own check, at the start of rendering a frame (main thread). Only while battle frames are running:
        // the actor list and the zoom stay set after a battle, and the Skills/Items... menus (in the field, and the battle
        // menu's submenus, where the battle frames stop) were drawn as battle scene - cut off at both sides (zarusama).
        static void AfterStartRender()
        {
            try
            {
                float now = UnityEngine.Time.unscaledTime;
                int frames = CoopBattleInput.Frames;
                if (frames != lastFrames) { lastFrames = frames; lastFrameSeen = now; }
                // (The battle menu's own icon row needs the scene kept - characters, the ground past the picture. A submenu
                // opened zoomed out is drawn as scene for about a second, until its battle frames stop.)
                if (!battleView || Native.Base == 0 || Battle.Actors() == IntPtr.Zero || now - lastFrameSeen > 0.25f ||
                    System.Runtime.InteropServices.Marshal.ReadInt16(Native.At(Rva.ZoomNow)) <= BattleGround.ZoomGameMax) return;
                if (menuPanel) return;
                sceneFrame.SetValue(null, true);
                lastSceneFrame.SetValue(null, true);
            }
            catch { }
        }

        // A battle submenu (Skills, Items...) is in this frame: two full-screen layers, or a batch forming a big window
        // inside the screen (depths aren't set yet when this runs). The icon row, HP bars and hit counters are small. Opened zoomed out, a submenu was drawn as
        // battle scene - blown up past the screen - for about a second, until its battle frames stopped.
        static unsafe bool MenuPanel()
        {
            if (frameDraws == null || drawerManager == null || getVertexBuffer == null || drawFirst == null || drawCount == null) return false;
            var list = frameDraws.GetValue(null) as System.Collections.IList;
            object manager = drawerManager.GetValue(null);
            var getVb = getVertexBuffer.GetValue(null) as MethodInfo;
            if (list == null || manager == null || getVb == null) return false;
            byte* vb = (byte*)System.Reflection.Pointer.Unbox(getVb.Invoke(manager, null));
            if (vb == null) return false;
#if COOP_DEBUG
            bool log = UnityEngine.Time.unscaledTime >= nextLog && (System.Runtime.InteropServices.Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) != 0;
            var sb = log ? new System.Text.StringBuilder("Uncrop frame (paused): " + list.Count + " batches;") : null;
            if (log) nextLog = UnityEngine.Time.unscaledTime + 0.3f;
#endif
            bool found = false;
            int wholeCount = 0;
            foreach (object d in list)
            {
                int first = (int)drawFirst.GetValue(d), count = (int)drawCount.GetValue(d);
                if (count <= 0) continue;
                float z = *(float*)(vb + (long)first * 48 + 8);
#if COOP_DEBUG
                if (log)
                {
                    float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
                    for (int i = 0; i < count; i++) { float* v = (float*)(vb + (long)(first + i) * 48); a0 = Math.Min(a0, v[0]); a1 = Math.Max(a1, v[0]); b0 = Math.Min(b0, v[1]); b1 = Math.Max(b1, v[1]); }
                    if (a1 - a0 > 200 || b1 - b0 > 200) sb.AppendFormat(" [z {0:0} n {1} x {2:0}..{3:0} y {4:0}..{5:0}]", z, count, a0, a1, b0, b1);
                }
#endif
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    float* v = (float*)(vb + (long)(first + i) * 48);
                    if (v[0] < x0) x0 = v[0]; if (v[0] > x1) x1 = v[0];
                    if (v[1] < y0) y0 = v[1]; if (v[1] > y1) y1 = v[1];
                }
                // A window inside the screen, most of its width and over half its height (the Skills window: 1708x852).
                // The stage's ground strips reach past the screen's edges and are at most about half its height; full-
                // screen quads (flashes, the menu's dimmed backdrop) cover it all.
                bool inside = x0 >= 0f && x1 <= 1920f && y0 >= 0f && y1 <= 1088f;
                bool whole = x0 <= 1f && x1 >= 1919f && y0 <= 1f && y1 >= 1079f;
                if (whole) wholeCount++;
                if (inside && !whole && x1 - x0 > 1400f && y1 - y0 > 600f) found = true;
            }
            // Submenus (Skills, Strategy, Formation...) are drawn over two full-screen layers - the copied battle picture
            // and the dimming over it; battle frames, the icon row and target select have one.
            if (wholeCount >= 2) found = true;
#if COOP_DEBUG
            if (log) Plugin.Log.LogInfo(sb.Append(" -> menu panel " + found).ToString());
#endif
            return found;
        }
#if COOP_DEBUG
        static float nextLog;
#endif

        // Main thread, every frame.
        public static void Update(float now)
        {
            if (active == null)
            {
                // Uncrop starts through the bridge: look again now and then for the first minute.
                if (searched && (now < nextSearch || now > 60f)) return;
                searched = true;
                nextSearch = now + 2f;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "UncropPlugin") continue;
                    Type view = asm.GetType("UncropPlugin.BattleView", false);
                    if (view != null) active = view.GetField("Active", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
#if COOP_DEBUG
                    if (view != null) UncropProbe.Install(view);
#endif
                    if (active != null && active.FieldType != typeof(bool)) active = null;
                    if (active != null && !patched) Patch(view);
                    break;
                }
                if (active == null) return;
            }
            bool on;
            try { on = (bool)active.GetValue(null); }
            catch { active = null; on = false; }   // looked for again (within the first minute); the patch isn't repeated
            if (on && !reported)
            {
                reported = true;
                Plugin.Log.LogMessage("Uncrop's battle view is on" + (patched ? "." : ": the co-op camera stays within the game's own zoom range while it is."));
            }
            battleView = on;
        }
    }
}
