using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Progression screens into Logs/UiShot/progression (muted, a throwaway save): Armory, Gear (+ merge ready), the
    // briefing with power, a level-up card pick, the floor cache, an evolution, defeat with the upgrade suggestion,
    // victory with loot.   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.ProgShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class ProgShot
    {
        const string Key = "Maze.ProgShot.Active", Out = "Logs/UiShot/progression";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;
        static ProgShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
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
        static readonly Action[] Steps =
        {
            () => {
                Progress.Sim = true;
                var d = new SaveData { credits = 1140, parts = 9, alloy = 85, sectorsCleared = 1 };
                Progress.UseTemp(d); d.weaponLv[0] = 6;
                var a = Progress.AddGear(0, 1); a.level = 3; Progress.Equip(a);
                var b = Progress.AddGear(2, 2); b.level = 2; Progress.Equip(b);
                Progress.AddGear(4, 0); Progress.AddGear(4, 0); Progress.AddGear(4, 0); Progress.AddGear(1, 0); Progress.AddGear(3, 3);
                G.menu.OpenSheet("loadout"); Later(1.5);
            },
            () => { Shot("01_armory_weapons"); G.menu.OpenSheet("gear"); Later(1.0); },
            () => { var mi = typeof(MainMenu).GetField("gearSelUid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var g in Progress.D.gear) if (g.slot == 4) { mi.SetValue(G.menu, g.uid); break; }
                    typeof(MainMenu).GetMethod("RefreshGear", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(G.menu, null); Later(0.8); },
            () => { Shot("02_gear_merge_ready"); G.menu.CloseSheet(); MapSelect.I.Show(); Later(2.5); },
            () => { MapSelect.I.PickSector(0); Later(1.2); },
            () => { Shot("03_sector_select_rec_power"); MapSelect.I.OpenBriefing(); Later(1.5); },
            () => { Shot("04_briefing_power"); MapSelect.I.LaunchDrop(); wait = () => G.state == Game.State.Playing && !G.menu.Busy; },
            () => { G.godMode = true; G.holdFire = false; Later(3.0); },
            () => { Shot("05_floor1_hud_xp"); G.DebugXp(40); Later(0.8); },
            () => { Shot("06_level_up_cards"); G.Pick(0); Later(0.5); },
            () => { G.DebugCache(); Later(0.8); },
            () => { Shot("07_floor_cache"); G.Pick(1); Later(0.8); },
            () => { Shot("08_rare_card"); G.Pick(0); Later(0.4); },
            () => { G.DebugMods("pierce", "pierce", "pierce", "range", "range", "range"); G.DebugLevelUp(); Later(0.8); },
            () => { Shot("09_evolution_offer"); G.Pick(0); Later(0.35); },
            () => { Shot("10_evolution_flash"); Later(2.0); },
            () => { G.godMode = false; G.player.dashT = 0; G.HurtPlayer(99999); Later(1.2); },
            () => { Shot("11_defeat_upgrade_suggestion"); G.Restart(); Later(1.5); },
            () => { G.godMode = true; G.DebugWin(); Later(1.2); },
            () => { Shot("12_victory_loot"); },
        };
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 100) { Debug.LogError("ProgShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"ProgShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; SafeArea.Override = null;
            Debug.Log("ProgShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
