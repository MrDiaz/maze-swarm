using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Escape door sounds timing check (MUTED: read the "Maze door audio:" log lines): walk into room 1 so the door
    // seals (slam, lock +0.35 s, alarm +0.8 s), then clear the room (alarm fade, unlock, open +1.5 s). ~15 s.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.DoorAudioCheck.Run   (no -quit)
    [InitializeOnLoad]
    public static class DoorAudioCheck
    {
        const string Key = "Maze.DoorAudioCheck.Active";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;
        static DoorAudioCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void WaitGame(float s) { float t0 = G.GameTime; wait = () => G.GameTime >= t0 + s; }
        static readonly Action[] Steps =
        {
            () => { var g = G; g.LoadMap(1); g.menu.Hide(); g.Restart(); g.godMode = true; g.holdFire = true; WaitGame(0.5f); },
            () => { G.player.p = MazeMap.roomCenter[1]; Debug.Log($"DoorAudioCheck: entered room 1 t={G.escape.RunTime:F2}"); WaitGame(3f); },
            () => { var g = G; g.TogglePause(); Debug.Log("DoorAudioCheck: paused, alarm playing=" + IsAlarm()); WaitReal(0.5); },
            () => { var g = G; Debug.Log("DoorAudioCheck: still paused, alarm playing=" + IsAlarm()); g.TogglePause(); WaitGame(0.5f); },
            () => { Debug.Log("DoorAudioCheck: resumed, alarm playing=" + IsAlarm()); G.escape.DebugClear(); WaitGame(3f); },
            () => { Debug.Log("DoorAudioCheck: alarm playing=" + IsAlarm()); },
        };
        static bool IsAlarm() { var a = GameObject.Find("DoorAlarm"); return a != null && a.GetComponent<AudioSource>().isPlaying; }
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 60) { Debug.LogError("DoorAudioCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; next = now + 2; return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"DoorAudioCheck: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; SafeArea.Override = null;
            Debug.Log("DoorAudioCheck: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
