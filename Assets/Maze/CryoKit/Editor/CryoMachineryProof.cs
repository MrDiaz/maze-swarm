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
    // One disabled-by-default machinery replacement. No scene/prefab/save writes.
    [InitializeOnLoad]
    public static class CryoMachineryProof
    {
        const string Key = "Maze.CryoMachineryProof.Active", Out = "Logs/MachineryPreview";
        static readonly BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        static readonly Rect Footprint = new(20, -14, 6, 4);
        static Game G => Object.FindAnyObjectByType<Game>();
        static string Root => Directory.GetParent(Application.dataPath).FullName;
        static Report report;
        static List<Action> steps;
        static int step, frame;
        static double started = -1, next;
        static bool prepared, done, originalPreview, originalKit, originalFloor, originalSim, originalGameEnabled, originalPause;
        static string originalSave, originalHero;
        static float originalVolume, originalTime, originalDistance;
        static SectorDef originalSector; static int originalTier; static Game.Goal[] originalPlan;
        static Vector3 gameCameraPosition; static Quaternion gameCameraRotation; static float gameCameraFov;
        static readonly List<Vector2> actorPositions = new();
        static readonly List<Vector3[]> triangles = new();

        static CryoMachineryProof() { if (SessionState.GetBool(Key, false)) Hook(); }
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start machinery proof outside Play mode.");
            SessionState.SetBool(Key, true); Hook();
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
        }
        static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Logged; Application.logMessageReceived += Logged;
        }
        static void Logged(string message, string stack, LogType type)
        {
            if (done || !SessionState.GetBool(Key, false)) return;
            report ??= new Report();
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                bool known = (bool)typeof(CryoKitProof).GetMethod("KnownEditorSearchStartup", Flags).Invoke(null, new object[] { message, stack, type });
                if (known) report.editorDiagnostics.Add(type + ": " + message + "\n" + stack);
                else report.errors.Add(message + "\n" + stack);
            }
            else if (type == LogType.Warning) report.warnings.Add(message);
        }
        static void Tick()
        {
            if (done) return;
            if (started < 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 240) { Finish(false, "Capture timed out."); return; }
            if (!EditorApplication.isPlaying || G == null || G.player == null || G.menu == null || Camera.main == null) return;
            try
            {
                if (!prepared) { Prepare(); return; }
                Require(report.errors.Count == 0, "Unity logged an error during the proof.");
                if (G.state == Game.State.Choosing) { Require(G.offer.Count > 0, "Empty startup choice."); G.Pick(0); Freeze(); Wait(.3); return; }
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < frame) return;
                if (step >= steps.Count) { Finish(true); return; }
                steps[step++]();
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static void Prepare()
        {
            report ??= new Report(); report.unityVersion = Application.unityVersion; report.generatedUtc = DateTime.UtcNow.ToString("O");
            report.savedProgressBefore = Hash(PlayerPrefs.GetString("ms.progress", ""));
            originalSave = JsonUtility.ToJson(Progress.D); originalSim = Progress.Sim; originalHero = Prefs.HeroOverride;
            originalPreview = CryoMachineryPreview.Enabled; originalKit = CryoKitEnvironment.On; originalFloor = CryoFloor.On;
            originalGameEnabled = G.enabled; originalDistance = G.cfg.camera.distance;
            originalVolume = AudioListener.volume; originalPause = AudioListener.pause; originalTime = Time.timeScale;
            originalSector = Game.PendingSector; originalTier = Game.PendingTier; originalPlan = Game.PendingPlan;
            prepared = true; AudioListener.volume = 0; AudioListener.pause = true;
            Require(Mathf.Abs(G.cfg.camera.fov - 30) < .001f && Mathf.Abs(G.cfg.camera.pitch - 57) < .001f && Mathf.Abs(G.cfg.camera.yaw + 14) < .001f, "Production camera tuning changed.");
            Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); UiShot.Prepare();
            steps = new()
            {
                () => { Start(false); Wait(2.8); },
                () => { report.beforeMap = Snapshot(); SetActors(); Wait(.5); },
                () => { Shot("01_v2_before_gameplay", false); CaptureCamera(); },
                () => { Start(true); Wait(2.8); },
                () => { report.afterMap = Snapshot(); Require(report.beforeMap == report.afterMap, "Authoritative map/collision/navigation data changed."); InspectBlock(); SetActors(); Wait(.5); },
                () => { RequireSameCamera(); MeasureOcclusion(); Shot("02_v2_after_gameplay", false); Wait(.3); },
                () => { InspectCamera(); Wait(.5); },
                () => { Shot("03_v2_asset_inspection", true); RestoreCamera(); Wait(.3); },
                () => { CryoMachineryPreview.Enabled = false; G.enabled = true; Start(false); Wait(1); },
                () => { Require(Object.FindObjectsByType<CryoMachineryPreviewInfo>(FindObjectsSortMode.None).Length == 0, "Preview remained after disabling."); report.disableRestored = true; }
            };
        }
        static void Start(bool preview)
        {
            CryoMachineryPreview.Enabled = preview;
            typeof(CryoKitProof).GetMethod("StartCryo", Flags).Invoke(null, new object[] { true });
            G.cfg.camera.distance = 40; Freeze();
        }
        static string Snapshot() => JsonUtility.ToJson(typeof(CryoKitProof).GetMethod("MapSnapshot", Flags).Invoke(null, null));
        static void Freeze() => typeof(CryoKitProof).GetMethod("Freeze", Flags).Invoke(null, new object[] { G });
        static void Wait(double seconds) { next = EditorApplication.timeSinceStartup + seconds; frame = Time.frameCount + 3; }
        static void SetActors()
        {
            G.swarm.Clear(); actorPositions.Clear(); UnityEngine.Random.InitState(11);
            foreach (var world in new[] { new Vector2(19,-12), new Vector2(27,-12), new Vector2(23,-15), new Vector2(21,-9), new Vector2(23,-9), new Vector2(25,-9), new Vector2(23,-8), new Vector2(23,-17) })
            {
                Vector2 p = new(world.x, -world.y);
                if (!MazeMap.Walkable(Mathf.FloorToInt(p.x / MazeMap.C), Mathf.FloorToInt(p.y / MazeMap.C))) continue;
                var e = G.swarm.Spawn(p, 2, G.swarm.TypeByName("drone")); e.p = p; e.spawnT = 1; e.k = Vector2.zero; actorPositions.Add(p);
            }
            Require(actorPositions.Count >= 4, "Too few surrounding walkable samples."); Freeze();
        }
        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); Require(rs.Length > 0, "Block has no renderer.");
            Bounds b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
        }
        static CryoMachineryPreviewInfo Block() => Object.FindObjectsByType<CryoMachineryPreviewInfo>(FindObjectsSortMode.None).Single();
        static void InspectBlock()
        {
            var info = Block(); var b = BoundsOf(info.gameObject); report.bounds = b;
            Require((info.transform.localScale - Vector3.one).sqrMagnitude < .000001f, "Authored scale changed.");
            Require(Mathf.Abs(b.min.y) < .002f && Mathf.Abs(b.size.y - 3.808f) < .003f, "Unexpected machinery height/pivot.");
            Require(Mathf.Abs(b.size.x - 6) < .002f && Mathf.Abs(b.size.z - 4) < .002f, "Footprint dimensions changed.");
            Require(b.min.x >= Footprint.xMin - .002f && b.max.x <= Footprint.xMax + .002f && b.min.z >= Footprint.yMin - .002f && b.max.z <= Footprint.yMax + .002f, "Visual block spills outside authorized footprint.");
            Require(info.GetComponentsInChildren<Collider>().Length == 0, "New visual block has colliders.");
            var scene = Object.FindObjectsByType<CryoKitSceneInfo>(FindObjectsSortMode.None).Single();
            report.oldFootprintProps = scene.placements.Count(p => p.instance != null && p.allowedXZ == Footprint);
            Require(report.oldFootprintProps == 0, "Old kit props remain in replacement footprint.");
            triangles.Clear(); int meshCount = 0; var materials = new HashSet<Material>();
            foreach (var mf in info.GetComponentsInChildren<MeshFilter>())
            {
                meshCount++; var mesh = mf.sharedMesh; var vertices = mesh.vertices.Select(mf.transform.TransformPoint).ToArray();
                for (int s = 0; s < mesh.subMeshCount; s++) { var indices = mesh.GetTriangles(s); for (int i=0;i<indices.Length;i+=3) triangles.Add(new[] { vertices[indices[i]], vertices[indices[i+1]], vertices[indices[i+2]] }); }
            }
            foreach(var r in info.GetComponentsInChildren<MeshRenderer>()) foreach(var m in r.sharedMaterials) materials.Add(m);
            report.meshes = meshCount; report.materials = materials.Count; report.triangles = triangles.Count;
            Require(meshCount == 1 && materials.Count == 1 && triangles.Count == 17202, "Imported geometry/material count differs from authored v2.");
            report.geometryPassed = true; report.mapAndCollisionPassed = true;
        }
        static void CaptureCamera() { var c = Camera.main; gameCameraPosition = c.transform.position; gameCameraRotation = c.transform.rotation; gameCameraFov = c.fieldOfView; }
        static void RequireSameCamera()
        {
            var c = Camera.main; Require((c.transform.position-gameCameraPosition).magnitude<.001f && Quaternion.Angle(c.transform.rotation,gameCameraRotation)<.01f && Mathf.Abs(c.fieldOfView-gameCameraFov)<.001f, "Before/after gameplay cameras differ."); report.cameraMatched = true;
        }
        static bool Hit(Vector3 target)
        {
            var origin = Camera.main.transform.position; var delta = target-origin; float limit = delta.magnitude; var dir = delta / limit;
            foreach (var t in triangles)
            {
                var e1=t[1]-t[0]; var e2=t[2]-t[0]; var p=Vector3.Cross(dir,e2); float det=Vector3.Dot(e1,p);
                if(Mathf.Abs(det)<.000001f)continue; float inv=1/det; var v=origin-t[0]; float u=Vector3.Dot(v,p)*inv; if(u<0||u>1)continue;
                var q=Vector3.Cross(v,e1); float w=Vector3.Dot(dir,q)*inv; if(w<0||u+w>1)continue;
                float d=Vector3.Dot(e2,q)*inv; if(d>.001f&&d<limit-.01f)return true;
            }
            return false;
        }
        static void MeasureOcclusion()
        {
            foreach(var p in new[] { G.player.p }.Concat(actorPositions))
            {
                var world=MazeMap.ToWorld(p); var row=new ActorSample { role=p==G.player.p?"operative at unchanged spawn":"staged drone", world=world, headBlocked=Hit(world+Vector3.up*1.6f), chestBlocked=Hit(world+Vector3.up), feetBlocked=Hit(world+Vector3.up*.15f) };
                foreach(var offset in new[] { new Vector3(0,.15f,0), new Vector3(0,.65f,0), new Vector3(0,1,0), new Vector3(0,1.6f,0), new Vector3(-.25f,.9f,0), new Vector3(.25f,.9f,0) }) if(Hit(world+offset))row.blockedSamples++;
                row.viewport=Camera.main.WorldToViewportPoint(world+Vector3.up); report.actors.Add(row);
            }
            report.occlusionMeasured = true;
            if(report.actors.Any(a=>a.headBlocked||a.chestBlocked)) report.warnings.Add("The taller visual block occludes one or more sampled actor head/chest points from the unchanged gameplay camera. Review the screenshot before accepting.");
        }
        static void InspectCamera()
        {
            G.enabled=false; var b=BoundsOf(Block().gameObject); var c=Camera.main; c.fieldOfView=40;
            c.transform.position=b.center+new Vector3(-6,5,-8); c.transform.LookAt(b.center);
        }
        static void RestoreCamera() { var c=Camera.main; c.transform.SetPositionAndRotation(gameCameraPosition,gameCameraRotation); c.fieldOfView=gameCameraFov; G.enabled=true; }
        static void Shot(string name,bool inspection)
        {
            Require(G.state==Game.State.Paused&&G.choosing==Game.Choice.None,"Capture has a gameplay/modal state mismatch.");
            UiShot.Shot(name,new[]{Path.Combine(Root,Out)}); var path=Path.Combine(Root,Out,name+".png"); var bytes=File.ReadAllBytes(path);
            Require(bytes.Length>20000,"Empty capture."); report.captures.Add(new Capture { file=path, sha256=Hash(bytes), bytes=bytes.Length, inspection=inspection, preview=CryoMachineryPreview.Enabled, cameraPosition=Camera.main.transform.position, cameraRotation=Camera.main.transform.rotation, fov=Camera.main.fieldOfView }); Save();
        }
        static void Finish(bool passed,string error=null)
        {
            if(done)return;done=true;report??=new Report();if(error!=null)report.errors.Add(error);
            if(prepared)try
            {
                G.enabled=originalGameEnabled; G.cfg.camera.distance=originalDistance; G.holdFire=false; G.ToMenu();
                CryoMachineryPreview.Enabled=originalPreview; CryoKitEnvironment.On=originalKit; CryoFloor.On=originalFloor;
                Game.PendingSector=originalSector;Game.PendingTier=originalTier;Game.PendingPlan=originalPlan;
                Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalSave));Progress.Sim=originalSim;Prefs.HeroOverride=originalHero;
                SafeArea.Override=null;AudioListener.volume=originalVolume;AudioListener.pause=originalPause;Time.timeScale=originalTime;
                report.savedProgressAfter=Hash(PlayerPrefs.GetString("ms.progress",""));Require(report.savedProgressAfter==report.savedProgressBefore,"Saved progression changed.");
            }catch(Exception e){report.errors.Add("Cleanup: "+e);passed=false;}
            report.passed=passed&&report.errors.Count==0;report.completedUtc=DateTime.UtcNow.ToString("O");Save();
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Logged;
            Debug.Log("CryoMachineryProof: "+(report.passed?"PASS":"FAIL")+"; "+Path.Combine(Root,Out,"proof-report.json"));
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);else EditorApplication.ExitPlaymode();
        }
        static void Save(){Directory.CreateDirectory(Path.Combine(Root,Out));File.WriteAllText(Path.Combine(Root,Out,"proof-report.json"),JsonUtility.ToJson(report,true));}
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static string Hash(string text)=>Hash(Encoding.UTF8.GetBytes(text));
        static string Hash(byte[] bytes){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        [Serializable] class Report
        {
            public bool passed,geometryPassed,mapAndCollisionPassed,cameraMatched,occlusionMeasured,disableRestored;
            public string unityVersion,generatedUtc,completedUtc,savedProgressBefore,savedProgressAfter,beforeMap,afterMap;
            public Bounds bounds;public int meshes,materials,triangles,oldFootprintProps;
            public List<string> errors=new(),warnings=new(),editorDiagnostics=new();public List<Capture> captures=new();public List<ActorSample> actors=new();
            public string[] limits={"Actual Unity Editor Play-mode rendering; no phone install/performance claim.","Staged enemies are frozen on walkable cells for visual review.","Occlusion is a geometric ray/triangle sample of the machinery visual mesh, not a whole-frame visibility or transparency measurement.","Preview replaces only one existing 6 x 4 m footprint, with its own flag disabled by default. Other machinery blocks are unchanged.","Inspection camera is temporary; gameplay comparison retains 30 FOV / 57 pitch / -14 yaw / 40 m distance."};
        }
        [Serializable] class Capture {public string file,sha256;public int bytes;public bool inspection,preview;public Vector3 cameraPosition;public Quaternion cameraRotation;public float fov;}
        [Serializable] class ActorSample {public string role;public Vector3 world,viewport;public bool headBlocked,chestBlocked,feetBlocked;public int blockedSamples;}
    }
}
#endif
