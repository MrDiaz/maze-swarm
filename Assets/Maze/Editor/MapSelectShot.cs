using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Quick capture of the map-select flow at iPhone resolution into Logs/UiShot/mapselect_*:
    // the planet, a locked sector, the briefing, the drop countdown, and back in the level.
    // Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.MapSelectShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class MapSelectShot
    {
        const string Key = "Maze.MapSelectShot.Active";
        static readonly string[] Out = { "Logs/UiShot" };
        static int step; static bool prepared; static double next = -1, armed = -1;
        static Func<bool> wait;

        static MapSelectShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Shot(string n) => UiShot.Shot(n, Out);
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }

        static readonly Action[] Steps =
        {
            () => { G.menu.OpenSheet("settings"); Later(2); },
            () => { Shot("settings_framerate"); G.menu.CloseSheet(); Later(1); },
            () => { G.menu.OpenSheet("squad"); Later(2); },
            () => { Shot("squad_portrait"); G.menu.CloseSheet(); Later(1); },
            () => { MapSelect.I.Show(); Later(3); },
            () => { Shot("mapselect_planet"); SavePlanet(); MapSelect.I.PickSector(4); Later(2.5); },
            () => { Shot("mapselect_locked"); MapSelect.I.PickSector(0); Later(2); },
            () => { MapSelect.I.OpenBriefing(); Later(1.5); },
            () => { Shot("mapselect_briefing"); Shot("briefing_portrait"); MapSelect.I.LaunchDrop(); Later(0.6); },
            () => { Shot("mapselect_drop"); wait = () => G.state == Game.State.Playing && !G.menu.Busy; },
            () => { Later(0.5); },
            () => { Shot("mapselect_ingame"); },
        };

        static void SavePlanet()
        {
            var rt = MapSelect.I.PlanetRT; if (rt == null) { Debug.LogError("MapSelectShot: no planet RT"); return; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
            File.WriteAllBytes("Logs/UiShot/mapselect_planet_rt.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 400) { Debug.LogError("MapSelectShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || MapSelect.I == null) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; Later(3); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"MapSelectShot: step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            SafeArea.Override = null;
            Debug.Log("MapSelectShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
