using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The drop zones (Logs/UiShot/zones): the planet with every pin (a mid-campaign save: some cleared, starred,
    // locked), then a few zones in play to compare their looks. Muted, a throwaway save.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.EnemyShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class EnemyShot
    {
        const string Key = "Maze.EnemyShot.Active", Out = "Logs/UiShot/enemies";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static string[] ZonesUnused = { "cryo-station", "evac-deck" };   // every floor of each (authored layouts)
        static EnemyShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
            string[] roster = { "carapace", "spitter", "burrower", "shrieker", "tick", "weaver", "lancer", "splitter", "mender", "stalker", "spore_pod" };
            var s = new List<Action>
            {
                () => { Progress.Sim = true; var def = SectorDefs.Get("cryo-station"); Game.PendingTier = 1; Game.PendingSector = def; Game.PendingPlan = new[] { Game.Goal.Survive, Game.Goal.Survive, Game.Goal.Survive, Game.Goal.Survive, Game.Goal.Boss };
                        var g = G; g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; WaitGame(0.5f); },
                () => { var g = G; g.swarm.Clear(); int k = 0;
                        foreach (var n in roster) { int ti = g.swarm.TypeByName(n); float a = k++ / (float)roster.Length * 6.283f;
                            var at = g.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6f; var e = g.swarm.Spawn(at, 3, ti); e.p = at;
                            Debug.Log($"EnemyShot: {n} -> {g.cfg.enemyTypes[ti].name}"); }
                        WaitGame(1.2f); },
                () => { Shot("01_roster"); WaitGame(2.5f); },
                () => { Shot("02_roster_action"); WaitGame(2.5f); },
                () => { Shot("03_roster_late"); },
            };
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 170) { Debug.LogError("EnemyShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"EnemyShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; SafeArea.Override = null;
            Debug.Log("EnemyShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
