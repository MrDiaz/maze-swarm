using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Maze
{
    // The model test arena (Assets/Maze/Scenes/Sandbox.unity, menu Maze > Open Sandbox, or SANDBOX on the
    // phone's main menu). An empty lit arena in the game's style with a 1 m grid, an orbit camera, and a holo
    // side panel to spawn models (the game's own enemies and hero, plus any GLB dropped into
    // "Maze Swarm/Sandbox/Drop/", loaded at runtime with glTFast), play their clips, show skeleton/wireframe,
    // walk them speed-matched like the game, fire the hero at them and trigger the game's effects.
    // See Sandbox/README.txt.
    public class Sandbox : MonoBehaviour
    {
        public static Sandbox I;
        readonly MazeConfig cfg = new MazeConfig();

        // ---------------------------------------------------------------- catalogue
        public enum Kind { Drone, Bloater, Mini, Hero, Drop, Leaper }
        public class Entry { public string name, path; public Kind kind; }
        public readonly List<Entry> entries = new();

        public class Spawned
        {
            public Entry entry;
            public Transform root, model;      // root: position/yaw/scale; model: the loaded hierarchy
            public Animation anim;
            public readonly List<AnimationState> clips = new();
            public int clip; public bool paused, loop = true;
            public float baseScale = 1, scale = 1, lift, stride = 1;   // stride: ground per clip-second at scale 1
            public int tris, bones;
            public Transform belly;
            public Renderer[] renderers;
            public Vector3 home, walkTarget; public float walkAngle; public bool hasTarget;
            public float hideT = -1, swellT = -1, swellDur, squashT = -1, hopT = 1; public Vector3 hopFrom, hopTo;
            public bool hidden, castBurst, dieAfterSwell;
            public string note;
            // skeleton + wireframe overlays
            public List<(Transform a, Transform b, LineRenderer lr)> bonesLines;
            public List<(Renderer r, Mesh src, Mesh wire, Transform t)> wires;
        }
        public readonly List<Spawned> spawned = new();
        public Spawned sel;
        public bool Busy { get; private set; } // a drop model is loading

        // ---------------------------------------------------------------- state
        public enum Walk { Off, Circle, Follow }
        public Walk walk = Walk.Off;
        public float walkSpeed = 2f, animSpeed = 1f;
        public bool showSkeleton, showWire, heroFire;
        Player hero; int heroWeapon; float heroCd;
        class Bullet { public Transform tr; public Vector3 p, d; public float left, speed, len; public Weapon w; public Spawned target; }
        readonly List<Bullet> bullets = new();
        Camera cam; float yaw = -14, pitch = 38, dist = 14, targetDist = 14; Vector3 pivot, pivotGoal;
        bool gameCam;
        Material lineMat, wireMat, miniMat;
        public static string DropDir;
        public float clock; // sandbox time (the editor capture waits on it)

        // ---------------------------------------------------------------- boot
        void Awake() { I = this; Prefs.ApplyFrameRate(); }

        void Start()
        {
            SetupLightsAndCamera();
            BuildArena();
            Fx.I = null;
            new GameObject("Fx").AddComponent<Fx>().Init(cfg.weapons);
            new GameObject("CombatAudio").AddComponent<CombatAudio>().Init();
            Prefs.ApplySound();
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var colored = Shader.Find("Hidden/Internal-Colored");
            lineMat = colored != null ? new Material(colored) : Mats.Unlit(Holo.Acid);
            if (colored != null) { lineMat.SetInt("_ZTest", (int)CompareFunction.Always); lineMat.SetInt("_ZWrite", 0); lineMat.SetColor("_Color", Holo.Acid); }
            wireMat = colored != null ? new Material(colored) : Mats.Unlit(Holo.Cyan);
            if (colored != null) { wireMat.SetInt("_ZTest", (int)CompareFunction.Always); wireMat.SetInt("_ZWrite", 0); wireMat.SetColor("_Color", Holo.A(Holo.Cyan, 0.4f)); /* x-ray: lines on the surface lose the depth test */ wireMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); wireMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); }
            Catalogue();
            BuildUi();
            Debug.Log($"Maze sandbox: {entries.Count} models, drop folder {(DropSupported ? DropDir ?? "not found" : "n/a on this device")}, skeleton shader {(lineMat.shader.name)}");
        }

        void SetupLightsAndCamera()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.69f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.6f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.44f, 0.47f, 0.55f);
            RenderSettings.fog = false; RenderSettings.skybox = null;
            var sgo = new GameObject("Sun");
            var sun = sgo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color32(0xff, 0xf6, 0xea, 255); sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f;
            sun.transform.rotation = Quaternion.LookRotation(-new Vector3(20, 34, 18));
            RenderSettings.sun = sun;
            cam = Camera.main;
            if (cam == null) { var cg = new GameObject("Main Camera") { tag = "MainCamera" }; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(0xe3, 0xe6, 0xeb, 255);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 400; cam.fieldOfView = cfg.camera.fov;
        }

        // the game's materials: concrete floor + a 1 m grid, sided walls with trim, crates and barrels for scale
        void BuildArena()
        {
            const float S = 36;
            var root = new GameObject("Arena").transform;
            var floor = Box(root, "Floor", new Vector3(0, -0.05f, 0), new Vector3(S, 0.1f, S), Mats.Lit(Color.white, ProcTex.Concrete(), new Vector2(4, 4), 0.05f));
            var gridTex = Holo.Tex(128, 128, (x, y) =>
            {
                bool major = x < 3 || y < 3;
                return major ? new Color(0.1f, 0.2f, 0.4f, 0.45f) : new Color(0, 0, 0, 0);
            }, true);
            gridTex.wrapMode = TextureWrapMode.Repeat; gridTex.filterMode = FilterMode.Trilinear; gridTex.anisoLevel = 4;
            var gm = Mats.Fade(Color.white, gridTex); gm.SetTextureScale("_BaseMap", new Vector2(S, S)); gm.SetTextureOffset("_BaseMap", new Vector2(0.5f / 128 * 1.5f, 0.5f / 128 * 1.5f));
            var grid = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(grid.GetComponent<Collider>());
            grid.name = "Grid1m"; grid.transform.SetParent(root, false);
            grid.transform.SetPositionAndRotation(new Vector3(0, 0.005f, 0), Quaternion.Euler(90, 0, 0)); grid.transform.localScale = new Vector3(S, S, 1);
            grid.GetComponent<MeshRenderer>().sharedMaterial = gm; grid.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var side = Mats.Lit(Color.white, ProcTex.Siding(), new Vector2(4, 4), 0.18f);
            var trim = Mats.Lit(Color.white, ProcTex.Trim(), new Vector2(2, 1), 0.45f, 0.15f);
            for (int i = 0; i < 4; i++)
            {
                var r = Quaternion.Euler(0, 90 * i, 0);
                var w = Box(root, "Wall" + i, r * new Vector3(0, 1.3f, S / 2 + 0.4f), r * new Vector3(S + 1.6f, 2.6f, 0.8f), side);
                Box(root, "Trim" + i, r * new Vector3(0, 0.15f, S / 2 - 0.02f), r * new Vector3(S, 0.3f, 0.06f), trim);
                for (int k = 0; k < 3; k++) w.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            }
            var crate = Mats.Lit(Color.white, ProcTex.Crate(), new Vector2(2, 1.4f), 0.1f);
            Box(root, "Crate", new Vector3(-9, 0.7f, 6), Vector3.one * 1.4f, crate);
            Box(root, "Crate", new Vector3(-7.4f, 0.7f, 6.3f), Vector3.one * 1.4f, crate);
            Box(root, "Crate", new Vector3(-8.2f, 2.1f, 6.1f), Vector3.one * 1.4f, crate);
            Box(root, "Crate", new Vector3(10, 0.7f, -8), Vector3.one * 1.4f, crate);
            var barrel = Resources.Load<GameObject>("Models/barrel");
            if (barrel != null)
                foreach (var p in new[] { new Vector3(8, 0, 7), new Vector3(8.9f, 0, 7.3f), new Vector3(-10, 0, -7) })
                {
                    var b = Instantiate(barrel, root); Mats.ConvertGltf(b);
                    var bb = Bounds(b.transform);
                    float s = 1.35f / Mathf.Max(0.01f, bb.size.y);
                    b.transform.localScale *= s; b.transform.position = p + Vector3.up * (-(bb.min.y - b.transform.position.y) * s);
                }
        }

        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(go.GetComponent<Collider>());
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        static Bounds Bounds(Transform t)
        {
            var b = new Bounds(t.position, Vector3.zero); bool first = true;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            return b;
        }

        // ---------------------------------------------------------------- catalogue: built-in + drop folder
        public static bool DropSupported => !Application.isMobilePlatform;

        void Catalogue()
        {
            entries.Clear();
            entries.Add(new Entry { name = "DRONE", kind = Kind.Drone });
            entries.Add(new Entry { name = "BLOATER", kind = Kind.Bloater });
            entries.Add(new Entry { name = "MINI", kind = Kind.Mini });
            entries.Add(new Entry { name = "LEAPER", kind = Kind.Leaper });
            entries.Add(new Entry { name = "ARCTIC SENTINEL (HERO)", kind = Kind.Hero });
            entries.Add(new Entry { name = "IRON VANGUARD (HEAVY)", kind = Kind.Hero });
            if (!DropSupported) return;
            DropDir = FindDrop();
            if (DropDir == null) return;
            foreach (var f in Directory.GetFiles(DropDir))
            {
                var ext = Path.GetExtension(f).ToLower();
                if (ext == ".glb" || ext == ".gltf") entries.Add(new Entry { name = Path.GetFileName(f), path = f, kind = Kind.Drop });
            }
        }

        // "<project>/Sandbox/Drop" from the editor (Assets/..) or from a Mac build inside the project's Builds folder
        static string FindDrop()
        {
            var d = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "Sandbox", "Drop");
                if (Directory.Exists(p)) return p;
            }
            return null;
        }

        public void Rescan() { Catalogue(); RefreshList(); }

        // ---------------------------------------------------------------- spawning
        public async Task<Spawned> Spawn(Entry e, Vector3 at)
        {
            Transform model = null; string note = null;
            float baseScale = 1, stride = 1;
            var root = new GameObject(e.name).transform;
            root.position = at;
            if (e.kind == Kind.Drop)
            {
                Busy = true; status = "LOADING " + e.name + "...";
                try
                {
                    var imp = new GLTFast.GltfImport();
                    var settings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.Legacy };
                    bool ok = await imp.Load(e.path, settings);
                    var holder = new GameObject("Model").transform; holder.SetParent(root, false);
                    if (ok) ok = await imp.InstantiateMainSceneAsync(holder);
                    if (!ok) { status = "COULD NOT LOAD " + e.name; Destroy(root.gameObject); Busy = false; return null; }
                    model = holder;
                    Mats.ConvertGltf(holder.gameObject);
                }
                finally { Busy = false; }
            }
            else
            {
                var res = e.kind == Kind.Bloater ? "bloater" : e.kind == Kind.Leaper ? "leaper" : e.kind == Kind.Hero ? (e.name.Contains("VANGUARD") ? "hero_vanguard" : "hero") : "drone";
                var prefab = Resources.Load<GameObject>("Models/" + res);
                if (prefab == null) { status = "MISSING Models/" + res; Destroy(root.gameObject); return null; }
                var go = Instantiate(prefab, root); Mats.ConvertGltf(go);
                model = go.transform;
            }
            var sp = new Spawned { entry = e, root = root, model = model };
            sp.renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var r in sp.renderers) { r.shadowCastingMode = ShadowCastingMode.On; if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; }

            // game size, like Swarm: footprint 2.4 x enemy radius x type scale; the hero is 1.6 m tall; drops as authored
            var smr0 = model.GetComponentInChildren<SkinnedMeshRenderer>();
            bool enemy = e.kind == Kind.Drone || e.kind == Kind.Bloater || e.kind == Kind.Mini || e.kind == Kind.Leaper;
            var mb = enemy && smr0 != null ? smr0.sharedMesh.bounds : LocalBounds(model); // enemies: bind-pose bounds, as Swarm
            if (enemy)
            {
                var t = cfg.enemyTypes[e.kind == Kind.Bloater ? TypeIdx(x => x.bloater) : e.kind == Kind.Mini ? TypeIdx(x => x.mini) : e.kind == Kind.Leaper ? TypeIdx(x => x.leaper) : 0];
                baseScale = 2.4f * cfg.enemy.radius / Mathf.Max(0.01f, Mathf.Max(mb.size.x, mb.size.z)) * t.scale;
                stride = t.stride > 0 ? t.stride : 1.25f; // the same speed match as Swarm.SyncModel
            }
            else if (e.kind == Kind.Hero) { baseScale = cfg.player.modelHeight / Mathf.Max(0.01f, mb.size.y); stride = 1.1f; }
            sp.baseScale = baseScale; sp.stride = stride;
            sp.lift = mb.min.y < -0.02f || e.kind == Kind.Drop ? -mb.min.y : 0;
            model.localPosition = new Vector3(0, sp.lift, 0);
            if (e.kind == Kind.Mini)
            {
                if (miniMat == null && sp.renderers.Length > 0)
                {
                    miniMat = new Material(sp.renderers[0].sharedMaterial) { name = "MiniDrone" };
                    miniMat.SetColor("_BaseColor", miniMat.GetColor("_BaseColor") * new Color(0.62f, 1.15f, 0.5f));
                }
                foreach (var r in sp.renderers) r.sharedMaterial = miniMat;
            }
            foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Belly") sp.belly = t;

            // stats + clips
            var bones = new HashSet<Transform>();
            foreach (var r in sp.renderers)
            {
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m != null) sp.tris += TriCount(m);
                if (r is SkinnedMeshRenderer s2) foreach (var b in s2.bones) if (b != null) bones.Add(b);
            }
            sp.bones = bones.Count;
            sp.anim = model.GetComponentInChildren<Animation>();
            if (sp.anim != null) { foreach (AnimationState st in sp.anim) sp.clips.Add(st); sp.anim.Stop(); }
            sp.clip = Mathf.Max(0, sp.clips.FindIndex(c => c.name.ToLower().Contains("walk")));
            var notes = new List<string>();
            if (sp.bones == 0) notes.Add("NO RIG");
            if (sp.clips.Count == 0) notes.Add("NO ANIMATIONS");
            sp.note = notes.Count > 0 ? string.Join(" / ", notes) : null;
            sp.home = at;
            ApplyScale(sp);
            if (sp.clips.Count > 0) PlayClip(sp, sp.clip);
            spawned.Add(sp);
            Select(sp);
            status = e.name + (sp.note != null ? "  //  " + sp.note : "  //  READY");
            return sp;
        }

        static int TriCount(Mesh m) { int n = 0; for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i) / 3; return n; }
        int TypeIdx(System.Func<EnemyType, bool> f) { for (int i = 0; i < cfg.enemyTypes.Length; i++) if (f(cfg.enemyTypes[i])) return i; return 0; }

        static Bounds LocalBounds(Transform model)
        {   // bounds of all renderers in the model's local space (unscaled hierarchy)
            var b = new Bounds(); bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var wb = r.bounds;
                var c = model.InverseTransformPoint(wb.center); var sz = model.InverseTransformVector(wb.size);
                var lb = new Bounds(c, new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z)));
                if (first) { b = lb; first = false; } else b.Encapsulate(lb);
            }
            return b;
        }

        public void ApplyScale(Spawned s) => s.root.localScale = Vector3.one * (s.baseScale * s.scale);

        public void Select(Spawned s)
        {
            sel = s;
            if (s != null) pivotGoal = s.root.position + Vector3.up * 0.8f * s.baseScale * s.scale;
            RefreshSelected();
        }

        public void Delete(Spawned s)
        {
            if (s == null) return;
            ClearOverlays(s);
            Destroy(s.root.gameObject);
            spawned.Remove(s);
            foreach (var b in bullets) if (b.target == s) b.target = null;
            Select(spawned.Count > 0 ? spawned[^1] : null);
        }

        public void ClearAll()
        {
            foreach (var s in spawned.ToArray()) { ClearOverlays(s); Destroy(s.root.gameObject); }
            spawned.Clear();
            if (hero != null) { Destroy(hero.gameObject); hero = null; heroFire = false; }
            foreach (var b in bullets) Fx.I.ReleaseTracer(b.tr);
            bullets.Clear();
            Fx.I.ClearAll();
            Select(null);
        }

        Vector3 FreeSpot()
        {
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3((i % 5 - 2) * 4f, 0, -(i / 5) * 4f + 2);
                bool used = false;
                foreach (var s in spawned) if ((s.root.position - p).sqrMagnitude < 6) used = true;
                if (!used) return p;
            }
            return Vector3.zero;
        }
        public Task<Spawned> SpawnEntry(Entry e) => Spawn(e, FreeSpot());

        // a raw drop next to its built game version, both scaled to the same footprint
        public async Task Compare(Entry raw)
        {
            if (raw == null || raw.kind != Kind.Drop) { status = "PICK A DROPPED GLB TO COMPARE"; return; }
            string n = raw.name.ToLower();
            var kind = n.Contains("bloat") ? Kind.Bloater : n.Contains("drone") || n.Contains("spider") ? Kind.Drone : n.Contains("hero") || n.Contains("wanderer") ? Kind.Hero : (Kind)(-1);
            if ((int)kind < 0) { status = "NO GAME VERSION MATCHES " + raw.name; return; }
            var built = entries.Find(x => x.kind == kind);
            var a = await Spawn(raw, new Vector3(-2.2f, 0, 4));
            var b = await Spawn(built, new Vector3(2.2f, 0, 4));
            if (a == null || b == null) return;
            float wa = Footprint(a), wb = Footprint(b);
            if (wa > 0.01f) { a.scale = wb / wa; ApplyScale(a); }
            Select(b);
            pivotGoal = new Vector3(0, 0.8f, 4);
            status = "COMPARE  //  LEFT: RAW " + raw.name + "   RIGHT: BUILT " + built.name;
        }
        static float Footprint(Spawned s) { var b = Bounds(s.root); return Mathf.Max(b.size.x, b.size.z); }

        // ---------------------------------------------------------------- clips
        public void PlayClip(Spawned s, int i)
        {
            if (s == null || s.anim == null || s.clips.Count == 0) return;
            s.clip = Mathf.Clamp(i, 0, s.clips.Count - 1);
            var st = s.clips[s.clip];
            foreach (var c in s.clips) if (c != st) { c.enabled = false; c.weight = 0; }
            st.wrapMode = s.loop ? WrapMode.Loop : WrapMode.ClampForever;
            st.enabled = true; st.weight = 1;
            s.anim.Play(st.name);
            st.speed = s.paused ? 0 : animSpeed;
        }
        public void TogglePause() { if (sel == null) return; sel.paused = !sel.paused; }
        public void ToggleLoop() { if (sel == null) return; sel.loop = !sel.loop; PlayClip(sel, sel.clip); }
        public void Step(int frames)
        {
            if (sel?.anim == null || sel.clips.Count == 0) return;
            sel.paused = true;
            var st = sel.clips[sel.clip];
            st.speed = 0;
            float fr = st.clip != null && st.clip.frameRate > 0 ? st.clip.frameRate : 30;
            st.time = Mathf.Repeat(st.time + frames / fr, st.length);
            sel.anim.Sample();
        }

        // ---------------------------------------------------------------- effects (the game's own Fx)
        public enum Effect { DroneDeath, SacBurst, Minis, BloaterDeath, Hit }
        public void Trigger(Effect fx)
        {
            var s = sel;
            if (s == null) { status = "SPAWN A MODEL FIRST"; return; }
            var center = s.root.position + Vector3.up * (0.45f * s.baseScale * s.scale);
            float size = s.baseScale * s.scale / Mathf.Max(0.01f, BaseFit(s));
            switch (fx)
            {
                case Effect.DroneDeath:
                    s.swellT = 0; s.swellDur = 0.1f; s.dieAfterSwell = true; s.castBurst = false;
                    break;
                case Effect.SacBurst:
                    s.swellT = 0; s.swellDur = cfg.bloater.castTime; s.castBurst = true; s.dieAfterSwell = false;
                    break;
                case Effect.Minis:
                    _ = Minis(s, 3, false);
                    break;
                case Effect.BloaterDeath:
                    s.swellT = 0; s.swellDur = 0.25f; s.dieAfterSwell = true; s.castBurst = false;
                    break;
                case Effect.Hit:
                    var fwd = s.root.forward;
                    bool pus = s.entry.kind == Kind.Bloater;
                    if (s.entry.kind == Kind.Drop || s.entry.kind == Kind.Hero) GunFx.I.Impact(center + fwd * 0.3f, fwd, 6);
                    else { for (int i = 0; i < 3; i++) Fx.I.GooHit(center + Random.insideUnitSphere * 0.2f, fwd, pus); CombatAudio.I.Hit(); }
                    break;
            }
        }
        float BaseFit(Spawned s) => s.entry.kind == Kind.Bloater ? cfg.enemyTypes[TypeIdx(x => x.bloater)].scale : s.entry.kind == Kind.Mini ? cfg.enemyTypes[TypeIdx(x => x.mini)].scale : 1;

        void TickEffects(Spawned s, float dt)
        {
            float typeScale = BaseFit(s), sc = s.baseScale * s.scale;
            var center = s.root.position + Vector3.up * (0.45f * sc);
            if (s.swellT >= 0)
            {
                s.swellT += dt;
                float k = Mathf.Clamp01(s.swellT / s.swellDur), sw = 1 - (1 - k) * (1 - k);
                if (s.castBurst && s.belly != null) { }    // the belly swells in LateUpdate
                else
                {
                    float g = 1 + (s.entry.kind == Kind.Bloater ? 0.18f : 0.25f) * sw;
                    s.root.localScale = new Vector3(sc * g, sc * g * (1 + 0.08f * sw), sc * g);
                    s.model.localRotation = Quaternion.Euler(Random.Range(-5f, 5f) * sw, 0, Random.Range(-5f, 5f) * sw);
                }
                if (s.swellT >= s.swellDur)
                {
                    s.swellT = -1; s.model.localRotation = Quaternion.identity; ApplyScale(s);
                    var fwd = s.root.forward;
                    if (s.castBurst)
                    {
                        var at = s.root.position + fwd * (0.5f * sc) + Vector3.up * 0.9f;
                        Fx.I.PusSplat(at, typeScale, fwd);
                        GunFx.I.Ring(new Vector3(at.x, 0.06f, at.z), 2.4f * cfg.enemy.radius * typeScale, new Color(0.6f, 1f, 0.25f, 0.85f), 0.22f);
                        CombatAudio.I.BloaterSpawn();
                        s.squashT = 0;
                        _ = Minis(s, Random.Range(2, 4), false);
                    }
                    else if (s.dieAfterSwell)
                    {
                        if (s.entry.kind == Kind.Bloater)
                        {
                            Fx.I.PusBurst(s.root.position + Vector3.up * (0.35f * typeScale), typeScale);
                            CombatAudio.I.BloaterBurst();
                            _ = Minis(s, Random.Range(3, 5), true);
                        }
                        else { Fx.I.GooBurst(center, typeScale); CombatAudio.I.Squish(); }
                        Hide(s, true); s.hideT = 1.6f;
                    }
                }
            }
            if (s.hideT >= 0) { s.hideT -= dt; if (s.hideT < 0) Hide(s, false); }
            if (s.squashT >= 0)
            {
                s.squashT += dt;
                float t = s.squashT, y = t < 0.07f ? Mathf.Lerp(1, 0.9f, t / 0.07f) : t < 0.17f ? Mathf.Lerp(0.9f, 1.1f, (t - 0.07f) / 0.1f) : Mathf.Lerp(1.1f, 1, (t - 0.17f) / 0.18f);
                if (t > 0.35f) { s.squashT = -1; y = 1; }
                float xz = 1 / Mathf.Sqrt(y);
                s.root.localScale = new Vector3(sc * xz, sc * y, sc * xz);
            }
            if (s.hopT < 1)
            {
                s.hopT = Mathf.Min(1, s.hopT + dt / 0.45f);
                var p = Vector3.Lerp(s.hopFrom, s.hopTo, s.hopT); p.y = 4 * 0.8f * s.hopT * (1 - s.hopT);
                s.root.position = p;
                if (s.hopT >= 1) s.home = s.root.position;
            }
        }

        void Hide(Spawned s, bool h) { s.hidden = h; foreach (var r in s.renderers) if (r != null) r.enabled = !h; }

        async Task Minis(Spawned from, int n, bool death)
        {
            var mini = entries.Find(x => x.kind == Kind.Mini);
            var fwd = from.root.forward;
            var at = from.root.position + fwd * (0.4f * from.baseScale * from.scale);
            var keep = sel;
            for (int i = 0; i < n; i++)
            {
                float a = (n == 1 ? 0 : (i / (n - 1f) - 0.5f) * (death ? 4.4f : 1.6f)) * Mathf.Rad2Deg;
                var d = Quaternion.Euler(0, a, 0) * fwd;
                var m = await Spawn(mini, at);
                if (m == null) continue;
                m.hopFrom = at; m.hopTo = at + d * Random.Range(1.8f, 2.6f); m.hopT = 0;
                m.root.rotation = Quaternion.LookRotation(d);
            }
            Select(keep);
        }

        // ---------------------------------------------------------------- hero (fires at the selection)
        public void SpawnHero()
        {
            if (hero != null) { Destroy(hero.gameObject); hero = null; heroFire = false; status = "HERO REMOVED"; RefreshSelected(); return; }
            hero = new GameObject("Hero").AddComponent<Player>();
            hero.Init(cfg);
            var near = sel != null ? sel.root.position : Vector3.zero;
            var p = near + new Vector3(-7, 0, -2);
            hero.p = MazeMap.ToPlane(p); hero.weapon = heroWeapon; hero.aim = new Vector2(1, 0);
            hero.facing = Mathf.PI / 2;
            status = "HERO READY  //  FIRE TO SHOOT AT THE SELECTED MODEL";
            RefreshSelected();
        }
        public void CycleWeapon() { heroWeapon = (heroWeapon + 1) % cfg.weapons.Length; if (hero != null) hero.weapon = heroWeapon; RefreshSelected(); }
        public string WeaponName => cfg.weapons[heroWeapon].name;

        void TickHero(float dt)
        {
            if (hero == null) return;
            hero.weapon = heroWeapon;
            Vector3 target = sel != null && !sel.hidden ? sel.root.position + Vector3.up * (0.45f * sel.baseScale * sel.scale) : MazeMap.ToWorld(hero.p + hero.aim * 5, 0.95f);
            var to = MazeMap.ToPlane(target) - hero.p;
            if (to.sqrMagnitude > 0.01f) { hero.aim = to.normalized; hero.facing = Mathf.Atan2(hero.aim.x, hero.aim.y); }
            heroCd -= dt;
            if (heroFire && sel != null && !sel.hidden && heroCd <= 0) HeroShot(target);
            hero.hurtT -= dt; hero.shootT -= dt;
            hero.Visuals(dt, cfg, Vector2.zero, false);
        }

        void HeroShot(Vector3 target)
        {
            var w = cfg.weapons[heroWeapon];
            heroCd = 1 / w.rate;
            CombatAudio.I.Fire(w.name);
            var muzzle = hero.gun != null ? hero.MuzzleWorld : MazeMap.ToWorld(hero.p + hero.aim * 0.9f, 0.95f);
            var flat = MazeMap.ToWorld(hero.aim);
            GunFx.I.Shot(GunFx.Style(w.name), muzzle, flat);
            hero.Kick(GunFx.Style(w.name)); hero.shootT = 0.35f;
            for (int i = 0; i < w.pellets; i++)
            {
                float spread = w.pellets > 1 ? (i / (w.pellets - 1f) - 0.5f) * 2 * w.spread + Random.Range(-0.04f, 0.04f) : Random.Range(-w.spread, w.spread);
                var d = Quaternion.Euler(0, spread * Mathf.Rad2Deg, 0) * (target - muzzle).normalized;
                var tr = Fx.I.GetTracer(heroWeapon);
                bullets.Add(new Bullet { tr = tr, p = muzzle, d = d, left = (target - muzzle).magnitude, speed = w.speed, len = tr.localScale.z, w = w, target = sel });
            }
        }

        void TickBullets(float dt)
        {
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                float step = b.speed * dt; b.left -= step; b.p += b.d * step;
                float len = Mathf.Min(b.len, 1.5f);
                var sc = b.tr.localScale; sc.z = len; b.tr.localScale = sc;
                b.tr.SetPositionAndRotation(b.p - b.d * (len * 0.5f), Quaternion.LookRotation(b.d));
                if (b.left > 0) continue;
                var s = b.target;
                if (s != null && !s.hidden)
                {
                    if (s.entry.kind == Kind.Drop || s.entry.kind == Kind.Hero) GunFx.I.Impact(b.p, -b.d, b.w.pellets > 1 ? 2 : 5);
                    else { Fx.I.GooHit(b.p, b.d, s.entry.kind == Kind.Bloater); CombatAudio.I.Hit(); }
                }
                Fx.I.ReleaseTracer(b.tr);
                bullets[i] = bullets[^1]; bullets.RemoveAt(bullets.Count - 1);
            }
        }

        // ---------------------------------------------------------------- walking
        void TickWalk(Spawned s, float dt)
        {
            if (s.hopT < 1 || s.hidden) return;
            bool moving = false;
            float v = walkSpeed;
            if (walk == Walk.Circle && s == sel)
            {
                const float R = 4.5f;
                s.walkAngle += v / R * dt;
                var c = s.home + new Vector3(-R, 0, 0);
                var p = c + new Vector3(Mathf.Cos(s.walkAngle), 0, Mathf.Sin(s.walkAngle)) * R;
                var d = p - s.root.position; d.y = 0;
                if (d.sqrMagnitude > 1e-6f) s.root.rotation = Quaternion.Slerp(s.root.rotation, Quaternion.LookRotation(d), dt * 10);
                s.root.position = p; moving = true;
            }
            else if (walk == Walk.Follow && s == sel && s.hasTarget)
            {
                var d = s.walkTarget - s.root.position; d.y = 0;
                if (d.magnitude > 0.15f)
                {
                    s.root.rotation = Quaternion.Slerp(s.root.rotation, Quaternion.LookRotation(d), dt * 8);
                    s.root.position += d.normalized * Mathf.Min(v * dt, d.magnitude); moving = true;
                }
            }
            if (s.anim == null || s.clips.Count == 0) return;
            var st = s.clips[s.clip];
            if (s == sel && walk != Walk.Off && !s.paused)
                st.speed = moving ? Mathf.Clamp(v / (s.stride * s.baseScale * s.scale), 0.05f, 6f) : 0; // feet planted, like the game
            else st.speed = s.paused ? 0 : animSpeed;
        }

        // ---------------------------------------------------------------- overlays: skeleton lines, wireframe
        void ClearOverlays(Spawned s)
        {
            if (s.bonesLines != null) foreach (var x in s.bonesLines) if (x.lr != null) Destroy(x.lr.gameObject);
            s.bonesLines = null;
            if (s.wires != null) foreach (var w in s.wires) { if (w.t != null) Destroy(w.t.gameObject); Destroy(w.wire); }
            s.wires = null;
        }

        void BuildSkeleton(Spawned s)
        {
            s.bonesLines = new();
            var set = new HashSet<Transform>();
            foreach (var r in s.renderers) if (r is SkinnedMeshRenderer smr) foreach (var b in smr.bones) if (b != null) set.Add(b);
            foreach (var b in set)
            {
                if (b.parent == null || !set.Contains(b.parent)) continue;
                var go = new GameObject("Bone_" + b.name);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMat; lr.positionCount = 2; lr.useWorldSpace = true;
                lr.widthMultiplier = 0.035f * Mathf.Max(0.5f, s.baseScale * s.scale / Mathf.Max(0.01f, BaseFit(s)));
                lr.startColor = lr.endColor = Holo.Acid; lr.shadowCastingMode = ShadowCastingMode.Off;
                s.bonesLines.Add((b.parent, b, lr));
            }
        }

        void BuildWire(Spawned s)
        {
            s.wires = new();
            int unreadable = 0;
            foreach (var r in s.renderers)
            {
                Mesh src = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (src == null) continue;
                if (!src.isReadable) { unreadable++; continue; }
                var edges = new HashSet<long>(); var idx = new List<int>();
                var tris = src.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tris[t + k], b = tris[t + (k + 1) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (edges.Add(key)) { idx.Add(a); idx.Add(b); }
                    }
                var wire = new Mesh { indexFormat = src.vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                wire.vertices = src.vertices; wire.SetIndices(idx, MeshTopology.Lines, 0); wire.RecalculateBounds();
                var go = new GameObject("Wire");
                go.AddComponent<MeshFilter>().sharedMesh = wire;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = wireMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                s.wires.Add((r, src, wire, go.transform));
            }
            if (s.wires.Count == 0) status = unreadable > 0 ? "WIREFRAME: THIS MESH ISN'T CPU-READABLE" : "WIREFRAME: NO MESH";
        }

        readonly List<Vector3> bakeVerts = new();
        Mesh bakeMesh;
        void TickOverlays(Spawned s)
        {
            bool wantBones = showSkeleton && s == sel, wantWire = showWire && s == sel;
            if (wantBones && s.bonesLines == null) BuildSkeleton(s);
            if (!wantBones && s.bonesLines != null) { foreach (var x in s.bonesLines) Destroy(x.lr.gameObject); s.bonesLines = null; }
            if (wantWire && s.wires == null) BuildWire(s);
            if (!wantWire && s.wires != null) { foreach (var w in s.wires) { Destroy(w.t.gameObject); Destroy(w.wire); } s.wires = null; }
            if (s.bonesLines != null) foreach (var (a, b, lr) in s.bonesLines) { lr.enabled = !s.hidden; lr.SetPosition(0, a.position); lr.SetPosition(1, b.position); }
            if (s.wires != null)
                foreach (var w in s.wires)
                {
                    w.t.gameObject.SetActive(!s.hidden);
                    if (w.r is SkinnedMeshRenderer smr)
                    {   // the posed mesh, in the renderer's space with its scale baked in
                        bakeMesh ??= new Mesh();
                        smr.BakeMesh(bakeMesh, true);
                        bakeMesh.GetVertices(bakeVerts); w.wire.SetVertices(bakeVerts); w.wire.RecalculateBounds();
                        w.t.SetPositionAndRotation(smr.transform.position, smr.transform.rotation); w.t.localScale = Vector3.one;
                    }
                    else { w.t.SetPositionAndRotation(w.r.transform.position, w.r.transform.rotation); w.t.localScale = w.r.transform.lossyScale; }
                }
        }

        // ---------------------------------------------------------------- frame
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            clock += dt;
            CameraInput();
            foreach (var s in spawned.ToArray()) { TickEffects(s, dt); TickWalk(s, dt); }
            TickHero(dt);
            TickBullets(dt);
            Fx.I.Tick(dt);
            UpdateCamera(dt);
            RefreshLive();
        }

        void LateUpdate()
        {
            foreach (var s in spawned)
            {
                if (s.belly != null && s.swellT >= 0 && s.castBurst)
                {
                    float k = Mathf.SmoothStep(0, 1, s.swellT / s.swellDur);
                    s.belly.localScale = s.belly.localScale * (1 + 0.35f * k) + Vector3.one * (Random.Range(-0.03f, 0.03f) * k);
                }
                TickOverlays(s);
            }
        }

        // ---------------------------------------------------------------- camera: orbit / zoom / pan, or the game camera
        Vector2 lastMouse; bool dragging; float downT; Vector2 downPos;
        float lastPinch; Vector2 lastTwo;

        bool OverUi(Vector2 screen)
        {
            if (EventSystem.current == null) return false;
            var ped = new PointerEventData(EventSystem.current) { position = screen };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            return hits.Count > 0;
        }

        void CameraInput()
        {
            if (Input.touchCount >= 2)
            {
                var a = Input.GetTouch(0); var b = Input.GetTouch(1);
                float pinch = (a.position - b.position).magnitude; var mid = (a.position + b.position) * 0.5f;
                if (a.phase == TouchPhase.Began || b.phase == TouchPhase.Began) { lastPinch = pinch; lastTwo = mid; return; }
                if (lastPinch > 1) targetDist = Mathf.Clamp(targetDist * lastPinch / Mathf.Max(1, pinch), 2, 60);
                var dm = mid - lastTwo; yaw += dm.x * 0.25f; pitch = Mathf.Clamp(pitch - dm.y * 0.2f, 5, 85);
                lastPinch = pinch; lastTwo = mid; gameCam = false; dragging = true;
                return;
            }
            Vector2 pos; bool down, held, up;
            if (Input.touchCount == 1) { var t = Input.GetTouch(0); pos = t.position; down = t.phase == TouchPhase.Began; up = t.phase == TouchPhase.Ended; held = !down && !up; }
            else { pos = Input.mousePosition; down = Input.GetMouseButtonDown(0); held = Input.GetMouseButton(0) && !down; up = Input.GetMouseButtonUp(0); }
            if (down) { if (OverUi(pos)) { downT = -1; return; } downT = Time.unscaledTime; downPos = lastMouse = pos; dragging = false; }
            if (held && downT >= 0)
            {
                var d = pos - lastMouse; lastMouse = pos;
                if ((pos - downPos).magnitude > 8) dragging = true;
                if (dragging) { yaw += d.x * 0.3f; pitch = Mathf.Clamp(pitch - d.y * 0.25f, 5, 85); gameCam = false; }
            }
            if (up && downT >= 0) { if (!dragging) Click(pos); downT = -1; dragging = false; }
            if (Input.touchCount == 0 && Input.GetMouseButton(1))
            {
                Vector2 d = (Vector2)Input.mousePosition - lastMouse;
                var right = cam.transform.right; var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                pivotGoal -= (right * d.x + fwd * d.y) * dist * 0.0015f; pivot = pivotGoal; gameCam = false;
            }
            if (Input.touchCount == 0) lastMouse = Input.mousePosition;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !OverUi(Input.mousePosition)) { targetDist = Mathf.Clamp(targetDist * (1 - wheel * 0.1f), 2, 60); gameCam = false; }
        }

        // a click/tap on the floor: pick a model under it, else set the walk-to point
        void Click(Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(screen);
            Spawned best = null; float bd = float.MaxValue;
            foreach (var s in spawned)
            {
                if (s.hidden) continue;
                var b = Bounds(s.root);
                if (b.IntersectRay(ray, out float d) && d < bd) { bd = d; best = s; }
            }
            if (walk == Walk.Follow && sel != null && ray.direction.y < -1e-4f)
            {
                var p = ray.GetPoint(-ray.origin.y / ray.direction.y);
                if (best == null || best == sel) { sel.walkTarget = p; sel.hasTarget = true; GunFx.I.Ring(p + Vector3.up * 0.05f, 0.9f, Holo.A(Holo.Cyan, 0.8f), 0.3f); return; }
            }
            if (best != null) Select(best);
        }

        public void GameCamera() { gameCam = true; }
        public void SetCamera(float distance, float pitchDeg, float yawDeg) { gameCam = false; targetDist = dist = distance; pitch = pitchDeg; yaw = yawDeg; }
        void UpdateCamera(float dt)
        {
            if (sel != null && walk != Walk.Off) pivotGoal = sel.root.position + Vector3.up * 0.8f;
            pivot = Vector3.Lerp(pivot, pivotGoal, 1 - Mathf.Exp(-dt * 6));
            float py = pitch, pyaw = yaw, pd = targetDist;
            if (gameCam) { py = cfg.camera.pitch; pyaw = cfg.camera.yaw; pd = cfg.camera.distance; }
            dist = Mathf.Lerp(dist, pd, 1 - Mathf.Exp(-dt * 8));
            var rot = Quaternion.Euler(py, -pyaw, 0); // looking down at the pivot from the -Z side, like the game
            if (gameCam) { pitch = Mathf.Lerp(pitch, py, 1 - Mathf.Exp(-dt * 6)); yaw = Mathf.LerpAngle(yaw, pyaw, 1 - Mathf.Exp(-dt * 6)); targetDist = pd; rot = Quaternion.Euler(pitch, -yaw, 0); }
            cam.transform.position = pivot - rot * Vector3.forward * dist;
            cam.transform.rotation = rot;
            cam.fieldOfView = cfg.camera.fov;
            // keep the pivot in the middle of the free area left of the panel
            if (canvas != null)
            {
                float canvasW = ((RectTransform)canvas.transform).rect.width;
                float panelFrac = canvasW > 1 ? (PanelW + 36) / canvasW : 0;
                float halfW = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect;
                cam.transform.position += cam.transform.right * (panelFrac * halfW);
            }
        }

        // ---------------------------------------------------------------- UI
        string status = "";
        Canvas canvas;
        RectTransform panel, pageModels, pageAnim, pageFx;
        Text statusText, selText, statsText, clipText, listPageText;
        readonly List<HoloButton> listButtons = new();
        int listPage, listSel = -1;
        readonly List<HoloButton> clipButtons = new();
        HoloButton tabModels, tabAnim, tabFx, playBtn, loopBtn, skelBtn, wireBtn, walkCircle, walkFollow, walkOff, heroBtn, fireBtn, weaponBtn, camBtn;
        HoloSlider speedSlider, scaleSlider, walkSlider;
        const int ListRows = 5;
        public const float PanelW = 640;

        void BuildUi()
        {
            canvas = Holo.MakeCanvas(transform, "SandboxUI", 0);
            var safe = Holo.SafeRoot(canvas.transform);
            panel = Holo.Panel(safe, "Panel");
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1); panel.pivot = new Vector2(1, 0.5f);
            panel.offsetMin = new Vector2(-PanelW - 18, 18); panel.offsetMax = new Vector2(-18, -18);
            var title = Holo.Label(Holo.Rect(panel, "Title", new Vector2(0, 1), new Vector2(30, -18), new Vector2(360, 56)), "SANDBOX", 46, Holo.Ink, TextAnchor.UpperLeft);
            title.fontStyle = FontStyle.Bold; Holo.GlowText(title, Holo.Cyan, 0.4f);
            title.rectTransform.pivot = new Vector2(0, 1);
            var back = Btn(panel, "Back", "BACK", "home", new Vector2(170, 64), new Vector2(1, 1), new Vector2(-24, -18), 26, () => BackToGame());
            back.gameObject.SetActive(Application.CanStreamedLevelBeLoaded("Main"));
            tabModels = Btn(panel, "TabModels", "MODELS", null, new Vector2(196, 62), new Vector2(0, 1), new Vector2(24, -86), 26, () => Tab(0));
            tabAnim = Btn(panel, "TabAnim", "ANIM", null, new Vector2(196, 62), new Vector2(0, 1), new Vector2(222, -86), 26, () => Tab(1));
            tabFx = Btn(panel, "TabFx", "FX / HERO", null, new Vector2(196, 62), new Vector2(0, 1), new Vector2(420, -86), 26, () => Tab(2));
            selText = Lbl(panel, "Selected", new Vector2(30, -160), new Vector2(PanelW - 60, 34), 26, Holo.Acid);
            statsText = Lbl(panel, "Stats", new Vector2(30, -194), new Vector2(PanelW - 60, 34), 23, Holo.Muted);
            statusText = Lbl(panel, "Status", new Vector2(30, 0), new Vector2(PanelW - 60, 70), 22, Holo.Ink);
            var srt = statusText.rectTransform; srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0, 0); srt.anchoredPosition = new Vector2(30, 14);
            statusText.alignment = TextAnchor.LowerLeft;

            pageModels = Page("Models"); pageAnim = Page("Anim"); pageFx = Page("Fx");
            // MODELS: the list (paged), spawn / delete / clear / compare / rescan
            for (int i = 0; i < ListRows; i++)
            {
                int row = i;
                var b = Btn(pageModels, "Item" + i, "", null, new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, -row * 68), 26, () => { listSel = listPage * ListRows + row; RefreshList(); });
                listButtons.Add(b);
            }
            Btn(pageModels, "Prev", "<", null, new Vector2(90, 56), new Vector2(0, 1), new Vector2(0, -ListRows * 68 - 4), 30, () => { listPage = Mathf.Max(0, listPage - 1); RefreshList(); });
            Btn(pageModels, "Next", ">", null, new Vector2(90, 56), new Vector2(0, 1), new Vector2(100, -ListRows * 68 - 4), 30, () => { listPage = Mathf.Min((entries.Count - 1) / ListRows, listPage + 1); RefreshList(); });
            listPageText = Lbl(pageModels, "Page", new Vector2(206, -ListRows * 68 - 14), new Vector2(380, 40), 22, Holo.Muted);
            float y = -ListRows * 68 - 72;
            Btn(pageModels, "Spawn", "SPAWN", "deploy", new Vector2(290, 70), new Vector2(0, 1), new Vector2(0, y), 30, () => { if (listSel >= 0 && listSel < entries.Count && !Busy) _ = SpawnEntry(entries[listSel]); });
            Btn(pageModels, "Compare", "COMPARE", null, new Vector2(290, 70), new Vector2(0, 1), new Vector2(302, y), 30, () => { if (listSel >= 0 && !Busy) _ = Compare(entries[listSel]); });
            y -= 80;
            Btn(pageModels, "Delete", "DELETE", null, new Vector2(190, 64), new Vector2(0, 1), new Vector2(0, y), 26, () => Delete(sel));
            Btn(pageModels, "Clear", "CLEAR ALL", null, new Vector2(190, 64), new Vector2(0, 1), new Vector2(200, y), 26, ClearAll);
            var rescan = Btn(pageModels, "Rescan", "RESCAN", null, new Vector2(190, 64), new Vector2(0, 1), new Vector2(400, y), 26, Rescan);
            rescan.gameObject.SetActive(DropSupported);
            y -= 74;
            Btn(pageModels, "Next model", "SELECT NEXT", null, new Vector2(290, 60), new Vector2(0, 1), new Vector2(0, y), 24, () => { if (spawned.Count > 0) Select(spawned[(spawned.IndexOf(sel) + 1) % spawned.Count]); });
            camBtn = Btn(pageModels, "GameCam", "GAME CAMERA", null, new Vector2(290, 60), new Vector2(0, 1), new Vector2(302, y), 24, GameCamera);

            // ANIM: clips, transport, speed, loop, scale, skeleton / wireframe, walk
            clipText = Lbl(pageAnim, "Clips", new Vector2(0, 0), new Vector2(PanelW - 48, 32), 22, Holo.Muted);
            for (int i = 0; i < 6; i++)
            {
                int ci = i;
                var b = Btn(pageAnim, "Clip" + i, "", null, new Vector2(190, 54), new Vector2(0, 1), new Vector2((i % 3) * 198, -38 - (i / 3) * 60), 22, () => { if (sel != null) PlayClip(sel, ci); RefreshSelected(); });
                clipButtons.Add(b);
            }
            y = -168;
            Btn(pageAnim, "StepBack", "|<", null, new Vector2(110, 64), new Vector2(0, 1), new Vector2(0, y), 30, () => Step(-1));
            playBtn = Btn(pageAnim, "Play", "PAUSE", null, new Vector2(200, 64), new Vector2(0, 1), new Vector2(118, y), 28, () => { TogglePause(); RefreshSelected(); });
            Btn(pageAnim, "StepFwd", ">|", null, new Vector2(110, 64), new Vector2(0, 1), new Vector2(326, y), 30, () => Step(1));
            loopBtn = Btn(pageAnim, "Loop", "LOOP", null, new Vector2(140, 64), new Vector2(0, 1), new Vector2(444, y), 26, () => { ToggleLoop(); RefreshSelected(); });
            y -= 84;
            speedSlider = Slider(pageAnim, "SPEED", y, 0.1f, 3f, 1f, v => { animSpeed = v; return $"{v:F2}x"; });
            y -= 84;
            scaleSlider = Slider(pageAnim, "SCALE", y, 0.25f, 4f, 1f, v => { if (sel != null) { sel.scale = v; ApplyScale(sel); } return $"{v:F2}x"; });
            y -= 84;
            skelBtn = Btn(pageAnim, "Skeleton", "SKELETON", null, new Vector2(290, 62), new Vector2(0, 1), new Vector2(0, y), 26, () => { showSkeleton = !showSkeleton; RefreshSelected(); });
            wireBtn = Btn(pageAnim, "Wire", "WIREFRAME", null, new Vector2(290, 62), new Vector2(0, 1), new Vector2(302, y), 26, () => { showWire = !showWire; RefreshSelected(); });
            y -= 76;
            walkOff = Btn(pageAnim, "WalkOff", "STAND", null, new Vector2(190, 62), new Vector2(0, 1), new Vector2(0, y), 24, () => SetWalk(Walk.Off));
            walkCircle = Btn(pageAnim, "WalkCircle", "WALK CIRCLE", null, new Vector2(190, 62), new Vector2(0, 1), new Vector2(198, y), 22, () => SetWalk(Walk.Circle));
            walkFollow = Btn(pageAnim, "WalkFollow", "TAP TO WALK", null, new Vector2(190, 62), new Vector2(0, 1), new Vector2(396, y), 22, () => SetWalk(Walk.Follow));
            y -= 84;
            walkSlider = Slider(pageAnim, "WALK SPEED", y, 0.3f, 7f, walkSpeed, v => { walkSpeed = v; return $"{v:F1} m/s"; });

            // FX / HERO
            y = 0;
            Btn(pageFx, "DroneDeath", "DRONE DEATH GOO", null, new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () => Trigger(Effect.DroneDeath)); y -= 70;
            Btn(pageFx, "SacBurst", "BLOATER SAC BURST", null, new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () => Trigger(Effect.SacBurst)); y -= 70;
            Btn(pageFx, "Minis", "MINIS SPAWN", null, new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () => Trigger(Effect.Minis)); y -= 70;
            Btn(pageFx, "BloaterDeath", "BLOATER DEATH", null, new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () => Trigger(Effect.BloaterDeath)); y -= 70;
            Btn(pageFx, "Hit", "HIT (SPARKS / GOO / PUS)", null, new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () => Trigger(Effect.Hit)); y -= 92;
            heroBtn = Btn(pageFx, "Hero", "SPAWN HERO", "squad", new Vector2(290, 70), new Vector2(0, 1), new Vector2(0, y), 26, SpawnHero);
            fireBtn = Btn(pageFx, "Fire", "FIRE", "rifle", new Vector2(290, 70), new Vector2(0, 1), new Vector2(302, y), 26, () => { if (hero == null) SpawnHero(); heroFire = !heroFire; RefreshSelected(); }); y -= 80;
            weaponBtn = Btn(pageFx, "Weapon", "WEAPON: RIFLE", null, new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, CycleWeapon); y -= 70;
            Btn(pageFx, "DevScout", "DEV: UNLOCK SCOUT", "squad", new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () =>
            {   // testing only: the Scout stays shard-locked (30) in normal play
                var d = Progress.D; if (!d.heroStars.Exists(x => x.StartsWith("scout:"))) { d.heroStars.Add("scout:1"); d.heroLv.Add("scout:1"); Progress.Save(); }
                status = "SCOUT UNLOCKED (DEV)";
            }); y -= 70;
            Btn(pageFx, "DevWeapons", "DEV: UNLOCK BLUEPRINT WEAPONS", "rifle", new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () =>
            {   // testing only: the Frag Launcher / Cryo Lance without their 10 fragments
                var d = Progress.D; foreach (var w in new[] { "FRAG", "LANCE", "THERMAL", "RAIL" }) if (!d.blueprints.Contains(w + ".dev:1")) d.blueprints.Add(w + ".dev:1");
                Progress.Save(); status = "BLUEPRINT WEAPONS UNLOCKED (DEV)";
            }); y -= 70;
            Btn(pageFx, "DevWeekly", "DEV: TOGGLE WEEKLY MUTATION", "flask", new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () =>
            {   // testing only: force the Weekly Mutation below rank 8
                Ops.WeeklyDev = !Ops.WeeklyDev; status = Ops.WeeklyDev ? "WEEKLY MUTATION FORCED ON: " + Ops.WeeklyLine : "WEEKLY MUTATION: RANK 8 RULE";
            }); y -= 70;
            Btn(pageFx, "DevTest", "DEV: TOGGLE TEST MODE", "flask", new Vector2(PanelW - 48, 62), new Vector2(0, 1), new Vector2(0, y), 26, () =>
            {   // the same local setting as Settings > TEST MODE: every zone open, runs save nothing
                Prefs.TestMode = !Prefs.TestMode; if (!Prefs.TestMode) Prefs.TestPower = false; status = Prefs.TestMode ? "TEST MODE ON: ALL ZONES OPEN, NO PROGRESS SAVED" : "TEST MODE OFF";
            });
            Tab(0);
            listSel = entries.Count > 0 ? 0 : -1;
            RefreshList(); RefreshSelected();
            status = DropSupported ? (DropDir != null ? "DROP FOLDER: " + DropDir : "NO DROP FOLDER FOUND (Maze Swarm/Sandbox/Drop)") : "BUILT-IN MODELS (THE DROP FOLDER IS DESKTOP ONLY)";
        }

        RectTransform Page(string name)
        {
            var rt = Holo.Rect(panel, "Page" + name);
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.offsetMin = new Vector2(24, 96); rt.offsetMax = new Vector2(-24, -240);
            return rt;
        }

        static Text Lbl(RectTransform parent, string name, Vector2 pos, Vector2 size, int font, Color c)
        {
            var t = Holo.Label(Holo.Rect(parent, name, new Vector2(0, 1), pos, size), "", font, c, TextAnchor.MiddleLeft);
            t.rectTransform.pivot = new Vector2(0, 1); t.rectTransform.anchoredPosition = pos;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static HoloButton Btn(RectTransform parent, string name, string label, string icon, Vector2 size, Vector2 anchor, Vector2 pos, int font, System.Action a)
        {
            var b = Holo.Button(parent, name, size, label, icon, font);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(anchor.x, 1); rt.anchoredPosition = pos;
            b.onClick = () => { a(); Sfx.Click(); };
            return b;
        }

        HoloSlider Slider(RectTransform parent, string name, float y, float min, float max, float value, System.Func<float, string> onChange)
        {
            var rt = Holo.Rect(parent, name, new Vector2(0, 1), new Vector2(0, y), new Vector2(PanelW - 48, 70));
            rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(0, y);
            var s = rt.gameObject.AddComponent<HoloSlider>();
            s.Build(name, min, max, value, onChange);
            return s;
        }

        void Tab(int i)
        {
            pageModels.gameObject.SetActive(i == 0); pageAnim.gameObject.SetActive(i == 1); pageFx.gameObject.SetActive(i == 2);
            tabModels.selected = i == 0; tabAnim.selected = i == 1; tabFx.selected = i == 2;
        }
        public void ShowTab(int i) => Tab(i);
        public void SelectEntry(int i) { listSel = i; listPage = Mathf.Max(0, i) / ListRows; RefreshList(); }

        void SetWalk(Walk w)
        {
            walk = w;
            if (sel != null) { sel.home = sel.root.position; sel.walkAngle = 0; sel.hasTarget = false; if (w != Walk.Off && sel.clips.Count > 0) PlayClip(sel, sel.clip); }
            if (w == Walk.Follow) status = "TAP / CLICK THE FLOOR TO WALK THERE";
            RefreshSelected();
        }
        public void SetWalkMode(Walk w) => SetWalk(w);

        void RefreshList()
        {
            for (int i = 0; i < ListRows; i++)
            {
                int k = listPage * ListRows + i;
                var b = listButtons[i];
                b.gameObject.SetActive(k < entries.Count);
                if (k >= entries.Count) continue;
                var e = entries[k];
                b.label.text = (e.kind == Kind.Drop ? "DROP  " : "GAME  ") + e.name;
                b.label.fontSize = e.name.Length > 24 ? 21 : 26;
                b.selected = k == listSel;
            }
            listPageText.text = $"PAGE {listPage + 1}/{Mathf.Max(1, (entries.Count + ListRows - 1) / ListRows)}  //  {entries.Count} MODELS";
        }

        void RefreshSelected()
        {
            if (selText == null) return;
            var s = sel;
            selText.text = s == null ? "NOTHING SELECTED" : $"SELECTED: {s.entry.name}";
            statsText.text = s == null ? "" : $"{s.tris:N0} TRIS  //  {s.bones} BONES  //  {s.clips.Count} CLIPS" + (s.note != null ? "  //  " + s.note : "");
            clipText.text = s == null ? "" : s.clips.Count == 0 ? "NO ANIMATIONS IN THIS MODEL" : "CLIPS:";
            for (int i = 0; i < clipButtons.Count; i++)
            {
                bool has = s != null && i < s.clips.Count;
                clipButtons[i].gameObject.SetActive(has);
                if (has) { clipButtons[i].label.text = s.clips[i].name.ToUpper(); clipButtons[i].selected = i == s.clip; }
            }
            playBtn.label.text = s != null && s.paused ? "PLAY" : "PAUSE";
            loopBtn.selected = s != null && s.loop;
            skelBtn.selected = showSkeleton; wireBtn.selected = showWire;
            walkOff.selected = walk == Walk.Off; walkCircle.selected = walk == Walk.Circle; walkFollow.selected = walk == Walk.Follow;
            if (s != null) scaleSlider.SetValue(s.scale);
            heroBtn.label.text = hero != null ? "REMOVE HERO" : "SPAWN HERO";
            fireBtn.selected = heroFire; fireBtn.label.text = heroFire ? "STOP FIRE" : "FIRE";
            weaponBtn.label.text = "WEAPON: " + WeaponName;
        }

        void RefreshLive()
        {
            if (statusText != null) statusText.text = status;
            if (sel != null && sel.anim != null && sel.clips.Count > 0 && clipText != null)
            {
                var st = sel.clips[sel.clip];
                clipText.text = $"CLIPS:   {st.name}  {Mathf.Repeat(st.time, Mathf.Max(0.001f, st.length)):F2} / {st.length:F2} s  @ {st.speed:F2}x";
            }
        }

        void BackToGame() { if (Application.CanStreamedLevelBeLoaded("Main")) SceneManager.LoadScene("Main"); }

        void OnDestroy() { if (I == this) I = null; }
    }

    // A holo-style horizontal slider: label, value text and a cyan fill; drag or tap anywhere on the track.
    public class HoloSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        float min, max, value; System.Func<float, string> onChange;
        Image fill; Text valueText; RectTransform track;

        public void Build(string name, float min, float max, float value, System.Func<float, string> onChange)
        {
            this.min = min; this.max = max; this.onChange = onChange;
            var rt = (RectTransform)transform;
            Holo.Label(Holo.Rect(rt, "Name", new Vector2(0, 1), new Vector2(0, 0), new Vector2(300, 30)), name, 22, Holo.Muted, TextAnchor.UpperLeft).rectTransform.pivot = new Vector2(0, 1);
            valueText = Holo.Label(Holo.Rect(rt, "Value", new Vector2(1, 1), new Vector2(0, 0), new Vector2(240, 30)), "", 24, Holo.Acid, TextAnchor.UpperRight);
            valueText.rectTransform.pivot = new Vector2(1, 1);
            track = Holo.Rect(rt, "Track");
            track.anchorMin = new Vector2(0, 0); track.anchorMax = new Vector2(1, 0); track.pivot = new Vector2(0.5f, 0);
            track.offsetMin = new Vector2(0, 0); track.offsetMax = new Vector2(0, 36);
            Holo.Img(track, Holo.FillSprite, Holo.A(Holo.Glass, 1), true, true);
            fill = Holo.Img(Holo.Fill(track, "Fill", 4), Holo.WhiteSprite, Holo.A(Holo.Cyan, 0.75f));
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            Holo.Img(Holo.Fill(track, "Edge"), Holo.EdgeSprite, Holo.A(Holo.Cyan, 0.8f), true);
            SetValue(value);
        }

        public void SetValue(float v)
        {
            value = Mathf.Clamp(v, min, max);
            fill.fillAmount = (value - min) / (max - min);
            valueText.text = onChange(value);
        }

        void FromPointer(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position, e.pressEventCamera, out var lp)) return;
            var r = track.rect;
            SetValue(Mathf.Lerp(min, max, Mathf.InverseLerp(r.xMin, r.xMax, lp.x)));
        }
        public void OnPointerDown(PointerEventData e) => FromPointer(e);
        public void OnDrag(PointerEventData e) => FromPointer(e);
    }
}
