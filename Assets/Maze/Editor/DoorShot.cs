using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Blast door capture (Escape Test, muted): the hero steps into room 1 a few metres past door 0, the door seals
    // (frames through the slam, lock and alarm), then the room is cleared (amber, bolts off, crack, slide). Game time
    // runs at 1/4 speed so frames land on the beats; Logs/UiShot/doors/<cam>_<phase>_<t>.png plus the "Maze door
    // audio:" log lines for sound sync.   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.DoorShot.Run
    [InitializeOnLoad]
    public static class DoorShot
    {
        const string Key = "Maze.DoorShot.Active", Out = "Logs/UiShot/doors";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;
        static float mark;
        static DoorShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void At(float s) { float t = mark + s; wait = () => G.GameTime >= t; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); Debug.Log($"DoorShot: {n} at +{G.GameTime - mark:F2}"); }
        static readonly float[] SealT = { 0.0f, 0.12f, 0.2f, 0.26f, 0.32f, 0.7f, 1.2f, 1.45f };
        static readonly float[] OpenT = { 0.1f, 0.25f, 0.8f, 1.55f, 1.75f, 2.2f, 2.6f, 3.0f, 3.4f };
        static System.Collections.Generic.List<Action> Build()
        {
            var s = new System.Collections.Generic.List<Action>
            {
                () => { var g = G; g.LoadMap(1); g.menu.Hide(); g.Restart(); g.godMode = true; g.holdFire = true; g.cfg.camera.distance = 30; WaitReal(1.0); },
                () => {
                    var g = G; var d = MazeMap.doors[0]; var dir = (MazeMap.roomCenter[1] - d.center).normalized;
                    g.player.p = d.center + dir * 3.6f; g.player.aim = -dir; g.player.facing = Mathf.Atan2(-dir.x, -dir.y);
                    Time.timeScale = 0.25f; mark = g.GameTime; Debug.Log("DoorShot: stepped in");
                },
            };
            foreach (var t in SealT) { float tt = t; s.Add(() => At(tt)); s.Add(() => Shot($"close_seal_{tt:F2}")); }
            s.Add(() => { G.cfg.camera.distance = 40; At(1.55f); });
            s.Add(() => Shot("far_sealed"));
            // the room's exit door (doors[1], in a horizontal wall: its face toward the camera) is the one that opens
            s.Add(() => {
                var g = G; var d = MazeMap.doors[1]; var dir = (MazeMap.roomCenter[1] - d.center).normalized;
                g.cfg.camera.distance = 30; g.player.p = d.center + dir * 4.2f; Time.timeScale = 1; WaitReal(1.5);
            });
            s.Add(() => { Time.timeScale = 0.25f; G.escape.DebugClear(); mark = G.GameTime; Debug.Log("DoorShot: cleared"); });
            foreach (var t in OpenT) { float tt = t; s.Add(() => At(tt)); s.Add(() => Shot($"close_open_{tt:F2}")); }
            s.Add(() => { G.cfg.camera.distance = 40; At(3.6f); });
            s.Add(() => Shot("far_open"));
            return s;
        }
        static System.Collections.Generic.List<Action> steps;
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 80) { Debug.LogError("DoorShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); next = now + 2; return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"DoorShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Time.timeScale = 1; SafeArea.Override = null;
            Debug.Log("DoorShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
