using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Maze
{
    // The holographic UI kit shared by the main menu, the HUD and the pause / game-over panels:
    // palette, font, procedurally drawn sprites and icons, layout helpers and the glass panel
    // button. Everything is generated in code (no prefabs, no sprite assets).
    public static class Holo
    {
        public static readonly Color Cyan = new Color(0.40f, 0.88f, 1f);
        public static readonly Color CyanDeep = new Color(0.10f, 0.50f, 0.80f);
        public static readonly Color Acid = new Color(0.70f, 1f, 0.24f);
        public static readonly Color Glass = new Color(0.02f, 0.09f, 0.15f, 0.72f);
        public static readonly Color Ink = new Color(0.90f, 0.98f, 1f);
        public static readonly Color Muted = new Color(0.52f, 0.68f, 0.78f);
        public static readonly Color Red = new Color(1f, 0.30f, 0.32f);
        public static readonly Color Purple = new Color(0.72f, 0.38f, 1f);

        // Smallest button side in canvas units. Canvases are 1080 units tall (match height), so this
        // is >= 44 pt on every iPhone in landscape, down to the 375 pt tall SE.
        public const float MinTouch = 132f;

        static Font font;
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>("UI/Rajdhani-SemiBold");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Color A(Color c, float a) { c.a = a; return c; }

        // ------------------------------------------------------------ texture painting
        public static Texture2D Tex(int w, int h, Func<float, float, Color> f, bool repeat = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) px[y * w + x] = f(x + 0.5f, y + 0.5f);
            t.SetPixels(px); t.Apply(false, true);
            return t;
        }
        static Sprite Spr(Texture2D t, float border) =>
            Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        static Color W(float a) => new Color(1, 1, 1, Mathf.Clamp01(a));

        // signed distance to a box (half size bx, by) centered on the origin, all four corners cut by c
        public static float Chamfer(float x, float y, float bx, float by, float c)
        {
            float qx = Mathf.Abs(x) - bx, qy = Mathf.Abs(y) - by;
            float box = Mathf.Min(Mathf.Max(qx, qy), 0) + new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            float cut = (Mathf.Abs(x) + Mathf.Abs(y) - (bx + by - c)) * 0.70710678f;
            return Mathf.Max(box, cut);
        }

        static Sprite fill, edge, glow, brackets, soft, dot, white;
        static Texture2D grid, scan, band, hazard, vignette, leftShade;

        public static Sprite FillSprite => fill != null ? fill : fill = Spr(Tex(64, 64, (x, y) => W(0.5f - Chamfer(x - 32, y - 32, 31, 31, 15))), 22);
        public static Sprite EdgeSprite => edge != null ? edge : edge = Spr(Tex(64, 64, (x, y) => W(1.6f - Mathf.Abs(Chamfer(x - 32, y - 32, 30.5f, 30.5f, 15) + 1.6f))), 22);
        public static Sprite GlowSprite => glow != null ? glow : glow = Spr(Tex(160, 160, (x, y) =>
        {
            float d = Chamfer(x - 80, y - 80, 40, 40, 15);
            return W(d < 0 ? 0.35f : Mathf.Exp(-d * d / (2 * 13f * 13f)));
        }), 62);
        public static Sprite BracketSprite => brackets != null ? brackets : brackets = Spr(Tex(64, 64, (x, y) =>
        {
            var p = new Vector2(Mathf.Min(x, 64 - x), Mathf.Min(y, 64 - y));
            float d = Mathf.Min(Seg(p, new Vector2(3, 3), new Vector2(21, 3)), Seg(p, new Vector2(3, 3), new Vector2(3, 21))) - 1.4f;
            return W(0.5f - d);
        }), 26);
        public static Sprite SoftSprite => soft != null ? soft : soft = Spr(Tex(128, 128, (x, y) =>
        {
            float r = new Vector2(x - 64, y - 64).magnitude / 64f;
            return W(Mathf.Exp(-r * r * 4.5f) * Mathf.Clamp01((1 - r) * 6));
        }), 0);
        public static Sprite DotSprite => dot != null ? dot : dot = Spr(Tex(32, 32, (x, y) => W(15.5f - new Vector2(x - 16, y - 16).magnitude)), 0);
        public static Sprite WhiteSprite => white != null ? white : white = Spr(Tex(4, 4, (x, y) => Color.white), 0);

        public static Texture2D GridTex => grid != null ? grid : grid = Tex(32, 32, (x, y) =>
        {
            float line = (x < 1 || y < 1) ? 0.55f : 0;
            float cross = (Mathf.Abs(x - 16) < 1 && Mathf.Abs(y - 16) < 3) || (Mathf.Abs(y - 16) < 1 && Mathf.Abs(x - 16) < 3) ? 0.35f : 0;
            return W(Mathf.Max(line, cross));
        }, true);
        public static Texture2D ScanTex => scan != null ? scan : scan = Tex(2, 4, (x, y) => W(y < 1 ? 1 : 0), true);
        public static Texture2D BandTex => band != null ? band : band = Tex(4, 64, (x, y) => W(Mathf.Pow(Mathf.Sin(Mathf.PI * y / 64f), 4)));
        static Texture2D bandX;
        public static Texture2D BandXTex => bandX != null ? bandX : bandX = Tex(64, 4, (x, y) => W(Mathf.Pow(Mathf.Sin(Mathf.PI * x / 64f), 4)));
        public static Texture2D HazardTex => hazard != null ? hazard : hazard = Tex(32, 32, (x, y) => ((x + y) % 32) < 16 ? new Color(0.70f, 1f, 0.24f, 1) : new Color(0.04f, 0.06f, 0.08f, 1), true);
        public static Texture2D VignetteTex => vignette != null ? vignette : vignette = Tex(128, 64, (x, y) =>
        {
            var d = new Vector2((x / 128f - 0.5f) * 2, (y / 64f - 0.5f) * 2 * 0.9f).magnitude;
            return new Color(1, 1, 1, Mathf.SmoothStep(0, 0.85f, Mathf.InverseLerp(0.6f, 1.35f, d)));
        });
        public static Texture2D LeftShadeTex => leftShade != null ? leftShade : leftShade = Tex(128, 4, (x, y) => new Color(0, 0, 0, Mathf.Pow(1 - x / 128f, 1.5f)));

        // ------------------------------------------------------------ icons (signed distance shapes)
        static readonly Dictionary<string, Sprite> icons = new();

        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a; var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }
        static float Box(Vector2 p, float cx, float cy, float bx, float by)
        {
            float qx = Mathf.Abs(p.x - cx) - bx, qy = Mathf.Abs(p.y - cy) - by;
            return Mathf.Min(Mathf.Max(qx, qy), 0) + new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
        }
        static float Circ(Vector2 p, float cx, float cy, float r) => (p - new Vector2(cx, cy)).magnitude - r;
        static Vector2 V(float x, float y) => new Vector2(x, y);
        static float Poly(Vector2 p, params Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]), s = 1;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i]; var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }
        static float Bust(Vector2 p, float cx, float cy, float s)
        {
            float head = Circ(p, cx, cy + 0.34f * s, 0.21f * s);
            float body = Mathf.Max(Circ(p, cx, cy - 0.42f * s, 0.46f * s), (cy - 0.42f * s) - p.y);
            return Mathf.Min(head, body);
        }

        static float IconSdf(string name, Vector2 p)
        {
            switch (name)
            {
                case "deploy":
                    {
                        float pod = Poly(p, V(-0.36f, 0.26f), V(0.36f, 0.26f), V(0.24f, -0.42f), V(0, -0.64f), V(-0.24f, -0.42f));
                        float fins = Mathf.Min(Poly(p, V(-0.36f, 0.26f), V(-0.5f, 0.0f), V(-0.3f, -0.05f)), Poly(p, V(0.36f, 0.26f), V(0.3f, -0.05f), V(0.5f, 0.0f)));
                        float streaks = Mathf.Min(Seg(p, V(-0.2f, 0.42f), V(-0.2f, 0.82f)), Mathf.Min(Seg(p, V(0, 0.4f), V(0, 0.95f)), Seg(p, V(0.2f, 0.42f), V(0.2f, 0.82f)))) - 0.055f;
                        float window = Circ(p, 0, -0.08f, 0.1f);
                        return Mathf.Min(Mathf.Max(Mathf.Min(pod, fins), -window), streaks);
                    }
                case "chevrons":
                    {
                        float a = Mathf.Min(Seg(p, V(-0.6f, 0.5f), V(-0.12f, 0)), Seg(p, V(-0.12f, 0), V(-0.6f, -0.5f)));
                        float b = Mathf.Min(Seg(p, V(0.0f, 0.5f), V(0.48f, 0)), Seg(p, V(0.48f, 0), V(0.0f, -0.5f)));
                        return Mathf.Min(a, b) - 0.11f;
                    }
                case "rifle":
                    {
                        float stock = Poly(p, V(-0.95f, 0.14f), V(-0.52f, 0.14f), V(-0.52f, -0.04f), V(-0.95f, -0.26f));
                        float body = Box(p, -0.08f, 0.08f, 0.46f, 0.13f);
                        float barrel = Box(p, 0.62f, 0.12f, 0.34f, 0.045f);
                        float guard = Box(p, 0.5f, 0.06f, 0.16f, 0.09f);
                        float mag = Poly(p, V(-0.04f, -0.04f), V(0.14f, -0.04f), V(0.1f, -0.46f), V(-0.07f, -0.43f));
                        float grip = Poly(p, V(-0.4f, -0.04f), V(-0.24f, -0.04f), V(-0.3f, -0.36f), V(-0.44f, -0.33f));
                        float scope = Box(p, -0.1f, 0.3f, 0.2f, 0.06f);
                        return Mathf.Min(Mathf.Min(Mathf.Min(stock, body), Mathf.Min(barrel, guard)), Mathf.Min(Mathf.Min(mag, grip), scope));
                    }
                case "squad":
                    {
                        float front = Bust(p, 0.16f, -0.12f, 1.0f);
                        float back = Bust(p, -0.36f, 0.08f, 0.82f);
                        return Mathf.Min(front, Mathf.Max(back, -(front - 0.09f)));
                    }
                case "person": return Bust(p, 0, -0.08f, 1.15f);
                case "flask":
                    {
                        float body = Poly(p, V(-0.18f, 0.72f), V(0.18f, 0.72f), V(0.18f, 0.22f), V(0.62f, -0.66f), V(0.55f, -0.76f), V(-0.55f, -0.76f), V(-0.62f, -0.66f), V(-0.18f, 0.22f));
                        float outline = Mathf.Abs(body) - 0.06f;
                        float liquid = Mathf.Max(body + 0.1f, p.y + 0.22f);
                        float lip = Box(p, 0, 0.76f, 0.3f, 0.06f);
                        float bubbles = Mathf.Min(Circ(p, 0.08f, -0.02f, 0.07f), Circ(p, -0.06f, 0.16f, 0.05f));
                        return Mathf.Min(Mathf.Min(outline, liquid), Mathf.Min(lip, bubbles));
                    }
                case "gear":
                    {
                        float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
                        float teeth = Mathf.Clamp01((Mathf.Cos(a * 8) - 0.05f) * 4);
                        float outer = r - (0.6f + 0.2f * teeth);
                        return Mathf.Max(outer * 0.8f, -(r - 0.26f));
                    }
                case "lock":
                    {
                        float body = Box(p, 0, -0.3f, 0.5f, 0.38f) - 0.04f;
                        var q = p - V(0, 0.12f);
                        float shackle = Mathf.Max(Mathf.Abs(q.magnitude - 0.31f) - 0.085f, -q.y);
                        shackle = Mathf.Min(shackle, Mathf.Min(Seg(p, V(-0.31f, 0.12f), V(-0.31f, -0.05f)), Seg(p, V(0.31f, 0.12f), V(0.31f, -0.05f))) - 0.085f);
                        float hole = Mathf.Min(Circ(p, 0, -0.24f, 0.1f), Box(p, 0, -0.42f, 0.045f, 0.12f));
                        return Mathf.Min(Mathf.Max(body, -hole), shackle);
                    }
                case "play": return Poly(p, V(-0.38f, 0.6f), V(0.62f, 0), V(-0.38f, -0.6f));
                case "pause": return Mathf.Min(Box(p, -0.24f, 0, 0.12f, 0.5f), Box(p, 0.24f, 0, 0.12f, 0.5f)) - 0.02f;
                case "close": return Mathf.Min(Seg(p, V(-0.48f, -0.48f), V(0.48f, 0.48f)), Seg(p, V(-0.48f, 0.48f), V(0.48f, -0.48f))) - 0.1f;
                case "back": return Mathf.Min(Seg(p, V(0.22f, 0.52f), V(-0.26f, 0)), Seg(p, V(-0.26f, 0), V(0.22f, -0.52f))) - 0.11f;
                case "check": return Mathf.Min(Seg(p, V(-0.52f, 0.02f), V(-0.14f, -0.38f)), Seg(p, V(-0.14f, -0.38f), V(0.56f, 0.44f))) - 0.11f;
                case "retry":
                    {
                        float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
                        const float a0 = 0.15f, a1 = 1.35f, R = 0.52f;
                        float arc = (a > a0 && a < a1)
                            ? Mathf.Min((p - V(Mathf.Cos(a0), Mathf.Sin(a0)) * R).magnitude, (p - V(Mathf.Cos(a1), Mathf.Sin(a1)) * R).magnitude) - 0.1f
                            : Mathf.Abs(r - R) - 0.1f;
                        float head = Poly(p, V(0.26f, 0.06f), V(0.8f, 0.06f), V(0.53f, 0.4f));
                        return Mathf.Min(arc, head);
                    }
                case "home":
                    {
                        float house = Poly(p, V(-0.58f, -0.62f), V(0.58f, -0.62f), V(0.58f, 0.08f), V(0, 0.62f), V(-0.58f, 0.08f));
                        float roof = Mathf.Min(Seg(p, V(-0.78f, 0.06f), V(0, 0.8f)), Seg(p, V(0, 0.8f), V(0.78f, 0.06f))) - 0.08f;
                        float door = Box(p, 0, -0.4f, 0.14f, 0.24f);
                        return Mathf.Min(Mathf.Max(house, -door), roof);
                    }
                case "bolt": return Poly(p, V(0.18f, 0.92f), V(-0.52f, -0.1f), V(-0.06f, -0.1f), V(-0.22f, -0.92f), V(0.52f, 0.16f), V(0.06f, 0.16f));
                case "sound":
                    {
                        float spk = Poly(p, V(-0.72f, 0.22f), V(-0.38f, 0.22f), V(0.02f, 0.6f), V(0.02f, -0.6f), V(-0.38f, -0.22f), V(-0.72f, -0.22f));
                        var q = p - V(0.02f, 0);
                        float ang = Mathf.Abs(Mathf.Atan2(q.y, q.x)), r = q.magnitude;
                        float w1 = Mathf.Max(Mathf.Abs(r - 0.36f) - 0.065f, (ang - 0.75f) * 0.36f);
                        float w2 = Mathf.Max(Mathf.Abs(r - 0.64f) - 0.065f, (ang - 0.8f) * 0.64f);
                        return Mathf.Min(spk, Mathf.Min(w1, w2));
                    }
                case "haptic":
                    {
                        float phone = Mathf.Abs(Box(p, 0, 0, 0.26f, 0.54f) - 0.06f) - 0.07f;
                        float lines = Mathf.Min(Mathf.Min(Seg(p, V(-0.56f, 0.3f), V(-0.56f, -0.3f)), Seg(p, V(-0.78f, 0.16f), V(-0.78f, -0.16f))),
                                                Mathf.Min(Seg(p, V(0.56f, 0.3f), V(0.56f, -0.3f)), Seg(p, V(0.78f, 0.16f), V(0.78f, -0.16f)))) - 0.06f;
                        return Mathf.Min(phone, lines);
                    }
                case "quality":
                    {
                        float big = Poly(p, V(-0.12f, 0.82f), V(0.06f, 0.2f), V(0.66f, 0.02f), V(0.06f, -0.16f), V(-0.12f, -0.78f), V(-0.3f, -0.16f), V(-0.9f, 0.02f), V(-0.3f, 0.2f));
                        float small = Poly(p - V(0.62f, 0.58f), V(0, 0.3f), V(0.07f, 0.07f), V(0.3f, 0), V(0.07f, -0.07f), V(0, -0.3f), V(-0.07f, -0.07f), V(-0.3f, 0), V(-0.07f, 0.07f));
                        return Mathf.Min(big, small);
                    }
                case "eye":
                    {
                        float lens = Mathf.Max((p - V(0, -0.5f)).magnitude - 0.9f, (p - V(0, 0.5f)).magnitude - 0.9f);
                        return Mathf.Min(Mathf.Abs(lens) - 0.075f, p.magnitude - 0.2f);
                    }
                default: return 1;
            }
        }

        // a painted icon (Resources/UI/Icons/<name>), full colour: show it untinted (Ink-white); null if it doesn't exist
        static readonly Dictionary<string, Sprite> art = new();
        public static Sprite Art(string name)
        {
            if (art.TryGetValue(name, out var s)) return s;
            return art[name] = Resources.Load<Sprite>("UI/Icons/" + name);
        }
        // set an Image to a painted icon (untinted, aspect kept), else to the glyph with the tint
        public static void SetIcon(Image img, string artName, string glyph, Color glyphTint)
        {
            var a = artName != null ? Art(artName) : null;
            if (a != null) { img.sprite = a; img.color = Color.white; img.preserveAspect = true; }
            else { img.sprite = Icon(glyph); img.color = glyphTint; }
        }

        public static Sprite Icon(string name)
        {
            if (icons.TryGetValue(name, out var s) && s != null) return s;
            const int N = 96;
            var t = Tex(N, N, (x, y) =>
            {
                var p = new Vector2((x / N - 0.5f) * 2, (y / N - 0.5f) * 2);
                return W(0.5f - IconSdf(name, p) * N * 0.5f);
            });
            return icons[name] = Spr(t, 0);
        }

        // ------------------------------------------------------------ layout helpers
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }
        // anchor + pivot at `a`, positioned by `pos`, sized `size`
        public static RectTransform Rect(Transform parent, string name, Vector2 a, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = a;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }
        public static RectTransform Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }
        public static RectTransform Fill(Transform parent, string name, float inset = 0) => Stretch(Rect(parent, name), inset, inset, inset, inset);

        public static Image Img(RectTransform rt, Sprite s, Color c, bool sliced = false, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s; img.color = c; img.raycastTarget = raycast;
            if (sliced) img.type = Image.Type.Sliced;
            return img;
        }
        public static RawImage Raw(RectTransform rt, Texture t, Color c)
        {
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = t; img.color = c; img.raycastTarget = false;
            return img;
        }
        public static Text Label(RectTransform rt, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 0.9f;
            return t;
        }
        public static Text Label(Transform parent, string name, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleCenter) => Label(Rect(parent, name), text, size, c, align);
        public static void GlowText(Text t, Color c, float a = 0.45f)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = A(c, a); o.effectDistance = new Vector2(1.6f, -1.6f);
        }

        public static Canvas MakeCanvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform SafeRoot(Transform canvas)
        {
            var rt = Fill(canvas, "Safe");
            rt.gameObject.AddComponent<SafeArea>();
            return rt;
        }

        // Glass panel frame (non-interactive): glow, glass, grid, edge, corner brackets and a top accent bar.
        // the in-game HUD's quiet panel: dark glass and a faint edge only (no accent strip, brackets, glow or grid)
        public static RectTransform Plain(Transform parent, string name, float glassAlpha = 0.72f)
        {
            var rt = Panel(parent, name, null, glassAlpha);
            foreach (var n in new[] { "Accent", "Brackets", "Glow", "Grid" }) { var t = rt.Find(n); if (t != null) UnityEngine.Object.Destroy(t.gameObject); }
            var e = rt.Find("Edge")?.GetComponent<Image>(); if (e != null) e.color = A(Cyan, 0.35f);
            return rt;
        }

        public static RectTransform Panel(Transform parent, string name, Color? tint = null, float glassAlpha = 0.86f)
        {
            var c = tint ?? Cyan;
            var rt = Rect(parent, name);
            Img(Stretch(Rect(rt, "Glow"), -40, -40, -40, -40), GlowSprite, A(c, 0.22f), true);
            Img(Fill(rt, "Glass"), FillSprite, A(Glass, glassAlpha), true, true); // blocks clicks through the panel
            var g = Raw(Fill(rt, "Grid", 14), GridTex, A(c, 0.05f));
            g.gameObject.AddComponent<GridUv>();
            Img(Fill(rt, "Edge"), EdgeSprite, A(c, 0.8f), true);
            Img(Stretch(Rect(rt, "Brackets"), -12, -12, -12, -12), BracketSprite, A(c, 0.9f), true);
            var acc = Rect(rt, "Accent");
            acc.anchorMin = new Vector2(0, 1); acc.anchorMax = new Vector2(0, 1); acc.pivot = new Vector2(0, 1);
            acc.anchoredPosition = new Vector2(34, -3); acc.sizeDelta = new Vector2(180, 6);
            Img(acc, WhiteSprite, Acid);
            return rt;
        }

        public static HoloButton Button(Transform parent, string name, Vector2 size, string label, string icon, int fontSize = 46, bool hero = false)
        {
            var root = Rect(parent, name, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var b = root.gameObject.AddComponent<HoloButton>();
            b.hero = hero;
            b.body = Fill(root, "Body");
            b.glow = Img(Stretch(Rect(b.body, "Glow"), -40, -40, -40, -40), GlowSprite, A(Cyan, 0.2f), true);
            b.fill = Img(Fill(b.body, "Fill"), FillSprite, Glass, true, true);
            b.grid = Raw(Fill(b.body, "Grid", 12), GridTex, A(Cyan, 0.06f));
            b.grid.gameObject.AddComponent<GridUv>();
            if (hero)
            {
                var sw = Rect(b.body, "Sweep");
                sw.anchorMin = new Vector2(0, 0); sw.anchorMax = new Vector2(0, 1); sw.pivot = new Vector2(0.5f, 0.5f);
                sw.offsetMin = new Vector2(-40, 14); sw.offsetMax = new Vector2(40, -14);
                b.sweep = Raw(sw, BandXTex, A(Ink, 0));
            }
            var acc = Rect(b.body, "Accent");
            acc.anchorMin = new Vector2(0, 0); acc.anchorMax = new Vector2(0, 1); acc.pivot = new Vector2(0, 0.5f);
            acc.offsetMin = new Vector2(7, 22); acc.offsetMax = new Vector2(14, -22);
            b.accent = Img(acc, WhiteSprite, Acid);
            var tick = Rect(b.body, "Tick", new Vector2(1, 0), new Vector2(-26, 9), new Vector2(46, 5));
            b.tick = Img(tick, WhiteSprite, Acid);
            b.edge = Img(Fill(b.body, "Edge"), EdgeSprite, Cyan, true);
            b.brackets = Img(Stretch(Rect(b.body, "Brackets"), -11, -11, -11, -11), BracketSprite, Cyan, true);

            float h = size.y, left = 30;
            if (icon != null)
            {
                float isz = Mathf.Round(h * (hero ? 0.5f : 0.44f));
                if (label == null) { b.icon = Img(Rect(b.body, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(isz, isz)), Icon(icon), Ink); }
                else
                {
                    b.icon = Img(Rect(b.body, "Icon", new Vector2(0, 0.5f), new Vector2(left + 8, 0), new Vector2(isz, isz)), Icon(icon), Ink);
                    left += 8 + isz + 20;
                }
            }
            if (label != null)
            {
                var lr = Stretch(Rect(b.body, "Label"), left, 0, hero ? 110 : 24, 0);
                b.label = Label(lr, label, fontSize, Ink, TextAnchor.MiddleLeft);
                b.label.fontStyle = FontStyle.Bold;
                GlowText(b.label, Cyan, 0.35f);
                b.labelRt = lr;
                b.labelLeft = left;
            }
            if (hero)
            {
                b.chevrons = Img(Rect(b.body, "Chevrons", new Vector2(1, 0.5f), new Vector2(-34, 0), new Vector2(h * 0.42f, h * 0.42f)), Icon("chevrons"), Cyan);
            }
            return b;
        }
    }

    // Scrolls / sizes a grid RawImage so its cells stay square at any panel size.
    public class GridUv : MonoBehaviour
    {
        RawImage img; RectTransform rt;
        void Awake() { img = GetComponent<RawImage>(); rt = (RectTransform)transform; }
        void Update()
        {
            var s = rt.rect.size;
            float t = Time.unscaledTime * 0.015f;
            img.uvRect = new Rect(t, t * 0.5f, s.x / 34f, s.y / 34f);
        }
    }

    // Keeps a RectTransform inside the device safe area (notch, Dynamic Island, home bar).
    public class SafeArea : MonoBehaviour
    {
        public static Rect? Override; // normalized 0..1; the editor screenshot tool uses it to fake an iPhone
        RectTransform rt; Rect last = new Rect(-1, -1, 0, 0);
        void Awake() { rt = (RectTransform)transform; Apply(); }
        void Update() => Apply();
        void Apply()
        {
            Rect n;
            if (Override.HasValue) n = Override.Value;
            else
            {
                var sa = Screen.safeArea; float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
                n = new Rect(sa.x / w, sa.y / h, sa.width / w, sa.height / h);
            }
            if (n == last) return;
            last = n;
            rt.anchorMin = n.min; rt.anchorMax = n.max; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    // A click target without visuals (backdrops that close a sheet, etc.).
    public class Tap : MonoBehaviour, IPointerClickHandler
    {
        public Action onTap;
        public void OnPointerClick(PointerEventData e) => onTap?.Invoke();
    }

    // The holographic glass button: cyan glass, acid-green accent edge, soft glow, corner brackets,
    // a faint flicker; scales down when pressed, flashes, clicks and taps the haptic engine.
    public class HoloButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action onClick;
        public bool silent;   // the button's own sound plays instead of the tap (DEPLOY)
        public bool hero, locked, selected, manual, dimmed, interactable = true;
        public RectTransform body, labelRt;
        public Image glow, fill, edge, brackets, accent, tick, icon, chevrons;
        public RawImage grid, sweep;
        public Text label, sub;
        public Color tint = Holo.Cyan;
        public float labelLeft, glowBoost, spin;
        float press, flash, flickerT, shakeT, seed, sweepT = -1;
        bool down;

        void Awake() => seed = UnityEngine.Random.value * 10;

        public void SetSub(string text, Color c, int size = 26)
        {
            if (sub == null)
            {
                var r = Holo.Stretch(Holo.Rect(body, "Sub"), labelLeft, 0, hero ? 110 : 24, 0);
                sub = Holo.Label(r, "", size, c, TextAnchor.LowerLeft);
                r.offsetMin = new Vector2(labelLeft, body.rect.height * 0.12f + 6);
                if (labelRt != null) { labelRt.offsetMax = new Vector2(labelRt.offsetMax.x, -6); labelRt.offsetMin = new Vector2(labelRt.offsetMin.x, 26); }
            }
            sub.text = text; sub.color = c;
        }

        // feedback without the event system (the HUD's dash / weapon buttons read touches directly)
        public void Flash(bool sound = true)
        {
            flash = 1; press = 1;
            if (sound) Sfx.Click();
            Haptics.Light();
        }
        public void Deny()
        {
            shakeT = 0.32f;
            Sfx.Deny(); Haptics.Warn();
        }

        public void OnPointerDown(PointerEventData e) { if (!manual && interactable) down = true; }
        public void OnPointerUp(PointerEventData e) => down = false;
        public void OnPointerExit(PointerEventData e) => down = false;
        public void OnPointerClick(PointerEventData e)
        {
            if (manual || !interactable) return;
            if (locked) { Deny(); onClick?.Invoke(); return; }
            flash = 1;
            if (!silent) Sfx.Click(); Haptics.Light();
            onClick?.Invoke();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime + seed;
            press = Mathf.MoveTowards(press, down ? 1 : 0, dt * (down ? 14 : 6));
            flash = Mathf.Max(0, flash - dt * 3f);
            shakeT = Mathf.Max(0, shakeT - dt);
            if (flickerT > 0) flickerT -= dt;
            else if (UnityEngine.Random.value < dt * 0.22f) flickerT = UnityEngine.Random.Range(0.03f, 0.11f);
            float fl = flickerT > 0 ? 0.6f : 1f;
            float pulse = hero ? 0.5f + 0.5f * Mathf.Sin(t * 3.4f) : 0f;

            body.localScale = Vector3.one * (1 - press * 0.06f + pulse * 0.018f);
            body.anchoredPosition = new Vector2(shakeT > 0 ? Mathf.Sin(shakeT * 70) * 14 * (shakeT / 0.32f) : 0, 0);

            var c = locked ? Holo.Muted : tint;
            float gA = (0.10f + pulse * 0.32f + flash * 0.9f + (selected ? 0.25f : 0) + glowBoost) * fl;
            glow.color = Holo.A(selected ? Color.Lerp(c, Holo.Acid, 0.55f) : c, Mathf.Clamp01(gA));
            fill.color = Color.Lerp(Holo.Glass, Holo.A(Color.Lerp(Holo.Glass, c, 0.45f), 0.9f), Mathf.Max(flash, press * 0.6f));
            edge.color = Holo.A(selected ? Holo.Acid : c, (locked ? 0.45f : 0.9f) * fl + flash * 0.1f);
            brackets.color = Holo.A(selected ? Holo.Acid : c, (locked ? 0.4f : 0.85f) * fl);
            accent.color = Holo.A(locked ? Holo.Muted : Holo.Acid, 0.95f * fl);
            tick.color = Holo.A(locked ? Holo.Muted : Holo.Acid, (0.55f + 0.45f * Mathf.Sin(t * 2.1f)) * fl);
            grid.color = Holo.A(c, 0.05f + flash * 0.12f);
            if (icon != null)
            {
                icon.color = Holo.A(locked ? Holo.Muted : Holo.Ink, fl * (dimmed ? 0.3f : 1));
                if (spin != 0) icon.rectTransform.localEulerAngles = new Vector3(0, 0, -t * spin);
            }
            if (label != null) label.color = Holo.A(locked ? Holo.Muted : Holo.Ink, 0.85f + 0.15f * fl);
            if (chevrons != null)
            {
                chevrons.color = Holo.A(Holo.Acid, 0.45f + 0.55f * pulse);
                chevrons.rectTransform.anchoredPosition = new Vector2(-34 + Mathf.Repeat(t * 0.9f, 1f) * 12, 0);
            }
            if (sweep != null)
            {   // a light sweep crosses the hero button every few seconds
                if (sweepT < 0 && Mathf.Repeat(t, 3.2f) < dt) sweepT = 0;
                if (sweepT >= 0)
                {
                    sweepT += dt;
                    float k = sweepT / 0.7f;
                    var r = (RectTransform)sweep.transform;
                    r.anchorMin = new Vector2(Mathf.Lerp(0.04f, 0.96f, k), 0); r.anchorMax = new Vector2(r.anchorMin.x, 1);
                    sweep.color = Holo.A(Holo.Ink, 0.22f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k)));
                    if (k >= 1) sweepT = -1;
                }
            }
        }
    }

    // Saved player choices.
    public static class Prefs
    {
        public static bool Sound
        {
            get => PlayerPrefs.GetInt("ms.sound", 1) == 1;
            set { PlayerPrefs.SetInt("ms.sound", value ? 1 : 0); PlayerPrefs.Save(); ApplySound(); }
        }
        public static bool HapticsOn
        {
            get => PlayerPrefs.GetInt("ms.haptics", 1) == 1;
            set { PlayerPrefs.SetInt("ms.haptics", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        // FRAME RATE: 120 on ProMotion screens (default on), else 60
        public static bool Fps120
        {
            get => PlayerPrefs.GetInt("ms.fps120", 1) == 1;
            set { PlayerPrefs.SetInt("ms.fps120", value ? 1 : 0); PlayerPrefs.Save(); ApplyFrameRate(); }
        }
        public static int DisplayHz => Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
        public static bool Supports120 => DisplayHz >= 100 || Application.isEditor; // (the editor shows the row for layout checks)
        public static void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Fps120 && Supports120 ? Mathf.Min(120, DisplayHz) : 60;
        }

        // CAMERA: FAR = the original framing (distance 40), CLOSE = 25% nearer so the hero reads bigger (trial, default FAR)
        public const float CamFar = 40f, CamClose = 30f;
        public static bool CamCloseOn
        {
            get => PlayerPrefs.GetInt("ms.camclose", 0) == 1;
            set { PlayerPrefs.SetInt("ms.camclose", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static float CamDistance => CamCloseOn ? CamClose : CamFar;

        // Settings > SHOW FPS (default off): the frame counter in the top-left corner
        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt("ms.showfps", 0) == 1;
            set { PlayerPrefs.SetInt("ms.showfps", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool HighQuality
        {
            get => PlayerPrefs.GetInt("ms.quality", 1) == 1;
            set { PlayerPrefs.SetInt("ms.quality", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        // SQUAD pick: "sentinel" (default) or "vanguard" (Iron Vanguard, the Heavy)
        public static string Hero
        {
            get => HeroOverride ?? PlayerPrefs.GetString("ms.hero", "sentinel");
            set { PlayerPrefs.SetString("ms.hero", value); PlayerPrefs.Save(); }
        }
        public static bool Vanguard => Hero == "vanguard";
        public static bool Scout => Hero == "scout";
        // TEST MODE (Settings / Sandbox): every pin selectable, runs never touch the progression save. Local keys, default off.
        public static bool TestMode { get => PlayerPrefs.GetInt("ms.test.mode", 0) == 1; set { PlayerPrefs.SetInt("ms.test.mode", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool TestPower { get => PlayerPrefs.GetInt("ms.test.power", 0) == 1; set { PlayerPrefs.SetInt("ms.test.power", value ? 1 : 0); PlayerPrefs.Save(); } }   // (test runs: the zone scaled to the hero's CP)
        public static int TestFloor { get => Mathf.Clamp(PlayerPrefs.GetInt("ms.test.floor", 1), 1, 30); set { PlayerPrefs.SetInt("ms.test.floor", value); PlayerPrefs.Save(); } }   // the Scout (Meshy wanderer body, Twin Needlers = the SMG for now)
        // VANGUARD RIG (Squad): MESHY = Meshy's own rig and clips (default), BLENDER = the Sentinel skeleton fitted in Blender
        public static bool VanguardMeshy
        {
            get => PlayerPrefs.GetInt("ms.vgrig", 1) == 1;
            set { PlayerPrefs.SetInt("ms.vgrig", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static string HeroOverride; // perf runs (MAZE_HERO=vanguard) pick a hero without touching the saved pick
        // the selected hero, for every screen that shows or names him (one source: Squad pick)
        public static string HeroName => Vanguard ? "IRON VANGUARD" : Scout ? "WANDERER SCOUT" : "ARCTIC SENTINEL";
        public static string HeroRole => Vanguard ? "HEAVY  //  MINIGUN" : Scout ? "SKIRMISHER  //  TWIN NEEDLERS" : "RIFLEMAN  //  BALANCED";
        public static Texture2D HeroPortrait() => Vanguard
            ? Resources.Load<Texture2D>("UI/vanguard_portrait") ?? Resources.Load<Texture2D>("UI/Portraits/vanguard")
            : Scout ? Resources.Load<Texture2D>("UI/scout_portrait") ?? Resources.Load<Texture2D>("UI/Portraits/scout") ?? Resources.Load<Texture2D>("UI/Portraits/hero")
            : Resources.Load<Texture2D>("UI/hero_portrait") ?? Resources.Load<Texture2D>("UI/Portraits/hero");
        // the bridge painting (main menu, sector select, briefing) shows the hero at the window: a Vanguard version
        // (UI/menu_bg_vanguard.png) is used once it exists, else the Sentinel's
        public static Texture2D HeroDropBay() => (Vanguard ? Resources.Load<Texture2D>("UI/drop_bay_vanguard") : null) ?? Resources.Load<Texture2D>("UI/drop_bay");
        public static Texture2D HeroBackdrop() => (Vanguard ? Resources.Load<Texture2D>("UI/menu_bg_vanguard") : null) ?? Resources.Load<Texture2D>("UI/menu_bg");

        public static string Weapon
        {
            get => PlayerPrefs.GetString("ms.weapon", "RIFLE");
            set { PlayerPrefs.SetString("ms.weapon", value); PlayerPrefs.Save(); }
        }
        public static int Best
        {
            get => PlayerPrefs.GetInt("ms.best", 0);
            set { PlayerPrefs.SetInt("ms.best", value); PlayerPrefs.Save(); }
        }
        public static void ApplySound() => AudioListener.volume = Sound ? 1 : 0;
        public static int WeaponIndex(Weapon[] ws)
        {
            var n = Weapon;
            for (int i = 0; i < ws.Length; i++) if (ws[i].name == n) return i;
            return 0;
        }
    }

    // Light taps from the iPhone's Taptic Engine (UIImpactFeedbackGenerator, see Plugins/iOS/MazeHaptics.mm).
    // Handheld.Vibrate is a long, strong buzz, far too much for UI taps, so other platforms get nothing.
    public static class Haptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void MazeHapticImpact(int style);
#endif
        public static void Light() => Fire(0);
        public static void Medium() => Fire(1);
        public static void Heavy() => Fire(2);
        public static void Warn() => Fire(11);
        static float lastAt = -1; static int lastStyle; static readonly System.Collections.Generic.List<float> recent = new();
        static void Fire(int style)
        {
            if (!Prefs.HapticsOn) return;
            // one manager: min 80 ms apart, max 6 a second; inside the gap a weaker one is dropped (not queued)
            float now = Time.unscaledTime; recent.RemoveAll(x => now - x > 1f);
            int rank = style == 11 ? 1 : style;
            if (recent.Count >= 6 || (now - lastAt < 0.08f && rank <= lastStyle)) return;
            lastAt = now; lastStyle = rank; recent.Add(now);
#if UNITY_IOS && !UNITY_EDITOR
            MazeHapticImpact(style);
#endif
        }
    }

    // Tiny synthesized UI sounds (no audio assets).
    public static class Sfx
    {
        static AudioSource src;
        static AudioClip click, deny, open, close, drop, toggle, hiss, pounce;
        const int Rate = 44100;

        static AudioSource Src
        {
            get
            {
                if (src != null) return src;
                var go = new GameObject("UiSfx");
                UnityEngine.Object.DontDestroyOnLoad(go);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 0; src.ignoreListenerPause = true;
                return src;
            }
        }

        static AudioClip Make(string name, float len, Func<float, float, float> f) // f(t, phaseAccumulatorInput) -> sample
        {
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++) data[i] = f(i / (float)Rate, (float)(rng.NextDouble() * 2 - 1));
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }
        // frequency sweep with exponential glide: phase is integrated analytically
        static float Sweep(float t, float f0, float f1, float len)
        {
            float k = Mathf.Log(f1 / f0) / len;
            float ph = Mathf.Abs(k) < 1e-5f ? f0 * t : f0 * (Mathf.Exp(k * t) - 1) / k;
            return Mathf.Sin(2 * Mathf.PI * ph);
        }

        // the recorded UI / drop sounds (Resources/Audio, Sounds/Title finals); the synth versions stay as the fallback
        public static Action<string> Log;   // (editor checks: which clip played)
        static readonly System.Collections.Generic.Dictionary<string, AudioClip> rec = new();
        static bool Rec(string n, float vol)
        {
            if (!rec.TryGetValue(n, out var c)) rec[n] = c = Resources.Load<AudioClip>("Audio/" + n);
            if (c == null) return false;
            Src.PlayOneShot(c, vol); Log?.Invoke(n); return true;
        }
        public static void PanelOpen() { if (!Rec("sfx_ui_panel_open", 0.4f)) Open(); }      // Loadout / Squad / Research / Settings
        public static void DeployEngage() { if (!Rec("sfx_deploy_engage", 1f)) Click(); }    // the main menu's DEPLOY
        public static bool PodFall() => Rec("sfx_drop_pod_fall", 0.9f);                       // LAUNCH
        public static bool PodImpact() => Rec("sfx_drop_pod_impact", 1f);                     // the pod lands (heavy haptic at the caller)
        static void Play(ref AudioClip c, Func<AudioClip> make, float vol)
        {
            if (c == null) c = make();
            Src.PlayOneShot(c, vol);
        }

        public static void Click() { if (!Rec("sfx_ui_tap", 0.35f)) SynthClick(); }
        static void SynthClick() => Play(ref click, () => Make("click", 0.06f, (t, n) => (Sweep(t, 2300, 850, 0.06f) * 0.8f + n * 0.25f) * Mathf.Exp(-t * 85)), 0.55f);
        public static void Toggle() => Play(ref toggle, () => Make("toggle", 0.09f, (t, n) => Sweep(t, 1200, 2600, 0.09f) * Mathf.Exp(-t * 45) * 0.8f), 0.45f);
        public static void Deny() { if (Rec("sfx_ui_locked", 0.35f)) Haptics.Warn(); else SynthDeny(); }
        static void SynthDeny() => Play(ref deny, () => Make("deny", 0.22f, (t, n) =>
        {
            float gate = (t < 0.08f || (t > 0.11f && t < 0.19f)) ? 1 : 0;
            return Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 190 * t)) * 0.35f * gate * Mathf.Exp(-(t % 0.11f) * 18);
        }), 0.4f);
        public static void Open() => Play(ref open, () => Make("open", 0.24f, (t, n) => (Sweep(t, 260, 1500, 0.24f) * 0.6f + n * 0.12f) * Mathf.Sin(Mathf.PI * t / 0.24f)), 0.35f);
        // Leaper: a rising hiss while it coils, a short whoosh as it pounces (placeholders until proper SFX exist)
        public static void Hiss(float vol) { if (vol > 0.05f) Play(ref hiss, () => Make("hiss", 0.6f, (t, n) => n * 0.55f * Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((0.6f - t) / 0.12f) * (0.6f + 0.4f * Mathf.Sin(t * 90f))), 0.35f * vol); }
        public static void Pounce(float vol) { if (vol > 0.05f) Play(ref pounce, () => Make("pounce", 0.3f, (t, n) => (n * 0.6f + Sweep(t, 180, 90, 0.3f) * 0.4f) * Mathf.Sin(Mathf.PI * t / 0.3f)), 0.45f * vol); }
        public static void Close() { if (!Rec("sfx_ui_back", 0.3f)) SynthClose(); }
        static void SynthClose() => Play(ref close, () => Make("close", 0.2f, (t, n) => (Sweep(t, 1300, 240, 0.2f) * 0.6f + n * 0.1f) * Mathf.Sin(Mathf.PI * t / 0.2f)), 0.3f);
        public static void Drop() => Play(ref drop, () =>
        {
            float lp = 0;
            return Make("drop", 1.5f, (t, n) =>
            {
                lp += (n - lp) * 0.06f; // low-passed noise rumble
                float whoosh = Sweep(t, 900, 110, 0.5f) * 0.35f * Mathf.Clamp01(t / 0.45f) * (t < 0.5f ? 1 : 0);
                float rumble = lp * 2.2f * Mathf.Clamp01(t / 0.5f) * Mathf.Exp(-Mathf.Max(0, t - 0.5f) * 3);
                float ti = t - 0.5f;
                float impact = ti > 0 ? (Mathf.Sin(2 * Mathf.PI * 52 * ti) * 0.9f + n * 0.5f * Mathf.Exp(-ti * 30)) * Mathf.Exp(-ti * 6) : 0;
                return Mathf.Clamp(whoosh + rumble + impact, -1, 1);
            });
        }, 0.9f);
    }
}
