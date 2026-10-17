using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Shared canvas-unit rhythm. The existing scaler is 1080 units high and remains unchanged.
    public static class RoundedHudStyle
    {
        public const float Padding = 32, Gap = 20, Inner = 16;
        public const float ActionSize = 152, PauseSize = 132;
        public const float StatusWidth = 560, StatusHeight = 152;
        public const float ObjectiveWidth = 480, ObjectiveHeight = 112;
        public static readonly Color Ink = new Color32(246, 250, 255, 255);
        public static readonly Color Cyan = new Color32(86, 231, 250, 255);
        static readonly Dictionary<string, Sprite> sprites = new();

        public static Sprite Art(string name)
        {
            if (!sprites.TryGetValue(name, out var sprite) || sprite == null)
                sprites[name] = sprite = Resources.Load<Sprite>("UI/RoundedHud/" + name);
            return sprite;
        }

        public static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = Holo.Rect(parent, name, anchor, pos, size); rt.pivot = pivot; return rt;
        }

        public static UnityEngine.UI.Image Image(Transform parent, string name, string art, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, bool sliced = false, bool raycast = false)
        {
            var rt = Box(parent, name, anchor, pivot, pos, size);
            return Holo.Img(rt, Art(art), Color.white, sliced, raycast);
        }

        public static UnityEngine.UI.Image Fill(Transform parent, string name, string art)
        {
            var im = Holo.Img(Holo.Fill(parent, name), Art(art), Color.white);
            // Filled clips both geometry and UVs; it never scales the fill with its current value.
            im.type = UnityEngine.UI.Image.Type.Filled;
            im.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            im.fillOrigin = 0; im.fillAmount = 1;
            return im;
        }

        public static UnityEngine.UI.Text Label(Transform parent, string name, string text, int size, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = Holo.Label(Holo.Fill(parent, name), text, size, Ink, align);
            t.fontStyle = FontStyle.Bold; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        public static string SkillIcon => Prefs.Vanguard ? "barrage" : Prefs.Scout ? "decoy" : "freeze";
    }
}
