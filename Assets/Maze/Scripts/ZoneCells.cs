using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // The toolkit's cell mechanics (Design/drop-zones-core.json): a v2 layout's letters switch them on by themselves
    // (Hazards.Begin asks ForLayout). Numbers follow the spec: hero damage in player HP (player = 100, scaled to the
    // config's base HP), enemy damage in rifle hits (RH, a Drone = 2 RH at the current floor).
    public static class ZoneCells
    {
        public static float Hp(Game g, float specHp) => specHp * g.cfg.player.hp / 100f;
        public static float RH(Game g) => g.cfg.enemy.hp * g.swarm.HpScale * (1 + g.cfg.waves.hpGrowth * (g.wave - 1)) * 0.5f;
        public static char At(Vector2 p)
        {
            if (MazeMap.tag == null || !MazeMap.V2) return '.';
            int x = Mathf.FloorToInt(p.x / MazeMap.C), z = Mathf.FloorToInt(p.y / MazeMap.C);
            return x < 0 || z < 0 || x >= MazeMap.W || z >= MazeMap.H ? '#' : MazeMap.tag[z, x];
        }
        // the swarm on creep runs faster
        public static float EnemySpeedMul(Enemy e) { char c = At(e.p); return c == 'c' ? 1.15f : c == 'w' && CellWater.I != null && CellWater.I.Depth >= 2 ? 0.7f : 1f; }

        public static CellFences FindFences() { foreach (var h in Hazards.Live) if (h is CellFences f) return f; return null; }
        public static IEnumerable<Hazard> ForLayout(Game g)
        {
            if (!MazeMap.V2) yield break;
            foreach (var h in ZoneCells2.ForLayout(g)) yield return h;
            yield return new CellDecals();
            yield return new CellFloors();
            if (MazeMap.Marks('c').Count > 0) yield return new CellSpread();
            if (MazeMap.Marks('o').Count > 0) yield return new CellVents();
            if (MazeMap.Marks('e').Count > 0) yield return new CellEggs();
            if (MazeMap.Marks('T').Count > 0) yield return new CellTurrets();
            if (MazeMap.Marks('|').Count > 0) yield return new CellFences();
            if (MazeMap.Marks('+').Count > 0) yield return new CellMeds();
        }

        public static Mesh Quads(List<Vector2Int> cells, float y, float inset = 0f)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            float C = MazeMap.C;
            foreach (var c in cells)
            {
                float x0 = c.x * C + inset, x1 = (c.x + 1) * C - inset, z0 = c.y * C + inset, z1 = (c.y + 1) * C - inset;
                int b = v.Count;
                v.Add(MazeMap.ToWorld(new Vector2(x0, z0), y)); v.Add(MazeMap.ToWorld(new Vector2(x1, z0), y));
                v.Add(MazeMap.ToWorld(new Vector2(x1, z1), y)); v.Add(MazeMap.ToWorld(new Vector2(x0, z1), y));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);   // both faces (the plane flips z)
            }
            var m = new Mesh { indexFormat = v.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    // every mechanic letter drawn on the floor (placeholder looks: flat tinted quads, the spec's colours)
    public class CellDecals : ZoneHazard
    {
        static readonly Dictionary<char, (Color c, float y, bool glow)> Look = new()
        {
            { 'i', (new Color(0.62f, 0.84f, 0.98f, 0.8f), 0.025f, false) },
            { 'a', (new Color(0.49f, 1f, 0.23f, 0.75f), 0.03f, true) },
            { '*', (new Color(1f, 0.42f, 0.1f, 0.95f), 0.03f, true) },
            { 'q', (new Color(0.78f, 1f, 0.23f, 0.45f), 0.03f, true) },
            { 'f', (new Color(1f, 0.45f, 0.1f, 0.7f), 0.03f, true) },
            { 'd', (new Color(0f, 0f, 0f, 0.55f), 0.022f, false) },
            { 'g', (new Color(0.36f, 0.38f, 0.42f, 0.95f), 0.025f, false) },
            { 'k', (new Color(0.36f, 0.28f, 0.21f, 0.95f), 0.025f, false) },
            { '_', (new Color(0.02f, 0.02f, 0.03f, 1f), 0.02f, false) },
            { 'J', (new Color(1f, 0.82f, 0.1f, 0.9f), 0.03f, true) },
            { 'u', (new Color(0.55f, 0.6f, 0.65f, 0.35f), 3.2f, false) },
            { 'R', (new Color(0.2f, 0.9f, 1f, 0.8f), 0.035f, true) },
            { 'H', (new Color(1f, 0.7f, 0.2f, 0.8f), 0.035f, true) },
            { '$', (new Color(0.3f, 0.8f, 1f, 0.9f), 0.035f, true) },
            { '!', (new Color(1f, 0.25f, 0.2f, 0.9f), 0.035f, true) },
            { ':', (new Color(0.55f, 0.45f, 0.35f, 0.9f), 0.025f, false) },
            { 'Q', (new Color(0.9f, 0.7f, 0.2f, 0.9f), 0.035f, true) },
            { 'F', (new Color(1f, 0.35f, 0.2f, 0.9f), 0.035f, true) },
            { 'I', (new Color(0.9f, 0.95f, 1f, 0.9f), 0.035f, true) },
            { 'K', (new Color(1f, 0.85f, 0.2f, 0.9f), 0.035f, true) },
            { 'G', (new Color(0.4f, 1f, 0.6f, 0.9f), 0.035f, true) },
            { '%', (new Color(0.66f, 0.79f, 0.29f, 0.9f), 0.035f, true) },
            { '>', (new Color(0.17f, 0.17f, 0.18f, 1f), 0.025f, false) }, { '<', (new Color(0.17f, 0.17f, 0.18f, 1f), 0.025f, false) },
            { '^', (new Color(0.17f, 0.17f, 0.18f, 1f), 0.025f, false) }, { 'V', (new Color(0.17f, 0.17f, 0.18f, 1f), 0.025f, false) },
        };
        public override void Begin(Game game)
        {
            base.Begin(game);
            foreach (var kv in MazeMap.marks)
            {
                if (!Look.TryGetValue(kv.Key, out var l)) continue;
                bool small = "RH$!QFIKG%".IndexOf(kv.Key) >= 0;
                var go = new GameObject("Cells_" + kv.Key); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = ZoneCells.Quads(kv.Value, l.y, small ? 0.45f : 0f);
                var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.sharedMaterial = l.glow ? Mats.Glow(l.c) : l.c.a >= 0.9f ? Mats.Lit(new Color(l.c.r, l.c.g, l.c.b), null, null, 0.3f) : Mats.Fade(l.c);   // opaque ones take the light (they'd glow in the dark)
            }
            foreach (var c in MazeMap.Marks('G')) { var p = MazeMap.Center(c.x, c.y); Box(new Color(0.25f, 0.3f, 0.28f), p, new Vector3(1.3f, 1.6f, 1.3f), 0.8f); Box(new Color(0.4f, 1f, 0.6f), p, new Vector3(0.5f, 0.3f, 0.5f), 1.75f); }
            // conveyors: a scrolling chevron strip per direction
            foreach (var (ch, dir) in new[] { ('>', Vector2.right), ('<', Vector2.left), ('^', Vector2.down), ('V', Vector2.up) })
            {
                var cells = MazeMap.Marks(ch); if (cells.Count == 0) continue;
                var go = new GameObject("Belt_" + ch); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = BeltMesh(cells, dir);
                var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var m = Mats.Unlit(Color.white, Chevrons); mr.sharedMaterial = m; belts.Add(m);
            }
        }
        readonly List<Material> belts = new();
        public override void Tick(float dt)
        {
            foreach (var m in belts) { var o = m.GetTextureOffset("_BaseMap"); o.y -= dt * CellFloors.BeltSpeed * CellFloors.BeltDir / MazeMap.C; m.SetTextureOffset("_BaseMap", o); }
        }
        static Texture2D chev;
        public static Texture2D Chevrons
        {
            get
            {
                if (chev != null) return chev;
                chev = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    int v = (y + 32 - Mathf.Abs(x - 16)) % 16;
                    chev.SetPixel(x, y, v < 5 ? new Color(0.95f, 0.78f, 0.2f) : new Color(0.17f, 0.17f, 0.18f));
                }
                chev.Apply(); return chev;
            }
        }
        // a quad per cell whose v axis runs along the belt (plane dir), so one scrolling material reads for every cell
        static Mesh BeltMesh(List<Vector2Int> cells, Vector2 dir)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>(); float C = MazeMap.C;
            var side = new Vector2(dir.y, -dir.x);
            foreach (var c in cells)
            {
                var ctr = new Vector2((c.x + 0.5f) * C, (c.y + 0.5f) * C); int b = v.Count;
                var a = ctr - dir * C * 0.5f - side * C * 0.5f; var bb = ctr - dir * C * 0.5f + side * C * 0.5f;
                var cc = ctr + dir * C * 0.5f + side * C * 0.5f; var d = ctr + dir * C * 0.5f - side * C * 0.5f;
                v.Add(MazeMap.ToWorld(a, 0.03f)); v.Add(MazeMap.ToWorld(bb, 0.03f)); v.Add(MazeMap.ToWorld(cc, 0.03f)); v.Add(MazeMap.ToWorld(d, 0.03f));
                uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 0));
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
    }

    // what the floor under the hero does: ice (no grip), water (slow), creep (slow), acid / slag (burn), conveyors
    // (carry), radiation (RAD meter); and what it does to the swarm: slag kills, pits swallow anything shoved in
    public class CellFloors : ZoneHazard
    {
        public static float BeltSpeed = 3f; public static float BeltDir = 1f; public static Vector2? BeltOverride;   // (SET CONVEYOR dir)   // -1: reversed (TOGGLE CONVEYOR reverse)
        char under; float tick, ignite; public static float Rad;   // 0..1, the RAD meter (HUD later)
        Vector2 push;
        public override void Tick(float dt)
        {
            under = ZoneCells.At(g.player.p);
            push = under switch { '>' => Vector2.right, '<' => Vector2.left, '^' => Vector2.down, 'V' => Vector2.up, _ => Vector2.zero } * BeltSpeed * BeltDir;
            if (BeltOverride.HasValue && push != Vector2.zero) push = BeltOverride.Value * BeltSpeed * BeltDir;
            bool dashing = g.player.dashT > 0;
            if ((tick -= dt) <= 0)
            {
                tick = 0.25f;
                if (!dashing && under == 'a' && g.mods["acid_walker"] == 0) g.HurtPlayer(ZoneCells.Hp(g, 10) * 0.25f);
                if (!dashing && under == '*') { g.HurtPlayer(ZoneCells.Hp(g, 30) * 0.25f); ignite = 2f; }
                else if (ignite > 0) g.HurtPlayer(ZoneCells.Hp(g, 6) * 0.25f);
                if (Rad >= 1f || (Rad > 0.6f && radHot)) { radHot = true; g.HurtPlayer(ZoneCells.Hp(g, 5) * 0.25f); }
                if (Rad < 0.6f) radHot = false;
            }
            ignite -= dt;
            Rad = Mathf.Clamp01(Rad + (under == 'q' ? 0.12f : -0.08f) * dt);
            // the swarm: slag melts it, pits swallow what gets shoved in, belts carry it
            foreach (var e in g.swarm.enemies)
            {
                if (e.dead || e.type.nest || e.boss) continue;
                char c = ZoneCells.At(e.p);
                if (c == '*') { e.hp -= e.maxHp * 2f * dt; e.flash = 1; if (e.hp <= 0) g.KillEnemy(e); continue; }
                if (c == '>' || c == '<' || c == '^' || c == 'V') e.p += (BeltOverride ?? (c == '>' ? Vector2.right : c == '<' ? Vector2.left : c == '^' ? Vector2.down : Vector2.up)) * BeltSpeed * BeltDir * dt;
                if (e.k.sqrMagnitude > 4f)
                {   // shoved: about to land in a pit?
                    var ahead = e.p + e.k.normalized * (e.r + 0.3f);
                    if (ZoneCells.At(ahead) == '_' || ZoneCells.At(e.p) == '_')
                    {
                        if (!Sim) Fx.I.Smoke(MazeMap.ToWorld(ahead, 0.2f), 4, new Color(0.3f, 0.3f, 0.32f), 2, 0.5f, 0.2f, -2);
                        g.KillEnemy(e);
                    }
                }
            }
        }
        bool radHot;
        public override float MoveMul => g.player.dashT > 0 ? 1f : under switch { 'w' => 0.8f, 'c' => 0.65f, _ => 1f };
        public override float Friction => under == 'i' ? 0.25f : 1f;
        public override Vector2 Push => push;
        public override void End() { Rad = 0; BeltDir = 1f; BeltOverride = null; BeltSpeed = 3f; base.End(); }
    }

    // VENT: two alternating groups (column parity) on a 6 s cycle: 3.3 s idle, 1.2 s warning, 1.5 s eruption.
    // Cryo skin (Cryo Station) freezes instead of burning: hero 0.8 s, enemies 2 s (frozen take +50%).
    public class CellVents : ZoneHazard
    {
        class Vent { public Vector2 p; public int group; public Transform ring; public Material m; public bool hitHero; }
        readonly List<Vent> vents = new();
        float t, tick; Color col;
        public float Cycle = 6f, Warn = 1.2f, Erupt = 1.5f; public bool Enabled = true; const float R = 1.5f;
        string skin = "steam";
        public string Skin { get => skin; set { skin = value; col = skin == "cryo" ? new Color(0.65f, 0.85f, 1f) : skin == "toxic" ? new Color(0.5f, 1f, 0.3f) : new Color(1f, 0.55f, 0.15f); } }
        bool cryo => skin == "cryo";
        public static int Frozen;   // enemies frozen by vents this run (the zone's bonus star)
        // rev2 FLASH FREEZE: every vent erupts at once (radius r): cryo freezes enemies for freezeS (and a hero still on one)
        public void EruptAll(float r, float freezeS)
        {
            foreach (var v in vents)
            {
                foreach (var e in g.swarm.enemies) if (!e.dead && !e.type.nest && (e.p - v.p).magnitude < r + e.r) { if (cryo) { e.frozen = Mathf.Max(e.frozen, freezeS); Frozen++; } else { e.hp -= ZoneCells.RH(g) * 4f; e.flash = 1; } }
                if ((g.player.p - v.p).magnitude < 1.5f + g.cfg.player.radius && g.player.dashT <= 0) g.player.frozenT = Mathf.Max(g.player.frozenT, 0.8f);
                if (!Sim) Fx.I.Smoke(MazeMap.ToWorld(v.p, 0.3f), 18, Color.Lerp(col, Color.white, 0.5f), r * 0.6f, 0.8f, 0.5f, 1);
            }
            g.AddShake(0.3f); Haptics.Heavy();
        }
        public override void Begin(Game game)
        {
            base.Begin(game);
            Skin = g.sector != null && g.sector.id == "cryo-station" ? "cryo" : g.sector != null && g.sector.id == "quarantine-ward" ? "toxic" : "steam";   // scripts SET the skin too
            foreach (var c in MazeMap.Marks('o'))
            {
                var p = MazeMap.Center(c.x, c.y);
                Box(new Color(0.12f, 0.13f, 0.15f), p, new Vector3(1.5f, 0.08f, 1.5f), 0.04f);
                var (tr, m) = Flat(Fx.Ring, col * new Color(1, 1, 1, 0.2f), p, R, 0.08f);
                vents.Add(new Vent { p = p, group = c.x & 1, ring = tr, m = m });
            }
            t = 0;
        }
        public override void Tick(float dt)
        {
            t += dt; bool hurt = (tick -= dt) <= 0; if (hurt) tick = 0.5f;
            bool idleFloor = !Enabled;   // switched off by the floor script: shown, idle
            float Idle = Mathf.Max(0.2f, Cycle - Warn - Erupt);
            foreach (var v in vents)
            {
                float ph = (t + (v.group == 1 ? Cycle * 0.5f : 0)) % Cycle;
                bool warn = !idleFloor && ph >= Idle && ph < Idle + Warn, on = !idleFloor && ph >= Idle + Warn;
                v.m.SetColor("_BaseColor", col * new Color(1, 1, 1, on ? 0.9f : warn ? 0.35f + 0.5f * Mathf.PingPong(t * 6, 1) : 0.15f));
                if (warn && !Sim && Random.value < dt * 8) Fx.I.Smoke(MazeMap.ToWorld(v.p, 0.2f), 1, Color.Lerp(col, Color.white, 0.6f), 0.6f, 0.6f, 0.25f, 1.5f);
                if (!on) { v.hitHero = false; continue; }
                if (!Sim && Random.value < dt * 30) Fx.I.Smoke(MazeMap.ToWorld(v.p + Random.insideUnitCircle * 0.4f, 0.3f), 1, Color.Lerp(col, Color.white, 0.5f), 1.2f, 0.7f, 0.5f, 7f);
                bool heroIn = (g.player.p - v.p).magnitude < R + g.cfg.player.radius;
                if (cryo)
                {
                    if (heroIn && !v.hitHero && g.player.dashT <= 0) { g.player.frozenT = 0.8f; v.hitHero = true; Haptics.Light(); }
                    foreach (var e in g.swarm.enemies)
                        if (!e.dead && !e.type.nest && e.frozen <= 0 && (e.p - v.p).magnitude < R + e.r)
                        { e.frozen = e.boss ? 2f : 2f; Frozen++; if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(e.p, 0.8f), 6, new Color(0.7f, 0.9f, 1f), 2, 0.5f, 0.1f, 2); }
                }
                else if (hurt)
                {
                    if (heroIn) g.HurtPlayer(ZoneCells.Hp(g, 18));
                    HurtEnemies(v.p, R, 0, ZoneCells.RH(g) * 4f * 0.5f);
                }
            }
        }
    }

    // EGGS: clusters that hatch 3 Drones (2 minis on floors 1-2) when the hero lingers within 3.5 m for 1 s.
    // Shootable (2 RH, a low-priority target): destroyed = 1 biomass, nothing hatches.
    public class CellEggs : ZoneHazard
    {
        class Egg { public Enemy e; public float near; public bool hatched; }
        readonly List<Egg> eggs = new();
        public override void Begin(Game game)
        {
            base.Begin(game);
            int nt = g.swarm.NestType; if (nt < 0) return;
            foreach (var c in MazeMap.Marks('e'))
            {
                var p = MazeMap.Center(c.x, c.y);
                var e = g.swarm.Spawn(p, g.wave, nt); e.p = p; e.egg = true; e.spawnCd = 1e9f;
                e.hp = e.maxHp = ZoneCells.RH(g) * 2f; e.scale *= 0.5f; e.r *= 0.55f;
                eggs.Add(new Egg { e = e });
            }
        }
        public void HatchAll() { foreach (var x in eggs) if (!x.e.dead && !x.hatched) x.near = 1f; }
        public override void Tick(float dt)
        {
            foreach (var x in eggs)
            {
                if (x.hatched || x.e.dead) continue;
                bool close = (g.player.p - x.e.p).magnitude < 3.5f;
                x.near = close ? x.near + dt : Mathf.Max(0, x.near - dt);
                if (x.near > 0) x.e.flash = 0.5f + 0.5f * Mathf.PingPong(Time.time * 8, 1);   // wobble telegraph
                if (x.near < 1f) continue;
                x.hatched = true; x.e.dead = true;   // hatches: no biomass, three crawl out
                if (!Sim) Fx.I.GooBurst(MazeMap.ToWorld(x.e.p, 0.5f), 1.2f);
                bool minis = g.floor <= 2 && g.swarm.TypeByName("mini") >= 0;
                int n = minis ? 2 : 3;
                for (int i = 0; i < n; i++) { var d = g.swarm.Spawn(x.e.p + Random.insideUnitCircle * 0.8f, g.wave, minis ? g.swarm.TypeByName("mini") : g.swarm.TypeByName("drone")); d.spawnT = 0.6f; }
            }
        }
    }

    // TURRET: stand within 2 m for 2.5 s to capture it; it fires for 25 s (12 m, 6 shots/s, 1 RH a shot), then
    // cools down 15 s. Ring under it: grey neutral, cyan owned, red cooling. (The swarm switching targets to it is
    // approximated: enemies that touch it chew its 200 HP.)
    public class CellTurrets : ZoneHazard
    {
        class Turret { public Vector2 p; public float cap, owned, cool, hp, shot; public Transform head; public Material ring; public Vector2 aim = Vector2.up; }
        readonly List<Turret> list = new();
        public float CaptureMul = 1f; public bool Locked;   // Locked: CLOSE T (can't be captured)
        public override void Begin(Game game)
        {
            base.Begin(game);
            foreach (var c in MazeMap.Marks('T'))
            {
                var p = MazeMap.Center(c.x, c.y);
                Box(new Color(0.18f, 0.19f, 0.22f), p, new Vector3(1.0f, 0.9f, 1.0f), 0.45f);
                var head = Box(new Color(0.35f, 0.37f, 0.4f), p, new Vector3(0.35f, 0.3f, 1.4f), 1.1f);
                var (_, m) = Flat(Fx.Ring, new Color(0.6f, 0.6f, 0.6f, 0.6f), p, 2f, 0.06f);
                list.Add(new Turret { p = p, head = head, ring = m });
            }
        }
        public override void Tick(float dt)
        {
            foreach (var t in list)
            {
                var d = g.player.p - t.p; float dist = d.magnitude;
                if (dist < 0.9f) g.player.p = t.p + d.normalized * 0.9f;   // solid
                if (t.cool > 0) { t.cool -= dt; t.ring.SetColor("_BaseColor", new Color(1f, 0.25f, 0.2f, 0.6f)); continue; }
                if (t.owned > 0)
                {
                    t.owned -= dt; t.ring.SetColor("_BaseColor", new Color(0.3f, 0.85f, 1f, 0.8f));
                    Enemy best = null; float bd = 12f * 12f;
                    foreach (var e in g.swarm.enemies)
                    {
                        if (e.dead || e.spawnT < 0.5f) continue; float dd = (e.p - t.p).sqrMagnitude;
                        if (dd < bd) { bd = dd; best = e; }
                        if (dd < (e.r + 0.7f) * (e.r + 0.7f)) t.hp -= e.damage * dt * 1.2f;   // the swarm chews it
                    }
                    if (best != null)
                    {
                        t.aim = (best.p - t.p).normalized;
                        if ((t.shot -= dt) <= 0)
                        {
                            t.shot = 1f / 6f; best.hp -= ZoneCells.RH(g); best.flash = 1;
                            if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(best.p, 0.8f), 2, new Color(1f, 0.85f, 0.4f), 3, 0.2f, 0.06f, 1);
                            if (best.hp <= 0) g.KillEnemy(best);
                        }
                    }
                    if (t.owned <= 0 || t.hp <= 0) { t.owned = 0; t.cool = 15f; }
                }
                else
                {
                    bool near = dist < 1.5f + g.cfg.player.radius && !Locked;
                    t.cap = near ? t.cap + dt * CaptureMul * (Meta.Relic(1) ? 1f + Meta.RelicV : 1f) : Mathf.Max(0, t.cap - dt);
                    float k = t.cap / 2.5f;
                    t.ring.SetColor("_BaseColor", Color.Lerp(new Color(0.6f, 0.6f, 0.6f, 0.5f), new Color(0.3f, 0.85f, 1f, 0.9f), near ? Mathf.PingPong(Time.time * 4, 1) * k + k * 0.5f : 0));
                    if (t.cap >= 2.5f) { t.cap = 0; t.owned = 25f; t.hp = ZoneCells.Hp(g, 200); g.hud.ShowBanner("TURRET ONLINE"); Haptics.Light(); }
                }
                t.head.rotation = Quaternion.Euler(0, MazeMap.YawDeg(Mathf.Atan2(t.aim.x, t.aim.y)), 0);
            }
        }
    }

    // LASER_FENCE: each run of '|' cycles 5 s on / 3 s off with a 1 s flicker before it switches on. On: blocks
    // walking and bullets (the grid cell becomes '|'); touching it costs 25 HP and a 3 m shove; enemies 8 RH + shove.
    public class CellFences : ZoneHazard
    {
        class Fence { public List<Vector2Int> cells = new(); public Material m; public bool on; }
        readonly List<Fence> fences = new();
        float t; public float OnSec = 5f, OffSec = 3f; float hitCd;
        public string Mode = "cycle";   // cycle | on | off (TOGGLE from the floor script)
        public override void Begin(Game game)
        {
            base.Begin(game);
            var seen = new HashSet<Vector2Int>();
            foreach (var c in MazeMap.Marks('|'))
            {
                if (seen.Contains(c)) continue;
                var f = new Fence(); var st = new Stack<Vector2Int>(); st.Push(c); seen.Add(c);
                while (st.Count > 0)
                {
                    var q = st.Pop(); f.cells.Add(q);
                    foreach (var n in new[] { q + Vector2Int.up, q + Vector2Int.down, q + Vector2Int.left, q + Vector2Int.right })
                        if (n.x >= 0 && n.y >= 0 && n.x < MazeMap.W && n.y < MazeMap.H && MazeMap.tag[n.y, n.x] == '|' && seen.Add(n)) st.Push(n);
                }
                var go = new GameObject("Fence"); go.transform.SetParent(root, false);
                var mesh = new Mesh(); var v = new List<Vector3>(); var tri = new List<int>();
                foreach (var q in f.cells)
                {   // a glowing slab per cell, 1.6 m tall
                    var p = MazeMap.Center(q.x, q.y); int b = v.Count; float hw = MazeMap.C * 0.5f;
                    bool vertical = MazeMap.tag[Mathf.Clamp(q.y + 1, 0, MazeMap.H - 1), q.x] == '|' || MazeMap.tag[Mathf.Clamp(q.y - 1, 0, MazeMap.H - 1), q.x] == '|';
                    var a = vertical ? new Vector2(0, hw) : new Vector2(hw, 0);
                    v.Add(MazeMap.ToWorld(p - a, 0)); v.Add(MazeMap.ToWorld(p + a, 0)); v.Add(MazeMap.ToWorld(p + a, 1.6f)); v.Add(MazeMap.ToWorld(p - a, 1.6f));
                    tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2 });
                }
                mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); f.m = Mats.Glow(new Color(1f, 0.2f, 0.33f, 0.6f)); mr.sharedMaterial = f.m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fences.Add(f);
            }
            t = OnSec;   // start in the off phase
        }
        public override void Tick(float dt)
        {
            t += dt; hitCd -= dt;
            float cyc = OnSec + OffSec, ph = t % cyc;
            bool on = Mode == "on" || (Mode != "off" && ph < OnSec), flicker = Mode == "cycle" && !on && ph > cyc - 1f;
            foreach (var f in fences)
            {
                if (on != f.on)
                {
                    f.on = on;
                    foreach (var c in f.cells) MazeMap.grid[c.y, c.x] = on ? '|' : '.';
                    MazeMap.flowCell = -1;
                    if (on) foreach (var c in f.cells) Zap(MazeMap.Center(c.x, c.y));
                }
                float a = on ? 0.65f : flicker ? (Mathf.PingPong(t * 14, 1) > 0.5f ? 0.5f : 0.05f) : 0.05f;
                f.m.SetColor("_BaseColor", new Color(1f, 0.2f, 0.33f, a));
                if (on) foreach (var c in f.cells) Touch(MazeMap.Center(c.x, c.y));
            }
        }
        void Touch(Vector2 p)
        {
            var d = g.player.p - p;
            if (hitCd <= 0 && Mathf.Abs(d.x) < MazeMap.C * 0.5f + g.cfg.player.radius + 0.05f && Mathf.Abs(d.y) < MazeMap.C * 0.5f + g.cfg.player.radius + 0.05f)
            { g.HurtPlayer(ZoneCells.Hp(g, 25)); g.player.vel += (d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.up) * 9f; hitCd = 0.8f; }
        }
        void Zap(Vector2 p)
        {
            foreach (var e in g.swarm.enemies)
            {
                if (e.dead || e.type.nest) continue; var d = e.p - p;
                if (Mathf.Abs(d.x) < MazeMap.C * 0.5f + e.r && Mathf.Abs(d.y) < MazeMap.C * 0.5f + e.r)
                { e.hp -= ZoneCells.RH(g) * 8f; e.flash = 1; e.k += (d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.up) * 6f; if (e.hp <= 0) g.KillEnemy(e); }
            }
        }
        public override void End() { foreach (var f in fences) foreach (var c in f.cells) MazeMap.grid[c.y, c.x] = '.'; base.End(); }
    }

    // '+' med station: one use, +35 HP
    public class CellMeds : ZoneHazard
    {
        readonly List<(Vector2 p, Material m, bool used)> meds = new();
        public float OpenAt;   // a floor may lock it until a time (evac-deck floor 4: 25 s)
        float t;
        public override void Begin(Game game)
        {
            base.Begin(game);
            foreach (var c in MazeMap.Marks('+')) { var p = MazeMap.Center(c.x, c.y); var (_, m) = Flat(ZoneHazard.Disc, new Color(0.33f, 0.88f, 0.48f, 0.7f), p, 0.8f, 0.06f); meds.Add((p, m, false)); }
        }
        public override void Tick(float dt)
        {
            t += dt;
            for (int i = 0; i < meds.Count; i++)
            {
                var md = meds[i]; if (md.used) continue;
                bool open = t >= OpenAt;
                md.m.SetColor("_BaseColor", open ? new Color(0.33f, 0.88f, 0.48f, 0.5f + 0.4f * Mathf.PingPong(t * 2, 1)) : new Color(0.4f, 0.4f, 0.4f, 0.4f));
                if (open && (g.player.p - md.p).magnitude < 1.2f && g.player.hp < g.player.maxHp)
                {
                    g.player.hp = Mathf.Min(g.player.maxHp, g.player.hp + ZoneCells.Hp(g, 35)); md.used = true; meds[i] = md;
                    md.m.SetColor("_BaseColor", new Color(0.3f, 0.3f, 0.3f, 0.3f)); g.hud.ShowBanner("+35 HP"); Haptics.Light();
                }
            }
        }
    }
}

