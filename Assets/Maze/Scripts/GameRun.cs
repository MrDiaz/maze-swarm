using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // In-run progression: a sector run is 5 floors on the arena (floor 5: an elite giant Bloater), kills drop biomass
    // that levels you up (pick 1 of 3 cards), a cache between floors (heal / rare card / credits), evolutions when a
    // weapon's cards are maxed. Everything in here resets when the run ends; Progress keeps the meta side.
    public class Card
    {
        public string id, name, desc, icon; public int group, max = 3; public string hero = ""; public bool evo;
        public Color Tint => evo ? new Color(1f, 0.78f, 0.2f) : group == 0 ? Holo.Cyan : group == 1 ? Holo.Acid : group == 2 ? new Color(0.62f, 0.5f, 1f) : new Color(1f, 0.55f, 0.3f);
    }

    public class RunMods
    {
        readonly Dictionary<string, int> n = new();
        public int this[string id] => n.TryGetValue(id, out var v) ? v : 0;
        public void Add(string id) => n[id] = this[id] + 1;
        public void Reset() => n.Clear();
        public bool Has(string id) => this[id] > 0;
        public int Total { get { int t = 0; foreach (var v in n.Values) t += v; return t; } }
    }

    public partial class Game
    {
        public static readonly Card[] Cards =
        {
            new Card { id = "multi",   name = "MULTISHOT",      desc = "+1 projectile per shot",                 icon = "multishot", group = 0, max = 3 },
            new Card { id = "pierce",  name = "PIERCE",         desc = "Bullets pass through +1 enemy",          icon = "pierce",    group = 0, max = 3 },
            new Card { id = "rico",    name = "RICOCHET",       desc = "Bullets bounce to a nearby enemy",       icon = "ricochet",  group = 0, max = 3 },
            new Card { id = "rate",    name = "FIRE RATE",      desc = "+15% fire rate",                         icon = "firerate",  group = 0, max = 5 },
            new Card { id = "range",   name = "RANGE",          desc = "+2 m reach and bullet range",            icon = "range",     group = 0, max = 3 },
            new Card { id = "crit",    name = "CRITICAL",       desc = "+10% chance of double damage",           icon = "crit",      group = 0, max = 3 },
            new Card { id = "shove",   name = "SHOVE",          desc = "+50% knockback",                          icon = "shove",     group = 0, max = 3 },
            new Card { id = "hp",      name = "MAX HP",         desc = "+20% max HP (and heals it)",              icon = "maxhp",     group = 1, max = 3 },
            new Card { id = "heal",    name = "BIOMASS HEAL",   desc = "Each biomass orb heals 1 HP",             icon = "bioheal",   group = 1, max = 3 },
            new Card { id = "shield",  name = "SHIELD",         desc = "Blocks a hit every 10 s (faster per pick)", icon = "shield",  group = 1, max = 3 },
            new Card { id = "dashcd",  name = "DASH COOLDOWN",  desc = "-20% dash cooldown",                      icon = "dashcd",    group = 2, max = 3 },
            new Card { id = "trail",   name = "SHOCK TRAIL",    desc = "Dashing shocks every enemy you pass",     icon = "trail",     group = 2, max = 3 },
            new Card { id = "grenade", name = "GRENADE",        desc = "Auto-throws a grenade at the thickest pack", icon = "grenade", group = 2, max = 3 },
            new Card { id = "marks",   name = "MARKSMAN",       desc = "Your first hit on each enemy always crits", icon = "marksman", group = 3, max = 1, hero = "sentinel" },
            new Card { id = "storm",   name = "BULLET STORM",   desc = "+25% fire rate, +1 projectile",           icon = "storm",     group = 3, max = 2, hero = "vanguard" },
            new Card { id = "heavy",   name = "HEAVY ROUNDS",   desc = "+25% damage, +50% knockback",             icon = "heavy",     group = 3, max = 3, hero = "vanguard" },
            // evolutions: offered (gold) once a weapon's cards are maxed
            new Card { id = "railgun", name = "RAILGUN",        desc = "RIFLE EVOLVED: pierces the whole screen", icon = "railgun",   evo = true, max = 1 },
            new Card { id = "arcsmg",  name = "ARC SMG",        desc = "SMG EVOLVED: shots chain between enemies", icon = "arcsmg",   evo = true, max = 1 },
            new Card { id = "breacher",name = "BREACHER",       desc = "SHOTGUN EVOLVED: blasts the pack off its feet", icon = "breacher", evo = true, max = 1 },
            new Card { id = "inferno", name = "INFERNO GATLING",desc = "MINIGUN EVOLVED: faster, sets the floor on fire", icon = "inferno", evo = true, max = 1 },
        };
        public static readonly string[] CacheIds = { "cache_heal", "cache_card", "cache_credits" };

        public static int PendingTier;               // set by the briefing: the next Play() starts a run of this tier (0 = none)
        public static SectorDef PendingSector;       // the zone the briefing launched (null: the first zone of PendingTier)
        [System.NonSerialized] public SectorDef sector; float runT;
        public readonly FloorEvents events = new();
        public Enemy Boss => boss != null && !boss.dead ? boss : null;
        public float BossHpFrac => boss != null ? (boss.dead ? 0 : Mathf.Clamp01(boss.hp / Mathf.Max(1, boss.maxHp))) : 1f;
        // a named boss / mini-boss from a floor script: a giant elite of its stand-in type until zone-bosses.json lands
        public Enemy SpawnMiniBoss(string name, Vector2 at)
        {
            var mb = BossBrain.Spawn(this, name, at, true); if (mb != null) return mb;
            int ti = swarm.TypeByName(name); if (ti < 0) return null;
            var e = swarm.Spawn(at, wave, ti); e.p = at; MakeElite(e); e.hp *= 4f; e.maxHp = e.hp; e.scale *= 1.2f; e.r *= 1.2f;
            hud.ShowBanner(name.ToUpperInvariant()); return e;
        }
        [System.NonSerialized] public bool run;
        [System.NonSerialized] public int tier, floor, level = 1, xp, bonusCredits;
        public const int Floors = 5;
        public readonly RunMods mods = new();
        public int XpNeed => Mathf.RoundToInt((18 + 4 * (level - 1)) * (Ops.Mut(0) ? 0.7f : 1f));   // Hot Zone OVERCLOCKED: 30% faster
        public enum Choice { None, LevelUp, Cache, Rare }
        [System.NonSerialized] public Choice choosing;
        public readonly List<Card> offer = new();
        int scoutStars;   // the Scout's star passives (0 for the others)
        bool supplyPick; int EvoRush => (Ops.Mut(3) ? 1 : 0) + (TS(6, 1) ? 1 : 0);   // Hot Zone EVOLUTION RUSH: evolutions need one card level less
        int pendingLevels; float floorT, shieldT, grenadeT, choiceT; bool shieldUp; Enemy boss;
        readonly List<Enemy> trailHit = new();
        [System.NonSerialized] public RunUi runUi;
        public static bool SimRun;                    // editor RunSim: skip the visuals that cost time
        public float FloorMul(int f) => 1f + 0.15f * (f - 1);
        public static float FloorTime;                 // (last floor's duration, for the sims)

        string WeaponName => VgMinigun ? "MINIGUN" : cfg.weapons[player.weapon].name;

        // ---- derived in-run stats
        public float RunDamageMul => (run ? Progress.DamageMul(CurWeaponIdx) : 1f)
                                     * (1 + 0.25f * mods["heavy"]) * (Evolved("railgun") ? 1.6f : 1f);
        public float RunRateMul => (1 + 0.15f * mods["rate"]) * (1 + 0.25f * mods["storm"]) * (Evolved("inferno") ? 1.35f : 1f);
        public float RunRange => (2f * mods["range"] + (Evolved("railgun") ? 6f : 0)) * (VgMinigun ? MinigunRangeMul : 1f);
        // the Iron Vanguard's minigun: its own reach / range, half the rifle's (15 -> 7.5 m auto-fire circle, 30 -> 15 m
        // bullet travel); the RANGE card's +2 m is halved for it too
        public const float MinigunRangeMul = 0.5f;
        public float Reach => cfg.weapons[player.weapon].reach * (VgMinigun ? MinigunRangeMul : 1f) + (run ? RunRange : 0);
        public float BulletRange => cfg.weapons[player.weapon].range * (VgMinigun ? MinigunRangeMul : 1f) + (run ? RunRange : 0);
        int RunMulti => mods["multi"] + mods["storm"];
        int RunPierce => mods["pierce"] + (run ? Progress.PerkPierce(CurWeaponIdx) : 0) + (Evolved("railgun") ? 99 : 0);
        int RunBounce => mods["rico"] + (Evolved("arcsmg") ? 5 : 0) + (run && Meta.Set2On(5) ? 1 : 0);   // VOLT 2: ricochet +1
        float RunCrit => 0.1f * mods["crit"] + (run ? Progress.PerkCrit(CurWeaponIdx) + Meta.GloveCrit : 0);
        float RunShove => (TV(6, 2) ? 1.5f : 1f) * (1 + 0.5f * mods["shove"]) * (1 + 0.5f * mods["heavy"]) * (Evolved("breacher") ? 3f : 1f);
        int CurWeaponIdx => VgMinigun ? 3 : Progress.WeaponIndexFor(cfg.weapons[player.weapon].name);
        bool Evolved(string id) => mods.Has(id) && (id == "railgun" && WeaponName == "RIFLE" || id == "arcsmg" && WeaponName == "SMG" || id == "breacher" && WeaponName == "SHOTGUN" || id == "inferno" && WeaponName == "MINIGUN");

        // ---- flow
        // ---- the run's plan: floor 1 PURGE (teaches the game), floors 2-4 three of SURVIVE / ESCAPE / HOLD / HUNT (no
        // repeats), floor 5 BOSS; each floor on its own compact layout (MazeMap.FloorTemplates, no layout twice a run)
        // the spec's 15 objectives; the new ones run on the closest existing floor logic (Behaves) until they're built
        public enum Goal { Purge, Survive, Escape, Hold, Hunt, Boss, Uplink, Escort, Salvage, Defend, Detonate, Outrun, Relay, Burn, Assassinate }
        public static readonly string[] GoalNames = { "PURGE", "SURVIVE", "ESCAPE", "HOLD", "HUNT", "BOSS", "UPLINK", "ESCORT", "SALVAGE", "DEFEND", "DETONATE", "OUTRUN", "RELAY", "BURN", "ASSASSINATE" };
        public static readonly string[] GoalIcons = { "rifle", "retry", "deploy", "squad", "quality", "bolt", "squad", "deploy", "gear", "squad", "bolt", "deploy", "squad", "quality", "rifle" };
        public static Goal Behaves(Goal g) => g switch
        {
            Goal.Defend => Goal.Hold, Goal.Outrun => Goal.Escape,   // DEFEND: HOLD's generator variant; OUTRUN: ESCAPE + the script's FRONT
            _ => g,
        };
        public static Goal[] PendingPlan; public static int PlanSeed;   // drawn when the briefing opens (shown there)
        public Goal[] plan = new Goal[5]; int[] planMap = new int[5];
        int Slot => Endless ? (Mathf.Max(1, floor) - 1) % 5 : Mathf.Clamp(floor - 1, 0, 4);   // endless runs cycle the 5 slots
        public Goal goal => Behaves(plan[Slot]);   // how the floor plays
        public Goal shownGoal => plan[Slot];        // what the floor is called
        public ZoneFloor zoneFloor => Endless ? gateFloor : sector?.floors != null && floor >= 1 && floor <= sector.floors.Length ? sector.floors[floor - 1] : null;
        string BossName => gateBoss ?? sector?.boss ?? "THE BROODMOTHER";
        // floor 1: the zone's opener; floors 2-4: drawn by weight from its pool without repeats; floor 5: the boss
        public static Goal[] NewPlan(SectorDef def = null)
        {
            if (def?.order != null && def.order.Length == Floors) return (Goal[])def.order.Clone();
            var pool = new List<(Goal goal, float weight)>(def?.pool ?? new[] { (Goal.Survive, 1f), (Goal.Escape, 1f), (Goal.Hold, 1f), (Goal.Hunt, 1f) });
            var mid = new List<Goal>();
            for (int n = 0; n < 3; n++)
            {
                if (pool.Count == 0) { mid.Add(Goal.Purge); continue; }
                float sum = 0; foreach (var p in pool) sum += p.weight;
                float r = Random.value * sum; int pick = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++) { r -= pool[i].weight; if (r <= 0) { pick = i; break; } }
                mid.Add(pool[pick].goal); pool.RemoveAt(pick);
            }
            return new[] { def?.first ?? Goal.Purge, mid[0], mid[1], mid[2], Goal.Boss };
        }

        // TEST MODE runs: the save is snapshotted at the drop and restored at the end (or on leaving); nothing is written
        // meanwhile (Progress.Sim blocks Save, so an app quit mid-run leaves the save as it was). Direct PlayerPrefs bests skip too.
        public static bool TestRun; static string testSnap;
        public bool LastRunTest;
        void TestBegin() { TestEnd(); LastRunTest = false; TestRun = Prefs.TestMode && !SimRun; if (!TestRun) return; testSnap = JsonUtility.ToJson(Progress.D); Progress.Sim = true; }
        public void TestEnd() { if (!TestRun) return; TestRun = false; Progress.UseTemp(JsonUtility.FromJson<SaveData>(testSnap)); Progress.Sim = false; testSnap = null; }
        void StartRun()
        {
            TestBegin();
            sector = PendingSector ?? SectorDefs.All.Find(s => s.tier == Mathf.Max(1, PendingTier)) ?? SectorDefs.All[0]; PendingSector = null;
            run = true; tier = PendingTier > 0 ? PendingTier : sector.tier; floor = 1; runT = 0; OnboardStart(); Ops.R.Reset(); Ops.R.hot = !Endless && Ops.IsHot(sector); if (Endless) ZoneMods.Clear(); else ZoneMods.Setup(sector, Meta.Difficulty, Ops.R.hot); supplyPick = false; ApplyNeedlerTier(); CacheTalents(); scoutStars = Prefs.Scout ? Meta.HeroStars("scout") : 0; talentRare = TS(4, 1); lowHealUsed = vanRevive = evolvedAny = false; levelUpsSeen = 0; reflect = 0; fireStreak = 0; helixN = 0; relayHits = 0; relicT = 0; tagsUsed = false; level = 1; xp = 0; pendingLevels = 0; bonusCredits = 0; mods.Reset(); if (TS(4, 2)) { level = 2; pendingLevels = 1; }   // talent: start a level ahead
            Abilities.Reset(); Hud.SkillOn = true; CellLight.Flares = 3; Chain.Reset(); Feel.Reset(); RunScore = 0; damageTaken = 0; flawlessFloors = 0; secondWindUsed = false;
            choosing = Choice.None; shieldT = 0; shieldUp = false; grenadeT = 0;
            player.maxHp = cfg.player.hp * Progress.HpMul * (Prefs.Scout ? 0.8f : 1f); player.hp = player.maxHp;   // the Scout: 80 HP
            plan = PendingPlan ?? NewPlan(sector); PendingPlan = null;
            // the zone's layout set (arena templates shuffled, no repeats until they run out; ESCAPE floors from its chains)
            var arenas = new List<int>(); foreach (var i in sector.arenas) if (i >= 0 && i < MazeMap.ArenaCount) arenas.Add(i);
            if (arenas.Count == 0) for (int i = 0; i < MazeMap.ArenaCount; i++) arenas.Add(i);
            for (int i = arenas.Count - 1; i > 0; i--) { int k = Random.Range(0, i + 1); (arenas[i], arenas[k]) = (arenas[k], arenas[i]); }
            var escs = new List<int>(); foreach (var i in sector.escapes) if (i >= MazeMap.ArenaCount && i < MazeMap.FloorTemplates.Length) escs.Add(i);
            if (escs.Count == 0) for (int i = MazeMap.ArenaCount; i < MazeMap.FloorTemplates.Length; i++) escs.Add(i);
            int a = 0;
            for (int f = 0; f < 5; f++) planMap[f] = MazeMap.FirstFloorMap + (Behaves(plan[f]) == Goal.Escape ? escs[Random.Range(0, escs.Count)] : arenas[a++ % arenas.Count]);
            // spec zones: each floor on its authored layout
            if (sector.floors != null)
                for (int f = 0; f < 5 && f < sector.floors.Length; f++) { int li = MazeMap.ZoneLayoutIndex(sector.floors[f].layout); if (li >= 0) planMap[f] = li; else Debug.LogWarning("Maze: no layout " + sector.floors[f].layout); }
            StartFloor(TestRun ? Mathf.Clamp(Prefs.TestFloor, 1, Endless ? 30 : Floors) : 1);   // TEST MODE: the briefing's start floor
            runUi?.RunStarted();
        }

        // rebuild the level for the floor's layout without touching the run (ResetGame would)
        void LoadFloorMap(int index)
        {
            swarm.Clear();
            foreach (var b in bullets) Fx.I.ReleaseTracer(b.tr); bullets.Clear();
            foreach (var p in pickups) Destroy(p.tr.gameObject); pickups.Clear();
            ClearOrbs(); fires.Clear(); Fx.I.ClearAll();
            MazeMap.MapIndex = index; MazeMap.Init();
            if (levelRoot != null) Destroy(levelRoot.gameObject);
            BuildLevel(); swarm.ResizeHash(); barrels.Rebuild(); barrels.ResetAll(); escape.Build(this);
            MazeMap.flowCell = -1; flowT = 0;
            player.p = MazeMap.playerSpawn; player.vel = Vector2.zero; camTarget = player.p;
            goalField = new short[MazeMap.W * MazeMap.H];
            EnemyAI.Init(this); EnemyAI.Latched = 0; EnemyAI.ClearWebs(); EndObjective(); Elites.ClearRings();
            if (run) ApplyBiome(sector);
            // (the old floors' generated textures are freed at the run's end: unloading here hitched ~0.3 s)
        }

        // per-floor objective state
        float genHp, genMax, sweepT; int sweepKills;
        // a floor's spec param in seconds ("45 s", "60 s, 'Lockdown'"), else the default
        float ParamSeconds(float def)
        {
            var p = zoneFloor?.param; if (string.IsNullOrEmpty(p)) return def;
            var m = System.Text.RegularExpressions.Regex.Match(p, @"(\d+)\s*s\b");
            return m.Success ? float.Parse(m.Groups[1].Value) : def;
        }
        float goalT, goalNeed, hackT; int goalCount, goalTotal, purgeWave, escRoom; Vector2 goalAt, liftAt; short[] goalField; float goalFieldT;
        const float HackTime = 4.5f;
        bool Hacking => goal == Goal.Escape && escRoom < MazeMap.doors.Count && !MazeMap.doors[escRoom].open;
        // the ESCAPE floor's next stop: the locked door ahead (its near side), else the lift
        void EscTarget()
        {
            if (escRoom < MazeMap.doors.Count && !MazeMap.doors[escRoom].open)
            {
                var d = MazeMap.doors[escRoom]; var c = MazeMap.roomCenter[escRoom]; var dir = (c - d.center).normalized;
                goalAt = d.center + new Vector2(Mathf.Abs(dir.x) > Mathf.Abs(dir.y) ? Mathf.Sign(dir.x) * 2.6f : 0, Mathf.Abs(dir.y) >= Mathf.Abs(dir.x) ? Mathf.Sign(dir.y) * 2.6f : 0);
                // the open side of the door into this room (1-wide doors in thin walls: the room centre can point along the wall)
                int dx0 = d.r.x + d.r.width / 2, dz0 = d.r.y + d.r.height / 2;
                foreach (var o in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                {
                    int nx = dx0 + o.x * (o.x != 0 ? (d.r.width / 2 + 1) : 0), nz = dz0 + o.y * (o.y != 0 ? (d.r.height / 2 + 1) : 0);
                    if (MazeMap.Walkable(nx, nz) && MazeMap.roomOf[nz * MazeMap.W + nx] == escRoom) { goalAt = d.center + new Vector2(o.x, o.y) * 2.6f; break; }
                }
            }
            else goalAt = liftAt;
            MazeMap.FlowTo(goalAt, goalField); ShowGoalMarker();
        }
        readonly List<Enemy> nests = new();
        Transform goalRing, goalArrow; Material goalRingMat, goalArrowMat;
        const int PurgeWaves = 3;
        public string GoalLine => goal switch
        {
            Goal.Purge => $"PURGE  //  WAVE {Mathf.Min(purgeWave, PurgeWaves)}/{PurgeWaves}  //  {swarm.enemies.Count + toSpawn} LEFT",
            Goal.Survive => $"SURVIVE  //  {Hud.Clock(Mathf.Max(0, goalNeed - goalT))}",
            Goal.Escape => Hacking ? $"ESCAPE  //  HACK DOOR {escRoom + 1}/{MazeMap.doors.Count}  //  {Mathf.FloorToInt(100 * hackT / HackTime)}%" : $"ESCAPE  //  REACH THE LIFT  //  {Mathf.RoundToInt((liftAt - player.p).magnitude)} M",
            Goal.Hold when shownGoal == Goal.Defend => $"DEFEND  //  {Hud.Clock(Mathf.Max(0, goalNeed - goalT))}  //  GENERATOR {Mathf.CeilToInt(100 * genHp / Mathf.Max(1, genMax))}%",
            Goal.Hold => $"{(shownGoal == Goal.Hold ? "HOLD THE ZONE" : GoalNames[(int)shownGoal])}  //  UPLOAD {Mathf.FloorToInt(100 * goalT / goalNeed)}%",
            Goal.Hunt => $"HUNT  //  NESTS {goalCount}/{goalTotal}",
            Goal.Boss => $"BOSS  //  KILL {BossName.ToUpper()}",
            _ => ObjLine(),
        };
        public float GoalProgress => goal switch
        {
            Goal.Survive => goalT / goalNeed, Goal.Hold => goalT / goalNeed, Goal.Hunt => goalTotal > 0 ? goalCount / (float)goalTotal : 0,
            Goal.Purge => (purgeWave - 1 + (goalTotal > 0 ? 1 - (swarm.enemies.Count + toSpawn) / (float)goalTotal : 0)) / PurgeWaves,
            Goal.Escape => Hacking ? hackT / HackTime : goalNeed > 0 ? Mathf.Clamp01(1 - (liftAt - player.p).magnitude / goalNeed) : 0,
            Goal.Boss => boss != null ? 1 - Mathf.Clamp01(boss.hp / boss.maxHp) : 1, _ => ObjProgress(),
        };
        // the intro line: the real objectives by name, the ones still running on a stand-in say so
        string Intro() => shownGoal switch
        {
            Goal.Defend => $"PROTECT THE GENERATOR  //  {Hud.Clock(goalNeed)}",
            Goal.Survive => $"HOLD OUT {Hud.Clock(goalNeed)}",
            Goal.Hold => $"STAND IN THE ZONE  //  {Mathf.RoundToInt(goalNeed)} S UPLOAD",
            Goal.Boss => sector != null ? $"KILL {BossName.ToUpper()}" : GoalIntro(goal),
            Goal.Outrun => "OUTRUN IT  //  REACH THE LIFT",
            Goal.Relay => "ACTIVATE THE TERMINALS IN ORDER", Goal.Salvage => "GRAB 5 DATA CORES  //  REACH THE LIFT", Goal.Detonate => "PLANT THE CHARGES  //  25 S TO THE LIFT",
            Goal.Uplink => $"FOLLOW THE UPLINK RING  //  {Mathf.RoundToInt(goalNeed)} S", Goal.Escort => "STAY WITH THE CART", Goal.Burn => "BURN 80% OF THE CREEP",
            Goal.Assassinate => "KILL THE MARKED TARGET",
            _ => GoalIntro(goal),
        };
        static string GoalIntro(Goal g) => g switch
        {
            Goal.Purge => "KILL EVERYTHING  //  3 WAVES", Goal.Survive => "HOLD OUT 0:45", Goal.Escape => "HACK THE DOORS  //  REACH THE LIFT",
            Goal.Hold => "STAND IN THE ZONE TO UPLOAD", Goal.Hunt => "DESTROY 3 NESTS", _ => "KILL THE GIANT BLOATER",
        };

        void StartFloor(int f)
        {
            floor = f; floorT = 0; floorSpawned.Clear(); RoomsVisited.Clear(); lastBreathUsed = false; sealUsed = false; floorBio = 0; huntElite = null; floorDamage = damageTaken; floorTotal = 0;
            if (Endless) GatePlanFloor(f);
            LoadFloorMap(planMap[Slot]);
            int compWave = 1 + 2 * f + tier;                              // composition: Leapers from floor 2, Bloaters from 2
            // threat = RP / 1000 (Hard x1.9, Nightmare x3.4); floors ramp 1.0-1.6 on top
            float ps = TestRun && Prefs.TestPower ? Progress.Power() / 1000f : Endless ? GatePowerScale(f) / FloorMul(f) : sector != null && sector.power > 0 ? Meta.RecommendedAt(sector, Meta.Difficulty) / 1000f : 1.15f * Progress.PowerScale(sector, tier);
            float scale = ps * FloorMul(f);
            swarm.HpScale = scale / (1 + cfg.waves.hpGrowth * (compWave - 1)); // the floor's scale replaces the per-wave HP growth
            swarm.DmgScale = Mathf.Pow(ps, 0.8f) * FloorMul(f);
            if (!Endless && sector != null && sector.power > 0 && !(TestRun && Prefs.TestPower))
            {   // under-power pressure (the wall): below the Recommended Power enemies hit x(RP/CP)^2 and soak x(RP/CP)
                float under = Mathf.Clamp(Meta.RecommendedAt(sector, Meta.Difficulty) / (float)Mathf.Max(1, Progress.Power()), 1f, 2f);
                swarm.DmgScale *= under * under; swarm.HpScale *= under;
            }   // hero HP is only half of CP (sqrt(D x H)): linear damage made late zones ~3x as lethal a hit
            wave = compWave; toSpawn = 0; spawnT = 1.0f; breakT = 99;
            goalT = 0; goalCount = 0; goalTotal = 0; purgeWave = 0; escRoom = 0; slamPending = -1; nests.Clear(); boss = null;
            for (int i = 0; i < player.ammo.Length; i++) { player.ammo[i] = cfg.weapons[i].magSize; player.reloadLeft[i] = 0; }
            switch (goal)
            {
                case Goal.Purge: NextPurgeWave(); break;
                case Goal.Survive: goalNeed = ParamSeconds(45f); break;
                case Goal.Hold:
                    goalNeed = ParamSeconds(shownGoal == Goal.Defend ? 45f : 30f);
                    var gm = MazeMap.Marks('G');
                    goalAt = shownGoal == Goal.Defend && gm.Count > 0 ? MazeMap.Center(gm[0].x, gm[0].y) : MazeMap.zoneSpots.Count > 0 ? MazeMap.zoneSpots[0] : MazeMap.playerSpawn;
                    genMax = genHp = ZoneCells.Hp(this, 600); break;
                case Goal.Escape:
                    // the doors ahead are locked: hack each (stand at it 4.5 s) while the swarm chases, then the lift (3 s)
                    escape.CloseAllDoors();
                    liftAt = Vector2.zero; foreach (var c in MazeMap.exitCells) liftAt += c; liftAt /= Mathf.Max(1, MazeMap.exitCells.Count);
                    goalNeed = (liftAt - player.p).magnitude; hackT = 0; EscTarget(); break;
                case Goal.Hunt:
                    swarm.NestEvery = 2.6f - 0.1f * tier; swarm.NestCap = 20 + 2 * f;
                    foreach (var at in MazeMap.nestSpots) { if (nests.Count >= 3) break; var n = swarm.Spawn(at, compWave, swarm.NestType); n.p = at; nests.Add(n); if (MazeMap.V2) n.hp = n.maxHp = ZoneCells.RH(this) * 30f; }   // spec: 30 RH
                    goalTotal = nests.Count; break;
                case Goal.Boss:
                    var bat = MazeMap.hasBossSpot ? MazeMap.bossSpot : FarSpawn();
                    boss = sector != null ? BossBrain.Spawn(this, BossName, bat, false) : null;   // the zone's data boss
                    if (boss != null && boss.brain != null) { boss.brain.Intro(1.8f); Feel.Dip(0.25f, 1.4f, exempt: true); Feel.Shake(0.2f, 1.2f); Haptics.Heavy(); }
                    int bt = swarm.TypeByName(BossName); if (bt < 0) bt = swarm.BloaterType;
                    if (boss == null && bt >= 0)
                    {   // the zone's boss: a giant elite of its boss type (until real bosses exist)
                        var at = MazeMap.hasBossSpot ? MazeMap.bossSpot : FarSpawn();
                        boss = swarm.Spawn(at, compWave, bt); boss.p = at;
                        boss.boss = true; boss.scale *= 1.55f; boss.r *= 1.55f; boss.hp *= 14f; boss.maxHp = boss.hp; boss.damage *= 1.5f;
                    }
                    toSpawn = 999; break;   // a steady trickle until the boss falls
            }
            if (goal == Goal.Escape || goal == Goal.Hold) MazeMap.FlowTo(goalAt, goalField);
            if (goal > Goal.Boss) StartObjective();
            Hazards.Begin(this, ZoneMods.WithHazards(sector?.hazards)); ZoneMods.ApplySpecials(this);
            events.ClearBlocks(); events.End(); events.Begin(this, zoneFloor?.script);
            PowerUps.Begin(this, goal == Goal.Purge || goal == Goal.Survive || goal == Goal.Hold);
            ShowGoalMarker();
            runUi?.FloorIntro(f, shownGoal, Intro());
            Debug.Log($"Maze run: floor {f} {GoalNames[(int)goal]} on {MazeMap.LayoutName(planMap[Slot])}");
        }

        void NextPurgeWave()
        {
            if (purgeWave > 0) events.Wave(purgeWave, false);
            purgeWave++; events.Wave(purgeWave, true);
            toSpawn = goalTotal = Mathf.RoundToInt((9 + 3 * floor + 3 * purgeWave) * (ZoneMods.On[ZoneMods.Brood] ? 1.4f : 1f)); spawnT = 0.8f;   // BROOD SURGE: +40%
            if (purgeWave > 1) hud.ShowBanner($"WAVE {purgeWave}/{PurgeWaves}");
        }

        Vector2 FarSpawn()
        {
            var pts = MazeMap.spawnPoints; Vector2 best = pts.Count > 0 ? pts[0] : MazeMap.playerSpawn; float bd = -1;
            foreach (var s in pts) { float d = (s - player.p).sqrMagnitude; if (d > bd) { bd = d; best = s; } }
            return best;
        }

        // one enemy from a vent away from the hero (the floor's mix: Leapers from floor 2, a Bloater now and then)
        void SpawnOne(bool preferBehind = false)
        {
            if (floorT < (floor == 1 ? 2.5f : 1.5f)) return;   // drop-in: the first spawns wait
            if (zoneFloor != null && goal != Goal.Purge && goal != Goal.Boss && floorTotal >= FloorBudget) return;   // the spec's counts are the floor's whole swarm
            var pts = MazeMap.spawnPoints; if (pts.Count == 0) return;
            Vector2 at = pts[0]; int count = 0; int myRoom = MazeMap.Facility ? MazeMap.RoomAt(player.p) : -1;
            foreach (var s in pts)
            {
                float d = (s - player.p).magnitude; if (d < 7f) continue;
                if (goal == Goal.Escape && MazeMap.RoomAt(s) != myRoom) continue;           // the doors behind are shut
                if (preferBehind && (s - goalAt).sqrMagnitude < (player.p - goalAt).sqrMagnitude) continue; // chase from behind
                if (Random.Range(0, ++count) == 0) at = s;
            }
            if (count == 0) { float bd = -1; foreach (var s in pts) { if (goal == Goal.Escape && MazeMap.RoomAt(s) != myRoom) continue; float d = (s - player.p).sqrMagnitude; if (d > bd) { bd = d; at = s; } } if (bd < 0) return; }
            if (OnboardBlocksSpawn(at)) return;   // run 1, floor 1: a calm start
            var e = swarm.Spawn(at, wave, PickZoneType()); floorTotal++;
            if (shownGoal == Goal.Defend && Random.value < 0.6f) { e.field = goalField; e.fieldAt = goalAt; }   // the generator draws 60%
            if (sector != null)
            {   // elites and modifiers by the zone's threat tier; ELITE HUNT: one elite a floor (its kill is a free card)
                bool hunt = ZoneMods.On[ZoneMods.EliteHunt] && huntElite == null && floorT > 8f;
                Elites.Roll(this, e, tier, floor, hunt); if (hunt) huntElite = e;
                if (ZoneMods.On[ZoneMods.Armoured] && e.type.name == "grunt") { e.hp *= 1.6f; e.maxHp = e.hp; }   // ARMOURED SWARM
            }
        }

        // the zone's enemy table: weighted entries, each from its floor on (unknown names are skipped)
        readonly Dictionary<string, int> floorSpawned = new(); int floorTotal;
        int FloorBudget { get { int n = 0; if (zoneFloor?.enemies != null) foreach (var en in zoneFloor.enemies) if (!EnemyRegistry.IsBoss(en.type)) n += (int)en.weight; return Mathf.Max(20, n) - (events != null ? ScriptedTotal : 0); } }
        int ScriptedTotal { get { int n = 0; foreach (var v in events.Scripted.Values) n += v; return n; } }
        int PickZoneType()
        {
            var table = zoneFloor?.enemies ?? sector?.enemies;
            if (table == null) return -1;
            // spec zones: the floor's counts are its composition. Specials are a budget spent as they spawn (spread
            // over the floor: weight = what's left of it), Drones are the filler and never run out.
            bool spec = zoneFloor != null;
            float W(EnemyEntry en)
            {
                if (floor < en.fromFloor || IsBossName(en.type) || swarm.TypeByName(en.type) < 0) return 0;
                if (!spec) return en.weight;
                if (EnemyRegistry.Resolve(en.type) == "grunt") return en.weight;
                floorSpawned.TryGetValue(en.type, out int used); events.Scripted.TryGetValue(en.type, out int scripted);
                return Mathf.Max(0, en.weight - used - scripted) * 0.5f;
            }
            float sum = 0; foreach (var en in table) sum += W(en);
            if (sum <= 0) return -1;
            float r = Random.value * sum;
            foreach (var en in table)
            {
                float w = W(en); if (w <= 0) continue;
                if ((r -= w) <= 0) { floorSpawned.TryGetValue(en.type, out int u); floorSpawned[en.type] = u + 1; return swarm.TypeByName(en.type); }
            }
            return -1;
        }

        bool IsBossName(string n) => sector != null && (n == BossName || EnemyRegistry.IsBoss(n));

        // an elite: bigger, ~3x HP, harder hits, worth more biomass
        void MakeElite(Enemy e) => Elites.MakeElite(e);

        // kill feedback by size (2.2) + the chain (2.3) + the run score (3.2)
        public int RunScore; public float damageTaken; float sKillHaptic, floorDamage; int flawlessFloors;
        public int XpGained, ShardsGained; public bool FirstClear; public string ShardHero; public List<string> RankUps = new(), StarTrack = new(), OpsToasts = new();
        static readonly string[] Featured = { "sentinel", "vanguard", "sentinel", "vanguard", "sentinel", "scout", "sentinel", "vanguard", "scout", "sentinel", "vanguard", "scout", "sentinel", "vanguard", "scout", "sentinel", "vanguard", "scout" };
        public static string FeaturedHero(int zone) => Featured[Mathf.Clamp(zone, 1, 18) - 1];
        public string Grade, GradeVerdict; public float GradePoints; public int GradeBonus;
        public enum Size { S, M, L, Elite, Mini, Boss }
        public static Size SizeOf(Enemy e)
        {
            if (e.boss) return Size.Boss;
            if (e.brain != null) return Size.Mini;
            if (e.elite) return Size.Elite;
            var n = e.type.name; var ai = e.type.ai;
            if (e.type.bloater || ai == "carapace" || ai == "spore_pod" || e.type.nest) return Size.L;
            if (n == "grunt" || n == "mini" || ai == "tick" || n == "splitter_s") return Size.S;
            return Size.M;
        }
        void KillFeel(Enemy e)
        {
            if (e.egg && !e.breaker) return;
            var sz = SizeOf(e); float hpf = player.hp / Mathf.Max(1, player.maxHp); bool dipOk = hpf >= 0.3f;
            switch (sz)
            {
                case Size.S: if (Time.unscaledTime - sKillHaptic > 0.12f) { sKillHaptic = Time.unscaledTime; Haptics.Light(); } break;
                case Size.M: Feel.Shake(0.04f, 0.1f); Haptics.Light(); break;
                case Size.L: Feel.Shake(e.type.bloater ? 0.16f : 0.10f, 0.2f); if (dipOk) Feel.Dip(0.3f, 0.05f); Haptics.Medium(); break;
                case Size.Elite: Feel.Shake(0.14f, 0.25f); if (dipOk) Feel.Dip(0.25f, 0.08f); Haptics.Medium(); if (Feel.Callouts) hud.ShowBanner("ELITE DOWN"); break;
                case Size.Mini: Feel.Shake(0.25f, 0.5f); if (dipOk) Feel.Dip(0.2f, 0.25f); Haptics.Heavy(); foreach (var o in swarm.enemies) if (!o.dead && !o.type.nest && !o.boss && (o.p - e.p).sqrMagnitude < 36f) o.hp = 0; break;
                case Size.Boss: Feel.Flash(Color.white, 0.5f, 0.12f, 0.5f); Feel.Dip(0.2f, 1.2f, exempt: true); Feel.Zoom(0.22f, 1.5f); Feel.Shake(0.3f, 1f); Haptics.Heavy(); break;
            }
            int[] pts = { 1, 2, 5, 10, 20, 0 }; int[] score = { 10, 30, 100, 300, 1000, 4000 };
            Chain.Add(pts[(int)sz], this);
            RunScore += Mathf.RoundToInt(score[(int)sz] * Chain.ScoreMult);
        }

        void UpdateFloor(float dt)
        {
            floorT += dt;
            if (floor == 1 && floorT >= 0.9f && floorT - dt < 0.9f && !SimRun)
            {   // drop-in impact (2.9): dust ring, shake, flash, heavy haptic
                Feel.Shake(0.25f, 0.5f); Feel.Flash(Color.white, 0.2f, 0.1f); Haptics.Heavy();
                Fx.I.Smoke(MazeMap.ToWorld(player.p, 0.1f), 16, new Color(0.8f, 0.78f, 0.72f), 5, 0.6f, 0.5f, 0.5f);
            }
            escape.TickDoors(dt); { int rm = MazeMap.RoomAt(player.p); if (rm >= 0) RoomsVisited.Add(rm); }
            int alive = 0; foreach (var e in swarm.enemies) if (!e.type.nest || (!e.egg && goal != Goal.Purge)) alive++;   // eggs, breakers, pods don't hold a floor open
            switch (goal)
            {
                case Goal.Purge:
                case Goal.Boss:
                    if (toSpawn > 0 && (spawnT -= dt) <= 0 && alive < (goal == Goal.Boss ? 16 : cfg.waves.maxAlive)) { SpawnOne(); toSpawn--; spawnT = goal == Goal.Boss ? 0.9f : 0.3f; }
                    // soft-lock guard: the last few out of reach (cut off by a fallen walkway, wedged) for 15 s get swept
                    if (goal == Goal.Purge && toSpawn == 0 && alive > 0 && alive <= 8 && kills == sweepKills) { if ((sweepT += dt) > 15f) { foreach (var e in swarm.enemies.ToArray()) if (!e.dead && !e.type.nest) KillEnemy(e); hud.ShowBanner("AREA SWEPT"); } }
                    else { sweepT = 0; sweepKills = kills; }
                    if (toSpawn == 0 && alive == 0 && !swarm.AnyDying && orbs.Count == 0)
                    {
                        if (goal == Goal.Purge && purgeWave < PurgeWaves) NextPurgeWave();
                        else if (choosing == Choice.None && pendingLevels == 0) FloorCleared();
                    }
                    if (goal == Goal.Boss && boss != null && boss.dead && toSpawn > 0) { toSpawn = 0; GoalDone("BOSS DOWN"); }
                    break;
                case Goal.Survive:
                {
                    goalT += dt;
                    float k = Mathf.Clamp01(goalT / goalNeed);                       // spawns ramp up
                    if ((spawnT -= dt) <= 0 && alive < 14 + 22 * k) { SpawnOne(); spawnT = Mathf.Lerp(0.55f, 0.22f, k); }
                    if (goalT >= goalNeed) GoalDone("SURVIVED");
                    break;
                }
                case Goal.Hold when shownGoal == Goal.Defend:
                {   // DEFEND: the generator must live for goalNeed s; the swarm that reaches it chews it
                    goalT += dt;
                    foreach (var e in swarm.enemies)
                        if (!e.dead && !e.type.nest && (e.p - goalAt).sqrMagnitude < (e.r + 1.3f) * (e.r + 1.3f)) genHp -= e.damage * dt / Mathf.Max(0.1f, cfg.enemy.attackCooldown);
                    if ((spawnT -= dt) <= 0 && alive < 22) { SpawnOne(); spawnT = 0.45f; }
                    if (genHp <= 0) { genHp = 0; hud.ShowBanner("GENERATOR LOST"); GameOver(); break; }
                    if (goalT >= goalNeed) GoalDone("GENERATOR HELD");
                    break;
                }
                case Goal.Hold:
                {
                    bool inside = (player.p - goalAt).magnitude < 3.6f;
                    if (inside) goalT += dt;
                    if (SimRun && (holdDbg += dt) > 10f) { holdDbg = 0; Debug.Log($"Maze hold: F{floor} goalT {goalT:0.0} need {goalNeed} inside {inside} param '{zoneFloor?.param}'"); }
                    if ((spawnT -= dt) <= 0 && alive < 18) { SpawnOne(); spawnT = inside ? 0.42f : 0.6f; }  // the swarm comes hard
                    if (goalT >= goalNeed) GoalDone("UPLOAD COMPLETE");
                    break;
                }
                case Goal.Hunt:
                    goalCount = 0; foreach (var n in nests) if (n.dead) goalCount++;
                    if ((spawnT -= dt) <= 0 && alive < 10) { SpawnOne(); spawnT = 1.5f; }       // a trickle besides the nests
                    if (goalCount >= goalTotal) GoalDone("NESTS DESTROYED");
                    break;
                case Goal.Escape:
                {
                    int room = MazeMap.RoomAt(player.p);
                    if (Hacking)
                    {   // at the locked door: the hack fills while you stand there (the bolts go one by one when it's done)
                        if ((player.p - MazeMap.doors[escRoom].center).magnitude < 4.2f) hackT += dt;
                        if (hackT >= HackTime) { escape.UnlockDoor(escRoom); hud.ShowBanner("DOOR OPEN"); hackT = 0; slamPending = escRoom; events.Done("door"); }
                    }
                    if (slamPending >= 0 && room > slamPending)
                    {   // through: it slams behind you once you're clear of it, cutting off what's behind
                        var d = MazeMap.doors[slamPending];
                        var rect = new Rect(d.r.x * MazeMap.C, d.r.y * MazeMap.C, d.r.width * MazeMap.C, d.r.height * MazeMap.C);
                        var cp = new Vector2(Mathf.Clamp(player.p.x, rect.xMin, rect.xMax), Mathf.Clamp(player.p.y, rect.yMin, rect.yMax));
                        if ((cp - player.p).magnitude > 1.4f && MazeMap.doors[slamPending].open)
                        {
                            escape.SlamDoor(slamPending); foreach (var e in swarm.enemies) if (MazeMap.RoomAt(e.p) <= slamPending && MazeMap.RoomAt(e.p) >= 0) e.dead = true;
                            escRoom = slamPending + 1; slamPending = -1; EscTarget();
                        }
                    }
                    if ((spawnT -= dt) <= 0 && alive < 20) { SpawnOne(); spawnT = Hacking && hackT > 0 ? 0.35f : 0.55f; }
                    if (!Hacking && slamPending < 0 && MazeMap.NearExit(player.p))
                    {
                        goalT += dt;
                        int n = Mathf.CeilToInt(3f - goalT); if (n != goalCount && n > 0) { goalCount = n; hud.ShowBanner("EXTRACTING " + n); Sfx.Click(); Haptics.Light(); }
                        if (goalT >= 3f) GoalDone("EXTRACTED");
                    }
                    else { goalT = 0; goalCount = 0; }
                    break;
                }
            }
            if (goal > Goal.Boss) UpdateObjective(dt, alive);
            TickFront(dt);
            if ((goal == Goal.Escape || goal == Goal.Hold || (goal > Goal.Boss && goal != Goal.Assassinate)) && (goalFieldT -= dt) <= 0) { goalFieldT = 1f; MazeMap.FlowTo(slamPending >= 0 ? MazeMap.roomCenter[slamPending + 1] : goalAt, goalField); }
            UpdateGoalMarker(dt);
        }

        // the goal's done: what's left of the swarm bursts, then the cache
        void GoalDone(string banner)
        {
            hud.ShowBanner(banner);
            instantXp = true; foreach (var e in swarm.enemies.ToArray()) if (!e.dead) KillEnemy(e); instantXp = false;
            foreach (var o in orbs) GainXpQuiet(o.value); ClearOrbs();
            toSpawn = 0;
            if (pendingLevels > 0) { floorDonePending = true; OpenChoice(Choice.LevelUp); } else FloorCleared();
        }
        bool floorDonePending, instantXp; int slamPending = -1;
        void GainXpQuiet(int v) { xp += v; while (xp >= XpNeed) { xp -= XpNeed; level++; pendingLevels++; } }

        void FloorCleared()
        {
            floorDonePending = false;
            FloorTime = floorT;
            if (damageTaken <= floorDamage + 0.01f) flawlessFloors++;
            if (!SimRun) { Feel.Dip(0.3f, 0.15f); Feel.Shake(0.08f, 0.2f); Haptics.Medium(); }   // the objective sting
            if (goalRing != null) goalRing.gameObject.SetActive(false);
            if (goalArrow != null) goalArrow.gameObject.SetActive(false);
            if (Endless && !TestRun) { if (floor > GateBest) { PlayerPrefs.SetInt("ms.gate.best", floor); PlayerPrefs.Save(); hud.ShowBanner($"NEW BEST: FLOOR {floor}"); } }
            else if (floor >= Floors) { RunEnd(true); return; }
            OpenChoice(Choice.Cache);
        }

        // the zone / lift ring and a floor arrow towards the goal (the lift, the zone, the nearest nest)
        void ShowGoalMarker()
        {
            if (goalRing == null)
            {
                goalRing = new GameObject("GoalRing").transform; goalRing.SetParent(transform, false);
                goalRing.gameObject.AddComponent<MeshFilter>().sharedMesh = Fx.Ring;
                var mr = goalRing.gameObject.AddComponent<MeshRenderer>(); goalRingMat = Mats.Glow(Holo.Acid); mr.sharedMaterial = goalRingMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                goalArrow = new GameObject("GoalArrow").transform; goalArrow.SetParent(transform, false);
                var am = new Mesh(); am.vertices = new[] { new Vector3(0, 0, 0.9f), new Vector3(0.75f, 0, -0.35f), new Vector3(0, 0, 0f), new Vector3(-0.75f, 0, -0.35f) };
                am.triangles = new[] { 0, 1, 2, 0, 2, 3 }; am.RecalculateNormals(); am.RecalculateBounds();
                goalArrow.gameObject.AddComponent<MeshFilter>().sharedMesh = am;
                var ar = goalArrow.gameObject.AddComponent<MeshRenderer>(); goalArrowMat = Mats.Glow(Holo.Acid); goalArrowMat.SetFloat("_Cull", 0); ar.sharedMaterial = goalArrowMat; ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            bool ring = goal == Goal.Hold || goal == Goal.Escape || (goal > Goal.Boss && goal != Goal.Burn && goal != Goal.Assassinate);
            goalRing.gameObject.SetActive(ring);
            if (ring) { goalRing.position = MazeMap.ToWorld(goalAt, 0.07f); goalRing.localScale = Vector3.one * (goal == Goal.Hold ? 3.6f : goal == Goal.Uplink ? 3f : goal > Goal.Boss ? 2.1f : Hacking ? 3.0f : 2.4f); }
            goalArrow.gameObject.SetActive(false);
        }

        void UpdateGoalMarker(float dt)
        {
            if (goalRing == null || SimRun) return;
            float t = Time.time;
            if (goalRing.gameObject.activeSelf)
            {
                bool inside = goal == Goal.Hold && (player.p - goalAt).magnitude < 3.6f;
                goalRingMat.SetColor("_BaseColor", Holo.A(inside ? Holo.Acid : Holo.Cyan, 0.55f + 0.35f * Mathf.Sin(t * (inside ? 10 : 4))));
                goalRing.localRotation = Quaternion.Euler(0, t * 30, 0);
            }
            Vector2 dir = Vector2.zero;
            if (goal == Goal.Escape || goal == Goal.Hold || goal > Goal.Boss) { if ((player.p - goalAt).magnitude > 4f) dir = goal > Goal.Boss ? MazeMap.StepAlong(goalField, player.p, goalAt) : MazeMap.StepAlong(goalField, player.p, goalAt); }
            else if (goal == Goal.Hunt) { var n = NearestNest(); if (n != null && (n.p - player.p).magnitude > 6f) dir = (n.p - player.p).normalized; }
            bool show = dir.sqrMagnitude > 0.01f && state == State.Playing;
            goalArrow.gameObject.SetActive(show);
            if (show)
            {
                goalArrow.SetPositionAndRotation(MazeMap.ToWorld(player.p + dir * 2.1f, 0.07f), Quaternion.LookRotation(MazeMap.ToWorld(dir)));
                goalArrow.localScale = Vector3.one * (0.9f + 0.12f * Mathf.Sin(t * 6));
                goalArrowMat.SetColor("_BaseColor", Holo.A(Holo.Acid, 0.6f + 0.35f * Mathf.Sin(t * 6)));
            }
        }
        Enemy NearestNest() { Enemy best = null; float bd = 1e9f; foreach (var n in nests) { if (n.dead) continue; float d = (n.p - player.p).sqrMagnitude; if (d < bd) { bd = d; best = n; } } return best; }

        // the demo bot: where the floor's goal wants it to go (null = just fight)
        public Vector2? BotGoalDir()
        {
            if (!run) return null;
            switch (goal)
            {
                case Goal.Escape: return Hacking && (player.p - MazeMap.doors[escRoom].center).magnitude < 3.6f ? (Vector2?)null : MazeMap.StepAlong(goalField, player.p, slamPending >= 0 ? MazeMap.roomCenter[slamPending + 1] : goalAt);
                case Goal.Hold: return (player.p - goalAt).magnitude > 2.4f ? MazeMap.StepAlong(goalField, player.p, goalAt) : (Vector2?)null;
                case Goal.Hunt: { var n = NearestNest(); if (n == null || (n.p - player.p).magnitude <= Mathf.Min(9f, Reach - 1f)) return null; var d = BotPath(n.p); return d.sqrMagnitude > 0.01f ? d : BotPathWalls(n.p); }
                case Goal.Burn:
                {   // kill the nests first, then sweep the creep
                    var n = NearestNest(); if (n != null) return (n.p - player.p).magnitude > 9f ? BotPath(n.p) : (Vector2?)null;
                    Vector2 best = player.p; float bd = 1e9f;
                    for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.tag[z, x] == 'c' && MazeMap.flow[z * MazeMap.W + x] >= 0) { var c = MazeMap.Center(x, z); float d = (c - player.p).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
                    return bd < 1e9f ? BotPath(best) : (Vector2?)null;
                }
                case Goal.Escort: return (player.p - goalAt).magnitude > 2.5f ? BotPath(goalAt) : (Vector2?)null;   // the cart moves: a fresh path, not the floor's field
                case Goal.Assassinate: return marked != null && (marked.p - player.p).magnitude > 8f ? MazeMap.StepAlong(goalField, player.p, marked.p) : (Vector2?)null;
                default:
                    if (goal > Goal.Boss) return (player.p - goalAt).magnitude > (MazeMap.OnExit(goalAt) && !MazeMap.OnExit(player.p) ? 0.4f : 1.2f) ? MazeMap.StepAlong(goalField, player.p, goalAt) : (Vector2?)null;   // a lift: onto its cell
                    break;
            }
            return null;
        }
        // the bot: a goal behind hive walls ('m', solid until shot): plan as if they were open; the bot shoots the one ahead
        short[] wallField; float wallFieldT; Vector2 wallFieldTo;
        Vector2 BotPathWalls(Vector2 to)
        {
            if (wallField == null || wallField.Length != MazeMap.W * MazeMap.H) wallField = new short[MazeMap.W * MazeMap.H];
            if ((wallFieldT -= Time.deltaTime) <= 0 || (to - wallFieldTo).sqrMagnitude > 4f)
            {
                wallFieldT = 1f; wallFieldTo = to; var walls = MazeMap.Marks('m');
                foreach (var c in walls) if (MazeMap.grid[c.y, c.x] == 'm') MazeMap.grid[c.y, c.x] = '~';   // (a temporary open cell)
                MazeMap.FlowTo(to, wallField);
                foreach (var c in walls) if (MazeMap.grid[c.y, c.x] == '~') MazeMap.grid[c.y, c.x] = 'm';
            }
            // StepAlong skips solid cells: step toward the best neighbour by hand (walls included)
            int x = Mathf.FloorToInt(player.p.x / MazeMap.C), z = Mathf.FloorToInt(player.p.y / MazeMap.C);
            int best = wallField[z * MazeMap.W + x]; if (best < 0) return Vector2.zero; Vector2 dir = Vector2.zero;
            foreach (var o in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
            {
                int nx = x + o.x, nz = z + o.y; if (nx < 0 || nz < 0 || nx >= MazeMap.W || nz >= MazeMap.H) continue;
                int f = wallField[nz * MazeMap.W + nx]; if (f >= 0 && f < best) { best = f; dir = MazeMap.Center(nx, nz) - player.p; }
            }
            return dir;
        }
        public bool BotWallAhead(Vector2 dir, out Vector2 at)
        {   // a hive wall cell right ahead of the bot
            at = player.p + dir.normalized * 1.6f;
            int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C);
            if (x < 0 || z < 0 || x >= MazeMap.W || z >= MazeMap.H || MazeMap.grid[z, x] != 'm') return false;
            at = MazeMap.Center(x, z); return true;
        }
        // the bot: a goal cut off by pits is reached through a jump pad pair (J cells pair up in order)
        short[] padField;
        Vector2? ViaJumpPad(Vector2 goal)
        {
            var pads = MazeMap.Marks('J'); if (pads.Count < 2) return null;
            if (padField == null || padField.Length != MazeMap.W * MazeMap.H) padField = new short[MazeMap.W * MazeMap.H];
            MazeMap.FlowTo(goal, padField);
            for (int i = 0; i + 1 < pads.Count; i += 2)
                for (int k = 0; k < 2; k++)
                {
                    var a = pads[i + k]; var b = pads[i + 1 - k];
                    if (padField[b.y * MazeMap.W + b.x] < 0) continue;   // the far pad can't reach the goal
                    var ac = MazeMap.Center(a.x, a.y); if ((ac - player.p).magnitude < 1f) return (ac - player.p);
                    var d = BotPath(ac); if (d.sqrMagnitude > 0.01f) return d;
                }
            return null;
        }
        // the bot walks the flow field to a point (authored layouts have walls between it and its goal)
        Vector2 botPathTo; float botPathT; readonly short[] noField = new short[0]; short[] botField;
        Vector2 BotPath(Vector2 to)
        {
            if (botField == null || botField.Length != MazeMap.W * MazeMap.H) botField = new short[MazeMap.W * MazeMap.H];
            if ((botPathT -= Time.deltaTime) <= 0 || (to - botPathTo).sqrMagnitude > 4f) { botPathT = 1f; botPathTo = to; MazeMap.FlowTo(to, botField); }
            return MazeMap.StepAlong(botField, player.p, to);
        }
        public Vector2 GoalAt => goalAt;   // (sims)
        public void OvertimeSurvive(float s) { if (goal == Goal.Survive) goalNeed = s; }
        float holdDbg;
        public readonly HashSet<int> RoomsVisited = new();   // (rev2/3 'rooms_skipped')
        public Enemy BotGoalTarget() => run && goal == Goal.Hunt ? NearestNest() : null;

        void RunEnd(bool won)
        {
            if (!SimRun) Resources.UnloadUnusedAssets(); // the run's old floors (textures, meshes) behind the end screen
            int reached = won ? Floors : floor;
            bool firstClear = won && sector != null && !Endless && !Meta.TierCleared(sector, Meta.Difficulty);
            var loot = Meta.Reward(sector, reached, won, kills, firstClear);
            // Commander XP: 12 a floor reached + 10 a run + 50 a first clear (grade bonus after the grade)
            XpGained = 12 * reached + 10 + (firstClear ? 50 : 0); FirstClear = firstClear;
            // shards: the boss drops 1-3 of the zone's featured hero (x2 Hard, x3 Nightmare)
            ShardsGained = 0;
            if (won && sector != null && sector.num > 0) { ShardHero = FeaturedHero(sector.num); ShardsGained = Random.Range(1, 4) * (Meta.Difficulty + 1) * (Ops.R.hot && Ops.HotMaterial == 3 ? 2 : 1); Meta.AddShards(ShardHero, ShardsGained); }
            if (bonusCredits > 0) { Progress.D.credits += bonusCredits; loot.credits += bonusCredits; Progress.Save(); }
            if (SimRun && DamageBy.Count > 0)
            {
                var l = new List<KeyValuePair<string, float>>(DamageBy); l.Sort((a, b) => b.Value.CompareTo(a.Value));
                Debug.Log("Maze sim damage: " + string.Join(", ", l.ConvertAll(kv => $"{kv.Key} {kv.Value:F0}"))); DamageBy.Clear();
            }
            Hazards.End(); events.End(); events.ClearBlocks(); EnemyAI.Reset(); PowerUps.EndRun(); Hud.SkillOn = false; EndObjective(); Elites.ClearRings(); EnemyAI.Latched = 0; EnemySlow = 1f; HideBrood(); ZoneMods.Clear();
            newStars = won && sector != null ? Progress.CompleteZone(sector, runT, player.hp / Mathf.Max(1, player.maxHp)) : new List<int>();
            if (won && Progress.D.sectorsCleared < tier) { Progress.D.sectorsCleared = tier; Progress.Save(); }
            // run score (3.2) and, for wins, the grade (3.1) with its Credits bonus (3.3)
            float par = sector != null ? sector.par : 300f;
            int clearedFloors = won ? reached : Mathf.Max(0, reached - 1);
            RunScore += 500 * (clearedFloors * (clearedFloors + 1) / 2) + Mathf.RoundToInt(won ? 30f * Mathf.Max(0, 1.5f * par - runT) : 0) + 400 * flawlessFloors;
            Grade = null; GradeVerdict = "";
            if (won && !Endless && sector != null)
            {
                float T = Mathf.Clamp(100f * (1.5f * par - runT) / (0.8f * par), 0, 100);
                float f = damageTaken / Mathf.Max(1, player.maxHp * 5f); float Sf = Mathf.Clamp(100f * (1 - f / 0.8f), 0, 100);
                float tot = 0; foreach (var x in Chain.TimeAt) tot += x; float hi = Chain.TimeAt[2] + Chain.TimeAt[3] + Chain.TimeAt[4];
                float C = 50f * Chain.PeakTier / 4f + 50f * Mathf.Min(1, tot > 0 ? hi / tot / 0.5f : 0);
                float St = 100f * Progress.StarCount(sector) / 3f;
                GradePoints = 0.30f * T + 0.30f * Sf + 0.25f * C + 0.15f * St;
                Grade = GradePoints >= 88 ? "S" : GradePoints >= 70 ? "A" : GradePoints >= 45 ? "B" : "C";
                float weakest = Mathf.Min(T, Mathf.Min(Sf, C));
                GradeVerdict = Grade == "S" ? "FAST AND CLEAN." : weakest == T ? "FASTER NEXT TIME." : weakest == Sf ? "WATCH THE TELEGRAPHS." : "KEEP THE KILLS COMING.";
                int pct = Grade == "S" ? 25 : Grade == "A" ? 12 : Grade == "B" ? 5 : 0;
                GradeBonus = Mathf.RoundToInt(loot.credits * pct / 100f); if (GradeBonus > 0) { Progress.D.credits += GradeBonus; loot.credits += GradeBonus; Progress.Save(); }
                string key = "ms.grade." + sector.id; int rank = "CBAS".IndexOf(Grade);
                if (!TestRun && rank > PlayerPrefs.GetInt(key, -1)) { if (Grade == "S" && PlayerPrefs.GetInt(key, -1) < 3) Progress.D.cores += 5; PlayerPrefs.SetInt(key, rank); PlayerPrefs.Save(); Progress.Save(); }
            }
            if (Grade == "A") XpGained += 10; else if (Grade == "S") XpGained += 20;
            RankUps = SimRun ? new List<string>() : Meta.AddXp(XpGained);
            StarTrack = won && !SimRun ? Meta.ClaimStarTrack() : new List<string>();
            var salv = OnboardDeath(won, reached);
            OpsToasts = Ops.RunEnd(this, Endless ? null : sector, won, Meta.Difficulty, newStars, player.hp / Mathf.Max(1, player.maxHp), runT, won && Floors >= 5 && damageTaken <= floorDamage + 0.01f, Progress.HeroWeapon, Meta.HeroId);
            if (salv != null) OpsToasts.Insert(0, salv);
            Progress.Save();
            state = won ? State.Won : State.Dead;
            if (won) { Haptics.Heavy(); AddShake(0.4f); }
            LastRunTest = TestRun; TestEnd();   // TEST MODE: the save goes back to how it was at the drop (rewards shown, not kept)
            runUi?.ShowEnd(won, reached, loot);
            hud.SetMode(Hud.Mode.Hidden);
        }

        // ---- biomass
        class Orb { public Vector2 p, v; public int value; public float t; public Transform tr; }
        readonly List<Orb> orbs = new(); readonly Stack<Transform> orbPool = new(); Material orbMat;
        public int BiomassFor(Enemy e) => e.boss ? 40 : e.elite ? 6 : e.type.nest ? 10 : e.type.bloater ? 8 : e.type.leaper ? 3 : 1;
        void DropBiomass(Enemy e)
        {
            int total = run ? Mathf.Max(1, Chain.Biomass(Mathf.RoundToInt(BiomassFor(e) * (ZoneMods.On[ZoneMods.Brood] ? 1.4f : 1f)))) : BiomassFor(e); if (run) { Ops.R.biomass += total; floorBio += total; OnboardOrb(); }
            int n = Mathf.Min(total, e.boss ? 8 : e.type.bloater || e.type.nest ? 4 : e.type.leaper ? 2 : 1);
            for (int i = 0; i < n; i++)
            {
                int val = total / n + (i < total % n ? 1 : 0);
                if (instantXp) { GainXpQuiet(val); continue; }
                if (SimRun || orbs.Count > 160) { GainXp(val); continue; }
                var o = new Orb { p = e.p, v = Random.insideUnitCircle * 5f, value = val, tr = GetOrb() };
                o.tr.localScale = Vector3.one * (0.22f + 0.04f * val);
                orbs.Add(o);
            }
        }
        Transform GetOrb()
        {
            if (orbPool.Count > 0) { var t = orbPool.Pop(); t.gameObject.SetActive(true); return t; }
            orbMat ??= Mats.Unlit(new Color(0.75f, 0.35f, 1f));
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(go.GetComponent<Collider>());
            go.name = "Biomass"; go.transform.SetParent(transform, false);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = orbMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
        void UpdateOrbs(float dt)
        {
            for (int i = orbs.Count - 1; i >= 0; i--)
            {
                var o = orbs[i]; o.t += dt;
                var to = player.p - o.p; float d = to.magnitude;
                float pull = o.t < 0.25f ? 0 : Mathf.Min(60f, 14f + o.t * 40f);
                o.v = Vector2.Lerp(o.v, to / Mathf.Max(d, 0.01f) * pull, o.t < 0.25f ? dt * 2 : dt * 10);
                o.p += o.v * dt;
                o.tr.position = MazeMap.ToWorld(o.p, 0.6f + 0.3f * Mathf.Sin(o.t * 9f) * Mathf.Clamp01(1 - o.t));
                if (d < 0.7f || o.t > 3f)
                {
                    GainXp(o.value);
                    if (mods["heal"] > 0 || TS(1, 1)) player.hp = Mathf.Min(player.maxHp, player.hp + mods["heal"] + (TS(1, 1) ? 1 : 0));
                    o.tr.gameObject.SetActive(false); orbPool.Push(o.tr); orbs[i] = orbs[^1]; orbs.RemoveAt(orbs.Count - 1);
                }
            }
        }
        void ClearOrbs() { foreach (var o in orbs) { o.tr.gameObject.SetActive(false); orbPool.Push(o.tr); } orbs.Clear(); }
        void GainXp(int v)
        {
            if (!run) return;
            xp += v;
            while (xp >= XpNeed) { xp -= XpNeed; level++; pendingLevels++; }
            if (pendingLevels > 0 && choosing == Choice.None && state == State.Playing) OpenChoice(Choice.LevelUp);
        }

        // ---- choices (level-up cards, the floor cache, a cache's rare card)
        void OpenChoice(Choice c)
        {
            if (c == Choice.LevelUp) pendingLevels--;
            choosing = c; choiceT = 0;
            offer.Clear();
            if (c == Choice.Cache)
            {
                int credits = Mathf.RoundToInt(60f * Mathf.Pow(1.4f, tier - 1) / 5f) * 5;
                offer.Add(new Card { id = "cache_heal", name = "MED CRATE", desc = "Restore all HP", icon = "cache_heal", group = 1 });
                offer.Add(new Card { id = "cache_card", name = "TECH CRATE", desc = "A rare upgrade card", icon = "cache_card", group = 4 });
                offer.Add(new Card { id = "cache_credits", name = "SUPPLY CRATE", desc = $"+{credits} CREDITS at the end of the run", icon = "cache_credits", group = 0, max = credits });
            }
            else
            {   // HELIX DRIVE: every Nth level-up is rare; talent: the first level-up is rare; COMMANDER'S PICK / talent: 4 cards
                bool four = c == Choice.LevelUp && (TS(9, 2) || levelUpsSeen < Meta.CommandersPick);
                if (c == Choice.LevelUp) levelUpsSeen++;
                bool rareT = c == Choice.LevelUp && talentRare; if (rareT) talentRare = false;
                if (!(c == Choice.LevelUp && OnboardOffer())) BuildOffer(c == Choice.Rare || c == Choice.LevelUp && (HelixTick() || rareT), four ? 4 : 3);
            }
            state = State.Choosing; Time.timeScale = 0;
            hud.SetMode(Hud.Mode.Hidden);
            runUi?.ShowChoice(c, offer);
        }

        bool CanOffer(Card k)
        {
            if (mods[k.id] >= k.max) return false;
            if (k.hero == "sentinel" && Prefs.Vanguard || k.hero == "vanguard" && !Prefs.Vanguard) return false;
            if (!k.evo) return true;
            switch (k.id)
            {
                case "railgun": return WeaponName == "RIFLE" && mods["pierce"] >= 3 - EvoRush && mods["range"] >= 3 - EvoRush;
                case "arcsmg": return WeaponName == "SMG" && mods["rico"] >= 3 - EvoRush;
                case "breacher": return WeaponName == "SHOTGUN" && mods["shove"] >= 3 - EvoRush;
                case "inferno": return WeaponName == "MINIGUN" && mods["rate"] >= 5 - EvoRush;
            }
            return false;
        }

        void BuildOffer(bool rare, int want = 3)
        {
            var pool = new List<Card>();
            foreach (var k in Cards) if (CanOffer(k)) { if (k.evo) { offer.Add(k); } else pool.Add(k); }
            foreach (var k in Cards2) if (mods[k.id] < k.max && !(k.hero == "sentinel" && Prefs.Vanguard || k.hero == "vanguard" && !Prefs.Vanguard) && CanOffer2(k)) { if (k.evo) { if (offer.Count < 3) offer.Add(k); } else pool.Add(k); }
            // a rare card: only weapon / hero cards (the strong ones)
            if (rare) pool.RemoveAll(k => k.group != 0 && k.group != 3 && k.group != 4);
            if (TV(4, 1)) { var hv = pool.Find(k => k.id == "heavy"); if (hv != null) { pool.Add(hv); pool.Add(hv); } }   // talent: Heavy Rounds offered more
            while (offer.Count < want && pool.Count > 0) { int i = Random.Range(0, pool.Count); if (!offer.Contains(pool[i])) offer.Add(pool[i]); pool.RemoveAt(i); }
        }

        public void Pick(int i)
        {
            if (choosing == Choice.None || i < 0 || i >= offer.Count) return;
            var k = offer[i]; var was = choosing; ObRecommend = false;
            if (was == Choice.Cache && !supplyPick) Ops.R.caches++;
            if (k.evo) Ops.R.evos++;
            if (k.group == 1 && was != Choice.Cache) Ops.R.survivalCard = true;
            if (was == Choice.Cache && (Ops.Mut(1) || TS(8, 1)) && !supplyPick && k.id != "cache_card" && offer.Count > 1)
            {   // Hot Zone SUPPLY LINES: take a second crate
                supplyPick = true; if (k.id == "cache_heal") player.hp = player.maxHp; else if (k.id == "cache_credits") bonusCredits += k.max;
                offer.RemoveAt(i); Sfx.Toggle(); runUi?.ShowChoice(Choice.Cache, offer); return;
            }
            supplyPick = false;
            choosing = Choice.None; state = State.Playing; Time.timeScale = 1;
            hud.SetMode(Hud.Mode.Playing);
            runUi?.HideChoice();
            switch (k.id)
            {
                case "cache_heal": player.hp = player.maxHp; break;
                case "cache_card": OpenChoice(Choice.Rare); return;
                case "cache_credits": bonusCredits += k.max; break;
                default:
                    mods.Add(k.id);
                    if (k.id == "hp") { float old = player.maxHp; player.maxHp = cfg.player.hp * Progress.HpMul * (Prefs.Scout ? 0.8f : 1f) * (1 + 0.2f * mods["hp"]); player.hp += player.maxHp - old; }
                    if (k.evo) Evolve(k);
                    break;
            }
            Sfx.Toggle(); Haptics.Light();
            if ((was == Choice.Cache || was == Choice.Rare) && (floor < Floors || Endless)) StartFloor(floor + 1); // the cache sits between floors
            if (was == Choice.LevelUp && pendingLevels == 0) Feel.Dip(0.5f, 0.3f, exempt: true);
            if (was == Choice.LevelUp && Meta.Leg(4)) player.hp = Mathf.Min(player.maxHp, player.hp + player.maxHp * 0.1f * Meta.AffixMul(4));   // implant: level-ups heal   // the level-up ease back in
            if (pendingLevels > 0) OpenChoice(Choice.LevelUp);
            else if (floorDonePending && choosing == Choice.None) FloorCleared();
        }

        void Evolve(Card k)
        {   // the big moment: flash, shake, a banner, sparks around the hero
            evolvedAny = true; AddShake(0.6f); Haptics.Heavy();
            if (!SimRun) for (int i = 0; i < 3; i++) Fx.I.Sparks(MazeMap.ToWorld(player.p, 1f), 24, new Color(1f, 0.8f, 0.25f), 7, 0.9f, 0.12f, 5);
            runUi?.ShowEvolution(k);
            Debug.Log($"Maze run: evolved {k.name}");
        }

        // bots and sims pick for themselves (a bit of a preference order, like a player would)
        void AutoPick()
        {
            choiceT += Time.unscaledDeltaTime;
            if (!demo && !SimRun) return;
            if (!SimRun && choiceT < 0.35f) return;
            int best = 0; float bs = -1;
            for (int i = 0; i < offer.Count; i++)
            {
                var k = offer[i]; float s = k.evo ? 100 : k.id == "cache_heal" ? (player.hp < player.maxHp * 0.6f ? 50 : 1) : k.id == "cache_card" ? 20 : k.id == "cache_credits" ? 10 :
                    k.id == "rate" || k.id == "multi" ? 9 : k.id == "pierce" || k.id == "crit" || k.id == "heavy" || k.id == "storm" ? 8 : k.id == "hp" || k.id == "shield" ? 7 : k.id == "marks" ? 7 : 4;
                if (s > bs) { bs = s; best = i; }
            }
            Pick(best);
        }

        // ---- per frame in a run: orbs, shield, grenades, the dash trail
        [System.NonSerialized] public List<int> newStars = new();
        public float RunTime => runT;
        void TickRun(float dt)
        {
            runT += dt; lastBreathT -= dt;
            Chain.Tick(dt); Feel.TelegraphNear = EnemyAI.Tele.Danger(player.p, out _);
            CardTick(dt); Elites.TickRings(this); if (obRun > 0 || HintsOn) OnboardTick(dt);
            Hazards.Tick(this, dt);
            events.Tick(dt); events.TickBlocks();
            EnemyAI.TickGlobal(dt);
            EnemySlow = EnemyAI.LatchSlow * EnemyAI.WebSlow(player, dt) * PowerUps.MoveMul * GearMoveMul;
            PowerUps.Tick(dt);
            UpdateOrbs(dt);
            if ((mods["shield"] > 0 || Meta.Set4On(3)) && !shieldUp) { shieldT += dt; if (shieldT >= (Meta.Set4On(3) ? 6f : 12f - 2f * mods["shield"])) { shieldUp = true; shieldT = 0; } }   // BULWARK 4: a shield every 6 s
            player.shieldUp = shieldUp;
            if (mods["grenade"] > 0)
            {
                grenadeT += dt;
                if (grenadeT >= 8f - 1.5f * (mods["grenade"] - 1) && ThrowGrenade()) grenadeT = 0;
            }
            if (mods["trail"] > 0 && player.dashT > 0)
            {
                foreach (var e in swarm.enemies)
                {
                    if (e.dead || trailHit.Contains(e) || (e.p - player.p).sqrMagnitude > 1.7f * 1.7f) continue;
                    trailHit.Add(e); e.hp -= 30f * mods["trail"] * RunDamageMul; e.flash = 1;
                    if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(e.p, 0.8f), 6, new Color(0.5f, 0.85f, 1f), 4, 0.4f, 0.08f, 3);
                    if (e.hp <= 0) KillEnemy(e);
                }
            }
            else if (trailHit.Count > 0) trailHit.Clear();
            for (int i = fires.Count - 1; i >= 0; i--)
            {   // Inferno Gatling: burning patches on the floor
                var f = fires[i]; f.t -= dt;
                foreach (var e in swarm.enemies) if (!e.dead && (e.p - f.p).sqrMagnitude < 1.4f * 1.4f) { e.hp -= 22f * RunDamageMul * dt; if (e.hp <= 0) KillEnemy(e); }
                if (!SimRun && Random.value < dt * 8) Fx.I.Sparks(MazeMap.ToWorld(f.p + Random.insideUnitCircle * 0.9f, 0.1f), 2, new Color(1f, 0.5f, 0.15f), 2, 0.5f, 0.09f, 3);
                if (f.t <= 0) fires.RemoveAt(i);
            }
        }

        class FirePatch { public Vector2 p; public float t; }
        readonly List<FirePatch> fires = new(); int infernoCount;

        bool ThrowGrenade()
        {   // the densest spot of live enemies within reach
            float R = Reach; Vector2 best = default; int bn = 0;
            foreach (var e in swarm.enemies)
            {
                if (e.dead || (e.p - player.p).sqrMagnitude > R * R) continue;
                int n = 0; foreach (var o in swarm.enemies) if (!o.dead && (o.p - e.p).sqrMagnitude < 9f) n++;
                if (n > bn) { bn = n; best = e.p; }
            }
            if (bn == 0) return false;
            float dmg = 70f * RunDamageMul * (1 + 0.25f * (mods["grenade"] - 1));
            foreach (var e in swarm.enemies)
            {
                if (e.dead) continue; float d = (e.p - best).magnitude; if (d > 3.2f) continue;
                e.hp -= dmg * (1 - 0.6f * d / 3.2f); e.flash = 1; e.k += (e.p - best).normalized * 6f;
                if (e.hp <= 0) KillEnemy(e);
            }
            if (!SimRun) Fx.I.Boom(MazeMap.ToWorld(best), 3.2f);
            AddShake(0.15f);
            return true;
        }

        // a hit with the run's mods: crits, marksman; returns the damage dealt
        float RunHitDamage(Enemy e, float baseDmg, out bool crit)
        {
            crit = Random.value < RunCrit || mods["marks"] > 0 && !e.marked || scoutStars >= 5 && Abilities.DecoyActive(out var dAt) && (e.p - dAt).sqrMagnitude < 36f;   // Scout 5*: decoy-chasers always crit
            e.marked = true;
            return baseDmg * (crit ? 2f + Meta.ImplantCritDmg : 1f);
        }

        // editor captures (ProgShot)
        public void DebugXp(int v) => GainXp(v);
        public void DebugCache() { swarm.Clear(); toSpawn = 0; OpenChoice(Choice.Cache); }
        public void DebugMods(params string[] ids) { foreach (var id in ids) mods.Add(id); }
        public void DebugLevelUp() { pendingLevels++; OpenChoice(Choice.LevelUp); }
        public void DebugWin() { swarm.Clear(); RunEnd(true); }
        public void DebugFloor(int f) { StartFloor(f); }
        public void DebugSkill() { skillPressed = true; Abilities.Cd = 0; }
        public void DebugGoal(Goal g, int f, int map) { floor = f; plan[f - 1] = g; planMap[f - 1] = MazeMap.FirstFloorMap + map; StartFloor(f); }

        // HurtPlayer: the shield takes one hit
        bool ShieldBlocks()
        {
            if (!shieldUp) return false;
            shieldUp = false; shieldT = 0;
            if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(player.p, 1f), 18, new Color(0.4f, 0.8f, 1f), 5, 0.5f, 0.1f, 3);
            return true;
        }
    }
}
