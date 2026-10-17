using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // 2026-10-14 tweaks, checked in play (muted, HUD on): cone targeting (the hero faces +x; a tanky enemy ahead and one
    // behind: only the one ahead is shot; then only the one behind: no fire at all), and HOLD THE ZONE under continuous
    // spawns (Cryo floor 3, the demo bot holding the ring: alive / spawned over the upload, editor fps).
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.TweaksShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class TweaksShot
    {
        const string Key = "Maze.TweaksShot.Active", Out = "Design/Captures/tweaks-2026-10-14";
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static TweaksShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => Object.FindAnyObjectByType<Game>();
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static int Shots => (int)typeof(Game).GetField("shotCount", F).GetValue(G);
        static bool holdSpawns = true;
        static void HoldSpawns() { if (!holdSpawns) return; typeof(Game).GetField("toSpawn", F).SetValue(G, 0); typeof(Game).GetField("breakT", F).SetValue(G, 99f); }
        static Enemy front, behind; static float hpF, hpB; static int s0;
        static Enemy Put(Vector2 off) { var g = G; var at = g.player.p + off; var e = g.DebugSpawn(0, at); e.hp = e.maxHp = 1e6f; e.p = at; return e; }
        static List<Action> Build()
        {
            return new List<Action>
            {
                () => { UiShot.Prepare(); Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; CryoSoftEnvironment.Enabled = true;
                        var def = SectorDefs.Get("cryo-station"); var g = G; Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
                        g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = false; g.holdFire = false; WaitReal(4.5); },
                // facing +x; one tanky enemy 6 m ahead, one 5 m behind (both in reach, clear lines)
                () => { var g = G; g.swarm.Clear(); typeof(Game).GetField("faceDir", F).SetValue(g, Vector2.right); front = Put(new Vector2(6f, 0.4f)); behind = Put(new Vector2(-5f, -0.3f)); WaitReal(0.6); },
                () => { hpF = front.hp; hpB = behind.hp; s0 = Shots; WaitReal(2.0); },
                () => { Shot("cone_1_front_and_behind"); Debug.Log($"TweaksShot: CONE front+behind, 2 s: shots {Shots - s0}, damage to front {hpF - front.hp:0}, to behind {hpB - behind.hp:0}, hero facing {G.player.facing * Mathf.Rad2Deg:0} deg"); Later(0.1); },
                // the one ahead gone: only the one behind is left -> the hero holds fire
                () => { var g = G; front.hp = 0.01f; front.p = g.player.p + new Vector2(30f, 30f); g.swarm.Clear(); behind = Put(new Vector2(-5f, -0.3f)); WaitReal(0.8); },
                () => { hpB = behind.hp; s0 = Shots; WaitReal(2.0); },
                () => { Shot("cone_2_behind_only_no_fire"); Debug.Log($"TweaksShot: CONE behind only, 2 s: shots {Shots - s0}, damage to behind {hpB - behind.hp:0} (expect 0 / 0)"); Later(0.1); },
                // turn round (as if the stick pointed left): it fires again
                () => { typeof(Game).GetField("faceDir", F).SetValue(G, Vector2.left); s0 = Shots; hpB = behind.hp; WaitReal(1.5); },
                () => { Shot("cone_3_turned_round_fires"); Debug.Log($"TweaksShot: CONE turned to face it, 1.5 s: shots {Shots - s0}, damage {hpB - behind.hp:0}"); G.swarm.Clear(); Later(0.1); },
                // HOLD THE ZONE: Cryo floor 3 (PURGE > HUNT > HOLD), the demo bot holds the ring
                () => { holdSpawns = false; var g = G; g.DebugFloor(3); g.godMode = true; g.demo = true; Debug.Log($"TweaksShot: floor 3 goal {g.goal}"); WaitReal(1.0); },
                () => { int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; int k = 0;
                        wait = () => { double t = EditorApplication.timeSinceStartup - t0; var g = G;
                            if (t >= 5 * (k + 1)) { k++; int ft = (int)typeof(Game).GetField("floorTotal", F).GetValue(g); Debug.Log($"TweaksShot: HOLD t {t:0}s: floor {g.floor} goal {g.goal} alive {g.swarm.enemies.Count} spawned {ft}, editor fps {(Time.frameCount - f0) / t:0}"); if (k == 3 || k == 5) Shot($"hold_t{k * 5}s"); }
                            return t >= 30 || g.floor != 3; }; },
                () => { Debug.Log($"TweaksShot: HOLD done: floor {G.floor}, goal {G.goal}, alive {G.swarm.enemies.Count}"); G.ToMenu(); Later(0.5); },
            };
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 240) { Debug.LogError("TweaksShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; steps = Build(); Later(2); return; }
            if (g.run) HoldSpawns();
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"TweaksShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            CryoSoftEnvironment.Enabled = false;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("TweaksShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
