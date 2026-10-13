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
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.ZoneShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class ZoneShot
    {
        const string Key = "Maze.ZoneShot.Active", Out = "Logs/UiShot/bossbar";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        public static string[] Zones = { "cryo-station", "drowned-city" };
        public static int FirstFloor = 5;   // 1 = every floor; 5 = boss floors only   // every floor of each (authored layouts)
        static ZoneShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
            var save = new SaveData { sectorsCleared = 3 };
            foreach (var id in new[] { "cryo", "rust", "verdant", "dust", "kessler" }) save.cleared.Add(id);
            foreach (var st in new[] { "cryo:0", "cryo:1", "cryo:2", "rust:0", "rust:2", "verdant:0", "dust:0", "dust:1", "kessler:0" }) save.stars.Add(st);
            var s = new List<Action>
            {
                () => { Progress.Sim = true; Progress.UseTemp(save); MapSelect.I.Show(); Later(2.5); },
                () => { Shot("00_planet"); MapSelect.I.PickSector(5); Later(1.2); },
                () => { Shot("01_planet_drowned"); MapSelect.I.PickSector(12); Later(1.2); },
                () => { Shot("02_planet_locked"); MapSelect.I.Hide(); Later(0.5); },
            };
            int i = 3;
            foreach (var id in Zones)
            {
                var zid = id;
                s.Add(() =>
                {
                    var def = SectorDefs.Get(zid); var g = G;
                    Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
                    g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = true; WaitGame(FirstFloor > 1 ? 0.3f : 20f);
                });
                for (int f = FirstFloor; f <= FirstFloor; f++)
                {
                    int k = i++, ff = f;
                    if (f > 1) s.Add(() => { G.DebugFloor(ff); G.godMode = true; G.demo = true; WaitGame(4f); });
                    s.Add(() => Shot($"{k:00}_{zid}_f{ff}_{Game.GoalNames[(int)G.shownGoal].ToLower()}"));
                }
            }
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 230) { Debug.LogError("ZoneShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"ZoneShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; SafeArea.Override = null;
            Debug.Log("ZoneShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
