using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Hazard VFX batch 1, before / after (Logs/UiShot/vfx1): each case starts the zone's floor with the kit off
    // (VfxKit.I = null: every hook falls back to the old look), then again with it on. The hero stands next to the hazard
    // (god mode), the game camera follows; the hazard's viewport point is logged for the close-up crop. Then a 10 s
    // frame-rate sample on the fire floor (demo bot), old vs new. A throwaway save; muted.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.VfxShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class VfxShot
    {
        const string Key = "Maze.VfxShot.Active", Out = "Logs/UiShot/vfx1";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps; static VfxKit kit;
        static VfxShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
            VfxKit.I = vfx ? kit : null;
            UnityEngine.Random.InitState(5);
            var def = SectorDefs.Get(zone); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart();
            if (floor > 1) g.DebugFloor(floor);
            g.godMode = true; g.demo = demo;
            Debug.Log($"VfxShot: {zone} F{floor} vfx {vfx}: map {MazeMap.MapIndex} {MazeMap.W}x{MazeMap.H} goal '{g.GoalLine}'");
        }
        // the hero next to the first cell of a letter (or a turret 'T'); returns the hazard's world point
        static Vector3 Near(char ch)
        {
            var g = G; var cells = MazeMap.Marks(ch); if (cells.Count == 0) { Debug.Log($"VfxShot: no '{ch}' cells"); return MazeMap.ToWorld(g.player.p); }
            var c = cells[cells.Count / 2];
            Vector2 best = g.player.p; float bd = 1e9f;
            for (int dz = -4; dz <= 4; dz++) for (int dx = -4; dx <= 4; dx++)
            {
                int x = c.x + dx, z = c.y + dz; if (!MazeMap.Walkable(x, z)) continue;
                char k = MazeMap.grid[z, x]; if (k == 'a' || k == '*' || k == 'f' || k == '|') continue;
                float d = Mathf.Abs(dx) + Mathf.Abs(dz); if (d >= 2 && d < bd) { bd = d; best = MazeMap.Center(x, z); }
            }
            g.player.p = best; g.player.vel = Vector2.zero;
            return MazeMap.ToWorld(MazeMap.Center(c.x, c.y));
        }
        static void LogView(string n, Vector3 w) { var vp = Camera.main.WorldToViewportPoint(w); Debug.Log($"VfxShot: view {n} {vp.x:0.000} {vp.y:0.000}"); }
        static void Spitters()
        {   // acid puddles on the light Cryo deck (the Spitter's 1.4 m and the boss slam's 2.2 m), plus a spore pod for the gas
            var gp = G.player.p;
            EnemyAI.AcidPuddle(gp + new Vector2(3.5f, -1.5f), 1.4f, 30f, 0f); EnemyAI.AcidPuddle(gp + new Vector2(-4f, 2f), 2.2f, 30f, 0f); EnemyAI.AcidPuddle(gp + new Vector2(1f, 4f), 1.4f, 30f, 0f);
            { var at = gp + new Vector2(-3f, -3f); var e = G.swarm.Spawn(at, 2, G.swarm.TypeByName("spore_pod")); e.p = at; }
            return;
            var g = G; int ti = g.swarm.TypeByName("spitter");
            for (int k = 0; k < 3; k++) { float a = k * 2.1f; var at = g.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6f; int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C); if (!MazeMap.Walkable(x, z)) continue; var e = g.swarm.Spawn(at, 2, ti); e.p = at; }
        }
        static int f0; static double r0;

        static List<Action> Case(string tag, string zone, int floor, char ch, bool spit, int diff = 0, int shots = 3, double gap = 0.6)
        {
            var l = new List<Action>();
            foreach (bool on in new[] { false, true })
            {
                bool v = on; Vector3 hz = default;
                l.Add(() => { Meta.Difficulty = diff; Start(zone, floor, v, false); WaitReal(1.0); });
                l.Add(() => { hz = Near(ch); if (spit) Spitters(); WaitReal(spit ? 4.0 : 4.0); });
                for (int i = 0; i < shots; i++)
                {
                    int k = i;
                    l.Add(() => { if (k == 0) LogView($"{tag}_{(v ? "after" : "before")}", hz); Shot($"{tag}_{(v ? "after" : "before")}_{k}"); Later(gap); });
                }
            }
            l.Add(() => { Meta.Difficulty = 0; Later(0.1); });
            return l;
        }
        static List<Action> Build()
        {
            var s = new List<Action> { () => { kit = VfxKit.I; Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; Debug.Log($"VfxShot: kit {(kit != null)}"); Later(0.5); } };
            var only = Environment.GetEnvironmentVariable("VFX_ONLY");
            if (only == null || only.Contains("1")) s.AddRange(Case("1_cryo_spitter_acid", "cryo-station", 1, '~', true));
            if (only == null || only.Contains("2")) s.AddRange(Case("2_acidmarsh_pools", "acid-marsh", 1, 'a', false));
            if (only == null || only.Contains("3")) s.AddRange(Case("3_evac_fences", "evac-deck", 2, '|', false));
            if (only == null || only.Contains("4")) s.AddRange(Case("4_evac_sentries", "evac-deck", 1, 'T', false, 2, 8, 0.3));   // Nightmare: "Abandoned Armoury" turns on the sentry turrets
            if (only == null || only.Contains("5")) s.AddRange(Case("5_kessler_slag", "kessler-refinery", 1, '*', false));
            if (only == null || only.Contains("6")) s.AddRange(Case("6_dreadnought_fire", "dreadnought", 1, 'f', false));
            foreach (bool on in only == null ? new[] { false, true } : new bool[0])
            {
                bool v = on;
                s.Add(() => { Start("dreadnought", 1, v, true); WaitReal(2.0); });
                s.Add(() => { f0 = Time.frameCount; r0 = EditorApplication.timeSinceStartup; WaitReal(10.0); });
                s.Add(() => { int n = Time.frameCount - f0; double t = EditorApplication.timeSinceStartup - r0; Debug.Log($"VfxShot: FPS dreadnought F1 demo vfx {v}: {n} frames in {t:0.0} s = {n / t:0.0} fps, enemies {G.swarm.enemies.Count}, particles {(VfxKit.I != null ? VfxKit.I.fire.particleCount + VfxKit.I.smoke.particleCount : 0)}"); Later(0.2); });
            }
            s.Add(() => { VfxKit.I = kit; G.ToMenu(); Later(0.5); });
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 280) { Debug.LogError("VfxShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (g.state == Game.State.Choosing) { g.Pick(0); Later(0.8); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"VfxShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            if (kit != null) VfxKit.I = kit;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null; SafeArea.Override = null;
            Debug.Log("VfxShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
