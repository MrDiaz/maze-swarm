using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Zone bosses and mini-bosses from data (Resources/Design/bosses.json = zone-bosses-1/2 + the enemies.json bosses).
    // Phases switch on HP; each attack runs on its interval (or once on a phase start / at an HP mark) and is played
    // from its telegraph: a lane charges or lashes, circles rain globs / strikes, rings burst around the boss (or
    // erupt under the hero), cones sweep in front, lines cut across; spawns call adds. mapRules / effects that need a
    // mechanic not built yet are skipped (the boss still plays its attacks). HP in RH, damage in player HP.
    public class BossBrain
    {
        static List<object> all;
        public static Dictionary<string, object> Find(string name)
        {
            if (all == null)
            {
                var ta = Resources.Load<TextAsset>("Design/bosses");
                all = ta != null && Json.Parse(ta.text) is Dictionary<string, object> d && d["bosses"] is List<object> l ? l : new List<object>();
            }
            string k = Norm(name);
            foreach (var o in all) if (o is Dictionary<string, object> b && (Norm(S(b, "name")) == k || Norm(S(b, "id")) == k)) return b;
            // "Mire Matriarch (the Acid Matriarch ...)" style names
            foreach (var o in all) if (o is Dictionary<string, object> b && k.StartsWith(Norm(S(b, "name")))) return b;
            return null;
        }
        static string Norm(string s) => (s ?? "").ToLowerInvariant().Replace("_", " ").Replace("-", " ").Trim();

        // spawn a data boss: the base model's stand-in type, spec HP / size / speed / contact, its brain
        public static Enemy Spawn(Game g, string name, Vector2 at, bool mini)
        {
            var b = Find(name); if (b == null) return null;
            var look = b.TryGetValue("look", out var lo) ? lo as Dictionary<string, object> : null;
            string bm = look != null ? S(look, "baseModel") : "bloater";
            string tn = bm == "leaper" ? "boss_leaper" : bm == "drone" ? "boss_drone" : bm == "iron_vanguard" ? "boss_vanguard" : "boss_bloater";
            int ti = g.swarm.TypeByName(tn); if (ti < 0) return null;
            var e = g.swarm.Spawn(at, g.wave, ti); e.p = at;
            float rh = ZoneCells.RH(g);
            e.hp = e.maxHp = F(b, "hp", 100) * rh;
            float lookScale = look != null ? F(look, "scale", 1.5f) : 1.5f;
            e.scale = g.cfg.enemyTypes[ti].scale * lookScale * (bm == "carapace-look" ? 0.75f : 1f) * (bm == "bloater" || bm == "carapace-look" ? 0.45f : 1f);
            e.r = Mathf.Max(0.5f, F(b, "size", 1.5f) * 0.8f);
            e.speed = g.cfg.enemy.speed * F(b, "speed", 0.7f);
            e.damage = e.damage / Mathf.Max(0.01f, g.cfg.enemyTypes[ti].damage) * F(b, "contactDamage", 15) / 8f;
            e.boss = !mini; e.elite = mini;
            e.brain = new BossBrain(g, e, b);
            g.hud.ShowBanner(S(b, "name").ToUpperInvariant());
            return e;
        }

        readonly Game g; readonly Enemy e; readonly Dictionary<string, object> def;
        readonly List<Dictionary<string, object>> phases = new();
        int phase = -1; readonly Dictionary<string, float> cd = new(); readonly HashSet<string> once = new();
        public float front; float baseSpeed; float stun;
        // a committed movement (charge / pounce): direction, distance left, chain count left, damage
        Vector2 moveDir; float moveLeft, moveSpeed, moveDmg, windup; int chainLeft; bool moveHit; string moveId;

        BossBrain(Game g, Enemy e, Dictionary<string, object> def)
        {
            this.g = g; this.e = e; this.def = def; baseSpeed = e.speed;
            if (def.TryGetValue("phases", out var ps) && ps is List<object> l) foreach (var p in l) if (p is Dictionary<string, object> d) phases.Add(d);
            phases.Sort((a, b) => F(b, "fromPct", 1).CompareTo(F(a, "fromPct", 1)));
            if (phases.Count == 0 && def.TryGetValue("attacks", out var at)) phases.Add(new Dictionary<string, object> { { "fromPct", 1.0 }, { "attacks", at } });   // single-phase mini-bosses
            if (def.TryGetValue("frontArmour", out var fa)) front = fa is Dictionary<string, object> fd ? F(fd, "damageTaken", 0.15f) : ToF(fa);
        }

        float introT; public bool Invulnerable => introT > 0;
        public string Name => S(def, "name");
        public float IntroLeft => introT;
        // the HP marks where phases change (for the bar's notches)
        public List<float> PhaseMarks { get { var l = new List<float>(); foreach (var p in phases) { float f = F(p, "fromPct", 1); if (f < 0.999f) l.Add(f); } return l; } }
        public void Intro(float s) { introT = s; stun = s; }
        public float FrontMul(Vector2 bulletDir)
        {
            if (introT > 0) return 0f;   // the intro: untouchable
            if (haulT > 0) return 1.5f;   // hauling a crate: +50%
            if (front <= 0 || stun > 0) return 1f;
            var fwd = new Vector2(Mathf.Sin(e.yaw), Mathf.Cos(e.yaw));
            return Vector2.Dot(fwd, -bulletDir) > 0.5f ? front : 1f;
        }

        // true = it moved itself this frame
        public bool Tick(Player pl, float dist, float dt, ref float spd, ref Vector2 target)
        {
            float frac = e.hp / Mathf.Max(1, e.maxHp);
            int want = 0; for (int i = 0; i < phases.Count; i++) if (frac <= F(phases[i], "fromPct", 1) + 1e-4f) want = i;
            if (want != phase) EnterPhase(want);
            if (stun > 0) { stun -= dt; spd = 0; return true; }
            if (haulT > 0) { haulT -= dt; spd = 0; return true; }
            if (windup > 0) { windup -= dt; spd = 0; e.yaw = Mathf.Atan2(moveDir.x, moveDir.y); if (windup <= 0) moveLeft = moveLeft > 0 ? moveLeft : 0; return true; }
            if (moveLeft > 0) return Move(pl, dt);
            var ph = phases[phase];
            if (ph.TryGetValue("attacks", out var al) && al is List<object> atks)
                foreach (var o in atks)
                {
                    if (!(o is Dictionary<string, object> a)) continue;
                    string id = S(a, "id");
                    if (a.ContainsKey("atPct")) { if (frac <= F(a, "atPct", 0) && once.Add(phase + id + "pct")) Attack(a, pl); continue; }
                    string trig = S(a, "trigger");
                    if (trig.Contains("phase start") || trig.Contains("entering phase") || trig == "phaseStart") continue;   // ran in EnterPhase
                    if (!a.ContainsKey("interval")) continue;
                    string key = phase + id;
                    if (!cd.TryGetValue(key, out float c)) c = F(a, "interval", 8) * Random.Range(0.4f, 0.8f);
                    c -= dt; cd[key] = c;
                    if (c <= 0 && dist < 30f && e.spawnT >= 1) { cd[key] = F(a, "interval", 8); Attack(a, pl); if (moveLeft > 0 || windup > 0) return true; }
                }
            // keep-distance bosses hold their band
            if (def.TryGetValue("keepDistance", out var kd) && kd is List<object> band && band.Count == 2)
            {
                float near = ToF(band[0]), far = ToF(band[1]);
                if (dist < near) target = (e.p - pl.p).normalized; else if (dist < far) spd = 0;
            }
            return false;
        }

        void EnterPhase(int i)
        {
            bool first = phase < 0; phase = i; var ph = phases[i];
            e.speed = ph.ContainsKey("speed") ? g.cfg.enemy.speed * F(ph, "speed", 0.7f) : baseSpeed;
            if (!first) { g.hud.ShowBanner($"{S(def, "name").ToUpperInvariant()}  //  PHASE {i + 1}"); Feel.Shake(0.2f, 0.4f); Feel.Dip(0.3f, 0.4f, exempt: true); Feel.Flash(Color.white, 0.25f, 0.1f); Haptics.Heavy(); stun = 0.8f; }
            if (ph.TryGetValue("transition", out var tr) && tr is Dictionary<string, object> td && td.TryGetValue("waves", out var wv) && wv is Dictionary<string, object> waves) SpawnAdds(waves, e.p, 6f);
            if (ph.TryGetValue("spawn", out var sp) && sp is Dictionary<string, object> sd) SpawnAdds(sd, e.p, 3f);
            if (ph.TryGetValue("attacks", out var al) && al is List<object> atks)
                foreach (var o in atks) if (o is Dictionary<string, object> a) { var t = S(a, "trigger"); if (t.Contains("phase start") || t.Contains("entering phase") || t == "phaseStart") Attack(a, g.player); }
        }

        // ---- one attack, played from its telegraph
        void Attack(Dictionary<string, object> a, Player pl)
        {
            string id = S(a, "id"), lid = id.ToLowerInvariant();
            var tel = a.TryGetValue("telegraph", out var to) ? to as Dictionary<string, object> : null;
            string shape = tel != null ? S(tel, "shape") : "none";
            var size = tel != null && tel.TryGetValue("size", out var so) ? so as Dictionary<string, object> : null;
            float grow = tel != null ? F(tel, "growSec", 1f) + F(tel, "lockSec", 0f) : 1f;
            float dmg = Hp(F(a, "damage", 0));
            Color col = ColorOf(tel != null ? S(tel, "color") : "red");
            var toHero = (pl.p - e.p); var dir = toHero.sqrMagnitude > 0.01f ? toHero.normalized : Vector2.up;
            var spawn = a.TryGetValue("spawn", out var sp) ? sp as Dictionary<string, object> : null;

            switch (shape)
            {
                case "lane":
                {
                    float len = size != null ? F(size, "length", 12) : 12, w = size != null ? F(size, "width", 2) : 2;
                    bool moves = lid.Contains("pounce") || lid.Contains("charge") || lid.Contains("leap");
                    EnemyAI.Tele.Lane(e.p, dir, len, w, grow, col, null);
                    if (moves)
                    {
                        moveDir = dir; moveLeft = len; windup = grow; moveSpeed = Mathf.Max(9f, len / 0.8f); moveDmg = dmg > 0 ? dmg : Hp(25); moveHit = false; moveId = id;
                        chainLeft = (int)F(a, "charges", 1) - 1;
                    }
                    else
                    {   // (a hook: effect.hitHero.pullM drags the hero toward the boss; a crate cell in the way is hauled instead)
                        var from = e.p; float pull = 0;
                        if (a.TryGetValue("effect", out var ef) && ef is Dictionary<string, object> efd && efd.TryGetValue("hitHero", out var hh) && hh is Dictionary<string, object> hhd) pull = F(hhd, "pullM", 0);
                        bool haul = a.TryGetValue("effect", out var ef2) && ef2 is Dictionary<string, object> efd2 && efd2.ContainsKey("hitContainerOrC");
                        Later(grow, () => LaneHit(from, dir, len, w, dmg, pull, haul));
                    }
                    break;
                }
                case "line":
                {
                    float len = size != null ? F(size, "length", 22) : 22, w = size != null ? Mathf.Max(0.6f, F(size, "width", 0.6f)) : 0.6f;
                    int n = (int)F(a, "count", 1); float fan = size != null ? F(size, "fanDeg", 10) : 10;
                    for (int k = 0; k < n; k++)
                    {
                        float ang = (k - (n - 1) * 0.5f) * fan * Mathf.Deg2Rad;
                        var d2 = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
                        var from = e.p - d2 * (lid.Contains("quarantine") || lid.Contains("cable") ? len * 0.5f : 0);
                        EnemyAI.Tele.Lane(from, d2, len, w, grow, col, null);
                        if (dmg > 0) Later(grow, () => LaneHit(from, d2, len, w, dmg));
                    }
                    break;
                }
                case "circle":
                {
                    float r = size != null ? F(size, "radius", 1.5f) : 1.5f;
                    int n = Mathf.Max(1, (int)Mathf.Max(F(a, "globs", 0), F(a, "strikes", 0)));
                    var puddle = a.TryGetValue("puddle", out var pu) ? pu as Dictionary<string, object> : null;
                    bool atBoss = spawn != null && dmg <= 0;   // a call: purple circle where the adds come out
                    for (int k = 0; k < n; k++)
                    {
                        var at = atBoss ? e.p + Random.insideUnitCircle * 2f : pl.p + pl.vel * 0.5f + (k == 0 ? Vector2.zero : Random.insideUnitCircle * 4f);
                        var c = at;
                        EnemyAI.Tele.Circle(c, r, grow + k * F(a, "staggerSec", 0.12f), col, () =>
                        {
                            if (dmg > 0 && (g.player.p - c).magnitude < r + g.cfg.player.radius && g.player.dashT <= 0) g.HurtPlayer(dmg);
                            if (puddle != null) EnemyAI.AcidPuddle(c, F(puddle, "radius", r), F(puddle, "durationSec", 4), F(puddle, "dps", 6));
                            if (spawn != null && k == 0) SpawnAdds(spawn, c, 1.5f);
                            if (!Game.SimRun && dmg > 0) Fx.I.Debris(MazeMap.ToWorld(c, 0.3f), 8, col, 4, 0.5f, 0.12f, 4);
                        });
                    }
                    if (lid.Contains("pounce"))
                    {   // a sky pounce: land on the circle
                        moveDir = ((pl.p) - e.p).normalized; moveLeft = (pl.p - e.p).magnitude; windup = grow; moveSpeed = Mathf.Max(12f, moveLeft / 0.6f); moveDmg = 0; moveHit = true; moveId = id; chainLeft = 0;
                    }
                    break;
                }
                case "ring":
                {
                    float r = size != null ? F(size, "radius", 3) : 3;
                    bool under = lid.Contains("surface") || lid.Contains("erupt") || lid.Contains("breach") || lid.Contains("dive");
                    var at = under ? pl.p : e.p; int rings = Mathf.Max(1, (int)F(a, "rings", 1)); float step = size != null ? F(size, "step", 3) : 3;
                    for (int k = 0; k < rings; k++)
                    {
                        float rr = r + k * step, delay = grow + k * 0.5f; var c = at;
                        EnemyAI.Tele.Circle(c, rr, delay, col, () =>
                        {
                            if (under) { e.p = c; e.under = false; }
                            if (dmg > 0) { float d = (g.player.p - c).magnitude; if (d < rr + g.cfg.player.radius && (rings == 1 || d > rr - step) && g.player.dashT <= 0) g.HurtPlayer(dmg); }
                            if (spawn != null && k == 0) SpawnAdds(spawn, c, 2f);
                        }, ring: true);
                    }
                    if (under) { e.under = true; Later(grow, () => e.under = false); }
                    break;
                }
                case "cone":
                {
                    float ang = size != null ? F(size, "angleDeg", 60) : 60, r = size != null ? F(size, "radius", 6) : 6;
                    bool behind = lid.Contains("tail") || lid.Contains("sweep") && def.ContainsKey("arena");
                    var cdir = behind ? -dir : dir; float dur = F(a, "durationSec", F(a, "sweepSec", 0));
                    var cc = e.p;
                    EnemyAI.Tele.Lane(cc, cdir, r, 2f * r * Mathf.Tan(Mathf.Min(70, ang * 0.5f) * Mathf.Deg2Rad), grow, col, null);
                    int ticks = dur > 0 ? Mathf.Clamp(Mathf.RoundToInt(dur / 0.5f), 1, 6) : 1;
                    for (int k = 0; k < ticks; k++)
                        Later(grow + k * 0.5f, () =>
                        {
                            var d = g.player.p - cc; if (d.magnitude > r + g.cfg.player.radius || g.player.dashT > 0) return;
                            if (Vector2.Angle(cdir, d) <= ang * 0.5f) g.HurtPlayer(dmg > 0 ? dmg : Hp(15));
                        });
                    stun = grow * 0.6f;   // plants while it winds up
                    break;
                }
                default:
                    if (spawn != null) SpawnAdds(spawn, e.p, 3f);
                    if (lid.Contains("acidfield"))
                        for (int k = 0; k < 6; k++) { var c = pl.p + Random.insideUnitCircle * 9f; EnemyAI.Tele.Circle(c, 2.2f, grow, ColorOf("green"), () => EnemyAI.AcidPuddle(c, 2.2f, F(a, "durationSec", 6), F(a, "dps", 12))); }
                    break;
            }
        }

        bool Move(Player pl, float dt)
        {
            float step = moveSpeed * dt; var before = e.p;
            e.p += moveDir * Mathf.Min(step, moveLeft); moveLeft -= step;
            MazeMap.CollideGrid(ref e.p, e.r);
            bool wall = (e.p - before).sqrMagnitude < step * step * 0.2f;
            e.yaw = Mathf.Atan2(moveDir.x, moveDir.y);
            if (!moveHit && (pl.p - e.p).magnitude < e.r + g.cfg.player.radius + 0.3f && pl.dashT <= 0) { moveHit = true; g.HurtPlayer(moveDmg); pl.vel += moveDir * 10f; g.AddShake(0.35f); Haptics.Heavy(); }
            foreach (var o in g.swarm.enemies) if (o != e && !o.dead && (o.p - e.p).sqrMagnitude < (o.r + e.r) * (o.r + e.r)) o.k += moveDir * 7f;
            if (wall) { moveLeft = 0; stun = 2.5f; g.AddShake(0.3f); if (!Game.SimRun) Fx.I.Debris(MazeMap.ToWorld(e.p, 1f), 14, new Color(0.5f, 0.5f, 0.5f), 5, 0.8f, 0.2f, 5); g.hud.ShowBanner("STUNNED"); }
            if (moveLeft <= 0)
            {
                if (!Game.SimRun) Fx.I.Smoke(MazeMap.ToWorld(e.p, 0.1f), 6, new Color(0.8f, 0.8f, 0.82f), 2, 0.5f, 0.3f, 1);
                if (chainLeft > 0 && stun <= 0)
                {   // the next link of a chain: a fresh arrow 0.6 s later
                    chainLeft--; var dir = (pl.p - e.p).normalized; moveDir = dir; moveLeft = 12f; windup = 0.6f; moveHit = false;
                    EnemyAI.Tele.Lane(e.p, dir, 12f, 2.4f, 0.6f, ColorOf("red"), null);
                }
                else if (stun <= 0) stun = 0.6f;
            }
            return true;
        }

        void LaneHit(Vector2 from, Vector2 dir, float len, float w, float dmg, float pull = 0, bool haul = false)
        {
            var rel = g.player.p - from; float along = Vector2.Dot(rel, dir), side = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
            if (haul)
            {   // the hook catches the first crate cell on the lane before the hero: 2 s hauling it, +50% damage taken
                float reach = along > 0 && side < w * 0.5f + g.cfg.player.radius ? Mathf.Min(along, len) : len;
                for (float s = 1f; s < reach; s += 0.5f)
                {
                    var q = from + dir * s; int x = Mathf.FloorToInt(q.x / MazeMap.C), z = Mathf.FloorToInt(q.y / MazeMap.C);
                    if (x < 0 || z < 0 || x >= MazeMap.W || z >= MazeMap.H) break;
                    if (MazeMap.grid[z, x] == 'C') { haulT = 2f; g.hud.ShowHint("HOOKED A CRATE: HIT IT NOW"); if (Game.SimRun) Debug.Log("Maze hook: hauled a crate"); return; }
                    if (MazeMap.grid[z, x] == '#' || MazeMap.grid[z, x] == 'B') break;
                }
            }
            if (dmg > 0 && along > 0 && along < len && side < w * 0.5f + g.cfg.player.radius && g.player.dashT <= 0)
            {
                g.HurtPlayer(dmg);
                if (pull > 0)
                {   // dragged toward the boss (walls stop it)
                    if (Game.SimRun) Debug.Log("Maze hook: pulled the hero");
                    var to = (from - g.player.p); float m = Mathf.Min(pull, Mathf.Max(0, to.magnitude - 1.8f)); var d = to.normalized;
                    for (float s = 0; s < m; s += 0.4f) { var p = g.player.p + d * Mathf.Min(0.4f, m - s); MazeMap.CollideGrid(ref p, g.cfg.player.radius); g.player.p = p; }
                }
            }
        }
        float haulT;   // (hauling a hooked crate: rooted, +50% damage taken)

        void SpawnAdds(Dictionary<string, object> sp, Vector2 at, float spread)
        {
            foreach (var kv in sp)
            {
                string n = kv.Key == "drone_mini" ? "mini" : kv.Key == "leaper_pup" ? "leaper" : kv.Key == "gloom_decoy" ? "stalker" : kv.Key;
                int ti = g.swarm.TypeByName(n), count = Mathf.Min(16, (int)ToF(kv.Value)); if (ti < 0) continue;
                for (int i = 0; i < count; i++) { var p = at + Random.insideUnitCircle * spread; var a = g.swarm.Spawn(p, g.wave, ti); a.p = p; a.spawnT = 0.5f; }
            }
        }

        readonly List<(float t, System.Action a)> later = new(); float clock;
        void Later(float s, System.Action a) => later.Add((clock + s, a));
        public void TickLater(float dt)
        {
            clock += dt; introT -= dt;
            for (int i = later.Count - 1; i >= 0; i--) if (clock >= later[i].t) { var a = later[i].a; later.RemoveAt(i); if (!e.dead) a(); }
        }

        float Hp(float spec) => spec * g.cfg.player.hp / 100f * Mathf.Max(0.5f, g.swarm.DmgScale / 1.2f);
        static Color ColorOf(string c) => c switch
        {
            "yellow" => new Color(1f, 0.85f, 0.15f, 0.8f), "green" => new Color(0.5f, 1f, 0.2f, 0.8f), "purple" => new Color(0.7f, 0.3f, 1f, 0.8f),
            "white" => new Color(1f, 1f, 1f, 0.8f), _ => new Color(1f, 0.15f, 0.1f, 0.85f),
        };
        static float ToF(object o) => o switch { double d => (float)d, long l => l, int i => i, _ => 0 };
        static float F(Dictionary<string, object> d, string k, float def) => d != null && d.TryGetValue(k, out var v) && (v is double || v is long || v is int) ? ToF(v) : def;
        static string S(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is string s ? s : "";
    }
}
