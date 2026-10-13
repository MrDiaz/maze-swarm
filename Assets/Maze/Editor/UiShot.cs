using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Maze.EditorTools
{
    // Plays the game in the editor and captures the UI at iPhone 15 Pro resolution (2556x1179, with its
    // Dynamic Island / home-bar safe area faked) into Logs/UiShot and Concepts/StartScreen2/built:
    // main menu, loadout, squad, research, settings, drop hatch, low ammo, reloading, gameplay HUD, a mid-flash
    // frame per weapon, an AK slow-motion strip, pause and game over.
    // Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.UiShot.Run   (no -quit, not -nographics)
    [InitializeOnLoad]
    public static class UiShot
    {
        const string Key = "Maze.UiShot.Active";
        // UISHOT_SIZE=1920x1080 captures a 16:9 phone (no notch insets); default the iPhone Pro landscape
        static readonly int W = Size(0, 2556), H = Size(1, 1179);
        static int Size(int i, int def) { var e = System.Environment.GetEnvironmentVariable("UISHOT_SIZE"); var p = e?.Split('x'); return p != null && p.Length == 2 && int.TryParse(p[i], out var v) ? v : def; }
        static readonly string[] OutDirs = { "Logs/UiShot", "Concepts/StartScreen2/built" };
        static double start = -1, armed = -1;
        static int step;
        static RenderTexture rt;

        static UiShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        [MenuItem("Maze/Capture UI Screenshots", priority = 40)]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick; // covers projects with domain reload disabled
            EditorApplication.EnterPlaymode();
        }

        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();

        static readonly (double at, Action act)[] Steps =
        {
            (2.8, () => Shot("01_main_menu")),
            (3.0, () => G.menu.OpenSheet("loadout")),
            (4.4, () => Shot("02_loadout")),
            (4.5, () => G.menu.OpenSheet("squad")),
            (5.7, () => Shot("03_squad")),
            (5.8, () => G.menu.OpenSheet("research")),
            (7.0, () => Shot("04_research")),
            (7.1, () => G.menu.OpenSheet("settings")),
            (8.2, () => Shot("05_settings")),
            (8.3, () => G.menu.CloseSheet()),
            (9.2, () => G.menu.Deploy()),
            (10.05, () => Shot("06_drop_hatch")),
            (11.2, () => G.demo = true),              // autopilot plays a few waves
            (15.6, () => { var g = G; g.demo = false; g.player.weapon = 0; }), // idle auto-fire with the rifle
            (15.7, () => { var p = G.player; p.ammo[0] = Mathf.Min(p.ammo[0], 6); }),
            (15.75, () => Shot("07a_low_ammo")),
            (16.0, () => { var p = G.player; if (!p.Reloading) p.ammo[0] = 1; }),  // runs dry -> automatic reload
            (16.9, () => Shot("07b_reloading")),
            (17.5, () => Shot("07_gameplay_hud")),
            // gunfire, frozen mid-flash: gameplay held (state only, no pause panel), time slowed 20x so the
            // 50 ms flash lasts a second; a first shot at full speed gets the hero into the shoot pose
            (17.6, () => Hold(true)),
            (17.7, () => Aim(0)), (17.8, () => G.DebugFire()),
            (18.6, () => Slow(0.05f)), (18.65, () => G.DebugFire()),
            (18.95, () => Shot("10_flash_ak")),
            (19.9, () => Slow(1)), (20.0, () => Aim(2)), (20.1, () => G.DebugFire()),
            (20.9, () => Slow(0.05f)), (20.95, () => G.DebugFire()),
            (21.25, () => Shot("11_flash_smg")),
            (22.2, () => Slow(1)), (22.3, () => Aim(1)), (22.4, () => G.DebugFire()),
            (23.2, () => Slow(0.05f)), (23.25, () => G.DebugFire()),
            (23.55, () => Shot("12_flash_shotgun")),
            // AK in slow motion: 4 frames ~12 ms apart (at 1/20 speed)
            (24.5, () => Slow(1)), (24.6, () => Aim(0)), (24.7, () => G.DebugFire()),
            (25.5, () => Slow(0.05f)), (25.55, () => G.DebugFire()),
            (25.62, () => Shot("13_ak_slowmo_1")), (25.86, () => Shot("13_ak_slowmo_2")),
            (26.10, () => Shot("13_ak_slowmo_3")), (26.34, () => Shot("13_ak_slowmo_4")),
            // shotgun: mid-blast, slow motion, the pump's back stroke (hull in the air), then live against Drones
            (26.6, () => Slow(1)), (26.7, () => Aim(1)), (26.8, () => G.DebugFire()),
            (27.7, () => Slow(0.05f)), (27.75, () => G.DebugFire()),
            (27.8, () => Shot("shotgun_slowmo_1")), (27.95, () => Shot("shotgun_blast")),
            (28.1, () => Shot("shotgun_slowmo_2")), (28.4, () => Shot("shotgun_slowmo_3")), (28.9, () => Shot("shotgun_slowmo_4")),
            (29.6, () => Slow(1)), (30.0, () => G.DebugFire()), (30.45, () => Shot("shotgun_pump")),
            // a staged pack of Drones 3-5 m ahead, then idle auto-fire: pellets, combined goo spray, slide smears
            (30.9, () => { Aim(1); Stage(); }), (31.0, () => { Hold(false); Aim(1); }),
            (31.12, () => Shot("shotgun_live_1")), (31.25, () => Shot("shotgun_live_2")), (31.4, () => Shot("shotgun_live_3")),
            (31.7, () => Shot("shotgun_live_4")), (32.1, () => Shot("shotgun_live_5")), (32.6, () => Shot("shotgun_live_6")),
            // the Bloater: a walk strip (pose sampled at 4 points of the cycle, gameplay held), the sac bursting
            // out minis, the death burst, then a wave-8 fight with the demo bot
            (33.0, () => { var g = G; g.holdFire = g.godMode = true; Hold(false); bloat = g.DebugSpawn(g.swarm.BloaterType, g.player.p + new Vector2(5.5f, 1.5f)); bloat.special = 99; }),
            (34.2, () => Hold(true)),
            (34.3, () => Pose(0f)), (34.8, () => Shot("bloater_walk_1")),
            (34.9, () => Pose(0.25f)), (35.4, () => Shot("bloater_walk_2")),
            (35.5, () => Pose(0.5f)), (36.0, () => Shot("bloater_walk_3")),
            (36.1, () => Pose(0.75f)), (36.6, () => Shot("bloater_walk_4")),
            // (batch frames are slow and game time is capped per frame, so these wait on the game, not the clock)
            (36.7, () => { Hold(false); bloat.special = 0.01f; Until(() => bloat.cast > 0 && bloat.cast < 0.3f); }), // stop, swell
            (36.8, () => Shot("bloater_swell")),
            (36.9, () => Until(() => bloat.cast <= 0 && bloat.minis > 0)),             // the sac bursts
            (37.0, () => WaitGame(0.12f)),
            (37.1, () => Shot("bloater_spawn_burst")),                                  // minis mid-leap
            (37.2, () => WaitGame(0.6f)),
            (37.3, () => Shot("bloater_minis")),
            (37.4, () => { G.KillEnemy(bloat); WaitGame(0.12f); }),
            (37.5, () => Shot("bloater_death_swell")),
            (37.6, () => WaitGame(0.2f)),
            (37.7, () => Shot("bloater_death_burst")),
            (37.8, () => WaitGame(0.7f)),
            (37.9, () => Shot("bloater_death_after")),
            (38.0, () => { var g = G; g.holdFire = false; g.demo = true; g.DebugWave(8); WaitGame(16f); }),
            (38.1, () => Shot("bloater_late_wave_1")),
            (38.2, () => WaitGame(5f)),
            (38.3, () => Shot("bloater_late_wave_2")),
            (38.4, () => { var g = G; g.demo = false; g.godMode = false; }),
            (38.5, () => G.TogglePause()),
            (39.2, () => Shot("08_pause")),
            (39.3, () => G.TogglePause()),
            (39.4, () => { var g = G; g.player.dashT = 0; g.HurtPlayer(9999); }),
            (41.5, () => Shot("09_game_over")),
            // map select: game over -> SECTOR -> the planet, a locked sector, the briefing, the drop, back in the level
            (41.7, () => G.ToSector()),
            (44.5, () => Shot("mapselect_planet")),
            (44.6, () => MapSelect.I.PickSector(4)),
            (47.0, () => Shot("mapselect_locked")),
            (47.1, () => MapSelect.I.PickSector(0)),
            (49.0, () => MapSelect.I.OpenBriefing()),
            (50.5, () => Shot("mapselect_briefing")),
            (50.6, () => MapSelect.I.LaunchDrop()),
            (51.2, () => Shot("mapselect_drop")),
            (51.3, () => Until(() => G.state == Game.State.Playing && !G.menu.Busy)),
            (51.4, () => Shot("mapselect_ingame")),
            (51.6, Finish),
        };

        static void Hold(bool on) { var g = G; g.demo = false; g.state = on ? Game.State.Paused : Game.State.Playing; }
        static void Slow(float k) => Time.timeScale = k;
        static Enemy bloat;
        // hold the step clock until a condition holds (checked every editor update)
        static Func<bool> waitUntil;
        static double lastNow;
        static void Until(Func<bool> f) => waitUntil = f;
        static void WaitGame(float sec) { float t0 = G.GameTime; waitUntil = () => G.GameTime >= t0 + sec; }
        // hold the Bloater's Walk clip at a point of its cycle
        static void Pose(float t) { var w = bloat.view.walk; if (w == null) return; bloat.v = Vector2.zero; bloat.k = Vector2.zero; w.speed = 0; w.normalizedTime = t; }
        // up to 6 live Drones moved into a loose pack ahead of the hero (plane +x)
        static void Stage()
        {
            var g = G; int n = 0;
            foreach (var e in g.swarm.enemies)
            {
                if (e.dead || n >= 6) continue;
                e.p = g.player.p + new Vector2(3f + (n % 3) * 0.8f, (n / 3 - 0.5f) * 1.4f + (n % 3 - 1) * 0.3f);
                e.spawnT = 1; e.k = Vector2.zero; n++;
            }
        }
        // weapon in hand, aimed screen-right (plane +x)
        static void Aim(int weapon) { var p = G.player; p.weapon = weapon; p.aim = new Vector2(1, 0); p.facing = Mathf.PI / 2; }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 900) { Debug.LogError("UiShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var game = G;
            if (game == null || game.menu == null || game.hud == null) return;
            if (start < 0) { Prepare(); start = now; }
            if (waitUntil != null)
            {
                if (!waitUntil()) { start += now - lastNow; lastNow = now; return; }
                waitUntil = null;
            }
            lastNow = now;
            double t = now - start;
            while (step < Steps.Length && t >= Steps[step].at)
            {
                try { Steps[step].act(); }
                catch (Exception e) { Debug.LogError($"UiShot: step {step} failed: {e}"); }
                step++;
                if (waitUntil != null) break; // the next step waits for the game
            }
        }

        // render the main camera into an iPhone-sized texture, with the UI canvases drawn by an overlay
        // camera stacked on it (Screen Space Overlay canvases never reach a render texture)
        internal static void Prepare()
        {
            AudioListener.volume = 0; AudioListener.pause = true; // editor captures run muted
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "UiShot" };
            SafeArea.Override = W / (float)H > 2f ? new Rect(177f / W, 63f / H, 1 - 354f / W, 1 - 63f / H) : new Rect(0, 0, 1, 1); // 59 pt sides, 21 pt home bar @3x (none on 16:9)
            var cam = Camera.main;
            cam.targetTexture = rt;
            var ui = new GameObject("UiShotCam").AddComponent<Camera>();
            ui.cullingMask = 1 << 5; ui.orthographic = true; ui.nearClipPlane = 0.1f; ui.farClipPlane = 200;
            ui.transform.position = new Vector3(0, -2000, 0);
            ui.targetTexture = rt; // gives the canvases the right pixel size (batch mode has no screen)
            ui.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
            cam.GetUniversalAdditionalCameraData().cameraStack.Add(ui);
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>())
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                int order = c.sortingOrder;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = ui; c.planeDistance = 50; c.sortingOrder = order;
                var sc = c.GetComponent<CanvasScaler>();
                if (sc != null) { sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; sc.scaleFactor = H / 1080f; }
            }
            Debug.Log($"UiShot: capturing at {W}x{H}, ui cam rect {ui.pixelRect}, screen {Screen.width}x{Screen.height}");
        }

        internal static void Shot(string name) => Shot(name, OutDirs);
        internal static void Shot(string name, string[] outDirs)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            var png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            foreach (var d in outDirs)
            {
                Directory.CreateDirectory(d);
                File.WriteAllBytes(Path.Combine(d, name + ".png"), png);
            }
            var mc = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
            Debug.Log($"UiShot: wrote {name}.png");
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            SafeArea.Override = null;
            Debug.Log("UiShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }
    }
}
