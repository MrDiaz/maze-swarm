using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.EditorTools
{
    // The mission briefing (Logs/UiShot/briefing/<SHOT_OUT>): Cryo / Vanguard / weekly on (the concept's case), then the
    // controls by their own handlers (a locked tier refused, HARD picked where it is open, BACK, LAUNCH into the run) and
    // other zone / hero / test-mode combinations. Throwaway saves; the editor prefs it touches are put back. Muted.
    //   SHOT_OUT=r1 UISHOT_SIZE=1672x941 Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.DeployShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class DeployShot
    {
        const string Key = "Maze.DeployShot.Active";
        static string Out => "Logs/UiShot/deploy/" + (Environment.GetEnvironmentVariable("SHOT_OUT") ?? "shots");
        static readonly string[] Keep = { "ms.test.mode", "ms.test.power", "ms.test.floor", "ms.sector", "ms.dev.weekly", "ms.grade.cryo-station" };
        static readonly Dictionary<string, int?> backup = new();
        static int step; static bool prepared; static double next = -1, armed = -1; static List<Action> steps; static Func<bool> wait;
        static DeployShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
        static int Idx(string id) => Array.FindIndex(MapSelect.Sectors, s => s.def != null && s.def.id == id);
        static Transform Find(string n) { foreach (var t in MapSelect.I.GetComponentsInChildren<Transform>(true)) if (t.name == n && t.gameObject.activeInHierarchy) return t; throw new Exception("no " + n); }
        static void TapIt(string n) { Debug.Log($"DeployShot: tap {n}"); var t = Find(n); var hb = t.GetComponent<HoloButton>(); if (hb != null) hb.OnPointerClick(null); else t.GetComponent<Tap>().onTap(); }
        static ScrollRect Strip => MapSelect.I.GetComponentsInChildren<ScrollRect>(true)[0];
        static void Pick(string id) { MapSelect.I.PickSector(Idx(id)); Debug.Log($"DeployShot: picked {id}: locked {MapSelect.Sectors[Idx(id)].locked}, weekly {Ops.WeeklyOn}, rank {Meta.Rank}"); }
        static float p1;
        static List<Action> Build()
        {
            var fresh = new SaveData { runs = 3 };
            var vet = new SaveData { runs = 40, rank = 6, sectorsCleared = 2 };
            foreach (var id in new[] { "cryo-station", "evac-deck" }) vet.cleared.Add(id);
            vet.stars.Add("cryo-station:0"); vet.stars.Add("cryo-station:1");
            return new List<Action>
            {
                () =>
                {
                    foreach (var k in Keep) backup[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : null;
                    Ops.WeeklyDev = false; Prefs.TestMode = false; Prefs.TestFloor = 1; PlayerPrefs.DeleteKey("ms.grade.cryo-station"); MapSelect.Selected = 0;
                    Progress.Sim = true; Progress.UseTemp(fresh); MapSelect.I.Show(); Later(2.5);
                },
                () => { Shot("1_cryo_rank1_weekly_locked"); Ops.WeeklyDev = true; Pick("cryo-station"); Later(1.2); },
                () => { Shot("2_cryo_weekly_override"); TapIt("WeeklyInfo"); Later(0.5); },
                () => { Shot("3_weekly_info"); TapIt("WeeklyTip"); Pick("evac-deck"); Later(1.5); },
                () => { Debug.Log($"DeployShot: locked evac: BRIEFING shown {Find0("Deploy")}, locked box {Find0("Locked")}"); Shot("4_locked_evac"); Progress.UseTemp(vet); PlayerPrefs.SetInt("ms.grade.cryo-station", 2); Pick("cryo-station"); Later(1.5); },
                () => { Shot("5_cryo_attempted"); Prefs.TestMode = true; MapSelect.I.Hide(); MapSelect.I.Show(); Pick("skyhook"); Later(1.5); },
                () => { Shot("6_testmode_skyhook"); p1 = Strip.horizontalNormalizedPosition; TapIt("Deploy"); Later(1.2); },
                () => { Debug.Log($"DeployShot: BRIEFING from map -> brief open {Find0("BriefBack")}"); TapIt("BriefBack"); Later(1.2); },
                () => { Debug.Log($"DeployShot: back from briefing: selected {MapSelect.Sectors[MapSelect.Selected].name}, strip {p1:0.000} -> {Strip.horizontalNormalizedPosition:0.000}"); MapSelect.I.Hide(); MapSelect.I.Show(); Later(1.5); },
                () => { Debug.Log($"DeployShot: reopened: selected {MapSelect.Sectors[MapSelect.Selected].name}, strip {Strip.horizontalNormalizedPosition:0.000}"); Shot("7_reopened"); Pick("swarm-gate"); Later(1.5); },
                () => { Shot("8_gate_test"); TapIt("Back"); Later(1.0); },
                () => { Debug.Log($"DeployShot: BACK -> map open {MapSelect.I.Open}"); Prefs.TestMode = false; Progress.UseTemp(fresh); MapSelect.Selected = 0; G.menu.GetType().GetField("deployBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(G.menu).GetType().GetMethod("OnPointerClick").Invoke(G.menu.GetType().GetField("deployBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(G.menu), new object[] { null }); Later(2.0); },
                () => { Debug.Log($"DeployShot: menu DEPLOY -> map open {MapSelect.I.Open}"); Shot("9_flow_map"); TapIt("Pin0"); Later(1.0); },
                () => { TapIt("Deploy"); Later(1.2); },
                () => { Shot("10_flow_brief"); TapIt("Launch"); Later(0.8); },
                () => { double t0 = EditorApplication.timeSinceStartup; wait = () => (G.state == Game.State.Playing && G.run) || EditorApplication.timeSinceStartup - t0 > 25; },
                () => { G.godMode = true; Later(2); },
                () => { Debug.Log($"DeployShot: LAUNCH -> state {G.state} run {G.run} zone {G.sector?.id} floor {G.floor} TestRun {Game.TestRun}"); Shot("11_ingame"); G.ToMenu(); Later(1); },
            };
        }
        static bool Find0(string n) { foreach (var t in MapSelect.I.GetComponentsInChildren<Transform>(true)) if (t.name == n && t.gameObject.activeInHierarchy) return true; return false; }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 260) { Debug.LogError("DeployShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || MapSelect.I == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"DeployShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            foreach (var kv in backup) { if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value); else PlayerPrefs.DeleteKey(kv.Key); }
            PlayerPrefs.Save(); Prefs.HeroOverride = null; Meta.Difficulty = 0;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; SafeArea.Override = null;
            Debug.Log("DeployShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
