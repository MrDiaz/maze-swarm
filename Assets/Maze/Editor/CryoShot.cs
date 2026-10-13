using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Cryo Station "Frosted lab" captures at iPhone resolution into Logs/UiShot/cryo_*: the map at the game camera,
    // a closer view, walls + icicles, creep at a spawn, and a mid-fight frame with a wave-6 swarm.
    // Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.CryoShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class CryoShot
    {
        const string Key = "Maze.CryoShot.Active";
        static readonly string[] Out = { "Logs/UiShot" };
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;

        static CryoShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

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
        static void WaitGame(float s) { float t0 = G.GameTime; wait = () => G.GameTime >= t0 + s; }
        static void Cam(float d) => G.cfg.camera.distance = d;
        static void Teleport(Vector2 p) { var g = G; g.player.p = p; }

        static readonly Action[] Steps =
        {
            () => { var g = G; g.menu.Hide(); g.Restart(); g.godMode = true; g.holdFire = true; Cam(40); WaitGame(1.2f); },
            () => { Shot("cryo_overview"); Cam(24); WaitGame(1.0f); },
            () => { Shot("cryo_close"); Teleport(new Vector2(19 * MazeMap.C, 9.5f * MazeMap.C)); Cam(15); WaitGame(1.2f); },
            () => { Shot("cryo_walls_icicles"); Teleport(MazeMap.spawnPoints[0] + new Vector2(5, 4)); Cam(17); WaitGame(1.2f); },
            () => { Shot("cryo_creep_spawn"); var g = G; g.holdFire = false; Teleport(MazeMap.playerSpawn); Cam(28); g.demo = true; g.DebugWave(6); WaitGame(13f); },
            () => { Shot("cryo_fight_swarm"); WaitGame(3f); },
            () => { Shot("cryo_fight_swarm_2"); Debug.Log($"CryoShot: wave {G.wave}, {G.swarm.enemies.Count} hostiles"); },
        };

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 900) { Debug.LogError("CryoShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"CryoShot: step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            SafeArea.Override = null;
            Debug.Log("CryoShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
