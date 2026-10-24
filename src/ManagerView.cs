// The controller manager's window, drawn in Tales of Eternia's own battle-UI style: a uGUI canvas with the game's
// TextMeshPro fonts (BorderFont_en - the lettering of its menus, or Font Replacer's Font of Eternia when that mod has
// put it in the game's English slot - and its Frutiger UI font). The look follows the game's arte-name box (a deep
// navy rounded window with a thin silver edge), its "Standard Formation" header (blue-violet triangle arrows) and
// its yellow highlighted names. Every shape is generated here; no game art is copied. Built once, then only moved and
// recoloured. If the game's fonts aren't there (another language, a later version), Available stays false and
// ControllerManager falls back to its plain IMGUI window.
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EterniaCoop
{
    static class ManagerView
    {
        public struct Row
        {
            public string Name;
            public int Player;     // -1 Off, 0-3
            public bool Selected, Active, NoOff;   // NoOff: a setting row (Field control) without the Off choice
        }

        static bool tried;
        static GameObject root;
        static RectTransform panel;
        static TMP_FontAsset border, plain;

        // The game's palette (from its battle UI).
        public static readonly Color Silver = new Color(0.86f, 0.89f, 0.95f), SilverDim = new Color(0.55f, 0.60f, 0.72f);
        public static readonly Color Yellow = new Color(0.98f, 0.86f, 0.36f);                 // highlighted names
        public static readonly Color ArrowBlue = new Color(0.30f, 0.36f, 0.95f), ArrowViolet = new Color(0.55f, 0.30f, 0.90f);
        public static readonly Color Gold = Yellow, GoldDim = SilverDim;                        // for the IMGUI fallback
        public static readonly Color[] PlayerColors =
            { new Color(0.95f, 0.38f, 0.38f), new Color(0.42f, 0.62f, 1f), new Color(0.42f, 0.86f, 0.48f), new Color(1f, 0.84f, 0.32f) };

        public static bool Available
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    try { Build(); }
                    catch (System.Exception e) { Plugin.Log.LogWarning("Controller manager: game fonts unavailable, plain window (" + e.Message + ")"); root = null; }
                }
                return root != null;
            }
        }

        // ---- generated shapes ----
        // A rounded rectangle (filled, or just its edge) as a 9-sliced sprite, anti-aliased.
        static Sprite Rounded(int radius, float edge)
        {
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = radius, c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // distance outside the rounded rectangle (negative inside), for a square of half-size c - 1
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - c) - (c - 1 - r), 0), dy = Mathf.Max(Mathf.Abs(y + 0.5f - c) - (c - 1 - r), 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (edge > 0) a *= Mathf.Clamp01(edge + 0.5f + d);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            float b = radius + 2;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        // A right-pointing triangle, like the game's formation arrows.
        static Sprite Triangle()
        {
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float fy = Mathf.Abs(y + 0.5f - S / 2f) / (S / 2f), fx = (x + 0.5f) / S;   // inside when fy < 1 - fx
                    float a = Mathf.Clamp01((1 - fx - fy) * S * 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        // A vertical gradient strip (top colour at the top).
        static Texture2D Gradient(Color top, Color bottom)
        {
            var tex = new Texture2D(1, 64) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            tex.Apply();
            return tex;
        }

        // A horizontal line that fades out at both ends.
        static Texture2D FadeLine()
        {
            var tex = new Texture2D(64, 1) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, new Color(1, 1, 1, Mathf.Sin(Mathf.PI * (x + 0.5f) / 64f)));
            tex.Apply();
            return tex;
        }

        // A highlight bar: strong at the left, fading to the right (the selected row).
        static Texture2D Sweep()
        {
            var tex = new Texture2D(64, 1) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, new Color(1, 1, 1, Mathf.Lerp(1f, 0.15f, x / 63f)));
            tex.Apply();
            return tex;
        }

        static Sprite bigFill, bigEdge, smallFill, smallEdge, triangle;
        static Texture2D windowGradient, fadeLine, sweep;

        // ---- building blocks ----
        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            return rt;
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static Image Img(Transform parent, Sprite sprite)
        {
            var img = Node("img", parent).gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = sprite;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        static RawImage Raw(Transform parent, Texture tex)
        {
            var img = Node("raw", parent).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = tex;
            return img;
        }

        // A rounded box: fill and edge.
        class Box
        {
            public RectTransform Rt;
            readonly Image back, edge;

            public Box(Transform parent, bool big)
            {
                Rt = Node("box", parent);
                back = Img(Rt, big ? bigFill : smallFill);
                edge = Img(Rt, big ? bigEdge : smallEdge);
            }

            public void Set(float x, float y, float w, float h, Color fill, Color line)
            {
                Place(Rt, x, y, w, h);
                Place(back.rectTransform, 0, 0, w, h);
                Place(edge.rectTransform, 0, 0, w, h);
                back.color = fill; back.enabled = fill.a > 0;
                edge.color = line; edge.enabled = line.a > 0;
            }
        }

        static TextMeshProUGUI Label(Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions align, float outline)
        {
            var t = Node("text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = false;
            if (outline > 0) { t.outlineWidth = outline; t.outlineColor = new Color32(0, 0, 0, 230); }
            return t;
        }

        static void Text(TextMeshProUGUI t, float x, float y, float w, float h, string s, Color c)
        {
            Place(t.rectTransform, x, y, w, h);
            if (t.text != s) t.text = s;
            t.color = c;
            t.gameObject.SetActive(true);
        }

        // The game's header arrows: overlapping triangles, blue to violet.
        static Image[] Arrows(Transform parent, int n)
        {
            var a = new Image[n];
            for (int i = 0; i < n; i++) { a[i] = Img(parent, triangle); a[i].color = Color.Lerp(ArrowBlue, ArrowViolet, n == 1 ? 0 : i / (n - 1f)); }
            return a;
        }

        static void PlaceArrows(Image[] a, float x, float yMid, float size, float step)
        {
            for (int i = 0; i < a.Length; i++) Place(a[i].rectTransform, x + i * step, yMid - size / 2, size, size);
        }

        // ---- the cast ----
        // The game's own character illustrations (face_pic1-4: Reid, Farah, Keele, Meredy - the art its status menus
        // use), copied on the GPU from the copies the game has loaded (they're in memory on the title screen), so the
        // window can show them later even when the game has let go of its own. Nothing is shipped with the mod.
        static readonly string[] CastNames = { "face_pic1", "face_pic2", "face_pic3", "face_pic4" };   // Reid, Farah, Keele, Meredy
        static readonly Texture[] cast = new Texture[4];
        static float nextLook;

        // Main thread, called every frame by ControllerManager; looks once a second until all four are copied.
        public static void FindCast()
        {
            // A copy the GPU lost (device reset) is made again while the game still has the original.
            for (int i = 0; i < 4; i++)
            {
                var rt = cast[i] as RenderTexture;
                if (rt != null && !rt.IsCreated()) { Object.Destroy(rt); cast[i] = null; }
            }
            if (cast[0] != null && cast[1] != null && cast[2] != null && cast[3] != null) return;
            // Searching the loaded textures costs a little; stop after a few minutes if they never turn up.
            if (Time.unscaledTime < nextLook || Time.unscaledTime > 300f && cast[0] == null) return;
            nextLook = Time.unscaledTime + 1f;
            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    int i = System.Array.IndexOf(CastNames, t.name);
                    if (i < 0 || cast[i] != null) continue;
                    var copy = new RenderTexture(t.width, t.height, 0, RenderTextureFormat.ARGB32) { name = "EterniaCoop " + t.name };
                    Graphics.Blit(t, copy);
                    cast[i] = copy;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Controller manager art: " + e.Message); nextLook = float.MaxValue; }
        }

        // Where each one stands across the window, in player order left to right like the P1-P4 choices (Reid, Farah,
        // Meredy, Keele), as fractions of the window: centre x, top y, height; opacity; mirrored (Reid and Keele, so
        // they look in towards each other).
        static readonly float[][] CastLayout =
        {
            new[] { 0.14f, 0.02f, 1.15f, 0.24f, 1 },   // Reid
            new[] { 0.38f, 0.02f, 1.15f, 0.24f, 0 },   // Farah
            new[] { 0.86f, 0.02f, 1.15f, 0.24f, 1 },   // Keele, looking back towards Reid
            new[] { 0.62f, 0.06f, 1.03f, 0.24f, 0 },   // Meredy, looking at Keele (her art fills more of its frame: scaled to Farah's height)
        };
        static RawImage[] castImages;

        // ---- the window ----
        static Image dim;
        static RectTransform window;
        static RawImage back;
        static Image windowEdge;
        static RawImage windowGlow;
        static Image[] titleArrows;
        static TextMeshProUGUI title, footer, note, message;
        static RawImage rule1, rule2;
        static RectTransform hintRow;   // the button hints, scaled down as one line if they'd be wider than the window

        class RowView
        {
            public RectTransform Rt;
            public RawImage Highlight;
            public Image Activity;
            public Image[] Cursor;
            public TextMeshProUGUI Name;
            public Box[] Pills = new Box[5];
            public TextMeshProUGUI[] PillText = new TextMeshProUGUI[5];
        }
        static readonly List<RowView> rows = new List<RowView>();

        class HintView { public Box Key; public TextMeshProUGUI KeyText, Meaning; }
        static readonly List<HintView> hints = new List<HintView>();

        static TMP_FontAsset FindFont(string name)
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) if (f.name == name) return f;
            return null;
        }

        static void Build()
        {
            // Font Replacer puts its Font of Eternia into the game's English font slot: use it too when it's there.
            border = FindFont("FontOfEternia_Runtime") ?? FindFont("BorderFont_en");
            plain = FindFont("FrutigerNeueLTW1G-CnRg SDF") ?? border;
            if (border == null) return;

            bigFill = Rounded(16, 0); bigEdge = Rounded(16, 2.2f);
            smallFill = Rounded(6, 0); smallEdge = Rounded(6, 1.4f);
            triangle = Triangle();
            windowGradient = Gradient(new Color(0.11f, 0.22f, 0.45f, 0.97f), new Color(0.03f, 0.07f, 0.19f, 0.97f));
            fadeLine = FadeLine();
            sweep = Sweep();

            root = new GameObject("EterniaCoop Controller Manager");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;   // sized by screen height, like the game's 4:3 menus

            dim = Img(root.transform, null);
            var drt = dim.rectTransform;
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.sizeDelta = Vector2.zero; drt.anchoredPosition = Vector2.zero;
            dim.color = new Color(0, 0, 0, 0.45f);

            panel = Node("panel", root.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);

            // The window: a rounded mask holding the navy gradient, a soft top glow, then the silver edge.
            var maskImg = Img(panel, bigFill);
            window = maskImg.rectTransform;
            maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            back = Raw(window, windowGradient);
            windowGlow = Raw(window, Gradient(new Color(0.55f, 0.70f, 1f, 0.14f), new Color(0.55f, 0.70f, 1f, 0f)));
            castImages = new RawImage[4];
            foreach (int i in new[] { 2, 3, 1, 0 }) castImages[i] = Raw(window, null);   // back to front: Keele, Meredy, Farah, Reid
            windowEdge = Img(panel, bigEdge);
            windowEdge.color = Silver;

            titleArrows = Arrows(panel, 3);
            title = Label(panel, border, 46, TextAlignmentOptions.Left, 0);
            rule1 = Raw(panel, fadeLine);
            rule2 = Raw(panel, fadeLine);
            hintRow = Node("hints", panel);
            footer = Label(panel, plain, 19, TextAlignmentOptions.Center, 0.15f);
            note = Label(panel, plain, 18, TextAlignmentOptions.Center, 0.15f);
            message = Label(panel, border, 28, TextAlignmentOptions.Center, 0);
            root.SetActive(false);
            Plugin.Log.LogInfo("Controller manager: drawn with the game's fonts (" + border.name + ", " + plain.name + ")");
        }

        static RowView GetRow(int i)
        {
            while (rows.Count <= i)
            {
                var r = new RowView { Rt = Node("row", panel) };
                r.Highlight = Raw(r.Rt, sweep);
                r.Activity = Img(r.Rt, smallFill);
                r.Cursor = Arrows(r.Rt, 2);
                r.Name = Label(r.Rt, border, 32, TextAlignmentOptions.Left, 0);
                for (int k = 0; k < 5; k++)
                {
                    r.Pills[k] = new Box(r.Rt, false);
                    r.PillText[k] = Label(r.Pills[k].Rt, border, 25, TextAlignmentOptions.Center, 0);
                }
                rows.Add(r);
            }
            return rows[i];
        }

        static HintView GetHint(int i)
        {
            while (hints.Count <= i)
            {
                var h = new HintView { Key = new Box(hintRow, false) };
                h.KeyText = Label(h.Key.Rt, border, 23, TextAlignmentOptions.Center, 0);
                h.Meaning = Label(hintRow, plain, 21, TextAlignmentOptions.Left, 0.15f);
                hints.Add(h);
            }
            return hints[i];
        }

        public static void Hide() { if (root != null && root.activeSelf) root.SetActive(false); }

        // Lays out and shows the window (main thread, every frame while the manager is open).
        public static void Show(List<Row> list, string[][] hintList, string footerText, string noteText, string messageText)
        {
            if (!Available) return;
            if (!root.activeSelf) root.SetActive(true);

            const float W = 1060, RowH = 54, Pad = 34;
            float h = 108 + RowH * list.Count + 30 + 48 + 26 + (noteText == "" ? 0 : 26) + 54;
            Place(panel, 0, 0, W, h);
            panel.anchoredPosition = Vector2.zero;
            Place(window, 0, 0, W, h);
            Place(back.rectTransform, 0, 0, W, h);
            Place(windowGlow.rectTransform, 0, 0, W, 90);
            Place(windowEdge.rectTransform, 0, 0, W, h);
            for (int i = 0; i < 4; i++)
            {
                var img = castImages[i];
                img.enabled = cast[i] != null;
                if (cast[i] == null) continue;
                if (img.texture != cast[i]) img.texture = cast[i];
                float[] l = CastLayout[i];
                float ch = h * l[2], cw = ch * cast[i].width / cast[i].height;
                Place(img.rectTransform, W * l[0] - cw / 2, h * l[1], cw, ch);
                img.color = new Color(1, 1, 1, l[3]);
                img.uvRect = l[4] > 0 ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
            }

            // Title: "▶▶▶ Controllers", centred as a group, like the game's formation header.
            float titleW = title.GetPreferredValues("Controllers").x, arrowsW = 26 + 2 * 15, groupW = arrowsW + 12 + titleW;
            float tx = (W - groupW) / 2;
            PlaceArrows(titleArrows, tx, 52, 26, 15);
            Text(title, tx + arrowsW + 12, 22, titleW + 10, 60, "Controllers", Color.white);
            Place(rule1.rectTransform, W * 0.12f, 88, W * 0.76f, 2); rule1.color = new Color(Silver.r, Silver.g, Silver.b, 0.55f);

            float y = 108, pillW = 66, gap = 8, rw = W - 2 * Pad, pillsX = rw - 5 * pillW - 4 * gap - 6;
            for (int i = 0; i < rows.Count || i < list.Count; i++)
            {
                if (i >= list.Count) { GetRow(i).Rt.gameObject.SetActive(false); continue; }
                var r = GetRow(i);
                var row = list[i];
                float rh = RowH - 8;
                r.Rt.gameObject.SetActive(true);
                Place(r.Rt, Pad, y, rw, rh);

                r.Highlight.enabled = row.Selected;
                r.Highlight.color = new Color(0.35f, 0.45f, 0.95f, 0.40f);
                Place(r.Highlight.rectTransform, 0, 0, rw, rh);
                r.Activity.enabled = row.Active;
                r.Activity.color = Color.white;
                Place(r.Activity.rectTransform, 0, 4, 5, rh - 8);
                foreach (var a in r.Cursor) a.enabled = row.Selected;
                PlaceArrows(r.Cursor, 12, rh / 2, 18, 10);
                Text(r.Name, 52, 0, pillsX - 64, rh, row.Name,
                    row.Selected ? Yellow : row.NoOff ? new Color(0.62f, 0.78f, 1f) : new Color(0.92f, 0.94f, 1f));

                for (int k = 0; k < 5; k++)
                {
                    int p = k - 1;
                    r.Pills[k].Rt.gameObject.SetActive(!(row.NoOff && p < 0));
                    if (row.NoOff && p < 0) continue;
                    bool on = row.Player == p;
                    Color c = p < 0 ? new Color(0.70f, 0.72f, 0.80f) : PlayerColors[p];
                    float ph = rh * 0.70f;
                    r.Pills[k].Set(pillsX + k * (pillW + gap), (rh - ph) / 2, pillW, ph,
                        on ? new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.60f, 0.95f) : new Color(0.02f, 0.05f, 0.14f, 0.55f),
                        on ? Color.Lerp(c, Color.white, 0.35f) : new Color(SilverDim.r, SilverDim.g, SilverDim.b, 0.45f));
                    Text(r.PillText[k], 0, 0, pillW, ph, p < 0 ? "Off" : "P" + (p + 1), on ? Color.white : new Color(1, 1, 1, 0.38f));
                }
                y += RowH;
            }

            y += 12;
            Place(rule2.rectTransform, W * 0.08f, y, W * 0.84f, 2); rule2.color = new Color(Silver.r, Silver.g, Silver.b, 0.40f);
            y += 18;

            // Button hints, centred as one line; measured from the text itself, and scaled down to fit if needed.
            float total = 0;
            var widths = new float[hintList.Length * 2];
            for (int i = 0; i < hintList.Length; i++)
            {
                var hv = GetHint(i);
                widths[i * 2] = Mathf.Max(hv.KeyText.GetPreferredValues(hintList[i][0]).x + 20, 36);
                widths[i * 2 + 1] = hv.Meaning.GetPreferredValues(hintList[i][1]).x;
                total += widths[i * 2] + 8 + widths[i * 2 + 1] + (i < hintList.Length - 1 ? 26 : 0);
            }
            float room = W - 2 * Pad - 24, scale = total > room ? room / total : 1f;
            Place(hintRow, (W - total * scale) / 2, y + (34 - 34 * scale) / 2, total, 34);
            hintRow.localScale = new Vector3(scale, scale, 1);
            float x = 0;
            for (int i = 0; i < hints.Count; i++)
            {
                var hv = GetHint(i);
                bool used = i < hintList.Length;
                hv.Key.Rt.gameObject.SetActive(used);
                hv.Meaning.gameObject.SetActive(used);
                if (!used) continue;
                hv.Key.Set(x, 0, widths[i * 2], 34, new Color(0.02f, 0.05f, 0.14f, 0.7f), Silver);
                Text(hv.KeyText, 0, 0, widths[i * 2], 34, hintList[i][0], Yellow);
                x += widths[i * 2] + 8;
                Text(hv.Meaning, x, 0, widths[i * 2 + 1] + 4, 34, hintList[i][1], Color.white);
                x += widths[i * 2 + 1] + 26;
            }
            y += 50;
            Text(footer, Pad, y, W - 2 * Pad, 26, footerText, new Color(0.85f, 0.88f, 0.98f, 0.75f));
            y += 26;
            if (noteText != "") { Text(note, Pad, y, W - 2 * Pad, 26, noteText, new Color(0.85f, 0.88f, 0.98f, 0.6f)); y += 26; }
            else note.gameObject.SetActive(false);
            if (messageText != null) Text(message, Pad, h - 50, W - 2 * Pad, 34, messageText, Yellow);
            else message.gameObject.SetActive(false);
        }
    }
}
