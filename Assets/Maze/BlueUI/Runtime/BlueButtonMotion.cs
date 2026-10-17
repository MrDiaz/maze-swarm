using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

namespace Maze
{
    // Unscaled presentation feedback. Unity's Button remains the only owner of the click callback.
    public sealed class BlueButtonMotion : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        UnityEngine.UI.Button button;
        bool pressed;
        float settleUntil;
        Coroutine motion;

        void Awake() { button = GetComponent<UnityEngine.UI.Button>(); }
        void OnEnable() { pressed = false; settleUntil = 0; transform.localScale = Vector3.one; }
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && button != null && button.IsInteractable())
            { pressed = true; settleUntil = 0; Begin(); }
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!pressed) return;
            pressed = false;
            settleUntil = Time.unscaledTime + .075f;
            Begin();
        }
        public void OnPointerExit(PointerEventData e) { pressed = false; settleUntil = 0; Begin(); }
        void Begin() { if (motion == null && isActiveAndEnabled) motion = StartCoroutine(Animate()); }
        IEnumerator Animate()
        {
            do
            {
                if (button == null || !button.IsInteractable()) { pressed = false; settleUntil = 0; }
                float target = pressed ? .974f : Time.unscaledTime < settleUntil ? 1.012f : 1f;
                float size = Mathf.Lerp(transform.localScale.x, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 33));
                if (Mathf.Abs(size - target) < .0001f) size = target;
                transform.localScale = Vector3.one * size;
                yield return null;
            } while (pressed || Time.unscaledTime < settleUntil || Mathf.Abs(transform.localScale.x - 1) > .0001f);
            transform.localScale = Vector3.one;
            motion = null;
        }
        void OnDisable() { if (motion != null) StopCoroutine(motion); motion = null; pressed = false; settleUntil = 0; transform.localScale = Vector3.one; }
    }
}
