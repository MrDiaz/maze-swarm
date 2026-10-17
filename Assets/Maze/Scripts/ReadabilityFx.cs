using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Maze
{
    // The readability pass (2026-10-15, modelled on Archero 2; Manuel: "let's try it"). Every change has its own static toggle
    // (true = the new look); all of it is world-space and lives here, nothing in the HUD files. Visual only: colliders,
    // ranges, damage and the swarm's rules are untouched.
    //   CameraNew        the run camera's CLOSE framing at 24 m (was 30) and CLOSE the default (FAR 40 stays selectable)
    //   HeroLook         the Vanguard's upper body x1.12 / head x1.2 (bones, visual) and one value step lighter
    //   EnemyScale       enemy bodies drawn x1.15 (collision radius unchanged)
    //   FloorCalm        CryoSoft floors: one step darker / bluer, the two plate tones merged, seams and stripes softened
    //   Rings            a soft red ring and a blob shadow under every enemy
    //   HpBars           a small HP bar over damaged enemies
    //   DamageNumbers    pooled floating damage numbers (trial)
    //   KillBurst        a shard burst on every kill (the XP orbs that fly to the hero already exist)
    //   SpawnMarkers     a red floor marker while an enemy rises out of the floor, and a puff as it appears
    //   LobTrails        the Spitter's acid glob flies to its (already telegraphed) landing spot with a trail
    //   Explosions       a floor ring and a fading scorch under every blast (no screen flash)
    //   LevelUpBurst     a gold ring and sparks on the hero at each level
    //   Tracers          rifle / minigun tracers: brighter warm head, 1.5x tail (same rate, damage, pooling)
    //   IceFinish        (2026-10-16, Manuel: "floor tiles that appear unfinished") the Cryo ice sheets were one flat untextured slab
    //                    (SC_marking_Soft_Ice) plus, on F2/F3/F5, the old flat ice decal under it: a finished ice surface over the same
    //                    cells instead (frost, faint cracks, bevelled 4 m plate edges, gloss, a normal map), the flat ones hidden
    public class ReadabilityFx : MonoBehaviour
    {
        public static bool CameraNew = true, HeroLook = true, EnemyScale = true, FloorCalm = true, Rings = true, HpBars = true, DamageNumbers = true,
            KillBurst = true, SpawnMarkers = true, LobTrails = true, Explosions = true, LevelUpBurst = true, Tracers = true, IceFinish = true, CryoDressing = true, CryoWallVariety = true, TallIslands = true, StandaloneTurrets = true;
        public const float CamCloseNew = 24f, EnemyVisual = 1.15f, HeadMul = 1.2f, UpperMul = 1.12f, HeroValue = 1.18f, TracerLen = 1.5f;
        public static ReadabilityFx I;
        public static float EnemyScaleNow => EnemyScale ? EnemyVisual : 1f;

        // ---- camera: CLOSE at 24 m becomes the default once (a fresh pref key); FAR stays the old 40 m
        public static float CamDistance => !CameraNew ? Prefs.CamDistance : Prefs.CamCloseOn ? CamCloseNew : Prefs.CamFar;
        static void CameraDefaultOnce()
        {
            if (!CameraNew || PlayerPrefs.GetInt("ms.camframe.v2", 0) == 1) return;
            Prefs.CamCloseOn = true; PlayerPrefs.SetInt("ms.camframe.v2", 1); PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { CameraDefaultOnce(); var go = new GameObject("ReadabilityFx"); DontDestroyOnLoad(go); I = go.AddComponent<ReadabilityFx>(); }

        Game game; Camera cam;
        Material ringMat, blobMat, barBack, barFill, markMat, decalMat, scorchMat, goldMat, lobMat;
        readonly Dictionary<Enemy, EnemyMarks> marks = new(); readonly Stack<EnemyMarks> markPool = new();
        readonly Dictionary<Enemy, (float hp, float acc, float t)> hpSeen = new();
        readonly List<Enemy> gone = new();
        int lastLevel = -1; GameObject lastRoom;

        class EnemyMarks { public Transform ring, blob, bar, fill, spawn; public bool used; public float spawnFade; }

        void Start()
        {
            ringMat = Mats.Glow(new Color(1f, .22f, .18f, .55f));
            blobMat = Mats.Fade(new Color(.02f, .03f, .08f, .32f));
            barBack = Mats.Fade(new Color(.06f, .05f, .1f, .8f));
            barFill = Mats.Glow(new Color(1f, .28f, .22f, 1f));
            markMat = Mats.Glow(new Color(1f, .15f, .12f, .6f));
            decalMat = Mats.Glow(new Color(1f, .62f, .25f, .8f));
            scorchMat = Mats.Fade(new Color(.05f, .04f, .06f, .55f));
            goldMat = Mats.Glow(new Color(1f, .8f, .25f, .9f));
            lobMat = Mats.Glow(new Color(.55f, 1f, .25f, 1f));
            foreach (var m in new[] { ringMat, blobMat, barBack, barFill, markMat }) m.enableInstancing = true;   // (many small markers: instanced)
        }

        static Transform Flat(string name, Mesh mesh, Material m, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; return go.transform;
        }
        static Mesh quadMesh;
        static Mesh Quad { get { if (quadMesh == null) { var go = GameObject.CreatePrimitive(PrimitiveType.Quad); quadMesh = go.GetComponent<MeshFilter>().sharedMesh; Destroy(go); } return quadMesh; } }

        EnemyMarks GetMarks()
        {
            var m = markPool.Count > 0 ? markPool.Pop() : null;
            if (m == null)
            {
                m = new EnemyMarks();
                m.blob = Flat("EnemyBlob", ZoneHazard.Disc, blobMat, transform);
                m.ring = Flat("EnemyRing", Fx.Ring, ringMat, transform);
                m.bar = Flat("EnemyHpBack", Quad, barBack, transform); m.fill = Flat("EnemyHpFill", Quad, barFill, m.bar);
                m.spawn = Flat("SpawnMark", ZoneHazard.Disc, markMat, transform);
            }
            m.used = true; m.spawnFade = 0; return m;
        }
        void Release(EnemyMarks m) { m.used = false; m.ring.gameObject.SetActive(false); m.blob.gameObject.SetActive(false); m.bar.gameObject.SetActive(false); m.spawn.gameObject.SetActive(false); markPool.Push(m); }

        bool cargoWas;
        int cargoFloor;
        void ClearPresentationPools()
        {
            foreach (var p in active)
            {
                var trail = p.t.GetComponent<TrailRenderer>(); if (trail != null) trail.Clear();
                p.t.gameObject.SetActive(false);
                if (!free.TryGetValue(p.kind, out var stack)) free[p.kind] = stack = new Stack<Pooled>();
                stack.Push(p);
            }
            active.Clear();
        }
        void LateUpdate()
        {
            if (game == null) { game = FindAnyObjectByType<Game>(); if (game == null) return; }
            cam = Camera.main;
            bool cargo = CohesiveZone.For(game);
            if (cargoWas && (!cargo || cargoFloor != game.floor)) ClearPresentationPools();
            cargoWas = cargo; cargoFloor = game.floor;
            bool live = game.run && !Game.SimRun && (game.state == Game.State.Playing || game.state == Game.State.Paused || game.state == Game.State.Choosing);
            if (!live) { if (marks.Count > 0) { foreach (var kv in marks) Release(kv.Value); marks.Clear(); hpSeen.Clear(); } lastLevel = game.level; TickPools(Time.deltaTime); return; }
            float dt = Time.deltaTime;
            FloorPass();
            // per enemy: rings, blob, HP bar, spawn marker, damage numbers, kill burst
            foreach (var e in game.swarm.enemies)
            {
                if (e.view == null || e.type.nest) continue;
                if (!marks.TryGetValue(e, out var m))
                {
                    m = GetMarks(); marks[e] = m;
                    if (SpawnMarkers && e.spawnT < .5f) { m.spawnFade = 1; if (VfxKit.I != null) VfxKit.I.Puff(MazeMap.ToWorld(e.p, .3f), Vector3.up * .6f, 1.2f * e.scale, .5f, new Color(1f, .45f, .4f, .5f)); }
                }
                var w = MazeMap.ToWorld(e.p, 0); float r = e.r * 1.25f * EnemyScaleNow + .1f;
                bool show = !e.dead && e.spawnT > .05f;
                m.ring.gameObject.SetActive(Rings && show); m.blob.gameObject.SetActive(Rings && show);
                if (Rings && show)
                {
                    m.ring.position = w + Vector3.up * .045f; m.ring.localScale = Vector3.one * r;
                    m.blob.position = w + Vector3.up * .04f; m.blob.localScale = Vector3.one * r * .95f;
                }
                // spawn marker: while it rises out of the floor
                if (m.spawnFade > 0)
                {
                    m.spawnFade = e.spawnT < 1 ? 1 : Mathf.Max(0, m.spawnFade - dt * 3f);
                    m.spawn.gameObject.SetActive(SpawnMarkers && m.spawnFade > 0);
                    m.spawn.position = w + Vector3.up * .05f; m.spawn.localScale = Vector3.one * (r * 1.3f) * (1.1f - .1f * Mathf.Sin(Time.time * 14f));
                }
                // HP bar over a damaged enemy (above its head, facing the camera)
                bool dmg = HpBars && show && e.hp < e.maxHp * .999f && e.maxHp < 1e5f;
                m.bar.gameObject.SetActive(dmg);
                if (dmg && cam != null)
                {
                    float wBar = Mathf.Clamp(.55f + .25f * e.scale, .55f, 1.4f);
                    m.bar.position = w + Vector3.up * (1.0f + 1.1f * e.scale * EnemyScaleNow); m.bar.rotation = cam.transform.rotation; m.bar.localScale = new Vector3(wBar, .09f, 1);
                    float k = Mathf.Clamp01(e.hp / e.maxHp); m.fill.localPosition = new Vector3(-.5f + k * .5f, 0, -.01f); m.fill.localScale = new Vector3(k, .7f, 1);
                }
                // damage numbers: the HP it lost since the last look, summed over ~0.12 s
                if (hpSeen.TryGetValue(e, out var s))
                {
                    float lost = s.hp - e.hp; float acc = s.acc + Mathf.Max(0, lost), t = s.t + dt;
                    if (acc > 0 && (t > .25f || e.dead)) { if (DamageNumbers && show) Number(w + Vector3.up * (1.2f + e.scale), acc); acc = 0; t = 0; }
                    hpSeen[e] = (e.hp, acc, t);
                }
                else hpSeen[e] = (e.hp, 0, 0);
            }
            // gone or dead: release, kill burst
            gone.Clear();
            foreach (var kv in marks) if (kv.Key.dead || !game.swarm.enemies.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var e in gone)
            {
                if (KillBurst && VfxKit.I != null && !e.type.nest && e.dead)
                {
                    var p = MazeMap.ToWorld(e.p, .6f); var c = e.elite ? new Color(1f, .82f, .3f) : new Color(.55f, .95f, 1f);
                    VfxKit.I.FxSparks(p, 7 + Mathf.RoundToInt(4 * e.scale), c, 4.5f, .45f, .09f, 3.5f);
                    VfxKit.I.Flash(p, .9f * e.scale, new Color(c.r, c.g, c.b, .7f), .12f);
                }
                Release(marks[e]); marks.Remove(e); hpSeen.Remove(e);
            }
            // level-up: a gold ring and sparks on the hero
            if (lastLevel >= 0 && game.level > lastLevel && LevelUpBurst) LevelUp(MazeMap.ToWorld(game.player.p, .1f));
            lastLevel = game.level;
            TickPools(dt);
        }

        // ---------------------------------------------------------------- pooled one-shots
        class Pooled { public Transform t; public TextMeshPro text; public float age, life; public Vector3 v, a, b; public float s0, s1; public Material m; public int kind; public Color c; }
        readonly List<Pooled> active = new(); readonly Dictionary<int, Stack<Pooled>> free = new();
        Pooled Get(int kind)
        {
            if (!free.TryGetValue(kind, out var st)) free[kind] = st = new Stack<Pooled>();
            Pooled p;
            if (st.Count > 0) p = st.Pop();
            else
            {
                p = new Pooled { kind = kind };
                if (kind == 0)
                {   // damage number
                    var go = new GameObject("DmgNumber"); go.transform.SetParent(transform, false);
                    var t = go.AddComponent<TextMeshPro>(); t.font = Resources.Load<TMP_FontAsset>("BlueUI/Fonts/Display") ?? TMP_Settings.defaultFontAsset;
                    t.alignment = TextAlignmentOptions.Center; t.fontSize = 3.2f; t.textWrappingMode = TextWrappingModes.NoWrap; t.outlineWidth = .22f; t.outlineColor = new Color32(20, 16, 40, 255);
                    t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    p.t = go.transform; p.text = t;
                }
                else
                {
                    var m = new Material(kind == 1 ? decalMat : kind == 2 ? scorchMat : kind == 3 ? goldMat : lobMat);
                    p.m = m; p.t = Flat("ReadFx" + kind, kind == 4 ? null : kind == 2 ? ZoneHazard.Disc : Fx.Ring, m, transform);
                    if (kind == 4)
                    {   // the Spitter's glob: a small sphere with a trail
                        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(s.GetComponent<Collider>()); s.transform.SetParent(p.t, false); s.transform.localScale = Vector3.one * .38f;
                        var sr = s.GetComponent<MeshRenderer>(); sr.sharedMaterial = m; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        var tr = p.t.gameObject.AddComponent<TrailRenderer>(); tr.time = .22f; tr.startWidth = .26f; tr.endWidth = 0; tr.sharedMaterial = m; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.minVertexDistance = .1f;
                    }
                }
            }
            p.age = 0; p.t.gameObject.SetActive(true); active.Add(p); return p;
        }
        void TickPools(float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i]; p.age += dt; float u = Mathf.Clamp01(p.age / p.life);
                switch (p.kind)
                {
                    case 0:   // number: rises and fades, faces the camera
                        p.t.position = p.a + Vector3.up * (.9f * u - .3f * u * u); if (cam != null) p.t.rotation = cam.transform.rotation;
                        var c = p.c; c.a = u < .65f ? 1 : 1 - (u - .65f) / .35f; p.text.color = c; p.t.localScale = Vector3.one * (u < .12f ? 1.25f - 2f * u : 1f); break;
                    case 1: case 3:   // floor ring: grows and fades
                        p.t.localScale = Vector3.one * Mathf.Lerp(p.s0, p.s1, 1 - (1 - u) * (1 - u)); var cc = p.c; cc.a *= 1 - u; p.m.SetColor("_BaseColor", cc); break;
                    case 2:   // scorch: holds, then fades
                        var sc = p.c; sc.a *= u < .5f ? 1 : 1 - (u - .5f) / .5f; p.m.SetColor("_BaseColor", sc); break;
                    case 4:   // the glob: a lob from a to b
                        var pos = Vector3.Lerp(p.a, p.b, u); pos.y += 4f * 2.2f * u * (1 - u); p.t.position = pos; break;
                }
                if (p.age >= p.life)
                {
                    if (p.kind == 4) { var tr = p.t.GetComponent<TrailRenderer>(); if (tr != null) tr.Clear(); }
                    p.t.gameObject.SetActive(false); active.RemoveAt(i); free[p.kind].Push(p);
                }
            }
        }
        void Number(Vector3 at, float amount)
        {
            if (active.Count > 160) return;
            var p = Get(0); p.life = .7f; p.a = at + new Vector3(Random.Range(-.5f, .5f), Random.Range(0f, .3f), Random.Range(-.3f, .3f));
            int v = Mathf.Max(1, Mathf.RoundToInt(amount)); p.text.text = v.ToString(); p.c = amount >= 60 ? new Color(1f, .85f, .3f) : Color.white; p.text.color = p.c; p.t.position = p.a;
        }
        public static void Explosion(Vector3 at, float r)
        {
            if (!Explosions || I == null || Game.SimRun) return;
            at.y = .06f;
            var a = I.Get(1); a.t.position = at; a.s0 = r * .3f; a.s1 = r * 1.1f; a.life = .45f; a.c = new Color(1f, .62f, .25f, .8f);
            var s = I.Get(2); s.t.position = at - Vector3.up * .01f; s.t.localScale = Vector3.one * r * .5f; s.life = 6f; s.c = new Color(.05f, .04f, .06f, .32f); s.m.SetColor("_BaseColor", s.c);
        }
        void LevelUp(Vector3 at)
        {
            var a = Get(3); a.t.position = at + Vector3.up * .05f; a.s0 = .4f; a.s1 = 2.6f; a.life = .6f; a.c = new Color(1f, .8f, .25f, .9f);
            if (VfxKit.I != null) { VfxKit.I.FxSparks(at + Vector3.up * .8f, 14, new Color(1f, .82f, .3f), 5f, .6f, .1f, 5f); VfxKit.I.Flash(at + Vector3.up * .9f, 2.2f, new Color(1f, .85f, .4f, .8f), .25f); }
        }
        public static void Lob(Vector2 from, Vector2 to, float sec, Vector3? muzzle = null)
        {
            if (!LobTrails || I == null || Game.SimRun) return;
            var p = I.Get(4); p.a = muzzle ?? MazeMap.ToWorld(from, 1.0f); p.b = MazeMap.ToWorld(to, .15f); p.life = Mathf.Max(.25f, sec); p.t.position = p.a;
            // Set every flight, since these objects are pooled across runs/sectors.
            bool cargo = CohesiveZone.Active;
            p.m.SetColor("_BaseColor", cargo ? new Color(.67f, .35f, .83f, .8f) : new Color(.55f, 1f, .25f, 1f));
            var tr = p.t.GetComponent<TrailRenderer>();
            if (tr != null) { tr.time = cargo ? .16f : .22f; tr.startWidth = cargo ? .17f : .26f; tr.Clear(); }
        }

        // ---------------------------------------------------------------- the hero: bones and value
        static readonly Dictionary<Transform, (Transform head, Transform upper, Renderer[] rs)> heroCache = new();
        static readonly MaterialPropertyBlock mpb = new();
        public static void HeroBones(Player p)
        {
            var vis = p.VisualRoot; if (vis == null) return;
            if (!heroCache.TryGetValue(vis, out var h) || h.rs == null || (h.rs.Length > 0 && h.rs[0] == null))
            {
                Transform head = null, upper = null;
                foreach (var t in vis.GetComponentsInChildren<Transform>(true)) { if (t.name.EndsWith(":Head") || t.name == "Head") head = t; if (t.name.EndsWith(":Spine2") || t.name == "Spine2") upper = t; }
                var rs = vis.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                h = (head, upper, rs); heroCache[vis] = h;
            }
            bool chibi = HeroChibi.Tick(vis);   // (the chibi pass: its own materials, head / body / legs; off by default)
            float up = chibi && HeroChibi.ModelOn ? 1f : chibi ? HeroChibi.Upper : HeroLook ? UpperMul : 1f, hd = chibi && HeroChibi.ModelOn ? 1f : chibi ? HeroChibi.Head : HeroLook ? HeadMul : 1f;
            if (h.upper != null) h.upper.localScale = Vector3.one * up;
            if (h.head != null) h.head.localScale = Vector3.one * (hd / up);
            foreach (var r in h.rs)
            {
                if (r == null || r.sharedMaterial == null) continue;
                r.GetPropertyBlock(mpb);
                if (HeroLook && !chibi && r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", r.sharedMaterial.GetColor("_BaseColor") * HeroValue); else mpb.Clear();
                r.SetPropertyBlock(mpb);
            }
        }

        // ---------------------------------------------------------------- tracers: a brighter warm head and a longer tail
        static readonly MaterialPropertyBlock tmpb = new();
        public static void Tracer(Transform t)
        {
            var r = t.GetComponent<MeshRenderer>(); if (r == null) return;
            if (!Tracers) { r.SetPropertyBlock(null); return; }
            var s = t.localScale; s.z *= TracerLen; s.x *= 1.1f; t.localScale = s;
            tmpb.Clear(); tmpb.SetColor("_BaseColor", new Color(1.35f, 1.22f, .95f, 1f)); r.SetPropertyBlock(tmpb);
        }

        // ---------------------------------------------------------------- the CryoSoft floor: calmer
        float iceHideT; GameObject iceObj;
        void FloorPass()
        {
            var info = FindAnyObjectByType<CryoSoftSceneInfo>(); if (info == null) return;
            if (iceHideT > 0) { iceHideT -= Time.deltaTime; if (IceFinish) { var d = GameObject.Find("Cells_i"); if (d != null && d.activeSelf && d.transform.parent != null && d.transform.parent.name == "Hazard_CellDecals") d.SetActive(false); } }
            if (info.gameObject == lastRoom) return; lastRoom = info.gameObject;
            Calm(info); IceSurface(info); iceHideT = 4f;
        }
        void Calm(CryoSoftSceneInfo info)
        {
            if (!FloorCalm) return;
            Color? floorCol = null;
            foreach (var r in info.GetComponentsInChildren<Renderer>(true))
            {
                var n = r.name;
                if (!n.StartsWith("SC_floor_") && !n.StartsWith("SC_marking_")) continue;
                foreach (var m in r.materials)   // (the room's own instances: CryoSoftSceneInfo already cloned them)
                {
                    if (!m.HasProperty("_BaseColor")) continue;
                    var c = m.GetColor("_BaseColor");
                    if (n == "SC_floor_Soft_Floor" || n == "SC_floor_Soft_FloorLight" || n == "SC_floor_Soft_FloorShade")
                    {   // one value step darker and a touch bluer; the light / shade plate tones pulled together (fewer, calmer plates)
                        var target = new Color(c.r * .84f, c.g * .87f, c.b * .97f, c.a);
                        floorCol ??= target; m.SetColor("_BaseColor", Color.Lerp(target, floorCol.Value, n == "SC_floor_Soft_Floor" ? 0 : .7f));
                    }
                    else if (n == "SC_floor_Soft_Seam") m.SetColor("_BaseColor", Color.Lerp(c, floorCol ?? c, .65f));   // softer seams
                    else if (n.StartsWith("SC_marking_")) m.SetColor("_BaseColor", Color.Lerp(c, floorCol ?? new Color(.6f, .66f, .78f), .55f));   // quieter stripes / dock lines
                }
            }
        }

        // ---------------------------------------------------------------- the ice sheets: a finished surface
        static Texture2D iceBase, iceNormal;
        void IceSurface(CryoSoftSceneInfo info)
        {
            if (iceObj != null) Destroy(iceObj);
            if (!IceFinish) return;
            Renderer slab = null; foreach (var r in info.GetComponentsInChildren<Renderer>(true)) if (r.name == "SC_marking_Soft_Ice") slab = r;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>(); float C = MazeMap.C, h = C * .5f;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++)
            {
                var c = MazeMap.Center(x, z); if (ZoneCells.At(c) != 'i') continue;
                var w = MazeMap.ToWorld(c, .058f); int b = verts.Count;
                foreach (var (dx, dz) in new[] { (-h, -h), (-h, h), (h, h), (h, -h) }) { var v = w + new Vector3(dx, 0, dz); verts.Add(v); uvs.Add(new Vector2(v.x / C, v.z / C)); }
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            if (verts.Count == 0) return;
            if (iceBase == null) MakeIceTextures();
            var mesh = new Mesh { name = "IceSurface" }; mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            iceObj = new GameObject("IceSurface"); var mr = iceObj.AddComponent<MeshRenderer>(); iceObj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var m = slab != null ? new Material(slab.sharedMaterial) : new Material(Shader.Find("Maze/CryoSoftSurface"));
            m.name = "IceSurface (finished)";
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", iceBase);
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", iceNormal); m.SetFloat("_NormalStrength", 1f); }
            if (m.HasProperty("_UseVertexAO")) m.SetFloat("_UseVertexAO", 0f);
            if (m.HasProperty("_Gloss")) m.SetFloat("_Gloss", .55f);
            if (m.HasProperty("_BaseColor")) { var c0 = m.GetColor("_BaseColor"); m.SetColor("_BaseColor", Color.Lerp(c0, new Color(.80f, .90f, 1f, 1f), .35f)); }
            mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = true;
            if (slab != null) slab.enabled = false;
        }
        // one 2 m ice tile (512 px): near-white frost so the room's ice tint carries, soft lighter / darker bevel at the plate edge,
        // a few faint cracks; the normal map from the same height
        static void MakeIceTextures()
        {
            const int N = 512; var hgt = new float[N * N]; var col = new Color32[N * N]; var rnd = new System.Random(7);
            var cracks = new List<(Vector2 a, Vector2 b)>();
            for (int k = 0; k < 7; k++) { var a = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble()); for (int sgm = 0; sgm < 4; sgm++) { var b = a + new Vector2((float)rnd.NextDouble() - .5f, (float)rnd.NextDouble() - .5f) * .22f; cracks.Add((a, b)); a = b; } }
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + .5f) / N, v = (y + .5f) / N, e = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));   // distance to the plate edge
                float bevel = Mathf.Clamp01(e / .045f), frost = Mathf.PerlinNoise(u * 6f + 3.1f, v * 6f + 1.7f) * .6f + Mathf.PerlinNoise(u * 19f, v * 19f) * .4f;
                float crack = 0; var p = new Vector2(u, v);
                foreach (var (a, b) in cracks) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); float d = (p - (a + ab * t)).magnitude; crack = Mathf.Max(crack, Mathf.Clamp01(1 - d / .004f)); }
                hgt[y * N + x] = bevel * .6f + frost * .12f - crack * .25f;
                float lum = .92f + .08f * frost - .10f * crack;
                lum *= Mathf.Lerp(.86f, 1f, bevel);                                   // the groove at the plate edge
                if (e > .045f && e < .07f) lum += .07f * (1 - Mathf.Abs(e - .057f) / .013f);   // a lit bevel lip just inside it
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(lum * 255), 0, 255);
                col[y * N + x] = new Color32((byte)Mathf.Clamp(g - 6, 0, 255), g, 255, 255);
            }
            iceBase = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "IceBase", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 }; iceBase.SetPixels32(col); iceBase.Apply(true, true);
            var nrm = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float hx = hgt[y * N + (x + 1) % N] - hgt[y * N + (x + N - 1) % N], hy = hgt[((y + 1) % N) * N + x] - hgt[((y + N - 1) % N) * N + x];
                var n = new Vector3(-hx * 6f, -hy * 6f, 1).normalized;
                nrm[y * N + x] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
            }
            iceNormal = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "IceNormal", wrapMode = TextureWrapMode.Repeat }; iceNormal.SetPixels32(nrm); iceNormal.Apply(true, true);
        }
    }
}
