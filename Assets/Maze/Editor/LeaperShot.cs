using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Blast door capture (Escape Test, muted): the hero steps into room 1 a few metres past door 0, the door seals
    // (frames through the slam, lock and alarm), then the room is cleared (amber, bolts off, crack, slide). Game time
    // runs at 1/4 speed so frames land on the beats; Logs/UiShot/leaper/<cam>_<phase>_<t>.png plus the "Maze door
    // audio:" log lines for sound sync.   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.LeaperShot.Run
    [InitializeOnLoad]
    public static class LeaperShot
    {
        const string Key = "Maze.LeaperShot.Active", Out = "Logs/UiShot/leaper";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;
        static float mark;
        static LeaperShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void At(float s) { float t = mark + s; wait = () => G.GameTime >= t; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); Debug.Log($"LeaperShot: {n} at +{G.GameTime - mark:F2}"); }
        // Leaper capture: two Leapers ~9 m from a hero holding fire (god mode), 1/4 speed; frames through the stalk,
        // the crouch (telegraph), the pounce and the stun. Then a FAR frame.
        static readonly float[] T = { 0.4f, 0.9f, 1.2f, 1.45f, 1.6f, 1.75f, 1.9f, 2.2f, 2.6f };
        static System.Collections.Generic.List<Action> Build()
        {
            var s = new System.Collections.Generic.List<Action>
            {
                () => { var g = G; g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.holdFire = true; g.cfg.camera.distance = 30; WaitReal(1.0); },
                () => {
                    var g = G; g.DebugWave(1); foreach (var e in g.swarm.enemies.ToArray()) g.KillEnemy(e);
                    g.DebugSpawn(g.swarm.LeaperType, g.player.p + new Vector2(8.5f, 3f));
                    g.DebugSpawn(g.swarm.LeaperType, g.player.p + new Vector2(-6f, -7f));
                    Time.timeScale = 0.25f; mark = g.GameTime; Debug.Log("LeaperShot: spawned");
                },
            };
            foreach (var t in T) { float tt = t; s.Add(() => At(tt)); s.Add(() => Shot($"close_{tt:F2}")); }
            s.Add(() => { G.cfg.camera.distance = 40; At(3.0f); });
            s.Add(() => Shot("far"));
            return s;
        }
        static System.Collections.Generic.List<Action> steps;
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 80) { Debug.LogError("LeaperShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); next = now + 2; return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"LeaperShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Time.timeScale = 1; SafeArea.Override = null;
            Debug.Log("LeaperShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
