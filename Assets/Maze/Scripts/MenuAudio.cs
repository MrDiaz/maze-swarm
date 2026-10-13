using UnityEngine;

namespace Maze
{
    // Title / menu bed (Sounds/Title, ElevenLabs takes picked 2026-10-13): mus_menu loops at 60% over the title, the
    // menus and the drop-zone map; amb_bridge loops at ~28% under it on the title (bridge) screen only. Both fade in over
    // ~1 s and out over 0.2 s; LAUNCH (the drop) fades them out until the menu shows again. Mute = the listener volume.
    public class MenuAudio : MonoBehaviour
    {
        public static MenuAudio I;
        public static bool Hold;   // set by LAUNCH, cleared when the main menu shows again
        AudioSource music, amb; MainMenu menu;
        const float MusicVol = 0.6f, AmbVol = 0.28f;

        public static void Init(MainMenu m)
        {
            if (I != null) return;
            var go = new GameObject("MenuAudio"); I = go.AddComponent<MenuAudio>(); I.menu = m;
            I.music = I.Src(Resources.Load<AudioClip>("Audio/mus_menu")); I.amb = I.Src(Resources.Load<AudioClip>("Audio/amb_bridge"));
        }
        AudioSource Src(AudioClip c)
        {
            if (c == null) return null;
            var s = gameObject.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0; s.volume = 0; s.ignoreListenerPause = true;
            return s;
        }
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool title = menu != null && menu.Visible && !menu.Dropping;
            bool map = MapSelect.I != null && MapSelect.I.Open && !MapSelect.I.Dropping;
            Fade(music, !Hold && (title || map) ? MusicVol : 0, dt);
            Fade(amb, !Hold && title ? AmbVol : 0, dt);
        }
        static void Fade(AudioSource s, float target, float dt)
        {
            if (s == null) return;
            float rate = target > s.volume ? 0.6f : 3f;   // per second: in over ~1 s, out over 0.2 s
            s.volume = Mathf.MoveTowards(s.volume, target, rate * dt);
            if (s.volume > 0 && !s.isPlaying) { s.Play(); Sfx.Log?.Invoke(s.clip.name + " start"); }
            else if (s.volume <= 0 && s.isPlaying && target <= 0) { s.Pause(); Sfx.Log?.Invoke(s.clip.name + " fade out"); }
        }
    }
}
