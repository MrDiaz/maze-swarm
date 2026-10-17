#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // Same-camera, HUD-off captures of non-Cryo zones plus the title screen, for before / after comparisons of
    // project-wide render settings (the HDR switch). Frozen, no enemies, seeded. Muted. Also an editor fps sample.
    //   ZONE_LOOK_OUT=dir UISHOT_SIZE=1600x738 Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.ZoneLookShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class ZoneLookShot
    {
        const string Key = "Maze.ZoneLookShot.Active";
        static readonly BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static string Out => Environment.GetEnvironmentVariable("ZONE_LOOK_OUT") ?? "Logs/ZoneLookShot";
        static int step; static bool prepared, wasSoft; static double next = -1, armed = -1; static List<Action> steps; static int f0; static double r0;
        static ZoneLookShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            SessionState.SetBool(Key, true); EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
        }
        static Game G => Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void Freeze() => typeof(CryoKitProof).GetMethod("Freeze", F).Invoke(null, new object[] { G });
        static void Canvases(bool on) { foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = on; }
        static void Shot(string n) { Directory.CreateDirectory(Out); UiShot.Shot(n, new[] { Out }); }
        static List<Action> Build()
        {
            var l = new List<Action>
            {
                () => { wasSoft = CryoSoftEnvironment.Enabled; CryoSoftEnvironment.Enabled = false; UnityEngine.Random.InitState(3); G.menu.Show(); Later(2.0); },
                () => { Shot("0_title"); f0 = Time.frameCount; r0 = EditorApplication.timeSinceStartup; Later(4.0); },
                () => { Debug.Log($"ZoneLookShot: title editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - r0):0.0}"); Later(0.1); },
            };
            int k = 1;
            foreach (var z in new[] { "acid-marsh", "evac-deck", "kessler-refinery", "cryo-station" })
            {
                var id = z; int n = k++;
                l.Add(() => { UnityEngine.Random.InitState(5); typeof(CryoKitProof).GetMethod("StartSector", F).Invoke(null, new object[] { id, false }); Freeze(); Later(2.5); });
                l.Add(() => { Freeze(); G.swarm.Clear(); Canvases(false); Later(0.5); });
                l.Add(() => { Shot($"{n}_{id}"); f0 = Time.frameCount; r0 = EditorApplication.timeSinceStartup; Later(4.0); });
                l.Add(() => { Debug.Log($"ZoneLookShot: {id} editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - r0):0.0}"); Canvases(true); Later(0.1); });
            }
            return l;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup; if (armed < 0) armed = now;
            if (now - armed > 200) { Debug.LogError("ZoneLookShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || Camera.main == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; UiShot.Prepare(); steps = Build(); Later(2); return; }
            if (now < next) return;
            if (g.state == Game.State.Choosing) { g.Pick(0); Freeze(); Later(0.3); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"ZoneLookShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            CryoSoftEnvironment.Enabled = wasSoft; SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false;
            Debug.Log("ZoneLookShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
#endif
