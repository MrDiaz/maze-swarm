using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Pooled one-shot playback for the recorded combat sounds in Resources/Audio (cut from Manuel's
    // sound library by Tools/prep_sfx.py, see Sounds/CREDITS.txt): 2-3 takes per gunshot rotated at
    // random, the shotgun pump, reload handling (dry click, mag out / in, shell in), Drone squishes (a juicy impact layered with a goo squish), wet bullet hits
    // and goo drips. Every play gets a random
    // pitch (+-6%) and volume (+-10%); each kind has a voice cap (the oldest voice of that kind is
    // stolen), wet sounds get quieter as more of the same kind pile up (1/sqrt(n+1)), and gunfire ducks
    // a little under the squishes so a barrel chain reads as splatter, not clipping.
    public class CombatAudio : MonoBehaviour
    {
        public static CombatAudio I;

        enum Kind { Shot, Smg, Pump, Squish, Hit, Drip, Goo, Gear, Bloat }
        public enum GearSound { DryClick, MagOut, MagIn, ShellIn, Rack }
        static readonly int[] Caps = { 4, 4, 2, 6, 3, 4, 6, 3, 3 };
        class Voice { public AudioSource src; public Kind kind; public float baseVol; }

        const int PoolSize = 24;
        readonly List<Voice> voices = new();
        AudioClip[] rifle, smg, shotgun, pump, squish, goo, hit, drip;
        AudioClip dry, magOut, magIn, shellIn, bloatBurst, bloatSpawn, bloatStep;
        float squishesT = -1; int squishesLeft;
        float duck = 1, lastHit, pumpT = -1;

        public void Init()
        {
            I = this;
            rifle = LoadSet("rifle", 3); smg = LoadSet("smg", 3); shotgun = LoadSet("shotgun", 3); pump = LoadSet("shotgun_pump", 2);
            bloatBurst = Load("bloater_burst"); bloatSpawn = Load("bloater_spawn"); bloatStep = Load("bloater_step");
            dry = Load("dry_click"); magOut = Load("mag_out"); magIn = Load("mag_in"); shellIn = Load("shell_in");
            squish = LoadSet("squish", 4); goo = LoadSet("goo", 7); hit = LoadSet("hit", 3); drip = LoadSet("drip", 3);
            for (int i = 0; i < PoolSize; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0; s.loop = false; s.dopplerLevel = 0;
                voices.Add(new Voice { src = s });
            }
        }

        static AudioClip Load(string name)
        {
            var c = Resources.Load<AudioClip>("Audio/" + name);
            if (c == null) Debug.LogWarning($"Maze: Resources/Audio/{name} missing (run Tools/prep_sfx.py)");
            return c;
        }

        static AudioClip[] LoadSet(string name, int n)
        {
            var set = new AudioClip[n];
            for (int i = 0; i < n; i++) set[i] = Load($"{name}_{i + 1}");
            return set;
        }

        static AudioClip Any(AudioClip[] set) => set[Random.Range(0, set.Length)];

        static bool IsGunfire(Kind k) => k == Kind.Shot || k == Kind.Smg || k == Kind.Pump;

        Voice Pick(Kind k, out int same)
        {
            same = 0; Voice free = null, oldestSame = null, oldest = null;
            foreach (var v in voices)
            {
                if (!v.src.isPlaying) { free ??= v; continue; }
                if (oldest == null || v.src.time > oldest.src.time) oldest = v;
                if (v.kind != k) continue;
                same++;
                if (oldestSame == null || v.src.time > oldestSame.src.time) oldestSame = v;
            }
            if (same >= Caps[(int)k]) { same--; return oldestSame; }
            return free ?? oldest;
        }

        void Play(AudioClip clip, Kind k, float vol)
        {
            if (clip == null || !Prefs.Sound) return;
            var v = Pick(k, out int same);
            if (v == null) return;
            if (!IsGunfire(k)) vol /= Mathf.Sqrt(1 + same); // a crowd dying at once must not stack past full scale
            v.src.Stop();
            v.kind = k; v.src.clip = clip;
            v.src.pitch = Random.Range(0.94f, 1.06f);
            v.baseVol = vol * Random.Range(0.9f, 1.1f);
            v.src.volume = v.baseVol * (IsGunfire(k) ? duck : 1);
            v.src.Play();
        }

        // pump: rack the shotgun after the shot (not after the last shell: the reload racks it)
        public void Fire(string weapon, bool pump = true)
        {
            switch (weapon)
            {
                case "SMG": Play(Any(smg), Kind.Smg, 0.25f); break;
                case "SHOTGUN": Play(Any(shotgun), Kind.Shot, 0.5f); if (pump) pumpT = 0.34f; break;
                default: Play(Any(rifle), Kind.Shot, 0.42f); break;
            }
        }

        public void Gear(GearSound g)
        {
            switch (g)
            {
                case GearSound.DryClick: Play(dry, Kind.Gear, 0.5f); break;
                case GearSound.MagOut: Play(magOut, Kind.Gear, 0.5f); break;
                case GearSound.MagIn: Play(magIn, Kind.Gear, 0.6f); break;
                case GearSound.ShellIn: Play(shellIn, Kind.Gear, 0.5f); break;
                case GearSound.Rack: pumpT = -1; Play(Any(pump), Kind.Pump, 0.4f); break;
            }
        }

        // the Bloater: a deep burst then a few squishes trailing after it; a wet squelch when a sac lets minis
        // out; a low thud on footfalls, quieter with distance
        public void BloaterBurst() { Play(bloatBurst, Kind.Bloat, 0.95f); duck = 0.6f; squishesLeft = 3; squishesT = 0.06f; }
        public void BloaterSpawn() => Play(bloatSpawn, Kind.Bloat, 0.7f);
        public void BloaterStep(float dist) { if (dist < 22f) Play(bloatStep, Kind.Bloat, 0.45f * Mathf.Clamp01(1.2f - dist / 22f)); }

        public void Squish()
        {
            Play(Any(squish), Kind.Squish, 0.6f);
            Play(Any(goo), Kind.Goo, 0.42f);
            duck = 0.68f;
        }

        public void Hit()
        {
            if (Time.time - lastHit < 0.035f) return; // the SMG lands ~16 hits a second
            lastHit = Time.time;
            Play(Any(hit), Kind.Hit, 0.32f);
        }

        // a goo blob landing (Fx calls this when a blob splats)
        public void Drip()
        {
            if (Random.value < 0.45f) return;
            Play(Any(drip), Kind.Drip, 0.25f);
        }

        public void StopAll()
        {
            foreach (var v in voices) v.src.Stop();
            pumpT = -1; squishesT = -1;
        }

        void Update()
        {
            if (squishesT >= 0) { squishesT -= Time.deltaTime; if (squishesT < 0 && squishesLeft-- > 0) { Play(Any(squish), Kind.Squish, 0.5f); Play(Any(goo), Kind.Goo, 0.4f); squishesT = Random.Range(0.07f, 0.14f); } }
            if (pumpT >= 0) { pumpT -= Time.deltaTime; if (pumpT < 0) Play(Any(pump), Kind.Pump, 0.4f); } // waits while paused
            duck = Mathf.MoveTowards(duck, 1, Time.unscaledDeltaTime * 1.4f);
            foreach (var v in voices)
                if (v.src.isPlaying && IsGunfire(v.kind)) v.src.volume = v.baseVol * duck;
        }
    }
}
