using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Door alignment captures (SHOT_OUT, default Logs/UiShot/doorfix): every door on the door floors (Evac Deck F1 + F3,
    // Quarantine Ward F2, plus the legacy Escape Test), each top-down (an orthographic camera over the door: the wall line
    // and the door must agree) and from the gameplay camera with the hero parked beside it. Muted, god mode, a throwaway save.
    //   SHOT_OUT=Logs/UiShot/doorfix/before Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.DoorCheck.Run
    [InitializeOnLoad]
    public static class DoorCheck
    {
        const string Key = "Maze.DoorCheck.Active";
        static readonly string Out = Environment.GetEnvironmentVariable("SHOT_OUT") ?? "Logs/UiShot/doorfix";
        static readonly (string zone, int floor)[] Targets = { ("evac-deck", 1), ("evac-deck", 3), ("quarantine-ward", 2), ("", 0) };   // "" = the legacy Escape Test map
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static DoorCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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

        static List<Action> Build()
        {
            var l = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { credits = 1000, rank = 6 }); Later(0.5); } };
            foreach (var (zone, floor) in Targets)
            {
                string z = zone; int f = floor;
                l.Add(() =>
                {
                    Prefs.HeroOverride = "sentinel"; var g = G;
                    if (z == "") { Game.PendingSector = null; g.LoadMap(1); }
                    else { var def = SectorDefs.Get(z); Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null; g.LoadMap(0); }
                    g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = false; WaitGame(1.5f);
                });
                if (f > 1) l.Add(() => { G.DebugFloor(f); WaitGame(1.5f); });
                for (int i = 0; i < 4; i++)
                {
                    int di = i;
                    l.Add(() =>
                    {   // park the hero beside door di (a walkable side), clear the swarm
                        if (di >= MazeMap.doors.Count) return;
                        var d = MazeMap.doors[di]; var g = G; g.swarm.Clear();
                        foreach (var o in new[] { new Vector2(0, -3.2f), new Vector2(0, 3.2f), new Vector2(-3.2f, 0), new Vector2(3.2f, 0) })
                        {
                            var p = d.center + o; int x = Mathf.FloorToInt(p.x / MazeMap.C), zz = Mathf.FloorToInt(p.y / MazeMap.C);
                            if (MazeMap.Walkable(x, zz)) { g.player.p = p; g.player.vel = Vector2.zero; break; }
                        }
                        Later(1.0);
                    });
                    l.Add(() =>
                    {
                        if (di >= MazeMap.doors.Count) return;
                        var d = MazeMap.doors[di]; string n = $"{(z == "" ? "escape-test" : z)}_f{f}_door{di}_{d.r.width}x{d.r.height}";
                        Directory.CreateDirectory(Out); UiShot.Shot(n + "_game", new[] { Out });
                        TopDown(d.center, Path.Combine(Out, n + "_top.png"));
                    });
                }
            }
            return l;
        }

        // an orthographic camera straight down over the door (7 m half-height), rendered to a PNG
        static void TopDown(Vector2 at, string file)
        {
            var go = new GameObject("DoorCheckCam"); var cam = go.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 7f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.transform.position = MazeMap.ToWorld(at, 30f); cam.transform.rotation = Quaternion.Euler(90, 0, 0); cam.nearClipPlane = 0.1f; cam.farClipPlane = 60f;
            var rt = new RenderTexture(900, 900, 24); cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(900, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); tex.Apply();
            RenderTexture.active = prev; File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("DoorCheck: " + file);
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 300) { Debug.LogError("DoorCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"DoorCheck: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; Prefs.HeroOverride = null;
            Debug.Log("DoorCheck: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
