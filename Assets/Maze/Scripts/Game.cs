using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Maze
{
    // The whole game, built at runtime: lights, camera, level, swarm, barrels, HUD and the
    // game loop (a port of the three.js build). Drop it on an empty GameObject, or just press
    // Play: it bootstraps itself if the open scene has none. Tune everything in `cfg`.
    public partial class Game : MonoBehaviour
    {
        public MazeConfig cfg = new MazeConfig();
        [Tooltip("Floor texture resolution (pixels per meter)")] public int floorPixelsPerMeter = 10;

        public enum State { Menu, Playing, Paused, Dead, Won, Choosing } // Won: escaped / run won; Choosing: a run's card or cache pick
        [System.NonSerialized] public State state = State.Menu;
        [System.NonSerialized] public float flowT;
        [System.NonSerialized] public Player player;
        [System.NonSerialized] public Swarm swarm;
        [System.NonSerialized] public Barrels barrels;
        [System.NonSerialized] public Hud hud;
        [System.NonSerialized] public MainMenu menu;
        [System.NonSerialized] public int wave, score, kills;
        [System.NonSerialized] public bool demo;

        int toSpawn;
        int bloatersLeft, bloaterStride, nextBloaterAt; // Bloaters spread evenly through a wave's spawns
        int leapersLeft, leaperStride, nextLeaperAt;    // Leapers too (from wave 4)
        float spawnT, breakT, shake, time;

        Camera cam;
        Light sun;
        Vector2 camTarget;
        static readonly Vector3 SunOffset = new Vector3(20, 34, 18);
        static Color Bg => CryoLook ? new Color32(0xd3, 0xdf, 0xea, 255) : new Color32(0xe3, 0xe6, 0xeb, 255); // icy haze / warehouse grey

        class Bullet { public Vector2 p, d, from; public float speed, life, len, width, dmg; public int blast, pierce, bounce, fang; public Enemy last; public Weapon w; public Transform tr; }
        readonly List<Bullet> bullets = new();
        const int MaxBullets = 400;
        Bullet hitBullet; bool hitDone;
        System.Func<Enemy, bool> bulletHitFn;

        class Pickup { public Vector2 p; public float t; public Transform tr; }
        readonly List<Pickup> pickups = new();
        Material pickupMat, blueMat;
        Mesh cubeMesh;

        Vector2 moveVel; // velocity actually applied this frame (drives the run animation)
        Vector2 botMove; bool botFire, botDash;
        Vector2 faceDir = Vector2.up; Enemy lockOn;   // the hero's facing (from movement / aim; kept while standing) and the held auto-fire target

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<Sandbox>() != null) return; // the model test arena runs without the game
            if (FindAnyObjectByType<Game>() == null) new GameObject("Game").AddComponent<Game>();
        }

        // (2026-10-15, Manuel: "remove reloading for now") false = every gun is bottomless: it never empties, never reloads
        // (no reload pause, sound or animation). The magazine / reload code stays as it was behind this switch.
        public static bool ReloadEnabled = false;
        static readonly System.Collections.Generic.Dictionary<string, int> magBackup = new();
        public void ApplyReloadSwitch()
        {
            if (ReloadEnabled) { foreach (var w in cfg.weapons) if (w.magSize == 0 && magBackup.TryGetValue(w.name, out int m)) w.magSize = m; return; }
            foreach (var w in cfg.weapons) { if (w.magSize > 0) magBackup[w.name] = w.magSize; w.magSize = 0; }
            if (player != null) for (int i = 0; i < player.reloadLeft.Length; i++) player.reloadLeft[i] = 0;
        }
        public void SetReload(bool on) { ReloadEnabled = on; ApplyReloadSwitch(); if (on && player != null) for (int i = 0; i < player.ammo.Length && i < cfg.weapons.Length; i++) player.ammo[i] = cfg.weapons[i].magSize; }   // (captures / checks)

        void Awake()
        {
            BlueprintWeapons.Ensure(cfg); ApplyReloadSwitch();
            Prefs.ApplyFrameRate(); // 120 on ProMotion screens unless turned off in Settings, else 60
            var heroEnv = System.Environment.GetEnvironmentVariable("MAZE_HERO");
            if (!string.IsNullOrEmpty(heroEnv)) Prefs.HeroOverride = heroEnv;
            Debug.Log($"Maze: display {Prefs.DisplayHz} Hz, target {Application.targetFrameRate} fps");
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToPortrait = Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
        }

        void Start()
        {
            MazeMap.Init();
            SetupLightsAndCamera();
            BuildLevel();
            escape = gameObject.AddComponent<EscapeRun>();

            Fx.I = null;
            Child<Fx>("Fx").Init(cfg.weapons);
            player = Child<Player>("Player"); player.Init(this);
            swarm = Child<Swarm>("Swarm"); swarm.Init(this);
            barrels = Child<Barrels>("Barrels"); barrels.Init(this);
            Child<CombatAudio>("CombatAudio").Init();
            escape.Build(this);
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
            hud = Child<Hud>("Hud"); hud.Init(this);
            runUi = Child<RunUi>("RunUi"); runUi.Init(this);

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            pickupMat = Mats.Lit(new Color(0.92f, 0.97f, 1f), null, null, 0.5f);
            blueMat = Mats.Lit(new Color32(0x2f, 0x55, 0xd4, 255), null, null, 0.4f, 0.15f);
            bulletHitFn = BulletHit;

            ApplyQuality();
            Prefs.ApplySound();
            cfg.camera.distance = ReadabilityFx.CamDistance;
            ResetGame();
            menu = Child<MainMenu>("Menu"); menu.Init(this);
            EnterMenu();
            if (PerfRequested()) StartPerf();
            // device check: env MAZE_MAPSELECT=1 opens sector select straight away
            if (System.Environment.GetEnvironmentVariable("MAZE_MAPSELECT") == "1") MapSelect.I?.Show();
            // device check: launched with env MAZE_SANDBOX=1, go straight to the model test arena
            if (System.Environment.GetEnvironmentVariable("MAZE_SANDBOX") == "1" && Application.CanStreamedLevelBeLoaded("Sandbox"))
                UnityEngine.SceneManagement.SceneManager.LoadScene("Sandbox");
        }

        // ------------------------------------------------------------ device perf run
        // Launched with -mazePerf (or env MAZE_PERF=1): the demo bot plays with the SMG only (or env
        // MAZE_PERF_WEAPON=SHOTGUN / RIFLE), from wave
        // PerfWave, can't die, and the frame rate is logged every 5 s for 60 s ("Maze perf: ...").
        const int PerfWave = 9;
        bool perf; float perfT, perfWin, perfWorst; int perfFrames, perfForceWeapon = -1;
        float perfSum; int perfN; float perfMin = 999;
        readonly List<float> perfFrameTimes = new(8000);

        static bool PerfRequested()
        {
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-mazePerf") return true;
            return System.Environment.GetEnvironmentVariable("MAZE_PERF") == "1";
        }

        void StartPerf()
        {
            perf = true;
            AudioListener.volume = 0; // perf runs are silent
            if (System.Environment.GetEnvironmentVariable("MAZE_PERF_RUN") == "1")
            {   // a sector run (tier 1) played by the demo bot: biomass orbs, cards, floors, the boss
                PendingTier = 1; LoadMap(0); menu.Hide(); Play(); StartRun(); demo = true; godMode = true; // (Play skips runs in perf mode)
                Debug.Log("Maze perf: started, sector run tier 1, demo bot");
                return;
            }
            if (System.Environment.GetEnvironmentVariable("MAZE_PERF_MAP") == "start")
            {   // the escape floor's START room (the floor test): stand there, a few Drones around, can't die
                LoadMap(1); menu.Hide(); Play(); godMode = true;
                for (int k = 0; k < 6; k++) DebugSpawn(0, player.p + new Vector2(Mathf.Cos(k * 1.05f), Mathf.Sin(k * 1.05f)) * 5f);
                Debug.Log("Maze perf: started, escape floor start room, 6 Drones, floor " + (FloorNew ? "NEW" : "OLD"));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("MAZE_PERF_MAP") == "1")
            {   // the escape floor: straight into room 4 (the biggest swarm, a Bloater)
                LoadMap(1); menu.Hide(); Play(); demo = true; escape.DebugEnter(4);
                Debug.Log("Maze perf: started, demo bot on the escape floor, room 4");
                return;
            }
            menu.Hide(); Play(); demo = true;
            string wn = System.Environment.GetEnvironmentVariable("MAZE_PERF_WEAPON");
            if (string.IsNullOrEmpty(wn)) wn = "SMG";
            for (int i = 0; i < cfg.weapons.Length; i++) if (cfg.weapons[i].name == wn) perfForceWeapon = i;
            wave = PerfWave - 1; breakT = 0.5f;
            Debug.Log($"Maze perf: started, demo bot with the {wn} from wave {PerfWave}");
        }

        void PerfTick()
        {
            float ft = Time.unscaledDeltaTime;
            perfT += ft; perfWin += ft; perfFrames++; perfWorst = Mathf.Max(perfWorst, ft);
            if (perfT > 6f && perfT <= 61f) perfFrameTimes.Add(ft);
            if (perfWin < 5f || perfT > 65f) return;
            float fps = perfFrames / perfWin;
            if (perfT > 6f) { perfSum += fps; perfN++; perfMin = Mathf.Min(perfMin, fps); } // skip the first window (loading)
            Debug.Log($"Maze perf: t={perfT:F0}s wave {wave}  {swarm.enemies.Count} hostiles  avg {fps:F1} fps  worst frame {perfWorst * 1000:F0} ms");
            if (perfT > 60f)
            {
                perfFrameTimes.Sort();
                float p99 = perfFrameTimes.Count > 0 ? perfFrameTimes[Mathf.Min(perfFrameTimes.Count - 1, (int)(perfFrameTimes.Count * 0.99f))] : 0;
                float worst = perfFrameTimes.Count > 0 ? perfFrameTimes[^1] : 0;
                Debug.Log($"Maze perf: done, target {Application.targetFrameRate} fps, mean {perfSum / Mathf.Max(1, perfN):F1} fps, lowest 5 s window {perfMin:F1} fps, 1% low {(p99 > 0 ? 1 / p99 : 0):F1} fps, worst frame after load {worst * 1000:F1} ms, {perfFrameTimes.Count} frames");
            }
            perfWin = 0; perfFrames = 0; perfWorst = 0;
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        // ------------------------------------------------------------ setup
        void SetupLightsAndCamera()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // cold blue ambient (Cryo Station) under a strong, slightly cool key light so shadows stay readable
            RenderSettings.ambientSkyColor = CryoLook ? new Color(0.6f, 0.71f, 0.86f) : new Color(0.66f, 0.69f, 0.78f);
            RenderSettings.ambientEquatorColor = CryoLook ? new Color(0.54f, 0.63f, 0.76f) : new Color(0.58f, 0.6f, 0.66f);
            RenderSettings.ambientGroundColor = CryoLook ? new Color(0.4f, 0.47f, 0.58f) : new Color(0.44f, 0.47f, 0.55f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Bg;
            RenderSettings.fogStartDistance = 90; RenderSettings.fogEndDistance = 170;
            RenderSettings.skybox = null;

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) l.gameObject.SetActive(false);
            var sgo = new GameObject("Sun");
            sgo.transform.SetParent(transform, false);
            sun = sgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = CryoLook ? new Color32(0xf4, 0xf7, 0xff, 255) : new Color32(0xff, 0xf6, 0xea, 255);
            sun.intensity = CryoLook ? 1.4f : 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sun.transform.rotation = Quaternion.LookRotation(-SunOffset);
            RenderSettings.sun = sun;

            cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Bg;
            cam.nearClipPlane = 1f; cam.farClipPlane = 400f;
            cam.fieldOfView = cfg.camera.fov;
        }

        // Cryo Station, "Frosted lab": frost-plated walls with snow caps and icicles, a pale frosted deck (+ plate detail),
        // glowing cryo pads under ice, grey/yellow crates with snow, swarm creep at the spawns and edges.
        // Textures from Concepts/CryoStation (Tools/cryo_tiles.py); falls back to the warehouse if they're missing.
        public static bool CryoLook = false; // reverted Oct 11 at Manuel's request: the original white/grey level. true = the Cryo re-skin (kept, unused)
        public static bool CryoPbr = true; // false = the earlier painted-tile Cryo materials (Resources/Cryo/*.png, still shipped)

        void BuildLevel()
        {
            var root = new GameObject("Level").transform;
            levelRoot = root;
            root.SetParent(transform, false);
            if (CohesiveZone.For(this))
            {
                CargoEnvironment.Build(root); lastLb = null; cryoDeck = true;
                return;
            }
            if (CryoSoftEnvironment.For(run, sector, floor))
            {
                CryoSoftEnvironment.Build(root); lastLb = null; cryoDeck = true;
                return; // original materials/deck are not allocated by the rounded art path
            }
            Texture2D T(string n) => Resources.Load<Texture2D>("Cryo/" + n);
            bool cryo = CryoLook && T("frost_wall") != null;
            LevelBuilder lb;
            Texture2D Pbr(string n) => Resources.Load<Texture2D>("Cryo/pbr/" + n);
            bool pbr = cryo && CryoPbr && Pbr("wall_color") != null; // the CC0 PBR sets (Tools/cryo_pbr.mjs); else the painted tiles
            if (cryo)
            {
                Material M(string n, float tile, float nrm = 1f) => Mats.PBR(Pbr(n + "_color"), Pbr(n + "_normal"), Pbr(n + "_ms"), new Vector2(tile, tile), nrm);
                var wall = pbr ? M("wall", 3f) : Mats.Lit(Color.white, T("frost_wall"), new Vector2(4, 4), 0.3f);
                var snow = pbr ? M("snow", 4f, 0.35f) : Mats.Lit(Color.white, T("snow"), new Vector2(4, 4), 0.12f);
                var icicle = Mats.Lit(new Color(0.82f, 0.93f, 1f), null, null, 0.92f); icicle.SetFloat("_Cull", 0);
                lb = new LevelBuilder(root)
                {
                    side = wall, roof = snow, concrete = wall,
                    crate = pbr ? Mats.PBR(Pbr("crate_color"), Pbr("crate_normal"), Pbr("crate_ms"), new Vector2(2, 1.4f)) : Mats.Lit(Color.white, T("crate"), new Vector2(2, 1.4f), 0.3f, 0.2f),
                    crateCap = snow,
                    trim = Mats.Lit(new Color32(0x3a, 0x42, 0x50, 255), null, null, 0.5f, 0.3f),
                    door = Mats.Lit(new Color(0.78f, 0.86f, 0.95f), ProcTex.Door(), null, 0.4f, 0.2f),
                    hvac = Mats.Lit(new Color(0.9f, 0.95f, 1f), ProcTex.Hvac(), new Vector2(1, 1), 0.5f, 0.35f),
                    glass = Mats.Lit(new Color(0.7f, 0.9f, 1f), null, null, 0.92f, 0.3f),
                    yellow = Mats.Lit(new Color32(0xf0, 0xc0, 0x34, 255), null, null, 0.4f),
                    dark = Mats.Lit(new Color32(0x1b, 0x1d, 0x23, 255), null, null, 0.5f, 0.2f),
                    sign = Mats.Lit(Color.white, ProcTex.Sign(), null, 0.3f),
                    ground = pbr ? M("snow", 6f, 0.35f) : Mats.Lit(Color.white, T("snow"), new Vector2(6, 6), 0.08f),
                    cryoPad = pbr ? Mats.PBR(Pbr("ice_color"), Pbr("ice_normal"), Pbr("ice_ms"), new Vector2(4, 4), 0.8f, Pbr("ice_emit"), new Color(1.2f, 1.5f, 1.5f))
                                  : Mats.LitEmit(T("cryo_pad"), new Vector2(4, 4), T("cryo_pad_emit"), new Color(0.75f, 1f, 1f), 0.85f),
                    icicle = icicle,
                    wallLight = Mats.Unlit(new Color(1f, 0.62f, 0.24f)),
                    drift = Mats.Fade(Color.white, T("drift")),
                    creep = pbr ? Mats.PBR(Pbr("creep_color"), Pbr("creep_normal"), Pbr("creep_ms"), Vector2.one, 1f, Pbr("creep_emit"), new Color(0.7f, 1f, 0.5f), 0.42f)
                                : Mats.UnlitClip(T("creep_mask"), 0.42f),
                    sac = Mats.Lit(new Color(0.32f, 0.66f, 0.12f), null, null, 0.9f),
                };
                lb.drift.SetTextureScale("_BaseMap", Vector2.one);
            }
            else
            {
                lb = new LevelBuilder(root)
                {
                    side = Mats.Lit(Color.white, ProcTex.Siding(), new Vector2(4, 4), 0.18f),
                    roof = Mats.Lit(Color.white, ProcTex.Roof(), new Vector2(4, 4), 0.05f),
                    concrete = Mats.Lit(Color.white, ProcTex.Concrete(), new Vector2(4, 4), 0.05f),
                    crate = Mats.Lit(Color.white, ProcTex.Crate(), new Vector2(2, 1.4f), 0.1f),
                    trim = Mats.Lit(Color.white, ProcTex.Trim(), new Vector2(2, 1), 0.45f, 0.15f),
                    door = Mats.Lit(Color.white, ProcTex.Door(), null, 0.4f, 0.2f),
                    hvac = Mats.Lit(Color.white, ProcTex.Hvac(), new Vector2(1, 1), 0.5f, 0.35f),
                    glass = Mats.Lit(new Color32(0x8e, 0xaa, 0xd8, 255), null, null, 0.85f, 0.5f),
                    yellow = Mats.Lit(new Color32(0xf0, 0xc9, 0x3a, 255), null, null, 0.4f),
                    dark = Mats.Lit(new Color32(0x1b, 0x1d, 0x23, 255), null, null, 0.5f, 0.2f),
                    sign = Mats.Lit(Color.white, ProcTex.Sign(), null, 0.3f),
                    ground = Mats.Lit(Color.white, ProcTex.Ground(), new Vector2(4, 4), 0.05f),
                };
            }
            var floorTex = ProcTex.Floor(floorPixelsPerMeter, cryo, MazeMap.Facility);
            // floor: the painted map (pale, frost, stripes, contact shading) with the plate set as detail, a plate ~2 m
            var plates = new Vector2(MazeMap.W * MazeMap.C / 9f, MazeMap.H * MazeMap.C / 9f);
            lb.floor = pbr ? Mats.LitDetailPBR(floorTex, Pbr("floor_detail"), Pbr("floor_normal"), plates, 0.32f, 0.7f)
                     : cryo ? Mats.LitDetail(floorTex, T("floor_detail"), new Vector2(MazeMap.W * MazeMap.C / 6, MazeMap.H * MazeMap.C / 6), 0.25f) : Mats.Lit(Color.white, floorTex, null, 0.12f);
            bool deck = run && CryoFloor.For(sector);   // Cryo Station: the tiled deck + markings overlay + decals
            if (deck) { lb.floor = CryoFloor.Floor(); lb.floorWorldTile = CryoFloor.Tile; }
            if (!FloorEditing.FloorRuntime.TryCurrent(out _) && CryoKitEnvironment.For(run, sector)) lb.cryoKit = new CryoKitEnvironment(root, lb);
            lb.Build(floorTex); lastLb = lb; cryoDeck = deck;
            if (FloorEditing.FloorRuntime.TryCurrent(out _)) FloorEditing.FloorRuntime.Spawn(root);
            if (deck) CryoFloor.Build(root);
            if (MazeMap.Facility && !deck) StartRoomFloor(root, floorTex);
            if (cryo) { cryoFx = root.gameObject.AddComponent<CryoAmbience>(); cryoFx.Init(lb.creepEdges, lb.creep); }
        }
        CryoAmbience cryoFx; LevelBuilder lastLb; bool cryoDeck;

        // a zone's look over the rebuilt level (null: the default facility look)
        public void ApplyBiome(SectorDef s)
        {
            if (s == null) { RestoreBiome(); return; }
            if (CohesiveZone.For(s)) { CargoEnvironment.Lighting(sun, cam); return; }
            var walls = new List<Material>(); var floors = new List<Material>();
            if (lastLb != null) { walls.Add(lastLb.side); walls.Add(lastLb.roof); walls.Add(lastLb.concrete); walls.Add(lastLb.trim); if (!cryoDeck) floors.Add(lastLb.floor); floors.Add(lastLb.ground); }   // (the Cryo deck keeps its own colour: no zone tint)
            if (startFloor != null && startFloor.TryGetComponent<MeshRenderer>(out var mr)) floors.Add(mr.sharedMaterial);
            Biome.Apply(s, sun, cam, walls, floors, cfg.camera.distance);
        }
        public void RestoreBiome()
        {
            CargoEnvironment.RestoreLighting();
            sun.color = new Color32(0xff, 0xf6, 0xea, 255); sun.intensity = 1.25f;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.69f, 0.78f); RenderSettings.ambientEquatorColor = new Color(0.58f, 0.6f, 0.66f); RenderSettings.ambientGroundColor = new Color(0.44f, 0.47f, 0.55f);
            RenderSettings.fogColor = Bg; RenderSettings.fogStartDistance = 90; RenderSettings.fogEndDistance = 170; cam.backgroundColor = Bg;
            Biome.Clear();
        }

        // FLOOR (Escape Test, every room): the picked worn tile as a x2 detail + low normal on top of the painted floor
        // (stripes / pads stay, creep and the LIFT pad still draw over it), ~4 m per tile, world-space. Grime is a
        // separate world-space layer so nothing repeats with the tile: a two-octave noise baked into a copy of the
        // painted map (non-repeating, ~10 px/m) plus seeded stain decals per room, scuffs along walls and vents and a
        // darker trodden path through the doors (Resources/Textures/EscapeFloor/floor_dirt.png, 4 blotches).
        // The pause panel flips NEW / OLD for comparison (not remembered).
        public static bool FloorNew = true, FloorDirt = true;
        GameObject startFloor, floorDirt;
        void StartRoomFloor(Transform root, Texture2D floorTex)
        {
            var det = Resources.Load<Texture2D>("Textures/EscapeFloor/floor_worn_detail");
            var nrm = Resources.Load<Texture2D>("Textures/EscapeFloor/floor_worn_normal");
            if (det == null || nrm == null || MazeMap.roomOf == null) return;
            float C = MazeMap.C, mapW = MazeMap.W * C, mapH = MazeMap.H * C;
            startFloor = new GameObject("EscapeFloorNew");
            startFloor.transform.SetParent(root, false);
            var mesh = new Mesh { name = "EscapeFloor" };
            mesh.vertices = new[] { new Vector3(0, 0.004f, -mapH), new Vector3(0, 0.004f, 0), new Vector3(mapW, 0.004f, 0), new Vector3(mapW, 0.004f, -mapH) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.RecalculateBounds();
            startFloor.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = startFloor.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mats.LitDetailPBR(GrimeMap(floorTex, mapW, mapH), det, nrm, new Vector2(mapW / 4f, mapH / 4f), 0.18f, 0.35f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            floorDirt = BuildFloorDirt(root);
            startFloor.SetActive(FloorNew);
            if (floorDirt != null) floorDirt.SetActive(FloorNew && FloorDirt);
        }

        // the painted map with a soft, non-repeating grime: two octaves of world-space noise, darkening up to ~15%
        static Texture2D GrimeMap(Texture2D src, float mapW, float mapH)
        {
            if (src == null || !src.isReadable) return src;
            var px = src.GetPixels32(); int w = src.width, h = src.height;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float wx = x * mapW / w, wz = y * mapH / h;
                float n = Mathf.PerlinNoise(wx / 13f + 31.7f, wz / 13f + 7.3f) * 0.65f + Mathf.PerlinNoise(wx / 4.5f + 3.1f, wz / 4.5f + 91.4f) * 0.35f;
                float k = 1f - 0.15f * Mathf.Clamp01((n - 0.4f) * 2.2f);
                var c = px[y * w + x];
                px[y * w + x] = new Color32((byte)(c.r * k), (byte)(c.g * k * 0.995f), (byte)(c.b * k * 0.985f), c.a);
            }
            var t = new Texture2D(w, h, src.format, src.mipmapCount > 1) { name = "EscapeFloorGrime", wrapMode = src.wrapMode, filterMode = src.filterMode, anisoLevel = src.anisoLevel };
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        // stain decals: one mesh for grime (grey-brown), one for scorch/oil (near black), atlas of 4 blotches
        GameObject BuildFloorDirt(Transform root)
        {
            var tex = Resources.Load<Texture2D>("Textures/EscapeFloor/floor_dirt");
            if (tex == null) return null;
            var rng = new System.Random(4242); // seeded, and apart from UnityEngine.Random (the sims stay deterministic)
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float C = MazeMap.C; int W = MazeMap.W, H = MazeMap.H;
            var grime = new List<(Vector2 p, Vector2 size, float rot, int tile)>(); var scorch = new List<(Vector2 p, Vector2 size, float rot, int tile)>();
            bool Solid(int x, int z) => x < 0 || z < 0 || x >= W || z >= H || MazeMap.IsSolidMove(MazeMap.grid[z, x]) && MazeMap.grid[z, x] != 'D';
            // per room: 15-30 stains on its cells, a few of them scorch / oil
            var cellsOf = new Dictionary<int, List<int>>();
            for (int i = 0; i < MazeMap.roomOf.Length; i++) { int r = MazeMap.roomOf[i]; if (r < 0) continue; if (!cellsOf.TryGetValue(r, out var l)) cellsOf[r] = l = new List<int>(); l.Add(i); }
            foreach (var kv in cellsOf)
            {
                var cells = kv.Value; int n = Mathf.Clamp(cells.Count / 5, 15, 30);
                for (int k = 0; k < n; k++)
                {
                    int c = cells[rng.Next(cells.Count)];
                    var p = new Vector2((c % W + R(0, 1)) * C, (c / W + R(0, 1)) * C);
                    float s = R(1.3f, 3.6f);
                    if (rng.NextDouble() < 0.18) scorch.Add((p, new Vector2(s * 0.7f, s * R(0.5f, 0.8f)), R(0, 360), rng.Next(4)));
                    else grime.Add((p, new Vector2(s, s * R(0.6f, 1f)), R(0, 360), rng.Next(4)));
                }
                // scuffs along the walls: small elongated marks hugging solid neighbours
                foreach (var c in cells)
                {
                    int x = c % W, z = c / W;
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (!Solid(x + dx, z + dz) || rng.NextDouble() > 0.22) continue;
                        var p = new Vector2((x + 0.5f + dx * 0.36f) * C, (z + 0.5f + dz * 0.36f) * C) + new Vector2(dz, dx) * R(-0.6f, 0.6f);
                        grime.Add((p, new Vector2(R(1.1f, 2f), R(0.45f, 0.75f)), (dx != 0 ? 90 : 0) + R(-12, 12), rng.Next(4)));
                    }
                }
            }
            // vents: a ring of grime around each
            foreach (var vents in MazeMap.roomVents) foreach (var v in vents) grime.Add((v, new Vector2(2.6f, 2.6f), R(0, 360), rng.Next(4)));
            // doors: a darker trodden path through the opening and a step either side
            foreach (var d in MazeMap.doors)
            {
                bool alongX = !MazeMap.DoorAlongX(d); // the passage runs across the door's wall
                var dir = alongX ? Vector2.right : Vector2.up;
                for (int k = -1; k <= 1; k++)
                    grime.Add((d.center + dir * k * C * 0.9f, new Vector2(R(2.2f, 2.8f), R(1.2f, 1.6f)), (alongX ? 0 : 90) + R(-8, 8), rng.Next(4)));
                scorch.Add((d.center, new Vector2(1.6f, 1.0f), (alongX ? 0 : 90) + R(-10, 10), rng.Next(4)));
            }
            var go = new GameObject("EscapeFloorDirt"); go.transform.SetParent(root, false);
            Decals(go.transform, "Grime", grime, Mats.Fade(new Color(0.31f, 0.27f, 0.23f, 0.38f), tex), 0.008f);
            Decals(go.transform, "Scorch", scorch, Mats.Fade(new Color(0.09f, 0.085f, 0.08f, 0.40f), tex), 0.009f);
            return go;
        }

        static void Decals(Transform parent, string name, List<(Vector2 p, Vector2 size, float rot, int tile)> list, Material mat, float y)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var (p, size, rot, tile) in list)
            {
                var q = Quaternion.Euler(0, rot, 0); var c = MazeMap.ToWorld(p, y); int k = v.Count;
                var u0 = new Vector2((tile % 2) * 0.5f, (tile / 2) * 0.5f);
                foreach (var (sx, sz) in new[] { (-1, -1), (-1, 1), (1, 1), (1, -1) })
                {
                    v.Add(c + q * new Vector3(sx * size.x * 0.5f, 0, sz * size.y * 0.5f));
                    uv.Add(u0 + new Vector2(sx > 0 ? 0.5f : 0, sz > 0 ? 0.5f : 0));
                }
                tri.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            if (v.Count == 0) return;
            var mesh = new Mesh { name = name }; mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }
        public void ToggleFloor() { FloorNew = !FloorNew; if (startFloor != null) startFloor.SetActive(FloorNew); if (floorDirt != null) floorDirt.SetActive(FloorNew && FloorDirt); }
        public void SetFloorDirt(bool on) { FloorDirt = on; if (floorDirt != null) floorDirt.SetActive(FloorNew && FloorDirt); }

        // ------------------------------------------------------------ flow
        // ------------------------------------------------------------ maps
        // Switch the level (0 Cryo Station, 1 Escape Test): rebuild geometry, floor, barrels, doors in place.
        [System.NonSerialized] public EscapeRun escape;
        Transform levelRoot;
        public void LoadMap(int index)
        {
            if (MazeMap.MapIndex == index && levelRoot != null) return;
            swarm.Clear();
            MazeMap.MapIndex = index;
            MazeMap.Init();
            if (levelRoot != null) Destroy(levelRoot.gameObject);
            BuildLevel(); RestoreBiome();
            swarm.ResizeHash();
            barrels.Rebuild();
            escape.Build(this);
            ResetGame();
            Debug.Log($"Maze: map {index} loaded ({MazeMap.W}x{MazeMap.H}, {MazeMap.RoomCount} rooms, {MazeMap.doors.Count} doors)");
        }

        public string WaveLabel => MazeMap.Escape ? escape.Label : run ? GoalNames[(int)shownGoal] : "WAVE " + wave;

        // the extraction lift took you out: a win screen with the time
        public void Escape(float runTime)
        {
            state = State.Won;
            Haptics.Heavy(); AddShake(0.4f);
            float best = PlayerPrefs.GetFloat("ms.escape.best", 0);
            bool isBest = best <= 0 || runTime < best;
            if (isBest && !TestRun) { PlayerPrefs.SetFloat("ms.escape.best", runTime); PlayerPrefs.Save(); best = runTime; }
            hud.ShowEscaped(runTime, kills, score, best, isBest);
        }

        void ResetGame()
        {
            swarm.Clear();
            foreach (var b in bullets) Fx.I.ReleaseTracer(b.tr);
            bullets.Clear();
            foreach (var p in pickups) Destroy(p.tr.gameObject);
            pickups.Clear();
            barrels.ResetAll();
            Fx.I.ClearAll();
            player.p = MazeMap.playerSpawn; player.vel = Vector2.zero; player.ResetEngineerMotion(); player.maxHp = cfg.player.hp; player.hp = player.maxHp;
            run = false; choosing = Choice.None; mods.Reset(); ClearOrbs(); fires.Clear(); swarm.HpScale = swarm.DmgScale = swarm.SpeedScale = 1; player.shieldUp = false;
            player.dashT = player.dashCd = player.fireCd = player.hurtT = player.shootT = 0;
            if (escape != null) escape.ResetRun();
            int nw = cfg.weapons.Length;
            player.ammo = new int[nw]; player.reloadFrom = new int[nw]; player.reloadLeft = new float[nw]; player.reloadDur = new float[nw];
            for (int i = 0; i < nw; i++) player.ammo[i] = cfg.weapons[i].magSize;
            player.idleT = 0;
            blastEnemies.Clear(); blastAge = 9; camPunch = 0;
            bloatersLeft = 0; leapersLeft = 0;
            wave = 0; toSpawn = 0; spawnT = 0; breakT = 1.2f; kills = 0; score = 0; time = 0; shake = 0;
            MazeMap.flowCell = -1; flowT = 0;
            camTarget = player.p;
            player.SetVisible(true);
        }

        void Play()
        {
            ResetGame();
            player.weapon = Prefs.Vanguard ? 0 : Prefs.Scout ? ScoutWeapon : Prefs.WeaponIndex(cfg.weapons);
            state = State.Playing; Time.timeScale = 1;
            if (PendingTier > 0 && !MazeMap.Escape && !perf) StartRun(); // a sector run (floors, levels, cards)
            cam.cullingMask = ~0;
            hud.SetMode(Hud.Mode.Playing);
        }

        // called by the main menu behind the closed drop hatch
        public void Deploy() { Play(); AddShake(0.6f); }
        public void Restart() => Play();
        public void ToMenu() { TestEnd(); ResetGame(); CombatAudio.I.StopAll(); EnterMenu(); }
        public void ToSector() { ToMenu(); MapSelect.I?.Show(); } // game over -> SECTOR
        public void TogglePause() { if (state == State.Playing || state == State.Paused) SetPaused(state == State.Playing); }

        void EnterMenu()
        {
            state = State.Menu; Time.timeScale = 1; demo = false;
            cam.cullingMask = 0; // the menu backdrop covers the screen: don't render the level under it
            hud.SetMode(Hud.Mode.Hidden);
            menu.Show();
        }

        // Settings > QUALITY: LOW drops shadows, MSAA and a little render scale (battery saver)
        (int msaa, float scale)? urpDefaults;
        // CAMERA setting (Settings and the pause panel): flips FAR/CLOSE, applies at once and drops any pinch zoom
        public void ToggleCamera()
        {
            Prefs.CamCloseOn = !Prefs.CamCloseOn;
            cfg.camera.distance = ReadabilityFx.CamDistance;
        }

        public void ApplyQuality()
        {
            bool hi = Prefs.HighQuality;
            if (sun != null) sun.shadows = hi ? LightShadows.Soft : LightShadows.None;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urpDefaults ??= (urp.msaaSampleCount, urp.renderScale);
                urp.msaaSampleCount = hi ? urpDefaults.Value.msaa : 1;
                urp.renderScale = hi ? urpDefaults.Value.scale : 0.8f;
            }
        }
        void OnDestroy()
        {   // the URP asset is shared: don't leave the editor's copy changed after Play mode
            if (urpDefaults.HasValue && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            { urp.msaaSampleCount = urpDefaults.Value.msaa; urp.renderScale = urpDefaults.Value.scale; }
        }

        void StartWave(int n)
        {
            wave = n;
            toSpawn = cfg.waves.firstCount + cfg.waves.perWave * (n - 1);
            bloatersLeft = swarm.BloaterType >= 0 ? cfg.bloater.CountFor(n) : 0;
            bloaterStride = toSpawn / (bloatersLeft + 1); nextBloaterAt = toSpawn - bloaterStride;
            leapersLeft = swarm.LeaperType >= 0 ? cfg.leaper.CountFor(n) : 0;
            leaperStride = toSpawn / (leapersLeft + 1); nextLeaperAt = toSpawn - leaperStride / 2;
            spawnT = 0.5f;
            if (cfg.barrels.respawnEachWave && n > 1) barrels.Respawn(player.p);
            hud.ShowBanner("WAVE " + n);
            if (cryoFx != null) cryoFx.SetWave(n); // the creep spreads a little each wave
        }

        void GameOver()
        {
            state = State.Dead;
            player.SetVisible(false);
            var at = MazeMap.ToWorld(player.p, 0.9f);
            Fx.I.Debris(at, 26, new Color(0.97f, 0.97f, 0.98f), 6, 3, 0.2f, 6);
            Fx.I.Debris(at, 8, new Color32(0x23, 0x46, 0xb8, 255), 5, 3, 0.15f, 6);
            shake = 1;
            Haptics.Heavy();
            if (run) { RunEnd(false); return; }
            int best = Prefs.Best; bool isBest = score > best && score > 0;
            if (isBest && !TestRun) Prefs.Best = best = score;
            if (MazeMap.Escape) hud.ShowGameOver(escape.Cur, kills, score, best, isBest, "ROOM");
            else hud.ShowGameOver(wave, kills, score, best, isBest);
        }

        void SetPaused(bool p)
        {
            state = p ? State.Paused : State.Playing;
            Time.timeScale = p ? 0 : 1;
            hud.SetMode(p ? Hud.Mode.Paused : Hud.Mode.Playing);
        }

        void OnApplicationPause(bool paused) { if (paused && state == State.Playing) SetPaused(true); }
        void OnApplicationFocus(bool focus) { if (!focus && state == State.Playing && Application.isMobilePlatform) SetPaused(true); }

        public void AddShake(float s) => Feel.Shake(Mathf.Min(0.35f, s * 0.5f), 0.3f);   // (the old 0-1 shake, mapped onto Feel)

        bool skillPressed; float lastBlastHaptic;
        public static readonly Dictionary<string, float> DamageBy = new();
        [System.NonSerialized] public float EnemySlow = 1f;   // latched Ticks, Weaver webs (set by the run each frame)
        // LAST BREATH (2.6): once a floor, a hit that would kill from >= 35% HP leaves 1 HP instead: 1.2 s untouchable,
        // a 4 m shockwave (2 m shove, Drones stunned 0.8 s), and the whole screen says so
        public bool lastBreathUsed, secondWindUsed; float lastBreathT;
        public void OnCloseCall() { Chain.Add(3, this); Feel.Dip(0.6f, 0.12f); Haptics.Light(); if (Feel.Callouts) hud.ShowBanner("CLOSE CALL"); }
        public void OnPerfectDash() { Chain.Add(10, this); Feel.Dip(0.5f, 0.18f); Haptics.Medium(); player.dashCd *= 0.67f; hud.ShowBanner("PERFECT"); if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(player.p, 0.6f), 16, new Color(1f, 0.8f, 0.25f), 5, 0.5f, 0.1f, 3); }
        float botFlareT;
        // FRAG LAUNCHER: lands on the first enemy along the aim (else at reach); 2.5 m blast, half at the edge, a 1 m shove
        void FragShot(float dmg)
        {
            float R = Reach, best = R; Vector2 at = player.p + player.aim * (R * 0.8f);
            foreach (var e in swarm.enemies)
            {
                if (e.dead || e.Hidden) continue; var d = e.p - player.p; float m = d.magnitude;
                if (m < best && m > 0.5f && Vector2.Dot(d / m, player.aim) > 0.92f) { best = m; at = e.p; }
            }
            MazeMap.CollideGrid(ref at, 0.3f);
            const float B = 2.5f;
            foreach (var e in swarm.enemies)
            {
                if (e.dead) continue; float d = (e.p - at).magnitude; if (d > B + e.r) continue;
                e.hp -= dmg * (1f - 0.5f * Mathf.Clamp01(d / B)); e.flash = 1; if (!e.boss) e.k += (e.p - at).normalized * 4f;
                if (e.hp <= 0) KillEnemy(e);
            }
            if (!SimRun) Fx.I.Boom(MazeMap.ToWorld(at), B);
            AddShake(0.12f);
        }
        // who carries what: the Heavy the minigun (slot 0) + THERMAL / RAIL; the others everything else; blueprints must be owned
        public bool WeaponAllowed(int i)
        {
            if (i < 0 || i >= cfg.weapons.Length) return false; var w = cfg.weapons[i]; bool heavy = w.name == "THERMAL" || w.name == "RAIL";
            if (Prefs.Scout) return w.name == "NEEDLER"; if (w.name == "NEEDLER") return false;
            if (Prefs.Vanguard) return i == 0 || heavy && Meta.WeaponOwned(w.name);
            return !heavy && Meta.WeaponOwned(w.name);
        }
        // TWIN NEEDLERS: needles turn toward the nearest enemy ahead at 10 deg/s; every 6th on one target pops for +60
        // (Mk II +8 mag, Mk III pop every 5th, Mk IV rate +10%, Mk V +8 mag and pop +90; HORNET SWARM / VENOM NEEDLES evolutions)
        void Home(Bullet b, float dt)
        {
            Enemy best = null; float bd = 64f;
            foreach (var e in swarm.enemies) { if (e.dead || e.Hidden) continue; var d = e.p - b.p; float m = d.sqrMagnitude; if (m < bd && Vector2.Dot(d, b.d) > 0) { bd = m; best = e; } }
            if (best == null) return;
            float want = Mathf.Atan2((best.p - b.p).x, (best.p - b.p).y), cur = Mathf.Atan2(b.d.x, b.d.y);
            float a = cur + Mathf.Clamp(Mathf.DeltaAngle(cur * Mathf.Rad2Deg, want * Mathf.Rad2Deg), -10f * dt, 10f * dt) * Mathf.Deg2Rad;
            b.d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }
        void NeedlePop(Enemy e, Bullet b)
        {
            int tierN = Meta.WeaponTier(8);
            if (++e.needles % (tierN >= 2 ? 5 : 6) != 0) return;
            float pop = (tierN >= 4 ? 90f : 60f) * b.dmg / Mathf.Max(1f, b.w.damage);
            e.hp -= pop; e.flash = 1;
            if (Evolved2("venom_needles")) e.burnT = Mathf.Max(e.burnT, 4f);
            if (Evolved2("hornet_swarm")) ChainHit(e, 3, 6f, pop * 0.5f);
            if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(e.p, 0.9f), 8, new Color(0.25f, 1f, 0.85f), 4, 0.3f, 0.08f, 3);
        }
        public void ApplyNeedlerTier()
        {   // the tier deltas (mag / rate) on the weapon itself, at run start
            int i = ScoutWeapon; if (cfg.weapons[i].name != "NEEDLER") return; int t = Meta.WeaponTier(8);
            cfg.weapons[i].magSize = 48 + (t >= 1 ? 8 : 0) + (t >= 4 ? 8 : 0); cfg.weapons[i].rate = 14f * (t >= 3 ? 1.1f : 1f); ApplyReloadSwitch();
        }
        public bool VgMinigun => Prefs.Vanguard && player.weapon == 0;
        // the in-run swap skips weapons still on blueprints
        int NextOwned(int from, int dir)
        {
            int n = cfg.weapons.Length;
            for (int k = 1; k <= n; k++) { int i = ((from + dir * k) % n + n) % n; if (WeaponAllowed(i)) return i; }
            return from;
        }
        int ScoutWeapon => Mathf.Max(0, System.Array.FindIndex(cfg.weapons, w => w.name == "NEEDLER"));   // Twin Needlers
        int BotCrowd() { int n = 0; foreach (var e in swarm.enemies) if (!e.dead && !e.type.nest && !e.egg && (e.p - player.p).sqrMagnitude < 36f) n++; return n; }   // the bot's skill: a real crowd within 6 m
        public void HurtPlayer(float dmg)
        {
            if (lastBreathT > 0) return;
            if (player.dashT > 0 || state != State.Playing || perf || godMode) return;
            if (run && ShieldBlocks()) return;
            if (run) { dmg = TalentHurt(PowerUps.Absorb(dmg) * GearDamageTakenMul); if (dmg <= 0) return; damageTaken += dmg; }
            if (SimRun)
            {   // the sims: who hurts the hero (caller method), printed when the run ends
                var m = new System.Diagnostics.StackFrame(1).GetMethod(); string k = m != null ? m.DeclaringType.Name + "." + m.Name : "?";
                DamageBy.TryGetValue(k, out float v); DamageBy[k] = v + dmg;
            }
            if (run && !lastBreathUsed && player.hp >= player.maxHp * 0.35f && dmg >= player.hp)
            {
                lastBreathUsed = true; lastBreathT = 1.2f; player.hp = 1; player.hurtT = 0.25f; RunMusic.Duck();
                foreach (var e in swarm.enemies) { var d = e.p - player.p; if (e.dead || e.boss || d.magnitude > 4f) continue; e.k += d.normalized * 8f; if (e.type.name == "grunt") e.frozen = Mathf.Max(e.frozen, 0.8f); }
                Feel.Flash(new Color(1f, 0.6f, 0.6f), 0.45f, 0.1f, 0.45f); Feel.Dip(0.4f, 0.5f, exempt: true); Feel.Shake(0.2f, 0.3f, heroHit: true); Haptics.Heavy();
                hud.ShowBanner("LAST BREATH"); Chain.Hit(); return;
            }
            player.hp -= dmg; player.hurtT = 0.25f; OnHeroHit();
            float hp10 = cfg.player.hp * 0.1f;   // hero hit feel: shake, red edge, haptic; the chain loses a quarter
            Feel.Shake(dmg <= hp10 ? 0.12f : 0.18f, 0.16f, heroHit: true); Feel.Vignette(new Color(0.9f, 0.05f, 0.05f), dmg <= hp10 * 0.5f ? 0.12f : 0.35f, 0.18f);
            if (dmg > cfg.player.hp * 0.25f) Haptics.Heavy(); else if (dmg > hp10 * 0.5f) Haptics.Medium();
            if (run) Chain.Hit();
            Fx.I.Debris(MazeMap.ToWorld(player.p, 1), 4, Color.white, 3, 0.5f, 0.12f);
            if (player.hp <= 0 && run && !secondWindUsed && Meta.SecondWindHp > 0)
            {   // Research: SECOND WIND, one revive a run
                secondWindUsed = true; player.hp = player.maxHp * Meta.SecondWindHp; lastBreathT = 1.5f; hud.ShowBanner("SECOND WIND");
                Feel.Flash(new Color(0.6f, 1f, 0.7f), 0.4f, 0.15f, 0.45f); Haptics.Heavy(); return;
            }
            if (run && Meta.Relic(10) && !sealUsed && player.hp > 0 && player.hp < player.maxHp * 0.3f)
            {   // QUARANTINE SEAL: a bubble, once a floor
                sealUsed = true; lastBreathT = Meta.RelicV; hud.ShowBanner("QUARANTINE SEAL"); Haptics.Medium();
            }
            if (run && TS(5, 1) && !lowHealUsed && player.hp > 0 && player.hp < player.maxHp * 0.2f) { lowHealUsed = true; player.hp += player.maxHp * 0.3f; hud.ShowBanner("FIELD PATCH"); }   // talent
            if (player.hp <= 0 && run && TV(8, 1) && !vanRevive)
            {   // talent: one revive a run at 50%
                vanRevive = true; player.hp = player.maxHp * 0.5f; lastBreathT = 1.5f; hud.ShowBanner("BACK UP"); Haptics.Heavy(); return;
            }
            if (player.hp <= 0 && run && !tagsUsed && Meta.Relic(13))
            {   // VALIANT DOG TAGS: one revive a run
                tagsUsed = true; player.hp = player.maxHp * Meta.RelicV; lastBreathT = 1.5f; hud.ShowBanner("DOG TAGS");
                Feel.Flash(new Color(1f, 0.85f, 0.4f), 0.4f, 0.15f, 0.45f); Haptics.Heavy(); return;
            }
            if (player.hp <= 0) { player.hp = 0; GameOver(); }
        }

        public void KillEnemy(Enemy e)
        {
            if (e.dead) return;
            FeelFx.OnKill(this, e);   // (feel: the rare heart drop)
            swarm.Defer(true);
            try { KillEnemyInner(e); } finally { swarm.Defer(false); }
        }
        void KillEnemyInner(Enemy e)
        {
            e.dead = true;
            if (run && e.type.nest && !e.egg) events.Done("nest");
            if (run) { PowerUps.OnKill(e); CardOnKill(e); if (e.mods != 0) Elites.OnDeath(this, e); KillFeel(e); }
            if (e.type.ai != null) { EnemyAI.OnDeath(e); if (e.type.name.StartsWith("splitter")) EnemyAI.Split(swarm, e); }
            kills++; score += e.type.score * Mathf.Max(1, wave);
            if (run) { Ops.R.kills++; if (e.type.bloater) Ops.R.bloaterKills++; if (Ops.Mut(2)) bonusCredits++; }   // Hot Zone BOUNTY: +1 Credit a kill
            if (run) DropBiomass(e);
            if (Random.value < cfg.pickups.dropChance && !e.type.nest) SpawnPickup(e.p);
            if (e.type.nest && !SimRun) { Fx.I.GooBurst(MazeMap.ToWorld(e.p, 0.8f), 2.6f); Fx.I.Sparks(MazeMap.ToWorld(e.p, 1f), 20, new Color(0.55f, 1f, 0.3f), 6, 0.8f, 0.12f, 5); AddShake(0.45f); Haptics.Heavy(); if (CombatAudio.I != null) CombatAudio.I.BloaterBurst(); }
            if (e.view.model != null) return; // skinned models swell and pop in Swarm (goo burst)
            int n = Mathf.RoundToInt(10 * e.scale);
            Fx.I.Debris(MazeMap.ToWorld(e.p, 0.8f * e.scale), n, e.type.color, 4.5f, 2.6f, 0.17f * e.scale, 5);
            Fx.I.Debris(MazeMap.ToWorld(e.p, 0.8f * e.scale), Mathf.CeilToInt(n / 3f), new Color32(0x22, 0x33, 0x7a, 255), 4, 2.6f, 0.12f * e.scale, 5);
            Fx.I.Sparks(MazeMap.ToWorld(e.p, 1.3f * e.scale), 2, new Color32(0xff, 0x3b, 0x2f, 255), 3, 0.8f, 0.07f, 4);
        }

        // ------------------------------------------------------------ frame
        void Update()
        {
            float udt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            bool feel = run && state == State.Playing;
            Feel.Tick(udt, player != null ? player.hp / Mathf.Max(1, player.maxHp) : 1, feel);
            Tick(udt * (feel ? Feel.Scale : 1f));   // time dips scale the game, never the UI
        }

        // one frame of the game (public so the editor's MazeSim can fast-forward whole runs)
        public void Tick(float dt)
        {
            if (perf) PerfTick();
            bool overlayOpen = state != State.Playing;
            var inp = hud.ReadInput(overlayOpen);
            if (inp.skill) skillPressed = true;
            if (inp.flare && run && CellLight.I != null && CellLight.I.Dark) CellLight.I.ThrowFlare();
            if (state == State.Choosing) AutoPick(); // (the demo bot and the sims choose for themselves)

            if (inp.demo && !menu.Busy)
            {
                demo = !demo;
                if (state == State.Menu) { menu.Hide(); Play(); }
                hud.ShowBanner(demo ? "DEMO ON" : "DEMO OFF");
            }
            if (inp.pause && (state == State.Playing || state == State.Paused)) TogglePause();
            else if (inp.restart && state != State.Menu) Play();
            else if (inp.tap && overlayOpen)
            {
                if (state == State.Paused) SetPaused(false);
                else if (state == State.Dead || state == State.Won) Play();
                else if (state == State.Menu) menu.Deploy();
            }
            if (inp.zoom != 0)
            {
                var c = cfg.camera;
                c.distance = Mathf.Clamp(c.distance * (1 + Mathf.Sign(inp.zoom) * 0.08f), c.minDistance, c.maxDistance);
            }

            if (state == State.Playing)
            {
                time += dt;
                UpdatePlayer(dt, inp);
                if (MazeMap.Escape) escape.Tick(dt); else if (run) UpdateFloor(dt); else UpdateWaves(dt);
                if (run) TickRun(dt);
                swarm.Tick(dt, player);
                UpdateBullets(dt);
                UpdatePickups(dt);
            }
            else if (state == State.Dead) swarm.Tick(dt, player); // the swarm keeps milling around the body

            if (state != State.Paused)
            {
                barrels.Tick(dt, time, false);
                swarm.Compact();
                swarm.TickDying(dt);
                Fx.I.Tick(dt);
                player.hurtT -= dt; player.shootT -= dt;
            }
            if (state == State.Menu) moveVel = Vector2.zero;
            player.ringReach = Reach; // the auto-fire circle on the floor follows the real reach (minigun, RANGE cards)
            player.Visuals(dt, cfg, moveVel, state == State.Dead);
            swarm.Sync();
            UpdateCamera(dt);
            hud.Refresh(Time.unscaledDeltaTime, this);
        }

        // screen-space vector (x right, y up) -> plane direction, matching the camera yaw
        Vector2 ScreenToPlane(Vector2 s)
        {
            float yaw = cfg.camera.yaw * Mathf.Deg2Rad;
            return new Vector2(s.x * Mathf.Cos(yaw) - s.y * Mathf.Sin(yaw), -s.x * Mathf.Sin(yaw) - s.y * Mathf.Cos(yaw));
        }

        void UpdatePlayer(float dt, InputFrame inp)
        {
            var P = cfg.player;
            if (inp.selectWeapon > 0 && Meta.WeaponOwned(cfg.weapons[Mathf.Min(inp.selectWeapon, cfg.weapons.Length) - 1].name)) player.weapon = Mathf.Min(inp.selectWeapon, cfg.weapons.Length) - 1;
            if (inp.nextWeapon) player.weapon = NextOwned(player.weapon, 1);
            if (inp.prevWeapon) player.weapon = NextOwned(player.weapon, -1);
            // Iron Vanguard: one weapon (the rifle's stats stand in for a proper minigun for now), manual RELOAD
            if (Prefs.Vanguard) { if (!WeaponAllowed(player.weapon)) player.weapon = 0; if (inp.reload) StartReload(player.weapon); }   // the Heavy: the minigun, plus the Thermal Projector / Rail Cannon once owned
            else if (Prefs.Scout) player.weapon = ScoutWeapon;   // the Scout: Twin Needlers (the SMG) only

            // aim + move
            Vector2 move; bool firing, dashing;
            if (demo)
            {
                DemoBrain();
                move = botMove.sqrMagnitude > 0 ? botMove.normalized : Vector2.zero;
                if (run && EnemyAI.Tele.Danger(player.p, out var away)) move = away;   // the bot dodges telegraphs
                string avoid = ZoneCells2.Find<CellGrates>() is CellGrates gr && gr.On ? "*a_g" : "*a_";   // live grates only
                if (run && move != Vector2.zero && ZoneCells.At(player.p + move * 1.6f) == 'g' && avoid.IndexOf('g') >= 0 && player.dashCd <= 0) botDash = true;   // a live grate ahead: dash across (a dash takes no damage)
                else if (run && move != Vector2.zero && avoid.IndexOf(ZoneCells.At(player.p + move * 1.6f)) >= 0 && avoid.IndexOf(ZoneCells.At(player.p)) < 0)
                {   // the bot steps around slag / acid / pits / live grates
                    var l = new Vector2(-move.y, move.x);
                    move = avoid.IndexOf(ZoneCells.At(player.p + l * 1.6f)) < 0 ? l : -l;
                }
                if (run && EnemyAI.Latched > 0 && Random.value < 0.03f) botDash = true;   // shake the Ticks off
                if (run && CellLight.I != null && CellLight.I.Dark && CellLight.Flares > 0 && (botFlareT -= dt) <= 0)
                {   // the dark: a flare toward 3+ unseen enemies within 9 m
                    Vector2 sum = Vector2.zero; int n = 0;
                    foreach (var e in swarm.enemies) { var d = e.p - player.p; if (!e.dead && d.sqrMagnitude < 81f && !CellLight.I.Lit(e.p)) { sum += d; n++; } }
                    if (n >= 3) { var keep = player.vel; player.vel = Vector2.zero; player.aim = sum.normalized; CellLight.I.ThrowFlare(); player.vel = keep; botFlareT = 8f; }
                }
                if (run && goal == Goal.Burn && ZoneCells.At(player.p) == 'c' && player.dashCd <= 0) botDash = true;   // BURN: dash lines scorch
                firing = botFire; dashing = botDash; botDash = false;
                if (perfForceWeapon >= 0) player.weapon = perfForceWeapon;
                if (demoWeapon >= 0) player.weapon = demoWeapon;
            }
            else
            {
                move = ScreenToPlane(inp.move);
                if (move.magnitude < 0.12f) move = Vector2.zero;
                if (move != Vector2.zero) faceDir = move.normalized;   // face where you walk; standing still keeps the last facing
                if (inp.aim != Vector2.zero) { player.aim = ScreenToPlane(inp.aim).normalized; faceDir = player.aim; }
                else if (inp.hasMouseAim && inp.fire)
                {
                    var ray = cam.ScreenPointToRay(inp.mouseScreen);
                    if (Mathf.Abs(ray.direction.y) > 1e-4f)
                    {
                        var hit = ray.GetPoint((0.95f - ray.origin.y) / ray.direction.y);
                        var d = MazeMap.ToPlane(hit) - player.p;
                        if (d.magnitude > 0.3f) { player.aim = d.normalized; faceDir = player.aim; }
                    }
                }
                else player.aim = faceDir; // touch, not aiming: face where you walk (or last walked)
                firing = inp.fire; dashing = inp.dash;
                // auto-fire: only what's in front (a cone round the facing, in reach, clear line), held until it's gone
                player.hasTarget = false;
                if (!firing && AutoTarget(out var tp)) { player.aim = (tp - player.p).normalized; firing = player.hasTarget = true; }
            }
            player.facing = Mathf.Atan2(player.aim.x, player.aim.y);
            if (player.frozenT > 0) { player.frozenT -= dt; move = Vector2.zero; dashing = false; }   // a cryo vent caught you
            if (run) { Abilities.Tick(this, dt, skillPressed || (demo && Abilities.Cd <= 0 && BotCrowd() >= 6)); skillPressed = false; }
            if (Abilities.Rooted) { move = Vector2.zero; dashing = false; }   // Barrage plants the Vanguard

            player.dashCd -= dt;
            if (dashing && player.dashCd <= 0 && player.dashT <= 0)
            {
                player.dashT = P.dashTime; player.dashCd = Mathf.Max(0.5f, P.dashCooldown * (1 - 0.2f * mods["dashcd"]) * (run ? PowerUps.DashCdMul * (Meta.Set2On(2) ? 0.85f : 1f) : 1f) - (run ? Meta.BootsDashCut : 0));
                player.dashDir = move != Vector2.zero ? move.normalized : player.aim;
                if (run) { CardOnDash(); EnemyAI.Tele.OnDash(player.p); if (scoutStars >= 3 && !Abilities.DecoyActive(out _)) PowerUps.DecoyFor(player.p, 1f, boom: false); }   // Scout 3*: 1 s unseen (the swarm hunts where you were)
                Fx.I.Smoke(MazeMap.ToWorld(player.p, 0.2f), 6, new Color32(0xcf, 0xd8, 0xee, 255), 3, 0.4f, 0.12f, 1);
            }

            if (player.dashT > 0)
            {
                player.dashT -= dt;
                moveVel = player.dashDir * P.dashSpeed * (run && scoutStars >= 3 ? 1.4f : 1f);   // Scout 3*: dash range +40%
            }
            else
            {
                float k = 1 - Mathf.Exp(-14 * Hazards.Friction * dt);   // zone footing: ice / low gravity slide
                player.vel += (move * P.speed * cfg.weapons[player.weapon].moveMul * Hazards.MoveMul * EnemySlow - player.vel) * k;
                moveVel = player.vel;
            }
            player.p += (moveVel + Hazards.Push) * dt;
            MazeMap.CollideGrid(ref player.p, P.radius);
            barrels.Collide(ref player.p, P.radius);

            player.fireCd -= dt;
            TickReloads(dt);
            var cw = cfg.weapons[player.weapon];
            bool loaded = cw.magSize <= 0 || (!player.Reloading && player.ammo[player.weapon] > 0);
            if (firing && player.fireCd <= 0 && loaded && !holdFire) Fire();
            // top up a part-empty magazine once nothing has been in reach for a moment
            player.idleT = AnyInReach(Reach) ? 0 : player.idleT + dt;
            if (player.idleT >= P.topUpIdle) StartReload(player.weapon);
        }

        // ------------------------------------------------------------ magazines
        // Reloads run per weapon and keep going after a switch; only the weapon in hand is heard.
        // Magazines: mag out, then mag in near the end, full at the end. Shotgun: one shell per
        // (reloadTime - Rack) / magSize, then a rack to chamber; it still can't fire until done.
        const float Rack = 0.3f;

        void StartReload(int i)
        {
            var w = cfg.weapons[i];
            if (w.magSize <= 0 || player.reloadLeft[i] > 0 || player.ammo[i] >= w.magSize) return;
            float dur = w.perShell ? (w.reloadTime - Rack) / w.magSize * (w.magSize - player.ammo[i]) + Rack : w.reloadTime;
            player.reloadLeft[i] = player.reloadDur[i] = dur;
            player.reloadFrom[i] = player.ammo[i]; // 0 = ran dry (an early top-up always has rounds left)
        }

        void TickReloads(float dt)
        {
            for (int i = 0; i < cfg.weapons.Length; i++)
            {
                if (player.reloadLeft[i] <= 0) continue;
                var w = cfg.weapons[i];
                float dur = player.reloadDur[i], e0 = dur - player.reloadLeft[i], e1 = e0 + dt;
                bool heard = i == player.weapon;
                bool Cross(float at) => e0 < at && at <= e1;
                bool dry = player.reloadFrom[i] == 0;
                if (heard && dry && Cross(0.1f)) CombatAudio.I.Gear(CombatAudio.GearSound.DryClick); // the trigger on an empty gun
                if (w.perShell)
                {
                    float per = (w.reloadTime - Rack) / w.magSize;
                    int need = w.magSize - player.reloadFrom[i];
                    for (int k = 0; k < need; k++)
                        if (Cross(per * (k + 0.6f))) { player.ammo[i] = player.reloadFrom[i] + k + 1; if (heard) CombatAudio.I.Gear(CombatAudio.GearSound.ShellIn); }
                    if (heard && Cross(dur - Rack + 0.04f)) { CombatAudio.I.Gear(CombatAudio.GearSound.Rack); GunFx.I.Rack(); player.Rack(); }
                }
                else
                {
                    if (heard && Cross(dry ? 0.3f : 0.12f)) CombatAudio.I.Gear(CombatAudio.GearSound.MagOut);
                    if (heard && Cross(dur - 0.4f)) CombatAudio.I.Gear(CombatAudio.GearSound.MagIn);
                }
                player.reloadLeft[i] -= dt;
                if (player.reloadLeft[i] <= 0) { player.reloadLeft[i] = 0; player.ammo[i] = w.magSize; }
            }
        }

        bool AnyInReach(float R)
        {
            float rr = R * R;
            foreach (var e in swarm.enemies)
                if (!e.dead && e.spawnT >= 0.5f && (e.p - player.p).sqrMagnitude < rr) return true;
            return false;
        }

        // Auto-fire target: only inside the cone of the hero's facing (cfg.player.aimCone, from movement; the hero never
        // auto-turns), in reach, with a clear line of fire. The held target is kept until it dies or leaves the cone /
        // reach / line; otherwise the one nearest the facing line (then the nearer). A barrel with a crowd around it in
        // the cone wins, like the demo bot.
        // (2026-10-15, Manuel: auto-fire all round the hero) true = no facing cone: the nearest target in reach, any direction (the
        // hero turns to it while firing); false = the 30-degree forward cone as before
        public static bool AutoFireAllRound = true;
        bool InCone(Vector2 p, float cosCone) { var d = p - player.p; float m = d.magnitude; return m < 1e-3f || Vector2.Dot(d / m, faceDir) >= cosCone; }
        bool Targetable(Enemy e) => !(e.dead || e.spawnT < 0.5f || (e.Hidden && !(e.cloaked && run && mods["phase_sight"] > 0) && !(e.under && run && Meta.Relic(15))) || (e.type.ai == "tick" && e.aiState == 2)
            || (CellLight.I != null && !CellLight.I.Lit(e.p)) || (run && EnemyAI.Clouds.Count > 0 && EnemyAI.InCloud(e.p) && !EnemyAI.InCloud(player.p)));
        bool AutoTarget(out Vector2 target)
        {
            float R = Reach, cosCone = AutoFireAllRound ? -2f : Mathf.Cos(cfg.player.aimCone * Mathf.Deg2Rad);
            target = default;
            if (lockOn != null && swarm.enemies.Contains(lockOn) && Targetable(lockOn) && !lockOn.type.nest && (lockOn.p - player.p).sqrMagnitude < R * R && InCone(lockOn.p, cosCone) && ClearShot(player.p, lockOn.p)) { target = lockOn.p; return true; }
            lockOn = null;
            var b = barrels.BestTarget(player.p);
            if (b != null && (b.p - player.p).magnitude <= R && (b.p - player.p).magnitude > cfg.barrels.radius + 0.8f && InCone(b.p, cosCone) && ClearShot(player.p, b.p)) { target = b.p; return true; }   // never one that would catch the hero
            float best = float.MaxValue, bestLow = R * R; bool found = false; Vector2 low = default; bool foundLow = false; Enemy pick = null;
            foreach (var e in swarm.enemies)
            {
                if (!Targetable(e) || (e.p - player.p).sqrMagnitude >= R * R || !InCone(e.p, cosCone)) continue;
                if (e.dead || e.spawnT < 0.5f || (e.Hidden && !(e.cloaked && run && mods["phase_sight"] > 0) && !(e.under && run && Meta.Relic(15))) || (e.type.ai == "tick" && e.aiState == 2)) continue;   // cloaked / underground / latched: not targets (PHASE SIGHT sees the cloaked)
                if (CellLight.I != null && !CellLight.I.Lit(e.p)) continue;   // darkness: only lit enemies are targets
                if (run && EnemyAI.Clouds.Count > 0 && EnemyAI.InCloud(e.p) && !EnemyAI.InCloud(player.p)) continue;   // hidden in a spore cloud   // cloaked, underground, latched: not auto-targeted
                float d = (e.p - player.p).sqrMagnitude;
                if (e.type.nest)
                {   // nests, egg clusters: only when nothing that moves is in range
                    if (d < bestLow && ClearShot(player.p, e.p)) { bestLow = d; low = e.p; foundLow = true; }
                    continue;
                }
                var off = e.p - player.p; float m = Mathf.Sqrt(d), lateral = Mathf.Abs(off.x * faceDir.y - off.y * faceDir.x);
                float score = AutoFireAllRound ? m : lateral + 0.15f * m;   // all round: the nearest first; cone: nearest the facing line first, then the nearer
                if (run && mods["hunters_mark"] > 0 && IsPriority(e)) score *= 0.25f;   // HUNTER'S MARK: the dangerous ones first
                if (score < best && ClearShot(player.p, e.p)) { best = score; target = e.p; found = true; pick = e; }
            }
            if (found) { lockOn = pick; return true; }
            if (foundLow) { target = low; return true; }
            return false;
        }

        static bool ClearShot(Vector2 a, Vector2 b)
        {
            var d = b - a; int n = Mathf.CeilToInt(d.magnitude / 0.5f);
            for (int i = 1; i < n; i++) { var q = a + d * (i / (float)n); if (MazeMap.ShotSolidAt(q.x, q.y)) return false; }
            return true;
        }

        // a shotgun blast: pellets that hit Drones are gathered for one combined goo spray, and every
        // Drone they shove leaves a smear of goo along its slide
        int blastId, blastHits; float blastAge = 9; Vector2 blastSum, blastDir;
        readonly List<Enemy> blastEnemies = new(16);
        float camPunch;

        void TickBlast(float dt)
        {
            if (blastAge > 0.14f) return;
            blastAge += dt;
            if (blastAge <= 0.14f) return; // the pellets cross their 10 m range in ~0.17 s; most hits land by now
            var dirW = MazeMap.ToWorld(blastDir);
            if (blastHits >= 3) Fx.I.GooSpray(MazeMap.ToWorld(blastSum / blastHits, 0.6f), dirW, blastHits);
            foreach (var e in blastEnemies)
            {
                float k = e.k.magnitude;
                if (e.dead || k < 3f) continue;
                Fx.I.GooSmear(MazeMap.ToWorld(e.p), MazeMap.ToWorld(e.k / k), Mathf.Min(3f, k / cfg.enemy.knockbackDecay), k);
            }
            blastEnemies.Clear();
        }

        // editor screenshots (UiShot)
        [System.NonSerialized] public bool holdFire, godMode;
        [System.NonSerialized] public int demoWeapon = -1; // MazeSim: lock the demo bot to one weapon
        public float GameTime => time;
        public Enemy DebugSpawn(int type, Vector2 at) { var e = swarm.Spawn(at, Mathf.Max(3, wave), type); e.p = at; e.spawnT = 1; return e; }
        public void DebugWave(int n) { swarm.Clear(); wave = n - 1; toSpawn = 0; breakT = 0.2f; }

        // editor screenshots (UiShot): one shot with the weapon in hand, whatever the gameplay state
        public void DebugFire() { player.ammo[player.weapon] = Mathf.Max(player.ammo[player.weapon], 2); Fire(); }

        void Fire()
        {
            var w = cfg.weapons[player.weapon];
            player.fireCd = 1 / (w.rate * (run ? RunRateMul * PowerUps.RateMul * Abilities.RateMul * CardRateMul : 1f)); shotCount++;
            if (run) TalentFire();
            bool last = w.magSize > 0 && !(run && PowerUps.NoAmmo) && !(run && TV(2, 1) && Random.value < 0.2857f) && --player.ammo[player.weapon] <= 0;   // talent: +40% magazine
            CombatAudio.I.Fire(w.name, !last);
            if (last) StartReload(player.weapon);
            float f = player.facing, sf = Mathf.Sin(f), cf = Mathf.Cos(f);
            var o = player.p + new Vector2(sf * 0.9f + cf * 0.24f, cf * 0.9f - sf * 0.24f);
            // a run's cards: extra projectiles (fanned, or more pellets), pierce, ricochet, damage, range
            int multi = run ? RunMulti : 0, pellets = w.pellets > 1 ? w.pellets + 2 * multi + (run && Evolved("breacher") ? 3 : 0) : 1 + multi;
            float dmgMul = run ? RunDamageMul : 1f, range = BulletRange;
            if (w.kind == "frag") { FragShot(w.damage * dmgMul * (1 + 0.25f * multi)); pellets = 0; }   // FRAG LAUNCHER: an arcing blast, no bullets
            for (int i = 0; i < pellets && bullets.Count < MaxBullets; i++)
            {
                float spread = w.pellets > 1 ? (i / (pellets - 1f) - 0.5f) * 2 * w.spread * (1 + 0.15f * multi) + Random.Range(-0.04f, 0.04f)
                             : Random.Range(-w.spread, w.spread) + (pellets > 1 ? (i - (pellets - 1) * 0.5f) * 0.11f : 0);
                float a = f + spread;
                var b = new Bullet { p = o, d = new Vector2(Mathf.Sin(a), Mathf.Cos(a)), speed = w.speed * Random.Range(0.92f, 1.05f), w = w, dmg = w.damage * dmgMul };
                if (run) { b.pierce = RunPierce + Abilities.Pierce; b.bounce = RunBounce; b.fang = 0; }
                if (w.kind == "rail") b.pierce = 99;   // RAIL CANNON: through everything
                b.life = range / w.speed;
                b.tr = Fx.I.GetTracer(player.weapon);
                b.from = o; b.len = b.tr.localScale.z; b.width = b.tr.localScale.x; b.blast = w.pellets > 1 ? blastId + 1 : 0;
                PlaceTracer(b);
                bullets.Add(b);
            }
            if (w.pellets > 1) { blastId++; blastAge = 0; blastHits = 0; blastSum = Vector2.zero; blastEnemies.Clear(); blastDir = new Vector2(sf, cf); }
            // muzzle flash, smoke, casing, light pop and the gun's kick
            int style = GunFx.Style(w.name);
            var dirW = MazeMap.ToWorld(new Vector2(sf, cf));
            GunFx.I.Shot(style, player.gun != null ? player.MuzzleWorld : MazeMap.ToWorld(o, 0.95f), dirW, !last);
            player.Kick(style, !last);
            if (style == 2) camPunch = 0.45f; // the shotgun shoves the camera back a touch
            player.shootT = 0.35f;
            if (w.pellets > 1) { Feel.Shake(0.06f, 0.12f); if (Time.unscaledTime - lastBlastHaptic > 0.45f) { lastBlastHaptic = Time.unscaledTime; Haptics.Medium(); } }
            else if (Prefs.Vanguard) Feel.Shake(0.03f, 0.1f);   // the minigun's sustained hum (combined cap keeps it small)
            player.vel -= player.aim * (w.knockback * 0.3f); // recoil
        }

        void PlaceTracer(Bullet b)
        {
            var dir = MazeMap.ToWorld(b.d);
            // head at the bullet; the streak grows out of the muzzle instead of starting inside the hero
            float len = Mathf.Min(b.len, (b.p - b.from).magnitude + 0.25f);
            var sc = b.tr.localScale; sc.z = len;
            if (b.w.pellets > 1) sc.x = b.width * Mathf.Clamp01(b.life / (b.w.range / b.w.speed) * 1.5f); // pellets fade over their range
            b.tr.localScale = sc;
            b.tr.SetPositionAndRotation(MazeMap.ToWorld(b.p - b.d * (len * 0.5f), 0.95f), Quaternion.LookRotation(dir));
        }

        void UpdateBullets(float dt)
        {
            swarm.RebuildHash();
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                bool remove = false;
                float travel = b.speed * dt;
                b.life -= dt;
                while (travel > 0 && !remove)
                {
                    float step = Mathf.Min(0.35f, travel); travel -= step;
                    if (b.w.kind == "needler") Home(b, step / Mathf.Max(1f, b.speed));
                    b.p += b.d * step;
                    if (MazeMap.ShotSolidAt(b.p.x, b.p.y))
                    {
                        if (run && CellHiveWalls.Instance != null) CellHiveWalls.Instance.Hit(b.p, b.dmg);
                        GunFx.I.Impact(MazeMap.ToWorld(b.p - b.d * 0.2f, 0.95f), MazeMap.ToWorld(-b.d), b.w.pellets > 1 ? 2 : 5);
                        HazardAudio.Shot("sfx_hit_sparks", MazeMap.ToWorld(b.p, 0.95f), 0.35f, 4, HitKey, 0.03f);
                        remove = true; break;
                    }
                    var hb = barrels.At(b.p, 0.1f);
                    if (hb != null) { GunFx.I.Impact(MazeMap.ToWorld(b.p - b.d * 0.2f, 0.95f), MazeMap.ToWorld(-b.d), b.w.pellets > 1 ? 2 : 4); barrels.Damage(hb, b.dmg); remove = true; break; }
                    hitBullet = b; hitDone = false;
                    swarm.ForNear(b.p, bulletHitFn);
                    remove = hitDone;
                }
                if (remove || b.life <= 0)
                {
                    Fx.I.ReleaseTracer(b.tr);
                    bullets[i] = bullets[^1]; bullets.RemoveAt(bullets.Count - 1);
                }
                else PlaceTracer(b);
            }
            swarm.Compact();
            TickBlast(dt);
        }

        static readonly object HitKey = new object();   // (bullet-hit sparks: one shared 30 ms throttle)
        bool BulletHit(Enemy e)
        {
            if (e.dead || e.under && !(run && Meta.Relic(15))) return true;   // underground: bullets pass over (BURROWER FANG hits them)
            var b = hitBullet;
            float rr = e.r + 0.12f;
            if ((e.p - b.p).sqrMagnitude >= rr * rr || e == b.last) return true;
            bool crit = false;
            float dmg = run ? RunHitDamage(e, b.dmg, out crit) : b.dmg;
            if (e.frozen > 0) dmg *= 1.5f;   // frozen enemies take +50%
            if (run && CellLight.I != null && CellLight.I.Dark && CellLight.I.Exposed(e.p)) dmg *= 1.2f;   // exposed in flare / pylon light
            if (run && e.mods != 0) dmg = Elites.Damage(e, dmg, b.d);
            float front = run && PowerUps.IgnoreFront ? 1f : EnemyAI.FrontMul(e, b.d);
            if (run && front < 1f) front = Mathf.Min(1f, front + 0.25f * mods["armour_piercer"]);
            if (front < 1f) HazardAudio.Shot("sfx_hit_sparks", MazeMap.ToWorld(e.p, 0.95f), 0.35f, 4, HitKey, 0.03f);   // armour
            dmg *= front * (run ? PowerUps.HitMul(e) * CardHitMul(e) : 1f); EnemyAI.OnHit(e);
            if (run) { PowerUps.OnHit(e); CardAfterHit(e, b, dmg); }
            e.hp -= dmg; e.flash = 1;
            if (crit && !SimRun) Fx.I.Sparks(MazeMap.ToWorld(e.p, 1f), 6, new Color(1f, 0.85f, 0.2f), 4, 0.35f, 0.08f, 3);
            if (!SimRun) GunFx.I.HitEnemy(MazeMap.ToWorld(b.p, 0.95f), MazeMap.ToWorld(-b.d));   // gunfire kit: a small hit spark
            if (e.type.leaper) swarm.OnShot(e, b.d);
            float kb = b.w.knockback / Mathf.Sqrt(e.type.hp) * e.type.knockback * (run ? RunShove : 1f); // a Bloater barely budges
            if (run && Evolved("inferno") && ++infernoCount % 5 == 0) fires.Add(new FirePatch { p = e.p, t = 3f });
            e.k += b.d * kb * 3 * Hazards.KnockMul;
            if (b.blast == blastId && b.blast > 0)
            {
                blastHits++; blastSum += e.p;
                if (!blastEnemies.Contains(e) && blastEnemies.Count < 16) blastEnemies.Add(e);
            }
            if (e.view.model != null) { Fx.I.GooHit(MazeMap.ToWorld(b.p, Mathf.Min(0.5f * e.scale, 1.1f)), new Vector3(b.d.x, 0, -b.d.y), e.type.bloater); CombatAudio.I.Hit(); }
            else Fx.I.Debris(MazeMap.ToWorld(b.p, 0.9f), 2, e.type.color, 2.5f, 0.9f, 0.09f, 3);
            if (run && b.w.kind == "needler") NeedlePop(e, b);
            if (e.hp <= 0) KillEnemy(e);
            b.last = e;
            if (run && Meta.Relic(15) && (e.under || e.type.ai == "burrower") && b.fang < Mathf.RoundToInt(Meta.RelicV)) { b.fang++; return true; }   // BURROWER FANG
            if (b.pierce > 0) { b.pierce--; return true; }        // PIERCE / RAILGUN: on through
            if (b.bounce > 0)
            {   // RICOCHET / ARC SMG: on to the nearest other enemy
                b.bounce--; Enemy next = null; float nd = 7f * 7f;
                foreach (var o in swarm.enemies) { if (o.dead || o == e) continue; float d = (o.p - b.p).sqrMagnitude; if (d < nd) { nd = d; next = o; } }
                if (next != null) { b.d = (next.p - b.p).normalized; b.from = b.p; b.life = Mathf.Max(b.life, 8f / b.speed); return false; }
            }
            hitDone = true;
            return false;
        }

        void UpdateWaves(float dt)
        {
            var W = cfg.waves;
            if (toSpawn > 0)
            {
                spawnT -= dt;
                if (spawnT <= 0 && swarm.enemies.Count < W.maxAlive)
                {
                    var pts = MazeMap.spawnPoints;
                    int count = 0; Vector2 pick = pts[0];
                    foreach (var s in pts)   // random far-enough point (reservoir sampling, no allocs)
                        if ((s - player.p).magnitude > W.minSpawnDistance && Random.Range(0, ++count) == 0) pick = s;
                    if (count == 0)
                    {   // nothing far enough: use one of the two farthest
                        float best = -1, second = -1; Vector2 a = pts[0], b2 = pts[0];
                        foreach (var s in pts)
                        {
                            float d = (s - player.p).magnitude;
                            if (d > best) { second = best; b2 = a; best = d; a = s; }
                            else if (d > second) { second = d; b2 = s; }
                        }
                        pick = Random.value < 0.5f ? a : b2;
                    }
                    if (bloatersLeft > 0 && toSpawn <= nextBloaterAt)
                    { swarm.Spawn(pick, wave, swarm.BloaterType); bloatersLeft--; nextBloaterAt -= bloaterStride; }
                    else if (leapersLeft > 0 && toSpawn <= nextLeaperAt)
                    { swarm.Spawn(pick, wave, swarm.LeaperType); leapersLeft--; nextLeaperAt -= leaperStride; }
                    else swarm.Spawn(pick, wave);
                    toSpawn--; spawnT = W.spawnInterval;
                }
            }
            else if (swarm.enemies.Count == 0)
            {
                breakT -= dt;
                if (breakT <= 0) { StartWave(wave + 1); breakT = W.breakTime; }
            }
        }

        // ------------------------------------------------------------ pickups
        void SpawnPickup(Vector2 at)
        {
            var root = new GameObject("Pickup").transform;
            root.SetParent(transform, false);
            AddCube(root, Vector3.zero, Vector3.one * 0.55f, pickupMat, true);
            AddCube(root, Vector3.up * 0.3f, new Vector3(0.4f, 0.1f, 0.12f), blueMat, false);
            AddCube(root, Vector3.up * 0.3f, new Vector3(0.12f, 0.1f, 0.4f), blueMat, false);
            root.position = MazeMap.ToWorld(at, 0.5f);
            pickups.Add(new Pickup { p = at, tr = root });
        }

        void AddCube(Transform parent, Vector3 pos, Vector3 size, Material m, bool shadow)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        void UpdatePickups(float dt)
        {
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var p = pickups[i]; p.t += dt;
                p.tr.position = MazeMap.ToWorld(p.p, 0.55f + Mathf.Sin(p.t * 3) * 0.12f);
                p.tr.rotation = Quaternion.Euler(0, p.t * 115f, 0);
                bool taken = (p.p - player.p).magnitude < 1.1f;
                if (taken)
                {
                    player.hp = Mathf.Min(player.maxHp, player.hp + cfg.pickups.heal);
                    Fx.I.Sparks(MazeMap.ToWorld(p.p, 0.6f), 10, new Color(0.28f, 0.7f, 1f), 3, 0.6f, 0.1f);
                }
                if (taken || p.t > cfg.pickups.life) { Destroy(p.tr.gameObject); pickups.RemoveAt(i); }
            }
        }

        // ------------------------------------------------------------ demo autopilot (G)
        void DemoBrain()
        {
            Enemy near = null; float nd = 1e9f; Vector2 c = Vector2.zero; int cn = 0;
            foreach (var e in swarm.enemies)
            {
                if (MazeMap.Escape && MazeMap.RoomAt(e.p) >= 0 && MazeMap.RoomAt(e.p) != escape.Cur) continue; // sealed behind a door
                float d = (e.p - player.p).magnitude;
                if (d < nd) { nd = d; near = e; }
                if (d < 7) { c += e.p; cn++; }
            }
            botFire = false; botMove = Vector2.zero;
            if (near != null)
            {
                player.aim = (near.p - player.p).normalized;
                botFire = nd < 26;
                player.weapon = cn > 5 ? 1 : nd < 12 ? 0 : 2;
            }
            var target = barrels.BestTarget(player.p);
            if (target != null) { player.aim = (target.p - player.p).normalized; botFire = true; player.weapon = 0; }
            // like a player would: don't wait out a reload while another weapon is loaded
            for (int i = 0; i < cfg.weapons.Length && player.reloadLeft[player.weapon] > 0; i++)
                if (player.reloadLeft[i] <= 0) player.weapon = i;
            if (Prefs.Vanguard && !WeaponAllowed(player.weapon)) player.weapon = 0; // the Heavy: his own weapons only
            else if (Prefs.Scout) player.weapon = ScoutWeapon;
            if (cn > 0)
            {
                botMove = player.p - c / cn;
                if (cn > 8 && Random.value < 0.05f) botDash = true;
                if (run && goal == Goal.Burn && ZoneCells.At(player.p) == 'c' && Random.value < 0.08f) botDash = true;   // BURN: dash lines scorch
            }
            else if (MazeMap.Escape && (near == null || nd > 14) && escape.Cleared) botMove = escape.BotDir(player.p) + BotUnstick(); // on to the next room
            else if (near != null && nd > (run || Prefs.Vanguard ? Reach - 1.5f : 18)) botMove = near.p - player.p; // hunt stragglers (a run: into reach)
            else if (MazeMap.Escape) botMove = near != null ? (near.p - player.p) * 0.3f : Vector2.zero;
            else
            {
                botMove = MazeMap.playerSpawn - player.p; // drift back to the middle
                if (botMove.magnitude < 3) botMove = Vector2.zero;
            }
            // a run's floor goal: head for the lift / the zone / the next nest (fighting on the way)
            if (run)
            {
                var gd = BotGoalDir();
                if (gd.HasValue && gd.Value.sqrMagnitude < 0.01f) gd = ViaJumpPad(GoalAt) ?? gd;   // the goal is past pits: a jump pad
                if (gd.HasValue && gd.Value.sqrMagnitude > 0.01f && BotWallAhead(gd.Value, out var wallAt)) { player.aim = (wallAt - player.p).normalized; botFire = true; gd = Vector2.zero; }   // shoot the hive wall open
                if (gd.HasValue && gd.Value.sqrMagnitude > 0.01f) botMove = gd.Value.normalized * 1.2f + (cn > 0 ? (player.p - c / cn).normalized * 0.5f : Vector2.zero) + BotUnstick();
                var gt = BotGoalTarget();
                if (gt != null && (gt.p - player.p).magnitude < Reach && (near == null || nd > 4f)) { player.aim = (gt.p - player.p).normalized; botFire = true; }
            }
            if (run && goal != Goal.Hold && CryoHeroTurret.BotWants(this, out var tdir)) botMove = tdir;   // the Cryo floors' turrets: go and bring one online
            // a Leaper's line locked on the hero: step sideways out of it (and dash once it's about to go)
            foreach (var e in swarm.enemies)
            {
                if (!e.type.leaper || e.leap != 1 || e.leapT < cfg.leaper.lockAt - 0.12f) continue;
                var rel = player.p - e.p; float along = Vector2.Dot(rel, e.leapDir);
                if (along < 0 || along > cfg.leaper.distance + 1 || Mathf.Abs(e.leapDir.x * rel.y - e.leapDir.y * rel.x) > 1.4f) continue;
                var side = new Vector2(-e.leapDir.y, e.leapDir.x); if (Vector2.Dot(side, rel) < 0) side = -side;
                botMove = side; if (player.dashCd <= 0 && e.leapT > cfg.leaper.crouch - 0.2f) botDash = true;
                break;
            }
        }

        // the demo bot pinned against something: shuffle sideways for a moment
        Vector2 botLastP; float botStillT, botSideT; Vector2 botSide;
        Vector2 BotUnstick()
        {
            float dt = 1f / 30f;
            if ((player.p - botLastP).sqrMagnitude < 0.0004f) botStillT += dt; else botStillT = 0;
            botLastP = player.p;
            if (botStillT > 0.6f && botSideT <= 0) { botSideT = 0.6f; botSide = Random.insideUnitCircle.normalized; }
            botSideT -= dt;
            return botSideT > 0 ? botSide * 1.2f : Vector2.zero;
        }

        // ------------------------------------------------------------ camera
        void UpdateCamera(float dt)
        {
            var c = cfg.camera;
            camPunch *= Mathf.Exp(-dt * 16f);
            var t = player.p + player.aim * c.lead;
            camTarget += (t - camTarget) * (1 - Mathf.Exp(-c.follow * dt));
            float pitch = c.pitch * Mathf.Deg2Rad, yaw = c.yaw * Mathf.Deg2Rad;
            var off = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * Mathf.Cos(pitch) * c.distance;
            cam.transform.position = MazeMap.ToWorld(camTarget + off, Mathf.Sin(pitch) * c.distance);
            Biome.Follow(MazeMap.ToWorld(camTarget));
            float roll = 0; var so = state != State.Paused ? Feel.ShakeOffset(out roll) : Vector3.zero;
            cam.transform.position += so;
            cam.transform.LookAt(MazeMap.ToWorld(camTarget) + so);
            if (roll != 0) cam.transform.Rotate(0, 0, roll, Space.Self);
            if (state != State.Paused) cam.transform.position -= MazeMap.ToWorld(player.aim) * camPunch; // short shove opposite the shot
            cam.fieldOfView = c.fov * (1f - Feel.ZoomNow);   // the boss-kill push-in
        }
    }
}
