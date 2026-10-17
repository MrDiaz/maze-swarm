#if UNITY_EDITOR
using System;
using System.Collections;
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
    /// <summary>
    /// Actual URP Play-mode proof, using the existing phone-ratio UiShot renderer.
    /// Unity -batchmode -projectPath PATH -executeMethod Maze.EditorTools.CryoKitProof.Run
    /// Do not pass -quit: this runner waits for rendered frames and exits itself.
    /// QuickBaseline captures only the current Cryo art with the kit disabled.
    /// No saved camera, scene, prefab, progression or PlayerPrefs writes are performed.
    /// </summary>
    [InitializeOnLoad]
    public static class CryoKitProof
    {
        const string Key = "Maze.CryoKitProof.Active", Out = "Logs/CryoKit";
        const int MaxTriangles = 280000, MaxInstances = 72;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Game.Goal[] Expected = { Game.Goal.Purge, Game.Goal.Hunt, Game.Goal.Hold, Game.Goal.Survive, Game.Goal.Boss };
        static ProofReport report;
        static List<Action> steps;
        static int step, readyFrame, chooseAttempts;
        static double started = -1, next;
        static bool prepared, finishing;
        static string originalProgress, originalHero;
        static bool originalSim, originalKit, originalDeck, originalGameEnabled;
        static int originalPendingTier;
        static SectorDef originalPendingSector;
        static Game.Goal[] originalPendingPlan;
        static float originalVolume, originalTimeScale;
        static bool originalAudioPause;
        static CameraRecord originalCamera;
        static Dictionary<int, MapRecord> before = new();
        static readonly HashSet<string> usedIds = new();
        static Game G => Object.FindAnyObjectByType<Game>();
        static string Project => Directory.GetParent(Application.dataPath).FullName;
        static string Full(string p) => Path.Combine(Project, p);

        static CryoKitProof()
        {
            if (SessionState.GetBool(Key, false)) Hook();
        }
        [MenuItem("Maze/Cryo Kit/Capture In-engine Proof")]
        public static void Run() => Begin(false);
        public static void QuickBaseline() => Begin(true);
        static void Begin(bool baseline)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start CryoKitProof outside Play mode in the isolated integration project.");
            report = null; steps = null; step = 0; started = -1; next = 0;
            prepared = finishing = false; chooseAttempts = 0; before.Clear(); usedIds.Clear();
            Directory.CreateDirectory(Full(Out));
            SessionState.SetBool(Key + ".baseline", baseline);
            SessionState.SetBool(Key, true);
            Hook();
            try
            {
                if (!baseline) CryoKitImport.Validate();
                EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { Fail(e); }
        }
        static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Logged; Application.logMessageReceived += Logged;
        }
        static void Logged(string message, string stack, LogType kind)
        {
            if (finishing || !SessionState.GetBool(Key, false)) return;
            report ??= NewReport();
            if (KnownEditorSearchStartup(message, stack, kind))
            {
                // Observed twice in 6000.6.2f1 before the first gameplay capture. This
                // engine Editor Search index startup failure contains no game frame.
                // Preserve its exact severity/message/stack; it is not a gameplay pass.
                report.editorDiagnostics.Add(new EditorDiagnostic
                {
                    category = "Unity Editor Search startup index exception", originalLogType = kind.ToString(),
                    message = message, stack = stack
                });
                report.warnings.Add("Retained Editor diagnostic: Unity 6000.6.2f1 SearchDatabase.EnumerateAll / SearchInit.IndexationOnStartup ArgumentOutOfRangeException. Editor Search is not validated by this proof.");
                return;
            }
            if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
                report.errors.Add(message + (string.IsNullOrEmpty(stack) ? "" : "\n" + stack));
            else if (kind == LogType.Warning) report.warnings.Add(message);
        }
        static bool KnownEditorSearchStartup(string message, string stack, LogType kind)
        {
            if (Application.unityVersion != "6000.6.2f1" || (kind != LogType.Exception && kind != LogType.Error)) return false;
            if (message == null || stack == null ||
                !message.StartsWith("ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.", StringComparison.Ordinal) ||
                !message.Contains("Parameter name: index")) return false;
            string[] lines = stack.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
            string[] allowed = {
                "System.Collections.Generic.List`1[T].get_Item (System.Int32 index)",
                "UnityEditor.Search.SearchDatabase+<EnumerateAll>d__78.MoveNext ()",
                "System.Linq.Enumerable.TryGetFirst[TSource] (",
                "System.Linq.Enumerable.First[TSource] (",
                "UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase ()",
                "UnityEditor.Search.SearchInit.IndexationOnStartup ()",
                "UnityEditor.EditorApplication.Internal_CallDelayFunctions ()",
                "UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal("
            };
            // Every stack frame must belong to this exact observed startup path.
            // Added runtime/script/asset-import frames or another Search exception fail normally.
            return lines.Length >= 7 && lines.All(line => allowed.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal))) &&
                lines.Any(line => line.StartsWith(allowed[0], StringComparison.Ordinal)) &&
                lines.Any(line => line.StartsWith(allowed[1], StringComparison.Ordinal)) &&
                lines.Any(line => line.StartsWith(allowed[4], StringComparison.Ordinal)) &&
                lines.Any(line => line.StartsWith(allowed[5], StringComparison.Ordinal)) &&
                lines.Any(line => line.StartsWith(allowed[6], StringComparison.Ordinal));
        }
        static ProofReport NewReport() => new ProofReport
        {
            generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
            graphicsDevice = SystemInfo.graphicsDeviceName,
            baselineOnly = SessionState.GetBool(Key + ".baseline", false),
            limits = new[] {
                "Editor Play-mode rendering and startup smoke tests; no phone performance measurement.",
                "Floor startup is exercised through Game.Restart and DebugFloor, not full mission completion.",
                "Enemies in the comparison frames are deliberately staged on walkable cells and frozen.",
                "Spawn presentation is settled to spawnT=1 before freezing so the existing enemy meshes are visible; no kills or objective progression are forced.",
                "Gameplay frames retain source camera FOV/pitch/yaw and use the default FAR distance (40 m) in memory only.",
                "Close-up is a documented temporary inspection view; it is not the gameplay camera.",
                "The exact observed Unity 6000.6.2f1 Editor Search startup index exception is retained separately as a warning with its original severity and full stack; Editor Search functionality remains unverified. All other errors still fail this proof.",
                "Cryo authored layouts have no D door cells: kit 06 is a sealed service facade; existing Escape Test doors remain authoritative."
            }
        };
        static void Tick()
        {
            if (finishing) return;
            if (started < 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 420) { Fail(new TimeoutException("Cryo proof exceeded 420 seconds.")); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G;
            if (g == null || g.player == null || g.menu == null || g.hud == null || Camera.main == null) return;
            try
            {
                if (!prepared) { Prepare(g); return; }
                if (report.errors.Count > 0) throw new InvalidOperationException("A Unity error was logged during the proof; see errors in proof-report.json.");
                // Resolve actual startup card choices before pausing, then wait for a fresh frame.
                if (g.state == Game.State.Choosing)
                {
                    Require(++chooseAttempts <= 30 && g.offer.Count > 0, "Unresolvable startup card choice.");
                    g.Pick(0); Freeze(g); Wait(0.3); return;
                }
                Require(g.choosing == Game.Choice.None, "Choice state and Game.state disagree.");
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < readyFrame) return;
                if (step >= steps.Count) { Finish(true); return; }
                steps[step++]();
            }
            catch (Exception e) { Fail(e); }
        }
        static void Prepare(Game g)
        {
            report ??= NewReport();
            originalProgress = JsonUtility.ToJson(Progress.D); originalSim = Progress.Sim;
            originalHero = Prefs.HeroOverride; originalKit = KitOn; originalDeck = CryoFloor.On;
            originalVolume = AudioListener.volume; originalAudioPause = AudioListener.pause;
            originalTimeScale = Time.timeScale; originalGameEnabled = g.enabled;
            originalCamera = CameraRecord.Of(g, Camera.main);
            originalPendingTier = Game.PendingTier; originalPendingSector = Game.PendingSector; originalPendingPlan = Game.PendingPlan;
            report.initialCamera = originalCamera;
            report.savedProgressShaBefore = Hash(PlayerPrefs.GetString("ms.progress", ""));
            prepared = true; // cleanup is required even if native validation or capture preparation throws
            Require(Mathf.Abs(g.cfg.camera.fov - 30) < .001f && Mathf.Abs(g.cfg.camera.pitch - 57) < .001f && Mathf.Abs(g.cfg.camera.yaw + 14) < .001f,
                "Gameplay camera source tuning differs from the inspected baseline (30 FOV / 57 pitch / -14 yaw).");
            // FAR is the approved production camera. Never call Prefs setters (they save PlayerPrefs).
            g.cfg.camera.distance = 40;
            Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 });
            UiShot.Prepare();
            if (!report.baselineOnly) RecordNativeAssets();
            steps = BuildSteps(); Wait(0.3);
        }
        static List<Action> BuildSteps()
        {
            var result = new List<Action>
            {
                () => { StartCryo(false); Wait(2.8); },
                () => { before[1] = InspectFloor(1, false); Shot("01_before_start", false); Ring(); Wait(.5); },
                () => { Shot("02_before_enemies", false); }
            };
            if (report.baselineOnly) return result;
            result.Add(() => { StartCryo(true); Wait(2.8); });
            result.Add(() => { var after = InspectFloor(1, true); Compare(before[1], after); Shot("03_after_start", false); Ring(); Wait(.5); });
            result.Add(() => { Shot("04_after_enemies", false); Wait(.3); });
            result.Add(BeginCloseup);
            result.Add(() => { Shot("05_after_pod_inspection", true); RestoreCamera(); Wait(.3); });
            for (int f = 2; f <= Game.Floors; f++)
            {
                int floor = f;
                result.Add(() => { SetFloor(floor, false); Wait(2.8); });
                result.Add(() => { before[floor] = InspectFloor(floor, false); SetFloor(floor, true); Wait(2.8); });
                result.Add(() => { var after = InspectFloor(floor, true); Compare(before[floor], after); Shot($"06_after_floor{floor}_{G.shownGoal.ToString().ToLowerInvariant()}", false); Wait(.3); });
            }
            result.Add(() => { StartEscape(false); Wait(1); });
            result.Add(() => { report.escape.beforeHash = MapSnapshot().sha256; StartEscape(true); Wait(1); });
            result.Add(CheckEscapeDoors);
            result.Add(() => { StartSector("evac-deck", true); Wait(2.8); });
            result.Add(() =>
            {
                Require(SceneInfo() == null, "Cryo art unexpectedly instantiated in evac-deck.");
                Require(G.run && G.sector.id == "evac-deck", "Non-Cryo run did not start.");
                report.nonCryoSector = G.sector.id; report.nonCryoStartupPassed = true;
                Require(usedIds.Count == 20, $"Expected all twenty kit IDs across authored Cryo floors; observed {usedIds.Count}.");
                report.usedIds = usedIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            });
            return result;
        }
        static void Wait(double seconds)
        {
            next = EditorApplication.timeSinceStartup + seconds; readyFrame = Time.frameCount + 3;
        }
        static void Freeze(Game g)
        {
            g.demo = false; g.godMode = true; g.holdFire = true;
            if (g.state != Game.State.Choosing) g.state = Game.State.Paused; // no pause modal / HUD mode change
            g.player.vel = Vector2.zero;
            foreach (var enemy in g.swarm.enemies) if (!enemy.dead) enemy.spawnT = 1;
            if (g.state == Game.State.Paused) { Time.timeScale = 1; g.Tick(0); }
        }
        static void StartCryo(bool kit) => StartSector("cryo-station", kit);
        static void StartSector(string id, bool kit)
        {
            KitOn = kit; CryoFloor.On = true;
            var def = SectorDefs.Get(id); Require(def != null, "Missing sector " + id);
            var g = G; g.holdFire = true; g.demo = false; g.godMode = true;
            Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 });
            UnityEngine.Random.InitState(7);
            g.LoadMap(0);
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.menu.Hide(); g.Restart();
            if (g.floor != 1) g.DebugFloor(1); // current test-floor preference is left untouched
            Progress.Sim = true;
            // StartFloor installs the script; its first poll normally occurs on the first playing tick.
            // Run that production event API at t=0 before freezing, so vent startup settings are exercised.
            g.events.Tick(0);
            Freeze(g); chooseAttempts = 0;
            Require(g.run && g.sector?.id == id, "Sector startup failed: " + id);
            if (id == "cryo-station") Require(g.plan.SequenceEqual(Expected), "Cryo authored goal plan changed.");
        }
        static void SetFloor(int floor, bool kit)
        {
            KitOn = kit; UnityEngine.Random.InitState(7 + floor);
            G.DebugFloor(floor); G.events.Tick(0); Freeze(G); chooseAttempts = 0;
        }
        static void Ring()
        {
            var g = G; g.swarm.Clear(); UnityEngine.Random.InitState(11);
            string[] roster = { "drone", "drone", "leaper", "drone", "bloater", "drone", "leaper", "drone" };
            int count = 0;
            for (int k = 0; k < roster.Length; k++)
            {
                float a = k * Mathf.PI * 2 / roster.Length;
                var at = g.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (5 + k % 3);
                int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C);
                if (!MazeMap.Walkable(x, z)) continue;
                int type = g.swarm.TypeByName(roster[k]); Require(type >= 0, "Missing staged enemy " + roster[k]);
                var e = g.swarm.Spawn(at, 2, type); e.p = at; count++;
            }
            Require(count >= 3, "Too few staged enemies on walkable cells for comparison."); Freeze(g);
        }
        static void Shot(string name, bool inspection)
        {
            Require(G.state == Game.State.Paused && G.choosing == Game.Choice.None, "Refusing a modal/unfrozen capture: " + name);
            Require(G.kills == 0, "A comparison run killed enemies before capture.");
            var cam = Camera.main;
            Require(cam.targetTexture != null && cam.targetTexture.IsCreated(), "Phone-ratio renderer has no created target.");
            UiShot.Shot(name, new[] { Full(Out) });
            string file = Full(Out + "/" + name + ".png");
            Require(File.Exists(file) && new FileInfo(file).Length > 20000, "Capture is absent or unexpectedly empty: " + name);
            var frame = new CaptureRecord
            {
                file = Out + "/" + name + ".png", bytes = new FileInfo(file).Length, sha256 = FileHash(file),
                width = cam.targetTexture.width, height = cam.targetTexture.height, frame = Time.frameCount,
                sector = G.sector?.id, floor = G.floor, goal = G.shownGoal.ToString(), kitOn = KitOn,
                inspectionOnly = inspection, state = G.state.ToString(), kills = G.kills,
                liveEnemies = G.swarm.enemies.Count(e => !e.dead), camera = CameraRecord.Of(G, cam)
            };
            if (!inspection && G.floor == 1)
            {
                var reference = report.captures.FirstOrDefault(c => !c.inspectionOnly && c.floor == 1);
                if (reference != null)
                    Require((frame.camera.position - reference.camera.position).magnitude < .001f &&
                        Quaternion.Angle(frame.camera.rotation, reference.camera.rotation) < .01f &&
                        Mathf.Abs(frame.camera.fov - reference.camera.fov) < .001f,
                        "Before/after gameplay comparison cameras do not match.");
            }
            var pixels = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Require(pixels.LoadImage(File.ReadAllBytes(file)), "Captured PNG could not be decoded.");
                var colors = pixels.GetPixels32(); var distinct = new HashSet<uint>();
                for (int i = 0; i < colors.Length; i += 127)
                {
                    var c = colors[i]; distinct.Add((uint)(c.r << 16 | c.g << 8 | c.b));
                }
                frame.sampledDistinctColors = distinct.Count;
                Require(distinct.Count > 64, "Capture appears flat/blank.");
            }
            finally { Object.DestroyImmediate(pixels); }
            report.captures.Add(frame); SaveReport();
            Debug.Log("CryoKitProof capture ready: " + file);
        }
        static void BeginCloseup()
        {
            var info = SceneInfo(); Require(info != null, "Missing Cryo kit scene metadata for close-up.");
            var choices = ((IEnumerable)Field(info, "placements")).Cast<object>()
                .Where(p => Convert.ToString(Field(p, "id")) == "07")
                .Select(p => (GameObject)Field(p, "instance")).Where(o => o != null).ToArray();
            Require(choices.Length > 0, "No approved pod instance available for close-up.");
            var heroWorld = MazeMap.ToWorld(G.player.p);
            var pod = choices.OrderBy(o => (o.transform.position - heroWorld).sqrMagnitude).First();
            var bounds = RendererBounds(pod);
            G.enabled = false;
            var cam = Camera.main; cam.fieldOfView = 40;
            var centre = bounds.center;
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            // Inspect the asset's authored +Z front after its actual placement yaw.
            // The room pod is currently yaw 180, so this approaches world -Z.
            cam.transform.position = centre + pod.transform.TransformDirection(new Vector3(.9f, .58f, 1.2f)).normalized * (size * 1.8f);
            cam.transform.LookAt(centre);
            report.closeupPodName = pod.name; Wait(.5);
        }
        static void RestoreCamera()
        {
            G.enabled = originalGameEnabled;
            G.cfg.camera.distance = 40; G.Tick(0);
            Require(Mathf.Abs(Camera.main.fieldOfView - 30) < .01f, "Gameplay camera was not restored after inspection.");
        }
        static MapRecord InspectFloor(int floor, bool kit)
        {
            var g = G;
            Require(g.run && g.sector?.id == "cryo-station" && g.floor == floor, "Incorrect Cryo floor startup.");
            Require(g.shownGoal == Expected[floor - 1], "Authored objective changed on floor " + floor);
            Require(MazeMap.V2 && MazeMap.LayoutName(MazeMap.MapIndex) == g.zoneFloor.layout, "Incorrect authored Cryo layout.");
            Require(MazeMap.doors.Count == 0, "Cryo authored layout now has doors; sealed-facade assumption no longer valid.");
            var snapshot = MapSnapshot();
            var row = new FloorRecord
            {
                floor = floor, kitOn = kit, goal = g.shownGoal.ToString(), layout = MazeMap.LayoutName(MazeMap.MapIndex),
                goalLine = g.GoalLine, map = snapshot, liveHazards = Hazards.Live.Select(h => h.GetType().Name).ToArray(),
                nestEnemies = g.swarm.enemies.Count(e => !e.dead && e.type.nest && !e.egg), bossSpawned = g.Boss != null,
                eventScriptSha = Hash(g.zoneFloor.script ?? ""), deckOn = CryoFloor.On
            };
            Require(!string.IsNullOrWhiteSpace(row.goalLine), "Objective text failed to initialize.");
            if (floor == 2) Require(row.nestEnemies == 3, "HUNT did not initialize its three authored nests.");
            if (floor == 5) Require(row.bossSpawned, "BOSS did not initialize the authored boss.");
            var vents = Hazards.Live.OfType<CellVents>().SingleOrDefault();
            Require(vents != null && vents.Skin == "cryo", "Cryo vent hazard failed to initialize.");
            Require(vents.Enabled == (floor != 1), "Floor startup script did not preserve the authored vent on/off state.");
            row.ventsEnabled = vents.Enabled; row.ventSkin = vents.Skin;
            if (kit) row.kit = InspectPlacement(); else Require(SceneInfo() == null, "Kit instances remain with kit disabled.");
            report.floors.Add(row); return snapshot;
        }
        static KitRecord InspectPlacement()
        {
            var info = SceneInfo(); Require(info != null, "Cryo art scene metadata missing.");
            var owner = (Component)info;
            var native = new KitRecord { nativeTriangles = Convert.ToInt32(Field(info, "nativeTriangles")), nativeInstances = Convert.ToInt32(Field(info, "nativeInstances")) };
            Require(native.nativeTriangles > 0 && native.nativeTriangles <= MaxTriangles, "Cryo native triangle budget exceeded.");
            Require(native.nativeInstances > 0 && native.nativeInstances <= MaxInstances, "Cryo native instance budget exceeded.");
            native.colliders = owner.GetComponentsInChildren<Collider>(true).Length;
            Require(native.colliders == 0, "New art hierarchy contains a Collider.");
            int actualTriangles = 0;
            foreach (var p in (IEnumerable)Field(info, "placements"))
            {
                string id = Convert.ToString(Field(p, "id")); var instance = (GameObject)Field(p, "instance");
                Require(instance != null, "Missing placed native asset " + id);
                var allowed = (Rect)Field(p, "allowedXZ"); var bounds = RendererBounds(instance);
                const float tolerance = .025f;
                Require(bounds.min.x >= allowed.xMin - tolerance && bounds.max.x <= allowed.xMax + tolerance &&
                    bounds.min.z >= allowed.yMin - tolerance && bounds.max.z <= allowed.yMax + tolerance,
                    "Native art exceeds its allowed XZ envelope: " + id + " " + instance.name);
                if (!(bool)Field(p, "mounted"))
                {
                    int x0 = Mathf.FloorToInt((bounds.min.x + tolerance) / MazeMap.C);
                    int x1 = Mathf.FloorToInt((bounds.max.x - tolerance) / MazeMap.C);
                    int z0 = Mathf.FloorToInt((-bounds.max.z + tolerance) / MazeMap.C);
                    int z1 = Mathf.FloorToInt((-bounds.min.z - tolerance) / MazeMap.C);
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                        Require(!MazeMap.Walkable(x, z), "Ground art projects into an authoritative walkable cell: " + id + $" [{x},{z}]");
                    native.checkedGroundPlacements++;
                }
                foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++) actualTriangles += (int)mf.sharedMesh.GetIndexCount(sub) / 3;
                native.ids.Add(id); usedIds.Add(id); native.checkedPlacements++;
            }
            Require(native.checkedPlacements == native.nativeInstances, "Scene metadata instance count mismatch.");
            Require(actualTriangles == native.nativeTriangles, "Actual native mesh triangle sum disagrees with scene budget metadata.");
            return native;
        }
        static object SceneInfo()
        {
            var type = typeof(Game).Assembly.GetType("Maze.CryoKitSceneInfo");
            Require(type != null || report.baselineOnly, "Runtime CryoKitSceneInfo type missing.");
            if (type == null) return null;
            var all = Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Require(all.Length <= 1, "More than one current Cryo kit scene root remains.");
            return all.FirstOrDefault();
        }
        static MapRecord MapSnapshot()
        {
            var sb = new StringBuilder();
            sb.Append(MazeMap.MapIndex).Append('|').Append(MazeMap.W).Append('x').Append(MazeMap.H).Append('|');
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) sb.Append(MazeMap.grid[z, x]);
            sb.Append('|'); for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) sb.Append(MazeMap.tag[z, x]);
            void Points(string n, IEnumerable<Vector2> values) { sb.Append('|').Append(n); foreach (var p in values) sb.Append(':').Append(p.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(p.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
            Points("player", new[] { MazeMap.playerSpawn }); Points("spawn", MazeMap.spawnPoints); Points("barrel", MazeMap.barrelSpots);
            Points("zone", MazeMap.zoneSpots); Points("nest", MazeMap.nestSpots); Points("exit", MazeMap.exitCells);
            foreach (var kv in MazeMap.marks.OrderBy(k => k.Key)) { sb.Append('|').Append(kv.Key); foreach (var p in kv.Value) sb.Append(':').Append(p.x).Append(',').Append(p.y); }
            foreach (var d in MazeMap.doors) sb.Append('|').Append(d.r.ToString()).Append(':').Append(d.from).Append('>').Append(d.to).Append(':').Append(d.open);
            var seen = Flood(false); int walkable = 0;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) walkable++;
            var record = new MapRecord { sha256 = Hash(sb.ToString()), mapIndex = MazeMap.MapIndex, width = MazeMap.W, height = MazeMap.H,
                walkableCells = walkable, reachableCells = seen.Count, spawnCount = MazeMap.spawnPoints.Count, markerCount = MazeMap.marks.Sum(k => k.Value.Count), doors = MazeMap.doors.Count };
            if (MazeMap.V2 && MazeMap.doors.Count == 0) Require(seen.Count == walkable, "Authored Cryo walkable cells are disconnected.");
            var openDoorsReachable = Flood(true);
            foreach (var p in MazeMap.spawnPoints) Require(openDoorsReachable.Contains(MazeMap.CellIndex(p)), "A registered spawn is unreachable even with doors open.");
            return record;
        }
        static HashSet<int> Flood(bool doorsOpen)
        {
            var seen = new HashSet<int>(); var pending = new Queue<int>(); int start = MazeMap.CellIndex(MazeMap.playerSpawn);
            if (start < 0) return seen; seen.Add(start); pending.Enqueue(start);
            while (pending.Count > 0)
            {
                int i = pending.Dequeue(), x = i % MazeMap.W, z = i / MazeMap.W;
                foreach (var delta in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    int nx = x + delta.x, nz = z + delta.y;
                    if (nx < 0 || nx >= MazeMap.W || nz < 0 || nz >= MazeMap.H) continue;
                    char c = MazeMap.grid[nz, nx]; if (MazeMap.IsSolidMove(c) && !(doorsOpen && c == 'D')) continue;
                    int ni = nz * MazeMap.W + nx; if (seen.Add(ni)) pending.Enqueue(ni);
                }
            }
            return seen;
        }
        static void Compare(MapRecord baseline, MapRecord after)
        {
            Require(baseline.sha256 == after.sha256, "Kit changed the authoritative map grid, tags, spawns, markers or doors.");
            Require(baseline.walkableCells == after.walkableCells && baseline.reachableCells == after.reachableCells, "Kit changed traversable connectivity.");
            report.comparedFloors++;
        }
        static void StartEscape(bool kit)
        {
            KitOn = kit; Game.PendingTier = 0; Game.PendingSector = null; Game.PendingPlan = null;
            var g = G; g.LoadMap(1); g.menu.Hide(); g.Restart(); Freeze(g);
            Require(MazeMap.Escape && !g.run, "Escape Test startup failed.");
        }
        static void CheckEscapeDoors()
        {
            var g = G; Require(SceneInfo() == null, "Cryo native kit leaked into Escape Test.");
            report.escape.afterHash = MapSnapshot().sha256;
            Require(report.escape.beforeHash == report.escape.afterHash, "Escape grid/doors changed when Cryo kit enabled.");
            Require(MazeMap.doors.Count > 0, "Escape Test has no functioning door cells.");
            foreach (var d in MazeMap.doors.ToArray())
            {
                int index = MazeMap.doors.IndexOf(d);
                g.escape.CloseAllDoors(); g.escape.TickDoors(.001f);
                int x = d.r.x + d.r.width / 2, z = d.r.y + d.r.height / 2;
                Require(!MazeMap.Walkable(x, z), "Closed door collision failed.");
                bool alongX = MazeMap.DoorAlongX(d);
                Vector2 normal = alongX ? Vector2.up : Vector2.right;
                float thickness = (alongX ? d.r.height : d.r.width) * MazeMap.C;
                var a = d.center - normal * (thickness / 2 + .7f); var b = d.center + normal * (thickness / 2 + .7f);
                float opening = (alongX ? d.r.width : d.r.height) * MazeMap.C;
                Require(opening >= 2 * g.cfg.player.radius + .1f, "Door opening is smaller than operative collision diameter.");
                var closed = Traverse(a, b, g.cfg.player.radius);
                Require(Vector2.Dot(closed - d.center, normal) < 0, "Closed door allowed traversal across the seam.");
                var positionsBefore = DoorPanelPositions(d);
                g.escape.UnlockDoor(index); for (int k = 0; k < 120; k++) g.escape.TickDoors(1f / 30);
                Require(d.open && MazeMap.Walkable(x, z), "Existing door unlock did not open collision.");
                var open = Traverse(a, b, g.cfg.player.radius);
                Require((open - b).magnitude < .05f, "Open door blocked operative-radius traversal.");
                var positionsAfter = DoorPanelPositions(d);
                Require(positionsBefore.Length >= 2 && positionsBefore.Length == positionsAfter.Length && positionsBefore.Where((v, k) => (v - positionsAfter[k]).magnitude > .1f).Count() >= 2,
                    "Existing sliding panels did not visibly move on unlock.");
                report.escape.checkedDoors++; report.escape.openingMetres.Add(opening);
            }
            g.escape.OpenAllDoors();
            var reachable = Flood(false);
            foreach (var p in MazeMap.exitCells) Require(reachable.Contains(MazeMap.CellIndex(p)), "Extraction route disconnected after doors opened.");
            report.escape.allExitCellsReachable = true; report.escape.passed = true; Wait(.3);
        }
        static Vector2 Traverse(Vector2 from, Vector2 to, float radius)
        {
            Vector2 p = from, direction = (to - from).normalized;
            int n = Mathf.CeilToInt((to - from).magnitude / .05f);
            for (int k = 0; k < n; k++) { var attempt = p + direction * Mathf.Min(.05f, (to - p).magnitude); MazeMap.CollideGrid(ref attempt, radius); p = attempt; }
            return p;
        }
        static Vector3[] DoorPanelPositions(MazeMap.Door d)
        {
            var views = (IEnumerable)Field(G.escape, "views");
            var view = views.Cast<object>().Single(v => ReferenceEquals(Field(v, "d"), d));
            return new[] { ((Transform)Field(view, "panelL")).localPosition, ((Transform)Field(view, "panelR")).localPosition };
        }
        static void RecordNativeAssets()
        {
            CryoKitImport.Validate();
            string[] files = Directory.GetFiles(Full(CryoKitImport.Prefabs), "*.prefab").OrderBy(x => x).ToArray();
            Require(files.Length == 20, "Native prefab count is not twenty.");
            foreach (var file in files)
            {
                string path = file.Substring(Project.Length + 1).Replace('\\', '/');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Require(prefab != null, "Unreadable native prefab " + path);
                int triangles = 0, meshes = 0;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh; Require(mesh != null, "Missing mesh in " + path);
                    Require(mesh.uv.Length == mesh.vertexCount && mesh.uv2.Length == mesh.vertexCount && mesh.normals.Length == mesh.vertexCount && mesh.tangents.Length == mesh.vertexCount,
                        "Missing native UV/normal/tangent stream in " + path);
                    for (int k = 0; k < mesh.subMeshCount; k++) triangles += (int)mesh.GetIndexCount(k) / 3;
                    meshes++;
                }
                var materials = prefab.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
                foreach (var mat in materials) Require(mat != null && !ShaderUtil.ShaderHasError(mat.shader), "Missing or failed native shader in " + path);
                Require(prefab.GetComponentsInChildren<Collider>(true).Length == 0, "Prefab contains new physics collision.");
                report.nativeAssets.Add(new NativeRecord { path = path, triangles = triangles, meshes = meshes, materials = materials.Length, bounds = RendererBounds(prefab) });
            }
            foreach (var shader in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Maze/CryoKit/Shaders" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid))).Where(s => s != null))
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                {
                    string detail = shader.name + ": " + message.severity + ": " + message.message;
                    if (message.severity.ToString() == "Error") throw new InvalidOperationException(detail);
                    if (message.severity.ToString() == "Warning" && !report.warnings.Contains(detail)) report.warnings.Add(detail);
                }
            report.nativeImportPassed = true;
        }
        static Bounds RendererBounds(GameObject instance)
        {
            var rs = instance.GetComponentsInChildren<Renderer>(true); Require(rs.Length > 0, "No renderer in " + instance.name);
            var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
        }
        static Type KitType => typeof(Game).Assembly.GetType("Maze.CryoKitEnvironment");
        static bool KitOn
        {
            get { var t = KitType; if (t == null && SessionState.GetBool(Key + ".baseline", false)) return false; Require(t != null, "CryoKitEnvironment runtime type is missing."); return Convert.ToBoolean(t.GetField("On", Flags)?.GetValue(null) ?? t.GetProperty("On", Flags)?.GetValue(null)); }
            set { var t = KitType; if (t == null && SessionState.GetBool(Key + ".baseline", false)) return; Require(t != null, "CryoKitEnvironment runtime type is missing."); var f = t.GetField("On", Flags); if (f != null) f.SetValue(null, value); else { var p = t.GetProperty("On", Flags); Require(p != null && p.CanWrite, "Kit On flag is not writable."); p.SetValue(null, value); } }
        }
        static object Field(object target, string name)
        {
            var field = target.GetType().GetField(name, Flags); Require(field != null, "Missing proof metadata/API field " + name); return field.GetValue(target);
        }
        static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        static string FileHash(string p) { using var sha = SHA256.Create(); using var stream = File.OpenRead(p); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string Hash(string text) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        static void SaveReport() { Directory.CreateDirectory(Full(Out)); File.WriteAllText(Full(Out + "/proof-report.json"), JsonUtility.ToJson(report ??= NewReport(), true)); }
        static void Fail(Exception e)
        {
            report ??= NewReport(); report.errors.Add(e.ToString()); Finish(false);
        }
        static void Finish(bool passed)
        {
            if (finishing) return; finishing = true;
            try
            {
                if (prepared)
                {
                    var g = G;
                    if (g != null) { g.enabled = originalGameEnabled; g.cfg.camera.distance = originalCamera.distance; g.holdFire = false; g.ToMenu(); }
                    KitOn = originalKit; CryoFloor.On = originalDeck;
                    Game.PendingTier = originalPendingTier; Game.PendingSector = originalPendingSector; Game.PendingPlan = originalPendingPlan;
                    Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalProgress)); Progress.Sim = originalSim;
                    Prefs.HeroOverride = originalHero; SafeArea.Override = null; Time.timeScale = originalTimeScale;
                    AudioListener.pause = originalAudioPause; AudioListener.volume = originalVolume;
                    report.savedProgressShaAfter = Hash(PlayerPrefs.GetString("ms.progress", ""));
                    Require(report.savedProgressShaBefore == report.savedProgressShaAfter, "Saved progression key changed during proof.");
                    report.savedProgressUnchanged = true;
                }
            }
            catch (Exception e) { passed = false; report.errors.Add("Cleanup: " + e); }
            report ??= NewReport(); report.passed = passed && report.errors.Count == 0; report.completedUtc = DateTime.UtcNow.ToString("O");
            SaveReport(); SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick; Application.logMessageReceived -= Logged;
            Debug.Log("CryoKitProof: " + (report.passed ? "PASS" : "FAIL") + "; report " + Full(Out + "/proof-report.json"));
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1); else EditorApplication.ExitPlaymode();
        }

        [Serializable] public class ProofReport
        {
            public string generatedUtc, completedUtc, unityVersion, graphicsDevice, closeupPodName, nonCryoSector, savedProgressShaBefore, savedProgressShaAfter;
            public bool passed, baselineOnly, nativeImportPassed, nonCryoStartupPassed, savedProgressUnchanged;
            public int comparedFloors; public string[] limits; public CameraRecord initialCamera;
            public List<string> errors = new(), warnings = new(), usedIds = new();
            public List<EditorDiagnostic> editorDiagnostics = new();
            public List<CaptureRecord> captures = new(); public List<FloorRecord> floors = new(); public List<NativeRecord> nativeAssets = new();
            public EscapeRecord escape = new();
        }
        [Serializable] public class EditorDiagnostic
        {
            public string category, originalLogType, message, stack;
        }
        [Serializable] public class CameraRecord
        {
            public float fov, distance, pitch, yaw; public Vector3 position; public Quaternion rotation;
            public static CameraRecord Of(Game g, Camera cam) => new() { fov = cam.fieldOfView, distance = g.cfg.camera.distance, pitch = g.cfg.camera.pitch, yaw = g.cfg.camera.yaw, position = cam.transform.position, rotation = cam.transform.rotation };
        }
        [Serializable] public class CaptureRecord
        {
            public string file, sha256, sector, goal, state; public long bytes; public int width, height, frame, floor, kills, liveEnemies, sampledDistinctColors;
            public bool kitOn, inspectionOnly; public CameraRecord camera;
        }
        [Serializable] public class FloorRecord
        {
            public int floor, nestEnemies; public bool kitOn, bossSpawned, deckOn, ventsEnabled;
            public string goal, layout, goalLine, eventScriptSha, ventSkin; public string[] liveHazards; public MapRecord map; public KitRecord kit;
        }
        [Serializable] public class MapRecord
        {
            public string sha256; public int mapIndex, width, height, walkableCells, reachableCells, spawnCount, markerCount, doors;
        }
        [Serializable] public class KitRecord
        {
            public int nativeTriangles, nativeInstances, checkedPlacements, checkedGroundPlacements, colliders; public List<string> ids = new();
        }
        [Serializable] public class NativeRecord
        {
            public string path; public int triangles, meshes, materials; public Bounds bounds;
        }
        [Serializable] public class EscapeRecord
        {
            public string beforeHash, afterHash; public bool passed, allExitCellsReachable; public int checkedDoors; public List<float> openingMetres = new();
        }
    }
}
#endif
