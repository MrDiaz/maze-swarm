using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The mission briefing (Logs/UiShot/briefing/<SHOT_OUT>): Cryo / Vanguard / weekly on (the concept's case), then the
    // controls by their own handlers (a locked tier refused, HARD picked where it is open, BACK, LAUNCH into the run) and
    // other zone / hero / test-mode combinations. Throwaway saves; the editor prefs it touches are put back. Muted.
    //   SHOT_OUT=r1 UISHOT_SIZE=1672x941 Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.BriefShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class BriefShot
    {
        const string Key = "Maze.BriefShot.Active";
        static string Out => "Logs/UiShot/briefing/" + (Environment.GetEnvironmentVariable("SHOT_OUT") ?? "shots");
        static readonly string[] Keep = { "ms.test.mode", "ms.test.power", "ms.test.floor", "ms.sector", "ms.dev.weekly" };
        static readonly Dictionary<string, int?> backup = new();
        static int step; static bool prepared; static double next = -1, armed = -1; static List<Action> steps; static Func<bool> wait;
        static BriefShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
        static void TapIt(string n) { Debug.Log($"BriefShot: tap {n}"); Find(n).GetComponent<Tap>().onTap(); }
        static void Brief(string hero, string zone, bool test)
        {
            Prefs.HeroOverride = hero; Prefs.TestMode = test; Meta.Difficulty = 0;
            MapSelect.I.Hide(); MapSelect.I.Show(); MapSelect.I.RefreshHero(); MapSelect.I.PickSector(Idx(zone)); MapSelect.I.OpenBriefing();
            Debug.Log($"BriefShot: {hero}/{zone} test {test}: locked {MapSelect.Sectors[Idx(zone)].locked}, open {MapSelect.I.Open}, plan {string.Join(",", Game.PendingPlan ?? new Game.Goal[0])}, diff {Meta.Difficulty}, weekly {Ops.WeeklyOn}");
        }
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
                    Ops.WeeklyDev = true; Prefs.TestFloor = 1;
                    Progress.Sim = true; Progress.UseTemp(fresh); Brief("vanguard", "cryo-station", false); Later(2.0);
                },
                () => { Shot("1_cryo_vanguard"); TapIt("Tier1"); Later(0.6); },
                () => { Debug.Log($"BriefShot: tapped locked HARD -> difficulty {Meta.Difficulty} (want 0)"); TapIt("BriefBack"); Later(1.0); },
                () => { Debug.Log($"BriefShot: BACK -> map open {MapSelect.I.Open}"); Shot("2_back_to_map"); Progress.UseTemp(vet); Brief("sentinel", "cryo-station", false); Later(1.5); },
                () => { TapIt("Tier1"); Later(0.8); },
                () => { Debug.Log($"BriefShot: veteran tapped HARD -> difficulty {Meta.Difficulty} (want 1)"); Shot("3_cryo_sentinel_hard"); Meta.Difficulty = 0; Brief("scout", "hive-core", true); Later(1.5); },
                () => { Shot("4_hivecore_scout_test"); TapIt("TestFloor"); Later(0.5); },
                () => { Debug.Log($"BriefShot: test floor tap -> {Prefs.TestFloor} (want 2)"); Prefs.TestFloor = 1; Brief("vanguard", "swarm-gate", true); Later(1.5); },
                () => { Shot("5_gate_vanguard_test"); Brief("sentinel", "acid-marsh", true); Later(1.5); },
                () => { Shot("6_acidmarsh_sentinel_test"); Prefs.TestMode = false; Progress.UseTemp(fresh); Brief("vanguard", "cryo-station", false); Later(1.2); },
                () => { TapIt("Launch"); Later(0.8); },
                () => { Shot("7_drop"); double t0 = EditorApplication.timeSinceStartup; wait = () => (G.state == Game.State.Playing && G.run) || EditorApplication.timeSinceStartup - t0 > 25; },
                () => { G.godMode = true; Later(2); },
                () => { Debug.Log($"BriefShot: LAUNCH -> state {G.state} run {G.run} zone {G.sector?.id} floor {G.floor} goal '{G.GoalLine}' TestRun {Game.TestRun}"); Shot("8_ingame"); G.ToMenu(); Later(1); },
            };
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 220) { Debug.LogError("BriefShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || MapSelect.I == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; steps = Build(); Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"BriefShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            foreach (var kv in backup) { if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value); else PlayerPrefs.DeleteKey(kv.Key); }
            PlayerPrefs.Save(); Prefs.HeroOverride = null; Meta.Difficulty = 0;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; SafeArea.Override = null;
            Debug.Log("BriefShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
