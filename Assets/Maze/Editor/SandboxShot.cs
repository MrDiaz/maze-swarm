using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Plays the Sandbox scene and captures it at iPhone resolution into Logs/UiShot/sandbox_*: the panel, a
    // raw dropped GLB (no rig / no animations), the built Bloater walking, skeleton and wireframe, the sac
    // burst and death effects, the hero firing at it, and compare mode.
    // Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.SandboxShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class SandboxShot
    {
        const string Key = "Maze.SandboxShot.Active";
        static readonly string[] Out = { "Logs/UiShot" };
        static int step; static double armed = -1; static bool prepared;
        static Func<bool> wait;

        static SandboxShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Sandbox.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static Sandbox S => Sandbox.I;
        static void WaitClock(float sec) { float t0 = S.clock; wait = () => S.clock >= t0 + sec; }
        static void Shot(string n) => UiShot.Shot(n, Out);
        static int DropIndex => S.entries.FindIndex(e => e.kind == Sandbox.Kind.Drop && e.name.ToLower().Contains("bloater"));

        static readonly Action[] Steps =
        {
            () => { Shot("sandbox_01_panel"); },
            () => { S.SelectEntry(DropIndex); if (DropIndex < 0) Debug.LogError("SandboxShot: meshi-bloater.glb not in the drop list"); _ = S.SpawnEntry(S.entries[Math.Max(0, DropIndex)]); wait = () => !S.Busy && S.spawned.Count >= 1; },
            () => { S.ShowTab(1); WaitClock(0.3f); },
            () => { Shot("sandbox_02_raw_dropped_bloater"); Debug.Log($"SandboxShot: raw drop -> {S.sel?.tris} tris, {S.sel?.bones} bones, {S.sel?.clips.Count} clips, note '{S.sel?.note}'"); },
            () => { S.SelectEntry(1); _ = S.SpawnEntry(S.entries[1]); wait = () => S.spawned.Count >= 2; },
            () => { S.SetWalkMode(Sandbox.Walk.Circle); WaitClock(1.6f); },
            () => { Shot("sandbox_03_bloater_walk"); Debug.Log($"SandboxShot: built bloater -> {S.sel.tris} tris, {S.sel.bones} bones, clips {string.Join(",", S.sel.clips.Select(c => c.name))}, playing at {S.sel.clips[S.sel.clip].speed:F2}x"); },
            () => { S.showSkeleton = true; S.SetWalkMode(Sandbox.Walk.Off); WaitClock(0.4f); },
            () => { Shot("sandbox_04_skeleton"); S.showSkeleton = false; S.showWire = true; WaitClock(0.4f); },
            () => { Shot("sandbox_05_wireframe"); S.showWire = false; S.ShowTab(2); S.Trigger(Sandbox.Effect.SacBurst); WaitClock(0.82f); },
            () => { Shot("sandbox_06_sac_burst"); WaitClock(1.5f); },
            () => { S.Trigger(Sandbox.Effect.BloaterDeath); WaitClock(0.34f); },
            () => { Shot("sandbox_07_bloater_death"); WaitClock(2.2f); },
            () => { S.SpawnHero(); S.heroFire = true; WaitClock(1.3f); },
            () => { Shot("sandbox_08_hero_fire"); S.heroFire = false; S.ClearAll(); S.ShowTab(0); S.SelectEntry(DropIndex); _ = S.Compare(S.entries[Math.Max(0, DropIndex)]); wait = () => !S.Busy && S.spawned.Count >= 2; },
            () => { WaitClock(0.6f); },
            () => { Shot("sandbox_09_compare"); },
        };

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 600) { Debug.LogError("SandboxShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying || S == null || S.entries.Count == 0) return;
            if (!prepared) { UiShot.Prepare(); prepared = true; WaitClock(0.5f); return; }
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= Steps.Length) { Finish(); return; }
            try { Steps[step](); } catch (Exception e) { Debug.LogError($"SandboxShot: step {step} failed: {e}"); }
            step++;
        }

        static void Finish()
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            SafeArea.Override = null;
            Debug.Log("SandboxShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
