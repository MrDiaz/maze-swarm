using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Floor power-ups (Design/powerups.json): pickups on the floor (timed drops on PURGE / SURVIVE / HOLD floors,
    // enemy drops: 1.5% regular, 0.5% Drones, every elite, 2 from a boss), 15 effects with durations that stack
    // (same one again: +50% time, capped at 2x). Placeholder looks: a spinning cube over a coloured ring.
    public static class PowerUps
    {
        public class Def { public string id, name; public int rarity; public float dur; public Color col; }
        public static readonly Def[] All =
        {
            D("overcharge", "OVERCHARGE", 0, 8, 0xfff3a0), D("cryo_rounds", "CRYO ROUNDS", 0, 10, 0x5fd8ff), D("incendiary", "INCENDIARY ROUNDS", 0, 10, 0xff8a2a),
            D("bulwark", "BULWARK", 1, 12, 0x9fd0ff), D("med_gel", "MED GEL", 0, 0, 0x55e07a), D("magnet", "BIOMASS MAGNET", 0, 15, 0xa066ff),
            D("orbital_strike", "ORBITAL STRIKE", 1, 0, 0xff4040), D("sentry_drone", "SENTRY DRONE", 1, 15, 0x3d8bff), D("adrenal", "ADRENAL SURGE", 0, 8, 0xff6a3a),
            D("tesla_coil", "TESLA COIL", 1, 10, 0x4ab8ff), D("endless_mag", "ENDLESS MAG", 0, 10, 0xffc83a), D("decoy", "DECOY HOLOGRAM", 1, 6, 0x40f0e0),
            D("armour_breaker", "ARMOUR BREAKER", 1, 12, 0xc0c8d0), D("stasis_field", "STASIS FIELD", 2, 5, 0xa040ff), D("purge_charge", "PURGE CHARGE", 2, 0, 0xfff0c0),
        };
        static Def D(string id, string n, int r, float d, int hex) => new Def { id = id, name = n, rarity = r, dur = d, col = new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f) };
        public static Def Get(string id) { foreach (var d in All) if (d.id == id) return d; return null; }

        static Game g;
        static readonly Dictionary<string, float> active = new();
        class Pick { public Def d; public Vector2 p; public float t; public Transform tr, ring; }
        static readonly List<Pick> picks = new();
        static float timedT, teslaT, sentryT, absorb; static string lastTimed;
        static Transform sentry; static Vector2 decoyAt;
        public static bool Has(string id) => active.TryGetValue(id, out var t) && t > 0;
        public static float Left(string id) => active.TryGetValue(id, out var t) ? t : 0;
        public static IEnumerable<KeyValuePair<string, float>> Active => active;

        public static void Begin(Game game, bool timedDrops)
        {
            g = game; Clear(); timedT = timedDrops ? 20f : 1e9f;
        }
        public static void Clear()
        {
            foreach (var p in picks) if (p.tr != null) Object.Destroy(p.tr.gameObject);
            picks.Clear();
            if (sentry != null) Object.Destroy(sentry.gameObject); sentry = null;
        }
        public static void EndRun() { Clear(); active.Clear(); absorb = 0; }

        // ---- multipliers the game reads
        public static float RateMul => Has("overcharge") ? 1.8f : 1f;
        public static float MoveMul => Has("adrenal") ? 1.3f : 1f;
        public static float DashCdMul => Has("adrenal") ? 0.5f : 1f;
        public static bool NoAmmo => Has("endless_mag");
        public static bool IgnoreFront => Has("armour_breaker");
        public static float HitMul(Enemy e) => Has("armour_breaker") && (e.elite || e.boss) ? 1.5f : 1f;
        public static bool DecoyOn(out Vector2 at) { at = decoyAt; return Has("decoy"); }
        public static float EnemySpeedMul(Enemy e) => (ZoneMods.On[ZoneMods.Frenzy] ? 1.25f : 1f) * (e.boss && Meta.Relic(12) ? 1f - Meta.RelicV : 1f) * (e.slowT > 0 ? 0.6f : 1f) * (Has("stasis_field") && g != null && (e.p - g.player.p).sqrMagnitude < 100f ? (e.boss ? 0.6f : 0.3f) : 1f);
        // Bulwark soaks damage first
        public static float Absorb(float dmg)
        {
            if (!Has("bulwark") || absorb <= 0) return dmg;
            float a = Mathf.Min(absorb, dmg); absorb -= a; return dmg - a;
        }

        // ---- a bullet hit
        public static void OnHit(Enemy e)
        {
            if (Has("cryo_rounds"))
            {
                e.slowT = e.boss ? 0.5f : 2f;
                if (e.cryoWin <= 0) e.cryoHits = 0; e.cryoWin = 2f; e.cryoHits++;
                if (e.cryoHits >= 4 && !e.boss && e.frozen <= 0) { e.frozen = 1.5f; e.cryoHits = 0; }
            }
            if (Has("incendiary")) e.burnT = 3f;
        }
        public static void OnKill(Enemy e)
        {
            if (g == null || !g.run) return;
            if (e.burnT > 0 && Has("incendiary")) foreach (var o in g.swarm.enemies) if (!o.dead && (o.p - e.p).sqrMagnitude < 9f) o.burnT = Mathf.Max(o.burnT, 2f);
            float chance = (e.boss ? 1f : e.elite ? 1f : e.type.name == "grunt" ? 0.005f : 0.015f) * (1f + 0.5f * g.mods["scavenger"]);
            if (e.type.nest || e.egg) chance = 0;
            if (Random.value < chance) Drop(e.boss ? Roll(new[] { 0f, 0.6f, 0.4f }) : e.elite ? Roll(new[] { 0.45f, 0.45f, 0.1f }) : Roll(new[] { 0.8f, 0.19f, 0.01f }), e.p);
            if (e.boss) Drop(Roll(new[] { 0f, 0.6f, 0.4f }), e.p + Vector2.right * 1.5f);
        }

        static Def Roll(float[] w)
        {
            float r = Random.value; int rar = r < w[0] ? 0 : r < w[0] + w[1] ? 1 : 2;
            var pool = new List<Def>(); foreach (var d in All) if (d.rarity == rar && d.id != lastTimed) pool.Add(d);
            if (g != null && g.player.hp < g.player.maxHp * 0.4f) pool.Add(Get("med_gel"));   // low HP: med gel x3 weight
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : All[0];
        }

        public static void OrbitalAt(Game game) { g = game; Activate(Get("orbital_strike"), quiet: true); }
        public static bool DecoyNoBoom;   // the Scout's skill decoy (and its dash afterimage) end quietly unless the skill says otherwise
        public static void DecoyFor(Vector2 at, float s, bool boom = true) { decoyAt = at; active["decoy"] = Mathf.Max(Left("decoy"), s); DecoyNoBoom = !boom; }
        public static void Drop(Def d, Vector2 at)
        {
            if (g == null || d == null) return;
            var pk = new Pick { d = d, p = at, t = 12f };
            if (!Game.SimRun)
            {
                var root = new GameObject("PowerUp_" + d.id).transform; root.position = MazeMap.ToWorld(at, 0);
                if (FirstCryoFx.Active) { pk.tr = root; pk.ring = FirstCryoPickupView.Build(root, d); }
                else
                {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(root, false); cube.transform.localPosition = new Vector3(0, 0.9f, 0); cube.transform.localScale = Vector3.one * 0.55f;
                cube.GetComponent<MeshRenderer>().sharedMaterial = Mats.Glow(d.col);
                var ring = new GameObject("Ring"); ring.transform.SetParent(root, false); ring.transform.localPosition = new Vector3(0, 0.06f, 0); ring.transform.localScale = Vector3.one * 1.2f;
                ring.AddComponent<MeshFilter>().sharedMesh = Fx.Ring; var mr = ring.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Glow(d.col * new Color(1, 1, 1, 0.8f)); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pk.tr = root; pk.ring = cube.transform;
                }
            }
            picks.Add(pk);
        }

        public static void Tick(float dt)
        {
            if (g == null) return;
            // timed floor drops
            if ((timedT -= dt) <= 0 && picks.Count == 0)
            {
                timedT = 35f; var d = Roll(new[] { 0.7f, 0.27f, 0.03f }); lastTimed = d.id;
                for (int k = 0; k < 20; k++)
                {
                    var at = g.player.p + Random.insideUnitCircle.normalized * Random.Range(6f, 12f);
                    int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C);
                    if (MazeMap.Walkable(x, z)) { Drop(d, MazeMap.Center(x, z)); break; }
                }
            }
            // pickups
            float pickR = Has("magnet") ? 2.4f : 1.2f;
            for (int i = picks.Count - 1; i >= 0; i--)
            {
                var p = picks[i]; p.t -= dt;
                if (Has("magnet")) p.p = Vector2.MoveTowards(p.p, g.player.p, dt * 12f);
                if (p.tr != null)
                {
                    p.tr.position = MazeMap.ToWorld(p.p, 0); p.ring.Rotate(0, 120 * dt, 0, Space.World);
                    p.tr.gameObject.SetActive(p.t > 3f || Mathf.PingPong(p.t * 6, 1) > 0.4f);
                }
                if ((p.p - g.player.p).magnitude < pickR) { Activate(p.d); if (p.tr != null) Object.Destroy(p.tr.gameObject); picks.RemoveAt(i); continue; }
                if (p.t <= 0) { if (p.tr != null) Object.Destroy(p.tr.gameObject); picks.RemoveAt(i); }
            }
            // running effects
            var keys = new List<string>(active.Keys);
            foreach (var k in keys)
            {
                float before = active[k]; active[k] = before - dt;
                if (before > 0 && active[k] <= 0) Expire(k);
            }
            float rh = ZoneCells.RH(g);
            if (Has("tesla_coil") && (teslaT -= dt) <= 0)
            {
                teslaT = 0.5f; Enemy cur = Nearest(g.player.p, 6f, null); var from = g.player.p; var hit = new List<Enemy>();
                for (int c = 0; c < 3 && cur != null; c++)
                {
                    hit.Add(cur); cur.hp -= 2 * rh; cur.flash = 1;
                    if (!Game.SimRun) Fx.I.Sparks(MazeMap.ToWorld(cur.p, 0.9f), 5, new Color(0.3f, 0.75f, 1f), 4, 0.25f, 0.06f, 2);
                    if (cur.hp <= 0) g.KillEnemy(cur);
                    from = cur.p; cur = Nearest(from, 3f, hit);
                }
            }
            if (Has("sentry_drone"))
            {
                if (sentry == null && !Game.SimRun) { var s = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(s.GetComponent<Collider>()); s.transform.localScale = Vector3.one * 0.4f; s.GetComponent<MeshRenderer>().sharedMaterial = Mats.Glow(new Color(0.24f, 0.55f, 1f)); sentry = s.transform; }
                var sp = g.player.p + new Vector2(Mathf.Cos(Time.time * 2), Mathf.Sin(Time.time * 2)) * 1.5f;
                if (sentry != null) sentry.position = MazeMap.ToWorld(sp, 2f + Mathf.Sin(Time.time * 4) * 0.15f);
                if ((sentryT -= dt) <= 0)
                {
                    sentryT = 1f / 3f; var t = Nearest(sp, 10f, null);
                    if (t != null) { t.hp -= rh; t.flash = 1; if (t.hp <= 0) g.KillEnemy(t); if (!Game.SimRun) Fx.I.Sparks(MazeMap.ToWorld(t.p, 0.8f), 2, new Color(0.3f, 0.6f, 1f), 3, 0.2f, 0.05f, 1); }
                }
            }
            else if (sentry != null) { Object.Destroy(sentry.gameObject); sentry = null; }
            // burning / slowed enemies
            foreach (var e in g.swarm.enemies)
            {
                if (e.dead) continue;
                if (e.slowT > 0) e.slowT -= dt;
                if (e.cryoWin > 0) e.cryoWin -= dt;
                if (e.burnT > 0) { e.burnT -= dt; e.hp -= 1.5f * rh * dt; if (e.hp <= 0) g.KillEnemy(e); }
            }
        }

        static Enemy Nearest(Vector2 at, float r, List<Enemy> skip)
        {
            Enemy best = null; float bd = r * r;
            foreach (var e in g.swarm.enemies) { if (e.dead || e.Hidden || (skip != null && skip.Contains(e))) continue; float d = (e.p - at).sqrMagnitude; if (d < bd) { bd = d; best = e; } }
            return best;
        }

        static void Activate(Def d, bool quiet = false)
        {
            if (!quiet)
            {
                g.hud.ShowBanner(d.name); Haptics.Light();
                if (g.mods["power_surge"] > 0)
                {   // POWER SURGE: a 4 m shockwave, and everything running gets +2 s
                    float r0 = ZoneCells.RH(g);
                    foreach (var e in g.swarm.enemies) { var dv = e.p - g.player.p; if (!e.dead && dv.magnitude < 4f) { e.hp -= 6 * r0; e.k += dv.normalized * 6f; if (e.hp <= 0) g.KillEnemy(e); } }
                    foreach (var k in new List<string>(active.Keys)) if (active[k] > 0) active[k] += 2f;
                }
            }
            float rh = ZoneCells.RH(g);
            switch (d.id)
            {
                case "med_gel": g.player.hp = Mathf.Min(g.player.maxHp, g.player.hp + ZoneCells.Hp(g, 25)); return;
                case "orbital_strike":
                {
                    var t = Nearest(g.player.p, 18f, null); var at = t != null ? t.p : g.player.p + g.player.aim * 6f;
                    for (int k = 0; k < 3; k++)
                    {
                        var c = at + Random.insideUnitCircle * 1.5f;
                        EnemyAI.Tele.Circle(c, 3f, 1.5f + k * 0.4f, new Color(1f, 0.25f, 0.25f, 0.8f), () =>
                        {
                            foreach (var e in g.swarm.enemies) if (!e.dead && (e.p - c).magnitude < 3f + e.r) { e.hp -= (e.boss ? 6 : 12) * rh; e.flash = 1; if (e.hp <= 0) g.KillEnemy(e); }
                            if (!Game.SimRun) { Fx.I.Boom(MazeMap.ToWorld(c, 0.3f), 3f); g.AddShake(0.25f); }
                        }, ring: true);
                    }
                    return;
                }
                case "purge_charge":
                    EnemyAI.Tele.Circle(g.player.p, 20f, 1f, new Color(1f, 0.95f, 0.75f, 0.5f), () =>
                    {
                        foreach (var e in g.swarm.enemies)
                        {
                            if (e.dead) continue;
                            if (!e.elite && !e.boss && e.hp <= 6 * rh) { g.KillEnemy(e); continue; }
                            e.hp -= (e.boss ? 15 : 10) * rh; e.flash = 1; if (e.hp <= 0) g.KillEnemy(e);
                        }
                        g.AddShake(0.35f);
                    }, ring: true);
                    return;
                case "bulwark": absorb = ZoneCells.Hp(g, 40); break;
                case "decoy": decoyAt = g.player.p; break;
            }
            // stack: the same one again adds 50% of its time, capped at 2x
            float dur = d.dur * (1f + 0.3f * g.mods["scavenger"]);   // SCAVENGER: power-ups last longer
            float cur = Left(d.id); active[d.id] = cur > 0 ? Mathf.Min(dur * 2, cur + dur * 0.5f) : dur;
        }

        static void Expire(string id)
        {
            if (id == "bulwark")
            {   // end pulse: 8 RH in 3 m and a shove
                float rh = ZoneCells.RH(g);
                foreach (var e in g.swarm.enemies) { var d = e.p - g.player.p; if (!e.dead && d.magnitude < 3f) { e.hp -= 8 * rh; e.k += d.normalized * 6f; if (e.hp <= 0) g.KillEnemy(e); } }
                absorb = 0;
            }
            if (id == "decoy" && DecoyNoBoom) DecoyNoBoom = false;
            else if (id == "decoy")
            {
                float rh = ZoneCells.RH(g);
                foreach (var e in g.swarm.enemies) if (!e.dead && (e.p - decoyAt).magnitude < 3f) { e.hp -= 8 * rh; if (e.hp <= 0) g.KillEnemy(e); }
            }
        }

        // HUD: the running effects as one line
        public static string Line()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in active) if (kv.Value > 0) { var d = Get(kv.Key); sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(d.col)}>{d.name} {Mathf.CeilToInt(kv.Value)}</color>   "); }
            return sb.ToString();
        }
    }
}
