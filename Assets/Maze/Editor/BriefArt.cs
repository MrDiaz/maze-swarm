using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The mission briefing's preview: every drop zone's real floor-1 layout (its authored map, biome and props, no
    // enemies or hero) from above, slightly tilted, into Resources/UI/Arena/<zone id>.jpg. A throwaway save; muted.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.BriefArt.Run   (no -quit)
    [InitializeOnLoad]
    public static class BriefArt
    {
        const string Key = "Maze.BriefArt.Active", Dir = "Assets/Maze/Resources/UI/Arena";
        static int step; static bool prepared; static double next = -1, armed = -1; static List<Action> steps;
        static BriefArt() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static bool testWas;
        static List<Action> Build()
        {
            var l = new List<Action> { () => { testWas = Prefs.TestMode; Prefs.TestMode = false; Progress.Sim = true; Progress.UseTemp(new SaveData()); Directory.CreateDirectory(Dir); Later(0.5); } };
            var only = Environment.GetEnvironmentVariable("BRIEFART_ZONES");
            foreach (var d in SectorDefs.All)
            {
                if (!string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), d.id) < 0) continue;
                var def = d;
                l.Add(() =>
                {
                    var g = G; Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
                    g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = true; Later(1.0);
                });
                l.Add(() => Render(G, def.id));
            }
            l.Add(() => { G.ToMenu(); Later(0.5); });
            return l;
        }
        // the floor from above, tilted ~28 degrees for depth, framed to a 2:1 image
        static void Render(Game g, string id)
        {
            g.swarm.Clear(); Fx.I.ClearAll(); g.player.SetVisible(false);
            float w = MazeMap.W * MazeMap.C, h = MazeMap.H * MazeMap.C;
            var center = new Vector3(w / 2, 0, -h / 2);
            var c = new GameObject("ArenaCam").AddComponent<Camera>();
            c.enabled = false; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.03f, 0.07f, 0.11f, 1); c.nearClipPlane = 0.5f; c.farClipPlane = 900;
            c.fieldOfView = 30; c.aspect = 2f;
            const float pitch = 62f;
            // distance so the whole floor fits (vertical: the depth foreshortened; horizontal: the width)
            float vHalf = Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad), hHalf = vHalf * c.aspect;
            float needV = (h * Mathf.Sin(pitch * Mathf.Deg2Rad) * 0.5f + 2f) / vHalf, needH = (w * 0.5f + 1.5f) / hHalf;
            float dist = Mathf.Max(needV, needH) * 0.93f;   // the outer wall just cropped: the floor fills the frame
            var rot = Quaternion.Euler(pitch, 0, 0);
            c.transform.SetPositionAndRotation(center - rot * Vector3.forward * dist, rot);
            const int W = 1280, H = 640;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            c.targetTexture = rt; c.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev; c.targetTexture = null;
            File.WriteAllBytes($"{Dir}/{id}.jpg", tex.EncodeToJPG(88));
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(c.gameObject);
            g.player.SetVisible(true);
            Debug.Log($"BriefArt: wrote {id} (map {MazeMap.MapIndex}, {MazeMap.W}x{MazeMap.H}, floor {g.floor})");
            Later(0.2);
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 300) { Debug.LogError("BriefArt: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { AudioListener.volume = 0; AudioListener.pause = true; prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"BriefArt: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            Prefs.TestMode = testWas;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("BriefArt: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
