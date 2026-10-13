using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The hazard sounds, checked by log (muted): a few floors with the demo bot, each for a few seconds, logging every
    // clip HazardAudio starts (loops and one-shots), plus forced events (an acid glob, an explosion, a telegraph on the
    // hero, a lightning storm) and the frame rate per floor. Counts per clip are printed per floor.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.VfxSoundCheck.Run   (no -quit)
    [InitializeOnLoad]
    public static class VfxSoundCheck
    {
        const string Key = "Maze.VfxSoundCheck.Active";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static readonly Dictionary<string, int> counts = new(); static string floorTag; static int f0; static double r0;
        static VfxSoundCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Start(string zone, int floor, int diff = 0)
        {
            Meta.Difficulty = diff; var def = SectorDefs.Get(zone); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); if (floor > 1) g.DebugFloor(floor);
            g.godMode = true; g.demo = true;
            counts.Clear(); floorTag = $"{zone} F{floor}{(diff == 2 ? " Nightmare" : "")}"; f0 = Time.frameCount; r0 = EditorApplication.timeSinceStartup;
        }
        static void Report()
        {
            int n = Time.frameCount - f0; double t = EditorApplication.timeSinceStartup - r0;
            var l = new List<string>(); foreach (var kv in counts) l.Add($"{kv.Key} x{kv.Value}"); l.Sort();
            Debug.Log($"VfxSoundCheck: {floorTag}: {n / t:0} fps; {(l.Count == 0 ? "(none)" : string.Join(", ", l))}");
        }
        static List<Action> Floor(string zone, int floor, double secs, Action extra = null, int diff = 0) => new List<Action>
        {
            () => { Start(zone, floor, diff); WaitReal(1.5); },
            () => { extra?.Invoke(); WaitReal(secs); },
            () => { Report(); WaitReal(0.2); },
        };
        static List<Action> Build()
        {
            var s = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; WaitReal(0.5); } };
            s.AddRange(Floor("acid-marsh", 1, 6, () => { var p = G.player.p; EnemyAI.AcidPuddle(p + new Vector2(2f, 0), 1.4f, 4f, 0f); }));
            s.AddRange(Floor("kessler-refinery", 1, 7));
            s.AddRange(Floor("dreadnought", 1, 6, () => { Fx.I.Boom(MazeMap.ToWorld(G.player.p + new Vector2(3f, 0), 0.3f), 3f); }));
            s.AddRange(Floor("evac-deck", 2, 9));
            s.AddRange(Floor("evac-deck", 1, 9, () =>
            {   // the hero beside the first sentry, in its line (they shoot only with a clear line to him)
                foreach (var h in Hazards.Live) if (h is Turrets)
                {
                    var l = (System.Collections.IList)typeof(Turrets).GetField("list", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(h);
                    if (l.Count == 0) break; var tp = (Vector2)l[0].GetType().GetField("p").GetValue(l[0]);
                    for (int k = 0; k < 16; k++) { var at = tp + UnityEngine.Random.insideUnitCircle.normalized * 4f; if (MazeMap.Walkable(Mathf.FloorToInt(at.x / MazeMap.C), Mathf.FloorToInt(at.y / MazeMap.C))) { G.player.p = at; break; } }
                    G.demo = false; Debug.Log($"VfxSoundCheck: hero beside sentry at {tp}"); break;
                }
            }, 2));
            s.AddRange(Floor("cryo-station", 1, 7, () => { var p = G.player.p; EnemyAI.Tele.Circle(p, 2f, 1f, Color.red, null); ZoneMods.On[ZoneMods.Storm] = true; foreach (var h in Hazards.Live) if (h is CellVents v) { v.EruptAll(3f, 2f); Debug.Log("VfxSoundCheck: FLASH FREEZE (EruptAll)"); } }));
            s.Add(() => { ZoneMods.On[ZoneMods.Storm] = false; WaitReal(0.1); });
            s.AddRange(Floor("data-vault", 1, 8));
            s.AddRange(Floor("meltdown", 1, 7));
            s.Add(() => { Meta.Difficulty = 0; G.ToMenu(); WaitReal(0.5); });
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 240) { Debug.LogError("VfxSoundCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; HazardAudio.Log = n => { counts.TryGetValue(n, out var c); counts[n] = c + 1; }; steps = Build(); WaitReal(2); return; }
            if (wait != null) { if (!wait()) return; wait = null; }
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"VfxSoundCheck: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            HazardAudio.Log = null; ZoneMods.On[ZoneMods.Storm] = false; Meta.Difficulty = 0;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("VfxSoundCheck: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
