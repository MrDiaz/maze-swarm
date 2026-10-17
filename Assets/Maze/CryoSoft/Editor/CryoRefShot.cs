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
    // The rounded Cryo room against the exact reference (Claude pass, 2026-10-14): the first Cryo floor with the CryoSoft
    // variant at the unchanged gameplay camera, HUD hidden, one capture per preset listed in CRYO_REF_PRESETS (default
    // "2,3": the v2 balanced study, then the reference match). Muted; the editor's variant choice is put back afterwards.
    //   CRYO_REF_OUT=dir UISHOT_SIZE=1600x738 Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.CryoRefShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class CryoRefShot
    {
        const string Key = "Maze.CryoRefShot.Active";
        static readonly BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static string Out => Environment.GetEnvironmentVariable("CRYO_REF_OUT") ?? "Logs/CryoRefShot";
        static int step; static bool prepared, wasEnabled; static double next = -1, armed = -1; static List<Action> steps; static (int, double) fpsStart;
        static CryoRefShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            SessionState.SetBool(Key, true); EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
        }
        static Game G => Object.FindAnyObjectByType<Game>();
        // CRYO_REF_DIRECT=1: no UiShot stack. The game camera renders as in play (its own HDR intermediate and post chain)
        // into an sRGB target of the capture size, HUD canvases off; nothing else in the stack.
        static bool Direct => Environment.GetEnvironmentVariable("CRYO_REF_DIRECT") == "1";
        static void DirectShot(string name)
        {
            var size = (Environment.GetEnvironmentVariable("UISHOT_SIZE") ?? "1600x738").Split('x'); int w = int.Parse(size[0]), h = int.Parse(size[1]);
            var cam = Camera.main; var hdr = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear) { antiAliasing = 4 };   // an HDR target, as the game's backbuffer path gets (an 8-bit target clips before bloom)
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevT = cam.targetTexture; cam.targetTexture = hdr; cam.Render(); cam.targetTexture = prevT; Graphics.Blit(hdr, rt); hdr.Release();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev;
            Directory.CreateDirectory(Out); File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release();
            var d = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var st = UnityEngine.Rendering.VolumeManager.instance.stack; var b = st != null ? st.GetComponent<UnityEngine.Rendering.Universal.Bloom>() : null;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Debug.Log($"CryoRefShot: direct {name}: stack bloom active {(b != null && b.IsActive())} threshold {(b != null ? b.threshold.value : -1)} intensity {(b != null ? b.intensity.value : -1)}; asset HDR {(urp != null && urp.supportsHDR)}; volumes {Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None).Length}; renderer post data {(urp != null ? urp.scriptableRenderer?.GetType().Name : "?")}");
            Debug.Log($"CryoRefShot: direct {name}: camera HDR {cam.allowHDR}, post {(d != null && d.renderPostProcessing)}, volumeMask {(d != null ? d.volumeLayerMask.value : -1)}, stack {(d != null && d.cameraStack != null ? d.cameraStack.Count : 0)}");
        }
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void Freeze() => typeof(CryoKitProof).GetMethod("Freeze", F).Invoke(null, new object[] { G });
        static List<Action> Build()
        {
            var l = new List<Action>
            {
                () => { wasEnabled = CryoSoftEnvironment.Enabled; CryoSoftEnvironment.Enabled = true; CryoBoundaryPreview.Enabled = false; CryoMachineryPreview.Enabled = false;
                        typeof(CryoKitProof).GetMethod("StartCryo", F).Invoke(null, new object[] { true }); Freeze(); Later(2.8); },
                () => { Freeze(); G.swarm.Clear(); G.hud.gameObject.SetActive(false); foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (Environment.GetEnvironmentVariable("CRYO_REF_HUD") != "1") cv.enabled = false; var ui = Direct ? null : GameObject.Find("UiShotCam"); if (ui != null) { var d = ui.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); if (d != null) d.renderPostProcessing = true; } Later(0.4); },   // (the capture's stacked UI camera ends the stack: post must run there too; in game the canvases are overlays, no stack)   // HUD off for the environment review
            };
            var presets = (Environment.GetEnvironmentVariable("CRYO_REF_PRESETS") ?? "2,3").Split(',');
            foreach (var p in presets)
            {
                int k = int.Parse(p);
                l.Add(() => { var info = Object.FindAnyObjectByType<CryoSoftSceneInfo>(); if (info == null) { Debug.LogError("CryoRefShot: no CryoSoft room"); return; } info.SetPreset(k); Later(0.5); });
                l.Add(() => { if (Direct) DirectShot("preset" + k); else { Directory.CreateDirectory(Out); UiShot.Shot("preset" + k, new[] { Out }); } var sun = RenderSettings.sun; Debug.Log($"CryoRefShot: preset {k} captured; sun shadows {(sun != null ? sun.shadows.ToString() : "none")} strength {(sun != null ? sun.shadowStrength : 0)} rot {(sun != null ? sun.transform.eulerAngles : Vector3.zero)}; cam {Camera.main.transform.position} fov {Camera.main.fieldOfView}; shadowDistance {QualitySettings.shadowDistance}"); Later(0.2); });
                if (k == 3 && Direct && Environment.GetEnvironmentVariable("CRYO_REF_SHADE") == "1")
                {   // readability in shade: the hero and a few enemies in the divider wall's shadow (a floor point picked from the reference frame)
                    l.Add(() =>
                    {
                        var ray = Camera.main.ViewportPointToRay(new Vector3(0.394f, 0.458f, 0)); new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var t); var w = ray.GetPoint(t);
                        var at = new Vector2(w.x, -w.z); G.player.p = at;
                        for (int i = 0; i < 4; i++) G.DebugSpawn(i, at + new Vector2(0.9f * (i % 2 == 0 ? 1 : -1), 1.1f * (i < 2 ? 1 : -1) * 0.6f + 0.9f));
                        Debug.Log($"CryoRefShot: shade spot {at}"); Later(0.6);
                    });
                    l.Add(() => { DirectShot("shade"); G.swarm.Clear(); Later(0.3); });
                }
            }
            // post cost: uncapped frame rate with the room's bloom on, then off (vsync and the frame cap lifted for the sample)
            foreach (bool glow in new[] { true, false, true })
            {
                bool gl = glow;
                l.Add(() => { CryoSoftSceneInfo.Glow = gl; Object.FindAnyObjectByType<CryoSoftSceneInfo>()?.SetPreset(3); QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; fpsStart = (Time.frameCount, EditorApplication.timeSinceStartup); Later(4.0); });
                l.Add(() => { var (a0, b0) = fpsStart; double t = EditorApplication.timeSinceStartup - b0; int n = Time.frameCount - a0; Debug.Log($"CryoRefShot: uncapped editor {(gl ? "WITH" : "WITHOUT")} room bloom: {n / t:0.0} fps, {t * 1000 / n:0.00} ms/frame"); Later(0.1); });
            }
            l.Add(() => { CryoSoftSceneInfo.Glow = false; Object.FindAnyObjectByType<CryoSoftSceneInfo>()?.SetPreset(3); QualitySettings.vSyncCount = 1; Later(0.4); });
            l.Add(() => { if (Direct) DirectShot("preset3_noglow"); CryoSoftSceneInfo.Glow = true; Object.FindAnyObjectByType<CryoSoftSceneInfo>()?.SetPreset(3); Later(0.3); });
            l.Add(() => { int f0 = Time.frameCount; double r0 = EditorApplication.timeSinceStartup; G.hud.gameObject.SetActive(true); fpsStart = (f0, r0); Later(5.0); });
            l.Add(() => { var (f0, r0) = fpsStart; Debug.Log($"CryoRefShot: editor fps over 5 s: {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - r0):0.0}"); Later(0.1); });
            l.Add(() => { int tris = 0, rends = 0; var room = Object.FindAnyObjectByType<CryoSoftSceneInfo>(); if (room != null) foreach (var mf in room.GetComponentsInChildren<MeshFilter>()) { tris += mf.sharedMesh.triangles.Length / 3; rends++; } int mats = 0; if (room != null) { var set = new HashSet<Material>(); foreach (var r in room.GetComponentsInChildren<Renderer>()) foreach (var m in r.sharedMaterials) set.Add(m); mats = set.Count; } Debug.Log($"CryoRefShot: room {rends} mesh renderers, {tris} triangles, {mats} materials"); Later(0.1); });
            return l;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup; if (armed < 0) armed = now;
            if (now - armed > 150) { Debug.LogError("CryoRefShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || Camera.main == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; if (!Direct) UiShot.Prepare(); steps = Build(); Later(2); return; }
            if (now < next) return;
            if (g.state == Game.State.Choosing) { g.Pick(0); Freeze(); Later(0.3); return; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"CryoRefShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            CryoSoftEnvironment.Enabled = wasEnabled; SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false;
            Debug.Log("CryoRefShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
#endif
