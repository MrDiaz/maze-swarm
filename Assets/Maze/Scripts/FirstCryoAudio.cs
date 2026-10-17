using UnityEngine;

namespace Maze
{
    // Recorded presentation cues for original Cryo floor one and the full Cargo Depot route.
    // Two fixed 2D sources: reveal and accept. Each cue replaces its own prior
    // voice, uses unscaled cooldowns and contains no procedural tone fallback.
    public sealed class FirstCryoAudio : MonoBehaviour
    {
        static FirstCryoAudio instance;
        public static System.Action<string> Log;
        Game game;
        AudioSource revealSource, acceptSource;
        AudioClip revealClip, acceptClip, readyClip;
        float lastReveal = -100, lastAccept = -100, lastReady = -100;
        bool missingWarned; float focusUntil;
        static Game mixGame;
        public static float CombatGain => MixActive ? (instance != null && Time.unscaledTime < instance.focusUntil ? .60f : .85f) : 1f;

        static bool MixActive
        {
            get { if (mixGame == null) mixGame = FindAnyObjectByType<Game>(); return Active(mixGame); }
        }
        // Cargo recorded UI audio is independent of the optional VFX switch.
        public static bool Active(Game g) => Application.isPlaying && !Game.SimRun && g != null && g.run &&
            (CohesiveZone.For(g) || FirstCryoFx.Enabled && g.floor == 1 && g.sector != null && g.sector.id == "cryo-station");

        static FirstCryoAudio Get(Game g)
        {
            if (instance != null && instance.game == g) return instance;
            if (instance != null) Destroy(instance.gameObject);
            var go = new GameObject("FirstCryoAudio");
            go.transform.SetParent(g.transform, false);
            instance = go.AddComponent<FirstCryoAudio>(); instance.game = g;
            instance.revealSource = instance.Source(); instance.acceptSource = instance.Source();
            instance.revealClip = Resources.Load<AudioClip>("Audio/first_cryo_levelup_reveal");
            instance.acceptClip = Resources.Load<AudioClip>("Audio/first_cryo_upgrade_accept");
            instance.readyClip = Resources.Load<AudioClip>("Audio/dry_click");
            return instance;
        }

        AudioSource Source()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = 0;
            s.dopplerLevel = 0; s.pitch = 1; s.ignoreListenerPause = true;
            return s;
        }

        // Return true means the scoped recorded path is handled, including muted or
        // throttled calls. Callers must not emit a generic tap/synth afterward.
        public static bool Reveal(Game g)
        {
            if (!Active(g)) return false;
            if (!Prefs.Sound) return true;
            var a = Get(g); float now = Time.unscaledTime;
            if (now - a.lastReveal < .35f || a.acceptSource.isPlaying) return true; // Chained choices keep one confirmation cue.
            a.lastReveal = now;
            a.Play(a.revealSource, a.revealClip, .50f, "first_cryo_levelup_reveal");
            return true;
        }

        public static bool Accept(Game g)
        {
            if (!Active(g)) return false;
            if (!Prefs.Sound) return true;
            var a = Get(g); float now = Time.unscaledTime;
            if (now - a.lastAccept < .16f) return true;
            a.lastAccept = now;
            a.revealSource.Stop(); // Confirmation owns the UI mix once a card is chosen.
            a.Play(a.acceptSource, a.acceptClip, .60f, "first_cryo_upgrade_accept");
            return true;
        }

        // Optional replacement for the automatic dash-ready UI tap. It reuses
        // existing CC0 dry_click at a restrained level and never interrupts a
        // current upgrade acceptance. No new ready asset is required.
        public static bool Ready(Game g)
        {
            if (!Active(g)) return false;
            if (!Prefs.Sound) return true;
            var a = Get(g); float now = Time.unscaledTime;
            if (now - a.lastReady < 1 || a.acceptSource.isPlaying || a.revealSource.isPlaying) return true;
            a.lastReady = now;
            a.Play(a.acceptSource, a.readyClip, .14f, "first_cryo_dash_ready");
            return true;
        }

        void Play(AudioSource s, AudioClip clip, float volume, string name)
        {
            if (clip == null)
            {
                if (!missingWarned) { missingWarned = true; Debug.LogWarning("FirstCryoAudio: recorded cue missing; keeping this optional presentation cue silent."); }
                return;
            }
            if (name != "first_cryo_dash_ready") { focusUntil = Time.unscaledTime + clip.length; if (CombatAudio.I != null) CombatAudio.I.RefreshMix(); }
            s.Stop(); s.clip = clip; s.volume = volume; s.pitch = 1; s.Play();
            Log?.Invoke(name);
        }

        void Update()
        {
            if (Active(game) && Prefs.Sound) return;
            focusUntil = 0;
            if (revealSource != null && revealSource.isPlaying) revealSource.Stop();
            if (acceptSource != null && acceptSource.isPlaying) acceptSource.Stop();
        }
        void OnDestroy() { if (instance == this) instance = null; }
    }
}
