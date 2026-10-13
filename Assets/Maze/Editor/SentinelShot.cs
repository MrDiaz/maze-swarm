using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Captures the hero (Arctic Sentinel) into Logs/UiShot/sentinel_*: in the Sandbox, each of his 4 clips up close
    // (skin check after the 8->4 weight fold); in the game, idle / running / run-and-shoot with the demo bot, and an
    // AK grip close-up in the shoot pose.
    // Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.SentinelShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class SentinelShot
    {
        const string Key = "Maze.SentinelShot.Phase"; // 1 = sandbox, 2 = game
        static readonly string[] Out = { "Logs/UiShot" };
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;

        static SentinelShot() { if (SessionState.GetInt(Key, 0) > 0) EditorApplication.update += Tick; }

        public static void Run()
        {
            SessionState.SetInt(Key, 1);
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Sandbox.unity");
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Shot(string n) => UiShot.Shot(n, Out);
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static Sandbox S => Sandbox.I;
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static Sandbox.Spawned hero;

        static void Clip(int i, string name)
        {
            S.PlayClip(hero, i); hero.paused = false;
            Later(0.9);
        }

        static readonly Action[] SandboxSteps =
        {
            () => { int k = S.entries.FindIndex(e => e.kind == Sandbox.Kind.Hero); var t = S.SpawnEntry(S.entries[k]); wait = () => t.IsCompleted; },
            () => { hero = S.sel; S.SetCamera(4.2f, 12, 200); S.ShowTab(1); Debug.Log($"SentinelShot: {hero.tris} tris, {hero.bones} bones, clips {string.Join(",", hero.clips.ConvertAll(c => c.name))}"); Later(1); },
            () => { Clip(0, "a"); },
            () => { Shot("sentinel_clip_" + hero.clips[0].name); Clip(1, "b"); },
            () => { Shot("sentinel_clip_" + hero.clips[1].name); Clip(2, "c"); },
            () => { Shot("sentinel_clip_" + hero.clips[2].name); Clip(3, "d"); },
            () => { Shot("sentinel_clip_" + hero.clips[3].name); S.SetCamera(2.2f, 25, 230); Clip(0, "a"); },
            () => { Shot("sentinel_hands_shoulders"); },
        };

        static readonly Action[] GameSteps =
        {
            () => { var g = G; g.menu.Hide(); g.Restart(); g.godMode = true; g.cfg.camera.distance = 13; Later(1.5); },
            () => { Shot("sentinel_idle"); var g = G; g.demo = true; wait = () => G.player.shootT <= 0 && G.player.vel.magnitude > 3.5f && G.swarm.enemies.Count > 0; },
            () => { Shot("sentinel_run"); wait = () => G.player.shootT > 0.2f && G.player.vel.magnitude > 2f; },
            () => { Shot("sentinel_run_and_shoot"); var g = G; g.demo = false; g.holdFire = true; g.state = Game.State.Paused; var p = g.player; p.weapon = 0; p.aim = new Vector2(1, 0); p.facing = Mathf.PI / 2; Later(0.5); },
            () => { G.DebugFire(); Later(1.2); },
            () => { GripCloseUp("sentinel_ak_grip", 1.0f); GripCloseUp("sentinel_ak_grip_front", -1.0f); },
        };

        // a side view of the hands and the AK in the shoot pose, from its own camera (no HUD)
        static void GripCloseUp(string name, float side)
        {
            var p = G.player; var gunT = p.gun; if (gunT == null) { Debug.LogError("SentinelShot: no gun"); return; }
            var vis = p.VisualRoot;
            var focus = (p.Hand.position + (p.HandL != null ? p.HandL.position : p.MuzzleWorld)) * 0.5f;
            var cam = new GameObject("GripCam").AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = 32; cam.nearClipPlane = 0.05f;
            cam.transform.position = focus + vis.right * (1.5f * side) + Vector3.up * 0.35f + vis.forward * (side < 0 ? 1.2f : 0.2f);
            cam.transform.LookAt(focus);
            var rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(1280, 960, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes($"Logs/UiShot/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(cam.gameObject);
            Debug.Log($"SentinelShot: hand {p.Hand.position} left hand {(p.HandL != null ? p.HandL.position : Vector3.zero)} muzzle {p.MuzzleWorld}");
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 600) { Debug.LogError("SentinelShot: timed out"); Finish(); return; }
            int phase = SessionState.GetInt(Key, 0);
            if (!EditorApplication.isPlaying) return;
            if (phase == 1 && (S == null || S.entries.Count == 0)) return;
            if (phase == 2 && (G == null || G.menu == null)) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; Later(1.5); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            var steps = phase == 1 ? SandboxSteps : GameSteps;
            if (step >= steps.Length)
            {
                if (phase == 1)
                {   // on to the game scene
                    SessionState.SetInt(Key, 2); step = 0; prepared = false; armed = now;
                    EditorApplication.ExitPlaymode();
                    EditorApplication.delayCall += () => { EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode(); };
                    return;
                }
                Finish(); return;
            }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"SentinelShot: phase {phase} step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetInt(Key, 0);
            EditorApplication.update -= Tick;
            SafeArea.Override = null;
            Debug.Log("SentinelShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
