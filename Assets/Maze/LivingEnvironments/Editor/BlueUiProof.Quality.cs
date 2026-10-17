#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    public static partial class BlueUiProof
    {
        [Serializable] sealed class QualityFrame { public string file; public double seconds; public int unityFrame, floor; public float timeScale, feelScale; }
        [Serializable] sealed class QualityVideo
        {
            public string name, method = "Silent actual Unity render frames at native timestamps; automated/debug input, temporary QA save and god mode. Capture overhead means this is not a performance benchmark.";
            public int width, height; public double durationSeconds; public List<QualityFrame> frames = new();
        }
        [Serializable] sealed class QualitySample
        {
            public string screen; public double seconds; public bool recording, frameTimingValid, gpuTimingValid;
            public int width, height;
            public double cpuFrameMs, mainThreadMs, gpuFrameMs;
            public int cameras, visibleRenderers, activeUiGraphics, enemies, particles, editorDrawCalls, editorSetPassCalls;
            public long visibleGeometryTriangles, liveRenderTargetBytes;
        }
        [Serializable] sealed class QualityMetrics
        {
            public string unity, renderer, device, method = "Editor observations. Frustum-tested enabled geometry is a visible-mesh upper bound, not draw calls or shadow-pass triangles. Native RenderTexture memory is Unity-reported Editor resource memory. Frame timings, when available, include Editor overhead and delayed GPU results. No phone FPS claim.";
            public string apiReference = "https://docs.unity3d.com/6000.6/Documentation/ScriptReference/FrameTimingManager.html";
            public List<QualitySample> samples = new();
        }
        static QualityVideo qualityVideo;
        static QualityMetrics qualityMetrics;
        static Texture2D qualityPixels;
        static string qualityLabel, qualityVideoDirectory;
        static double qualityVideoStart, qualityNextVideo, qualityNextMetric;
        static int qualityLastFrame = -1;
        static readonly FrameTiming[] qualityTimings = new FrameTiming[1];
        static readonly List<Enemy> qualityFrozen = new();
        static UnityEngine.UI.Button qualityPressed;
        static Barrel qualityBarrel;
        static bool qualitySoftBefore;
        static Quaternion qualitySun;
        static float qualityShadowDistance, qualityShadowStrength;
        static bool qualityCameraPost;

        static void QualityLabel(string label)
        {
            qualityLabel = label; qualityNextMetric = 0;
            qualityMetrics ??= new QualityMetrics { unity = Application.unityVersion, renderer = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.deviceModel };
        }
        static void TickQualityRecording()
        {
            if (string.IsNullOrEmpty(qualityLabel) || target == null || Time.frameCount == qualityLastFrame) return;
            qualityLastFrame = Time.frameCount;
            double now = EditorApplication.timeSinceStartup;
            if (FrameTimingManager.IsFeatureEnabled()) FrameTimingManager.CaptureFrameTimings();
            if (now >= qualityNextMetric)
            {
                qualityNextMetric = now + .75;
                var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray();
                var frusta = cameras.Select(c => (camera: c, planes: GeometryUtility.CalculateFrustumPlanes(c))).ToArray();
                var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && !r.forceRenderingOff && frusta.Any(f => (f.camera.cullingMask & (1 << r.gameObject.layer)) != 0 && GeometryUtility.TestPlanesAABB(f.planes, r.bounds))).ToArray();
                long triangles = 0;
                foreach (var r in renderers)
                {
                    var filter = r.GetComponent<MeshFilter>();
                    Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : filter != null ? filter.sharedMesh : null;
                    if (mesh != null) for (int s = 0; s < mesh.subMeshCount; s++) triangles += (long)mesh.GetIndexCount(s) / 3;
                }
                var targets = Object.FindObjectsByType<LivingEnvironmentView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(v => v.PreviewCamera?.targetTexture).Where(t => t != null).Distinct().ToArray();
                uint timings = FrameTimingManager.IsFeatureEnabled() ? FrameTimingManager.GetLatestTimings(1, qualityTimings) : 0;
                var sample = new QualitySample
                {
                    screen = qualityLabel, seconds = now, recording = qualityVideo != null, width = width, height = height,
                    frameTimingValid = timings > 0 && qualityTimings[0].cpuFrameTime > 0, gpuTimingValid = timings > 0 && qualityTimings[0].gpuFrameTime > 0,
                    cpuFrameMs = timings > 0 ? qualityTimings[0].cpuFrameTime : 0,
                    mainThreadMs = timings > 0 ? qualityTimings[0].cpuMainThreadFrameTime : 0,
                    gpuFrameMs = timings > 0 ? qualityTimings[0].gpuFrameTime : 0,
                    cameras = cameras.Length, visibleRenderers = renderers.Length, editorDrawCalls = QualityEditorStat("drawCalls"), editorSetPassCalls = QualityEditorStat("setPassCalls"),
                    visibleGeometryTriangles = triangles,
                    liveRenderTargetBytes = targets.Sum(t => Profiler.GetRuntimeMemorySizeLong(t)),
                    activeUiGraphics = Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None).Count(g => g.isActiveAndEnabled && g.canvas != null && g.canvas.enabled && g.canvas.rootCanvas.enabled),
                    enemies = G.swarm.enemies.Count(e => !e.dead),
                    particles = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Sum(p => p.particleCount)
                };
                qualityMetrics.samples.Add(sample);
                File.WriteAllText(Path.Combine(Out, "quality-metrics.json"), JsonUtility.ToJson(qualityMetrics, true));
            }
            if (qualityVideo == null || now < qualityNextVideo || qualityVideo.frames.Count >= 420) return;
            qualityNextVideo = now + 1.0 / 12;
            var previous = RenderTexture.active; RenderTexture.active = target;
            qualityPixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); qualityPixels.Apply(); RenderTexture.active = previous;
            string file = Path.Combine(qualityVideoDirectory, "frame_" + qualityVideo.frames.Count.ToString("D4") + ".png");
            File.WriteAllBytes(file, qualityPixels.EncodeToPNG());
            qualityVideo.frames.Add(new QualityFrame { file = file, seconds = now - qualityVideoStart, unityFrame = Time.frameCount - 1, floor = G.floor, timeScale = Time.timeScale, feelScale = Feel.Scale });
        }
        static void BeginQualityVideo(string name)
        {
            StopQualityVideo(); name += "_" + width + "x" + height; qualityVideoDirectory = Path.Combine(Out, name + "-frames"); Directory.CreateDirectory(qualityVideoDirectory);
            qualityVideo = new QualityVideo { name = name, width = width, height = height };
            qualityPixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            qualityVideoStart = EditorApplication.timeSinceStartup; qualityNextVideo = 0;
        }
        static void StopQualityVideo()
        {
            if (qualityVideo != null)
            {
                qualityVideo.durationSeconds = EditorApplication.timeSinceStartup - qualityVideoStart;
                File.WriteAllText(Path.Combine(Out, qualityVideo.name + ".capture.json"), JsonUtility.ToJson(qualityVideo, true));
                qualityVideo = null;
            }
            if (qualityPixels != null) Object.DestroyImmediate(qualityPixels);
            qualityPixels = null;
        }
        static int QualityEditorStat(string name)
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var property = typeof(UnityStats).GetProperty(name, flags);
            if (property != null) return Convert.ToInt32(property.GetValue(null));
            var field = typeof(UnityStats).GetField(name, flags);
            return field != null ? Convert.ToInt32(field.GetValue(null)) : -1;
        }
        static PointerEventData QualityPointer() => new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = Bounds((RectTransform)qualityPressed.transform).center };
        static void SpawnQualityFrozen()
        {
            qualityFrozen.Clear();
            var positions = Enumerable.Range(0, MazeMap.H).SelectMany(z => Enumerable.Range(0, MazeMap.W).Where(x => MazeMap.Walkable(x, z)).Select(x => new Vector2((x + .5f) * MazeMap.C, (z + .5f) * MazeMap.C)))
                .Where(p => Vector2.Distance(p, G.player.p) > 2 && Vector2.Distance(p, G.player.p) < 7 && G.barrels.At(p, .6f) == null).OrderBy(p => Vector2.Distance(p, G.player.p)).Take(6);
            foreach (var p in positions) { var e = G.swarm.Spawn(p, 1, 0); e.p = p; e.frozen = 1.2f; qualityFrozen.Add(e); }
            Check(qualityFrozen.Count == 6, "staged freeze uses six actual enemies on walkable cells");
        }
        static void AddQualitySteps(Vector2Int size)
        {
            Add(() => { MenuTitle(size); Wait(.7); });
            Add(() => { Click(Title.PlayButton); QualityLabel("Hangar"); Wait(1.2); });
            Add(() => { CaptureScreen("quality_hangar"); qualityPressed = P.GearButton; ExecuteEvents.Execute(qualityPressed.gameObject, QualityPointer(), ExecuteEvents.pointerDownHandler); Wait(.12); });
            Add(() => { Check(qualityPressed.transform.localScale.x < .999f, "button gives restrained unscaled press feedback"); ExecuteEvents.Execute(qualityPressed.gameObject, QualityPointer(), ExecuteEvents.pointerExitHandler); ExecuteEvents.Execute(qualityPressed.gameObject, QualityPointer(), ExecuteEvents.pointerUpHandler); Wait(.3); });
            Add(() => { Check(Mathf.Abs(qualityPressed.transform.localScale.x - 1) < .001f && P.Current == MenuPresentation.Screen.Home, "cancelled press resets and does not activate navigation"); Click(P.GearButton); QualityLabel("Equipment"); Wait(1.2); });
            Add(() => { Gear.ChooseFilter(-1); CaptureScreen("quality_equipment"); Click(Button(Gear, "Loadout")); QualityLabel("Loadout"); Wait(1.2); });
            Add(() => { Check(P.Current == MenuPresentation.Screen.Weapons, "real Loadout callback opens the retained weapon screen"); Check(Object.FindObjectsByType<LivingEnvironmentView>(FindObjectsSortMode.None).Count(v => v.Rendering) == 1, "Loadout renders one live workshop camera"); CaptureScreen("quality_loadout"); P.Back(); Wait(.5); });
            Add(() => { qualitySoftBefore = CryoSoftEnvironment.Enabled; qualitySun = RenderSettings.sun != null ? RenderSettings.sun.transform.rotation : Quaternion.identity; qualityShadowStrength = RenderSettings.sun != null ? RenderSettings.sun.shadowStrength : 0; qualityCameraPost = Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing; qualityShadowDistance = ((UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline).shadowDistance; CryoSoftEnvironment.Enabled = true; StartRun(size); QualityLabel("Cryo idle"); Wait(3.0); });
            Add(() => { CaptureScreen("quality_cryo_idle"); G.holdFire = false; G.demo = true; G.godMode = true; G.enabled = true; QualityLabel("Cryo automated combat"); BeginQualityVideo("cryo-automated-combat"); Wait(5); });
            Add(() => { CaptureScreen("quality_combat_05"); Wait(7); });
            Add(() => { CaptureScreen("quality_combat_12"); Wait(8); });
            Add(() => { StopQualityVideo(); G.enabled = false; G.demo = false; G.holdFire = true; CaptureScreen("quality_combat_20"); StartRun(size); G.enabled = true; QualityLabel("Cryo staged effects"); Wait(2.5); });
            Add(() => { SpawnQualityFrozen(); BeginQualityVideo("cryo-staged-effects"); Wait(.30); });
            Add(() => { Check(FirstCryoFx.Get().FrozenMarkerCount >= 6, "actual frozen lifetimes drive six visible state markers"); CaptureScreen("quality_frozen_status"); Wait(1.8); });
            Add(() => { Check(qualityFrozen.All(e => e.frozen <= 0), "staged enemies thaw through the existing runtime countdown"); Check(FirstCryoFx.Get().FrozenMarkerCount == 0, "thawed enemies leave no frozen-state markers"); foreach (var e in qualityFrozen.Where(e => !e.dead)) e.frozen = 1.2f; G.KillEnemy(qualityFrozen[0]); Wait(.22); });
            Add(() => { Check(FirstCryoFx.Get().FrozenMarkerCount <= 5, "dead enemy loses its frozen-state marker"); var at = qualityFrozen[0].p; var e = G.swarm.Spawn(at, 1, 0); Check(e.frozen <= 0, "reused enemy view starts without stale frozen state"); Check(FirstCryoFx.Get().FrozenMarkerPoolCount <= 24, "frozen-state marker pool stays bounded"); qualityBarrel = G.barrels.list.First(b => b.alive); G.barrels.Damage(qualityBarrel, G.cfg.barrels.hp + 1); Wait(.08); });
            Add(() => { CaptureScreen("quality_barrel_fuse"); Wait(Mathf.Max(.08f, qualityBarrel.fuse + .06f)); });
            Add(() => { CaptureScreen("quality_barrel_blast"); Check(!qualityBarrel.alive, "painted barrel still explodes through its production fuse"); G.demo = true; G.DebugSkill(); Wait(.45); });
            Add(() => { CaptureScreen("quality_heavy_barrage"); Wait(2.8); });
            Add(() => { StopQualityVideo(); G.enabled = false; G.demo = false; G.ToMenu(); CryoSoftEnvironment.Enabled = qualitySoftBefore; QualityLabel("Return to Hangar"); Wait(.7); });
            Add(() => { Check(FirstCryoFx.Get().FrozenMarkerCount == 0, "returning to the menu clears active frozen-state markers"); Check(RenderSettings.sun == null || Quaternion.Angle(RenderSettings.sun.transform.rotation, qualitySun) < .01f, "Cryo restores the original scene light on exit (" + (RenderSettings.sun != null ? Quaternion.Angle(RenderSettings.sun.transform.rotation, qualitySun).ToString("F4") : "no sun") + ")"); Check(RenderSettings.sun == null || Mathf.Abs(RenderSettings.sun.shadowStrength - qualityShadowStrength) < .001f, "Cryo restores original shadow strength on menu return"); Check(Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing == qualityCameraPost, "Cryo restores original camera post-processing on menu return"); Check(Mathf.Abs(((UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline).shadowDistance - qualityShadowDistance) < .01f, "Cryo restores the original shadow distance on exit"); CaptureScreen("quality_return_hangar"); qualityLabel = null; });
        }
    }
}
#endif
