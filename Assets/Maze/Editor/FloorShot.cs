using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Each sector-run objective on its layout (Logs/UiShot/floors): the floor intro and, a few seconds in, the action
    // with the HUD tracker; plus the briefing with the run's plan. Muted, a throwaway save.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.FloorShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class FloorShot
    {
        const string Key = "Maze.FloorShot.Active", Out = "Logs/UiShot/floors";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static FloorShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
        static List<Action> Build()
        {
            var s = new List<Action>
            {
                () => { Progress.Sim = true; Progress.UseTemp(new SaveData { sectorsCleared = 1 }); MapSelect.I.Show(); Later(2.5); },
                () => { MapSelect.I.PickSector(0); Later(1.0); },
                () => { MapSelect.I.OpenBriefing(); Later(1.5); },
                () => { Shot("00_briefing_plan"); MapSelect.I.Hide(); Game.PendingTier = 1; var g = G; g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; WaitGame(1f); },
            };
            var goals = new (Game.Goal g, int map, float t)[] { (Game.Goal.Purge, 0, 6f), (Game.Goal.Survive, 2, 9f), (Game.Goal.Escape, 6, 7f), (Game.Goal.Hold, 4, 8f), (Game.Goal.Hunt, 1, 6f), (Game.Goal.Boss, 3, 5f) };
            int i = 1;
            foreach (var (goal, map, t) in goals)
            {
                int k = i++; var gg = goal; int mp = map; float tt = t;
                s.Add(() => { G.DebugGoal(gg, Mathf.Min(k, 5), mp); WaitGame(0.5f); });
                s.Add(() => { Shot($"{k:00}_{Game.GoalNames[(int)gg].ToLower()}_intro"); G.demo = true; if (gg == Game.Goal.Hunt && MazeMap.nestSpots.Count > 0) { G.player.p = MazeMap.nestSpots[0] + new Vector2(1f, -4f); G.cfg.camera.distance = 26; } WaitGame(tt); });
                s.Add(() => { Shot($"{k:00}_{Game.GoalNames[(int)gg].ToLower()}_play"); G.demo = false; });
            }
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 110) { Debug.LogError("FloorShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }   // level-ups along the way
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"FloorShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; SafeArea.Override = null;
            Debug.Log("FloorShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
