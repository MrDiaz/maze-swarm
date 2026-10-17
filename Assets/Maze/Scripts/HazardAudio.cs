using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Hazard and VFX sounds (Sounds/VFX/final, ElevenLabs take 1 of each, picked 2026-10-13).
    //   Loops: callers "touch" a loop every frame it should sound (id, an owner key, a world point); an untouched loop
    //   fades out in 0.3 s. Per id only the 4 owners nearest the hero get a voice (2 for poison; shared poison gain budget). 3D, linear rolloff 2 m to 14 m, random
    //   start offset, 0.3 s fade in.
    //   One-shots: 0.7 spatial, +-8% pitch, optional voice cap and per-key throttle.
    // The listener sits on the camera high above the floor, so every source is placed at the listener plus its offset
    // from the hero: distances are measured on the floor, from the hero, as the rolloff numbers intend.
    public class HazardAudio : MonoBehaviour
    {
        static HazardAudio I;
        public static System.Action<string> Log;   // (editor checks: which clip fired)
        static readonly Dictionary<string, AudioClip> clips = new();
        static AudioClip Clip(string id) { if (!clips.TryGetValue(id, out var c)) clips[id] = c = Resources.Load<AudioClip>("Audio/" + id); return c; }

        static readonly Dictionary<string, float> LoopVol = new()
        {
            { "amb_acid_pool", 0.12f }, { "amb_toxic_gas", 0.12f }, { "amb_fire", 0.45f }, { "amb_molten_slag", 0.4f },
            { "amb_laser_fence", 0.3f }, { "amb_electric_grate", 0.35f }, { "amb_radiation", 0.3f },
        };
        class Want { public string id; public object key; public Vector3 pos; public float seen; }
        class Voice { public AudioSource src; public string id; public object key; public Vector3 pos; public float k; public bool keep; }
        readonly Dictionary<(string, object), Want> wants = new();
        readonly List<Voice> voices = new();
        readonly List<AudioSource> shots = new(); readonly List<(AudioSource s, string id)> shotIds = new();
        readonly Dictionary<object, float> lastShot = new();
        const int MaxPerLoop = 4; const float Fade = 0.3f;
        float lastAcidSplash = -10f;
        static bool PoisonLoop(string id) => id == "amb_acid_pool" || id == "amb_toxic_gas";

        static HazardAudio Get()
        {
            if (I != null) return I;
            var go = new GameObject("HazardAudio"); I = go.AddComponent<HazardAudio>();
            for (int i = 0; i < 14; i++) { var c = new GameObject("Shot" + i); c.transform.SetParent(go.transform, false); var s = c.AddComponent<AudioSource>(); Setup(s, 0.7f); I.shots.Add(s); I.shotIds.Add((s, null)); }
            return I;
        }
        static void Setup(AudioSource s, float spatial)
        {
            s.playOnAwake = false; s.spatialBlend = spatial; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2f; s.maxDistance = 14f; s.dopplerLevel = 0; s.spread = 0;
        }
        static Transform Listener => Camera.main != null ? Camera.main.transform : null;
        static Vector3 hero;   // the hero's floor point (set each frame)
        static bool Live => !Game.SimRun && Application.isPlaying;
        // a floor point -> where the source goes (the listener plus its offset from the hero, on the floor)
        static Vector3 Place(Vector3 w) { var l = Listener; if (l == null) return w; var d = w - hero; d.y = 0; return l.position + d; }

        // ---- loops
        public static void Loop(string id, object key, Vector3 at)
        {
            if (!Live || key == null) return; var h = Get();
            var k = (id, key);
            if (!h.wants.TryGetValue(k, out var w)) h.wants[k] = w = new Want { id = id, key = key };
            w.pos = at; w.seen = Time.time;
        }
        // the point of a cell list nearest the hero (for long fences, big cell surfaces)
        public static Vector3 Nearest(List<Vector3> pts, Vector3 fallback)
        {
            if (pts == null || pts.Count == 0) return fallback; float bd = float.MaxValue; var b = fallback;
            foreach (var p in pts) { float d = (p.x - hero.x) * (p.x - hero.x) + (p.z - hero.z) * (p.z - hero.z); if (d < bd) { bd = d; b = p; } }
            return b;
        }
        public static Vector3 HeroFloor => hero;

        // ---- one-shots
        public static void Shot(string id, Vector3 at, float vol, int maxVoices = 0, object throttleKey = null, float throttle = 0f, bool flat = false)
        {
            if (!Live) return;
            if (FirstCryoFx.Cargo && id == "sfx_cryo_vent_burst")
            {
                vol *= .60f; maxVoices = maxVoices > 0 ? Mathf.Min(maxVoices, 2) : 2;
                throttleKey = "cargo-cooling-jet"; throttle = Mathf.Max(throttle, .1f);
            }
            if (FirstCryoFx.Active && id == "sfx_hit_sparks") { if (!Prefs.Sound) return; vol *= .45f; maxVoices = 2; throttleKey = "first-cryo-metal-hit"; throttle = .07f; }
            if (FirstCryoFx.Active && id == "sfx_telegraph_warning") vol *= .65f;
            var c = Clip(id); if (c == null) return; var h = Get();
            if (id == "sfx_acid_splash")
            {
                if (Time.time - h.lastAcidSplash < .18f) return;
                vol *= .4f;
                maxVoices = maxVoices > 0 ? Mathf.Min(maxVoices, 2) : 2;
            }
            if (throttleKey != null) { if (h.lastShot.TryGetValue(throttleKey, out var t) && Time.time - t < throttle) return; h.lastShot[throttleKey] = Time.time; }
            if (maxVoices > 0) { int n = 0; foreach (var (s, sid) in h.shotIds) if (sid == id && s.isPlaying) n++; if (n >= maxVoices) return; }
            int pick = -1;
            for (int i = 0; i < h.shots.Count; i++) if (!h.shots[i].isPlaying) { pick = i; break; }
            if (pick < 0) return;   // all 14 busy: drop it
            var src = h.shots[pick]; h.shotIds[pick] = (src, id);
            src.spatialBlend = flat ? 0f : 0.7f; src.transform.position = flat ? (Listener != null ? Listener.position : at) : Place(at);
            src.clip = c; src.volume = vol; src.pitch = Random.Range(0.92f, 1.08f); src.Play();
            if (id == "sfx_acid_splash") h.lastAcidSplash = Time.time;
            Log?.Invoke(id);
        }
        public static void ClearAll()
        {
            if (I == null) return;
            I.wants.Clear();
            foreach (var v in I.voices) v.src.Stop();
            foreach (var s in I.shots) s.Stop();
        }

        void LateUpdate()
        {
            var g = FindHero();
            if (g != null) hero = MazeMap.ToWorld(g.player.p, 0f);
            float now = Time.time, dt = Time.unscaledDeltaTime;
            // the wanted loops, per id, nearest first; keep the 4 nearest
            foreach (var v in voices) v.keep = false;
            var byId = new Dictionary<string, List<Want>>();
            var dead = new List<(string, object)>();
            foreach (var kv in wants)
            {
                if (now - kv.Value.seen > 0.15f) { dead.Add(kv.Key); continue; }
                if (!byId.TryGetValue(kv.Value.id, out var l)) byId[kv.Value.id] = l = new List<Want>(); l.Add(kv.Value);
            }
            foreach (var k in dead) wants.Remove(k);
            foreach (var kv in byId)
            {
                var l = kv.Value; l.Sort((a, b) => Flat(a.pos).CompareTo(Flat(b.pos)));
                for (int i = 0; i < l.Count && i < (PoisonLoop(kv.Key) ? 2 : MaxPerLoop); i++)
                {
                    var w = l[i]; if (Flat(w.pos) > 16f * 16f) continue;   // beyond the rolloff: no voice
                    var v = voices.Find(x => x.id == w.id && Equals(x.key, w.key));
                    if (v == null)
                    {
                        var c = Clip(w.id); if (c == null) continue;
                        v = voices.Find(x => x.k <= 0 && !x.src.isPlaying);
                        if (v == null) { var go = new GameObject("Loop"); go.transform.SetParent(transform, false); var s = go.AddComponent<AudioSource>(); Setup(s, 1f); s.loop = true; v = new Voice { src = s }; voices.Add(v); }
                        v.id = w.id; v.key = w.key; v.k = 0; v.src.clip = c; v.src.time = Random.Range(0f, c.length * 0.95f); v.src.volume = 0; v.src.Play();
                        Log?.Invoke(w.id + " loop");
                    }
                    v.pos = w.pos; v.keep = true;
                }
            }
            // Include fading owners in the shared poison budget so handoffs cannot boost the mix.
            float poisonWeight = 0f;
            foreach (var v in voices)
            {
                v.k = Mathf.MoveTowards(v.k, v.keep ? 1f : 0f, dt / Fade);
                if (v.src.isPlaying && PoisonLoop(v.id)) poisonWeight += v.k;
            }
            foreach (var v in voices)
            {
                if (v.src.isPlaying)
                {
                    v.src.transform.position = Place(v.pos);
                    float gain = v.k * (LoopVol.TryGetValue(v.id ?? "", out var lv) ? lv : 0.3f);
                    v.src.volume = PoisonLoop(v.id) ? gain / Mathf.Max(1f, poisonWeight) : gain;
                }
                if (!v.keep && v.k <= 0 && v.src.isPlaying) { v.src.Stop(); v.id = null; v.key = null; }
            }
        }
        static float Flat(Vector3 p) => (p.x - hero.x) * (p.x - hero.x) + (p.z - hero.z) * (p.z - hero.z);
        static Game game;
        static Game FindHero() { if (game == null) game = FindAnyObjectByType<Game>(); return game != null && game.player != null ? game : null; }
    }
}
