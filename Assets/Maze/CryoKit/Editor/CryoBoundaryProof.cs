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
    public static class CryoBoundaryProof
    {
        const string Key = "Maze.CryoBoundaryProof.Active";
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        static Game G => Object.FindAnyObjectByType<Game>();
        static string Out => Environment.GetEnvironmentVariable("CRYO_BOUNDARY_OUT") ?? "Logs/CryoBoundaryProof";
        static Report report; static List<Action> steps; static int step, frame;
        static double started = -1, next; static bool prepared, done, originalPreview, originalKit, originalSim, originalMachinery, originalPause;
        static float originalVolume, originalTime, originalDistance; static string originalSave, originalHero;
        static Vector3 cameraPosition; static Quaternion cameraRotation; static float cameraFov;
        static readonly List<Enemy> actors = new(); static readonly List<Vector2> actorPositions = new();
        static readonly List<Vector3[]> triangles = new();
        static CryoBoundaryProof() { if (SessionState.GetBool(Key, false)) Hook(); }
        public static void Run()
        {
            SessionState.SetBool(Key, true); Hook(); EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
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
            G.enabled = true; CryoBoundaryPreview.Enabled = preview; CryoMachineryPreview.Enabled = false;
            typeof(CryoKitProof).GetMethod("StartCryo", Flags).Invoke(null, new object[] { true }); Freeze();
            if (Environment.GetEnvironmentVariable("CRYO_BOUNDARY_POSE") == "north")
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
            report ??= new Report(); report.generatedUtc = DateTime.UtcNow.ToString("O"); report.unityVersion = Application.unityVersion; report.capturePose = Environment.GetEnvironmentVariable("CRYO_BOUNDARY_POSE") ?? "spawn";
            report.savedBefore = Hash(PlayerPrefs.GetString("ms.progress", "")); originalSave = JsonUtility.ToJson(Progress.D); originalSim = Progress.Sim; originalHero = Prefs.HeroOverride;
            originalPreview = CryoBoundaryPreview.Enabled; originalKit = CryoKitEnvironment.On; originalMachinery = CryoMachineryPreview.Enabled; originalVolume = AudioListener.volume; originalPause = AudioListener.pause; originalTime = Time.timeScale; originalDistance = G.cfg.camera.distance;
            AudioListener.volume = 0; AudioListener.pause = true; Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); UiShot.Prepare(); prepared = true;
            steps = new()
            {
                () => { Start(false); Wait(2.8); },
                () => { report.beforeMap = Map(); report.beforeProbes = Probes(); report.beforeColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length; Stage(true); cameraPosition = Camera.main.transform.position; cameraRotation = Camera.main.transform.rotation; cameraFov = Camera.main.fieldOfView; Wait(.5); },
                () => { Shot("01_before_gameplay"); Start(true); Wait(2.8); },
                () => { report.afterMap = Map(); report.afterProbes = Probes(); Stage(false); Check(report.beforeMap == report.afterMap, "identical grid/markers/spawns/walkable connectivity"); Check(report.beforeProbes == report.afterProbes, "identical movement collisions, shot blocking and flow-open cells"); Check(Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length == report.beforeColliders, "no new colliders"); Wait(.5); },
                () => { Check((Camera.main.transform.position - cameraPosition).sqrMagnitude < .0001f && Quaternion.Angle(Camera.main.transform.rotation, cameraRotation) < .001f && Mathf.Abs(Camera.main.fieldOfView - cameraFov) < .001f && Mathf.Abs(G.cfg.camera.distance - originalDistance) < .001f, "same gameplay camera/configuration"); Inspect(); Shot("02_after_gameplay"); Wait(.3); },
                () => { Start(false); Wait(1); },
                () => { Check(Object.FindObjectsByType<CryoBoundarySceneInfo>(FindObjectsSortMode.None).Length == 0, "disabling flag restores original presentation"); CryoBoundaryPreview.Enabled = true; G.DebugFloor(4); Freeze(); Wait(1); },
                () => { Check(G.floor == 4 && MazeMap.LayoutName(MazeMap.MapIndex) == "cryo-station/cryo-a" && Object.FindObjectsByType<CryoBoundarySceneInfo>(FindObjectsSortMode.None).Length == 0, "same layout on floor4 remains unchanged"); CryoBoundaryPreview.Enabled = false; Wait(.2); }
            };
        }
        static void Shot(string name)
        {
            UiShot.Shot(name, new[] { Out }); report.captures.Add(Path.Combine(Out, name + ".png"));
            report.cameraPosition = Camera.main.transform.position; report.cameraRotation = Camera.main.transform.rotation.eulerAngles; report.cameraFov = Camera.main.fieldOfView; report.cameraDistance = G.cfg.camera.distance;
        }
        static void Inspect()
        {
            var info = Object.FindObjectsByType<CryoBoundarySceneInfo>(FindObjectsSortMode.None).Single(); report.bays = info.bays; report.cutawayBays = info.cutawayBays; report.wallFootprints = info.wallFootprints.ToArray(); report.hardware = info.serviceHardware.Count;
            var kit = Object.FindObjectsByType<CryoKitSceneInfo>(FindObjectsSortMode.None).Single(); report.nativeTriangles = kit.nativeTriangles; report.nativeInstances = kit.nativeInstances;
            var meshes = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m => m.GetComponent<Renderer>() != null && (m.GetComponent<Renderer>().sharedMaterials.Any(x => x != null && x.name.StartsWith("Cryo boundary ")) || m.GetComponentInParent<CryoBoundarySceneInfo>() != null)).ToArray();
            bool contained = true; triangles.Clear();
            foreach (var f in meshes)
            {
                var vertices = f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v)).ToArray();
                foreach (var v in vertices) if (!info.wallFootprints.Any(r => v.x >= r.xMin - .002f && v.x <= r.xMax + .002f && v.z >= r.yMin - .002f && v.z <= r.yMax + .002f)) contained = false;
                var indices = f.sharedMesh.triangles; report.previewTriangles += indices.Length / 3;
                for (int i = 0; i < indices.Length; i += 3) triangles.Add(new[] { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] });
            }
            Check(contained, "all preview vertices stay inside original blocked perimeter footprints"); Check(info.cutawayBays > 0 && info.bays > info.cutawayBays, "foreground cutaway and taller rear/service bays present");
            Check(!CryoMachineryPreview.Enabled && Object.FindObjectsByType<CryoMachineryPreviewInfo>(FindObjectsSortMode.None).Length == 0, "tall machinery remains disabled"); Check(actors.Count >= 5, "staged actors cover visible perimeter lanes");
            foreach (var e in actors)
            {
                var rs = e.view.skin.Where(r => r != null).ToArray(); var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                var centre = b.center; var head = new Vector3(centre.x, Mathf.Lerp(b.min.y, b.max.y, .85f), centre.z);
                var sample = new Actor { kind = "drone", plane = e.p, centre = centre, viewport = Camera.main.WorldToViewportPoint(centre), bodyBlocked = Hit(centre), upperBlocked = Hit(head) }; report.actors.Add(sample);
            }
            var heroRs = G.player.transform.Find("Visual").GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            var heroBounds = heroRs[0].bounds; foreach (var r in heroRs.Skip(1)) heroBounds.Encapsulate(r.bounds);
            var heroCentre = heroBounds.center; var heroUpper = new Vector3(heroCentre.x, Mathf.Lerp(heroBounds.min.y, heroBounds.max.y, .85f), heroCentre.z);
            report.actors.Add(new Actor { kind = "hero", plane = G.player.p, centre = heroCentre, viewport = Camera.main.WorldToViewportPoint(heroCentre), bodyBlocked = Hit(heroCentre), upperBlocked = Hit(heroUpper) });
            report.addedBoxTriangles = info.boxes * 12;
            Check(report.actors.All(a => !a.bodyBlocked && !a.upperBlocked), "preview walls do not occlude sampled actual actor bounds centres/upper points");
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
                CryoBoundaryPreview.Enabled = originalPreview; CryoMachineryPreview.Enabled = originalMachinery; CryoKitEnvironment.On = originalKit;
                G.ToMenu(); Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalSave)); Progress.Sim = originalSim; Prefs.HeroOverride = originalHero; AudioListener.volume = originalVolume; AudioListener.pause = originalPause; Time.timeScale = originalTime; SafeArea.Override = null;
                report.savedAfter = Hash(PlayerPrefs.GetString("ms.progress", "")); Check(report.savedBefore == report.savedAfter, "saved progress unchanged");
            }
            report.passed = success && report.errors.Count == 0; report.completedUtc = DateTime.UtcNow.ToString("O"); Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, "proof-report.json"), JsonUtility.ToJson(report, true));
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Logged;
            Debug.Log("CryoBoundaryProof: " + (report.passed ? "PASS" : "FAIL")); if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1); else EditorApplication.ExitPlaymode();
        }
        [Serializable] class Report
        {
            public bool passed; public string capturePose, unityVersion, generatedUtc, completedUtc, beforeMap, afterMap, beforeProbes, afterProbes, savedBefore, savedAfter;
            public int addedBoxTriangles, beforeColliders, bays, cutawayBays, hardware, previewTriangles, nativeTriangles, nativeInstances; public Vector3 cameraPosition, cameraRotation; public float cameraFov, cameraDistance; public Rect[] wallFootprints;
            public List<string> tests = new(), errors = new(), warnings = new(), editorDiagnostics = new(), captures = new(); public List<Actor> actors = new();
            public string[] limits = { "Actual Unity Editor captures with temporary progression and staged/frozen actors; no phone build or device-performance claim.", "Geometric visibility test covers the changed perimeter meshes and sampled actual actor bounds; it is not whole-scene transparency/UI visibility analysis.", "Opt-in flag is disabled by default; only first-floor Cold Storage perimeter is changed. Existing internal divider/islands and all other floors stay on the original path.", "Observation panels use opaque frosted glazing; no traversable or animated doors are added." };
        }
        [Serializable] class Actor { public string kind; public Vector2 plane; public Vector3 centre, viewport; public bool bodyBlocked, upperBlocked; }
    }
}
#endif
