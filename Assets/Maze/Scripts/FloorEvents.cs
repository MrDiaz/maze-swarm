using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Maze
{
    // The floor event runner (Design/drop-zones-core-v2.json: event_triggers / event_actions). Each spec floor carries a
    // "script": a list of {when, do, params}. Triggers are polled every frame; an entry fires once (or on every repeat
    // for 'every'). Unknown triggers, actions and params are ignored (logged once), never errors.
    public class FloorEvents
    {
        class Entry { public Dictionary<string, object> when, prms; public string act; public bool fired; public float next = -1; public int times; }
        readonly List<Entry> list = new();
        readonly List<(float at, System.Action a)> later = new();
        Game g; float t; int lastWaveStart, lastWaveEnd; readonly Dictionary<string, int> doneCount = new();
        readonly List<Strike> schedules = new();
        static readonly HashSet<string> warned = new();

        // enemies the script brings in itself (once): the random spawns leave that much of the floor's budget to it
        public readonly Dictionary<string, int> Scripted = new();
        public void Begin(Game game, string script)
        {
            g = game; t = 0; list.Clear(); later.Clear(); doneCount.Clear(); schedules.Clear(); lastWaveStart = lastWaveEnd = 0; Scripted.Clear();
            if (string.IsNullOrEmpty(script)) return;
            if (!(Json.Parse(script) is List<object> arr)) { Debug.LogWarning("Maze events: bad script"); return; }
            foreach (var o in arr)
                if (o is Dictionary<string, object> d)
                {
                    var en = new Entry { when = d.TryGetValue("when", out var w) ? w as Dictionary<string, object> : null, act = d.TryGetValue("do", out var a) ? a as string : null, prms = d.TryGetValue("params", out var p) ? p as Dictionary<string, object> ?? new() : new() };
                    list.Add(en);
                    if (en.act == "SPAWN" && en.when != null && !en.when.ContainsKey("every")) { var n = S(en.prms, "enemy", ""); Scripted.TryGetValue(n, out int c); Scripted[n] = c + (int)F(en.prms, "count", 1); }
                }
        }

        // hooks from the run
        public void Wave(int n, bool start) { if (start) lastWaveStart = n; else lastWaveEnd = n; }
        public void Done(string kind) { doneCount.TryGetValue(kind, out int c); doneCount[kind] = c + 1; justDone = kind; Poll(); justDone = null; }
        string justDone;

        public void Tick(float dt)
        {
            if (g == null) return;
            t += dt;
            for (int i = later.Count - 1; i >= 0; i--) if (t >= later[i].at) { var a = later[i].a; later.RemoveAt(i); a(); }
            foreach (var s in schedules) s.Tick(dt);
            Poll();
        }

        void Poll()
        {
            foreach (var e in list)
            {
                if (e.when == null || e.act == null) continue;
                if (e.when.TryGetValue("every", out var ev))
                {
                    var d = ev as Dictionary<string, object>; float s = F(d, "s", 10), from = F(d, "from", s), until = F(d, "until", 1e9f); int max = (int)F(d, "times", 1e9f);
                    if (e.next < 0) e.next = from;
                    if (t >= e.next && t <= until && e.times < max) { e.next += s; e.times++; Fire(e); }
                    continue;
                }
                if (e.fired) continue;
                if (Met(e.when)) { e.fired = true; Fire(e); }
            }
        }

        bool Met(Dictionary<string, object> w)
        {
            foreach (var kv in w)
            {
                switch (kv.Key)
                {
                    case "start": break;
                    case "t": if (t < ToF(kv.Value)) return false; break;
                    case "progress": if (g.GoalProgress < ToF(kv.Value)) return false; break;
                    case "boss_hp": if (g.BossHpFrac > ToF(kv.Value)) return false; break;
                    case "hero_hp": if (g.player.hp / Mathf.Max(1, g.player.maxHp) > ToF(kv.Value)) return false; break;
                    case "wave":
                    {
                        var d = kv.Value as Dictionary<string, object>; int n = (int)F(d, "n", 1); bool st = S(d, "at", "start") == "start";
                        if ((st ? lastWaveStart : lastWaveEnd) < n) return false; break;
                    }
                    case "done":
                    {
                        var d = kv.Value as Dictionary<string, object>; string kind = S(d, "kind", "any");
                        doneCount.TryGetValue(kind, out int c);
                        if (d.ContainsKey("count")) { if (c < (int)F(d, "count", 1)) return false; }
                        else if (d.ContainsKey("index")) { if (c < (int)F(d, "index", 1)) return false; }
                        else if (justDone != kind && c == 0) return false;
                        break;
                    }
                    case "enter":
                    {
                        var d = kv.Value as Dictionary<string, object>; d.TryGetValue("cells", out var cells);
                        if (cells is string letter) { if (ZoneCells.At(g.player.p) != letter[0]) return false; }
                        else if (cells != null) { if (MazeMap.RoomAt(g.player.p) + 1 < (int)ToF(cells)) return false; }
                        break;
                    }
                    default: Warn("trigger " + kv.Key); return false;
                }
            }
            return true;
        }

        // rev2 depth gating (the Swarm Gate's floor = its depth): min_floor, floors [..], every_floor N, only_if
        bool Gate(Dictionary<string, object> p)
        {
            int fl = g.floor;
            if (p.ContainsKey("min_floor") && fl < F(p, "min_floor", 0)) return false;
            if (p.TryGetValue("floors", out var fs) && fs is List<object> fl2 && !fl2.Exists(x => Mathf.RoundToInt(ToF(x)) == fl)) return false;
            if (p.ContainsKey("every_floor")) { int n = Mathf.Max(1, Mathf.RoundToInt(F(p, "every_floor", 1))); if (fl % n != 0) return false; }
            var oi = S(p, "only_if", null);
            if (oi != null)
            {
                if (oi.StartsWith("first floor of a new stage")) { if ((fl - 1) % 5 != 0) return false; }
                else if (oi == "rooms_skipped") { if (!(p.TryGetValue("rooms", out var rs) && rs is List<object> rl && rl.Exists(r => !g.RoomsVisited.Contains(Mathf.RoundToInt(ToF(r)) - 1)))) return false; }
                else return false;   // (unknown conditions: skipped)
            }   // (unknown conditions: skipped)
            return true;
        }
        static readonly string[] Stages = { "CALM", "STORM", "CREEP", "GALE", "DUSK", "ACID RAIN", "BLACKOUT", "ROCKFALL", "DOUBLE TROUBLE" };
        void Fire(Entry e)
        {
            if (!Gate(e.prms)) return;
            float delay = F(e.prms, "delay", 0);
            if (delay > 0) { later.Add((t + delay, () => Run(e.act, e.prms))); return; }
            Run(e.act, e.prms);
        }

        void Run(string act, Dictionary<string, object> p)
        {
            if (Game.SimRun && (act == "HINT" || act == "SET" || act == "DROP" || act == "SPAWN" && S(p, "enemy", "") == "pool_miniboss")) Debug.Log($"Maze event F{g.floor} t{t:0} {act} {S(p, "text", S(p, "system", ""))}");
            switch (act)
            {
                case "HINT": g.hud.ShowHint(S(p, "text", "").Replace("{floor}", g.floor.ToString()).Replace("{stage_name}", Stages[Mathf.Clamp((g.floor - 1) / 5, 0, Stages.Length - 1)]).ToUpperInvariant()); break;
                case "DROP": if (S(p, "kind", "") == "flare") { CellLight.Flares = Mathf.Min(3, CellLight.Flares + 1); g.hud.ShowHint("FLARE +1"); } break;
                case "SPAWN": Spawn(p); break;
                case "HATCH_ALL": foreach (var h in Hazards.Live) if (h is CellEggs eg) eg.HatchAll(); break;
                case "SET": Set(p); break;
                case "TOGGLE": Toggle(p); break;
                case "OPEN": case "CLOSE":
                    if (S(p, "target", "") == "+") foreach (var h in Hazards.Live) if (h is CellMeds m) m.OpenAt = act == "OPEN" ? 0 : 1e9f;
                    if (S(p, "target", "") == "T") foreach (var h in Hazards.Live) if (h is CellTurrets tu) tu.Locked = act == "CLOSE";
                    break;
                case "FOG":
                {
                    float end = F(p, "end_m", p.ContainsKey("density") ? Mathf.Lerp(60f, 8f, Mathf.Clamp01(F(p, "density", 0.3f))) : RenderSettings.fogEndDistance), dur = F(p, "for", 0);
                    var fc = S(p, "color", null); Color c0 = RenderSettings.fogColor;
                    if (fc != null && ColorUtility.TryParseHtmlString(fc, out var fcol)) { RenderSettings.fogColor = fcol; RenderSettings.fog = true; if (dur > 0) later.Add((t + dur, () => RenderSettings.fogColor = c0)); }   // e.g. Meltdown's green coolant fog
                    float s0 = RenderSettings.fogStartDistance, e0 = RenderSettings.fogEndDistance, cam = g.cfg.camera.distance;
                    RenderSettings.fogStartDistance = cam - 4; RenderSettings.fogEndDistance = cam + end * 0.5f;
                    if (dur > 0) later.Add((t + dur, () => { RenderSettings.fogStartDistance = s0; RenderSettings.fogEndDistance = e0; }));
                    break;
                }
                case "WEATHER":
                {
                    var w = Weather.I; if (w == null) break;
                    string sys = S(p, "system", ""); bool on = S(p, "state", "on") != "off";
                    if (sys == "WIND") w.SetWind(on, p.ContainsKey("dir") ? Weather.ParseDir(p["dir"]) : w.WindDir, F(p, "period", 12), F(p, "first", 0));
                    if (sys == "ACID_RAIN") w.SetRain(on, F(p, "period", 20), F(p, "first", 0));
                    if (sys == "DRIFT") { w.Drift = on; if (p.ContainsKey("dir")) w.DriftDir = Weather.ParseDir(p["dir"]); }
                    break;
                }
                case "LIGHT":
                {
                    float lv = F(p, "level", 1), flash = F(p, "flash", 0);
                    if (CellLight.I != null && flash <= 0 && !(p.ContainsKey("area") && p["area"] is Dictionary<string, object>) && S(p, "area", "all") == "all") CellLight.I.Level = lv;
                    if (flash > 0) { float k0 = lightLevel; SetLight(1f); later.Add((t + flash, () => SetLight(k0))); }
                    else if (!(p.ContainsKey("area") && p["area"] is Dictionary<string, object>)) SetLight(lv);
                    else if (lv > 0.5f) SetLight(Mathf.Min(1f, lightLevel + 0.45f));   // area relight (closest version: the floor brightens)
                    break;
                }
                case "STRIKE": Strike.From(this, g, p); break;
                case "FRONT":
                {
                    string st = S(p, "state", "start");
                    if (st == "start") g.StartFront(S(p, "dir", "E"), F(p, "speed", 1.2f), F(p, "countdown", 5), S(p, "skin", "flood"));
                    else if (st == "pause") g.PauseFront(F(p, "for", 4));
                    break;
                }
                case "CUE": break;
                case "FLOOD": if (CellWater.I != null) CellWater.I.Flood(F(p, "spread_s", 8)); break;
                default: Warn("action " + act); break;
            }
        }

        // ---- LIGHT: the floor's light level (sun + ambient multiplier on the zone's look)
        float lightLevel = 1f, sun0 = -1; Color amb0;
        void SetLight(float k)
        {
            var sun = RenderSettings.sun; if (sun == null) return;
            if (sun0 < 0) { sun0 = sun.intensity; amb0 = RenderSettings.ambientEquatorColor; }
            lightLevel = k; sun.intensity = sun0 * Mathf.Max(0.06f, k);
            RenderSettings.ambientSkyColor = amb0 * 1.1f * Mathf.Max(0.12f, k); RenderSettings.ambientEquatorColor = amb0 * Mathf.Max(0.12f, k); RenderSettings.ambientGroundColor = amb0 * 0.75f * Mathf.Max(0.12f, k);
        }
        public void End() { if (sun0 >= 0) SetLight(1f); sun0 = -1; list.Clear(); later.Clear(); schedules.Clear(); }

        // ---- SPAWN
        void Spawn(Dictionary<string, object> p)
        {
            string name = S(p, "enemy", "drone"); int n = (int)F(p, "count", 1); string at = S(p, "at", "S");
            if (EnemyRegistry.IsBoss(name)) { g.SpawnMiniBoss(name, Where(at)); return; }
            if (name == "room_waves")
            {   // a wave of Drones for each listed room the hero skipped (the hive-wall shortcut)
                int skipped = 0; if (p.TryGetValue("rooms", out var rs) && rs is List<object> rl) foreach (var r in rl) if (!g.RoomsVisited.Contains(Mathf.RoundToInt(ToF(r)) - 1)) skipped++;
                int dti = g.swarm.TypeByName("drone"); for (int k = 0; k < 6 * skipped; k++) { var pt = Where(at) + Random.insideUnitCircle * 1.5f; var e = g.swarm.Spawn(pt, g.wave, dti); e.p = pt; }
                if (skipped > 0) g.hud.ShowHint("THE SKIPPED WARDS FOLLOW YOU"); return;
            }
            if (name == "pool_miniboss") { var pool = new[] { "razorback", "slagback", "highwire", "gravid_bloater", "tunnel_matron" }; name = pool[Random.Range(0, pool.Length)]; if (BossBrain.Find(name) != null) { BossBrain.Spawn(g, name, Where(at), true); return; } }   // the Gate's mini-boss pool
            int ti = g.swarm.TypeByName(name);
            if (ti < 0 && BossBrain.Find(name) != null) { BossBrain.Spawn(g, name, Where(S(p, "at", "far_S")), true); return; }   // a data boss by id (e.g. tunnel_matron) as a mini-boss
            if (ti < 0) return;
            bool pack = S(p, "group", "pack") == "pack"; var c = Where(at);
            for (int i = 0; i < n; i++)
            {
                var pos = pack ? c + Random.insideUnitCircle * 1.2f : Where(at);
                var e = g.swarm.Spawn(pos, g.wave, ti); e.p = pos;
            }
        }
        Vector2 Where(string at)
        {
            var pts = MazeMap.spawnPoints; var hp = g.player.p;
            switch (at)
            {
                case "S": return pts.Count > 0 ? pts[Random.Range(0, pts.Count)] : hp;
                case "far_S": { Vector2 b = hp; float bd = -1; foreach (var s in pts) { float d = (s - hp).sqrMagnitude; if (d > bd) { bd = d; b = s; } } return b; }
                case "nearest_S": { Vector2 b = hp; float bd = 1e9f; foreach (var s in pts) { float d = (s - hp).sqrMagnitude; if (d < bd && d > 25) { bd = d; b = s; } } return b; }
                case "ring": { var r = Random.insideUnitCircle.normalized * 8f; return hp + r; }
                case "ahead": return hp + (g.player.vel.sqrMagnitude > 0.5f ? g.player.vel.normalized : g.player.aim) * 9f;
                case "behind": return hp - (g.player.vel.sqrMagnitude > 0.5f ? g.player.vel.normalized : g.player.aim) * 9f;
                case "door": return MazeMap.doors.Count > 0 ? MazeMap.doors[Mathf.Min(MazeMap.doors.Count - 1, Mathf.Max(0, MazeMap.RoomAt(hp)))].center : Where("nearest_S");
                default:
                    if (at.Length == 1) { var m = MazeMap.Marks(at[0]); if (m.Count > 0) { var c = m[Random.Range(0, m.Count)]; return MazeMap.Center(c.x, c.y); } if (at == "n" && MazeMap.nestSpots.Count > 0) return MazeMap.nestSpots[0]; }
                    return Where("S");
            }
        }

        // ---- SET / TOGGLE on the toolkit systems that exist
        void Set(Dictionary<string, object> p)
        {
            string sys = S(p, "system", "");
            foreach (var h in Hazards.Live)
            {
                if (sys == "VENT" && h is CellVents v)
                {
                    if (p.ContainsKey("skin")) v.Skin = S(p, "skin", "steam");
                    if (p.ContainsKey("cycle")) v.Cycle = F(p, "cycle", 6);
                    if (p.ContainsKey("warn")) v.Warn = F(p, "warn", 1.2f);
                    if (p.ContainsKey("erupt")) v.Erupt = F(p, "erupt", 1.5f);
                    if (p.ContainsKey("erupt_all_now")) v.EruptAll(F(p, "radius", 6f), F(p, "freeze_s", 2f));
                }
                if (sys == "LASER_FENCE" && h is CellFences f)
                {
                    float on0 = f.OnSec, off0 = f.OffSec;
                    f.OnSec = F(p, "on", f.OnSec); f.OffSec = F(p, "off", f.OffSec);
                    float dur = F(p, "for", 0); if (dur > 0) later.Add((t + dur, () => { f.OnSec = on0; f.OffSec = off0; }));
                }
                if (sys == "TURRET" && h is CellTurrets tu) { if (p.ContainsKey("capture")) tu.CaptureMul = 2.5f / Mathf.Max(0.2f, F(p, "capture", 2.5f)); }
                if (sys == "COLLAPSE" && h is CellCollapse cc && p.ContainsKey("collapse")) cc.FallAll();
                if (sys == "HIVE_WALL" && h is CellHiveWalls hw && p.ContainsKey("regrow")) hw.RegrowSec = F(p, "regrow", 12);
                if (sys == "GRATE" && h is CellGrates gr) { if (p.ContainsKey("on")) gr.OnSec = F(p, "on", 1.6f); if (p.ContainsKey("off")) gr.OffSec = Mathf.Max(0.2f, F(p, "off", 4)); }
                if (sys == "CONVEYOR" && h is CellFloors && p.ContainsKey("dir")) { var dd = S(p, "dir", "N"); CellFloors.BeltOverride = dd == "N" ? Vector2.down : dd == "S" ? Vector2.up : dd == "E" ? Vector2.right : Vector2.left; g.hud.ShowHint("ALL BELTS TO THE PRESS"); }   // every belt runs one way ('^' = N)
                if (sys == "ACID_RAIN" && Weather.I != null && p.ContainsKey("period")) Weather.I.RainPeriod = F(p, "period", 20);
            }
        }
        void Toggle(Dictionary<string, object> p)
        {
            string sys = S(p, "system", ""), st = S(p, "state", "cycle");
            foreach (var h in Hazards.Live)
            {
                if (sys == "VENT" && h is CellVents v) v.Enabled = st != "off";
                if (sys == "TURRET" && h is CellTurrets tu) tu.Locked = st == "off";   // a power cut: turrets can't be held
                if (sys == "CONVEYOR" && h is CellFloors && (st == "off" || st == "on")) CellFloors.BeltDir = st == "off" ? 0f : 1f;
                if (sys == "GRATE" && h is CellGrates gr) gr.Mode = st == "reverse" ? "cycle" : st;
                if (sys == "LASER_FENCE" && h is CellFences f) f.Mode = st;
                if (sys == "CONVEYOR" && h is CellFloors cf && st == "reverse") { float warn = F(p, "warn", 1.5f); g.hud.ShowBanner("BELTS REVERSING"); later.Add((t + warn, () => CellFloors.BeltDir = -CellFloors.BeltDir)); }
            }
        }

        // ---- STRIKE: red circles that grow (default 1.5 s) then hit: 35 HP to the hero, 12 RH to enemies, radius 2 m.
        // random: K circles every interval, half near the hero; fixed: every cell of a letter (alternating halves)
        class Strike
        {
            FloorEvents ev; Game g; string mode, skin, leaves; float interval, delay, radius, until, tt; int count; char cells, aimC; bool alt, aimEdge; int half;
            public static void From(FloorEvents ev, Game g, Dictionary<string, object> p)
            {
                var s = new Strike { ev = ev, g = g, mode = S(p, "mode", "random"), skin = S(p, "skin", "rock"), leaves = S(p, "leaves", "none"), interval = F(p, "interval", 0),
                    delay = F(p, "delay", 1.5f), radius = F(p, "radius", 2f), count = (int)F(p, "count", 3), until = F(p, "until", 1e9f), alt = S(p, "alternate", "") == "halves" };
                var c = S(p, "cells", "y"); s.cells = c.Length > 0 ? c[0] : 'y';
                var aim = S(p, "aim", ""); if (aim == "edge") s.aimEdge = true; else if (aim.Length == 1) s.aimC = aim[0];   // aim at a cell letter (or the edge)
                if (p.ContainsKey("for")) s.until = F(p, "for", 1e9f);
                if (s.interval > 0) { ev.schedules.Add(s); s.tt = 0; } else s.Burst();
            }
            public void Tick(float dt)
            {
                if (interval <= 0 || (until -= dt) < 0) return;
                if ((tt -= dt) <= 0) { tt = interval; Burst(); }
            }
            void Burst()
            {
                var targets = new List<Vector2>();
                if (mode == "fixed")
                {
                    var m = MazeMap.Marks(cells); var all = new List<Vector2>();
                    if (m.Count == 0) for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.tag[z, x] == cells) all.Add(MazeMap.Center(x, z));
                    foreach (var c in m) all.Add(MazeMap.Center(c.x, c.y));
                    for (int i = 0; i < all.Count; i++) if (!alt || ((i + half) & 1) == 0) targets.Add(all[i]);
                    half++;
                }
                else
                {
                    int near = (count + 1) / 2;
                    if (aimC != '\0' || aimEdge)
                    {   // aimed: random cells carrying the letter (or next to the outer wall)
                        var pool = new List<Vector2>();
                        foreach (var m in MazeMap.Marks(aimC)) pool.Add(MazeMap.Center(m.x, m.y));
                        if (pool.Count == 0) for (int z = 1; z < MazeMap.H - 1; z++) for (int x = 1; x < MazeMap.W - 1; x++)
                            if (aimEdge ? (x == 1 || z == 1 || x == MazeMap.W - 2 || z == MazeMap.H - 2) && MazeMap.Walkable(x, z) : MazeMap.tag[z, x] == aimC) pool.Add(MazeMap.Center(x, z));
                        if (pool.Count > 0) { for (int i = 0; i < count; i++) targets.Add(pool[Random.Range(0, pool.Count)]); near = -1; }
                    }
                    for (int i = 0; i < count && near >= 0; i++)
                    {
                        Vector2 at = i < near ? g.player.p + Random.insideUnitCircle * 3f : RandomFloor();
                        targets.Add(at);
                    }
                }
                Color col = skin == "lightning" ? new Color(0.75f, 0.85f, 1f, 0.9f) : skin == "glob" ? new Color(0.5f, 1f, 0.2f, 0.9f) : new Color(1f, 0.15f, 0.1f, 0.85f);
                foreach (var at in targets)
                {
                    var a = at;
                    EnemyAI.Tele.Circle(a, radius, delay, col, () => Hit(a), ring: true);
                }
            }
            Vector2 RandomFloor()
            {
                for (int k = 0; k < 20; k++) { int x = Random.Range(1, MazeMap.W - 1), z = Random.Range(1, MazeMap.H - 1); if (MazeMap.Walkable(x, z)) return MazeMap.Center(x, z); }
                return g.player.p;
            }
            void Hit(Vector2 at)
            {
                bool covered = (skin == "lightning" || skin == "glob") && ZoneCells.At(g.player.p) == 'u';
                if (!covered && (g.player.p - at).magnitude < radius + g.cfg.player.radius && g.player.dashT <= 0) g.HurtPlayer(ZoneCells.Hp(g, 35));
                float rh = ZoneCells.RH(g) * 12f;
                foreach (var e in g.swarm.enemies) if (!e.dead && (e.p - at).magnitude < radius + e.r) { e.hp -= e.boss ? rh * 0.5f : rh; e.flash = 1; if (e.hp <= 0) g.KillEnemy(e); }
                if (!Game.SimRun)
                {
                    if (skin == "lightning") { Fx.I.Sparks(MazeMap.ToWorld(at, 0.5f), 18, new Color(0.8f, 0.9f, 1f), 7, 0.4f, 0.1f, 6); HazardAudio.Shot("sfx_lightning_strike", MazeMap.ToWorld(at, 0.5f), 0.9f); }
                    else Fx.I.Debris(MazeMap.ToWorld(at, 0.4f), 14, skin == "press" ? new Color(0.4f, 0.4f, 0.42f) : new Color(0.45f, 0.38f, 0.3f), 5, 0.7f, 0.18f, 5);
                    g.AddShake(0.15f);
                }
                if (leaves == "container" || leaves == "rubble") Block(at, leaves == "rubble" ? 15f : 0f);
            }
            void Block(Vector2 at, float life)
            {   // the landed container / rubble is a crate cell (walls the flow field) until it times out
                int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C);
                if (!MazeMap.Walkable(x, z) || MazeMap.CellIndex(g.player.p) == z * MazeMap.W + x) return;
                if (MazeMap.tag != null && (MazeMap.tag[z, x] == ':' || MazeMap.tag[z, x] == 'Q')) return;   // never on the ESCORT track (it would wall the cart off)
                MazeMap.grid[z, x] = 'C'; MazeMap.flowCell = -1;
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(box.GetComponent<Collider>());
                box.transform.position = MazeMap.ToWorld(MazeMap.Center(x, z), 0.9f); box.transform.localScale = new Vector3(1.9f, 1.8f, 1.9f);
                box.GetComponent<MeshRenderer>().sharedMaterial = Mats.Lit(life > 0 ? new Color(0.4f, 0.35f, 0.3f) : new Color(0.12f, 0.48f, 0.48f), null, null, 0.3f);
                ev.blocks.Add((box, x, z, life > 0 ? ev.t + life : 1e9f));
            }
        }
        readonly List<(GameObject go, int x, int z, float until)> blocks = new();
        public void TickBlocks()
        {
            for (int i = blocks.Count - 1; i >= 0; i--)
                if (t >= blocks[i].until) { var b = blocks[i]; if (b.go != null) Object.Destroy(b.go); if (MazeMap.grid != null && b.z < MazeMap.H && b.x < MazeMap.W) MazeMap.grid[b.z, b.x] = '.'; MazeMap.flowCell = -1; blocks.RemoveAt(i); }
        }
        public void ClearBlocks() { foreach (var b in blocks) if (b.go != null) Object.Destroy(b.go); blocks.Clear(); }

        // ---- helpers
        static void Warn(string what) { if (warned.Add(what)) Debug.Log("Maze events: not supported yet: " + what); }
        static float ToF(object o) => o switch { double d => (float)d, long l => l, int i => i, bool b => b ? 1 : 0, string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0, _ => 0 };
        static float F(Dictionary<string, object> d, string k, float def) => d != null && d.TryGetValue(k, out var v) && !(v is List<object>) && !(v is Dictionary<string, object>) ? ToF(v) : def;
        static string S(Dictionary<string, object> d, string k, string def) => d != null && d.TryGetValue(k, out var v) && v is string s ? s : def;
    }

    // a small JSON reader (objects -> Dictionary, arrays -> List, numbers -> double)
    public static class Json
    {
        public static object Parse(string s) { int i = 0; return Val(s, ref i); }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Val(string s, ref int i)
        {
            Ws(s, ref i); if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                while (true) { Ws(s, ref i); if (s[i] == '}') { i++; return d; } var k = Str(s, ref i); Ws(s, ref i); i++; d[k] = Val(s, ref i); Ws(s, ref i); if (s[i] == ',') i++; }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                while (true) { Ws(s, ref i); if (s[i] == ']') { i++; return l; } l.Add(Val(s, ref i)); Ws(s, ref i); if (s[i] == ',') i++; }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }
        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; char e = s[i]; sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'u' ? (char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber) : e); if (e == 'u') i += 4; }
                else sb.Append(s[i]);
                i++;
            }
            i++; return sb.ToString();
        }
    }
}
