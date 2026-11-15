// Uncrop (a BepInEx 6 mod, through the bridge) 1.7+ has a battle view ("Uncrop battles"): it shows the PS1's whole
// 320x208 battle picture, scaled to the screen height, by giving each batch of the game's drawing its own viewport. It
// sorts the batches by depth and look: characters, and ground it recognises as the stage's scenery (by texture name
// or by its size on screen), get the scene scaling; anything else it takes for the HUD. Zoomed out past the game's own
// limit, the co-op camera draws the ground too small for that test, so the ground and the shadows on it lost the
// scaling while the characters kept it: everyone floated high above their shadows (reported by zarusama).
//
// So while Uncrop's battle view is on, the co-op camera stays within the game's own zoom range (where Uncrop's own
// view already shows more of the field), and the ground extension isn't needed. Its switch is read once a frame on the
// main thread; the battle thread only reads the copy.
using System;
using System.Reflection;

namespace EterniaCoop
{
    static class UncropCompat
    {
        static FieldInfo active;      // UncropPlugin.BattleView.Active (static bool)
        static bool searched;
        static float nextSearch;
        static volatile bool battleView;
        static bool reported;

        // Uncrop's battle view is drawing this battle.
        public static bool BattleView { get { return battleView; } }

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
                    if (active != null && active.FieldType != typeof(bool)) active = null;
                    break;
                }
                if (active == null) return;
            }
            bool on;
            try { on = (bool)active.GetValue(null); }
            catch { active = null; on = false; }
            if (on && !reported)
            {
                reported = true;
                Plugin.Log.LogMessage("Uncrop's battle view is on: the co-op camera stays within the game's own zoom range while it is.");
            }
            battleView = on;
        }
    }
}
