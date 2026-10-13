using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // HUNT floor check through the real entry (map pin, briefing, DROP, countdown, drop transition, Game.Deploy) with a
    // copy of a real save (Logs/manuel_progress_copy.json; never written back). The bot plays the floors before the HUNT
    // floor (god mode); on the HUNT floor it logs the state every 4 s (goal line, nests, enemies alive / moving / distance)
    // and takes screenshots (Logs/UiShot/hunt/<tag>). Env HUNT_ZONES "zone:floor,..." (default cryo-station:2,kessler-refinery:3).
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.HuntCheck.Run   (no -quit)
    [InitializeOnLoad]
    public static class HuntCheck
    {
        const string Key = "Maze.HuntCheck.Active";
        static readonly string Out = Environment.GetEnvironmentVariable("SHOT_OUT") ?? "Logs/UiShot/hunt";
        static int ti = -1, huntFloor; static string zone; static double phaseAt, armed = -1, lastLog; static int phase;
        static List<(string z, int f)> targets; static Dictionary<object, Vector2> lastPos = new();
        static HuntCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 560) { Debug.LogError("HuntCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || MapSelect.I == null) return;
            if (targets == null)
            {
                UiShot.Prepare();
                targets = new List<(string, int)>();
                foreach (var s in (Environment.GetEnvironmentVariable("HUNT_ZONES") ?? "cryo-station:2,kessler-refinery:3").Split(','))
                { var p = s.Split(':'); targets.Add((p[0], int.Parse(p[1]))); }
                Next(now); return;
            }
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            switch (phase)
            {
                case 1:   // the map: pick the pin, open the briefing, DROP (the briefing's button calls Launch)
                    if (now - phaseAt < 1.5) return;
                    int idx = Array.FindIndex(MapSelect.Sectors, s => s.def != null && s.def.id == zone);
                    MapSelect.I.PickSector(idx); MapSelect.I.OpenBriefing();
                    Debug.Log($"HuntCheck: {zone} pin {idx} locked {MapSelect.Sectors[idx].locked}");
                    phase = 2; phaseAt = now; return;
                case 2:
                    if (now - phaseAt < 1.5) return;
                    Shot($"{zone}_0_briefing");
                    typeof(MapSelect).GetMethod("Launch", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(MapSelect.I, null);
                    phase = 3; phaseAt = now; return;
                case 3:   // the countdown and the drop transition run on their own until the floor plays
                    if (g.state != Game.State.Playing || !g.run) { if (now - phaseAt > 30) { Debug.LogError($"HuntCheck: {zone} never started (state {g.state})"); Next(now); } return; }
                    g.godMode = true; g.demo = true; Debug.Log($"HuntCheck: {zone} landed after {now - phaseAt:F1} s, floor {g.floor} goal {g.GoalLine}");
                    phase = 4; phaseAt = now; return;
                case 4:   // the bot plays up to the HUNT floor
                    if (g.floor < huntFloor) { if (now - phaseAt > 200) { Debug.LogError("HuntCheck: never reached the HUNT floor"); Next(now); } return; }
                    Debug.Log($"HuntCheck: {zone} HUNT floor {g.floor} reached: {g.GoalLine}");
                    phase = 5; phaseAt = now; lastLog = now; lastPos.Clear(); return;
                case 5:   // the HUNT floor: log, shoot, wait for it to finish
                    double t = now - phaseAt;
                    if (now - lastLog >= 4)
                    {
                        lastLog = now; int alive = 0, moving = 0, nests = 0; float dsum = 0; var idle = new Dictionary<string, int>(); var nestInfo = new List<string>();
                        foreach (var e in g.swarm.enemies)
                        {
                            if (e.dead) continue; if (e.type.nest) { nests++; if (!e.egg) nestInfo.Add($"{(e.p - g.player.p).magnitude:F0}m hp{e.hp:F0}"); continue; } alive++; dsum += (e.p - g.player.p).magnitude;
                            if (lastPos.TryGetValue(e, out var lp) && (e.p - lp).magnitude > 0.3f) moving++; else if (lastPos.ContainsKey(e)) { idle.TryGetValue(e.type.name + (e.egg ? "(egg)" : "") + "/ai" + e.aiState, out int c); idle[e.type.name + (e.egg ? "(egg)" : "") + "/ai" + e.aiState] = c + 1; int cx = Mathf.FloorToInt(e.p.x / MazeMap.C), cz = Mathf.FloorToInt(e.p.y / MazeMap.C); Debug.Log($"HuntCheck idle: {e.type.name} at {e.p} cell '{MazeMap.tag[cz, cx]}'/'{MazeMap.grid[cz, cx]}' flow {MazeMap.flow[cz * MazeMap.W + cx]} spawnT {e.spawnT:F1} speed {e.speed:F1} frozen {e.frozen:F1} slow {e.slowT:F1} dHero {(e.p - g.player.p).magnitude:F1} under {e.under} field {(e.field != null)}"); } lastPos[e] = e.p;
                        }
                        Debug.Log($"HuntCheck: {zone} F{g.floor} t{t:F0} state {g.state} goal '{g.GoalLine}' nests {nests} enemies {alive} moving {moving} meanDist {(alive > 0 ? dsum / alive : 0):F1} heroHp {g.player.hp:F0} reach {g.Reach:F1} weapon {g.cfg.weapons[g.player.weapon].name} idle [{string.Join(", ", idle)}] realNests [{string.Join("; ", nestInfo)}]");
                    }
                    if (t > 6 && t < 6.2) Shot($"{zone}_f{huntFloor}_a_start");
                    if (t > 20 && t < 20.2) Shot($"{zone}_f{huntFloor}_b_mid");
                    if (g.floor > huntFloor || g.state == Game.State.Won || g.state == Game.State.Dead)
                    { Shot($"{zone}_f{huntFloor}_c_after"); Debug.Log($"HuntCheck: {zone} HUNT floor DONE after {t:F0} s (floor now {g.floor}, state {g.state})"); Next(now); return; }
                    if (t > 150) { Shot($"{zone}_f{huntFloor}_c_stuck"); Debug.LogError($"HuntCheck: {zone} HUNT floor STUCK after {t:F0} s: {g.GoalLine}"); Next(now); }
                    return;
            }
        }
        static void Next(double now)
        {
            ti++;
            if (ti >= targets.Count) { Finish(); return; }
            (zone, huntFloor) = targets[ti];
            var json = File.ReadAllText("Logs/manuel_progress_copy.json");
            var sd = JsonUtility.FromJson<SaveData>(json); Progress.Sim = true; Progress.UseTemp(sd);   // a copy: never saved
            if (zone != "cryo-station" && zone != "evac-deck")
                foreach (var z in SectorDefs.All) if (!z.endless && z.num > 0 && z.num < SectorDefs.Get(zone).num && !sd.cleared.Contains(z.id)) sd.cleared.Add(z.id);   // (unlock later zones in the copy)
            Prefs.HeroOverride = Environment.GetEnvironmentVariable("HUNT_HERO") ?? "vanguard";   // his saved pick
            var g = G; if (g.run) g.ToMenu();
            MapSelect.I.Show(); phase = 1; phaseAt = now;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Prefs.HeroOverride = null;
            Debug.Log("HuntCheck: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
