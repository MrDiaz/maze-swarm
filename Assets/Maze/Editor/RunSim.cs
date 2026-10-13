using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Progression balance: the demo bot plays whole sector runs (5 floors + boss) at a fixed 1/30 s step with automatic
    // card / cache picks, on a throwaway save with a given rifle level, and reports win rate, floor reached, level and time.
    //   Unity -batchmode -nographics -projectPath . -executeMethod Maze.EditorTools.RunSim.Run -simRuns 1:1,1:3 -simSeeds 6
    //   (tier:rifleLevel pairs; no -quit; results in Logs/RunSim.txt)
    public static class RunSim
    {
        const string Key = "maze.runsim";
        const float Dt = 1f / 30f, MaxTime = 600f;
        const int StepsPerUpdate = 300;

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            int a = System.Array.IndexOf(args, "-simRuns"), b = System.Array.IndexOf(args, "-simSeeds"), z = System.Array.IndexOf(args, "-simZones");
            SessionState.SetString(Key + ".zones", z >= 0 && z + 1 < args.Length ? args[z + 1] : "");
            SessionState.SetBool(Key + ".god", System.Array.IndexOf(args, "-simGod") >= 0);
            SessionState.SetBool(Key + ".relic", System.Array.IndexOf(args, "-simRelic") >= 0);
            SessionState.SetBool(Key + ".tal", System.Array.IndexOf(args, "-simTal") >= 0);
            int ar = System.Array.IndexOf(args, "-simAtRec"); SessionState.SetFloat(Key + ".atrec", ar >= 0 && ar + 1 < args.Length ? float.Parse(args[ar + 1], System.Globalization.CultureInfo.InvariantCulture) : 0f);   // weapon level set so CP ~ this fraction of the zone's Recommended Power
            SessionState.SetBool(Key + ".weekly", System.Array.IndexOf(args, "-simWeekly") >= 0);   // the Weekly Mutation on
            int df = System.Array.IndexOf(args, "-simDiff"); SessionState.SetInt(Key + ".diff", df >= 0 && df + 1 < args.Length ? int.Parse(args[df + 1]) : 0);   // 1 Hard, 2 Nightmare (zone modifiers on)   // a level-50 hero; even seeds take every left talent, odd seeds every right   // each seed equips relic (seed % 18) at LV 5   // invulnerable: checks every floor can be finished
            SessionState.SetString(Key + ".runs", a >= 0 && a + 1 < args.Length ? args[a + 1] : "1:1,1:3");
            SessionState.SetInt(Key + ".seeds", b >= 0 && b + 1 < args.Length ? int.Parse(args[b + 1]) : 6);
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                SessionState.SetBool(Key, false);
                var sim = new Sim(); EditorApplication.update += sim.Update;
            };
        }

        class Sim
        {
            Game game; int sc = -1, seed; float time; (int tier, int lv)[] runs; int seeds;
            readonly StringBuilder log = new(); int wins; float floorSum, lvSum, timeSum, floorStart;
            readonly float[] floorTimes = new float[15]; readonly int[] floorN = new int[15]; string[] zones;
            public void Update()
            {
                if (game == null)
                {
                    game = Object.FindAnyObjectByType<Game>();
                    if (game == null || game.player == null || game.menu == null) { game = null; return; }
                    AudioListener.volume = 0; AudioListener.pause = true;
                    game.enabled = false; Game.SimRun = true; Progress.Sim = true;
                    var parts = SessionState.GetString(Key + ".runs", "1:1").Split(',');
                    runs = new (int, int)[parts.Length];
                    for (int i = 0; i < parts.Length; i++) { var p = parts[i].Split(':'); runs[i] = (int.Parse(p[0]), int.Parse(p[1])); }
                    seeds = SessionState.GetInt(Key + ".seeds", 6);
                    var zs = SessionState.GetString(Key + ".zones", "");
                    if (zs.Length > 0)
                    {   // -simZones id,id: one run spec per zone (its tier, the first spec's weapon level)
                        zones = zs.Split(','); int lv0 = runs[0].lv; runs = new (int, int)[zones.Length];
                        for (int i = 0; i < zones.Length; i++) runs[i] = (SectorDefs.Get(zones[i])?.tier ?? 1, lv0);
                    }
                    game.LoadMap(0);
                    log.AppendLine("tier rifleLv power/rec  seed  result     floor  lv  kills   time");
                    Next(); return;
                }
                for (int i = 0; i < StepsPerUpdate; i++)
                {
                    int fl = game.floor; var gl = game.goal;
                    game.Tick(Dt); time += Dt;
                    if (game.floor != fl && game.floor > fl) { floorTimes[(int)gl] += time - floorStart; floorN[(int)gl]++; floorStart = time; }
                    if (game.state == Game.State.Won || game.state == Game.State.Dead || time >= MaxTime) { Record(); if (!Next()) { Finish(); return; } break; }
                }
            }
            bool Next()
            {
                if (sc < 0) sc = 0;
                else if (++seed >= seeds) { Summary(); seed = 0; sc++; }
                if (sc >= runs.Length) return false;
                var (tier, lv) = runs[sc];
                var sd = new SaveData { weaponLv = new[] { lv, lv, lv, lv }, sectorsCleared = 9 };
                if (SessionState.GetBool(Key + ".relic", false)) { sd.starTrackClaimed = 60; sd.equippedRelic = seed % 18; sd.relics.Add(sd.equippedRelic + ":5"); log.AppendLine("relic " + Meta.Relics[sd.equippedRelic]); }
                if (SessionState.GetBool(Key + ".tal", false)) { foreach (var h in new[] { "sentinel", "vanguard" }) { sd.heroLv.Add(h + ":50"); sd.heroStars.Add(h + ":5"); if (System.Environment.GetEnvironmentVariable("SIM_NOTAL") == null) for (int r = 0; r < 10; r++) if (!("," + System.Environment.GetEnvironmentVariable("SIM_TALSKIP") + ",").Contains("," + r + ",")) sd.talents.Add(h + "." + r + ":" + (seed % 2 + 1)); } log.AppendLine("talents " + (seed % 2 == 0 ? "LEFT" : "RIGHT")); }
                Progress.UseTemp(sd);
                Meta.Difficulty = SessionState.GetInt(Key + ".diff", 0);
                Ops.SimWeekly = SessionState.GetBool(Key + ".weekly", false);
                Random.InitState(1000 + seed);
                Prefs.HeroOverride = SessionState.GetBool(Key + ".tal", false) && seed % 4 >= 2 ? "vanguard" : "sentinel"; Prefs.Weapon = "RIFLE";
                Game.PendingTier = tier; Game.PendingSector = zones != null ? SectorDefs.Get(zones[sc]) : null;
                var simHero = System.Environment.GetEnvironmentVariable("SIM_HERO"); if (!string.IsNullOrEmpty(simHero)) { Prefs.HeroOverride = simHero; if (!sd.heroStars.Exists(x => x.StartsWith(simHero + ":"))) sd.heroStars.Add(simHero + ":1"); }
                float atRec = SessionState.GetFloat(Key + ".atrec", 0f);
                if (atRec > 0 && Game.PendingSector != null)
                {   // the weakest weapon level that reaches atRec x the Recommended Power
                    float target = atRec * Meta.RecommendedAt(Game.PendingSector, Meta.Difficulty);
                    // a player's path there: earlier zones cleared, the rank to match, zone-tier gear, then the SWARMED
                    // screen's advice (best CP per Credit) bought until the target, tiering the weapon up when capped
                    int zn = Mathf.Max(1, Game.PendingSector.num); sd.rank = 2 + 2 * zn;
                    foreach (var z in SectorDefs.All) if (!z.endless && z.num > 0 && z.num < zn) sd.cleared.Add(z.id);
                    int rar = zn >= 15 ? 3 : zn >= 10 ? 2 : zn >= 4 ? 1 : 0;
                    for (int sl = 0; sl < 5; sl++) { var g = new GearItem { uid = sl + 1, slot = sl, rarity = rar, level = 1, set = -1 }; sd.gear.Add(g); sd.equipped[sl] = g.uid; }
                    sd.credits = sd.parts = sd.alloy = 1 << 28;
                    string hid = Meta.HeroId; sd.heroStars.RemoveAll(x => x.StartsWith(hid + ":")); sd.heroStars.Add(hid + ":" + Mathf.Clamp(1 + zn / 4, 1, 5));
                    for (int it = 0; it < 3000 && Meta.CP() < target; it++)
                    {
                        int w = Progress.HeroWeapon;
                        if (Progress.Level(w) >= Meta.WeaponCap(w) && Meta.TierGate(w) == null && Meta.TierUp(w)) continue;
                        var pk = Meta.BestUpgrades(1); if (pk.Count == 0 || !pk[0].buy()) break;
                    }
                    if (Meta.CP() < target && Meta.HeroStars(hid) < 5) { sd.heroStars.RemoveAll(x => x.StartsWith(hid + ":")); sd.heroStars.Add(hid + ":5"); for (int it = 0; it < 3000 && Meta.CP() < target; it++) { var pk = Meta.BestUpgrades(1); if (pk.Count == 0 || !pk[0].buy()) break; } }
                    int hl = Meta.HeroLevel(hid), gr = rar;
                    sd.credits = sd.parts = sd.alloy = 0;
                    log.AppendLine($"atRec {atRec:0.00}: CP {Meta.CP()} / target {target:0}  weapon lv {Progress.Level(Progress.HeroWeapon)} {Meta.Mk[Meta.WeaponTier(Progress.HeroWeapon)]}  hero lv {hl}  gear {Progress.Rarities[gr]} lv {sd.gear[0].level}  research {sd.research.Count}  D {Meta.D_(Progress.HeroWeapon, hid):0.0} H {Meta.H_(hid):0.0}");
                }
                if (zones != null) log.AppendLine("zone " + zones[sc]);
                var simW = System.Environment.GetEnvironmentVariable("SIM_WEAPON"); int simWi = string.IsNullOrEmpty(simW) ? 0 : Mathf.Max(0, System.Array.FindIndex(game.cfg.weapons, x => x.name == simW));
                if (!string.IsNullOrEmpty(simW)) { Progress.D.blueprints.Add(simW + ".dev:1"); log.AppendLine("weapon " + simW); }
                game.menu.Hide(); game.Restart(); game.demo = true; game.demoWeapon = simWi; game.godMode = SessionState.GetBool(Key + ".god", false);
                time = 0; floorStart = 0; return true;
            }
            void Record()
            {
                var (tier, lv) = runs[sc]; bool won = game.state == Game.State.Won;
                if (game.state == Game.State.Dead)
                {   // a death snapshot: the hero's max HP and what stood within 5 m
                    var near = new System.Collections.Generic.Dictionary<string, int>(); int alive = 0;
                    foreach (var e in game.swarm.enemies) { if (e.dead) continue; alive++; if ((e.p - game.player.p).sqrMagnitude < 25f) { near.TryGetValue(e.type.name, out int n); near[e.type.name] = n + 1; } }
                    var parts = new System.Collections.Generic.List<string>(); foreach (var kv in near) parts.Add(kv.Key + " " + kv.Value);
                    log.AppendLine($"    death: maxHp {game.player.maxHp:0}  alive {alive}  within 5 m: {string.Join(", ", parts)}  dmgScale {game.swarm.DmgScale:0.00} hpScale {game.swarm.HpScale:0.00}");
                }
                if (won) wins++; floorSum += won ? Game.Floors : game.floor; lvSum += game.level; timeSum += time;
                if (time >= MaxTime)
                {
                    var sb = new StringBuilder($"    cap: state {game.state} choosing {game.choosing} goal '{game.GoalLine}' hero {game.player.p} goalAt {game.GoalAt} botDir {game.BotGoalDir()} onExit {MazeMap.OnExit(game.player.p)} cellH {MazeMap.grid[Mathf.FloorToInt(game.player.p.y / MazeMap.C), Mathf.FloorToInt(game.player.p.x / MazeMap.C)]} cellG {MazeMap.grid[Mathf.FloorToInt(game.GoalAt.y / MazeMap.C), Mathf.FloorToInt(game.GoalAt.x / MazeMap.C)]} walkG {MazeMap.FlowOpen(Mathf.FloorToInt(game.GoalAt.x / MazeMap.C), Mathf.FloorToInt(game.GoalAt.y / MazeMap.C))} map {MazeMap.Dump()} enemies {game.swarm.enemies.Count}:");
                    foreach (var e in game.swarm.enemies) sb.Append($" [{e.type.name}{(e.boss ? " BOSS" : "")} hp {e.hp:F0} d {(e.p - game.player.p).magnitude:F0}]");
                    log.AppendLine(sb.ToString());
                }
                log.AppendLine($"{tier,4} {lv,7} {Progress.Power(),5}/{Progress.Recommended(tier),-5} {seed,4}  {(won ? "WIN" : time >= MaxTime ? "time cap" : "died"),-9} {(won ? Game.Floors : game.floor),5} {game.level,3} {game.kills,6} {time,5:F0}s  {string.Join(">", System.Array.ConvertAll(game.plan, g => Game.GoalNames[(int)g].Substring(0, 3)))}{(won ? "" : "  died on " + Game.GoalNames[(int)game.goal])}");
            }
            void Summary()
            {
                var (tier, lv) = runs[sc];
                log.AppendLine($"  => tier {tier} rifle Lv {lv}: wins {wins}/{seeds} ({100f * wins / seeds:F0}%), mean floor {floorSum / seeds:F1}, mean level {lvSum / seeds:F1}, mean time {timeSum / seeds:F0}s");
                wins = 0; floorSum = lvSum = timeSum = 0;
            }
            void Finish()
            {
                EditorApplication.update -= Update;
                Game.SimRun = false; Progress.Sim = false; Prefs.HeroOverride = null; Game.PendingTier = 0;
                log.Append("mean floor time:"); for (int g = 0; g < 15; g++) if (floorN[g] > 0) log.Append($"  {Game.GoalNames[g]} {floorTimes[g] / floorN[g]:F0}s (n{floorN[g]})"); log.AppendLine();
                File.WriteAllText("Logs/RunSim.txt", log.ToString());
                Debug.Log("RunSim\n" + log);
                EditorApplication.isPlaying = false;
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }
    }
}
