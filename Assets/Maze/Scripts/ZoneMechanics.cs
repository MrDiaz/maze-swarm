using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // The zone toolkit: mechanics a SectorDef switches on by id (Hazards.Make). Each lives for one floor, places
    // itself on the floor's walkable cells (layouts are shared, so placement is procedural), draws with flat glow
    // discs / quads, and hurts the hero by a share of max HP (so the same zone reads the same at any power).
    // Enemies get hurt too where it makes sense: lure the swarm into acid, vents and collapses.
    public abstract class ZoneHazard : Hazard
    {
        protected Transform root;
        static Mesh disc;
        public static Mesh Disc => disc != null ? disc : disc = Fx.RingMesh(0f, 1f, 40);

        public override void Begin(Game game)
        {
            base.Begin(game);
            root = new GameObject("Hazard_" + GetType().Name).transform;
        }
        public override void End() { if (root != null) Object.Destroy(root.gameObject); }

        protected (Transform tr, Material m) Flat(Mesh mesh, Color c, Vector2 at, float radius, float h = 0.05f)
        {
            var go = new GameObject("Flat"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); var m = Mats.Glow(c); mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            go.transform.position = MazeMap.ToWorld(at, h); go.transform.localScale = Vector3.one * radius;
            return (go.transform, m);
        }

        protected Transform Box(Color c, Vector2 at, Vector3 size, float h)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false); go.transform.position = MazeMap.ToWorld(at, h); go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mats.Lit(c, null, null, 0.4f, 0.3f);
            return go.transform;
        }

        // walkable cells at least `away` m from the hero's spawn (shuffled)
        protected static List<Vector2> Spots(int n, float away, float spacing)
        {
            var all = new List<Vector2>();
            for (int z = 1; z < MazeMap.H - 1; z++) for (int x = 1; x < MazeMap.W - 1; x++)
            {
                if (!MazeMap.Walkable(x, z) || MazeMap.grid[z, x] == 'E') continue;
                var c = MazeMap.Center(x, z); if ((c - MazeMap.playerSpawn).magnitude < away) continue;
                all.Add(c);
            }
            for (int i = all.Count - 1; i > 0; i--) { int k = Random.Range(0, i + 1); (all[i], all[k]) = (all[k], all[i]); }
            var got = new List<Vector2>();
            foreach (var c in all) { if (got.Count >= n) break; bool ok = true; foreach (var o in got) if ((o - c).magnitude < spacing) { ok = false; break; } if (ok) got.Add(c); }
            return got;
        }

        protected bool Sim => Game.SimRun;
        protected float HeroHurt(float frac) { float d = g.player.maxHp * frac; g.HurtPlayer(d); return d; }
        protected void HurtEnemies(Vector2 at, float r, float frac, float flat)
        {
            foreach (var e in g.swarm.enemies)
            {
                if (e.dead || (e.p - at).sqrMagnitude > (r + e.r) * (r + e.r)) continue;
                e.hp -= e.boss ? flat * 0.25f : Mathf.Max(flat, e.maxHp * frac); e.flash = 1;
                if (e.hp <= 0) g.KillEnemy(e);
            }
        }
    }

    // ---- ACID POOLS: glowing pools that eat the hero (and anything else) standing in them, and slow you down
    public class AcidPools : ZoneHazard
    {
        protected readonly List<(Vector2 p, float r, VfxPool v)> pools = new();
        float tick; bool inside;
        protected virtual Color Tint => new Color(0.55f, 1f, 0.2f);
        public override void Begin(Game game)
        {
            base.Begin(game);
            foreach (var s in Spots(6, 9f, 7f)) AddPool(s, Random.Range(1.6f, 2.4f));
        }
        protected void AddPool(Vector2 at, float r)
        {
            var v = VfxKit.I != null ? VfxKit.I.Pool(root, at, r, VfxKit.AcidPool, 0.04f) : null;   // real acid (was a flat glowing disc)
            pools.Add((at, r, v));
        }
        public override void Tick(float dt)
        {
            inside = false;
            foreach (var pl in pools) if ((g.player.p - pl.p).magnitude < pl.r) { inside = true; break; }
            if ((tick -= dt) <= 0)
            {
                tick = 0.25f;
                if (inside) HeroHurt(0.02f);
                foreach (var pl in pools) HurtEnemies(pl.p, pl.r, 0.06f, 6f);
            }
            foreach (var pl in pools) pl.v?.Ambient(dt);   // bubbles and fumes
        }
        public override float MoveMul => inside ? 0.7f : 1f;
        public override void End() { foreach (var pl in pools) pl.v?.Kill(); pools.Clear(); base.End(); }
    }

    // ---- TOXIC RAIN: burning drops fall around the hero (a ring marks each), leaving a short-lived acid puddle
    public class ToxicRain : ZoneHazard
    {
        readonly List<(Vector2 p, float t, Transform ring, Material m, VfxPool v)> drops = new();
        float next = 3f, tick; bool inside;
        static readonly Color C = new Color(0.75f, 1f, 0.25f);
        public override void Tick(float dt)
        {
            if ((next -= dt) <= 0)
            {
                next = Random.Range(1.2f, 2f);
                var at = g.player.p + Random.insideUnitCircle * 5f + g.player.vel * 0.8f;
                if (MazeMap.CellIndex(at) >= 0 && MazeMap.Walkable(Mathf.FloorToInt(at.x / MazeMap.C), Mathf.FloorToInt(at.y / MazeMap.C)))
                {
                    var (tr, m) = Flat(Fx.Ring, C, at, 0.4f, 0.06f); drops.Add((at, 0, tr, m, null));
                }
            }
            inside = false; bool hurt = (tick -= dt) <= 0; if (hurt) tick = 0.25f;
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i]; d.t += dt; drops[i] = d;
                const float warn = 1.3f, life = 4.5f, R = 2.1f;
                if (d.t < warn) { d.ring.localScale = Vector3.one * Mathf.Lerp(0.4f, R, d.t / warn); d.m.SetColor("_BaseColor", C * new Color(1, 1, 1, 0.5f + 0.5f * Mathf.PingPong(d.t * 6, 1))); continue; }
                if (d.t - dt < warn)
                {   // the splash: a hit, then the puddle
                    d.ring.gameObject.SetActive(false);   // the warning ring gives way to the acid itself
                    if (VfxKit.I != null) { d.v = VfxKit.I.Pool(root, d.p, R, VfxKit.Acid, 0.045f); d.v.SetLife(0); if (!Sim) VfxKit.I.Splash(MazeMap.ToWorld(d.p, 0f), VfxKit.AcidSplash, R); }
                    drops[i] = d;
                    if ((g.player.p - d.p).magnitude < R) HeroHurt(0.08f);
                    HurtEnemies(d.p, R, 0.3f, 20f);
                }
                bool inPuddle = (g.player.p - d.p).magnitude < R; inside |= inPuddle;
                if (hurt && inPuddle) HeroHurt(0.012f);
                if (d.v != null) { d.v.SetLife(Mathf.Pow(Mathf.Min(Mathf.Clamp01((d.t - warn) / 0.35f), Mathf.Clamp01((life - d.t) / 1.5f)), 0.6f)); if (life - d.t > 0.8f) d.v.Ambient(dt); }
                else d.m.SetColor("_BaseColor", C * new Color(1, 1, 1, 0.45f * (1 - (d.t - warn) / (life - warn))));
                if (d.t >= life) { d.v?.Kill(); Object.Destroy(d.ring.gameObject); drops.RemoveAt(i); }
            }
        }
        public override float MoveMul => inside ? 0.8f : 1f;
    }

    // ---- LOW GRAVITY: floaty footing (the hero drifts, dashes carry) and everything flies further when hit
    public class LowGravity : ZoneHazard
    {
        public override float MoveMul => 1.12f;
        public override float Friction => 0.35f;
        public override float KnockMul => 2.4f;
    }

    // ---- ICE: slick floors, the hero slides and overshoots
    public class Ice : ZoneHazard
    {
        public override float MoveMul => 1.15f;
        public override float Friction => 0.16f;
    }

    // ---- DARKNESS: the lights die; a flashlight cone follows your aim
    public class Darkness : ZoneHazard
    {
        Light spot, glow; float sunI; Color amb0, amb1, amb2;
        public override void Begin(Game game)
        {
            base.Begin(game);
            var sun = RenderSettings.sun; sunI = sun != null ? sun.intensity : 1; if (sun != null) sun.intensity = sunI * 0.08f;
            amb0 = RenderSettings.ambientSkyColor; amb1 = RenderSettings.ambientEquatorColor; amb2 = RenderSettings.ambientGroundColor;
            RenderSettings.ambientSkyColor = amb0 * 0.16f; RenderSettings.ambientEquatorColor = amb1 * 0.16f; RenderSettings.ambientGroundColor = amb2 * 0.16f;
            var s = new GameObject("Flashlight"); s.transform.SetParent(root, false);
            spot = s.AddComponent<Light>(); spot.type = LightType.Spot; spot.range = 26; spot.spotAngle = 62; spot.innerSpotAngle = 30; spot.intensity = 60; spot.color = new Color(1f, 0.95f, 0.85f); spot.shadows = LightShadows.None;
            var p = new GameObject("HeroGlow"); p.transform.SetParent(root, false);
            glow = p.AddComponent<Light>(); glow.type = LightType.Point; glow.range = 7; glow.intensity = 6; glow.color = new Color(0.7f, 0.85f, 1f); glow.shadows = LightShadows.None;
        }
        public override void Tick(float dt)
        {
            var a = g.player.aim.sqrMagnitude > 0.01f ? g.player.aim.normalized : Vector2.up;
            spot.transform.position = MazeMap.ToWorld(g.player.p - a * 1.5f, 3.2f);
            spot.transform.rotation = Quaternion.LookRotation(MazeMap.ToWorld(g.player.p + a * 9f, 0) - spot.transform.position);
            glow.transform.position = MazeMap.ToWorld(g.player.p, 2.2f);
        }
        public override void End()
        {
            var sun = RenderSettings.sun; if (sun != null) sun.intensity = sunI;
            RenderSettings.ambientSkyColor = amb0; RenderSettings.ambientEquatorColor = amb1; RenderSettings.ambientGroundColor = amb2;
            base.End();
        }
    }

    // ---- FOG OF WAR: a thick bank closes in; you see a few metres and hear the rest
    public class FogOfWar : ZoneHazard
    {
        float s0, e0; Color c0, bg0;
        public override void Begin(Game game)
        {
            base.Begin(game);
            s0 = RenderSettings.fogStartDistance; e0 = RenderSettings.fogEndDistance; c0 = RenderSettings.fogColor; var cam = Camera.main; bg0 = cam != null ? cam.backgroundColor : c0;
            var c = Color.Lerp(c0, new Color(0.25f, 0.27f, 0.3f), 0.45f);
            RenderSettings.fogColor = c; if (cam != null) cam.backgroundColor = c;
        }
        public override void Tick(float dt)
        {
            var cam = Camera.main; if (cam == null) return;
            float d = (cam.transform.position - MazeMap.ToWorld(g.player.p)).magnitude;   // fog measured from the camera
            RenderSettings.fogStartDistance = d - 2f; RenderSettings.fogEndDistance = d + 12f;
        }
        public override void End()
        {
            RenderSettings.fogStartDistance = s0; RenderSettings.fogEndDistance = e0; RenderSettings.fogColor = c0;
            var cam = Camera.main; if (cam != null) cam.backgroundColor = bg0;
            base.End();
        }
    }

    // ---- CONVEYORS: long belts across the floor carry whatever stands on them
    public class Conveyors : ZoneHazard
    {
        readonly List<(RectInt cells, Vector2 dir, Material m)> belts = new();
        Vector2 push; static Texture2D chevrons;
        const float Speed = 4.5f;
        public override void Begin(Game game)
        {
            base.Begin(game);
            if (chevrons == null)
            {
                chevrons = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    int v = (y + Mathf.Abs(x - 16)) % 16; bool on = v < 6;
                    chevrons.SetPixel(x, y, on ? new Color(1f, 0.8f, 0.2f, 1) : new Color(0.12f, 0.13f, 0.15f, 1));
                }
                chevrons.Apply();
            }
            // straight runs of walkable cells (rows or columns), at least 7 long, away from the spawn
            var runs = new List<(RectInt r, Vector2 dir)>();
            for (int z = 2; z < MazeMap.H - 2; z += 3) Scan(true, z, runs);
            for (int x = 2; x < MazeMap.W - 2; x += 3) Scan(false, x, runs);
            for (int i = runs.Count - 1; i > 0; i--) { int k = Random.Range(0, i + 1); (runs[i], runs[k]) = (runs[k], runs[i]); }
            foreach (var (r, dir) in runs)
            {
                if (belts.Count >= 3) break;
                bool clash = false; foreach (var b in belts) if (b.cells.Overlaps(new RectInt(r.x - 2, r.y - 2, r.width + 4, r.height + 4))) clash = true;
                var center = new Vector2((r.x + r.width * 0.5f) * MazeMap.C, (r.y + r.height * 0.5f) * MazeMap.C);
                if (clash || (center - MazeMap.playerSpawn).magnitude < 5f) continue;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>()); q.transform.SetParent(root, false);
                q.transform.position = MazeMap.ToWorld(center, 0.03f);
                q.transform.rotation = Quaternion.Euler(90, MazeMap.YawDeg(Mathf.Atan2(dir.x, dir.y)), 0);
                float len = (dir.x != 0 ? r.width : r.height) * MazeMap.C, wid = (dir.x != 0 ? r.height : r.width) * MazeMap.C;
                q.transform.localScale = new Vector3(wid, len, 1);
                var m = Mats.Unlit(Color.white, chevrons); m.SetTextureScale("_BaseMap", new Vector2(1, len / wid));
                var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                belts.Add((r, dir, m));
            }
        }
        void Scan(bool row, int line, List<(RectInt, Vector2)> runs)
        {
            int n = row ? MazeMap.W : MazeMap.H, start = -1;
            for (int i = 0; i <= n; i++)
            {
                int x = row ? i : line, z = row ? line : i;
                bool ok = i < n && MazeMap.Walkable(x, z) && MazeMap.grid[z, x] != 'E';
                if (ok && start < 0) start = i;
                if (!ok && start >= 0)
                {
                    int len = i - start;
                    if (len >= 7)
                    {
                        len = Mathf.Min(len, 14); var dir = row ? new Vector2(Random.value < 0.5f ? 1 : -1, 0) : new Vector2(0, Random.value < 0.5f ? 1 : -1);
                        runs.Add((row ? new RectInt(start, line, len, 1) : new RectInt(line, start, 1, len), dir));
                    }
                    start = -1;
                }
            }
        }
        public override void Tick(float dt)
        {
            push = Vector2.zero;
            foreach (var b in belts)
            {
                var off = b.m.GetTextureOffset("_BaseMap"); off.y -= dt * Speed / (MazeMap.C); b.m.SetTextureOffset("_BaseMap", off);
                if (On(b.cells, g.player.p)) push += b.dir * Speed;
                foreach (var e in g.swarm.enemies) if (!e.dead && e.speed > 0 && On(b.cells, e.p)) e.p += b.dir * Speed * dt;
            }
        }
        static bool On(RectInt r, Vector2 p) { int x = Mathf.FloorToInt(p.x / MazeMap.C), z = Mathf.FloorToInt(p.y / MazeMap.C); return x >= r.xMin && x < r.xMax && z >= r.yMin && z < r.yMax; }
        public override Vector2 Push => push;
    }

    // ---- COLLAPSING FLOOR: plates crack under the fight (a red ring spreads) and drop, hurting all on them
    public class Collapse : ZoneHazard
    {
        readonly List<(Vector2 p, float t, Transform tr, Material m)> cracks = new();
        float next = 4f;
        const float Warn = 1.6f, Hole = 5f, R = 1.9f;
        public override void Tick(float dt)
        {
            if ((next -= dt) <= 0)
            {
                next = Random.Range(1.8f, 2.8f);
                var at = g.player.p + g.player.vel * 0.6f + Random.insideUnitCircle * 4.5f;
                int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C);
                if (MazeMap.Walkable(x, z)) { var c = MazeMap.Center(x, z); var (tr, m) = Flat(Fx.Ring, new Color(1f, 0.3f, 0.2f), c, 0.3f, 0.06f); cracks.Add((c, 0, tr, m)); }
            }
            for (int i = cracks.Count - 1; i >= 0; i--)
            {
                var c = cracks[i]; c.t += dt; cracks[i] = c;
                if (c.t < Warn) { c.tr.localScale = Vector3.one * Mathf.Lerp(0.3f, R, c.t / Warn); c.m.SetColor("_BaseColor", new Color(1f, 0.3f, 0.2f, 0.5f + 0.5f * Mathf.PingPong(c.t * 8, 1))); continue; }
                if (c.t - dt < Warn)
                {
                    c.tr.GetComponent<MeshFilter>().sharedMesh = Disc; c.tr.localScale = Vector3.one * R;
                    if ((g.player.p - c.p).magnitude < R) HeroHurt(0.14f);
                    HurtEnemies(c.p, R, 0.8f, 60f);
                    if (!Sim) { Fx.I.Debris(MazeMap.ToWorld(c.p, 0.2f), 16, new Color(0.3f, 0.3f, 0.32f), 5, 0.9f, 0.18f, 6); g.AddShake(0.2f); }
                }
                c.m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f, 0.9f * Mathf.Clamp01((Warn + Hole - c.t) / 0.6f)));
                if (c.t >= Warn + Hole) { Object.Destroy(c.tr.gameObject); cracks.RemoveAt(i); }
            }
        }
    }

    // ---- EXPLOSIVE VENTS: grates that vent steam, glow, then blow; lure the swarm over them
    public class Vents : ZoneHazard
    {
        class Vent { public Vector2 p; public float t; public Transform ring; public Material m; }
        readonly List<Vent> vents = new();
        const float Warn = 1.4f, R = 2.6f;
        public override void Begin(Game game)
        {
            base.Begin(game);
            foreach (var s in Spots(5, 7f, 8f))
            {
                Box(new Color(0.16f, 0.17f, 0.19f), s, new Vector3(1.6f, 0.12f, 1.6f), 0.06f);
                var (tr, m) = Flat(Fx.Ring, new Color(1f, 0.55f, 0.15f, 0.3f), s, R, 0.07f);
                vents.Add(new Vent { p = s, t = Random.Range(2f, 7f), ring = tr, m = m });
            }
        }
        public override void Tick(float dt)
        {
            foreach (var v in vents)
            {
                v.t -= dt;
                float a = v.t < Warn ? 0.5f + 0.5f * Mathf.PingPong(v.t * 8, 1) : 0.18f;
                v.m.SetColor("_BaseColor", new Color(1f, 0.55f, 0.15f, a));
                if (v.t < Warn && !Sim && Random.value < dt * 12) Fx.I.Smoke(MazeMap.ToWorld(v.p, 0.3f), 1, new Color(0.9f, 0.85f, 0.8f), 1.5f, 0.8f, 0.4f, 3);
                if (v.t > 0) continue;
                v.t = Random.Range(5f, 8f);
                if ((g.player.p - v.p).magnitude < R) HeroHurt(0.16f);
                HurtEnemies(v.p, R, 0.7f, 80f);
                if (!Sim) { Fx.I.Boom(MazeMap.ToWorld(v.p, 0.3f), R); if ((g.player.p - v.p).magnitude < 14) g.AddShake(0.25f); }
            }
        }
    }

    // ---- TURRETS: old defence sentries; they paint a laser line, then fire down it (dash or sidestep)
    public class Turrets : ZoneHazard
    {
        class Turret { public Vector2 p; public float t, shotT = 9; public Vector2 aim, end; public Transform head, beam; public Material beamM; public VfxBeam paint, shot, scorch; }
        readonly List<Turret> list = new();
        const float Range = 17f, Paint = 0.9f;
        public override void Begin(Game game)
        {
            base.Begin(game);
            foreach (var s in Spots(3, 12f, 12f))
            {
                Box(new Color(0.22f, 0.23f, 0.26f), s, new Vector3(1.2f, 1.0f, 1.2f), 0.5f);
                var head = Box(new Color(0.85f, 0.25f, 0.2f), s, new Vector3(0.5f, 0.4f, 1.3f), 1.2f);
                var bq = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(bq.GetComponent<Collider>()); bq.transform.SetParent(root, false);
                var bm = Mats.Glow(new Color(1f, 0.2f, 0.15f, 0.8f)); var br = bq.GetComponent<MeshRenderer>(); br.sharedMaterial = bm; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bq.SetActive(false);
                var tu = new Turret { p = s, t = Random.Range(2f, 4f), head = head, beam = bq.transform, beamM = bm };
                if (VfxKit.I != null)
                {   // a thin flickering aim line, a heavy shot beam and a glowing scorch line on the floor
                    tu.paint = VfxKit.I.Beam(root, new Color(1f, 0.18f, 0.12f), 0.12f, 0.25f, 3f);
                    tu.shot = VfxKit.I.Beam(root, new Color(1f, 0.3f, 0.12f), 0.9f, 0.16f, 4f);
                    tu.scorch = VfxKit.I.Beam(root, new Color(1f, 0.35f, 0.1f), 0.5f, 0.08f, 0.3f, true);
                    tu.paint.Visible = tu.shot.Visible = tu.scorch.Visible = false;
                }
                list.Add(tu);
            }
            // a sentry is part of the layout: keep the hero out of it
        }
        // where a shot down `aim` stops: the first wall, or full range
        Vector2 Reach(Vector2 from, Vector2 aim)
        {
            for (float s = 1f; s < Range; s += 0.25f) { var q = from + aim * s; if (MazeMap.ShotSolidAt(q.x, q.y)) return from + aim * (s - 0.2f); }
            return from + aim * Range;
        }
        // the shot beam narrows and fades in 0.35 s; the scorch line glows on for 1.4 s
        void TickBeams(Turret t, float dt)
        {
            if (t.shot == null) return;
            t.shotT += dt;
            float fk = 1 - t.shotT / 0.35f, sk = 1 - t.shotT / 1.4f;
            t.shot.Visible = fk > 0; if (fk > 0) { t.shot.Intensity = 0.4f + 1.4f * fk; t.shot.Width = 0.35f + 0.75f * fk; }
            t.scorch.Intensity = sk > 0 ? sk * 0.55f : 0;
        }

        bool Sees(Vector2 a, Vector2 b)
        {
            var d = b - a; float L = d.magnitude; if (L > Range) return false;
            for (float s = 1f; s < L; s += 0.5f) { var q = a + d * (s / L); if (MazeMap.ShotSolidAt(q.x, q.y)) return false; }
            return true;
        }
        public override void Tick(float dt)
        {
            foreach (var t in list)
            {
                if ((g.player.p - t.p).magnitude < 1.4f) { var away = (g.player.p - t.p).normalized; g.player.p = t.p + away * 1.4f; }
                t.t -= dt;
                bool sees = Sees(t.p, g.player.p);
                TickBeams(t, dt);
                if (t.t > Paint)
                {   // tracking
                    if (sees) t.aim = (g.player.p - t.p).normalized; else if (t.t < Paint + 0.1f) t.t = Paint + 0.5f; // wait for a line
                    t.beam.gameObject.SetActive(false); if (t.paint != null) t.paint.Visible = false;
                }
                else
                {   // painting: the beam locks where you were
                    var end = t.p + t.aim * Range; var mid = (t.p + end) * 0.5f;
                    if (t.paint != null)
                    {
                        t.end = Reach(t.p, t.aim);
                        t.paint.Set(MazeMap.ToWorld(t.p + t.aim * 0.7f, 1.2f), MazeMap.ToWorld(t.end, 1.2f));
                        t.paint.Intensity = Random.value < 0.7f ? 1f : 0.35f;   // flicker
                    }
                    else
                    {
                        t.beam.gameObject.SetActive(true);
                        t.beam.position = MazeMap.ToWorld(mid, 0.1f); t.beam.rotation = Quaternion.Euler(90, MazeMap.YawDeg(Mathf.Atan2(t.aim.x, t.aim.y)), 0);
                        t.beam.localScale = new Vector3(t.t > 0 ? 0.12f : 0.9f, Range, 1);
                        t.beamM.SetColor("_BaseColor", new Color(1f, 0.2f, 0.15f, 0.35f + 0.6f * Mathf.PingPong(t.t * 10, 1)));
                    }
                    if (t.t <= 0)
                    {
                        var rel = g.player.p - t.p; float along = Vector2.Dot(rel, t.aim), side = Mathf.Abs(rel.x * t.aim.y - rel.y * t.aim.x);
                        if (along > 0 && along < Range && side < 0.9f && Sees(t.p, g.player.p)) HeroHurt(0.13f);
                        foreach (var e in g.swarm.enemies) { var re = e.p - t.p; float al = Vector2.Dot(re, t.aim); if (!e.dead && al > 0 && al < Range && Mathf.Abs(re.x * t.aim.y - re.y * t.aim.x) < 0.9f + e.r) { e.hp -= e.boss ? 40 : e.maxHp * 0.5f; e.flash = 1; if (e.hp <= 0) g.KillEnemy(e); } }
                        if (!Sim && VfxKit.I != null)
                        {   // the shot: heavy beam, muzzle and impact flashes, sparks and smoke where it lands
                            t.shotT = 0; t.paint.Visible = false;
                            var a = MazeMap.ToWorld(t.p + t.aim * 0.7f, 1.2f); var b = MazeMap.ToWorld(t.end, 1.2f);
                            t.shot.Set(a, b); t.scorch.Set(MazeMap.ToWorld(t.p + t.aim * 0.7f, 0.04f), MazeMap.ToWorld(t.end, 0.04f));
                            VfxKit.I.Sparks(b, 26, new Color(1f, 0.6f, 0.3f), 5f, 0.4f, 0.3f);
                            VfxKit.I.Flash(b, 3.8f, new Color(1f, 0.45f, 0.2f, 0.9f)); VfxKit.I.Flash(a, 2.4f, new Color(1f, 0.35f, 0.2f, 0.9f), 0.3f);
                            for (int k = 0; k < 3; k++) VfxKit.I.Puff(b, new Vector3(Random.Range(-0.5f, 0.2f), Random.Range(0.6f, 1.2f), Random.Range(-0.5f, 0.2f)), 1.6f, 1.4f, new Color(0.35f, 0.33f, 0.33f, 0.4f));
                        }
                        else if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(t.p + t.aim, 1.2f), 8, new Color(1f, 0.4f, 0.2f), 6, 0.3f, 0.08f, 2);
                        t.t = Random.Range(3.2f, 4.5f); t.beam.gameObject.SetActive(false);
                    }
                }
                t.head.rotation = Quaternion.Euler(0, MazeMap.YawDeg(Mathf.Atan2(t.aim.x, t.aim.y)), 0);
            }
        }
    }
}
