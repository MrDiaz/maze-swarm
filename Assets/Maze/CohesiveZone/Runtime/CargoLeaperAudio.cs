using UnityEngine;

namespace Maze
{
    // Presentation only. Existing crouch/release call sites and every gameplay clock stay unchanged.
    // Two fixed sample voices; no synthesized fallback, random pitch or queued/delayed warnings.
    public sealed class CargoLeaperAudio : MonoBehaviour
    {
        public static System.Action<string> Log;
        static CargoLeaperAudio instance;
        enum Cue { Windup, Pounce }
        sealed class Voice { public AudioSource source; public Cue cue; public float gain, began; }
        readonly Voice[] voices = new Voice[2];
        readonly float[] last = { -100f, -100f };
        readonly bool[] warned = new bool[2];
        readonly AudioClip[] clips = new AudioClip[2];
        Game game;
        int floorSeen;

        static CargoLeaperAudio Get(Game game)
        {
            if (instance != null && instance.game == game) return instance;
            if (instance != null) Destroy(instance.gameObject);
            var root = new GameObject("CargoLeaperRecordedAudio"); root.transform.SetParent(game.transform, false);
            var result = root.AddComponent<CargoLeaperAudio>(); result.game = game; result.floorSeen = game.floor;
            result.clips[0] = Resources.Load<AudioClip>("Audio/cargo_leaper_windup");
            result.clips[1] = Resources.Load<AudioClip>("Audio/cargo_leaper_pounce");
            for (int i = 0; i < result.voices.Length; i++)
            {
                var source = root.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                source.dopplerLevel = 0; source.pitch = 1; source.ignoreListenerPause = false;
                result.voices[i] = new Voice { source = source };
            }
            instance = result; return result;
        }

        public static void Windup(float distanceGain) => Play(Cue.Windup, distanceGain);
        public static void Pounce(float distanceGain) => Play(Cue.Pounce, distanceGain);

        static void Play(Cue cue, float distanceGain)
        {
            if (!CohesiveZone.Active || !Application.isPlaying || Game.SimRun || !Prefs.Sound || distanceGain <= .05f) return;
            var game = instance != null ? instance.game : Object.FindAnyObjectByType<Game>();
            if (game == null || game.state != Game.State.Playing) return;
            var audio = Get(game); int kind = (int)cue; float now = Time.unscaledTime;
            if (audio.floorSeen != game.floor)
            { audio.StopAll(); audio.floorSeen = game.floor; audio.last[0] = audio.last[1] = -100; }
            if (now - audio.last[kind] < (cue == Cue.Windup ? .12f : .10f)) return;
            var clip = audio.clips[kind];
            if (clip == null)
            {
                if (!audio.warned[kind]) { audio.warned[kind] = true; Debug.LogWarning("CargoLeaperAudio: missing recorded " + cue + "; leaving this optional cue silent."); }
                return;
            }
            Voice pick = null;
            foreach (var voice in audio.voices) if (!voice.source.isPlaying) { pick = voice; break; }
            // A launch may replace the oldest warning, but warnings never steal launch sounds.
            if (pick == null && cue == Cue.Pounce)
                foreach (var voice in audio.voices)
                    if (voice.cue == Cue.Windup && (pick == null || voice.began < pick.began)) pick = voice;
            if (pick == null) return;
            audio.last[kind] = now;
            pick.source.Stop(); pick.cue = cue; pick.began = now;
            pick.gain = Mathf.Clamp01(distanceGain) * (cue == Cue.Windup ? .52f : .62f);
            pick.source.clip = clip; pick.source.pitch = 1;
            pick.source.volume = pick.gain * FirstCryoAudio.CombatGain;
            pick.source.Play(); Log?.Invoke(clip.name);
        }

        void StopAll() { foreach (var voice in voices) if (voice != null && voice.source != null) voice.source.Stop(); }
        void Update()
        {
            if (game == null || !CohesiveZone.For(game) || Game.SimRun || !Prefs.Sound || game.state != Game.State.Playing)
            { StopAll(); return; }
            if (floorSeen != game.floor)
            { StopAll(); floorSeen = game.floor; last[0] = last[1] = -100; return; }
            float gain = FirstCryoAudio.CombatGain;
            foreach (var voice in voices) if (voice.source.isPlaying) voice.source.volume = voice.gain * gain;
        }
        void OnDisable() => StopAll();
        void OnDestroy() { if (instance == this) instance = null; }
    }
}
