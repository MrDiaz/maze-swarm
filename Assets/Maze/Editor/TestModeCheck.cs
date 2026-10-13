using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // TEST MODE check through the real menu flow, with REAL saving (editor PlayerPrefs, backed up first and restored after):
    // a copy of a real save goes in as ms.progress; TEST MODE on: Evac Deck, Hive Core (a late, locked zone) and the Swarm
    // Gate launch from the map; a forced win runs every reward path; the save must come out identical. Then TEST MODE off:
    // the locks are back and a normal Cryo win does record. Shots in Logs/UiShot/testmode.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.TestModeCheck.Run   (no -quit)
    [InitializeOnLoad]
    public static class TestModeCheck
    {
        const string Key = "Maze.TestModeCheck.Active", Out = "Logs/UiShot/testmode";
        static readonly string[] Watch = { "ms.progress", "ms.best", "ms.gate.best", "ms.escape.best", "ms.grade.evac-deck", "ms.grade.hive-core", "ms.grade.cryo-station", "ms.tut.armory" };
        static Dictionary<string, string> backup = new(), before = new();
        static int step; static double next = -1, armed = -1; static Func<bool> wait; static List<Action> steps; static bool prepared;
        static TestModeCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static string Get(string k) => PlayerPrefs.HasKey(k) ? (PlayerPrefs.GetString(k, null) ?? "") + "|" + PlayerPrefs.GetInt(k, int.MinValue) + "|" + PlayerPrefs.GetFloat(k, float.NaN) : "<none>";
        static Dictionary<string, string> Snap() { var d = new Dictionary<string, string>(); foreach (var k in Watch) d[k] = Get(k); return d; }
        static void Launch(string zone)
        {
            int idx = Array.FindIndex(MapSelect.Sectors, s => s.def != null && s.def.id == zone);
            MapSelect.I.Show(); MapSelect.I.PickSector(idx); MapSelect.I.OpenBriefing();
            Debug.Log($"TestModeCheck: {zone} pin {idx} locked {MapSelect.Sectors[idx].locked}");
        }
        static void Drop() => typeof(MapSelect).GetMethod("Launch", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(MapSelect.I, null);
        static void WaitPlaying() { double t0 = EditorApplication.timeSinceStartup; wait = () => (G.state == Game.State.Playing && G.run) || EditorApplication.timeSinceStartup - t0 > 25; }
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static int Moving() { int n = 0; foreach (var e in G.swarm.enemies) if (!e.dead && !e.type.nest && (e.v.sqrMagnitude > 0.2f || e.k.sqrMagnitude > 0.2f)) n++; return n; }
        static List<Action> Build()
        {
            var l = new List<Action>
            {
                () =>
                {   // the real save copy in; TEST MODE on
                    foreach (var k in new List<string>(Watch) { "ms.test.mode", "ms.test.power", "ms.test.floor" }) backup[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k, "") : null;
                    PlayerPrefs.SetString("ms.progress", File.ReadAllText("Logs/manuel_progress_copy.json")); PlayerPrefs.Save(); Progress.Load(); Progress.Sim = false;
                    Prefs.HeroOverride = "vanguard"; Prefs.TestMode = true; Prefs.TestPower = false; Prefs.TestFloor = 1;
                    before = Snap(); MapSelect.I.Show(); Later(2);
                },
                () => { Shot("01_map_testmode_on"); Launch("hive-core"); Later(1.5); },
                () => { Shot("02_briefing_testmode_badge"); Ops.Today(); Meta.BestUpgrades(3); Progress.Save(); before = Snap(); Debug.Log("TestModeCheck: baseline taken (after the menu's own migration save, today's Daily Ops roll and the picker's hero-level entry)"); Drop(); WaitPlaying(); },
                () => { G.godMode = true; G.demo = true; Debug.Log($"TestModeCheck: hive-core playing {G.state} run {G.run} TestRun {Game.TestRun} floor {G.floor} goal '{G.GoalLine}'"); WaitReal(12); },
                () => { Debug.Log($"TestModeCheck: hive-core live: enemies {G.swarm.enemies.Count} moving {Moving()} TestRun {Game.TestRun}"); Shot("03_hivecore_ingame_badge"); G.DebugWin(); WaitReal(4); },
                () => { Shot("04_hivecore_summary_testrun"); G.ToMenu(); Later(2); },
                () => { Launch("evac-deck"); Later(1.5); },
                () => { Drop(); WaitPlaying(); },
                () => { G.godMode = true; G.demo = true; WaitReal(12); },
                () => { Debug.Log($"TestModeCheck: evac-deck live: enemies {G.swarm.enemies.Count} moving {Moving()} TestRun {Game.TestRun} goal '{G.GoalLine}'"); Shot("05_evac_ingame_badge"); G.ToMenu(); Later(2); },
                () => { Launch("swarm-gate"); Later(1.5); },
                () => { Drop(); WaitPlaying(); },
                () => { G.godMode = true; G.demo = true; WaitReal(14); },
                () => { Debug.Log($"TestModeCheck: swarm-gate live: enemies {G.swarm.enemies.Count} moving {Moving()} TestRun {Game.TestRun} floor {G.floor} goal '{G.GoalLine}'"); Shot("06_gate_ingame_badge"); G.ToMenu(); Later(2); },
                () =>
                {   // the save after three test runs (one a forced win)
                    var after = Snap(); bool same = true;
                    foreach (var k in Watch) if (before[k] != after[k]) { same = false; Debug.Log($"TestModeCheck: CHANGED {k}"); File.WriteAllText($"Logs/testmode_{k}_before.txt", before[k]); File.WriteAllText($"Logs/testmode_{k}_after.txt", after[k]); }
                    Debug.Log($"TestModeCheck: save after test runs identical: {same}");
                    Prefs.TestMode = false; MapSelect.I.Show(); Later(2);
                },
                () =>
                {
                    Shot("07_map_testmode_off");
                    int hi = Array.FindIndex(MapSelect.Sectors, s => s.def != null && s.def.id == "hive-core");
                    Debug.Log($"TestModeCheck: OFF: hive-core locked {MapSelect.Sectors[hi].locked}, evac-deck locked {MapSelect.Sectors[Array.FindIndex(MapSelect.Sectors, s => s.def != null && s.def.id == "evac-deck")].locked}");
                    Launch("cryo-station"); Later(1.5);
                },
                () => { Drop(); WaitPlaying(); },
                () => { G.godMode = true; Debug.Log($"TestModeCheck: cryo normal run TestRun {Game.TestRun}"); WaitReal(4); },
                () => { G.DebugWin(); WaitReal(4); },
                () =>
                {
                    var after = Snap(); Debug.Log($"TestModeCheck: OFF normal Cryo win recorded progress: {after["ms.progress"] != before["ms.progress"]}");
                    var a = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString("ms.progress")); var b = JsonUtility.FromJson<SaveData>(File.ReadAllText("Logs/manuel_progress_copy.json"));
                    Debug.Log($"TestModeCheck: credits {b.credits} -> {a.credits}, runs {b.runs} -> {a.runs}");
                    G.ToMenu(); Later(1);
                },
            };
            return l;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 400) { Debug.LogError("TestModeCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || MapSelect.I == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"TestModeCheck: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            foreach (var kv in backup) { if (kv.Value == null) PlayerPrefs.DeleteKey(kv.Key); else if (kv.Key == "ms.progress") PlayerPrefs.SetString(kv.Key, kv.Value); }
            PlayerPrefs.DeleteKey("ms.test.mode"); PlayerPrefs.DeleteKey("ms.test.power"); PlayerPrefs.DeleteKey("ms.test.floor"); PlayerPrefs.Save();
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Prefs.HeroOverride = null;
            Debug.Log("TestModeCheck: done (editor prefs restored)");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
