using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The Select Drop Zone page (Logs/UiShot/mapdeclutter/<SHOT_OUT>): nothing selected, an unlocked zone, a locked
    // zone, test mode on, then the briefing. A throwaway mid-campaign save; the test prefs and the picked sector are
    // put back afterwards. Muted.
    //   SHOT_OUT=after UISHOT_SIZE=1920x1080 Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.MapShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class MapShot
    {
        const string Key = "Maze.MapShot.Active";
        static string Out => "Logs/UiShot/mapdeclutter/" + (Environment.GetEnvironmentVariable("SHOT_OUT") ?? "shots");
        static readonly string[] Keep = { "ms.test.mode", "ms.test.power", "ms.test.floor", "ms.sector" };
        static readonly Dictionary<string, int?> backup = new();
        static int step; static bool prepared; static double next = -1, armed = -1; static List<Action> steps;
        static MapShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static int open = 0, shut = 1;
        static void FindPins()
        {
            var S = MapSelect.Sectors;
            open = Array.FindIndex(S, x => x.def != null && !x.locked && !Progress.Cleared(x.def)); shut = Array.FindIndex(S, x => x.locked);
            Debug.Log($"MapShot: unlocked pin {open} {S[open].name}, locked pin {shut} {S[shut].name}");
        }
        const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        static Func<bool> wait;
        static void Info() => typeof(MapSelect).GetMethod("ToggleInfo", BF)?.Invoke(MapSelect.I, null);
        static void Click(object b) { if (b is HoloButton hb) { Debug.Log($"MapShot: click {hb.name}"); hb.OnPointerClick(null); } }
        static void TapIt(string n) { var t = Find(n); Debug.Log($"MapShot: tap {n}"); var hb = t.GetComponent<HoloButton>(); if (hb != null) hb.OnPointerClick(null); else t.GetComponent<Tap>().onTap(); }
        static Transform Find(string n) { foreach (var t in MapSelect.I.GetComponentsInChildren<Transform>(true)) if (t.name == n && t.gameObject.activeInHierarchy) return t; throw new Exception("no " + n); }
        static List<Action> Build()
        {
            var save = new SaveData { sectorsCleared = 3, runs = 9 };
            foreach (var id in new[] { "cryo", "rust", "verdant", "dust", "kessler" }) save.cleared.Add(id);
            foreach (var st in new[] { "cryo:0", "cryo:1", "cryo:2", "rust:0", "rust:2", "verdant:0", "dust:0", "dust:1", "kessler:0" }) save.stars.Add(st);
            return new List<Action>
            {
                () =>
                {
                    foreach (var k in Keep) backup[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : null;
                    Prefs.TestMode = false;
                    Progress.Sim = true; Progress.UseTemp(save); FindPins(); MapSelect.Selected = open; MapSelect.I.Show(); Later(2.5);
                },
                () => { Shot("1_nothing_selected"); Info(); Later(0.8); },
                () => { Shot("1b_info_panel"); Info(); MapSelect.I.PickSector(open); Later(1.5); },
                () => { Shot("2_unlocked_selected"); MapSelect.I.PickSector(shut); Later(1.5); },
                () => { Shot("3_locked_selected"); MapSelect.I.Hide(); Prefs.TestMode = true; Prefs.TestFloor = 3; MapSelect.I.Show(); Later(1.0); },
                () => { MapSelect.I.PickSector(shut); Later(1.5); },
                () => { Shot("4_testmode_selected"); MapSelect.I.OpenBriefing(); Later(1.5); },
                () => { Shot("5_briefing_testmode"); MapSelect.I.Hide(); Prefs.TestMode = false; MapSelect.I.Show(); MapSelect.I.PickSector(open); MapSelect.I.OpenBriefing(); Later(1.5); },
                () => { Shot("6_briefing"); MapSelect.I.Hide(); Later(1.0); },
                // the real flow, by the buttons' own handlers: main menu DEPLOY -> tap the pin -> DEPLOY -> LAUNCH -> the drop
                () => { Shot("7a_flow_main_menu"); Click(typeof(MainMenu).GetField("deployBtn", BF).GetValue(G.menu)); Later(1.5); },
                () => { Debug.Log($"MapShot: flow map open {MapSelect.I.Open}"); Shot("7b_flow_map"); Find("Pin" + open).GetComponent<Tap>().onTap(); Later(1.5); },
                () => { Shot("7c_flow_zone_picked"); TapIt("Deploy"); Later(1.5); },
                () => { Shot("7d_flow_briefing"); TapIt("Launch"); Later(0.8); },
                () => { Shot("7e_flow_drop"); double t0 = EditorApplication.timeSinceStartup; wait = () => (G.state == Game.State.Playing && G.run) || EditorApplication.timeSinceStartup - t0 > 25; },
                () => { G.godMode = true; Later(3); },
                () => { Debug.Log($"MapShot: flow in game: state {G.state} run {G.run} zone {G.sector?.id} floor {G.floor} goal '{G.GoalLine}' enemies {G.swarm.enemies.Count} TestRun {Game.TestRun}"); Shot("7f_flow_ingame"); G.ToMenu(); Later(1); },
            };
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 200) { Debug.LogError("MapShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || MapSelect.I == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (G.state == Game.State.Choosing) { G.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"MapShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            foreach (var kv in backup) { if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value); else PlayerPrefs.DeleteKey(kv.Key); }
            PlayerPrefs.Save();
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; SafeArea.Override = null;
            Debug.Log("MapShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
