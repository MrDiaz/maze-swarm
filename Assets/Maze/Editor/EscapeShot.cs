using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Escape floor captures at iPhone resolution into Logs/UiShot/escape_*: the start room (guide arrow, open door),
    // a sealed door mid-fight, a room clear, the extraction countdown and the FLOOR ESCAPED screen. The demo bot plays
    // (god mode on; staged by jumping between rooms, muted, no fast-forward).
    // Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.EscapeShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class EscapeShot
    {
        const string Key = "Maze.EscapeShot.Active";
        static readonly string[] Out = { "Logs/UiShot" };
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;

        static EscapeShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Shot(string n) => UiShot.Shot(n, Out);
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }

        // staged (no fast-forward): jump into a room, let it fight a few seconds, clear it, jump to the lift
        static void WaitGame(float sec) { float t0 = G.GameTime; wait = () => G.GameTime >= t0 + sec; }
        static readonly Action[] Steps =
        {
            () => { var g = G; g.LoadMap(1); g.menu.Hide(); g.Restart(); g.godMode = true; g.cfg.camera.distance = 34; WaitGame(1.5f); },
            () => { Shot("escape_start"); G.escape.DebugEnter(1); G.demo = true; WaitGame(7f); },
            () => { Shot("escape_door_locked_fight"); G.escape.DebugClear(); WaitGame(0.5f); },
            () => { Shot("escape_room_clear"); G.demo = false; G.escape.DebugToLift(); WaitGame(0.6f); },
            () => { G.player.p = G.escape.Pad; WaitGame(1.6f); },
            () => { Shot("escape_extracting"); wait = () => G.state == Game.State.Won; },
            () => { Later(2.0); },
            () => { Shot("escape_won"); },
        };

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 75) { Debug.LogError("EscapeShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.escape == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"EscapeShot: step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            SafeArea.Override = null;
            Debug.Log("EscapeShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
