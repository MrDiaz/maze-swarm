using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Zone mechanics: a SectorDef lists hazard ids; each floor of a run starts one instance of each (Begin), ticks it
    // (Tick), tells it when the hero steps onto / off a cell (Enter / Exit, with the cell's legend char so layouts can
    // carry hazard letters), and ends it when the floor ends (End). New mechanics subclass Hazard and register in Make.
    public abstract class Hazard
    {
        protected Game g;
        public virtual void Begin(Game game) { g = game; }
        public virtual void Tick(float dt) { }
        public virtual void Enter(int cell, char ch) { }
        public virtual void Exit(int cell, char ch) { }
        public virtual void End() { }
        // hero modifiers the game reads each frame (1 = no effect)
        public virtual float MoveMul => 1f;
        public virtual float Friction => 1f;      // < 1: sliding (ice)
        public virtual Vector2 Push => Vector2.zero; // conveyor floors
        public virtual float KnockMul => 1f;      // how far hits throw enemies (low gravity)
    }

    public static class Hazards
    {
        static readonly List<Hazard> live = new();
        static int lastCell = -1; static char lastCh;
        public static bool Enabled = true;   // a global switch (the sims / perf runs may turn zone mechanics off)
        public static IReadOnlyList<Hazard> Live => live;

        // the toolkit: id -> mechanic (filled in as the zone mechanics land)
        public static Hazard Make(string id) => id switch
        {
            "acid" => new AcidPools(), "toxicrain" => new ToxicRain(), "lowgrav" => new LowGravity(), "ice" => new Ice(),
            "dark" => new Darkness(), "fog" => new FogOfWar(), "conveyor" => new Conveyors(), "collapse" => new Collapse(),
            "vents" => new Vents(), "turrets" => new Turrets(),
            _ => null,
        };

        public static void Begin(Game g, string[] ids)
        {
            End();
            if (!Enabled) return;
            ids ??= new string[0];
            foreach (var h in ZoneCells.ForLayout(g)) { h.Begin(g); live.Add(h); }   // the layout's own mechanic letters
            foreach (var id in ids) { var h = Make(id); if (h == null) { Debug.LogWarning("Maze: unknown hazard " + id); continue; } h.Begin(g); live.Add(h); }
        }

        public static void Tick(Game g, float dt)
        {
            if (live.Count == 0) return;
            int cell = MazeMap.CellIndex(g.player.p);
            if (cell != lastCell)
            {
                if (lastCell >= 0) for (int i = 0; i < live.Count; i++) live[i].Exit(lastCell, lastCh);
                char ch = cell >= 0 ? MazeMap.tag[cell / MazeMap.W, cell % MazeMap.W] : '#';
                if (cell >= 0) for (int i = 0; i < live.Count; i++) live[i].Enter(cell, ch);
                lastCell = cell; lastCh = ch;
            }
            for (int i = 0; i < live.Count; i++) live[i].Tick(dt);   // (a hazard can end the run, which clears the list)
        }

        public static void End() { foreach (var h in live) h.End(); live.Clear(); lastCell = -1; }

        public static float MoveMul { get { float m = 1; foreach (var h in live) m *= h.MoveMul; return m; } }
        public static float Friction { get { float m = 1; foreach (var h in live) m *= h.Friction; return m; } }
        public static string Name(string id) => id switch
        {
            "acid" => "ACID POOLS", "toxicrain" => "TOXIC RAIN", "lowgrav" => "LOW GRAVITY", "ice" => "ICE FLOORS", "dark" => "BLACKOUT",
            "fog" => "FOG OF WAR", "conveyor" => "CONVEYORS", "collapse" => "COLLAPSING FLOOR", "vents" => "EXPLOSIVE VENTS", "turrets" => "SENTRY TURRETS",
            _ => id.ToUpper(),
        };
        public static string Names(string[] ids) => ids == null || ids.Length == 0 ? "NONE" : string.Join(" + ", System.Array.ConvertAll(ids, Name));
        public static float KnockMul { get { float m = 1; foreach (var h in live) m *= h.KnockMul; return m; } }
        public static string[] Ids = { "acid", "toxicrain", "lowgrav", "ice", "dark", "fog", "conveyor", "collapse", "vents", "turrets" };
        public static Vector2 Push { get { var v = Vector2.zero; foreach (var h in live) v += h.Push; return v; } }
    }

    // a zone's look: palette tints over the level materials, light, fog, ambient and a particle layer that follows
    // the camera (snow / spores / ash / rain / embers)
    public static class Biome
    {
        static ParticleSystem ps; static Particles current = Particles.None; static Material originalParticleMaterial;

        public static void Apply(SectorDef s, Light sun, Camera cam, IEnumerable<Material> walls, IEnumerable<Material> floors, float camDist = 40f)
        {
            if (s == null) return;
            if (sun != null) { sun.color = s.sun; sun.intensity = s.sunIntensity; }
            RenderSettings.ambientSkyColor = s.ambient * 1.1f; RenderSettings.ambientEquatorColor = s.ambient; RenderSettings.ambientGroundColor = s.ambient * 0.75f;
            // the specs give fog from the hero; the camera sits ~camDist away, so shift it out by that
            RenderSettings.fogColor = s.fog; RenderSettings.fogStartDistance = s.fogStart + camDist; RenderSettings.fogEndDistance = s.fogEnd + camDist * 1.2f;
            if (cam != null) cam.backgroundColor = s.fog;
            foreach (var m in walls) if (m != null && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * s.wallTint);
            foreach (var m in floors) if (m != null && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * s.floorTint);
            SetParticles(s.particles, cam, s.accent);
        }

        public static void SetParticles(Particles p, Camera cam, Color accent)
        {
            if (ps == null && p != Particles.None && cam != null)
            {
                var go = new GameObject("BiomeParticles");
                ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = originalParticleMaterial = Mats.Particles(true); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60, 1, 44);
                var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 600; m.playOnAwake = false;
            }
            if (ps == null) return;
            current = p;
            if (p == Particles.None) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.gameObject.SetActive(false); return; }
            ps.gameObject.SetActive(true);
            var main = ps.main; var em = ps.emission; var vel = ps.velocityOverLifetime; vel.enabled = true; var noise = ps.noise;
            noise.enabled = p == Particles.Spores || p == Particles.Snow || p == Particles.Ash; noise.strength = 0.6f; noise.frequency = 0.25f;
            var r2 = ps.GetComponent<ParticleSystemRenderer>();
            r2.sharedMaterial = originalParticleMaterial; main.maxParticles = 600; var fade = ps.colorOverLifetime; fade.enabled = false;
            r2.renderMode = p == Particles.Rain ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r2.velocityScale = p == Particles.Rain ? 0.06f : 0; r2.lengthScale = p == Particles.Rain ? 2.5f : 1;
            switch (p)
            {
                case Particles.Snow:   main.startColor = new Color(1, 1, 1, 0.85f); main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f); main.startLifetime = 6; em.rateOverTime = 70; vel.y = new ParticleSystem.MinMaxCurve(-2.2f, -1.4f); vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); break;
                case Particles.Ash:    main.startColor = new Color(0.55f, 0.5f, 0.45f, 0.7f); main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f); main.startLifetime = 7; em.rateOverTime = 60; vel.y = new ParticleSystem.MinMaxCurve(-1f, -0.5f); vel.x = new ParticleSystem.MinMaxCurve(1.5f, 3f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); break;
                case Particles.Rain:   main.startColor = new Color(0.7f, 0.85f, 1f, 0.55f); main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f); main.startLifetime = 1.4f; em.rateOverTime = 260; vel.y = new ParticleSystem.MinMaxCurve(-22f, -18f); vel.x = new ParticleSystem.MinMaxCurve(1f, 2f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); break;
                case Particles.Spores: main.startColor = Color.Lerp(accent, Color.white, 0.3f) * new Color(1, 1, 1, 0.75f); main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f); main.startLifetime = 8; em.rateOverTime = 40; vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.6f); vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f); vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f); break;
                case Particles.Embers: main.startColor = new Color(1f, 0.55f, 0.15f, 0.9f); main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f); main.startLifetime = 4; em.rateOverTime = 50; vel.y = new ParticleSystem.MinMaxCurve(1f, 2.6f); vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f); vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f); break;
            }
            if (p == Particles.Snow && FirstCryoFx.Active) FirstCryoFx.Snow(ps);
            ps.Play();
        }

        // keep the particle box over the camera's ground target
        public static void Follow(Vector3 groundTarget)
        {
            if (ps == null || current == Particles.None) return;
            ps.transform.position = groundTarget + Vector3.up * (current == Particles.Spores || current == Particles.Embers ? 0.2f : 14f);
        }

        public static void Clear() { if (ps != null) SetParticles(Particles.None, null, Color.white); }
    }
}
