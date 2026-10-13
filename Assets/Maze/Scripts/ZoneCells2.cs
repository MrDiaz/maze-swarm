using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // More of the toolkit: GRATE + BREAKER, JUMP_PAD, COLLAPSE, HIVE_WALL, placed SPORE_PODs, DARKNESS (light level,
    // hero light, pylons, flare caches), and the global WEATHER systems (WIND, DRIFT, ACID_RAIN) and FLOOD.
    public static class ZoneCells2
    {
        public static IEnumerable<Hazard> ForLayout(Game g)
        {
            if (MazeMap.Marks('w').Count > 0) yield return new CellWater();
            if (MazeMap.Marks('g').Count > 0) yield return new CellGrates();
            if (MazeMap.Marks('J').Count > 1) yield return new CellJumpPads();
            if (MazeMap.Marks('k').Count > 0) yield return new CellCollapse();
            if (MazeMap.Marks('m').Count > 0) yield return new CellHiveWalls();
            if (MazeMap.Marks('%').Count > 0) yield return new CellSporePods();
            if (MazeMap.Marks('K').Count > 0) yield return new CellBreakers();
            yield return new CellLight();
            yield return new Weather();
        }
        public static T Find<T>() where T : Hazard { foreach (var h in Hazards.Live) if (h is T t) return t; return null; }
    }

    // GRATE: 4 s off, 0.8 s warning, 1.6 s on. On: hero 15 HP + 0.5 s stun on contact, enemies 6 RH/s. Water touching
    // a grate carries the current. A BREAKER kills them for good.
    public class CellGrates : ZoneHazard
    {
        readonly HashSet<int> live = new(); Material m; float t, tick; bool hitThisPulse; readonly List<Vector3> pts = new();
        public string Mode = "cycle"; public bool Dead;
        public float OnSec = 1.6f, OffSec = 4f, WarnSec = 0.8f;
        public override void Begin(Game game)
        {
            base.Begin(game);
            var cells = new List<Vector2Int>(MazeMap.Marks('g'));
            // the current spreads through connected water
            var seen = new HashSet<Vector2Int>(cells); var st = new Stack<Vector2Int>(cells);
            while (st.Count > 0) { var c = st.Pop(); foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }) { var n = c + d; if (n.x < 0 || n.y < 0 || n.x >= MazeMap.W || n.y >= MazeMap.H || seen.Contains(n) || MazeMap.tag[n.y, n.x] != 'w') continue; seen.Add(n); st.Push(n); } }
            foreach (var c in seen) { live.Add(c.y * MazeMap.W + c.x); pts.Add(MazeMap.ToWorld(MazeMap.Center(c.x, c.y), 0.1f)); }
            var go = new GameObject("GrateGlow"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = ZoneCells.Quads(new List<Vector2Int>(seen), 0.04f);
            var mr = go.AddComponent<MeshRenderer>(); m = Mats.Glow(new Color(0.37f, 0.85f, 1f, 0f)); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        public bool On { get; private set; }
        public override void Tick(float dt)
        {
            t += dt; float cyc = OffSec + WarnSec + OnSec, ph = t % cyc;
            bool warn = !Dead && Mode == "cycle" && ph >= OffSec && ph < OffSec + WarnSec;
            On = !Dead && (Mode == "on" || (Mode == "cycle" && ph >= OffSec + WarnSec));
            m.SetColor("_BaseColor", new Color(0.37f, 0.85f, 1f, On ? 0.75f : warn ? (Mathf.PingPong(t * 12, 1) > 0.5f ? 0.35f : 0.05f) : 0f));
            if (!On) { hitThisPulse = false; return; }
            if (pts.Count > 0) HazardAudio.Loop("amb_electric_grate", this, HazardAudio.Nearest(pts, pts[0]));
            int hc = MazeMap.CellIndex(g.player.p);
            if (!hitThisPulse && hc >= 0 && live.Contains(hc) && g.player.dashT <= 0) { hitThisPulse = true; g.HurtPlayer(ZoneCells.Hp(g, 15)); g.player.frozenT = Mathf.Max(g.player.frozenT, 0.5f); }
            if ((tick -= dt) <= 0)
            {
                tick = 0.25f; float d = ZoneCells.RH(g) * 6f * 0.25f;
                foreach (var e in g.swarm.enemies)
                {
                    if (e.dead || e.type.nest) continue; int c = MazeMap.CellIndex(e.p);
                    if (c >= 0 && live.Contains(c)) { e.hp -= d; e.flash = 1; if (e.hp <= 0) g.KillEnemy(e); else if (!Sim && Random.value < 0.3f) Fx.I.Sparks(MazeMap.ToWorld(e.p, 0.4f), 2, new Color(0.4f, 0.85f, 1f), 3, 0.2f, 0.05f, 2); }
                }
            }
        }
    }

    // BREAKER: a 5 RH box (a low-priority target, like a nest); shot down, it switches off its system for good:
    // the grates, else the laser fences
    public class CellBreakers : ZoneHazard
    {
        readonly List<Enemy> boxes = new();
        public override void Begin(Game game)
        {
            base.Begin(game);
            int nt = g.swarm.NestType; if (nt < 0) return;
            foreach (var c in MazeMap.Marks('K'))
            {
                var p = MazeMap.Center(c.x, c.y); var e = g.swarm.Spawn(p, g.wave, nt); e.p = p; e.egg = true; e.spawnCd = 1e9f; e.breaker = true;
                e.hp = e.maxHp = ZoneCells.RH(g) * 5f; e.scale *= 0.45f; e.r *= 0.5f; boxes.Add(e);
            }
        }
        public override void Tick(float dt)
        {
            foreach (var b in boxes)
            {
                if (!b.dead || b.egg == false) continue;
                b.egg = false;   // once
                var gr = ZoneCells2.Find<CellGrates>(); var fe = ZoneCells.FindFences();
                if (gr != null) { gr.Dead = true; g.hud.ShowBanner("BREAKER DOWN  //  GRATES OFF"); }
                else if (fe != null) { fe.Mode = "off"; g.hud.ShowBanner("BREAKER DOWN  //  FENCES OFF"); }
                g.events.Done("breaker");
            }
        }
    }

    // JUMP_PAD: pads pair in reading order (1st-2nd, 3rd-4th). Step on one: a 0.8 s arc to its partner, untouchable in
    // flight; landing shoves everything within 2 m out 3 m. 2 s pad cooldown.
    public class CellJumpPads : ZoneHazard
    {
        readonly List<Vector2> pads = new(); float cd, flyT = -1; Vector2 from, to;
        public override void Begin(Game game) { base.Begin(game); foreach (var c in MazeMap.Marks('J')) pads.Add(MazeMap.Center(c.x, c.y)); }
        public override void Tick(float dt)
        {
            cd -= dt;
            if (flyT >= 0)
            {
                flyT += dt; float k = Mathf.Clamp01(flyT / 0.8f);
                g.player.p = Vector2.Lerp(from, to, k); g.player.vel = Vector2.zero; g.player.dashT = Mathf.Max(g.player.dashT, 0.05f); g.player.frozenT = Mathf.Max(g.player.frozenT, 0.05f);
                if (k >= 1)
                {
                    flyT = -1; cd = 2f;
                    foreach (var e in g.swarm.enemies) { var d = e.p - to; if (!e.dead && !e.type.nest && d.magnitude < 2f) e.k += (d.sqrMagnitude > 0.01f ? d.normalized : Random.insideUnitCircle.normalized) * 9f; }
                    if (!Sim) Fx.I.Smoke(MazeMap.ToWorld(to, 0.1f), 8, new Color(0.9f, 0.85f, 0.6f), 3, 0.5f, 0.3f, 1); g.AddShake(0.15f);
                }
                return;
            }
            if (cd > 0) return;
            for (int i = 0; i + 1 < pads.Count; i += 2)
            {
                if ((g.player.p - pads[i]).magnitude < 1.1f) { Launch(pads[i], pads[i + 1]); return; }
                if ((g.player.p - pads[i + 1]).magnitude < 1.1f) { Launch(pads[i + 1], pads[i]); return; }
            }
        }
        void Launch(Vector2 a, Vector2 b) { from = a; to = b; flyT = 0; Haptics.Light(); }
    }

    // COLLAPSE: a crumbling cell the hero steps on cracks for 1 s, then falls and becomes a pit; enemies on it die
    public class CellCollapse : ZoneHazard
    {
        readonly Dictionary<Vector2Int, float> cracking = new(); readonly Dictionary<Vector2Int, Transform> marks = new();
        public override void Tick(float dt)
        {
            int c = MazeMap.CellIndex(g.player.p);
            if (c >= 0) { var v = new Vector2Int(c % MazeMap.W, c / MazeMap.W); if (MazeMap.tag[v.y, v.x] == 'k' && !cracking.ContainsKey(v)) { cracking[v] = 1f; if (!Sim) Fx.I.Smoke(MazeMap.ToWorld(MazeMap.Center(v.x, v.y), 0.1f), 4, new Color(0.55f, 0.5f, 0.45f), 1, 0.6f, 0.25f, 0.5f); } }
            var done = new List<Vector2Int>();
            foreach (var kv in new List<KeyValuePair<Vector2Int, float>>(cracking))
            {
                float left = kv.Value - dt; cracking[kv.Key] = left;
                if (left <= 0) done.Add(kv.Key);
            }
            foreach (var v in done) { cracking.Remove(v); Fall(v); }
        }
        public void Fall(Vector2Int v)
        {
            if (MazeMap.tag[v.y, v.x] != 'k') return;
            MazeMap.tag[v.y, v.x] = '_'; MazeMap.grid[v.y, v.x] = '_'; MazeMap.flowCell = -1;
            var p = MazeMap.Center(v.x, v.y);
            foreach (var e in g.swarm.enemies) if (!e.dead && !e.boss && (e.p - p).magnitude < MazeMap.C * 0.6f) g.KillEnemy(e);
            if (!Sim)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>()); q.transform.SetParent(root, false);
                q.transform.position = MazeMap.ToWorld(p, 0.05f); q.transform.rotation = Quaternion.Euler(90, 0, 0); q.transform.localScale = Vector3.one * MazeMap.C;
                q.GetComponent<MeshRenderer>().sharedMaterial = Mats.Unlit(new Color(0.02f, 0.02f, 0.03f)); Fx.I.Debris(MazeMap.ToWorld(p, 0.2f), 10, new Color(0.4f, 0.35f, 0.3f), 3, 0.8f, 0.15f, -3);
            }
            // if the hero is on it now, step back to safe ground
            if (MazeMap.CellIndex(g.player.p) == v.y * MazeMap.W + v.x) MazeMap.CollideGrid(ref g.player.p, g.cfg.player.radius);
        }
        public void FallAll() { foreach (var c in new List<Vector2Int>(MazeMap.Marks('k'))) Fall(c); }
    }

    // HIVE_WALL: solid until shot down (10 RH, explosions x3); regrows 12 s later unless the hero stands on it
    public class CellHiveWalls : ZoneHazard
    {
        class W { public Vector2Int c; public float hp, regrow; public Transform tr; }
        readonly Dictionary<int, W> walls = new(); public float RegrowSec = 12f;
        public override void Begin(Game game)
        {
            base.Begin(game); Instance = this;
            var mat = Mats.Lit(new Color(0.24f, 0.09f, 0.3f), null, null, 0.6f);
            foreach (var c in MazeMap.Marks('m'))
            {
                var w = new W { c = c, hp = ZoneCells.RH(g) * 10f };
                if (!Sim)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(b.GetComponent<Collider>()); b.transform.SetParent(root, false);
                    b.transform.position = MazeMap.ToWorld(MazeMap.Center(c.x, c.y), 1.2f); b.transform.localScale = new Vector3(MazeMap.C, 2.4f, MazeMap.C);
                    b.GetComponent<MeshRenderer>().sharedMaterial = mat; w.tr = b.transform;
                }
                walls[c.y * MazeMap.W + c.x] = w;
            }
        }
        public static CellHiveWalls Instance;
        public bool Hit(Vector2 at, float dmg)
        {
            int i = MazeMap.CellIndex(at); if (i < 0 || !walls.TryGetValue(i, out var w) || w.hp <= 0) return false;
            w.hp -= dmg;
            if (w.hp <= 0)
            {
                MazeMap.grid[w.c.y, w.c.x] = '.'; MazeMap.flowCell = -1; w.regrow = RegrowSec;
                if (w.tr != null) w.tr.gameObject.SetActive(false);
                if (!Sim) Fx.I.GooBurst(MazeMap.ToWorld(MazeMap.Center(w.c.x, w.c.y), 1f), 1.4f);
            }
            return true;
        }
        public override void Tick(float dt)
        {
            foreach (var w in walls.Values)
            {
                if (w.hp > 0) { if (w.tr != null) w.tr.localScale = new Vector3(MazeMap.C, 2.4f * (1 + 0.015f * Mathf.Sin(Time.time * 2 + w.c.x)), MazeMap.C); continue; }
                w.regrow -= dt;
                if (w.regrow > 0 || MazeMap.CellIndex(g.player.p) == w.c.y * MazeMap.W + w.c.x) continue;
                w.hp = ZoneCells.RH(g) * 10f; MazeMap.grid[w.c.y, w.c.x] = 'm'; MazeMap.flowCell = -1;
                if (w.tr != null) w.tr.gameObject.SetActive(true);
                foreach (var e in g.swarm.enemies) { var p = MazeMap.Center(w.c.x, w.c.y); if (!e.dead && (e.p - p).magnitude < 1.2f) e.p += (e.p - p).normalized * 1.4f; }
            }
        }
        public override void End() { if (Instance == this) Instance = null; base.End(); }
    }

    // map-placed spore pods: the Spore Pod enemy on each '%'
    public class CellSporePods : ZoneHazard
    {
        public override void Begin(Game game)
        {
            base.Begin(game); int ti = g.swarm.TypeByName("spore_pod"); if (ti < 0) return;
            foreach (var c in MazeMap.Marks('%')) { var p = MazeMap.Center(c.x, c.y); var e = g.swarm.Spawn(p, g.wave, ti); e.p = p; }
        }
    }

    // DARKNESS: the floor's light level comes from the script (LIGHT); below 0.5 the hero carries a 5.5 m light, lit
    // pylons (stand 1.5 s on an I) and flares (walk over an F: a 12 s, 7 m flare drops at your feet) light the rest.
    // Auto-fire only takes enemies inside light; 'd' cells are dark even on lit floors.
    public class CellLight : ZoneHazard
    {
        public static CellLight I; public float Level = 1f;
        Light heroLight; readonly List<(Vector2 p, float t, Light l)> flares = new(); readonly List<(Vector2 p, Light l, bool lit, float hold, Material m)> pylons = new();
        readonly HashSet<Vector2Int> usedCaches = new();
        public override void Begin(Game game)
        {
            base.Begin(game); I = this;
            foreach (var c in MazeMap.Marks('I'))
            {
                var p = MazeMap.Center(c.x, c.y);
                var (_, m) = Flat(Fx.Ring, new Color(0.6f, 0.75f, 1f, 0.5f), p, 1.2f, 0.07f);
                pylons.Add((p, null, false, 0, m));
            }
        }
        public bool Dark => Level < 0.5f;
        public static int Flares = 3;   // FLARE button charges (refilled at caches)
        // throw one 5 m ahead of where you're moving: a 12 s, 7 m light that sticks to an enemy it lands next to
        public void ThrowFlare()
        {
            if (Flares <= 0) return; Flares--;
            var dir = g.player.vel.sqrMagnitude > 0.5f ? g.player.vel.normalized : g.player.aim;
            var at = g.player.p + dir * 5f; MazeMap.CollideGrid(ref at, 0.3f);
            flares.Add((at, 12f, MakeLight(at, new Color(1f, 0.45f, 0.25f), 9f, 6f)));
            if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(at, 0.3f), 10, new Color(1f, 0.5f, 0.25f), 3, 0.5f, 0.08f, 3);
        }
        public bool Lit(Vector2 p)
        {
            if (!Dark && ZoneCells.At(p) != 'd') return true;
            if ((p - g.player.p).sqrMagnitude < 7.5f * 7.5f) return true;   // the hero's own light (was 5.5 m: Burrows at full RP died in 20 s)
            foreach (var f in flares) if ((p - f.p).sqrMagnitude < 49f) return true;
            foreach (var y in pylons) if (y.lit && (p - y.p).sqrMagnitude < 49f) return true;
            return false;
        }
        public bool Exposed(Vector2 p)
        {
            foreach (var f in flares) if ((p - f.p).sqrMagnitude < 49f) return true;
            foreach (var y in pylons) if (y.lit && (p - y.p).sqrMagnitude < 49f) return true;
            return false;
        }
        Light MakeLight(Vector2 at, Color c, float range, float intensity)
        {
            if (Sim) return null;
            var go = new GameObject("ZoneLight"); go.transform.SetParent(root, false); go.transform.position = MazeMap.ToWorld(at, 2.5f);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = range; l.intensity = intensity; l.color = c; l.shadows = LightShadows.None; return l;
        }
        public override void Tick(float dt)
        {
            bool dark = Dark;
            if (dark && heroLight == null) heroLight = MakeLight(g.player.p, new Color(0.87f, 0.91f, 1f), 7f, 5f);
            if (heroLight != null) { heroLight.enabled = dark; heroLight.transform.position = MazeMap.ToWorld(g.player.p, 2.4f); }
            // flare caches: walking over one drops a flare
            foreach (var c in MazeMap.Marks('F'))
            {
                if (usedCaches.Contains(c)) continue; var p = MazeMap.Center(c.x, c.y);
                if ((g.player.p - p).magnitude < 1.2f && Flares < 3) { usedCaches.Add(c); Flares = 3; g.hud.ShowBanner("FLARES 3/3"); }
            }
            for (int i = flares.Count - 1; i >= 0; i--)
            {
                var f = flares[i]; f.t -= dt; flares[i] = f;
                if (f.l != null) f.l.intensity = 5f + Mathf.PerlinNoise(Time.time * 6, i) * 2f;
                if (f.t <= 0) { if (f.l != null) Object.Destroy(f.l.gameObject); flares.RemoveAt(i); }
            }
            for (int i = 0; i < pylons.Count; i++)
            {
                var y = pylons[i]; if (y.lit) continue;
                if ((g.player.p - y.p).magnitude < 1.6f) y.hold += dt; else y.hold = 0;
                if (y.hold >= 1.5f) { y.lit = true; y.l = MakeLight(y.p, new Color(0.9f, 0.95f, 1f), 9f, 5f); y.m.SetColor("_BaseColor", new Color(0.9f, 0.95f, 1f, 0.9f)); g.hud.ShowBanner("PYLON LIT"); }
                pylons[i] = y;
            }
        }
        public override void End() { if (I == this) I = null; base.End(); }
    }

    // WEATHER: WIND gusts (2 s warning, 3 s push 4 m/s one way; blocks give 2 cells of shadow), DRIFT (a constant
    // 1.2 m/s slide), ACID_RAIN pulses (3 s warning, 4 s rain: 6 HP / 0.5 s outside cover, enemies 4% max HP)
    public class Weather : ZoneHazard
    {
        public static Weather I;
        public float DriftSpeed = 1.2f;   // (HARD TILT: 2)
        public bool Wind, Rain, Drift; public float WindPeriod = 12f, RainPeriod = 20f; public Vector2 WindDir = Vector2.right, DriftDir = Vector2.right;
        float windT, rainT, tick; Vector2 push; bool gust, raining;
        public override void Begin(Game game) { base.Begin(game); I = this; windT = 12f; rainT = 20f; }
        public void SetWind(bool on, Vector2 dir, float period, float first) { Wind = on; WindDir = dir; WindPeriod = period; windT = first > 0 ? first : period; }
        public void SetRain(bool on, float period, float first) { Rain = on; RainPeriod = period; rainT = first > 0 ? first : period; }
        static Vector2 Dir(string d) => d == "N" ? Vector2.down : d == "S" ? Vector2.up : d == "W" ? Vector2.left : Vector2.right;
        public static Vector2 ParseDir(object o) => o is string s ? Dir(s) : o is List<object> l && l.Count > 0 ? Dir(l[Random.Range(0, l.Count)] as string) : Vector2.right;
        public override void Tick(float dt)
        {
            push = Vector2.zero;
            if (Wind)
            {
                windT -= dt;
                if (windT <= 2f && windT + dt > 2f) g.hud.ShowBanner("GUST " + (WindDir.x > 0 ? "→" : WindDir.x < 0 ? "←" : WindDir.y > 0 ? "↓" : "↑"));
                gust = windT <= 0 && windT > -3f;
                if (windT <= -3f) windT = WindPeriod;
                if (gust)
                {
                    if (g.player.dashT <= 0 && !Shadowed(g.player.p)) push += WindDir * 4f;
                    foreach (var e in g.swarm.enemies)
                    {
                        if (e.dead || e.type.nest || e.boss || Shadowed(e.p)) continue;
                        e.p += WindDir * 4f * dt;
                        if (ZoneCells.At(e.p + WindDir * e.r) == '_') g.KillEnemy(e);   // blown into the void
                    }
                }
            }
            if (Drift) { push += DriftDir * DriftSpeed; foreach (var e in g.swarm.enemies) if (!e.dead && !e.type.nest) e.p += DriftDir * DriftSpeed * dt; }
            if (Rain)
            {
                rainT -= dt;
                if (rainT <= 3f && rainT + dt > 3f) g.hud.ShowBanner("ACID RAIN IN 3");
                raining = rainT <= 0 && rainT > -4f;
                if (rainT <= -4f) rainT = RainPeriod;
                if (raining && (tick -= dt) <= 0)
                {
                    tick = 0.5f;
                    if (ZoneCells.At(g.player.p) != 'u') g.HurtPlayer(ZoneCells.Hp(g, 6));
                    foreach (var e in g.swarm.enemies) if (!e.dead && !e.type.nest && ZoneCells.At(e.p) != 'u') { e.hp -= e.maxHp * 0.04f; if (e.hp <= 0) g.KillEnemy(e); }
                }
            }
        }
        // a block (B/C/#/m) within 2 cells upwind
        bool Shadowed(Vector2 p) { for (int k = 1; k <= 2; k++) { var q = p - WindDir * MazeMap.C * k; if (MazeMap.ShotSolidAt(q.x, q.y)) return true; } return false; }
        public override Vector2 Push => push;
        public override void End() { if (I == this) I = null; base.End(); }
    }
}

