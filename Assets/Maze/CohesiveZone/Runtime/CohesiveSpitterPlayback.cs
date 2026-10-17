using UnityEngine;

namespace Maze
{
    // STAGED ONLY. Drives real authored Idle/Walk/Spit clips; no transform flailing,
    // attacks, cooldown changes, damage callbacks or random numbers. Called by Swarm.SyncModel.
    public sealed class CohesiveSpitterPlayback : MonoBehaviour
    {
        public const float ReleaseSeconds = 11f / 30f;
        public const float NativeStride = .28f; // measured metres covered per authored .8 s Walk cycle
        public const float WalkSeconds = .8f;
        Animation animationPlayer;
        AnimationState idle, walk, spit;
        Transform muzzle;
        bool torsoMask;
        float currentSpeed;
        public bool Casting => preparing || recovering;
        bool preparing, recovering;
        float spitTime, walkCycles, idleTime;
        public bool Ready => animationPlayer != null && idle != null && walk != null && spit != null;
        public Vector3 MuzzleWorld => muzzle != null ? muzzle.position : transform.position + Vector3.up * .7f;

        public static CohesiveSpitterPlayback Attach(GameObject model)
        {
            if (model == null) return null;
            var old = model.GetComponent<CohesiveSpitterPlayback>(); if (old != null) return old.Ready ? old : null;
            var a = model.GetComponentInChildren<Animation>(); if (a == null) return null;
            AnimationState idle = null, walk = null, spit = null;
            foreach (AnimationState state in a)
            {
                string n = state.name.ToLowerInvariant();
                if (n == "idle" || n.EndsWith("|idle")) idle = state;
                if (n == "walk" || n.EndsWith("|walk")) walk = state;
                if (n == "spit" || n.EndsWith("|spit")) spit = state;
            }
            if (idle == null || walk == null || spit == null)
            { Debug.LogError("Authored spitter needs real legacy Idle, Walk and Spit clips."); return null; }
            var p = model.AddComponent<CohesiveSpitterPlayback>();
            p.animationPlayer = a; p.idle = idle; p.walk = walk; p.spit = spit;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == "Muzzle") { p.muzzle = t; break; }
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == "Spine") { spit.AddMixingTransform(t, true); spit.layer = 1; p.torsoMask = true; break; }
            // Manual time sampling keeps freeze/pause and movement phase deterministic.
            a.Stop(); a.enabled = false;
            foreach (AnimationState s in a) { s.enabled = false; s.speed = 0; }
            p.Sample(idle, 0); return p;
        }

        // Run immediately after the existing aiCd decrement, with the unchanged LOS,
        // distance and spawn eligibility. Wind-up uses cooldown time; it cannot release damage.
        public void BeforeShot(float secondsUntilShot, bool eligible)
        {
            if (!Ready) return;
            if (!eligible) { preparing = false; return; }
            if (!recovering && secondsUntilShot > 0 && secondsUntilShot <= ReleaseSeconds)
            { preparing = true; spitTime = ReleaseSeconds - secondsUntilShot; }
            else if (secondsUntilShot > ReleaseSeconds) preparing = false;
        }
        // Invoke on the same tick that EnemyAI.Spitter actually schedules the existing
        // glob/landing telegraph. A newly acquired target jumps to the authored release pose;
        // gameplay timing never waits for a cosmetic wind-up to finish.
        public void ShotReleased()
        {
            if (!Ready) return;
            preparing = false; recovering = true; spitTime = ReleaseSeconds;
            Sample(spit, spitTime);
        }
        public bool Sync(float dt, float worldSpeed, float totalUniformScale, bool frozen, bool spawned, bool reverseGait = false)
        {
            if (!Ready) return false;
            if (frozen || dt <= 0) return true;
            if (!spawned) { preparing = recovering = false; Sample(idle, 0); return true; }
            currentSpeed = worldSpeed;
            if (worldSpeed > .08f) walkCycles += (reverseGait ? -1 : 1) * worldSpeed * dt / Mathf.Max(.001f, NativeStride * totalUniformScale);
            else idleTime += dt;
            if (preparing) { Sample(spit, spitTime); return true; }
            if (recovering)
            {
                spitTime += dt;
                if (spitTime < spit.length) { Sample(spit, spitTime); return true; }
                recovering = false;
            }
            if (worldSpeed > .08f)
            {
                // cycles/sec = world metres/sec / (native stride * total model scale).
                Sample(walk, Mathf.Repeat(walkCycles, 1) * walk.length);
            }
            else { Sample(idle, Mathf.Repeat(idleTime, Mathf.Max(.001f, idle.length))); }
            return true;
        }
        void Sample(AnimationState chosen, float seconds)
        {
            foreach (AnimationState state in animationPlayer) state.enabled = state == chosen;
            if (chosen == spit && torsoMask)
            {
                // The same authored Walk/Idle drives hips and planted feet while Spit drives torso/head/arms.
                var lower = currentSpeed > .08f ? walk : idle;
                lower.enabled = true; lower.weight = 1; lower.speed = 0; lower.layer = 0;
                lower.time = lower == walk ? Mathf.Repeat(walkCycles, 1) * walk.length : Mathf.Repeat(idleTime, Mathf.Max(.001f, idle.length));
            }
            chosen.weight = 1; chosen.speed = 0; chosen.time = Mathf.Clamp(seconds, 0, chosen.length);
            animationPlayer.Sample();
        }
        void OnDisable()
        {
            preparing = recovering = false; spitTime = walkCycles = idleTime = currentSpeed = 0;
            if (Ready) Sample(idle, 0); // A pooled spawn frozen before its first Sync must start from the authored rest pose.
        }
    }
}
