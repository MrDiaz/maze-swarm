using UnityEngine;

namespace Maze
{
    // Cryo Station's run music (Sounds/Cryo/final/mus_run_cryo.wav, ElevenLabs take 1, a 74 s seamless loop) at 35%, 2D,
    // with the station bed under it (amb_cryo_station, take 2, a 28 s loop, 25%; it keeps running between floors).
    // Starts as the drop hatch opens (2 s fade in), fades out over 1.5 s when a floor's goal is done, comes back on the
    // next floor; ducks 6 dB for 0.8 s on the boss intro, the boss kill and Last Breath; drops to 15% while paused.
    public class RunMusic : MonoBehaviour
    {
        static RunMusic I;
        public static System.Action<string> Log;   // (editor checks)
        AudioSource music, bed; Game game; int floorSeen = -1; bool ended; float duckT, endT; string playing;
        const float MusicVol = 0.35f, BedVol = 0.25f, PauseVol = 0.15f;

        public static void Init(Game g)
        {
            if (I != null) return;
            var go = new GameObject("RunMusic"); I = go.AddComponent<RunMusic>(); I.game = g;
            I.music = I.Src(); I.bed = I.Src();
        }
        AudioSource Src() { var s = gameObject.AddComponent<AudioSource>(); s.loop = true; s.playOnAwake = false; s.spatialBlend = 0; s.volume = 0; s.ignoreListenerPause = true; return s; }
        public static void FloorEnd() { if (I != null && !I.ended) { I.ended = true; I.endT = 0; Log?.Invoke("floor end: fade out"); } }   // the goal's sting
        public static void Duck() { if (I != null) { I.duckT = 0.8f; Log?.Invoke("duck"); } }      // boss intro / boss down / Last Breath

        static AudioClip Clip(string n) => Resources.Load<AudioClip>("Audio/" + n);
        void Update()
        {
            float dt = Time.unscaledDeltaTime; var g = game; if (g == null) return;
            string want = g.run && !Game.SimRun && g.sector != null && g.sector.id == "cryo-station" ? "cryo" : null;
            endT += dt;
            if (g.floor != floorSeen && (!ended || endT > 1.6f)) { floorSeen = g.floor; ended = false; }   // the next floor: back in after the fade-out has played
            if (!g.run) floorSeen = -1;
            bool hatchOpen = g.menu == null || g.menu.HatchOpen;
            bool inRun = want != null && hatchOpen && (g.state == Game.State.Playing || g.state == Game.State.Paused || g.state == Game.State.Choosing);
            bool on = inRun && !ended;
            if (want != playing && want != null)
            {   // a Cryo run: load the clips once
                playing = want; music.clip = Clip("mus_run_cryo"); bed.clip = Clip("amb_cryo_station");
            }
            duckT -= dt;
            float k = g.state == Game.State.Paused ? PauseVol / MusicVol : 1f;
            float duck = duckT > 0 ? 0.5f : 1f;   // -6 dB
            Fade(music, on ? MusicVol * k * duck : 0, MusicVol / 2f, MusicVol / 1.5f, dt);
            Fade(bed, inRun ? (g.state == Game.State.Paused ? PauseVol : BedVol) : 0, BedVol / 2f, BedVol / 1f, dt);   // the bed: through floor ends and floor changes, 15% paused, out over 1 s on leaving, no ducking
        }
        static void Fade(AudioSource s, float target, float upRate, float downRate, float dt)
        {
            if (s.clip == null) return;
            float rate = target > s.volume ? (target - s.volume > 0.1f ? upRate : 1f) : downRate;   // (small duck steps happen fast)
            s.volume = Mathf.MoveTowards(s.volume, target, rate * dt);
            if (s.volume > 0 && !s.isPlaying) { s.Play(); Log?.Invoke(s.clip.name + " start"); }
            else if (s.volume <= 0 && s.isPlaying && target <= 0) { s.Pause(); Log?.Invoke(s.clip.name + " faded out"); }
        }
    }
}