namespace Maze
{
    // WATER + FLOOD: 'w' is shallow water (hero -20%); each FLOOD pulse spreads it over spread_s seconds (cap 40% of
    // the floor) and deepens it (deep: hero -45%, Drones -30%). Drawn as one translucent mesh rebuilt on change.
    public class CellWater : ZoneHazard
    {
        public static CellWater I; public int Depth; Mesh mesh; bool dirty = true; float spreadLeft, spreadT; int walkable;
        public override void Begin(Game game)
        {
            base.Begin(game); I = this;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) walkable++;
            var go = new GameObject("Water"); go.transform.SetParent(root, false); mesh = new Mesh(); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Fade(new Color(0.16f, 0.36f, 0.48f, 0.6f)); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        public void Flood(float spreadS) { spreadLeft = spreadS; Depth++; g.hud.ShowBanner(Depth >= 2 ? "THE WATER IS DEEP" : "FLOODING"); }
        public override void Tick(float dt)
        {
            if (spreadLeft > 0 && (spreadT -= dt) <= 0)
            {
                spreadT = 0.25f; spreadLeft -= 0.25f; int water = 0;
                for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.tag[z, x] == 'w') water++;
                for (int k = 0; k < 6 && water < walkable * 0.4f; k++)
                {
                    int x = Random.Range(1, MazeMap.W - 1), z = Random.Range(1, MazeMap.H - 1); if (MazeMap.tag[z, x] != 'w') continue;
                    var d = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down }[Random.Range(0, 4)];
                    int nx = x + d.x, nz = z + d.y; char t = MazeMap.tag[nz, nx];
                    if (MazeMap.Walkable(nx, nz) && (t == '.' || t == '~' || t == 'y')) { MazeMap.tag[nz, nx] = 'w'; water++; dirty = true; }
                }
            }
            if (dirty)
            {
                dirty = false; var cells = new List<Vector2Int>();
                for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.tag[z, x] == 'w') cells.Add(new Vector2Int(x, z));
                var q = ZoneCells.Quads(cells, Depth >= 2 ? 0.12f : 0.03f); mesh.Clear(); mesh.SetVertices(new List<Vector3>(q.vertices)); mesh.SetTriangles(q.triangles, 0); mesh.RecalculateBounds(); Object.Destroy(q);
            }
        }
        public override float MoveMul => ZoneCells.At(g.player.p) == 'w' && g.player.dashT <= 0 && Depth >= 2 ? 0.55f / 0.8f : 1f;   // (shallow -20% is CellFloors)
        public override void End() { if (I == this) I = null; base.End(); }
    }
}
