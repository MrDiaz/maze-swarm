using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Game feel plumbing (Design/game-feel-brief.md section 1, numbers in game-feel-numbers.csv):
    //   shake  - impulses (s, duration) combined as min(0.35, sqrt(sum s^2)), linear decay; offset s*0.5 m, roll s*2 deg,
    //            22 Hz noise; while a hostile telegraph on the hero has < 0.6 s left everything but hero hits caps at 0.04
    //   dips   - one time-scale request at a time (lowest wins), 60 ms ease back, max 1.6 s per 5 s (boss moments exempt);
    //            the game's own dt is scaled (UI keeps real time)
    //   flash  - a full-screen tint, alpha <= 0.35 (exceptions pass a cap), max 3 per second
    //   vignette - red edge on hero hits, the low-HP pulse, green on heals
    //   settings - Feel group: shake Full/Half/Off, reduce flashes, callouts, hints (PlayerPrefs)
    public static class Feel
    {
        public static int ShakeMode { get => PlayerPrefs.GetInt("ms.feel.shake", 0); set => PlayerPrefs.SetInt("ms.feel.shake", value); }
        public static bool ReduceFlashes { get => PlayerPrefs.GetInt("ms.feel.flash", 0) == 1; set => PlayerPrefs.SetInt("ms.feel.flash", value ? 1 : 0); }
        public static bool Callouts { get => PlayerPrefs.GetInt("ms.feel.callouts", 1) == 1; set => PlayerPrefs.SetInt("ms.feel.callouts", value ? 1 : 0); }

        struct Imp { public float a, t, dur; }
        static readonly List<Imp> shakes = new();
        public static float ShakeNow { get; private set; }
        public static bool TelegraphNear;   // set each frame by the game (a hurting telegraph on the hero)

        public static void Shake(float s, float dur, bool heroHit = false)
        {
            if (Game.SimRun || ShakeMode == 2) return;
            if (ShakeMode == 1) s *= 0.5f;
            if (TelegraphNear && !heroHit) s = Mathf.Min(s, 0.04f);
            shakes.Add(new Imp { a = s, t = dur, dur = Mathf.Max(0.01f, dur) });
        }
        public static Vector3 ShakeOffset(out float rollDeg)
        {
            float s = ShakeNow; rollDeg = 0;
            if (s <= 0) return Vector3.zero;
            float t = Time.unscaledTime * 22f;
            rollDeg = (Mathf.PerlinNoise(t, 7.3f) * 2 - 1) * s * 2f;
            return new Vector3(Mathf.PerlinNoise(t, 1.1f) * 2 - 1, (Mathf.PerlinNoise(t, 3.7f) * 2 - 1) * 0.5f, Mathf.PerlinNoise(t, 5.9f) * 2 - 1) * s * 0.5f;
        }

        // ---- camera push-in (the boss kill): FOV narrows by amt and eases back over dur (real time)
        static float zoomEnd, zoomDur = 1, zoomAmt;
        public static void Zoom(float amt, float dur) { if (Game.SimRun) return; zoomAmt = amt; zoomDur = dur; zoomEnd = Time.unscaledTime + dur; }
        public static float ZoomNow { get { float left = zoomEnd - Time.unscaledTime; return left > 0 ? zoomAmt * Mathf.Sin(Mathf.PI * (1f - left / zoomDur)) : 0; } }

        // ---- time dips
        static float dipScale = 1, dipLeft, easeT; static bool dipExempt;
        static readonly List<(float at, float len)> spent = new();
        public static float Scale { get; private set; } = 1f;
        public static void Dip(float scale, float dur, bool exempt = false)
        {
            if (Game.SimRun) return;
            if (!exempt && TelegraphNear) return;
            if (!exempt)
            {
                float used = 0; foreach (var s in spent) used += s.len;
                if (used + dur > 1.6f) return;
                spent.Add((Time.unscaledTime, dur));
            }
            if (dipLeft > 0 && scale > dipScale) return;   // one at a time, the lowest wins
            dipScale = scale; dipLeft = dur; dipExempt = exempt;
        }

        // ---- flash + vignette (drawn by RunUi)
        public static Color FlashColor; public static float FlashAlpha;
        static float flashLeft, flashDur, flashA; static readonly List<float> flashTimes = new();
        public static void Flash(Color c, float alpha, float dur, float cap = 0.35f)
        {
            if (Game.SimRun) return;
            float now = Time.unscaledTime; flashTimes.RemoveAll(x => now - x > 1f);
            if (flashTimes.Count >= 3) return;
            if (ReduceFlashes) { alpha *= 0.5f; if (flashTimes.Count > 0 && now - flashTimes[^1] < 0.6f) return; }
            flashTimes.Add(now); FlashColor = c; flashA = Mathf.Min(alpha, cap); flashDur = flashLeft = dur;
        }
        public static Color VignetteColor = Color.red; public static float VignetteAlpha;
        static float vigLeft, vigDur, vigA; static Color vigC;
        public static void Vignette(Color c, float alpha, float dur) { if (Game.SimRun) return; vigC = c; vigA = alpha; vigDur = vigLeft = dur; }

        public static void Tick(float dt, float heroHpFrac, bool playing)
        {
            // shake
            float sum = 0;
            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                var s = shakes[i]; s.t -= dt; if (s.t <= 0) { shakes.RemoveAt(i); continue; }
                shakes[i] = s; float k = s.a * (s.t / s.dur); sum += k * k;
            }
            ShakeNow = Mathf.Min(0.35f, Mathf.Sqrt(sum));
            // dips (ease back over 60 ms)
            float now = Time.unscaledTime; spent.RemoveAll(x => now - x.at > 5f);
            if (dipLeft > 0) { dipLeft -= dt; Scale = dipScale; if (dipLeft <= 0) easeT = 0.06f; }
            else if (easeT > 0) { easeT -= dt; Scale = Mathf.Lerp(1f, dipScale, easeT / 0.06f); }
            else Scale = 1f;
            if (!playing) Scale = 1f;
            // flash
            if (flashLeft > 0) { flashLeft -= dt; FlashAlpha = flashA * Mathf.Clamp01(flashLeft / flashDur); } else FlashAlpha = 0;
            // vignette: a hit's red edge over the low-HP pulse
            float pulse = 0; Color pc = new Color(0.9f, 0.05f, 0.05f);
            if (playing && heroHpFrac > 0)
            {
                if (heroHpFrac <= 0.15f) pulse = 0.35f * (0.5f + 0.5f * Mathf.Sin(now * 2f * Mathf.PI * 2.0f));
                else if (heroHpFrac <= 0.25f) pulse = 0.28f * (0.5f + 0.5f * Mathf.Sin(now * 2f * Mathf.PI * 1.3f));
                else if (heroHpFrac <= 0.4f) pulse = 0.18f * (0.5f + 0.5f * Mathf.Sin(now * 2f * Mathf.PI * 0.8f));
            }
            if (vigLeft > 0) { vigLeft -= dt; float a = vigA * Mathf.Clamp01(vigLeft / vigDur); if (a >= pulse) { VignetteAlpha = a; VignetteColor = vigC; return; } }
            VignetteAlpha = pulse; VignetteColor = pc;
        }
        public static void Reset() { shakes.Clear(); dipLeft = 0; easeT = 0; Scale = 1; FlashAlpha = 0; VignetteAlpha = 0; vigLeft = 0; flashLeft = 0; }
    }

    // SWARM CHAIN (2.3): a pool filled by kills (S 1, M 2, L 5, Elite 10, Mini-boss 20; close call +3, perfect dash +10),
    // draining at 1 + 0.08*pool per second, -25% when the hero is hit. Tiers multiply score fully, biomass at half.
    public static class Chain
    {
        public static float Pool; public static int Tier, PeakTier; public static readonly float[] TimeAt = new float[5];
        static readonly float[] Th = { 0, 10, 25, 45, 70 };
        public static readonly string[] Names = { "", "CHAIN", "RAMPAGE", "CARNAGE", "EXTERMINATION" };
        public static readonly Color[] Colors = { new Color(0.6f, 0.6f, 0.6f), Color.white, new Color(1f, 0.9f, 0.2f), new Color(1f, 0.55f, 0.15f), new Color(1f, 0.3f, 0.15f) };
        static readonly float[] calloutAt = new float[5];
        public static float ScoreMult => 1f + 0.25f * Tier;
        public static float BiomassMult => 1f + 0.125f * Tier;
        public static float Bump; static float frac;
        public static float NextFrac => Tier >= 4 ? 1f : Mathf.InverseLerp(Th[Tier], Th[Tier + 1], Pool);
        public static void Reset() { Pool = 0; Tier = 0; PeakTier = 0; for (int i = 0; i < 5; i++) TimeAt[i] = 0; frac = 0; Bump = 0; }
        public static void Add(float pts, Game g)
        {
            Pool += pts; Bump = 0.4f;
            int t = TierOf(Pool);
            if (t > Tier)
            {
                Tier = t; PeakTier = Mathf.Max(PeakTier, t);
                if (Feel.Callouts && Time.unscaledTime - calloutAt[t] > 8f) { calloutAt[t] = Time.unscaledTime; g.hud.ShowBanner(Names[t]); }
                if (t >= 4) Haptics.Medium(); else Haptics.Light();
            }
        }
        public static void Hit() { Pool *= 0.75f; Tier = TierOf(Pool); }
        public static void Tick(float dt)
        {
            if (Pool > 0) Pool = Mathf.Max(0, Pool - (1 + 0.08f * Pool) * dt);
            Tier = TierOf(Pool); TimeAt[Tier] += dt; Bump -= dt;
        }
        static int TierOf(float p) { int t = 0; for (int i = 1; i < 5; i++) if (p >= Th[i]) t = i; return t; }
        // biomass with a fractional accumulator
        public static int Biomass(int baseOrbs) { float v = baseOrbs * BiomassMult + frac; int n = Mathf.FloorToInt(v); frac = v - n; return n; }
    }
}
