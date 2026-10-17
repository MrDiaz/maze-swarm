using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The CryoSoft room through the normal run start (as on device, Enabled on): the first Cryo floor with the demo bot for
    // 8 s (room, preset, glow, the hero moving and staying on walkable cells), then Acid Marsh floor 1 (no room). Muted.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.CryoSoftFlowCheck.Run   (no -quit)
    [InitializeOnLoad]
    public static class CryoSoftFlowCheck
    {
        const string Key = "Maze.CryoSoftFlowCheck.Active";
        static int step; static bool prepared, was; static double armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static Vector2 p0; static float travelled; static int offCells, samples;
        static CryoSoftFlowCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Start(string zone)
        {
            var def = SectorDefs.Get(zone); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = true;
        }
        static void Report(string tag)
        {
            var info = UnityEngine.Object.FindAnyObjectByType<CryoSoftSceneInfo>(); var g = G;
            var vol = GameObject.Find("Cryo room glow (bloom)");
            var cam = Camera.main != null ? UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(Camera.main) : null;
            Debug.Log($"CryoSoftFlowCheck: {tag}: layout {MazeMap.LayoutName(MazeMap.MapIndex)}, floor {g.floor}, CryoSoft active {CryoSoftEnvironment.Active}, room {(info != null)}, preset {(info != null ? info.preset : -1)}, glow volume {(vol != null)}, camera post {(cam != null && cam.renderPostProcessing)}; hero travelled {travelled:0.0} m, samples off walkable cells {offCells}/{samples}, hp {g.player.hp}");
        }
        static List<Action> Build()
        {
            var s = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; was = CryoSoftEnvironment.Enabled; CryoSoftEnvironment.Enabled = true; WaitReal(0.5); } };
            foreach (var z in new[] { "cryo-station", "acid-marsh" })
            {
                var zone = z;
                s.Add(() => { Start(zone); WaitReal(1.0); });
                s.Add(() => { p0 = G.player.p; travelled = 0; offCells = 0; samples = 0; var last = p0; double t0 = EditorApplication.timeSinceStartup; wait = () =>
                {
                    var p = G.player.p; travelled += (p - last).magnitude; last = p; samples++;
                    if (!MazeMap.Walkable(Mathf.FloorToInt(p.x / MazeMap.C), Mathf.FloorToInt(p.y / MazeMap.C))) offCells++;
                    return EditorApplication.timeSinceStartup >= t0 + 8; }; });
                s.Add(() => { Report(zone); WaitReal(0.2); });
            }
            s.Add(() => { G.ToMenu(); WaitReal(0.5); });
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 180) { Debug.LogError("CryoSoftFlowCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; steps = Build(); WaitReal(2); return; }
            if (wait != null) { if (!wait()) return; wait = null; }
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"CryoSoftFlowCheck: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            CryoSoftEnvironment.Enabled = was;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("CryoSoftFlowCheck: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
