using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // The new enemies of Design/enemies.json (placeholder looks: an existing model with a tint and scale). Each has a
    // small state machine run from Swarm.Tick (Pre: may stop or redirect its walk; returns true when it handled its
    // own movement). Numbers follow the spec: HP in rifle hits (the type's hp multiplier = spec / 2, a Drone is 2),
    // damage in player HP (scaled by the floor's damage scale through U), seconds as written.
    public static class EnemyAI
    {
        static Game G;
        // a hit of `spec` player HP at this floor's difficulty (a Drone's contact is 8)
        // (special attacks grow at half the rate of contact damage with a zone's difficulty)
        static float U(Enemy e, float spec) => spec * G.cfg.player.hp / 100f * (1f + (G.swarm.DmgScale - 1f) * 0.5f);
        static float RH => ZoneCells.RH(G);
        static bool Sim => Game.SimRun;

        public static void Init(Game g) { G = g; Tele.Clear(); puddles.Clear(); Clouds.Clear(); }

        // ---- telegraphs: flat glow shapes on the floor (pooled), and lingering acid puddles
        public class Tele
        {
            public Transform tr; public Material m; public float t, life; public Color c; public System.Action done; public bool ring, dashMarked;
            bool Hurts => !(c.r > 0.95f && c.g > 0.95f && c.b > 0.95f);
            // how far p is inside the shape (> 0 inside, < 0 outside by that many metres)
            public float Inside(Vector2 p)
            {
                var ctr = MazeMap.ToPlane(tr.position);
                if (tr.GetComponent<MeshFilter>().sharedMesh == quad)
                {
                    var fwd = (MazeMap.ToPlane(tr.position + tr.up) - ctr).normalized; var rel = p - ctr;
                    float along = Mathf.Abs(Vector2.Dot(rel, fwd)) - tr.localScale.y * 0.5f, side = Mathf.Abs(rel.x * fwd.y - rel.y * fwd.x) - tr.localScale.x * 0.5f;
                    return -Mathf.Max(along, side);
                }
                return tr.localScale.x - (p - ctr).magnitude;
            }
            // the hero dashed: every hurting telegraph he's in that lands within 0.25 s is a perfect-dash candidate
            public static void OnDash(Vector2 hero) { foreach (var x in live) if (x.Hurts && x.life - x.t <= 0.25f && x.Inside(hero) > -0.4f) x.dashMarked = true; }
            static float closeAt = -9, perfectAt = -9;
            static readonly List<Tele> live = new(); static readonly Stack<Tele> pool = new();
            public static Tele Circle(Vector2 at, float r, float sec, Color c, System.Action onDone, bool ring = false)
            {
                if (Meta.Difficulty == 2 && G != null && G.run) sec *= 0.7f;   // Nightmare: telegraphs 30% shorter
                var x = Get(ring ? Fx.Ring : ZoneHazard.Disc); x.tr.position = MazeMap.ToWorld(at, 0.07f); x.tr.rotation = Quaternion.identity;
                x.tr.localScale = Vector3.one * r; x.life = sec; x.t = 0; x.c = c; x.done = onDone; x.ring = ring; live.Add(x); return x;
            }
            public static Tele Lane(Vector2 from, Vector2 dir, float len, float width, float sec, Color c, System.Action onDone)
            {
                if (Meta.Difficulty == 2 && G != null && G.run && sec < 50f) sec *= 0.7f;
                var x = Get(Quad); var mid = from + dir * len * 0.5f;
                x.tr.position = MazeMap.ToWorld(mid, 0.07f); x.tr.rotation = Quaternion.Euler(90, MazeMap.YawDeg(Mathf.Atan2(dir.x, dir.y)), 0);
                x.tr.localScale = new Vector3(width, len, 1); x.life = sec; x.t = 0; x.c = c; x.done = onDone; x.ring = false; live.Add(x); return x;
            }
            static Mesh quad; static Mesh Quad { get { if (quad == null) { var go = GameObject.CreatePrimitive(PrimitiveType.Quad); quad = go.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(go); } return quad; } }
            static Tele Get(Mesh mesh)
            {
                Tele x = pool.Count > 0 ? pool.Pop() : null;
                if (x == null)
                {
                    var go = new GameObject("EnemyTele"); x = new Tele { tr = go.transform };
                    go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>(); x.m = Mats.Glow(Color.red); mr.sharedMaterial = x.m;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                x.tr.GetComponent<MeshFilter>().sharedMesh = mesh; x.tr.gameObject.SetActive(true); return x;
            }
            public static void TickAll(float dt)
            {
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (i >= live.Count) continue;   // a callback ended the run (cleared the list)
                    var x = live[i]; x.t += dt; float k = x.life > 0 ? x.t / x.life : 1;
                    x.m.SetColor("_BaseColor", new Color(x.c.r, x.c.g, x.c.b, x.c.a * (0.35f + 0.55f * k) * (Mathf.PingPong(x.t * 8, 1) > 0.3f || k > 0.7f ? 1 : 0.6f)));
                    if (x.t < x.life) continue;
                    live.RemoveAt(i); x.tr.gameObject.SetActive(false); pool.Push(x);
                    if (x.Hurts && G != null && G.run && x.done != null)
                    {   // dodge feedback: PERFECT (dashed out at the last moment), CLOSE CALL (missed by < 0.9 m)
                        float hp0 = G.player.hp, inside = x.Inside(G.player.p);
                        bool marked = x.dashMarked; x.done.Invoke(); x.dashMarked = false;
                        bool hit = G.player.hp < hp0;
                        float now = Time.unscaledTime;
                        if (!hit && inside < 0)
                        {
                            if (marked || (G.player.dashT > 0 && now - perfectAt > 2f && inside > -2.5f))
                            { if (now - perfectAt > 2f) { perfectAt = now; G.OnPerfectDash(); } }
                            else if (inside > -0.9f && now - closeAt > 1.5f) { closeAt = now; G.OnCloseCall(); }
                        }
                        continue;
                    }
                    x.done?.Invoke();
                }
            }
            // the demo bot dodges: the way out of the nearest live telegraph it stands in (circle or lane)
            public static bool Danger(Vector2 p, out Vector2 away)
            {
                away = Vector2.zero;
                foreach (var x in live)
                {
                    if (x.c.r > 0.95f && x.c.g > 0.95f && x.c.b > 0.95f) continue;   // white cues don't hurt
                    var c = MazeMap.ToPlane(x.tr.position); var mesh = x.tr.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh == quad)
                    {   // lane: local Y runs along it
                        var fwd = MazeMap.ToPlane(x.tr.position + x.tr.up) - c; float len = x.tr.localScale.y, w = x.tr.localScale.x;
                        var rel = p - c; float along = Vector2.Dot(rel, fwd.normalized), side = rel.x * fwd.normalized.y - rel.y * fwd.normalized.x;
                        if (Mathf.Abs(along) < len * 0.5f && Mathf.Abs(side) < w * 0.5f + 0.6f) { var sd = new Vector2(fwd.normalized.y, -fwd.normalized.x); away = side >= 0 ? sd : -sd; return true; }
                    }
                    else
                    {
                        float r = x.tr.localScale.x + 0.6f; var d = p - c;
                        if (d.sqrMagnitude < r * r && r < 9f) { away = d.sqrMagnitude > 0.01f ? d.normalized : Random.insideUnitCircle.normalized; return true; }
                    }
                }
                return false;
            }
            public static void Clear() { foreach (var x in live) { if (x.tr != null) { x.tr.gameObject.SetActive(false); pool.Push(x); } } live.Clear(); }
        }

        class Puddle { public Vector2 p; public float r, t, dps, age; public VfxPool v; }
        static readonly List<Puddle> puddles = new(); static float puddleTick;
        public static void AcidPuddle(Vector2 at, float r, float sec, float dps)
        {
            if (Sim) { puddles.Add(new Puddle { p = at, r = r, t = sec, dps = dps }); return; }
            VfxPool v = null;
            if (VfxKit.I != null)
            {
                v = VfxKit.I.Pool(null, at, r, VfxKit.Acid, 0.035f + puddles.Count % 8 * 0.001f);   // tiny y steps: overlapping puddles don't z-fight
                v.SetLife(0);
                if (r >= 1.3f) VfxKit.I.Splash(MazeMap.ToWorld(at, 0f), VfxKit.AcidSplash, r);   // (the toxic elite's 1 m trail drips quietly)
            }
            puddles.Add(new Puddle { p = at, r = r, t = sec, dps = dps, v = v });
        }

        public static void TickGlobal(float dt)
        {
            for (int i = Clouds.Count - 1; i >= 0; i--)
            {
                var c = Clouds[i]; c.t -= dt; if (c.t <= 0) { Clouds.RemoveAt(i); continue; } Clouds[i] = c;
                if (!Sim && VfxKit.I != null) VfxKit.I.Gas(MazeMap.ToWorld(c.p, 0f), 3.4f, dt, Mathf.Clamp01(c.t / 2.5f));   // thins out over the last 2.5 s
            }
            FlushSplits(G.swarm);
            Tele.TickAll(dt);
            bool hit = (puddleTick -= dt) <= 0; if (hit) puddleTick = 0.5f;
            for (int i = puddles.Count - 1; i >= 0; i--)
            {
                if (i >= puddles.Count) continue;
                var p = puddles[i]; p.t -= dt;
                if (hit && G.player.dashT <= 0 && G.mods["acid_walker"] == 0 && (G.player.p - p.p).magnitude < p.r) G.HurtPlayer(ZoneCells.Hp(G, p.dps) * 0.5f);
                if (p.v != null)
                {   // spreads in over 0.35 s, shrinks away over the last 1.5 s
                    p.age += dt; p.v.SetLife(Mathf.Pow(Mathf.Min(Mathf.Clamp01(p.age / 0.35f), Mathf.Clamp01(p.t / 1.5f)), 0.6f));
                    if (p.t > 0.8f && p.r >= 1.3f) p.v.Ambient(dt);
                }
                if (p.t <= 0) { p.v?.Kill(); puddles.RemoveAt(i); }
            }
        }

        public static void Reset() { Tele.Clear(); foreach (var p in puddles) p.v?.Kill(); puddles.Clear(); }

        // ---- per enemy. spd / target may be changed; true = it moved itself this frame (skip the walk)
        public static bool Pre(Swarm s, Enemy e, Player pl, float dist, float dt, ref float spd, ref Vector2 target)
        {
            if (e.frenzy > 0) { e.frenzy -= dt; spd *= 1.4f; }
            switch (e.type.ai)
            {
                case "carapace": return Carapace(s, e, pl, dist, dt, ref spd, ref target);
                case "spitter": Ranged(e, pl, dist, dt, ref spd, ref target, 9, 13, 5); Spitter(e, pl, dist, dt); return false;
                case "tick": return Tick(e, pl, dist, dt, ref spd, ref target);
                case "shrieker": Ranged(e, pl, dist, dt, ref spd, ref target, 12, 16, 0); Shrieker(s, e, dist, dt); return false;
                case "burrower": return Burrower(e, pl, dist, dt, ref spd);
                case "weaver": Ranged(e, pl, dist, dt, ref spd, ref target, 7, 11, 0); Weaver(e, pl, dist, dt); return false;
                case "lancer": Ranged(e, pl, dist, dt, ref spd, ref target, 16, 24, 12); Lancer(e, pl, dist, dt, ref spd); return false;
                case "mender": Mender(s, e, pl, dt, ref spd, ref target); return false;
                case "stalker": return Stalker(e, pl, dist, dt, ref spd, ref target);
                case "spore_pod": spd = 0; SporePod(e, pl, dist, dt); return false;
                case "boss": if (e.brain == null) return false; e.brain.TickLater(dt); return e.brain.Tick(pl, dist, dt, ref spd, ref target);
            }
            return false;
        }

        // keep a band of distance: back off inside `near`, hold between near and far, close in beyond far
        static void Ranged(Enemy e, Player pl, float dist, float dt, ref float spd, ref Vector2 target, float near, float far, float flee)
        {
            var away = (e.p - pl.p) / Mathf.Max(0.01f, dist);
            if (flee > 0 && dist < flee) { target = away; spd *= 1.2f; return; }
            if (dist < near) { target = away; spd *= 0.8f; }
            else if (dist < far && Swarm.ClearLine(e.p, pl.p)) spd = 0;
        }

        // CARAPACE: armoured front (15% damage from a 120 deg arc, see FrontMul), a charge down a yellow lane
        static bool Carapace(Swarm s, Enemy e, Player pl, float dist, float dt, ref float spd, ref Vector2 target)
        {
            e.aiCd -= dt;
            switch (e.aiState)
            {
                case 0:
                    if (e.aiCd <= 0 && dist > 4 && dist < 12 && e.spawnT >= 1 && Swarm.ClearLine(e.p, pl.p))
                    {
                        e.aiState = 1; e.aiT = 0; e.aiDir = (pl.p - e.p).normalized;
                        Tele.Lane(e.p, e.aiDir, 14, 1.6f, 0.9f, new Color(1f, 0.85f, 0.15f, 0.8f), null);
                    }
                    return false;
                case 1:   // wind-up, locked at its start
                    e.aiT += dt; spd = 0; e.yaw = Mathf.Atan2(e.aiDir.x, e.aiDir.y);
                    if (e.aiT >= 0.9f) { e.aiState = 2; e.aiT = 0; e.aiHit = false; }
                    return true;
                case 2:
                {
                    e.aiT += dt; float step = e.speed * 2.4f * 2.2f * dt; var before = e.p;
                    e.p += e.aiDir * step; MazeMap.CollideGrid(ref e.p, e.r);
                    bool wall = (e.p - before).sqrMagnitude < step * step * 0.25f;
                    if (!e.aiHit && (pl.p - e.p).magnitude < e.r + G.cfg.player.radius + 0.3f && pl.dashT <= 0)
                    { e.aiHit = true; G.HurtPlayer(U(e, 25)); pl.vel += e.aiDir * 9f; G.AddShake(0.3f); }
                    foreach (var o in s.enemies) if (o != e && !o.dead && (o.p - e.p).sqrMagnitude < (o.r + e.r) * (o.r + e.r)) { o.k += e.aiDir * 6f; }
                    if (wall) { e.aiState = 3; e.aiT = 0; e.stunFor = 2.5f; e.aiCd = 6f; G.AddShake(0.2f); if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(e.p, 0.8f), 10, new Color(1f, 0.7f, 0.3f), 5, 0.5f, 0.1f, 3); }
                    else if (e.aiT * e.speed * 2.4f * 2.2f >= 14f) { e.aiState = 3; e.aiT = 0; e.stunFor = 0.6f; e.aiCd = 6f; }
                    return true;
                }
                default:   // stunned (front open) / skid
                    e.aiT += dt; spd = 0;
                    if (e.aiT >= e.stunFor) e.aiState = 0;
                    return true;
            }
        }
        // the bullet multiplier on armoured fronts (a stunned Carapace is open)
        public static float FrontMul(Enemy e, Vector2 bulletDir)
        {
            if (e.brain != null) return e.brain.FrontMul(bulletDir);
            if (e.type.ai != "carapace" || e.aiState == 3) return 1f;
            var fwd = new Vector2(Mathf.Sin(e.yaw), Mathf.Cos(e.yaw));
            return Vector2.Dot(fwd, -bulletDir) > 0.5f ? 0.15f : 1f;   // within 60 deg of its nose
        }

        // SPITTER: lobs acid at where you'll be (0.5 s lead): green circle 0.7 s, 10 HP, a 5 s puddle
        static void Spitter(Enemy e, Player pl, float dist, float dt)
        {
            if ((e.aiCd -= dt) > 0 || dist > 16 || e.spawnT < 1 || !Swarm.ClearLine(e.p, pl.p)) return;
            e.aiCd = 3f * Random.Range(0.9f, 1.1f);
            var at = pl.p + pl.vel * 0.5f; float dmg = U(e, 10);
            Tele.Circle(at, 1.4f, 0.8f, new Color(0.5f, 1f, 0.2f, 0.8f), () =>
            {
                if ((G.player.p - at).magnitude < 1.4f + G.cfg.player.radius) G.HurtPlayer(dmg);
                AcidPuddle(at, 1.4f, 5f, 6f);
                if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(at, 0.3f), 8, new Color(0.6f, 1f, 0.2f), 3, 0.5f, 0.1f, 3);
            });
        }

        // TICK: fast zigzag; within 3 m it hops onto you and latches (4 HP/s, -30% speed each, max 3); dash shakes them
        public static int Latched;
        static bool Tick(Enemy e, Player pl, float dist, float dt, ref float spd, ref Vector2 target)
        {
            if (e.aiState == 2)
            {   // latched: ride the hero
                e.p = pl.p + e.aiDir * 0.35f; e.v = Vector2.zero;
                e.aiCd -= dt;
                if (pl.dashT > 0 || e.aiCd < -6f) { e.aiState = 0; Latched--; e.aiCd = 2f; e.k = -e.aiDir * 6f; e.hp -= RH; if (e.hp <= 0) G.KillEnemy(e); return true; }
                if ((e.aiT -= dt) <= 0) { e.aiT = 0.5f; G.HurtPlayer(U(e, 4) * 0.5f * (G.mods["thick_hide"] > 0 ? 0.5f : 1f)); }
                return true;
            }
            if (e.aiState == 1)
            {   // the hop (0.4 s white flash, then onto the hero)
                e.aiT += dt; spd = 0; e.flash = 1;
                if (e.aiT > 0.4f)
                {
                    if ((pl.p - e.p).magnitude < 3.5f && Latched < 3 && pl.dashT <= 0) { e.aiState = 2; Latched++; e.aiDir = Random.insideUnitCircle.normalized; e.aiT = 0.5f; e.aiCd = 0; }   // falls off after 6 s
                    else e.aiState = 0;
                }
                return true;
            }
            target = (target + new Vector2(-target.y, target.x) * Mathf.Sin(Time.time * 7 + e.walk) * 0.6f).normalized;   // zigzag
            if (dist < 3f && e.spawnT >= 1 && Latched < 3) { e.aiState = 1; e.aiT = 0; }
            return false;
        }
        public static float LatchSlow => 1f - 0.3f * Mathf.Min(3, Latched) / 3f * 1f;
        public static void OnDeath(Enemy e)
        {
            if (e.type.ai == "tick" && e.aiState == 2) Latched = Mathf.Max(0, Latched - 1);
            if (e.type.ai == "spore_pod" && G != null)
            {
                foreach (var o in G.swarm.enemies) if (!o.dead && (o.p - e.p).magnitude < 3f) { o.hp -= RH * 2; o.flash = 1; }
                if ((G.player.p - e.p).magnitude < 3f) G.HurtPlayer(U(e, 8));
            }
        }

        // SHRIEKER: hangs back; every 8 s a scream (red ring 8 m, 1.2 s) frenzies the pack: +40% speed for 6 s
        static void Shrieker(Swarm s, Enemy e, float dist, float dt)
        {
            if ((e.aiCd -= dt) > 0 || e.spawnT < 1 || dist > 22) return;
            e.aiCd = 8f;
            var at = e.p; var self = e;
            Tele.Circle(at, 8f, 1.2f, new Color(1f, 0.15f, 0.1f, 0.6f), () =>
            {
                if (self.dead) return;
                foreach (var o in s.enemies) if (!o.dead && o != self && !o.type.nest && (o.p - self.p).magnitude < 8f) o.frenzy = 6f;
                if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(self.p, 1.2f), 12, new Color(1f, 0.2f, 0.1f), 5, 0.5f, 0.1f, 3);
            }, ring: true);
        }

        // BURROWER: tunnels (untargetable, a dust trail), surfaces under you (red ring 1.1 m, 1 s), 18 HP + a 0.4 s
        // stun, then stays up 3 s (shoot it now), then digs again
        static bool Burrower(Enemy e, Player pl, float dist, float dt, ref float spd)
        {
            switch (e.aiState)
            {
                case 0:   // underground
                    e.under = true; spd *= 1.4f;
                    if (!Sim && Random.value < dt * 7) Fx.I.Smoke(MazeMap.ToWorld(e.p, 0.1f), 1, new Color(0.55f, 0.42f, 0.3f), 0.8f, 0.6f, 0.25f, 0.6f);
                    if (dist < 1.8f && e.spawnT >= 1 && MazeMap.tag != null && ZoneCells.At(pl.p) != '~')
                    {
                        e.aiState = 1; e.aiT = 0; var at = pl.p; var self = e; float dmg = U(e, 18);
                        Tele.Circle(at, 1.1f, 1.0f, new Color(1f, 0.15f, 0.1f, 0.8f), () =>
                        {
                            if (self.dead) return;
                            self.p = at; self.aiState = 2; self.aiT = 0; self.under = false;
                            if ((G.player.p - at).magnitude < 1.1f + G.cfg.player.radius && G.player.dashT <= 0) { G.HurtPlayer(dmg); G.player.frozenT = Mathf.Max(G.player.frozenT, 0.4f); }
                            foreach (var o in G.swarm.enemies) if (o != self && !o.dead && (o.p - at).magnitude < 1.1f) { o.hp -= dmg * 0.5f; o.flash = 1; }
                            G.AddShake(0.15f); if (!Sim) Fx.I.Debris(MazeMap.ToWorld(at, 0.3f), 12, new Color(0.45f, 0.34f, 0.24f), 5, 0.8f, 0.15f, 6);
                        });
                    }
                    return false;
                case 1: spd = 0; e.p = Vector2.MoveTowards(e.p, pl.p, dt * 2f); return true;   // under the ring
                default:   // surfaced
                    e.under = false; e.aiT += dt; spd *= 0.5f;
                    if (e.aiT > 3f) { e.aiState = 0; if (!Sim) Fx.I.Smoke(MazeMap.ToWorld(e.p, 0.1f), 6, new Color(0.55f, 0.42f, 0.3f), 1.5f, 0.5f, 0.3f, 1); }
                    return false;
            }
        }

        // WEAVER (closest version): every 3 s it strings a 10 m web across your path (white line 0.8 s, then a
        // strand that slows you 60% and burns 3 HP/s while you touch it, 12 s). Max 4 strands.
        class Web { public Vector2 a, b; public float t; public Transform tr; }
        static readonly List<Web> webs = new();
        static void Weaver(Enemy e, Player pl, float dist, float dt)
        {
            if ((e.aiCd -= dt) > 0 || e.spawnT < 1 || dist > 16) return;
            e.aiCd = 3f; if (webs.Count >= 4) return;
            var mv = pl.vel.sqrMagnitude > 0.5f ? pl.vel.normalized : (pl.p - e.p).normalized;
            var mid = pl.p + mv * 3f; var side = new Vector2(-mv.y, mv.x);
            var a = mid - side * 5f; var b = mid + side * 5f;
            Tele.Lane(a, side, 10f, 0.25f, 0.8f, new Color(1f, 1f, 1f, 0.8f), () =>
            {
                var w = new Web { a = a, b = b, t = 12f };
                if (!Sim) { var tl = Tele.Lane(a, side, 10f, 0.18f, 12f, new Color(0.95f, 0.93f, 0.85f, 0.9f), null); w.tr = tl.tr; }
                webs.Add(w);
            });
        }
        public static float WebSlow(Player pl, float dt)
        {
            float m = 1f; if (G.mods["acid_walker"] > 0) { for (int i = webs.Count - 1; i >= 0; i--) { webs[i].t -= dt; if (webs[i].t <= 0) webs.RemoveAt(i); } return 1f; }
            for (int i = webs.Count - 1; i >= 0; i--)
            {
                var w = webs[i]; w.t -= dt; if (w.t <= 0) { webs.RemoveAt(i); continue; }
                var ab = w.b - w.a; float u = Mathf.Clamp01(Vector2.Dot(pl.p - w.a, ab) / ab.sqrMagnitude);
                if ((w.a + ab * u - pl.p).magnitude < 0.5f) { m = 0.4f; webT += dt; if (webT > 0.5f) { webT = 0; G.HurtPlayer(ZoneCells.Hp(G, 3) * 0.5f); } }
            }
            return m;
        }
        static float webT;
        public static void ClearWebs() => webs.Clear();

        // LANCER: holds 16-24 m; a laser tracks you 1.4 s, locks 0.35 s, then a spine flies down it (22 HP, walls block)
        static void Lancer(Enemy e, Player pl, float dist, float dt, ref float spd)
        {
            e.aiCd -= dt;
            if (e.aiState == 0)
            {
                if (e.aiCd > 0 || e.spawnT < 1 || dist > 30 || !Swarm.ClearLine(e.p, pl.p)) return;
                e.aiState = 1; e.aiT = 0; e.aiTele = Tele.Lane(e.p, (pl.p - e.p).normalized, dist, 0.12f, 99f, new Color(1f, 0.1f, 0.1f, 0.8f), null);
            }
            if (e.aiState == 1)
            {
                spd = 0; e.aiT += dt;
                if (e.aiT < 1.4f) e.aiDir = (pl.p - e.p).normalized;
                if (!Swarm.ClearLine(e.p, pl.p) && e.aiT < 1.4f) { EndTele(e); e.aiState = 0; e.aiCd = 1f; return; }   // broke line of sight
                float len = 0; while (len < 30f) { var q = e.p + e.aiDir * (len + 0.5f); if (MazeMap.ShotSolidAt(q.x, q.y)) break; len += 0.5f; }
                if (e.aiTele != null) { var mid = e.p + e.aiDir * len * 0.5f; e.aiTele.tr.position = MazeMap.ToWorld(mid, 0.08f); e.aiTele.tr.rotation = Quaternion.Euler(90, MazeMap.YawDeg(Mathf.Atan2(e.aiDir.x, e.aiDir.y)), 0); e.aiTele.tr.localScale = new Vector3(e.aiT >= 1.4f ? 0.25f : 0.12f, len, 1); }
                e.yaw = Mathf.Atan2(e.aiDir.x, e.aiDir.y);
                if (e.aiT >= 1.75f)
                {
                    EndTele(e); e.aiState = 0; e.aiCd = 3.5f;
                    var rel = pl.p - e.p; float along = Vector2.Dot(rel, e.aiDir), side = Mathf.Abs(rel.x * e.aiDir.y - rel.y * e.aiDir.x);
                    if (along > 0 && along < len && side < 0.6f && pl.dashT <= 0) G.HurtPlayer(U(e, 22));
                    foreach (var o in G.swarm.enemies) { var r2 = o.p - e.p; float al = Vector2.Dot(r2, e.aiDir); if (o != e && !o.dead && al > 0 && al < len && Mathf.Abs(r2.x * e.aiDir.y - r2.y * e.aiDir.x) < o.r + 0.3f) { o.hp -= U(e, 22) * 0.5f; o.flash = 1; } }
                    if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(e.p + e.aiDir * len, 1f), 6, Color.white, 4, 0.3f, 0.08f, 2);
                }
            }
        }
        static void EndTele(Enemy e) { if (e.aiTele != null) { e.aiTele.life = 0; e.aiTele = null; } }

        // MENDER: follows a ward (boss > elite > bloater > carapace), tether-heals it 2 RH/s; pulse-heals 3 RH in 6 m every 6 s
        static void Mender(Swarm s, Enemy e, Player pl, float dt, ref float spd, ref Vector2 target)
        {
            if (e.ward == null || e.ward.dead || (e.aiCd -= dt) < -3f)
            {
                e.aiCd = 0; Enemy best = null; int bp = 0;
                foreach (var o in s.enemies)
                {
                    if (o.dead || o == e || o.type.nest || o.type.ai == "mender") continue;
                    int p = o.boss ? 5 : o.elite ? 4 : o.type.bloater ? 3 : o.type.ai == "carapace" ? 2 : 1;
                    if (p > bp || (p == bp && best != null && (o.p - e.p).sqrMagnitude < (best.p - e.p).sqrMagnitude)) { bp = p; best = o; }
                }
                e.ward = best;
            }
            if (e.ward == null) return;
            var d = e.ward.p - e.p; float dl = d.magnitude;
            if (dl > 4f) target = d / dl; else spd = 0;
            if (dl < 8f && e.ward.frozen <= 0) e.ward.hp = Mathf.Min(e.ward.maxHp, e.ward.hp + 2f * RH * dt);
            if ((e.aiT -= dt) <= 0)
            {
                e.aiT = 6f; var self = e;
                Tele.Circle(e.p, 6f, 1.0f, new Color(0.4f, 1f, 0.6f, 0.5f), () => { if (self.dead) return; foreach (var o in s.enemies) if (!o.dead && (o.p - self.p).magnitude < 6f) o.hp = Mathf.Min(o.maxHp, o.hp + 3f * RH); }, ring: true);
            }
        }

        // STALKER: cloaked (auto-fire ignores it) circling behind you; de-cloaks (0.5 s hiss) and slashes 20 HP,
        // stays visible 3 s, then re-cloaks. Any hit reveals it for 3 s.
        static bool Stalker(Enemy e, Player pl, float dist, float dt, ref float spd, ref Vector2 target)
        {
            e.revealT -= dt;
            switch (e.aiState)
            {
                case 0:
                {
                    e.cloaked = e.revealT <= 0; spd *= 1.3f;
                    var behind = pl.p - (pl.aim.sqrMagnitude > 0.01f ? pl.aim.normalized : Vector2.up) * 2.5f;
                    var d = behind - e.p; if (d.sqrMagnitude > 0.1f && dist < 9f) target = d.normalized;
                    if (dist < 3f && e.spawnT >= 1) { e.aiState = 1; e.aiT = 0; e.cloaked = false; Sfx.Hiss(0.8f); }
                    return false;
                }
                case 1:   // de-cloak + blades glow
                    e.aiT += dt; spd = 0; e.flash = 1;
                    if (e.aiT >= 0.5f)
                    {
                        var fwd = (pl.p - e.p).normalized;
                        if (dist < 2.6f && pl.dashT <= 0) G.HurtPlayer(U(e, 20));
                        if (!Sim) Fx.I.Sparks(MazeMap.ToWorld(e.p + fwd, 1f), 8, new Color(0.3f, 0.9f, 1f), 4, 0.3f, 0.08f, 2);
                        e.aiState = 2; e.aiT = 0;
                    }
                    return true;
                default:   // visible 3 s, then gone again
                    e.aiT += dt; e.cloaked = false;
                    if (e.aiT >= 3f) { e.aiState = 0; e.revealT = 0; }
                    return false;
            }
        }
        public static void OnHit(Enemy e) { if (e.type.ai == "stalker") { e.revealT = 3f; e.cloaked = false; } }

        // SPORE POD: rooted; every 4 s a spore arcs at you (green circle 1.6 s) bursting for 5 HP + a 3 s cloud
        // spore clouds (4 m, 8 s): enemies inside are hidden from auto-fire unless the hero is in the same cloud
        public static readonly List<(Vector2 p, float t)> Clouds = new();
        public static bool InCloud(Vector2 p) { foreach (var c in Clouds) if ((p - c.p).sqrMagnitude < 16f) return true; return false; }
        static void SporePod(Enemy e, Player pl, float dist, float dt)
        {
            if ((e.revealT -= dt) <= 0)
            {   // the puff, every 15 s
                e.revealT = 15f;
                Clouds.Add((e.p, 8f));
                if (!Sim && VfxKit.I != null) for (int k = 0; k < 10; k++) VfxKit.I.Gas(MazeMap.ToWorld(e.p, 0f), 3.4f, 0.1f);   // the puff (the cloud keeps itself going in TickGlobal)
            }
            if ((e.aiCd -= dt) > 0 || e.spawnT < 1 || dist > 12) return;
            e.aiCd = 4f; var at = pl.p + pl.vel * 0.8f; float dmg = U(e, 5);
            Tele.Circle(at, 2f, 1.6f, new Color(0.66f, 0.79f, 0.29f, 0.7f), () =>
            {
                if ((G.player.p - at).magnitude < 2f) G.HurtPlayer(dmg);
                AcidPuddle(at, 2f, 3f, 2f);
            });
        }

        // SPLITTER: dies into two of the next stage (large -> medium -> small)
        public static void Split(Swarm s, Enemy e)
        {
            string next = e.type.name == "splitter" ? "splitter_m" : e.type.name == "splitter_m" ? "splitter_s" : null;
            if (next == null || e.frozen > 0) return;   // frozen ones shatter
            int ti = s.TypeByName(next); if (ti < 0) return;
            for (int i = 0; i < 2; i++) splits.Add((e.p, ti));   // spawned next frame (kills happen inside enemy loops)
        }
        static readonly List<(Vector2 p, int ti)> splits = new();
        // the SPLITTING modifier: small weak copies (no modifiers)
        static readonly List<(Vector2 p, int ti, float size, float hp)> copies = new();
        public static void QueueCopies(Enemy e, int n, float size, float hp) { for (int i = 0; i < n; i++) copies.Add((e.p, e.typeIndex, size, hp * e.maxHp)); }
        public static void FlushSplits(Swarm s)
        {
            foreach (var (p, ti) in splits)
            {
                var d = Random.insideUnitCircle.normalized;
                var c = s.Spawn(p + d * 0.5f, Mathf.Max(1, G.wave), ti); c.p = p + d * 0.5f; c.k = d * 5f; c.spawnT = 0.7f;
            }
            splits.Clear();
            foreach (var (p, ti, size, hp) in copies)
            {
                var d = Random.insideUnitCircle.normalized; var c = s.Spawn(p + d * 0.4f, Mathf.Max(1, G.wave), ti);
                c.p = p + d * 0.4f; c.scale *= Mathf.Max(0.5f, size * 2f); c.r *= 0.6f; c.hp = c.maxHp = Mathf.Max(1, hp); c.k = d * 4f; c.spawnT = 0.7f;
            }
            copies.Clear();
        }
    }
}
