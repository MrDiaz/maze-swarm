using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Every screen that shows the selected hero, with one Squad pick (-heroPick vanguard | sentinel), into
    // Logs/UiShot/<pick>_screens/: bridge, Squad, Loadout, sector select, briefing, drop, HUD, pause, SWARMED, FLOOR
    // ESCAPED. The saved pick is restored after. Muted, ~50 s.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.HeroScreens.Run -heroPick vanguard   (no -quit)
    [InitializeOnLoad]
    public static class HeroScreens
    {
        const string Key = "Maze.HeroScreens.Active";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;
        static string Pick => SessionState.GetString(Key + ".pick", "vanguard");
        static string Out => $"Logs/UiShot/{Pick}_screens";

        static HeroScreens() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-heroPick");
            SessionState.SetString(Key + ".pick", at >= 0 && at + 1 < args.Length ? args[at + 1] : "vanguard");
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitGame(float s) { float t0 = G.GameTime; wait = () => G.GameTime >= t0 + s; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }

        static readonly Action[] Steps =
        {
            () => { SessionState.SetString(Key + ".prev", Prefs.Hero); G.menu.PickHero(Pick); Later(1.5); },
            () => { Shot("01_bridge"); G.menu.OpenSheet("squad"); Later(1.5); },
            () => { Shot("02_squad"); G.menu.CloseSheet(); Later(0.8); },
            () => { G.menu.OpenSheet("loadout"); Later(1.5); },
            () => { Shot("03_loadout"); G.menu.CloseSheet(); Later(0.8); },
            () => { MapSelect.I.Show(); Later(2.5); },
            () => { MapSelect.I.PickSector(0); Later(1.5); },
            () => { Shot("04_sector_select"); MapSelect.I.OpenBriefing(); Later(1.5); },
            () => { Shot("05_briefing"); MapSelect.I.LaunchDrop(); Later(0.8); },
            () => { Shot("06_drop"); wait = () => G.state == Game.State.Playing && !G.menu.Busy; },
            () => { G.godMode = true; WaitGame(1.5f); },
            () => { Shot("07_hud"); G.TogglePause(); Later(1.0); },
            () => { Shot("08_pause"); G.TogglePause(); G.godMode = false; G.player.dashT = 0; G.HurtPlayer(9999); wait = () => G.state == Game.State.Dead; },
            () => { Later(2.0); },
            () => { Shot("09_swarmed"); G.LoadMap(1); G.menu.Hide(); G.Restart(); G.godMode = true; WaitGame(0.5f); },
            () => { Shot("10_escape_start"); G.escape.DebugToLift(); WaitGame(0.6f); },
            () => { G.player.p = G.escape.Pad; wait = () => G.state == Game.State.Won; },
            () => { Later(2.0); },
            () => { Shot("11_floor_escaped"); },
        };

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 85) { Debug.LogError("HeroScreens: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"HeroScreens: step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            string prev = SessionState.GetString(Key + ".prev", ""); if (prev != "") Prefs.Hero = prev;
            SafeArea.Override = null;
            Debug.Log("HeroScreens: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
