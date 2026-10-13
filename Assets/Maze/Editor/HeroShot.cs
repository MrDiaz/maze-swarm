using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Hero definition captures at iPhone resolution into Logs/UiShot/hero_def/<tag>_*.png: the hero standing and
    // shooting at the DEFAULT game camera (distance from MazeConfig), on Cryo Station. Muted, ~20 s.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.HeroShot.Run -heroTag before   (no -quit)
    //   -heroRim also turns on the readability pass (rim + contrast + outline) for that run.
    [InitializeOnLoad]
    public static class HeroShot
    {
        const string Key = "Maze.HeroShot.Active";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;
        static string Tag => SessionState.GetString(Key + ".tag", "shot");

        static HeroShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-heroTag");
            SessionState.SetString(Key + ".tag", at >= 0 && at + 1 < args.Length ? args[at + 1] : "shot");
            SessionState.SetBool(Key + ".rim", Array.IndexOf(args, "-heroRim") >= 0);
            int hp = Array.IndexOf(args, "-heroPick");   // -heroPick vanguard: shoot that Squad pick (the saved pick is restored after)
            SessionState.SetString(Key + ".pick", hp >= 0 && hp + 1 < args.Length ? args[hp + 1] : "");
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        // camera 3.2 m from the hero along this direction (hero space: x right, z forward), at chest height
        static void Look(Vector3 heroDir)
        {
            var g = G; g.enabled = false; var v = g.player.VisualRoot; var cam = Camera.main;
            var c = v.position + Vector3.up * 1.0f;
            cam.transform.position = c + v.TransformDirection(heroDir.normalized) * 3.2f + Vector3.up * 0.4f;
            cam.transform.LookAt(c); cam.fieldOfView = 30;
        }

        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void Shot(string n)
        {
            Directory.CreateDirectory("Logs/UiShot/hero_def");
            UiShot.Shot(n, new[] { "Logs/UiShot/hero_def" });
        }

        static readonly Action[] Steps =
        {
            () => { var g = G; string pick = SessionState.GetString(Key + ".pick", "");
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-vgBlender") >= 0) { SessionState.SetBool(Key + ".rigRestore", true); Prefs.VanguardMeshy = false; }
                    if (pick != "") { SessionState.SetString(Key + ".prev", Prefs.Hero); Prefs.Hero = pick; g.player.RebuildHero(g.cfg); }
                    g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.holdFire = true; g.cfg.camera.distance = Prefs.CamFar;
                    Player.ReadabilityPass = SessionState.GetBool(Key + ".rim", false); g.player.ApplyReadability();
                    g.player.aim = new Vector2(0.6f, -0.8f).normalized; Later(2.5); },
            () => { Shot(Tag + "_idle"); var g = G; g.holdFire = false; g.state = Game.State.Playing; Later(0.1); },
            () => { var g = G; g.player.aim = new Vector2(0.6f, -0.8f).normalized; g.player.facing = Mathf.Atan2(g.player.aim.x, g.player.aim.y); g.DebugFire(); Later(0.25); },
            () => { Shot(Tag + "_shoot"); var g = G; g.cfg.camera.distance = Prefs.CamClose; g.holdFire = true; Later(1.5); },
            () => { var g = G; g.player.aim = new Vector2(0.6f, -0.8f).normalized; g.player.facing = Mathf.Atan2(g.player.aim.x, g.player.aim.y); g.holdFire = false; g.DebugFire(); Later(0.25); },
            () => { Shot(Tag + "_shoot_close"); G.cfg.camera.distance = Prefs.CamFar; G.holdFire = false; G.DebugFire(); Later(0.1); },
            // close side / front / three-quarter views of the hero (game camera paused), for hands, grip and gun checks
            () => { Player.IkDebug = true; Look(new Vector3(1, 0, 0)); Later(0.2); },
            () => { Shot(Tag + "_side"); Look(new Vector3(0, 0, 1)); Later(0.2); },
            () => { Shot(Tag + "_front"); Look(new Vector3(0.8f, 0, 0.6f)); Later(0.2); },
            () => { Shot(Tag + "_3q"); G.enabled = true; Player.IkDebug = false; },
        };

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 70) { Debug.LogError("HeroShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; Later(2); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"HeroShot: step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            string prev = SessionState.GetString(Key + ".prev", ""); if (prev != "") { Prefs.Hero = prev; SessionState.SetString(Key + ".prev", ""); }
            if (SessionState.GetBool(Key + ".rigRestore", false)) { Prefs.VanguardMeshy = true; SessionState.SetBool(Key + ".rigRestore", false); }
            EditorApplication.update -= Tick;
            SafeArea.Override = null;
            Debug.Log("HeroShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
