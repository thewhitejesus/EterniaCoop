// The PS1 battle radar, rebuilt. The PSP port (and so the remaster) dropped it; nothing of it is left in GameNative.
// The rules are the PS1's own, read from Tales of Destiny II (SLUS_013.55, function 0x80060954; see AGENTS.md):
//   - shown only while someone other than player 1 plays (the PS1 checks this every frame);
//   - two lines at y = 51 of the 320x240 picture: the whole battlefield in red (x 140..224), the part on screen in
//     purple (40 px at the game's normal zoom: here it follows the co-op camera's zoom);
//   - one small pixel glyph per fighter on its feet at x = world x / 128 + 160, y = 48 + height / 64 (fliers sit
//     higher): each player's number in their colour, a red E for each enemy.
// Characters on Auto show their slot's number in that slot's colour, dimmed (the player who would take it by joining).
// The PS1 drew 8x8 glyphs from its HUD texture, which the remaster doesn't have (and its battle HUD has no Unity text
// to borrow a font from: it's drawn natively). These are the mod's own 5x7 pixel glyphs with a one-pixel dark border,
// scaled with hard pixel edges.
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EterniaCoop
{
    static class Radar
    {
        // Filled by the battle input hook every running battle frame (which command blocks a person plays). The hook
        // may run off the main thread: tick tells Update whether the battle is running (it stops while paused).
        static readonly bool[] humans = new bool[4];
        static volatile int tick;
        static int seenTick, staleFrames;

        public static void Mark(bool[] wanted, int players)
        {
            humans[0] = true;
            for (int p = 1; p < 4; p++) humans[p] = wanted[p] && p < players;
            tick++;
        }

        // Players 1-4: blue, green, yellow, pink (the PS1 used orange, blue, green, purple; changed by request, away from
        // the enemies' red and the line's purple). Enemies keep the PS1's red; Six Person's slots 5-6 (no player): grey.
        static readonly Color[] PlayerColor =
        {
            new Color32(0x60, 0xA8, 0xFF, 0xFF), new Color32(0x70, 0xF0, 0x70, 0xFF),
            new Color32(0xF8, 0xE0, 0x50, 0xFF), new Color32(0xF8, 0x88, 0xE0, 0xFF),
        };
        static readonly Color EnemyColor = new Color32(0xF0, 0x40, 0x40, 0xFF), NoPlayerColor = new Color32(0xA0, 0xA0, 0xA0, 0xFF);
        static readonly Color FieldColor = new Color32(0x80, 0x00, 0x00, 0xFF), ViewColor = new Color32(0x40, 0x00, 0x80, 0xFF);

        // 5x7 glyphs: 1-6 (party slots; Six Person adds 5 and 6) and E. '#' = lit.
        static readonly string[][] Shapes =
        {
            new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            new[] { "####.", "#....", "#....", "###..", "#....", "#....", "####." },   // E: short arms, the old 8-bit shape
        };
        const int GlyphE = 6, CellW = 7, CellH = 9;   // glyph plus a one-pixel border all round

        const float LineY = 51f, FieldLeft = 140f, FieldRight = 224f;

        static GameObject root;
        static RectTransform field, view;
        static RawImage[] markers;
        static Texture2D[] glyphs;

        public static void Update()
        {
            bool show = Visible();
            if (!show) { if (root != null && root.activeSelf) root.SetActive(false); return; }
            if (root == null) Build();
            if (!root.activeSelf) root.SetActive(true);
            Layout();
        }

        static bool Visible()
        {
            if (!Settings.Radar.Value || !Plugin.CoopActive || !Features.Camera || Native.Base == 0) return false;
            IntPtr actors = Battle.Actors();
            if (actors == IntPtr.Zero) { staleFrames = 99; return false; }
            // The battle input hook ran since the last few frames: the battle is running (not paused, no menu).
            int t = tick;
            if (t != seenTick) { seenTick = t; staleFrames = 0; } else if (staleFrames < 99) staleFrames++;
            if (staleFrames > 3) return false;
            if (!humans[1] && !humans[2] && !humans[3]) return false;          // the PS1: multiplayer only
            if (Marshal.ReadByte(Native.At(Rva.CamMode)) != 0) return false;   // battle intro, scripted camera
            for (int i = 0; i < Rva.ActorCount; i++)                           // won: the results screen follows
            {
                IntPtr a = Battle.ActorAt(actors, i);
                if (Marshal.ReadInt16(a, Actor.Kind) == 1 && Marshal.ReadByte(a, Actor.Side) != 0) return true;
            }
            return false;
        }

        static void Build()
        {
            root = new GameObject("EterniaCoop Radar");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;   // under the controller manager (32000)
            field = Line("field", FieldColor);
            view = Line("view", ViewColor);

            // White glyph pixels (tinted per marker), a near-black border (stays dark under any tint), clear elsewhere.
            glyphs = new Texture2D[Shapes.Length];
            for (int g = 0; g < Shapes.Length; g++)
            {
                var tex = new Texture2D(CellW, CellH, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color32[CellW * CellH];
                for (int y = 0; y < CellH; y++)
                    for (int x = 0; x < CellW; x++)
                    {
                        Color32 c = new Color32(0, 0, 0, 0);
                        if (Lit(g, x, y)) c = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
                        // The border follows the glyph's shape (the four sides only), like the PS1's: not a box.
                        else if (Lit(g, x - 1, y) || Lit(g, x + 1, y) || Lit(g, x, y - 1) || Lit(g, x, y + 1))
                            c = new Color32(0x10, 0x10, 0x10, 0xFF);
                        px[(CellH - 1 - y) * CellW + x] = c;   // textures start at the bottom row
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                glyphs[g] = tex;
            }
            markers = new RawImage[Rva.ActorCount];
            for (int i = 0; i < markers.Length; i++)
            {
                var go = new GameObject("marker", typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                var img = go.AddComponent<RawImage>();
                img.raycastTarget = false;
                Anchor(img.rectTransform, new Vector2(0.5f, 0.5f));
                go.SetActive(false);
                markers[i] = img;
            }
        }

        // Pixel (x, y) of glyph g's cell, y from the top; the glyph sits one pixel in from each edge.
        static bool Lit(int g, int x, int y)
        {
            x -= 1; y -= 1;
            return x >= 0 && x < 5 && y >= 0 && y < 7 && Shapes[g][y][x] == '#';
        }

        static RectTransform Line(string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            Anchor(img.rectTransform, new Vector2(0f, 1f));
            return img.rectTransform;
        }

        // Positions are given in pixels from the screen's top-left corner.
        static void Anchor(RectTransform r, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = pivot;
        }

        static void Layout()
        {
            // The PS1 picture (320x240) stretched over the whole screen, as the remaster lays out its battle HUD (the
            // enemy list and party status sit at the same fractions of the width as on the PS1): the radar keeps the
            // PS1's place, 44% to 70% across, right of centre (the field reaches further right of x = 0 than left).
            // Heights (and the glyphs) use the vertical scale, so nothing is stretched.
            float s = Screen.height / 240f, sx = Screen.width / 320f;
            float thick = Mathf.Max(1f, Mathf.Round(s));
            Place(field, FieldLeft * sx, LineY * s, (FieldRight - FieldLeft) * sx, thick);

            // The part on screen: centred on the camera (scroll = -(centre x >> 4)), as wide as the zoom shows.
            int zoomAt = Features.Ground ? Rva.ZoomNow : Rva.CamZoom;
            float centre = 160f - Marshal.ReadInt16(Native.At(Rva.CamScrollNow)) / 8f;
            float half = CoopCamera.ViewWidth(Marshal.ReadInt16(Native.At(zoomAt))) / 16f;
            float left = Mathf.Max(FieldLeft, centre - half), right = Mathf.Min(FieldRight, centre + half);
            Place(view, left * sx, LineY * s, Mathf.Max(0f, right - left) * sx, thick);

            // Whole screen pixels per PS1 pixel, so every glyph pixel comes out the same size.
            float unit = Mathf.Max(1f, Mathf.Floor(s));
            IntPtr actors = Battle.Actors();
            int used = 0;
            // Enemies first, so the players' numbers are drawn on top.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < Rva.ActorCount && used < markers.Length; i++)
                {
                    IntPtr a = Battle.ActorAt(actors, i);
                    if (Marshal.ReadInt16(a, Actor.Kind) != 1) continue;
                    bool enemy = Marshal.ReadByte(a, Actor.Side) != 0;
                    if (enemy != (pass == 0)) continue;
                    int glyph; Color color;
                    if (enemy) { glyph = GlyphE; color = EnemyColor; }
                    else
                    {
                        int block = Marshal.ReadByte(a, Actor.Block);
                        if (block >= GlyphE) continue;
                        bool human = block < 4 && humans[block] && Marshal.ReadByte(a, Actor.Control) != 0;
                        glyph = block;
                        // On Auto: the slot's colour, dimmed (the player who would take it by joining).
                        color = block >= 4 ? NoPlayerColor : human ? PlayerColor[block] : Color.Lerp(PlayerColor[block], Color.black, 0.45f);
                    }
                    float px = Marshal.ReadInt16(a, Actor.X) / 128f + 160f;
                    float py = 48f + (Marshal.ReadInt16(a, Actor.Y) >> 6);
                    var m = markers[used++];
                    if (m.texture != glyphs[glyph]) m.texture = glyphs[glyph];
                    m.color = color;
                    Place(m.rectTransform, Mathf.Round(px * sx), Mathf.Round(py * s), CellW * unit, CellH * unit);
                    if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);
                }
            for (int i = used; i < markers.Length; i++)
                if (markers[i].gameObject.activeSelf) markers[i].gameObject.SetActive(false);
        }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }
    }
}
