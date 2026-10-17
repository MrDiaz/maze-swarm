using System;
using UnityEngine;
using TMPro;

namespace Maze
{
    // Local to Hangar: existing Equipment, gameplay and shared blue UI styles are untouched.
    public static class RichHangarStyle
    {
        public static readonly Color Ink = new Color32(3, 24, 79, 255);
        public static Sprite Art(string name) => Resources.Load<Sprite>("RichHangar/Art/" + name);

        public static UnityEngine.UI.Image Image(Transform parent, string name, string art, float x, float y, float width, float height, bool sliced = false)
        {
            var image = Holo.Img(BlueUi.Rect(parent, name, x, y, width, height), Art(art), Color.white, sliced);
            image.raycastTarget = false;
            image.preserveAspect = !sliced;
            return image;
        }

        public static TMP_Text Text(Transform parent, string name, string value, float x, float y, float width, float height, float size)
        {
            var text = BlueUi.Text(parent, name, value, x, y, width, height, size, Ink);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        public static UnityEngine.UI.Button Button(Transform parent, string name, string label, string icon, float x, float y, float width, float height, Action action, bool primary = false)
        {
            float dx = (Mathf.Max(124, width) - width) * .5f, dy = (Mathf.Max(124, height) - height) * .5f;
            var rect = BlueUi.Rect(parent, name, x - dx, y - dy, width + dx * 2, height + dy * 2);
            Holo.Img(Holo.Fill(rect, "TouchTarget"), Holo.WhiteSprite, Color.clear, false, true);
            var face = Image(rect, "Surface", primary ? "button_gold" : "button_pale", dx, dy, width, height, true);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.79f, .89f, .97f);
            colors.disabledColor = new Color(.55f, .63f, .72f, .8f);
            colors.fadeDuration = .06f;
            button.colors = colors;
            button.gameObject.AddComponent<BlueButtonMotion>();
            button.onClick.AddListener(() => action?.Invoke());
            if (string.IsNullOrEmpty(label))
                Image(rect, "Icon", icon, dx + 13, dy + 10, 56, 54);
            else
            {
                float iconX = icon == "icon_heroes" ? 38 : icon == "icon_gear" ? 67 : 44;
                var sprite = Art(icon);
                Image(rect, "Icon", icon, dx + iconX, dy + (icon == "icon_gear" ? 13 : 12), sprite.rect.width, sprite.rect.height);
                Text(rect, "Label", label, dx + (icon == "icon_gear" ? 109 : 105), dy + 10, width - 132, height - 20, 34);
            }
            return button;
        }

        public static void Background(RectTransform root, RectTransform frame)
        {
            var texture = CohesiveZone.Enabled ? CohesiveMenuArt.Hangar : Resources.Load<Texture2D>("RichHangar/Art/scene-rich-extended");
            var outer = Holo.Raw(Holo.Fill(root, "RichHangarScene"), texture, Color.white);
            outer.raycastTarget = false;
            outer.transform.SetAsFirstSibling();
            if (CohesiveZone.Enabled)
            {
                if (texture != null) outer.gameObject.AddComponent<CoverUv>().aspect = (float)texture.width / texture.height;
            }
            else outer.gameObject.AddComponent<RichHangarArtFit>().Init(outer, frame);
        }
    }

    // Match the illustrated design coordinates to the safe-area UI at every aspect ratio.
    // An illustrated extension supplies the extra scenery around the original design frame.
    // The single continuous plate preserves the original composition without inset seams.
    [DefaultExecutionOrder(10020)]
    public sealed class RichHangarArtFit : MonoBehaviour
    {
        UnityEngine.UI.RawImage image;
        RectTransform frame;
        readonly Vector3[] corners = new Vector3[4];
        public void Init(UnityEngine.UI.RawImage value, RectTransform designFrame) { image = value; frame = designFrame; Fit(); }
        void LateUpdate() => Fit();
        void Fit()
        {
            if (image == null || frame == null) return;
            var rect = image.rectTransform;
            frame.GetWorldCorners(corners);
            var min = rect.InverseTransformPoint(corners[0]);
            var max = rect.InverseTransformPoint(corners[2]);
            float width = max.x - min.x, height = max.y - min.y;
            if (width <= 0 || height <= 0) return;
            var native = new Rect((rect.rect.xMin - min.x) / width, (rect.rect.yMin - min.y) / height, rect.rect.width / width, rect.rect.height / height);
            // Extension guide: 2160x1008, with the original 1672x941 at (244,0) from top left.
            image.uvRect = new Rect((244 + native.x * 1672) / 2160, (67 + native.y * 941) / 1008, native.width * 1672 / 2160, native.height * 941 / 1008);
        }
    }
}
