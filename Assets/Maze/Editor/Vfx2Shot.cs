using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Hazard VFX batch 2, before / after (Logs/UiShot/vfx2): the same calls the game makes, with the kit off (VfxKit.I = null:
    // cubes and the old additive warnings) and on. Explosions (Fx.Boom) on the light Cryo deck and the dark Dreadnought floor,
    // attack warnings (EnemyAI.Tele circles and a lane) part-way through, kill sparks and dash dust (Fx.Sparks / Fx.Smoke);
    // then a 10 s frame-rate sample in a busy fight (demo bot, an explosion pair every 0.5 s), old vs new. Muted.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.Vfx2Shot.Run   (no -quit)
    [InitializeOnLoad]
    public static class Vfx2Shot
    {
        const string Key = "Maze.Vfx2Shot.Active", Out = "Logs/UiShot/vfx2";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps; static VfxKit kit;
        static Vfx2Shot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static void Start(string zone, int floor, bool vfx, bool demo)
        {
            VfxKit.I = vfx ? kit : null; UnityEngine.Random.InitState(5);
            var def = SectorDefs.Get(zone); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); if (floor > 1) g.DebugFloor(floor);
            g.godMode = true; g.demo = demo; g.swarm.Clear();
            Debug.Log($"Vfx2Shot: {zone} F{floor} vfx {vfx}");
        }
        static Vector3 W(Vector2 off, float y = 0.5f) => MazeMap.ToWorld(G.player.p + off, y);
        static void LogView(string n, Vector3 w) { var vp = Camera.main.WorldToViewportPoint(w); Debug.Log($"Vfx2Shot: view {n} {vp.x:0.000} {vp.y:0.000}"); }

        static List<Action> Boom(string tag, string zone)
        {
            var l = new List<Action>();
            foreach (bool on in new[] { false, true })
            {
                bool v = on; string s = v ? "after" : "before";
                l.Add(() => { Start(zone, 1, v, false); WaitReal(4.5); });   // past the floor banner
                l.Add(() => { G.swarm.Clear(); var p = W(new Vector2(4f, 0f), 0.4f); LogView($"{tag}_{s}", p); Fx.I.Boom(p, 3.2f); Later(0.04); });
                foreach (var (dt, k) in new[] { (0.06, 0), (0.08, 1), (0.15, 2), (0.3, 3), (0.5, 4) }) { var kk = k; var d = dt; l.Add(() => { Shot($"{tag}_{s}_{kk}"); Later(d); }); }
            }
            return l;
        }
        static List<Action> Tele(string tag)
        {
            var l = new List<Action>();
            foreach (bool on in Environment.GetEnvironmentVariable("VFX2_AFTERFIRST") != null ? new[] { true, false } : new[] { false, true })   // (warnings are pooled: the first run's material sticks)
            {
                bool v = on; string s = v ? "after" : "before";
                l.Add(() => { Start("cryo-station", 1, v, false); WaitReal(4.5); });
                l.Add(() =>
                {
                    G.swarm.Clear(); var pp = G.player.p;
                    EnemyAI.Tele.Circle(pp + new Vector2(-4.5f, 1.5f), 2.2f, 2.0f, new Color(1f, 0.25f, 0.2f), null);          // a strike / slam
                    EnemyAI.Tele.Circle(pp + new Vector2(4.5f, 2.5f), 1.4f, 2.0f, new Color(0.55f, 1f, 0.2f), null);           // a Spitter's landing spot
                    EnemyAI.Tele.Lane(pp + new Vector2(-3f, -4f), new Vector2(1f, 0.15f).normalized, 9f, 1.4f, 2.0f, new Color(1f, 0.8f, 0.2f), null);   // a charge lane
                    LogView($"{tag}_{s}", MazeMap.ToWorld(pp)); WaitReal(0.5);
                });
                l.Add(() => { Shot($"{tag}_{s}_0"); Later(0.6); });
                l.Add(() => { Shot($"{tag}_{s}_1"); Later(0.3); });
            }
            return l;
        }
        static List<Action> Sparks(string tag)
        {
            var l = new List<Action>();
            foreach (bool on in new[] { false, true })
            {
                bool v = on; string s = v ? "after" : "before";
                l.Add(() => { Start("dreadnought", 1, v, false); WaitReal(4.5); });
                l.Add(() =>
                {
                    G.swarm.Clear(); LogView($"{tag}_{s}", W(Vector2.zero));
                    for (int i = 0; i < 4; i++) Fx.I.Sparks(W(new Vector2(-4f + i * 2.6f, 2.5f), 0.8f), 18, i % 2 == 0 ? new Color(1f, 0.7f, 0.25f) : new Color(0.4f, 0.9f, 1f), 6f, 0.5f, 0.12f);   // kill / crit bursts
                    for (int i = 0; i < 3; i++) Fx.I.Smoke(W(new Vector2(-2.5f + i * 1.2f, -2f), 0.2f), 6, new Color(0.75f, 0.78f, 0.82f), 1.2f, 0.7f, 0.9f);           // dash dust
                    Later(0.12);
                });
                l.Add(() => { Shot($"{tag}_{s}_0"); Later(0.25); });
                l.Add(() => { Shot($"{tag}_{s}_1"); Later(0.2); });
            }
            return l;
        }
        static int f0; static double r0; static double boomT;
        static List<Action> Build()
        {
            var only = Environment.GetEnvironmentVariable("VFX2_ONLY");
            var s = new List<Action> { () => { kit = VfxKit.I; Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; Debug.Log($"Vfx2Shot: kit {(kit != null)}"); Later(0.5); } };
            if (only == null || only.Contains("1")) s.AddRange(Boom("1_boom_cryo", "cryo-station"));
            if (only == null || only.Contains("2")) s.AddRange(Boom("2_boom_dark", "dreadnought"));
            if (only == null || only.Contains("3")) s.AddRange(Tele("3_telegraphs"));
            if (only == null || only.Contains("4")) s.AddRange(Sparks("4_sparks_dust"));
            if (only == null || only.Contains("5"))
                foreach (bool on in new[] { false, true })
                {   // a busy fight: the demo bot on Swarm Gate floor 10, plus two explosions every half second near the hero
                    bool v = on;
                    s.Add(() => { Start("swarm-gate", 10, v, true); WaitReal(2.0); });
                    s.Add(() => { f0 = Time.frameCount; r0 = EditorApplication.timeSinceStartup; boomT = 0; wait = () =>
                    {
                        double t = EditorApplication.timeSinceStartup - r0;
                        if (t >= boomT) { boomT += 0.5; Fx.I.Boom(W(UnityEngine.Random.insideUnitCircle * 6f, 0.4f), 3f); Fx.I.Boom(W(UnityEngine.Random.insideUnitCircle * 6f, 0.4f), 2.2f); }
                        return t >= 10.0;
                    }; });
                    s.Add(() => { int n = Time.frameCount - f0; double t = EditorApplication.timeSinceStartup - r0; Debug.Log($"Vfx2Shot: FPS swarm-gate F10 demo + 40 explosions, vfx {v}: {n} frames in {t:0.0} s = {n / t:0.0} fps, enemies {G.swarm.enemies.Count}, blast {(VfxKit.I != null && VfxKit.I.blast != null ? VfxKit.I.blast.particleCount : 0)} smoke {(VfxKit.I != null ? VfxKit.I.smoke.particleCount : 0)}"); Shot($"5_busy_{(v ? "after" : "before")}"); Later(0.2); });
                }
            s.Add(() => { VfxKit.I = kit; G.ToMenu(); Later(0.5); });
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 300) { Debug.LogError("Vfx2Shot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (g.state == Game.State.Choosing) { g.Pick(0); Later(0.8); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"Vfx2Shot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            if (kit != null) VfxKit.I = kit;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; SafeArea.Override = null;
            Debug.Log("Vfx2Shot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
