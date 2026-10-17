using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // The hero on the floor, muted, HUD off, at the game camera (run with MAZE_HERO=vanguard or sentinel): per zone an idle
    // frame, a running frame (the demo bot, no enemies) and firing frames (a small tanky pack to the right; the Heavy in
    // Barrage), plus the hero's clips and the editor fps.   VG_OUT: output folder; VG_TAG: file prefix.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.VanguardShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class VanguardShot
    {
        const string Key = "Maze.VanguardShot.Active";
        static int step; static bool prepared, wasSoft, pack; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static string Out => Environment.GetEnvironmentVariable("VG_OUT") ?? "Design/Captures/vanguard-2026-10-14";
        static string Tag => Environment.GetEnvironmentVariable("VG_TAG") ?? "after";
        static VanguardShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => Object.FindAnyObjectByType<Game>();
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Start(string zone)
        {
            var def = SectorDefs.Get(zone); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = false; g.holdFire = true; g.swarm.Clear(); pack = false;
            Debug.Log($"VanguardShot: {zone}: VgMinigun {g.VgMinigun}, CryoSoft {CryoSoftEnvironment.Active}");
        }
        static void Hold() { var g = G; typeof(Game).GetField("toSpawn", F).SetValue(g, 0); typeof(Game).GetField("breakT", F).SetValue(g, 99f); }
        static float packT;
        static void KeepPack()
        {
            var g = G; if (g == null || g.player == null) return; Hold(); if (!pack) return;
            foreach (var e in g.swarm.enemies)
                if ((e.p - g.player.p).sqrMagnitude < 4.5f * 4.5f) { var to = g.player.p + new Vector2(UnityEngine.Random.Range(7f, 9.5f), UnityEngine.Random.Range(-1.8f, 1.8f)); if (MazeMap.Walkable(Mathf.FloorToInt(to.x / MazeMap.C), Mathf.FloorToInt(to.y / MazeMap.C))) e.p = to; }
            g.player.ammo[g.player.weapon] = Mathf.Max(g.player.ammo[g.player.weapon], 30);
            if (Time.time < packT) return; packT = Time.time + 0.25f;
            if (g.swarm.enemies.Count >= 4) return;
            for (int k = 0; k < 12; k++)
            {
                var at = g.player.p + new Vector2(UnityEngine.Random.Range(6.5f, 9.5f), UnityEngine.Random.Range(-1.8f, 1.8f));
                if (MazeMap.Walkable(Mathf.FloorToInt(at.x / MazeMap.C), Mathf.FloorToInt(at.y / MazeMap.C))) { var e = g.DebugSpawn(0, at); e.hp = 1e6f; break; }
            }
        }
        static Texture2D Frame(int w, int h)
        {
            var cam = Camera.main;
            var hdr = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear) { antiAliasing = 4 };
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            var pt = cam.targetTexture; cam.targetTexture = hdr; cam.Render(); cam.targetTexture = pt; Graphics.Blit(hdr, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev; hdr.Release(); rt.Release();
            return tex;
        }
        static void Shot(string n)
        {
            Directory.CreateDirectory(Out); G.hud.gameObject.SetActive(false);
            var t = Frame(1600, 738); File.WriteAllBytes(Path.Combine(Out, $"{Tag}_{n}.png"), t.EncodeToPNG()); Object.DestroyImmediate(t);
            G.hud.gameObject.SetActive(true);
        }
        // a burst of consecutive 60 fps frames (firing: some catch the flash)
        static void Burst(string n, int frames)
        {
            int i = 0; Time.captureFramerate = 60;
            wait = () => { Shot($"{n}_{i:00}"); if (++i < frames) return false; Time.captureFramerate = 0; return true; };
        }
        static List<Action> Zone(string zone, string z)
        {
            return new List<Action>
            {
                () => { Start(zone); WaitReal(4.5); },
                () => { Shot($"{z}_idle"); Later(0.1); },
                () => { G.demo = true; WaitReal(1.6); },
                () => { Burst($"{z}_run", 4); },
                () => { G.demo = false; G.player.vel = Vector2.zero; G.holdFire = false; pack = true; if (G.VgMinigun) Abilities.BarrageT = 30f; WaitReal(2.0); },
                () => { Burst($"{z}_fire", 12); },
                () => { int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                        wait = () => { if (EditorApplication.timeSinceStartup - t0 < 4) return false; Debug.Log($"VanguardShot: {zone} firing{(G.VgMinigun ? " + Barrage" : "")}: editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - t0):0}"); return true; }; },
                () => { pack = false; Abilities.BarrageT = 0; G.holdFire = true; G.swarm.Clear(); Later(0.3); },
            };
        }
        static List<Action> Build()
        {
            var s = new List<Action> { () => {
                Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; wasSoft = CryoSoftEnvironment.Enabled; CryoSoftEnvironment.Enabled = true;
                var a = G.player.GetType().GetField("anim", F)?.GetValue(G.player) as Animation; var clips = new List<string>(); if (a != null) foreach (AnimationState st in a) clips.Add($"{st.name} {st.length:0.00}s");
                Debug.Log($"VanguardShot: hero clips [{string.Join(", ", clips)}]"); Later(0.5); } };
            s.Add(() => { UiShot.Prepare(); Directory.CreateDirectory(Out); UiShot.Shot($"{Tag}_main_menu", new[] { Out }); Later(0.3); });   // the menu's gun preview (the Heavy: the minigun)
            if (Environment.GetEnvironmentVariable("VG_MENU_ONLY") == "1") { s.Add(() => Later(0.1)); return s; }
            s.AddRange(Zone("cryo-station", "cryo"));
            s.AddRange(Zone("acid-marsh", "acid"));
            s.Add(() => { G.ToMenu(); Later(0.5); });
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 300) { Debug.LogError("VanguardShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; steps = Build(); Later(2); return; }
            KeepPack();
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"VanguardShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            Time.captureFramerate = 0; CryoSoftEnvironment.Enabled = wasSoft; Abilities.BarrageT = 0;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("VanguardShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
