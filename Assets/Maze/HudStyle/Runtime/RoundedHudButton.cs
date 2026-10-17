using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

namespace Maze
{
    // Presentation for the four in-map controls only. HoloButton's event/data contract is retained,
    // but its holographic Update is disabled on these instances so it cannot recolour the artwork.
    public sealed class RoundedHudButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        HoloButton button;
        UnityEngine.UI.Image surface, glyph, dim, ring, ready, track;
        TMP_Text seconds;
        string iconName;
        bool pause, pressed, active, cooling, unavailable;
        float pulse;
        public string IconName => iconName;
        public float ReadyFraction => ring.fillAmount;
        public string Counter => seconds.text;
        public bool Active => active;

        public static RoundedHudButton Attach(HoloButton b, string icon, bool isPause = false)
        {
            b.enabled = false;
            foreach (var g in b.body.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.gameObject.SetActive(false);
            var v = b.gameObject.AddComponent<RoundedHudButton>();
            v.button = b; v.pause = isPause;
            v.surface = Holo.Img(Holo.Fill(b.body, "RoundedSurface"), BlueUi.Art(isPause ? "button_navy_blank" : "button_blue_blank") ?? RoundedHudStyle.Art(isPause ? "button_pause" : "button_circle_normal"), Color.white, false, true);
            v.surface.type = UnityEngine.UI.Image.Type.Sliced; v.surface.preserveAspect = false;
            v.dim = Holo.Img(Holo.Fill(b.body, "RoundedDim", 6), BlueUi.Art("panel_soft"), new Color(.20f,.35f,.55f,.22f), true);
            v.glyph = Holo.Img(RoundedHudStyle.Box(b.body, "RoundedIcon", new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, Vector2.one * (isPause ? 58 : 78)), null, Color.white);
            v.glyph.preserveAspect = true;
            v.seconds = BlueUi.Text(b.body,"RoundedCounter","",22,83,96,42,34,Color.white);
            v.seconds.alignment = TextAlignmentOptions.Center;
            // The approved chunky button artwork carries the control. Cooldown is a quiet bottom meter,
            // rather than the old dark neon rings. Existing readiness and numerical feedback are retained.
            v.track = Holo.Img(BlueUi.Rect(b.body,"RoundedTrack",24,124,92,5),Holo.WhiteSprite,new Color(.04f,.12f,.28f,.25f));
            v.ring = Holo.Img(BlueUi.Rect(b.body,"RoundedCooldown",24,124,92,5),Holo.WhiteSprite,new Color(.72f,.94f,1f));
            v.ring.type = UnityEngine.UI.Image.Type.Filled; v.ring.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            v.ring.fillOrigin = 0;
            v.ready = Holo.Img(Holo.Fill(b.body,"RoundedReady"),Holo.WhiteSprite,Color.clear);
            v.ready.gameObject.SetActive(false);
            b.icon = v.glyph; b.fill = v.surface;
            if (isPause)
            {
                v.surface.rectTransform.anchorMin = v.surface.rectTransform.anchorMax = v.surface.rectTransform.pivot = new Vector2(.5f,.5f);
                v.surface.rectTransform.anchoredPosition = Vector2.zero; v.surface.rectTransform.sizeDelta = new Vector2(91,75);
            }
            v.SetIcon(icon); v.SetState(0,1,false,false);
            if (isPause) { v.dim.gameObject.SetActive(false); v.ring.gameObject.SetActive(false); v.ready.gameObject.SetActive(false); v.track.gameObject.SetActive(false); }
            return v;
        }

        public void SetIcon(string value)
        {
            if (iconName == value) return; iconName = value; ApplyIcon();
        }

        void ApplyIcon()
        {
            glyph.sprite = pause ? BlueUi.Art("icon_pause") ?? RoundedHudStyle.Art("icon_pause_ivory") : PolishGlyphs.Get(iconName) ?? RoundedHudStyle.Art("icon_" + iconName + (unavailable ? "_disabled" : "_ivory"));
            glyph.color = active ? BlueUi.Navy : unavailable ? new Color(.70f,.78f,.86f,1) : Color.white;
        }

        public void SetState(float remaining, float duration, bool isActive, bool isUnavailable, int count = -1)
        {
            active = isActive; unavailable = isUnavailable; cooling = remaining > 0;
            ring.fillAmount = 1 - Mathf.Clamp01(remaining / Mathf.Max(.01f,duration));
            ring.enabled = cooling && !unavailable; track.enabled = cooling && !unavailable; ready.enabled = false;
            dim.enabled = cooling && !active || unavailable;
            seconds.text = count >= 0 ? count.ToString() : cooling ? Mathf.CeilToInt(remaining).ToString() : "";
            glyph.rectTransform.anchoredPosition = new Vector2(0,seconds.text.Length > 0 ? 16 : 0);
            glyph.rectTransform.sizeDelta = Vector2.one * (pause ? 58 : seconds.text.Length > 0 ? 64 : 78);
            seconds.color = active ? BlueUi.Navy : Color.white;
            ApplyIcon(); ApplySurface();
        }

        public void Pulse() { pulse = 1; ApplySurface(); }
        void ApplySurface()
        {
            surface.sprite = BlueUi.Art(pause ? "button_navy_blank" : active ? "button_yellow_blank" : "button_blue_blank") ?? RoundedHudStyle.Art(pause ? "button_pause" : unavailable ? "button_circle_disabled" : pressed || pulse > .6f ? "button_circle_pressed" : active ? "button_circle_selected" : "button_circle_normal");
            surface.color = unavailable ? new Color(.62f,.72f,.84f,1) : pressed || pulse > .6f ? new Color(.8f,.9f,1,1) : Color.white;
        }
        void Update()
        {
            pulse = Mathf.MoveTowards(pulse,0,Time.unscaledDeltaTime * 5);
            button.body.localScale = Vector3.one * (pressed ? .96f : 1 - .025f * pulse);
            ready.color = new Color(1,1,1,active ? .78f + .22f * Mathf.Sin(Time.unscaledTime * 5) : 1);
            ApplySurface();
        }
        public void OnPointerDown(PointerEventData e) { pressed = true; }
        public void OnPointerUp(PointerEventData e) { pressed = false; }
        public void OnPointerExit(PointerEventData e) { pressed = false; }
        public void OnPointerClick(PointerEventData e)
        {
            if (button.manual || !button.interactable) return;
            Pulse(); button.Flash(false); button.onClick?.Invoke();
        }
        void OnDisable() { pressed = false; pulse = 0; if (button != null) button.body.localScale = Vector3.one; }
    }
}
