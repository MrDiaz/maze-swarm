using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Cryo Station's run music, checked by log (muted): the real drop (map -> BRIEFING -> LAUNCH), music on as the hatch
    // opens, pause (15%), floor 1 won (fade out), floor 2 (back), the boss floor (intro duck, boss down), the end of
    // the run; a volume line every half second and the frame rate. Then a non-Cryo zone (no music).
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.RunMusicCheck.Run   (no -quit)
    [InitializeOnLoad]
    public static class RunMusicCheck
    {
        const string Key = "Maze.RunMusicCheck.Active";
        static int step; static bool prepared; static double next = -1, armed = -1, t0, lastVol;
        static List<Action> steps; static int f0; static double r0;
        static RunMusicCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void Note(string s) => Debug.Log($"RunMusicCheck: {EditorApplication.timeSinceStartup - t0:0.00}s {s}");
        static string Vol()
        {
            var go = GameObject.Find("RunMusic"); if (go == null) return "no RunMusic";
            var l = new List<string>(); foreach (var s in go.GetComponents<AudioSource>()) if (s.clip != null) l.Add($"{s.clip.name} {s.volume:0.00}{(s.isPlaying ? "" : " (stopped)")}");
            return l.Count == 0 ? "no clips" : string.Join(", ", l);
        }
        static Transform Find(string n) { foreach (var t in MapSelect.I.GetComponentsInChildren<Transform>(true)) if (t.name == n && t.gameObject.activeInHierarchy) return t; throw new Exception("no " + n); }
        static void TapIt(string n) { var t = Find(n); var hb = t.GetComponent<HoloButton>(); if (hb != null) hb.OnPointerClick(null); else t.GetComponent<Tap>().onTap(); }
        static List<Action> Build() => new List<Action>
        {
            () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; MapSelect.Selected = 0; G.menu.Show(); t0 = EditorApplication.timeSinceStartup; Later(1.0); },
            () => { MapSelect.I.Show(); MapSelect.I.PickSector(0); TapIt("Deploy"); Later(0.8); },
            () => { TapIt("Launch"); Note("LAUNCH"); Later(3.5); },
            () => { Note($"after the drop: state {G.state} run {G.run} floor {G.floor}; {Vol()}"); G.godMode = true; G.demo = true; f0 = Time.frameCount; r0 = EditorApplication.timeSinceStartup; Later(3.0); },
            () => { Note($"fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - r0):0}; {Vol()}"); G.TogglePause(); Note("pause"); Later(1.5); },
            () => { Note($"paused: {Vol()}"); G.TogglePause(); Note("resume"); Later(1.5); },
            () => { Note($"resumed: {Vol()}"); typeof(Game).GetMethod("GoalDone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(G, new object[] { "FLOOR CLEAR" }); Note("floor 1 goal done"); Later(2.0); },
            () => { Note($"after the floor end: state {G.state}; {Vol()}"); Later(1.0); },
            () => { if (G.state == Game.State.Choosing) G.Pick(0); Later(3.5); },
            () => { Note($"floor {G.floor}: {Vol()}"); G.DebugFloor(5); G.godMode = true; Note("boss floor"); Later(0.5); },
            () => { Note($"boss intro: {Vol()}"); Later(3.0); },
            () => { Note($"boss floor: {Vol()}"); var b = G.Boss; if (b != null) { b.hp = 0; G.KillEnemy(b); Note("boss killed"); } Later(0.4); },
            () => { Note($"boss down: {Vol()}"); Later(3.0); },
            () => { Note($"after the run: state {G.state} run {G.run}; {Vol()}"); G.ToMenu(); Later(1.2); },
            () => { Note($"back at the menu: {Vol()}"); Later(0.1); },
            () => { var def = SectorDefs.Get("acid-marsh"); Game.PendingTier = def.tier; Game.PendingSector = def; G.LoadMap(0); G.menu.Hide(); G.Restart(); G.godMode = true; Later(4.0); },
            () => { Note($"acid marsh (not Cryo): {Vol()}"); G.ToMenu(); Later(0.5); },
        };
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 150) { Debug.LogError("RunMusicCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || MapSelect.I == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; RunMusic.Log = s => Note("music: " + s); steps = Build(); Later(2); return; }
            if (now < next) return;
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"RunMusicCheck: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            RunMusic.Log = null; SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingSector = null; Game.PendingTier = 0;
            Debug.Log("RunMusicCheck: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
