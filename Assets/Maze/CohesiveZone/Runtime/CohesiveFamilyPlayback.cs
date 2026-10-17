using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Staged presentation only. Reads Enemy/cfg; never changes timers, attacks, damage,
    // AI, position, collision or Random.state. All movement is authored bone animation.
    public sealed class CohesiveFamilyPlayback : MonoBehaviour
    {
        Animation player;
        readonly Dictionary<string, AnimationState> clips = new Dictionary<string, AnimationState>();
        string role;
        float cycles, idleSeconds;
        bool initialized;
        float legacyPhase, legacySpeed = 2f;
        public float LegacyBloaterPhase => Mathf.Repeat(legacyPhase, 1f);
        public float LegacyBloaterSpeed => legacySpeed;
        public bool Ready => player != null && clips.ContainsKey("Idle") && clips.ContainsKey("Walk");
        float NativeStride => role == "runner" ? .3136f : role == "carrier" || role == "carapace" ? .24f : .28f;

        public static CohesiveFamilyPlayback Attach(GameObject model, string familyRole)
        {
            if (model == null || string.IsNullOrEmpty(familyRole) || familyRole == "spitter") return null;
            var old = model.GetComponent<CohesiveFamilyPlayback>();
            if (old != null) return old.Ready ? old : null;
            var a = model.GetComponentInChildren<Animation>(); if (a == null) return null;
            var p = model.AddComponent<CohesiveFamilyPlayback>(); p.role = familyRole; p.player = a;
            foreach (AnimationState state in a)
            {
                string n = state.name; int bar = n.LastIndexOf('|'); if (bar >= 0) n = n.Substring(bar + 1);
                p.clips[n] = state;
            }
            var required = new List<string> { "Idle", "Walk", "MeleeWindup", "Melee" };
            if (familyRole == "runner") required.AddRange(new[] { "Crouch", "Leap", "Land", "Sidestep" });
            if (familyRole == "carrier" || familyRole == "carapace") required.AddRange(new[] { "Bloat", "Burst" });
            foreach (string n in required)
                if (!p.clips.ContainsKey(n)) { Debug.LogError("Cargo family is missing authored clip " + n); Destroy(p); return null; }
            // Preserve planted gait while upper-body contact/recovery plays. No root lunge.
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == "Spine")
                {
                    p.clips["Melee"].AddMixingTransform(t, true); p.clips["Melee"].layer = 1;
                    p.clips["MeleeWindup"].AddMixingTransform(t, true); p.clips["MeleeWindup"].layer = 1;
                    break;
                }
            a.enabled = false;
            // Do not sample yet: Spawn already consumed the existing Random.value to seed Walk.
            // Sync captures that normalized phase before replacing pose/time, without another draw.
            return p;
        }

        public bool Sync(Enemy e, MazeConfig cfg, float dt, float totalScale, bool meleeMayContactSoon = false)
        {
            if (!Ready) return false;
            if (!initialized)
            {
                float seed = Mathf.Repeat(clips["Walk"].normalizedTime, 1f);
                cycles = legacyPhase = seed; legacySpeed = clips["Walk"].speed; initialized = true;
            }
            if (e.frozen > 0 || dt <= 0) return true;
            // A shadow of the old 1.2s Bloater Walk clock preserves the existing LateUpdate
            // footstep eligibility/RNG branch independently of the new distance-based gait.
            // It must be compared with the existing baseline in the integration runtime test.
            float legacyWant;
            // Measured old bloater.glb max X/Z extent, before replacement. Using the
            // new mesh fit here would alter this shadow clock's historical rate.
            float legacyBloaterFit = 2.4f * cfg.enemy.radius / 1.87635005f;
            if (e.spawnT < 1) legacyWant = 2f;
            else if (e.hop < 1 || e.leap == 2) legacyWant = 3f;
            else if (e.leap == 1) legacyWant = 0f;
            else if (e.leap == 3) legacyWant = e.leapHit ? .4f : .15f;
            else if (e.cast > 0) legacyWant = .15f;
            else if (e.atkAnim > 0 || e.k.sqrMagnitude > 2.25f) legacyWant = .3f;
            else if (e.v.magnitude < .15f) legacyWant = 0f;
            else legacyWant = Mathf.Clamp(e.v.magnitude / Mathf.Max(.001f, (e.type.stride > 0 ? e.type.stride : 1.25f) * legacyBloaterFit * e.scale), .5f, 4f);
            legacySpeed = Mathf.MoveTowards(legacySpeed, legacyWant, dt * 20f);
            legacyPhase += legacySpeed * dt / 1.2000000476837158f;

            float speed = e.v.magnitude;
            if (e.spawnT < 1) { Sample("Idle", 0); return true; }
            if (speed > .08f) cycles += speed * dt / Mathf.Max(.001f, NativeStride * Mathf.Abs(totalScale));
            else idleSeconds += dt;
            if (role == "runner")
            {
                if (e.leap == 1) { Sample("Crouch", .6f * Mathf.Clamp01(e.leapT / Mathf.Max(.001f, cfg.leaper.crouch))); return true; }
                if (e.leap == 2) { Sample("Leap", (14f / 30f) * Mathf.Clamp01(e.leapT / Mathf.Max(.001f, cfg.leaper.time))); return true; }
                if (e.leap == 3) { Sample("Land", .8f * Mathf.Clamp01(e.leapT / Mathf.Max(.001f, e.stunFor))); return true; }
                if (e.dodgeCd > cfg.leaper.dodgeCooldown - .35f)
                { Sample("Sidestep", Mathf.Clamp(cfg.leaper.dodgeCooldown - e.dodgeCd, 0, .35f)); return true; }
            }
            if (clips.ContainsKey("Bloat"))
            {
                if (e.cast > 0) { Sample("Bloat", .7f * Mathf.Clamp01(1 - e.cast / Mathf.Max(.001f, cfg.bloater.castTime))); return true; }
                // e.squash is still advanced by existing Swarm code; only its legacy root
                // scale assignment is bypassed for these authored views.
                if (e.squash >= 0 && e.squash <= .35f) { Sample("Burst", e.squash); return true; }
            }
            if (e.atkAnim > 0) { Sample("Melee", Mathf.Clamp(.25f - e.atkAnim, 0, .25f), speed); return true; }
            if (meleeMayContactSoon && e.atkCd > 0 && e.atkCd <= .15f)
            { Sample("MeleeWindup", .15f - e.atkCd, speed); return true; }
            Sample(speed > .08f ? "Walk" : "Idle", speed > .08f ? Mathf.Repeat(cycles, 1) * clips["Walk"].length : Mathf.Repeat(idleSeconds, clips["Idle"].length));
            return true;
        }
        void Sample(string name, float seconds, float groundSpeed = 0)
        {
            var chosen = clips[name];
            foreach (AnimationState st in player) { st.enabled = st == chosen; st.speed = 0; st.weight = 1; }
            if (name == "Melee" || name == "MeleeWindup")
            {
                var lower = clips[groundSpeed > .08f ? "Walk" : "Idle"];
                lower.enabled = true; lower.layer = 0;
                lower.time = groundSpeed > .08f ? Mathf.Repeat(cycles, 1) * lower.length : Mathf.Repeat(idleSeconds, lower.length);
            }
            chosen.time = Mathf.Clamp(seconds, 0, chosen.length); player.Sample();
        }
        void OnDisable()
        {
            initialized = false; cycles = idleSeconds = legacyPhase = 0; legacySpeed = 2;
            // A pooled model must not retain its last Leap/Bloat/contact pose if the next
            // spawn is frozen before its first Sync. Spawn still seeds Walk afterward.
            if (Ready) Sample("Idle", 0);
        }
    }
}
