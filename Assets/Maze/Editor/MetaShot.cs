using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The phone-less check (Logs/UiShot/meta): the planet with every pin, the briefing, Cryo Station floor 1,
    // Evac Deck's DEFEND floor, a zone boss, a power-up pickup, both hero skills (Flash Freeze, Barrage), and editor
    // frame times at a busy moment (a rough proxy only). Muted, a throwaway save.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.MetaShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class MetaShot
    {
        const string Key = "Maze.MetaShot.Active"; static readonly string Out = System.Environment.GetEnvironmentVariable("SHOT_OUT") ?? "Logs/UiShot/meta";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps; static readonly List<float> frames = new();
        static MetaShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitGame(float s) { float t0 = G.GameTime; wait = () => G.GameTime >= t0 + s; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static void StartZone(string id, string hero)
        {
            Prefs.HeroOverride = hero; var def = SectorDefs.Get(id); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = true;
        }
        static List<Action> Build()
        {
            var save = new SaveData { runs = 5, credits = 25000, parts = 300, alloy = 400, rank = 6, cxp = 200 };
            foreach (var id in new[] { "cryo-station", "evac-deck", "kessler-refinery" }) save.cleared.Add(id);
            save.stars.Add("cryo-station:0"); save.stars.Add("cryo-station:1"); save.shards.Add("scout:12"); save.shards.Add("sentinel:14");
            save.research.Add("fp1:2"); save.research.Add("ar1:1");
            save.heroStars.Add("scout:1"); save.heroLv.Add("scout:1"); save.relics.Add("0:3"); save.relics.Add("7:1"); save.equippedRelic = 0; save.starTrackClaimed = 60; save.cores = 40;
            return new List<Action>
            {
                () =>
                {   // gear with sets: a 2-piece FROSTLINE and a 4-piece-ready BULWARK stack
                    Progress.Sim = true; Progress.UseTemp(save);
                    for (int i = 0; i < 5; i++) { var g = Progress.AddGear(i, i < 2 ? 2 : 1, i < 2 ? 0 : 3); Progress.Equip(g); }
                    for (int i = 0; i < 4; i++) Progress.AddGear(0, 0, 4);
                    var lk = Progress.AddGear(3, 3, 5); lk.locked = true;
                    G.menu.OpenSheet("gear"); Later(1.5);
                },
                () => { Shot("00_gear_sets_relic"); G.menu.CloseSheet(); Later(0.8); },
                () => { Progress.D.heroLv.Add("sentinel:27"); Progress.D.talents.Add("sentinel.0:1"); Progress.D.talents.Add("sentinel.2:2"); Progress.D.talents.Add("sentinel.4:1"); G.menu.OpenSheet("talents"); Later(1.5); },
                () => { Shot("00_talents"); G.menu.CloseSheet(); Later(0.8); },
                () => { G.menu.OpenSheet("ops"); Later(1.5); },
                () => { Shot("00_ops"); G.menu.CloseSheet(); Later(0.8); },
                () => { G.menu.OpenSheet("research"); Later(1.5); },
                () => { Shot("01_research"); G.menu.CloseSheet(); Later(0.8); },
                () => { G.menu.OpenSheet("squad"); Later(1.5); },
                () => { Shot("02_squad"); G.menu.CloseSheet(); Later(0.8); },
                () => { G.menu.OpenSheet("loadout"); Later(1.5); },
                () => { Shot("03_armory"); G.menu.CloseSheet(); Prefs.HeroOverride = "scout"; Later(0.8); },
                () => { G.menu.OpenSheet("loadout"); Later(1.5); },
                () => { Shot("03b_armory_scout"); G.menu.CloseSheet(); Prefs.HeroOverride = null; Later(0.8); },
                () => { MapSelect.I.Show(); Later(2); },
                () => { Shot("04_planet_startrack"); MapSelect.I.PickSector(0); Later(1); },
                () => { MapSelect.I.OpenBriefing(); Later(1.5); },
                () => { Shot("05_briefing_tiers"); MapSelect.I.Hide(); StartZone("cryo-station", "sentinel"); WaitGame(3f); },
                () => { G.DebugWin(); Later(4.5); },
                () => { Shot("06_end_win_grade"); StartZone("evac-deck", "sentinel"); WaitGame(2f); },
                () => { G.godMode = false; G.player.hp = 0.01f; G.HurtPlayer(50f); Later(2.5); },
                () => { Shot("07_end_defeat_picks"); StartZone("drowned-city", "scout"); WaitGame(6f); },
                () => { Shot("08_scout_ingame"); StartZone("acid-marsh", "sentinel"); WaitGame(9f); },
                () =>
                {   // onboarding: a fresh commander's first drop (hint counters reset)
                    Shot("09_midfight_acid_marsh");
                    foreach (var k in new[] { "move", "auto", "bio", "dash", "skill", "skillon", "leaper", "bloater", "hold", "med" }) PlayerPrefs.DeleteKey("ms.hint." + k);
                    Progress.D.runs = 0; Progress.D.cleared.Clear(); StartZone("cryo-station", "sentinel"); WaitGame(0.8f);
                },
                () => { Shot("10_onboarding_first_hint"); WaitGame(5f); },
                () => { Shot("11_onboarding_auto_fire"); Prefs.HeroOverride = null; },
            };
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 200) { Debug.LogError("MetaShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }
            if (false && frames.Count > 0)
            {   // the DEFEND floor stretch: editor frame times (ms)
                frames.Sort(); float sum = 0; foreach (var f in frames) sum += f;
                Debug.Log($"MetaShot: editor frame ms over {frames.Count} frames  mean {sum / frames.Count:F1}  p50 {frames[frames.Count / 2]:F1}  p99 {frames[(int)(frames.Count * 0.99f)]:F1}  enemies {G.swarm.enemies.Count}");
                frames.Clear();
            }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"MetaShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; Prefs.HeroOverride = null; SafeArea.Override = null;
            Debug.Log("MetaShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
