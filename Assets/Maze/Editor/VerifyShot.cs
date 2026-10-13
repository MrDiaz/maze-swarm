using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The phone-less check (Logs/UiShot/verify): the planet with every pin, the briefing, Cryo Station floor 1,
    // Evac Deck's DEFEND floor, a zone boss, a power-up pickup, both hero skills (Flash Freeze, Barrage), and editor
    // frame times at a busy moment (a rough proxy only). Muted, a throwaway save.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.VerifyShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class VerifyShot
    {
        const string Key = "Maze.VerifyShot.Active", Out = "Logs/UiShot/verify";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps; static readonly List<float> frames = new();
        static VerifyShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
            var save = new SaveData { sectorsCleared = 3 };
            foreach (var id in new[] { "cryo-station", "evac-deck", "kessler-refinery", "verdant-colony", "blackout-labs" }) save.cleared.Add(id);
            return new List<Action>
            {
                () => { Progress.Sim = true; Progress.UseTemp(save); MapSelect.I.Show(); Later(2.5); },
                () => { Shot("01_planet_pins"); MapSelect.I.PickSector(0); Later(1.0); },
                () => { MapSelect.I.OpenBriefing(); Later(1.5); },
                () => { Shot("02_briefing_cryo"); MapSelect.I.Hide(); StartZone("cryo-station", "sentinel"); WaitGame(7f); },
                () => { Shot("03_cryo_floor1"); G.DebugSkill(); WaitGame(0.35f); },
                () => { Shot("04_flash_freeze_throw"); WaitGame(0.6f); },
                () => { Shot("05_flash_freeze_frozen"); PowerUps.Drop(PowerUps.Get("overcharge"), G.player.p + new Vector2(0.4f, 0)); WaitGame(0.6f); },
                () => { Shot("06_powerup_pickup"); StartZone("evac-deck", "sentinel"); WaitGame(0.3f); },
                () => { G.DebugFloor(2); G.godMode = true; G.demo = true; WaitGame(8f); },
                () => { Shot("07_evac_defend"); frames.Clear(); WaitGame(3f); },   // the busy moment: frame times sampled meanwhile
                () => { G.DebugFloor(5); G.godMode = true; G.demo = true; WaitGame(6f); },
                () => { Shot("08_zone_boss_razorback"); StartZone("kessler-refinery", "vanguard"); WaitGame(0.3f); },
                () => { G.DebugFloor(4); G.godMode = true; G.demo = true; WaitGame(6f); },
                () => { G.DebugSkill(); WaitGame(1.0f); },
                () => { Shot("09_barrage_vanguard"); WaitGame(1.0f); },
                () => { Shot("10_barrage_late"); },
            };
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 200) { Debug.LogError("VerifyShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }
            if (step == 10 && frames.Count > 0)
            {   // the DEFEND floor stretch: editor frame times (ms)
                frames.Sort(); float sum = 0; foreach (var f in frames) sum += f;
                Debug.Log($"VerifyShot: editor frame ms over {frames.Count} frames  mean {sum / frames.Count:F1}  p50 {frames[frames.Count / 2]:F1}  p99 {frames[(int)(frames.Count * 0.99f)]:F1}  enemies {G.swarm.enemies.Count}");
                frames.Clear();
            }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"VerifyShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; Prefs.HeroOverride = null; SafeArea.Override = null;
            Debug.Log("VerifyShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
