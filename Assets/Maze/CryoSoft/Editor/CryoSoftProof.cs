#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    [InitializeOnLoad]
    public static class CryoSoftProof
    {
        const string Key = "Maze.CryoSoftProof.Active";
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        static Game G => Object.FindAnyObjectByType<Game>();
        static string Out => Environment.GetEnvironmentVariable("CRYO_SOFT_OUT") ?? "Logs/CryoSoftProof";
        static Report report; static List<Action> steps; static int step, frame;
        static double started = -1, next; static bool prepared, done, originalPreview, originalKit, originalSim, originalMachinery, originalPause;
        static float originalVolume, originalTime, originalDistance; static string originalSave, originalHero;
        static Vector3 cameraPosition; static Quaternion originalLightRotation, cameraRotation; static float cameraFov;
        static readonly List<Enemy> actors = new(); static readonly List<Vector2> actorPositions = new();
        static readonly List<Vector3[]> triangles = new();
        static readonly List<Vector3> visibilityPoints = new(); static readonly List<bool> beforeVisibility = new();
        static CryoSoftProof() { if (SessionState.GetBool(Key, false)) Hook(); }
        public static void Run()
        {
            CryoSoftBuilder.Import(); SessionState.SetBool(Key, true); Hook(); EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
        }
        static void Hook() { EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived -= Logged; Application.logMessageReceived += Logged; }
        static void Logged(string message, string stack, LogType type)
        {
            if (done || !SessionState.GetBool(Key, false)) return; report ??= new Report();
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                bool known = (bool)typeof(CryoKitProof).GetMethod("KnownEditorSearchStartup", Flags).Invoke(null, new object[] { message, stack, type });
                if (known) report.editorDiagnostics.Add(message); else report.errors.Add(message + "\n" + stack);
            }
            else if (type == LogType.Warning) report.warnings.Add(message);
        }
        static void Tick()
        {
            if (done) return; if (started < 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 240) { Finish(false, "Timed out"); return; }
            if (!EditorApplication.isPlaying || G == null || G.player == null || G.menu == null || Camera.main == null) return;
            try
            {
                if (!prepared) { Prepare(); return; }
                if (G.state == Game.State.Choosing) { G.Pick(0); Freeze(); Wait(.3); return; }
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < frame) return;
                if (step >= steps.Count) { Finish(true); return; } steps[step++]();
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static string Hash(string s) { using var h = SHA256.Create(); return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant(); }
        static void Check(bool ok, string message) { report.tests.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) report.errors.Add(message); }
        static void Wait(double t) { next = EditorApplication.timeSinceStartup + t; frame = Time.frameCount + 3; }
        static void Freeze() => typeof(CryoKitProof).GetMethod("Freeze", Flags).Invoke(null, new object[] { G });
        static void Start(bool preview)
        {
            G.enabled = true; CryoSoftEnvironment.Enabled = preview; CryoBoundaryPreview.Enabled = false; CryoMachineryPreview.Enabled = false;
            typeof(CryoKitProof).GetMethod("StartCryo", Flags).Invoke(null, new object[] { true }); Freeze();
            if (Environment.GetEnvironmentVariable("CRYO_SOFT_POSE") == "north")
            {
                G.player.p = new Vector2(23, 7); G.player.transform.position = MazeMap.ToWorld(G.player.p);
                typeof(Game).GetField("camTarget", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(G, G.player.p + G.player.aim * G.cfg.camera.lead); G.Tick(0);
            }
        }
        static string Map() => JsonUtility.ToJson(typeof(CryoKitProof).GetMethod("MapSnapshot", Flags).Invoke(null, null));
        static string Probes()
        {
            var s = new StringBuilder(); MazeMap.ComputeFlow(MazeMap.playerSpawn);
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++)
            {
                s.Append(MazeMap.Walkable(x, z)).Append(MazeMap.FlowOpen(x, z)).Append(MazeMap.ShotSolidAt((x + .5f) * MazeMap.C, (z + .5f) * MazeMap.C));
                foreach (var off in new[] { new Vector2(.1f, .1f), new Vector2(1.9f, 1.9f) })
                { var p = new Vector2(x * MazeMap.C, z * MazeMap.C) + off; MazeMap.CollideGrid(ref p, .4f); s.Append(p.x.ToString("R")).Append(',').Append(p.y.ToString("R")).Append(';'); }
            }
            return Hash(s.ToString());
        }
        static void Stage(bool first)
        {
            G.swarm.Clear(); actors.Clear(); UnityEngine.Random.InitState(11);
            if (first)
            {
                actorPositions.Clear();
                foreach (var p in new[] { new Vector2(3, 6), new Vector2(7, 3), new Vector2(15, 3), new Vector2(29, 3), new Vector2(37, 3), new Vector2(11, 3), new Vector2(33, 3), new Vector2(41, 12), new Vector2(3, 25), new Vector2(11, 29), new Vector2(33, 29), new Vector2(41, 25), new Vector2(23, 9) })
                { var v = Camera.main.WorldToViewportPoint(MazeMap.ToWorld(p, .65f)); if (MazeMap.Walkable((int)(p.x / 2), (int)(p.y / 2)) && v.x > .03f && v.x < .97f && v.y > .03f && v.y < .94f) actorPositions.Add(p); }
            }
            foreach (var p in actorPositions) { var e = G.swarm.Spawn(p, 2, G.swarm.TypeByName("drone")); e.p = p; e.spawnT = 1; e.k = Vector2.zero; actors.Add(e); }
            Freeze();
        }
        static void Prepare()
        {
            report ??= new Report(); report.generatedUtc = DateTime.UtcNow.ToString("O"); report.unityVersion = Application.unityVersion; report.capturePose = Environment.GetEnvironmentVariable("CRYO_SOFT_POSE") ?? "spawn";
            report.savedBefore = Hash(PlayerPrefs.GetString("ms.progress", "")); originalSave = JsonUtility.ToJson(Progress.D); originalSim = Progress.Sim; originalHero = Prefs.HeroOverride;
            originalPreview = CryoSoftEnvironment.Enabled; originalKit = CryoKitEnvironment.On; originalMachinery = CryoMachineryPreview.Enabled; originalVolume = AudioListener.volume; originalPause = AudioListener.pause; originalTime = Time.timeScale; originalDistance = G.cfg.camera.distance;
            AudioListener.volume = 0; AudioListener.pause = true; Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); UiShot.Prepare(); prepared = true;
            steps = new()
            {
                () => { Start(false); Wait(2.8); },
                () => { report.beforeMap = Map(); report.beforeProbes = Probes(); report.beforeColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length; Stage(true); BaselineVisibility(); originalLightRotation = RenderSettings.sun.transform.rotation; cameraPosition = Camera.main.transform.position; cameraRotation = Camera.main.transform.rotation; cameraFov = Camera.main.fieldOfView; Wait(.5); },
                () => { Shot("01_before_gameplay"); Start(true); Wait(2.8); },
                () => { report.afterMap = Map(); report.afterProbes = Probes(); Stage(false); Check(report.beforeMap == report.afterMap, "identical grid/markers/spawns/walkable connectivity"); Check(report.beforeProbes == report.afterProbes, "identical movement collisions, shot blocking and flow-open cells"); Check(Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length == report.beforeColliders, "no new colliders"); Wait(.5); },
                () => { Check((Camera.main.transform.position - cameraPosition).sqrMagnitude < .0001f && Quaternion.Angle(Camera.main.transform.rotation, cameraRotation) < .001f && Mathf.Abs(Camera.main.fieldOfView - cameraFov) < .001f && Mathf.Abs(G.cfg.camera.distance - originalDistance) < .001f, "same gameplay camera/configuration"); Inspect(); Shot("02_after_gameplay"); Wait(.3); },
                () => { G.hud.gameObject.SetActive(false); Wait(.25); },
                () => { Shot("03_environment_camera"); Object.FindAnyObjectByType<CryoSoftSceneInfo>().SetPreset(0); Wait(.30); },
                () => { Shot("04_A_soft_front_key"); Object.FindAnyObjectByType<CryoSoftSceneInfo>().SetPreset(1); Wait(.30); },
                () => { Shot("05_B_directional_depth"); Object.FindAnyObjectByType<CryoSoftSceneInfo>().SetPreset(2); Wait(.30); },
                () => { Shot("06_C_balanced"); G.hud.gameObject.SetActive(true); Start(false); Wait(1); },
                () => { Check(Object.FindObjectsByType<CryoSoftSceneInfo>(FindObjectsSortMode.None).Length == 0, "disabling flag restores original presentation"); Check(Quaternion.Angle(RenderSettings.sun.transform.rotation, originalLightRotation) < .001f, "original directional lighting restored after variant removal"); CryoSoftEnvironment.Enabled = true; G.DebugFloor(4); Freeze(); Wait(1); },
                () => { Check(G.floor == 4 && MazeMap.LayoutName(MazeMap.MapIndex) == "cryo-station/cryo-a" && Object.FindObjectsByType<CryoSoftSceneInfo>(FindObjectsSortMode.None).Length == 0, "same layout on floor4 remains unchanged"); CryoSoftEnvironment.Enabled = false; Wait(.2); }
            };
        }
        static void Shot(string name)
        {
            UiShot.Shot(name, new[] { Out }); report.captures.Add(Path.Combine(Out, name + ".png"));
            report.cameraPosition = Camera.main.transform.position; report.cameraRotation = Camera.main.transform.rotation.eulerAngles; report.cameraFov = Camera.main.fieldOfView; report.cameraDistance = G.cfg.camera.distance;
        }
        static Transform Level => (Transform)typeof(Game).GetField("levelRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(G);
        static void Collect(MeshFilter[] meshes)
        {
            triangles.Clear();
            foreach (var f in meshes)
            {
                var vertices = f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v)).ToArray(); var indices = f.sharedMesh.triangles;
                for (int i = 0; i < indices.Length; i += 3) triangles.Add(new[] { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] });
            }
        }
        static void BaselineVisibility()
        {
            Collect(Level.GetComponentsInChildren<MeshFilter>()); visibilityPoints.Clear(); beforeVisibility.Clear();
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++)
            {
                if (!MazeMap.Walkable(x, z)) continue;
                foreach (float y in new[] { .4f, .9f })
                {
                    var p = new Vector3((x + .5f) * MazeMap.C, y, -(z + .5f) * MazeMap.C); var v = Camera.main.WorldToViewportPoint(p);
                    if (v.x < .03f || v.x > .97f || v.y < .03f || v.y > .94f) continue;
                    visibilityPoints.Add(p); beforeVisibility.Add(Hit(p));
                }
            }
        }
        static void Inspect()
        {
            var root = Object.FindObjectsByType<CryoSoftSceneInfo>(FindObjectsSortMode.None).Single();
            var meshes = root.GetComponentsInChildren<MeshFilter>(); Collect(meshes); bool contained = true, normalsValid = true;
            var solid = MazeMap.Rects(ch => ch == '#' || ch == 'B' || ch == 'C' || ch == 'r').Select(r => new Rect(r.x * 2, -(r.y + r.height) * 2, r.width * 2, r.height * 2)).ToArray();
            foreach (var f in meshes)
            {
                report.previewTriangles += f.sharedMesh.triangles.Length / 3; report.vertices += f.sharedMesh.vertexCount;
                foreach (var n in f.sharedMesh.normals) if (!float.IsFinite(n.x) || !float.IsFinite(n.y) || !float.IsFinite(n.z) || n.sqrMagnitude < .7f) normalsValid = false;
                if (!f.name.Contains("SC_wall") && !f.name.Contains("SC_obstacle") && !f.name.Contains("SC_rail")) continue;
                foreach (var v0 in f.sharedMesh.vertices)
                {
                    var v = f.transform.TransformPoint(v0);
                    if (!solid.Any(r => v.x >= r.xMin - .003f && v.x <= r.xMax + .003f && v.z >= r.yMin - .003f && v.z <= r.yMax + .003f)) { contained = false; report.badVertices.Add(v); if (report.badVertices.Count > 30) break; }
                }
            }
            var renderers = root.GetComponentsInChildren<Renderer>(); report.meshRenderers = renderers.Length; var materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray(); report.materials = materials.Length; report.submeshes = meshes.Sum(f => f.sharedMesh.subMeshCount); report.introducedLights = root.GetComponentsInChildren<Light>().Length; report.meshAssetMemoryBytes = meshes.Sum(f => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(f.sharedMesh));
            Check(materials.All(m => m.shader.name == "Maze/CryoSoftSurface" && !m.HasProperty("_BaseMap") && !m.HasProperty("_BumpMap")), "scoped matte/optical materials; no albedo noise or normal detail maps");
            Check(normalsValid, "imported smooth normals are finite and normalized"); Check(contained, "all rounded solid geometry stays inside original blocked grid footprints");
            Check(Object.FindObjectsByType<CryoKitSceneInfo>(FindObjectsSortMode.None).Length == 0, "new Blender environment replaces the original kit in this room");
            Check(!CryoBoundaryPreview.Enabled && !CryoMachineryPreview.Enabled && Object.FindObjectsByType<CryoBoundarySceneInfo>(FindObjectsSortMode.None).Length == 0 && Object.FindObjectsByType<CryoMachineryPreviewInfo>(FindObjectsSortMode.None).Length == 0, "rejected wall and tall machinery proofs remain disabled");
            foreach (var e in actors)
            {
                var rs = e.view.skin.Where(r => r != null).ToArray(); var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); var centre = b.center; var head = new Vector3(centre.x, Mathf.Lerp(b.min.y, b.max.y, .85f), centre.z);
                report.actors.Add(new Actor { kind = "drone", plane = e.p, centre = centre, viewport = Camera.main.WorldToViewportPoint(centre), bodyBlocked = Hit(centre), upperBlocked = Hit(head) });
            }
            var heroRs = G.player.transform.Find("Visual").GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray(); var hb = heroRs[0].bounds; foreach (var r in heroRs.Skip(1)) hb.Encapsulate(r.bounds);
            report.actors.Add(new Actor { kind = "hero", plane = G.player.p, centre = hb.center, viewport = Camera.main.WorldToViewportPoint(hb.center), bodyBlocked = Hit(hb.center), upperBlocked = Hit(new Vector3(hb.center.x, Mathf.Lerp(hb.min.y, hb.max.y, .85f), hb.center.z)) });
            for (int i = 0; i < visibilityPoints.Count; i++)
            {
                bool after = Hit(visibilityPoints[i]); if (beforeVisibility[i]) report.beforeBlockedSamples++; if (after) report.afterBlockedSamples++;
                if (!beforeVisibility[i] && after) report.newOcclusions.Add(visibilityPoints[i]);
            }
            report.visibilitySamples = visibilityPoints.Count; Check(report.newOcclusions.Count == 0, "no newly blocked sampled body/upper positions in visible walkable cells compared with original environment");
            Check(report.actors.Where(a => a.kind == "hero").All(a => !a.bodyBlocked && !a.upperBlocked), "hero sample remains visible through the environment");
            Check(report.introducedLights == 0, "no added realtime or shadow-casting lights");
            Check(report.previewTriangles < 200000 && report.meshRenderers < 40 && report.materials <= 13, "bounded geometry and opaque material/render batches (not a device timing claim)");
        }
        static bool Hit(Vector3 target)
        {
            var origin = Camera.main.transform.position; var delta = target - origin; float limit = delta.magnitude; var dir = delta / limit;
            foreach (var t in triangles)
            {
                var e1 = t[1] - t[0]; var e2 = t[2] - t[0]; var p = Vector3.Cross(dir, e2); float det = Vector3.Dot(e1, p); if (Mathf.Abs(det) < .000001f) continue;
                float inv = 1 / det; var v = origin - t[0]; float u = Vector3.Dot(v, p) * inv; if (u < 0 || u > 1) continue; var q = Vector3.Cross(v, e1); float b = Vector3.Dot(dir, q) * inv; if (b < 0 || u + b > 1) continue;
                float d = Vector3.Dot(e2, q) * inv; if (d > .001f && d < limit - .01f) return true;
            }
            return false;
        }
        static void Finish(bool success, string error = null)
        {
            if (done) return; done = true; report ??= new Report(); if (error != null) report.errors.Add(error);
            if (prepared)
            {
                CryoSoftEnvironment.Enabled = originalPreview; CryoBoundaryPreview.Enabled = false; CryoMachineryPreview.Enabled = originalMachinery; CryoKitEnvironment.On = originalKit;
                G.hud.gameObject.SetActive(true); G.ToMenu(); Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalSave)); Progress.Sim = originalSim; Prefs.HeroOverride = originalHero; AudioListener.volume = originalVolume; AudioListener.pause = originalPause; Time.timeScale = originalTime; SafeArea.Override = null;
                report.savedAfter = Hash(PlayerPrefs.GetString("ms.progress", "")); Check(report.savedBefore == report.savedAfter, "saved progress unchanged");
            }
            report.passed = success && report.errors.Count == 0; report.completedUtc = DateTime.UtcNow.ToString("O"); Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, "proof-report.json"), JsonUtility.ToJson(report, true));
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Logged;
            Debug.Log("CryoSoftProof: " + (report.passed ? "PASS" : "FAIL")); if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1); else EditorApplication.ExitPlaymode();
        }
        [Serializable] class Report
        {
            public bool passed; public string capturePose, unityVersion, generatedUtc, completedUtc, beforeMap, afterMap, beforeProbes, afterProbes, savedBefore, savedAfter;
            public long meshAssetMemoryBytes; public int submeshes, introducedLights, vertices, meshRenderers, materials, visibilitySamples, beforeBlockedSamples, afterBlockedSamples, beforeColliders, previewTriangles; public Vector3 cameraPosition, cameraRotation; public float cameraFov, cameraDistance;
            public List<Vector3> badVertices = new(), newOcclusions = new(); public List<string> tests = new(), errors = new(), warnings = new(), editorDiagnostics = new(), captures = new(); public List<Actor> actors = new();
            public string[] limits = { "Actual Unity Editor captures with temporary progression and staged paused actors. Hero/enemy source and art unchanged; no phone build or measured device-performance claim.", "Visibility compares sampled body and upper points in visible walkable cells; it is not every-pixel analysis or HUD overlap validation.", "Only first-floor Cold Storage is authored. All other floors retain original art. Rounded selection is an Editor art-review choice and defaults to original.", "Cyan cryo glass uses one alpha-blended shell around a luminous core, without refraction; Blender Cycles soft lighting is an authoring preview, not the Unity lighting result." };
        }
        [Serializable] class Actor { public string kind; public Vector2 plane; public Vector3 centre, viewport; public bool bodyBlocked, upperBlocked; }
    }
}
#endif
