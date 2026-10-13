using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // The title sounds, checked by log (muted: the listener volume is 0, nothing is heard): the main menu (music + bridge
    // bed), a panel open / close, a locked tap, DEPLOY, a zone tap, BRIEFING, BACK, BRIEFING, LAUNCH (music out, pod fall),
    // the hatch (pod impact). Each Sfx clip and each bed start / fade is logged with the time, plus the bed volumes.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.TitleAudioCheck.Run   (no -quit)
    [InitializeOnLoad]
    public static class TitleAudioCheck
    {
        const string Key = "Maze.TitleAudioCheck.Active";
        static int step; static double next = -1, armed = -1, t0; static List<Action> steps; static bool prepared;
        static TitleAudioCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => UnityEngine.Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static string T => $"{EditorApplication.timeSinceStartup - t0:0.00}s";
        static void Note(string s) => Debug.Log($"TitleAudioCheck: {T} {s}");
        static void Beds()
        {
            float m = -1, a = -1;
            if (MenuAudio.I != null) foreach (var src in MenuAudio.I.GetComponents<AudioSource>()) { if (src.clip.name == "mus_menu") m = src.volume; else a = src.volume; }
            Note($"beds: mus_menu {m:0.00} amb_bridge {a:0.00}");
        }
        static void Click(object hb) { ((HoloButton)hb).OnPointerClick(null); }
        static object Field(object o, string n) => o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(o);
        static Transform Find(string n) { foreach (var t in MapSelect.I.GetComponentsInChildren<Transform>(true)) if (t.name == n && t.gameObject.activeInHierarchy) return t; throw new Exception("no " + n); }
        static void TapIt(string n) { var t = Find(n); var hb = t.GetComponent<HoloButton>(); if (hb != null) hb.OnPointerClick(null); else t.GetComponent<Tap>().onTap(); Note("tap " + n); }
        static List<Action> Build() => new List<Action>
        {
            () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; MapSelect.Selected = 0; G.menu.Show(); t0 = EditorApplication.timeSinceStartup; Note("main menu shown"); Later(1.5); },
            () => { Beds(); G.menu.OpenSheet("loadout"); Note("open loadout"); Later(0.6); },
            () => { G.menu.CloseSheet(); Note("close sheet"); Later(0.5); },
            () => { G.menu.OpenSheet("ops"); Note("open ops (not a listed panel: the old open sound)"); Later(0.5); },
            () => { G.menu.CloseSheet(); Later(0.4); },
            () => { Sfx.Deny(); Note("locked tap (Sfx.Deny)"); Later(0.4); },
            () => { Click(Field(G.menu, "deployBtn")); Note("DEPLOY"); Later(1.5); },
            () => { Beds(); TapIt("Pin0"); Later(0.4); },
            () => { TapIt("Deploy"); Note("BRIEFING"); Later(0.8); },
            () => { TapIt("BriefBack"); Later(0.6); },
            () => { TapIt("Deploy"); Later(0.8); },
            () => { Beds(); TapIt("Launch"); Note("LAUNCH"); Later(0.3); },
            () => { Beds(); Later(2.6); },
            () => { Beds(); Later(1.2); },
            () => { Note($"state {G.state} run {G.run}"); Beds(); G.ToMenu(); Later(2.0); },
            () => { Beds(); Later(0.3); },
        };
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 150) { Debug.LogError("TitleAudioCheck: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || MapSelect.I == null) return;
            AudioListener.volume = 0;   // muted throughout (menu.Show re-applies the sound setting)
            if (!prepared) { prepared = true; AudioListener.volume = 0; Sfx.Log = n => Note("SFX " + n); steps = Build(); Later(2); return; }
            if (now < next) return;
            if (g.state == Game.State.Choosing) { g.Pick(0); Later(0.5); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"TitleAudioCheck: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            Sfx.Log = null; SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false;
            Debug.Log("TitleAudioCheck: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