namespace Maze
{
    // SPREAD (creep skin): the creep layer grows from living nests (one random frontier cell every 2.5 s per nest,
    // capped at 40% of the walkable floor) and is drawn as one mesh that rebuilds when cells change. Scorched
    // cells ('s') never regrow this floor.
    public class CellSpread : ZoneHazard
    {
        public static bool Dirty; float growT = 2.5f, drawT; Mesh creepMesh, scorchMesh; int walkable;
        public override void Begin(Game game)
        {
            base.Begin(game); Dirty = true;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) walkable++;
            creepMesh = Make("Creep", new Color(0.16f, 0.06f, 0.2f, 0.92f), false);
            scorchMesh = Make("Scorched", new Color(0.1f, 0.1f, 0.1f, 0.9f), false);
        }
        Mesh Make(string n, Color c, bool glow)
        {
            var go = new GameObject(n); go.transform.SetParent(root, false); var mf = go.AddComponent<MeshFilter>(); var m = new Mesh(); mf.sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = glow ? Mats.Glow(c) : Mats.Fade(c); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return m;
        }
        public override void Tick(float dt)
        {
            if ((growT -= dt) <= 0)
            {
                growT = 2.5f; int creep = 0, live = 0;
                for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.tag[z, x] == 'c') creep++;
                foreach (var e in g.swarm.enemies) if (!e.dead && e.type.nest && !e.egg) live++;
                for (int k = 0; k < live && creep < walkable * 0.4f; k++) if (GrowOne()) creep++;
            }
            if (Dirty && (drawT -= dt) <= 0) { drawT = 0.25f; Dirty = false; Rebuild(); }
        }
        bool GrowOne()
        {
            for (int tries = 0; tries < 30; tries++)
            {
                int x = Random.Range(1, MazeMap.W - 1), z = Random.Range(1, MazeMap.H - 1);
                if (MazeMap.tag[z, x] != 'c') continue;
                var d = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down }[Random.Range(0, 4)];
                int nx = x + d.x, nz = z + d.y; char t = MazeMap.tag[nz, nx];
                if (MazeMap.Walkable(nx, nz) && (t == '.' || t == '~' || t == 'y')) { MazeMap.tag[nz, nx] = 'c'; Dirty = true; return true; }
            }
            return false;
        }
        void Rebuild()
        {
            var c = new List<Vector2Int>(); var s = new List<Vector2Int>();
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) { char t = MazeMap.tag[z, x]; if (t == 'c') c.Add(new Vector2Int(x, z)); else if (t == 's') s.Add(new Vector2Int(x, z)); }
            Fill(creepMesh, c, 0.026f); Fill(scorchMesh, s, 0.024f);
        }
        static void Fill(Mesh m, List<Vector2Int> cells, float y) { var q = ZoneCells.Quads(cells, y); m.Clear(); m.SetVertices(new List<Vector3>(q.vertices)); m.SetUVs(0, new List<Vector2>(q.uv)); m.SetTriangles(q.triangles, 0); m.RecalculateBounds(); Object.Destroy(q); }
    }
}
