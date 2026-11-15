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
                new HarmonyLib.Harmony(Plugin.Guid + ".uncrop").Patch(start, null,
                    new HarmonyLib.HarmonyMethod(typeof(UncropCompat).GetMethod("AfterStartRender", BindingFlags.Static | BindingFlags.NonPublic)));
                patched = true;
                Plugin.Log.LogMessage("Uncrop's battle view found: the co-op camera zooms out fully with it (zoomed-out frames are kept as battle scene).");
            }
            catch (Exception e) { patched = false; Plugin.Log.LogWarning("Uncrop compatibility: " + e.Message); }
        }

        // After Uncrop's own check, at the start of rendering a frame (main thread).
        static void AfterStartRender()
        {
            try
            {
                if (!battleView || Native.Base == 0 || Battle.Actors() == IntPtr.Zero ||
                    System.Runtime.InteropServices.Marshal.ReadInt16(Native.At(Rva.ZoomNow)) <= BattleGround.ZoomGameMax) return;
                sceneFrame.SetValue(null, true);
                lastSceneFrame.SetValue(null, true);
            }
            catch { }
        }

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
