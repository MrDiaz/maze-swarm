using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Camera variants from ONE frozen gameplay moment on Cryo Station (wave 5, the demo bot firing into the swarm):
    // A = the current camera, B = 28% closer, C = 28% closer and steeper. Logs/UiShot/cam_var/cam_<A|B|C>.png at
    // iPhone resolution, plus the hero's on-screen height and whether the 15 m reach circle fits (cam_var.txt).
    // Muted, ~30 s. Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.CamShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class CamShot
    {
        const string Key = "Maze.CamShot.Active", Out = "Logs/UiShot/cam_var";
        const float ScreenH = 1179f, ScreenW = 2556f, Reach = 15f;
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;
        static readonly StringBuilder log = new();
        static readonly (string tag, float dist, float pitch, float fov)[] Variants =
            { ("FAR", Prefs.CamFar, 57f, 30f), ("CLOSE", Prefs.CamClose, 57f, 30f), ("DIABLO", 20f, 55f, 40f), ("LOW", 12f, 45f, 30f) };

        static CamShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitGame(float s) { float t0 = G.GameTime; wait = () => G.GameTime >= t0 + s; }

        // place the camera exactly like Game.UpdateCamera does, centred on the hero (the game is frozen meanwhile)
        static void Place(int i)
        {
            var g = G; var v = Variants[i]; var c = g.cfg.camera; var cam = Camera.main;
            float pitch = v.pitch * Mathf.Deg2Rad, yaw = c.yaw * Mathf.Deg2Rad;
            var off = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * Mathf.Cos(pitch) * v.dist;
            cam.transform.position = MazeMap.ToWorld(g.player.p + off, Mathf.Sin(pitch) * v.dist);
            cam.transform.LookAt(MazeMap.ToWorld(g.player.p));
            cam.fieldOfView = v.fov;
        }

        static void Measure(int i)
        {
            var g = G; var cam = Camera.main; var v = Variants[i];
            var b = new Bounds(); bool first = true;
            foreach (var r in g.player.GetComponentsInChildren<SkinnedMeshRenderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            var foot = MazeMap.ToWorld(g.player.p, b.min.y); var head = MazeMap.ToWorld(g.player.p, b.max.y);
            float h = (cam.WorldToViewportPoint(head).y - cam.WorldToViewportPoint(foot).y) * ScreenH;
            int inside = 0; float minX = 1, maxX = 0, minY = 1, maxY = 0;
            for (int k = 0; k < 64; k++)
            {
                float a = k * Mathf.PI * 2 / 64;
                var vp = cam.WorldToViewportPoint(MazeMap.ToWorld(g.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Reach));
                if (vp.x >= 0 && vp.x <= 1 && vp.y >= 0 && vp.y <= 1) inside++;
                minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x); minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
            }
            log.AppendLine($"{v.tag}: distance {v.dist:F1}, pitch {v.pitch:F0}, fov {v.fov:F0}  hero {h:F0} px tall ({h / ScreenH * 100:F1}% of screen height)  " +
                           $"15 m circle: {inside}/64 points on screen, spans x {minX * ScreenW:F0}..{maxX * ScreenW:F0}, y {(1 - maxY) * ScreenH:F0}..{(1 - minY) * ScreenH:F0} px (screen {ScreenW}x{ScreenH})");
        }

        // escape-floor test: start room, hero + a few Drones, NEW vs OLD floor at FAR and CLOSE (Logs/UiShot/escape_floor)
        const string FOut = "Logs/UiShot/escape_floor";
        static void FShot(string n) { Directory.CreateDirectory(FOut); UiShot.Shot(n, new[] { FOut }); }
        const string COut = "Logs/UiShot/crates";
        static void CrateRoom(int r, string tag) { var g = G; g.enabled = true; Time.timeScale = 1; g.player.p = MazeMap.roomCenter[r] + new Vector2(0.5f, 0.5f); WaitGame(0.8f); }
        static void CrateSnap(string n)
        {
            G.enabled = false; Time.timeScale = 0; Directory.CreateDirectory(COut);
            Place(0); double t0 = EditorApplication.timeSinceStartup; bool a = false;
            wait = () =>
            {
                double t = EditorApplication.timeSinceStartup - t0;
                if (!a && t > 0.25) { UiShot.Shot(n + "_far", new[] { COut }); Place(1); a = true; t0 = EditorApplication.timeSinceStartup; return false; }
                if (a && t > 0.25) { UiShot.Shot(n + "_close", new[] { COut }); return true; }
                return false;
            };
        }
        static void SetFloor(bool on) { if (Game.FloorNew != on) G.ToggleFloor(); }
        // a room's centre with three Drones, frozen; then old / new clean / new + dirt at FAR and CLOSE
        static void Room(int r)
        {
            var g = G; g.enabled = true; Time.timeScale = 1;
            g.player.p = MazeMap.roomCenter[r] + new Vector2(0.5f, 0.5f);
            for (int k = 0; k < 3; k++) g.DebugSpawn(0, g.player.p + new Vector2(Mathf.Cos(k * 2.1f + r), Mathf.Sin(k * 2.1f + r)) * (3.5f + k));
            WaitGame(1.0f);
        }
        static void View(int cam, bool isNew, bool dirt) { var g = G; g.enabled = false; Time.timeScale = 0; SetFloor(isNew); g.SetFloorDirt(dirt); Place(cam); }
        static void Snap(string r) { View(0, false, false); FShotLater(r + "_far_old", () => View(0, true, false), r + "_far_new"); }
        static void Snap2(string r) { View(0, true, true); FShotLater(r + "_far_dirt", () => View(1, false, false), r + "_close_old"); }
        static void Snap3(string r) { View(1, true, false); FShotLater(r + "_close_new", () => View(1, true, true), r + "_close_dirt"); }
        // shot a, then change, then shot b (renders between)
        static void FShotLater(string a, Action change, string b)
        {
            bool first = true; double t0 = EditorApplication.timeSinceStartup;
            wait = () =>
            {
                double t = EditorApplication.timeSinceStartup - t0;
                if (first && t > 0.25) { FShot(a); change(); first = false; t0 = EditorApplication.timeSinceStartup; return false; }
                if (!first && t > 0.25) { FShot(b); return true; }
                return false;
            };
        }
        static readonly Action[] Steps =
        {
            () => { if (Array.IndexOf(Environment.GetCommandLineArgs(), "-loadoutOnly") < 0) { step = 6; return; } // -> the floor steps (index 7)
                    Prefs.HeroOverride = "vanguard"; G.menu.OpenSheet("loadout"); Later(2.0); },
            () => { UiShot.Shot("vanguard_loadout", new[] { Out }); G.menu.CloseSheet(); Later(1.0); },
            () => { G.menu.OpenSheet("squad"); Later(1.5); },
            () => { UiShot.Shot("vanguard_squad", new[] { Out }); G.menu.CloseSheet(); MapSelect.I.Show(); Later(2.5); },
            () => { MapSelect.I.PickSector(0); Later(1.5); },
            () => { MapSelect.I.OpenBriefing(); Later(1.5); },
            () => { UiShot.Shot("vanguard_briefing", new[] { Out }); Prefs.HeroOverride = null; Finish(); },
            // crates: rooms 0, 1, 2 with the old blocks, then the cargo crates (Logs/UiShot/crates)
            () => { LevelBuilder.UseCrateProps = false; var g = G; g.LoadMap(0); g.LoadMap(1); g.menu.Hide(); g.Restart(); g.godMode = true; g.holdFire = true; WaitGame(0.6f); },
            () => CrateRoom(0, "before"), () => CrateSnap("r0_before"),
            () => CrateRoom(1, "before"), () => CrateSnap("r1_before"),
            () => CrateRoom(2, "before"), () => CrateSnap("r2_before"),
            () => { LevelBuilder.UseCrateProps = true; var g = G; g.enabled = true; Time.timeScale = 1; g.LoadMap(0); g.LoadMap(1); g.menu.Hide(); g.Restart(); g.godMode = true; g.holdFire = true; WaitGame(0.6f); },
            () => CrateRoom(0, "after"), () => CrateSnap("r0_after"),
            () => CrateRoom(1, "after"), () => CrateSnap("r1_after"),
            () => CrateRoom(2, "after"), () => CrateSnap("r2_after"),
            () => { G.enabled = true; Time.timeScale = 1; Finish(); },
            // Iron Vanguard UI: RELOAD in place of the weapon swap, MINIGUN in Loadout and the briefing
            () => { UiShot.Shot("vanguard_hud_reload", new[] { Out }); G.ToMenu(); Later(1.5); },
            () => { G.menu.OpenSheet("loadout"); Later(1.5); },
            () => { UiShot.Shot("vanguard_loadout", new[] { Out }); G.menu.CloseSheet(); MapSelect.I.Show(); Later(2.5); },
            () => { MapSelect.I.PickSector(0); Later(1.5); },
            () => { MapSelect.I.OpenBriefing(); Later(1.5); },
            () => { UiShot.Shot("vanguard_briefing", new[] { Out }); Prefs.HeroOverride = null; Finish(); },
        };

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 75) { Debug.LogError("CamShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { Directory.CreateDirectory(Out); UiShot.Prepare(); prepared = true; Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"CamShot: step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Time.timeScale = 1;
            SafeArea.Override = null;
            Debug.Log("CamShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
