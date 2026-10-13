using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Cryo Station's new deck, before / after (Logs/UiShot/cryofloor): the same floor-1 run start with the old painted
    // floor (CryoFloor.On = false) and the new tile, then each with a ring of enemies around the hero, and a short
    // frame-time sample of each (8 s, god mode). A throwaway save; muted.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.CryoFloorShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class CryoFloorShot
    {
        const string Key = "Maze.CryoFloorShot.Active", Out = "Logs/UiShot/cryofloor";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static CryoFloorShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitGame(float s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }   // real time (the run clock can sit still at a floor start)
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static void Start(bool deck)
        {
            CryoFloor.On = deck; UnityEngine.Random.InitState(7);
            var def = SectorDefs.Get("cryo-station"); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = new[] { Game.Goal.Survive, Game.Goal.Survive, Game.Goal.Survive, Game.Goal.Survive, Game.Goal.Boss };
            g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = false;
            Debug.Log($"CryoFloorShot: state {g.state} run {g.run} deck {deck}: map {MazeMap.MapIndex} {MazeMap.W}x{MazeMap.H}, decals {GameObject.Find("CryoFloorDecals") != null}");
        }
        static void Ring()
        {
            var g = G; g.swarm.Clear(); UnityEngine.Random.InitState(11);
            string[] roster = { "drone", "drone", "leaper", "drone", "bloater", "drone", "leaper", "drone" };
            for (int k = 0; k < roster.Length; k++)
            {
                float a = k / (float)roster.Length * 6.283f; var at = g.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (5f + k % 3);
                int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C); if (!MazeMap.Walkable(x, z)) continue;
                var e = g.swarm.Spawn(at, 2, g.swarm.TypeByName(roster[k])); e.p = at;
            }
        }
        static int f0; static double r0;
        static void FpsStart() { f0 = Time.frameCount; r0 = EditorApplication.timeSinceStartup; }
        static string Fps() { int n = Time.frameCount - f0; double t = EditorApplication.timeSinceStartup - r0; return $"{n} player frames in {t:0.0} s = {n / t:0.0} fps avg ({t * 1000 / Mathf.Max(1, n):0.0} ms/frame)"; }
        static List<Action> Build() => new List<Action>
        {
            () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Start(false); WaitGame(4f); },
            () => { Debug.Log($"CryoFloorShot: state {G.state} run {G.run} t {G.GameTime:0.0}"); Shot("1_before_start"); Ring(); WaitGame(1.6f); },
            () => { Shot("2_before_enemies"); FpsStart(); WaitGame(8f); },
            () => { Debug.Log($"CryoFloorShot: BEFORE (painted floor) {Fps()}"); Start(true); WaitGame(4f); },
            () => { Shot("3_after_start"); Ring(); WaitGame(1.6f); },
            () => { Shot("4_after_enemies"); FpsStart(); WaitGame(8f); },
            () => { Debug.Log($"CryoFloorShot: AFTER (new deck) {Fps()}"); G.ToMenu(); Later(0.5); },
        };
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 200) { Debug.LogError("CryoFloorShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"CryoFloorShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            CryoFloor.On = true;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; Game.PendingPlan = null; SafeArea.Override = null;
            Debug.Log("CryoFloorShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
