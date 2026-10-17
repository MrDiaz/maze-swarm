using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    public class Enemy
    {
        public Vector2 p, v, k;      // plane position, steering velocity, knockback velocity
        public float hp, maxHp, r, scale, speed, damage, atkCd, atkAnim, yaw, walk, spawnT, flash;
        public bool dead, elite, egg, breaker;   // egg: a rooted egg cluster (nest view, never spawns on its own; ZoneCells hatches it)
        public float frozen;            // cryo: can't move or attack, takes +50% damage
        public float slowT, burnT, cryoWin; public int cryoHits;
        public float acidUntil; public int needles; public bool settled; public Vector2 stillFrom; public float stillT; public bool stuckLogged;   // (sims: the stuck-enemy check)   // (Twin Needlers: needles landed, every 6th pops)          // ACID GLAND relic: takes more damage until this run time
        public Elites.Mod mods; public int shieldHits; public float shieldRegrow, armourHp, noDamageT, toxicT;   // enemy modifiers   // power-ups: Cryo Rounds slow / freeze build-up, Incendiary burn
        public short[] field; public Vector2 fieldAt;   // set: walks this flow field to fieldAt instead of the hero (DEFEND)
        // EnemyAI (the enemies.json roster): state machine, timers, aim, ward, cloak / underground, frenzy
        public int aiState; public float aiT, aiCd, frenzy, revealT; public Vector2 aiDir; public bool aiHit, under, cloaked;
        public EnemyAI.Tele aiTele; public Enemy ward; public BossBrain brain;
        public bool Hidden => under || cloaked;
        public EnemyType type;
        public int typeIndex;
        public EnemyView view;
        // Bloater: time to the next mini burst, the burst wind-up left, live minis, drip timer
        public float special, cast = -1, drip;
        public int minis;
        public Enemy owner;          // a mini's Bloater
        public float hop = 1;        // a mini's leap out of the sac, 0..1 (1 = landed)
        public float squash = -1;    // a Bloater's squash-and-stretch as a sac bursts (time since, < 0 = none)
        public bool boss, marked;    // a run's floor-5 boss; marked: already hit once (the MARKSMAN card)
        // Leaper: 0 stalk, 1 crouch (telegraph), 2 pounce, 3 stunned / recovering
        public int leap; public float leapT, leapCd, dodgeCd, stunFor; public Vector2 leapDir; public bool leapHit;
        public Transform tele;       // its telegraph on the floor while crouching
        public float spawnCd;        // a nest: time to its next Drone
    }

    // blocky robot (body, head, red eyes, swinging legs and arms), or a skinned model with a Walk clip
    public class EnemyView
    {
        public Transform root, body, head, legL, legR, armL, armR;
        public Renderer[] skin;
        public int typeIndex = -1;
        public bool flashing;
        public Transform model;     // skinned model views only
        public AnimationState walk;
        public Transform belly;     // Bloater: the sac bone, swelled on top of the clip
        public MeshRenderer ooze;   // Bloater: the pulsing pus ring on the floor
        public float lastPhase;     // Bloater: walk phase last frame (footfall thuds)
        public bool nest;           // a hive nest (own pool, pulses instead of walking)
        public Animation anim; public string clip; // Leaper: the clip playing (Walk / Crouch / Leap / Land / Sidestep)
    }

    // The swarm: spawning, flow-field steering, crowd separation, attacks and the walk animation.
    public class Swarm : MonoBehaviour
    {
        public readonly List<Enemy> enemies = new();
        public float HpScale = 1, DmgScale = 1, SpeedScale = 1; // a run's sector x floor scaling (1 outside runs)
        public float NestEvery = 2.4f; public int NestCap = 40; readonly List<Vector2> nestOut = new();
        // where a nest lets a Drone out: on an open cell clear of its body, the side facing the hero first. A random point on
        // the ring could land in the wall of a corner nest; the wall pushed the Drone back into the nest and it stayed there
        // wedged (HUNT floors looked like nothing started: the nests' Drones stood idle at the nests).
        public static int StuckCount; public static bool NoSettle = System.Environment.GetEnvironmentVariable("SIM_NOSETTLE") != null;   // (sims)
        bool NestExit(Enemy n, out Vector2 at) => OpenSpot(n.p, n.r + 0.8f, n.r + 2.4f, 0.45f, out at);

        // ---- one rule for every emitter (nests, Bloater / Gravid spits, Splitter pieces, egg hatches, boss adds and summons,
        // script SPAWNs, packs): a new enemy starts on an open cell, clear of the walls, or the nearest such spot around it
        // (the hero's side first); nothing found: where it was. A point inside a wall wedged the enemy for good.
        public bool Clear(Vector2 p, float r)
        {
            int x = Mathf.FloorToInt(p.x / MazeMap.C), z = Mathf.FloorToInt(p.y / MazeMap.C);
            if (!MazeMap.FlowOpen(x, z)) return false;
            var q = p; MazeMap.CollideGrid(ref q, Mathf.Min(r, 0.9f)); return (q - p).sqrMagnitude < 0.01f;
        }
        public bool OpenSpot(Vector2 c, float rMin, float rMax, float bodyR, out Vector2 at)
        {
            var toHero = game.player.p - c; float a0 = toHero.sqrMagnitude > 0.01f ? Mathf.Atan2(toHero.y, toHero.x) : Random.value * 6.283f;
            for (float r = rMin; r <= rMax + 1e-3f; r += 0.8f)
                for (int k = 0; k < 12; k++)
                {
                    float a = a0 + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 0.5236f + Random.Range(-0.15f, 0.15f);   // fan out from the hero side, 30 degrees a step
                    var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (Clear(p, bodyR) && LineOpen(c, p)) { at = p; return true; }   // and reachable in a straight line (never across a wall)
                }
            at = default; return false;
        }
        static bool LineOpen(Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude; int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.3f));
            int a0 = MazeMap.CellIndex(a);
            for (int i = 1; i <= n; i++) { var q = Vector2.Lerp(a, b, i / (float)n); if (MazeMap.CellIndex(q) == a0) continue; if (!MazeMap.Walkable(Mathf.FloorToInt(q.x / MazeMap.C), Mathf.FloorToInt(q.y / MazeMap.C))) return false; }   // (the origin's own cell may be the wall it landed in)
            return true;
        }
        public Vector2 SafeSpot(Vector2 p, float r) => Clear(p, r) ? p : OpenSpot(p, 0.8f, 4f, r, out var a) ? a : p;
        int nestType = -2; public int NestType => nestType != -2 ? nestType : nestType = TypeIndex(t => t.nest);
        readonly Stack<EnemyView> pool = new();
        Mesh cube;
        Material[] typeMats;
        Material flashMat, eyeMat, swellMat, pusFlashMat, oozeMat;
        MaterialPropertyBlock oozeMpb;
        Mesh quad;
        readonly Light[] pusLights = new Light[3];
        public int miniCount;       // live minis, all Bloaters
        readonly List<Enemy> bursts = new();

        // skinned model types (the Drone): one pool per type, one shared material per type
        GameObject[] modelPrefabs;
        Stack<EnemyView>[] modelPools;
        float[] modelFit;           // model scale so its footprint is ~2.4 x the enemy radius
        float[] modelLift;          // raise so the feet sit on the floor (the Bloater's mesh hangs below its origin)
        const float StrideLen = 1.25f; // ground covered by one Walk cycle at model scale 1

        // killed model enemies swell, then pop into goo
        class Dying { public EnemyView v; public float t, dur, s; public bool bloater; public Vector2 p; public float yaw; }
        readonly List<Dying> dying = new();
        public bool AnyDying => dying.Count > 0; // e.g. a Bloater still to burst into its last minis
        readonly Stack<Dying> dyingPool = new();
        Game game;

        // spatial hash (linked lists in arrays)
        const float HC = 1.6f;
        int HW, HH;
        int[] hashHead, hashNext = new int[512];

        public void Init(Game g)
        {
            game = g;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            var types = g.cfg.enemyTypes;
            typeMats = new Material[types.Length];
            for (int i = 0; i < types.Length; i++) typeMats[i] = Mats.Lit(types[i].color, null, null, 0.45f);
            flashMat = Mats.Lit(Color.white, null, null, 0.3f);
            eyeMat = Mats.Unlit(new Color(1f, 0.23f, 0.18f));
            swellMat = Mats.Lit(new Color(0.78f, 0.5f, 1f), null, null, 0.8f);
            modelPrefabs = new GameObject[types.Length];
            modelPools = new Stack<EnemyView>[types.Length];
            modelFit = new float[types.Length]; modelLift = new float[types.Length];
            pusFlashMat = Mats.Lit(new Color(0.75f, 1f, 0.45f), null, null, 0.6f);
            oozeMat = Mats.Fade(new Color(0.55f, 1f, 0.2f, 0.4f), Resources.Load<Texture2D>("Fx/ring"));
            oozeMpb = new MaterialPropertyBlock();
            var flat = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = flat.GetComponent<MeshFilter>().sharedMesh; Destroy(flat);
            for (int i = 0; i < types.Length; i++)
            {
                if (string.IsNullOrEmpty(types[i].model)) continue;
                modelPrefabs[i] = CohesiveCharacterPolicy.EnemyPrefab(types[i], "Models/" + types[i].model);
                if (modelPrefabs[i] == null) { Debug.LogWarning($"Maze: Resources/Models/{types[i].model} not found, {types[i].name} uses the blocky robot."); continue; }
                modelPools[i] = new Stack<EnemyView>();
                int warm = types[i].ai != null ? 3 : types[i].bloater || types[i].leaper ? 4 : types[i].mini ? 12 : 24;
                for (int k = 0; k < warm; k++)
                {
                    var v = NewModelView(i);
                    if (k == 0 && types[i].leaper && types[i].model == "drone")
                    {   // placeholder look: the Drone, sickly orange-red
                        var m = new Material(typeMats[i]) { name = "LeaperDrone" };
                        m.SetColor("_BaseColor", new Color(1.9f, 0.75f, 0.32f));
                        typeMats[i] = m; SetSkin(v, m);
                    }
                    if (k == 0 && types[i].mini && !CohesiveZone.For(game))
                    {   // the mini: a greener copy of the Drone's shared material
                        var m = new Material(typeMats[i]) { name = "MiniDrone" };
                        m.SetColor("_BaseColor", m.GetColor("_BaseColor") * new Color(0.62f, 1.15f, 0.5f));
                        typeMats[i] = m; SetSkin(v, m);
                    }
                    v.root.gameObject.SetActive(false); modelPools[i].Push(v);
                }
            }
            WarmUpModels();
            for (int i = 0; i < pusLights.Length; i++)
            {
                var lg = new GameObject("PusLight" + i); lg.transform.SetParent(transform, false);
                var l = pusLights[i] = lg.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(0.5f, 1f, 0.25f); l.range = 5f; l.shadows = LightShadows.None; l.enabled = false;
            }
            ResizeHash();
        }

        // draw one pooled view of each model type once, offscreen, so the GPU compiles its shader variants (the
        // Bloater's normal + metallic map) at load instead of hitching on the first spawn mid-fight
        void WarmUpModels()
        {
            var rt = RenderTexture.GetTemporary(64, 64, 16);
            var cg = new GameObject("WarmUpCam"); var c = cg.AddComponent<Camera>();
            c.enabled = false; c.targetTexture = rt; c.clearFlags = CameraClearFlags.SolidColor; c.cullingMask = ~0;
            var at = new Vector3(0, -500, 0);
            cg.transform.SetPositionAndRotation(at + new Vector3(0, 3, -6), Quaternion.Euler(25, 0, 0));
            var shown = new List<EnemyView>();
            for (int i = 0; i < modelPools.Length; i++)
                if (modelPools[i] != null && modelPools[i].Count > 0)
                {
                    var v = modelPools[i].Peek(); v.root.gameObject.SetActive(true); v.root.position = at + Vector3.right * (shown.Count * 3 - 3); shown.Add(v);
                }
            c.Render();
            foreach (var v in shown) v.root.gameObject.SetActive(false);
            c.targetTexture = null; RenderTexture.ReleaseTemporary(rt); Destroy(cg);
        }

        public void ResizeHash()
        {
            HW = Mathf.CeilToInt(MazeMap.W * MazeMap.C / HC) + 1;
            HH = Mathf.CeilToInt(MazeMap.H * MazeMap.C / HC) + 1;
            hashHead = new int[HW * HH];
        }

        Transform Part(Transform parent, Vector3 pos, Vector3 size, Material m, bool shadow, List<Renderer> skin)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (skin != null) skin.Add(mr);
            return go.transform;
        }
        Transform Pivot(Transform parent, Vector3 pos, Vector3 size, float len, Material m, List<Renderer> skin)
        {
            var pv = new GameObject("pivot").transform;
            pv.SetParent(parent, false); pv.localPosition = pos;
            Part(pv, new Vector3(0, -len / 2, 0), size, m, true, skin);
            return pv;
        }

        EnemyView NewView()
        {
            var v = new EnemyView();
            var skin = new List<Renderer>();
            v.root = new GameObject("Enemy").transform;
            v.root.SetParent(transform, false);
            var m = typeMats[0];
            v.body = Part(v.root, new Vector3(0, 0.73f, 0), new Vector3(0.72f, 0.62f, 0.5f), m, true, skin);
            v.head = Part(v.root, new Vector3(0, 1.28f, 0), new Vector3(0.48f, 0.44f, 0.46f), m, true, skin);
            Part(v.head, new Vector3(-0.23f, 0.09f, 0.52f), new Vector3(0.23f, 0.14f, 0.07f), eyeMat, false, null);
            Part(v.head, new Vector3(0.23f, 0.09f, 0.52f), new Vector3(0.23f, 0.14f, 0.07f), eyeMat, false, null);
            v.legL = Pivot(v.root, new Vector3(-0.17f, 0.43f, 0), new Vector3(0.21f, 0.42f, 0.24f), 0.42f, m, skin);
            v.legR = Pivot(v.root, new Vector3(0.17f, 0.43f, 0), new Vector3(0.21f, 0.42f, 0.24f), 0.42f, m, skin);
            v.armL = Pivot(v.root, new Vector3(-0.44f, 0.98f, 0), new Vector3(0.15f, 0.42f, 0.16f), 0.42f, m, skin);
            v.armR = Pivot(v.root, new Vector3(0.44f, 0.98f, 0), new Vector3(0.15f, 0.42f, 0.16f), 0.42f, m, skin);
            v.skin = skin.ToArray();
            return v;
        }

        // a hive nest: a cluster of glossy purple-black sacs with green glowing cores (pulses in Sync)
        readonly Stack<EnemyView> nestPool = new(); Material nestGlow;
        EnemyView NewNestView(int ti)
        {
            var v = new EnemyView { nest = true, typeIndex = ti };
            var skin = new List<Renderer>();
            v.root = new GameObject("Nest").transform; v.root.SetParent(transform, false);
            nestGlow ??= Mats.Unlit(CohesiveZone.Enabled ? new Color(.38f, .87f, .91f) : new Color(0.55f, 1f, 0.3f));
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); var sphere = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            v.body = new GameObject("Sacs").transform; v.body.SetParent(v.root, false);
            var rng = new System.Random(ti * 31);
            for (int k = 0; k < 7; k++)
            {
                float a = k * 0.9f, r = k == 0 ? 0 : 0.42f + 0.1f * (float)rng.NextDouble();
                var go = new GameObject("Sac"); go.transform.SetParent(v.body, false);
                go.transform.localPosition = new Vector3(Mathf.Cos(a) * r, k == 0 ? 0.55f : 0.32f, Mathf.Sin(a) * r);
                go.transform.localScale = Vector3.one * (k == 0 ? 0.95f : 0.5f + 0.15f * (float)rng.NextDouble());
                go.AddComponent<MeshFilter>().sharedMesh = sphere;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = typeMats[ti]; skin.Add(mr);
                var core = new GameObject("Core"); core.transform.SetParent(go.transform, false); core.transform.localPosition = new Vector3(0, 0.12f, 0.32f); core.transform.localScale = Vector3.one * 0.42f;
                core.AddComponent<MeshFilter>().sharedMesh = sphere; var cr = core.AddComponent<MeshRenderer>(); cr.sharedMaterial = nestGlow; cr.shadowCastingMode = ShadowCastingMode.Off;
            }
            v.skin = skin.ToArray();
            return v;
        }

        EnemyView NewModelView(int ti)
        {
            var v = new EnemyView { typeIndex = ti };
            v.root = new GameObject(game.cfg.enemyTypes[ti].name).transform;
            v.root.SetParent(transform, false);
            var go = Instantiate(modelPrefabs[ti]);
            bool cargo = CohesiveZone.For(game);
            bool authoredSpitter = cargo && CohesiveCharacterPolicy.IsAuthoredSpitterPrefab(modelPrefabs[ti]);
            bool authoredFamily = cargo && CohesiveCharacterPolicy.IsAuthoredFamilyPrefab(modelPrefabs[ti]);
            bool authored = authoredSpitter || authoredFamily;
            Mats.ConvertGltf(go); // shared URP material (cached per glTF material)
            v.model = go.transform;
            v.model.SetParent(v.root, false);
            var rs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in rs)
            {
                r.quality = authored ? SkinQuality.Bone4 : SkinQuality.Bone2;
                r.updateWhenOffscreen = false;
                r.skinnedMotionVectors = false;
                if (authored) { var culling = r.localBounds; culling.Expand(.24f); r.localBounds = culling; }
                r.shadowCastingMode = ShadowCastingMode.On;
                var tex = r.sharedMaterial != null ? r.sharedMaterial.GetTexture("_BaseMap") : null;
                if (tex != null && tex.width <= 128) tex.filterMode = FilterMode.Point; // flat-colour atlas: one texel per triangle
            }
            v.skin = rs;
            if (modelFit[ti] == 0)
            {
                // footprint in model space (bind pose), facing +Z; lift it if the feet hang below y = 0
                var b = rs.Length > 0 ? rs[0].sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                if (CohesiveZone.For(game) && CohesiveCharacterPolicy.TryMeshBounds(go.transform, out var measured)) b = measured;
                modelFit[ti] = 2.4f * game.cfg.enemy.radius / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
                modelLift[ti] = CohesiveZone.For(game) ? -b.min.y * modelFit[ti] : b.min.y < -0.05f ? -b.min.y * modelFit[ti] : 0;
                if (rs.Length > 0) typeMats[ti] = rs[0].sharedMaterial;
                var tt = game.cfg.enemyTypes[ti];
                if (!CohesiveZone.For(game) && rs.Length > 0 && tt.ai != null && tt.ai != "boss")
                {   // the enemies.json roster's placeholder look: the base model, tinted its own colour
                    var m = new Material(rs[0].sharedMaterial) { name = tt.name + "_tint" };
                    var c = Color.Lerp(tt.color, Color.white, 0.15f) * 1.6f; c.a = 1;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    typeMats[ti] = m;
                }
            }
            if (!CohesiveZone.For(game) && game.cfg.enemyTypes[ti].ai != null && game.cfg.enemyTypes[ti].ai != "boss") SetSkin(v, typeMats[ti]);   // the roster's tint
            v.model.localScale = Vector3.one * modelFit[ti];
            v.model.localPosition = new Vector3(0, modelLift[ti], 0);
            if (game.cfg.enemyTypes[ti].bloater)
            {
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    if (t.name == (authoredFamily ? "CarrierBelly" : "Belly")) { v.belly = t; break; }
                var ring = new GameObject("Ooze");
                ring.transform.SetParent(v.root, false);
                ring.transform.localPosition = new Vector3(0, 0.03f, 0);
                ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
                ring.transform.localScale = Vector3.one * 1.45f;
                ring.AddComponent<MeshFilter>().sharedMesh = quad;
                v.ooze = ring.AddComponent<MeshRenderer>(); v.ooze.sharedMaterial = oozeMat;
                v.ooze.shadowCastingMode = ShadowCastingMode.Off; v.ooze.receiveShadows = false;
            }
            if (!CohesiveZone.For(game) && typeMats[ti] != null) SetSkin(v, typeMats[ti]);
            var anim = go.GetComponentInChildren<Animation>();
            if (anim != null)
            {
                anim.cullingType = AnimationCullingType.BasedOnRenderers; v.anim = anim;
                foreach (AnimationState st in anim) if (v.walk == null || st.name.ToLower().Contains("walk")) v.walk = st;
                if (v.walk != null) { v.walk.wrapMode = WrapMode.Loop; anim.Play(v.walk.name); }
            }
            else Debug.LogWarning("Maze: enemy model has no legacy Animation component: " + game.cfg.enemyTypes[ti].name + " / " + game.cfg.enemyTypes[ti].model + ".");
            if (cargo) CohesiveEnemyStyle.Attach(go, game.cfg.enemyTypes[ti].name, authored);
            if (authoredFamily) CohesiveFamilyPlayback.Attach(go, CohesiveCharacterPolicy.RoleFor(game.cfg.enemyTypes[ti]));
            return v;
        }

        // Called before a pool pop. Route changes already Clear live actors. Retire only
        // inactive pooled views, and defer a switch if an active actor of this type remains.
        void EnsureModelPresentation(int ti)
        {
            var type = game.cfg.enemyTypes[ti]; if (string.IsNullOrEmpty(type.model)) return;
            var wanted = CohesiveCharacterPolicy.EnemyPrefab(type, "Models/" + type.model);
            if (wanted == modelPrefabs[ti]) return;
            foreach (var actor in enemies)
                if (actor.typeIndex == ti && actor.view != null && actor.view.root.gameObject.activeSelf) return;
            foreach (var actor in pendingAdd)
                if (actor.typeIndex == ti && actor.view != null && actor.view.root.gameObject.activeSelf) return;
            foreach (var death in dying)
                if (death.v.typeIndex == ti && death.v.root.gameObject.activeSelf) return;
            if (modelPools[ti] != null)
            {
                while (modelPools[ti].Count > 0)
                {
                    var stale = modelPools[ti].Pop();
                    if (stale.root != null) Destroy(stale.root.gameObject);
                }
            }
            modelPrefabs[ti] = wanted;
            modelPools[ti] = wanted != null ? new Stack<EnemyView>() : null;
            modelFit[ti] = modelLift[ti] = 0;
        }

        void SetSkin(EnemyView v, Material m)
        {
            var styled = v.model != null ? v.model.GetComponent<CohesiveEnemyStyle>() : null;
            if (styled != null) { styled.SetFlash(m == flashMat || m == pusFlashMat); return; }
            foreach (var r in v.skin) r.sharedMaterial = m;
        }

        int deferDepth; readonly List<Enemy> pendingAdd = new();
        bool iterating => deferDepth > 0;
        // spawns made while enemies are being iterated (the swarm update, any kill) join afterwards
        public void Defer(bool on) { if (on) deferDepth++; else if (--deferDepth <= 0) { deferDepth = 0; if (pendingAdd.Count > 0) { enemies.AddRange(pendingAdd); pendingAdd.Clear(); } } }
        public Enemy Spawn(Vector2 at, int wave, int forceType = -1)
        {
            var cfg = game.cfg;
            int ti = forceType >= 0 ? forceType : PickType(wave);
            var t = cfg.enemyTypes[ti];
            int n = wave - 1;
            bool rooted = t.ai == "spore_pod";
            float hp = cfg.enemy.hp * t.hp * (t.fixedHp ? 1 : 1 + cfg.waves.hpGrowth * n) * HpScale;
            var e = new Enemy
            {
                p = at + new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(-0.6f, 0.6f)),
                hp = hp, maxHp = hp, type = t, typeIndex = ti, scale = t.scale, r = cfg.enemy.radius * t.scale,
                speed = SpeedScale * cfg.enemy.speed * t.speed * Mathf.Min(cfg.waves.maxSpeedMult, 1 + cfg.waves.speedGrowth * n) * Random.Range(0.9f, 1.1f),
                damage = cfg.enemy.damage * t.damage * DmgScale, atkCd = Random.Range(0f, 0.5f),
                yaw = Random.Range(0f, 6.28f), walk = Random.Range(0f, 6.28f),
            };
            EnsureModelPresentation(ti);
            var mp = modelPools[ti];
            if (t.nest) { e.view = nestPool.Count > 0 ? nestPool.Pop() : NewNestView(ti); e.spawnCd = 1.5f + Random.value; }
            else if (mp != null) e.view = mp.Count > 0 ? mp.Pop() : NewModelView(ti);
            else e.view = pool.Count > 0 ? pool.Pop() : NewView();
            var v = e.view;
            v.root.gameObject.SetActive(true);
            if (v.typeIndex != ti || v.flashing) { SetSkin(v, typeMats[ti]); v.typeIndex = ti; v.flashing = false; }
            if (v.model != null)
            {
                v.model.localPosition = new Vector3(0, modelLift[ti], 0); v.model.localRotation = Quaternion.identity;
                if (v.walk != null) { v.walk.enabled = true; v.walk.weight = 1; v.walk.time = Random.value * v.walk.length; v.walk.speed = 2f; }
            }
            if (t.bloater) { e.special = 2.5f + Random.value * 2f; e.drip = Random.value * 0.4f; }
            if (t.leaper) { e.leapCd = 1.2f + Random.value; e.dodgeCd = 0; }
            if (rooted) { e.egg = true; e.spawnCd = 1e9f; }
            if (t.ai != null) { e.aiCd = 1f + Random.value * 2f; e.aiT = 0; }
            if (hashNext.Length < enemies.Count + 1) System.Array.Resize(ref hashNext, hashNext.Length * 2);
            if (iterating) pendingAdd.Add(e); else enemies.Add(e);   // spawned mid-update (boss adds, splits): joins after the loop
            return e;
        }

        readonly Dictionary<string, int> byName = new();
        public int TypeByName(string n)
        {
            if (string.IsNullOrEmpty(n)) return -1;
            if (byName.TryGetValue(n, out int i)) return i;
            i = TypeIndex(t => t.name == n);   // an exact type name first (splitter_m), then the spec's names
            var key = EnemyRegistry.Resolve(n);
            if (i < 0) i = TypeIndex(t => t.name == key);
            if (i < 0) { Debug.LogWarning($"Maze: enemy '{n}' has no type yet, it spawns as a grunt"); i = TypeIndex(t => t.name == "grunt"); }
            byName[n] = i; return i;
        }
        int TypeIndex(System.Func<EnemyType, bool> f) { var ts = game.cfg.enemyTypes; for (int i = 0; i < ts.Length; i++) if (f(ts[i])) return i; return -1; }
        int bloaterType = -2, miniType = -2;
        public int BloaterType => bloaterType != -2 ? bloaterType : bloaterType = TypeIndex(t => t.bloater);
        int MiniType => miniType != -2 ? miniType : miniType = TypeIndex(t => t.mini);
        int leaperType = -2;
        public int LeaperType => leaperType != -2 ? leaperType : leaperType = TypeIndex(t => t.leaper);

        // a mini leaps out of a Bloater's sac along `dir` (plane), lands and skitters
        Enemy SpawnMini(Enemy owner, Vector2 at, Vector2 dir)
        {
            var B = game.cfg.bloater;
            if (MiniType < 0 || miniCount >= B.capTotal) return null;
            var m = Spawn(at, Mathf.Max(1, game.wave), MiniType);
            m.p = at; m.spawnT = 1; m.hop = 0; m.owner = owner;
            m.k = dir * Random.Range(4f, 6.5f); m.yaw = Mathf.Atan2(dir.x, dir.y);
            if (owner != null) owner.minis++;
            miniCount++;
            return m;
        }

        // the sac bursts: a green splat and minis in an arc ahead of the Bloater
        void BurstMinis(Enemy b, int n, bool death)
        {
            var fwd = new Vector2(Mathf.Sin(b.yaw), Mathf.Cos(b.yaw));
            var at = b.p + fwd * b.r * 0.4f;
            for (int i = 0; i < n; i++)
            {
                if (!death && b.minis >= game.cfg.bloater.capPerBloater) break;
                float a = (n == 1 ? 0 : (i / (n - 1f) - 0.5f) * (death ? 4.4f : 1.6f)) + Random.Range(-0.15f, 0.15f);
                var d = new Vector2(fwd.x * Mathf.Cos(a) + fwd.y * Mathf.Sin(a), -fwd.x * Mathf.Sin(a) + fwd.y * Mathf.Cos(a));
                SpawnMini(death ? null : b, at + d * 0.3f, d);
            }
        }

        int PickType(int wave)
        {
            var types = game.cfg.enemyTypes;
            float total = 0;
            foreach (var t in types) if (wave >= t.fromWave) total += t.weight;
            float r = Random.value * total;
            for (int i = 0; i < types.Length; i++)
            {
                if (wave < types[i].fromWave) continue;
                r -= types[i].weight;
                if (r <= 0) return i;
            }
            return 0;
        }

        void HideTele(Enemy e) { if (e.tele != null) { e.tele.gameObject.SetActive(false); telePool.Push(e.tele); e.tele = null; } }
        void Release(EnemyView v)
        {
            v.root.gameObject.SetActive(false);
            if (v.ooze != null) v.ooze.enabled = true;
            (v.nest ? nestPool : v.model != null ? modelPools[v.typeIndex] : pool).Push(v);
        }
        public void Despawn(Enemy e) => Release(e.view);

        public void Compact()
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
                if (enemies[i].dead)
                {
                    var e = enemies[i];
                    if (e.type.mini) { miniCount--; if (e.owner != null) e.owner.minis--; }
                    HideTele(e);
                    if (e.view.model != null) StartDeath(e); else Despawn(e);
                    enemies[i] = enemies[^1]; enemies.RemoveAt(enemies.Count - 1);
                }
        }

        public void Clear()
        {
            foreach (var e in enemies) { HideTele(e); Despawn(e); }
            enemies.Clear(); miniCount = 0;
            foreach (var l in pusLights) l.enabled = false;
            foreach (var d in dying) { Release(d.v); dyingPool.Push(d); }
            dying.Clear();
        }

        // ---- death: swell ~25% for a beat, then pop into goo
        void StartDeath(Enemy e)
        {
            var d = dyingPool.Count > 0 ? dyingPool.Pop() : new Dying();
            d.v = e.view; d.t = 0; d.s = e.view.root.localScale.x;
            d.bloater = e.type.bloater; d.p = e.p; d.yaw = e.yaw;
            d.dur = d.bloater ? 0.25f : 0.08f + Random.value * 0.05f; // a crowd killed at once pops as a ripple, not a single frame
            SetSkin(d.v, d.bloater ? pusFlashMat : swellMat); d.v.flashing = true;
            if (d.v.walk != null) d.v.walk.speed = 3.5f; // last twitch
            dying.Add(d);
        }

        public void TickDying(float dt)
        {
            for (int i = dying.Count - 1; i >= 0; i--)
            {
                var d = dying[i]; d.t += dt;
                float k = Mathf.Clamp01(d.t / d.dur), sw = 1 - (1 - k) * (1 - k);
                float s = d.s * (1 + (d.bloater ? 0.18f : 0.25f) * sw);
                d.v.root.localScale = new Vector3(s, s * (1 + 0.08f * sw), s);
                float sh = d.bloater ? 7f : 4f;
                d.v.model.localRotation = Quaternion.Euler(Random.Range(-sh, sh) * sw, 0, Random.Range(-sh, sh) * sw); // shiver
                if (d.t < d.dur) continue;
                if (d.bloater)
                {   // a big green burst, a puddle that lingers, and the last minis let loose
                    Fx.I.PusBurst(d.v.root.position + Vector3.up * (0.35f * d.s), d.s);
                    if (CombatAudio.I != null) CombatAudio.I.BloaterBurst();
                    game.AddShake(game.cfg.bloater.deathShake);
                    var B = game.cfg.bloater;
                    BurstMinis(new Enemy { p = d.p, yaw = d.yaw, r = 1 }, Random.Range(B.deathMinisMin, B.deathMinisMax + 1), true);
                    if (d.v.ooze != null) d.v.ooze.enabled = false;
                }
                else
                {
                    Fx.I.GooBurst(d.v.root.position + Vector3.up * (0.45f * d.s), d.s);
                    if (CombatAudio.I != null) CombatAudio.I.Squish();
                }
                Release(d.v);
                dying[i] = dying[^1]; dying.RemoveAt(dying.Count - 1);
                dyingPool.Push(d);
            }
        }

        public void RebuildHash()
        {
            for (int i = 0; i < hashHead.Length; i++) hashHead[i] = -1;
            if (hashNext.Length < enemies.Count) System.Array.Resize(ref hashNext, enemies.Count * 2);
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                int k = Mathf.Clamp(Mathf.FloorToInt(e.p.y / HC), 0, HH - 1) * HW + Mathf.Clamp(Mathf.FloorToInt(e.p.x / HC), 0, HW - 1);
                hashNext[i] = hashHead[k]; hashHead[k] = i;
            }
        }

        // calls fn for enemies in the 3x3 hash cells around p; fn returns false to stop
        public void ForNear(Vector2 p, System.Func<Enemy, bool> fn)
        {
            int cx = Mathf.FloorToInt(p.x / HC), cz = Mathf.FloorToInt(p.y / HC);
            for (int j = cz - 1; j <= cz + 1; j++)
            {
                if (j < 0 || j >= HH) continue;
                for (int i = cx - 1; i <= cx + 1; i++)
                {
                    if (i < 0 || i >= HW) continue;
                    for (int k = hashHead[j * HW + i]; k != -1; k = hashNext[k])
                        if (!fn(enemies[k])) return;
                }
            }
        }

        void Separate(Enemy e)
        {
            int cx = Mathf.FloorToInt(e.p.x / HC), cz = Mathf.FloorToInt(e.p.y / HC);
            for (int j = cz - 1; j <= cz + 1; j++)
            {
                if (j < 0 || j >= HH) continue;
                for (int i = cx - 1; i <= cx + 1; i++)
                {
                    if (i < 0 || i >= HW) continue;
                    for (int k = hashHead[j * HW + i]; k != -1; k = hashNext[k])
                    {
                        var o = enemies[k];
                        if (o == e) continue;
                        var s = e.p - o.p; float rr = e.r + o.r, d2 = s.sqrMagnitude;
                        if (d2 < rr * rr && d2 > 1e-6f)
                        {
                            float d = Mathf.Sqrt(d2), push = (rr - d) * 0.5f * (o.scale / (e.scale + o.scale));
                            e.p += s / d * push;
                        }
                    }
                }
            }
        }

        public void Tick(float dt, Player pl)
        {
            var cfg = game.cfg; var E = cfg.enemy;
            game.flowT -= dt;
            int pcell = MazeMap.CellIndex(pl.p);
            if (pcell != MazeMap.flowCell && game.flowT <= 0) { MazeMap.ComputeFlow(pl.p); game.flowT = E.flowRefresh; }

            RebuildHash();
            int W = MazeMap.W;
            Defer(true);
            foreach (var e in enemies)
            {
                if (e.spawnT < 1) e.spawnT = Mathf.Min(1, e.spawnT + dt * 2.2f);
                if (!e.settled) { e.settled = true; if (!e.type.nest && !e.egg && !e.under && !NoSettle) e.p = SafeSpot(e.p, e.r); }   // every emitter: never start inside a wall
                if (Game.SimRun && !e.type.nest && !e.egg && e.type.ai != "spore_pod" && !e.stuckLogged)
                {   // (sims) an enemy that hasn't moved 0.3 m in 10 s with the hero within 25 m and out of its reach: stuck
                    float dh = (pl.p - e.p).magnitude;
                    if ((e.p - e.stillFrom).sqrMagnitude > 0.09f || dh > 25f || dh < 2.5f || e.frozen > 0 || e.under || e.aiState == 2 && e.type.ai == "tick") { e.stillFrom = e.p; e.stillT = 0; }
                    else if ((e.stillT += dt) > 10f) { e.stuckLogged = true; StuckCount++; Debug.Log($"Maze stuck: {game.sector?.id} F{game.floor} {e.type.name}{(e.boss ? " BOSS" : "")} at {e.p} cell '{MazeMap.tag[Mathf.FloorToInt(e.p.y / MazeMap.C), Mathf.FloorToInt(e.p.x / MazeMap.C)]}' dHero {dh:F0}"); }
                }
                if (e.type.nest)
                {   // rooted; every couple of seconds a Drone crawls out (while the floor's cap allows)
                    e.flash = Mathf.Max(0, e.flash - dt * 7); e.k = Vector2.zero;
                    if (e.type.ai != null) { float s0 = 0; var t0 = Vector2.zero; EnemyAI.Pre(this, e, pl, (pl.p - e.p).magnitude, dt, ref s0, ref t0); }
                    e.spawnCd -= dt;
                    if (!e.egg && e.spawnCd <= 0 && e.spawnT >= 1 && enemies.Count + nestOut.Count < NestCap) { e.spawnCd = NestEvery * Random.Range(0.8f, 1.2f); if (NestExit(e, out var exitAt)) nestOut.Add(exitAt); }
                    continue;
                }
                bool toField = e.field != null && e.field.Length == MazeMap.flow.Length && (pl.p - e.p).sqrMagnitude > 9f;   // close to the hero: fight him
                var fl = toField ? e.field : MazeMap.flow;
                var dvec = (toField ? e.fieldAt : pl.p) - e.p;
                if (PowerUps.DecoyOn(out var decoy) && (decoy - e.p).sqrMagnitude < 196f) { dvec = decoy - e.p; fl = null; }   // the hologram pulls the swarm
                float dist = Mathf.Max(dvec.magnitude, 1e-4f);
                var target = dvec / dist;
                int ex = Mathf.FloorToInt(e.p.x / MazeMap.C), ez = Mathf.FloorToInt(e.p.y / MazeMap.C);
                int cur = fl != null && (ex >= 0 && ez >= 0 && ex < W && ez < MazeMap.H) ? fl[ez * W + ex] : -1;
                if (fl != null && (cur > 1 || cur < 0) && dist > 2.5f)
                {
                    int best = cur < 0 ? int.MaxValue : cur, bx = -1, bz = -1;
                    for (int oz = -1; oz <= 1; oz++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oz == 0) continue;
                            int nx = ex + ox, nz = ez + oz;
                            if (!MazeMap.FlowOpen(nx, nz)) continue;
                            if (ox != 0 && oz != 0 && (!MazeMap.FlowOpen(ex + ox, ez) || !MazeMap.FlowOpen(ex, ez + oz))) continue;
                            int f = fl[nz * W + nx];
                            if (f >= 0 && f < best) { best = f; bx = nx; bz = nz; }
                        }
                    if (bx >= 0) target = (MazeMap.Center(bx, bz) - e.p).normalized;
                }
                if (e.type.bloater) TickBloater(e, dist, dt);
                if (e.hop < 1) e.hop = Mathf.Min(1, e.hop + dt / 0.45f);
                bool leaping = e.type.leaper && TickLeaper(e, pl, dist, dt);
                float spd = leaping || e.spawnT < 1 || e.cast > 0 || e.hop < 1 ? 0 : e.speed * (e.atkAnim > 0 ? 0.35f : 1f) * ZoneCells.EnemySpeedMul(e) * PowerUps.EnemySpeedMul(e);
                if (e.frozen > 0) { e.frozen -= dt; spd = 0; e.atkCd = Mathf.Max(e.atkCd, 0.15f); }
                bool selfMoved = e.type.ai != null && e.frozen <= 0 && EnemyAI.Pre(this, e, pl, dist, dt, ref spd, ref target);
                if (e.mods != 0) Elites.Tick(game, e, dt);
                if ((e.type.ai != null || e.mods != 0) && e.view != null && e.view.root != null && e.view.root.gameObject.activeSelf == e.Hidden) e.view.root.gameObject.SetActive(!e.Hidden);
                float kk = 1 - Mathf.Exp(-8 * dt);
                if (leaping || selfMoved) { e.v = Vector2.zero; e.p += e.k * dt; } // the pounce / a charge moves it itself
                else { e.v += (target * spd - e.v) * kk; e.p += (e.v + e.k) * dt; }
                e.k *= Mathf.Exp(-E.knockbackDecay * dt);

                Separate(e); // crowd separation (positional relaxation)
                // keep out of the player; shove the player a little
                var pv = e.p - pl.p; float pr = e.r + cfg.player.radius, pd = pv.magnitude;
                if (pd < pr && pd > 1e-4f && !e.under && !(e.type.ai == "tick" && e.aiState == 2))
                {
                    float push = pr - pd, cp = cfg.player.contactPush;
                    e.p += pv / pd * push * (1 - cp);
                    pl.p -= pv / pd * push * cp;
                }
                MazeMap.CollideGrid(ref e.p, e.r);
                game.barrels.Collide(ref e.p, e.r);

                e.atkCd -= dt; e.atkAnim -= dt;
                if (pd < pr + E.attackReach && e.atkCd <= 0 && e.spawnT >= 1 && e.leap == 0 && !e.under && e.aiState == 0 && e.type.ai != "lancer")
                {
                    e.atkCd = E.attackCooldown * Random.Range(0.85f, 1.15f); e.atkAnim = 0.25f;
                    game.HurtPlayer(e.damage); if (e.mods != 0) Elites.OnHitHero(e, e.damage);
                }

                float vm = e.v.magnitude;
                float want = e.leap > 0 ? Mathf.Atan2(e.leapDir.x, e.leapDir.y) : vm > 0.2f ? Mathf.Atan2(e.v.x, e.v.y) : Mathf.Atan2(dvec.x, dvec.y);
                float dy = Mathf.DeltaAngle(e.yaw * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                e.yaw += dy * Mathf.Min(1, dt * 10);
                e.walk += vm * dt * 2.6f;
                e.flash = Mathf.Max(0, e.flash - dt * 7);
            }
            MazeMap.CollideGrid(ref pl.p, cfg.player.radius);
            game.barrels.Collide(ref pl.p, cfg.player.radius);
            foreach (var b in bursts) if (!b.dead) BurstMinis(b, Random.Range(cfg.bloater.minisMin, cfg.bloater.minisMax + 1), false);
            bursts.Clear();
            Defer(false);
            foreach (var at in nestOut) { var d = Spawn(at, Mathf.Max(1, game.wave), 0); d.p = at; }
            nestOut.Clear();
        }

        // ---- Leaper: stalk to range, crouch + telegraph (tracks, then locks), pounce in a straight line, stun on a miss
        readonly Stack<Transform> telePool = new();
        Material teleMat; Mesh chevron;
        bool TickLeaper(Enemy e, Player pl, float dist, float dt)
        {
            var L = game.cfg.leaper;
            e.leapCd -= dt; e.dodgeCd -= dt;
            switch (e.leap)
            {
                case 0:
                    if (e.spawnT >= 1 && e.leapCd <= 0 && dist < L.range && dist > L.minRange && e.k.sqrMagnitude < 4f && ClearLine(e.p, pl.p))
                    {
                        e.leap = 1; e.leapT = 0; e.leapDir = (pl.p - e.p).normalized;
                        Sfx.Hiss(Mathf.Clamp01(1.3f - dist / 20f));
                        ShowTele(e);
                    }
                    return false;
                case 1:
                    e.leapT += dt;
                    if (e.leapT < L.lockAt) { var d = pl.p - e.p; if (d.sqrMagnitude > 0.01f) e.leapDir = d.normalized; }
                    UpdateTele(e, L);
                    if (e.leapT >= L.crouch) { e.leap = 2; e.leapT = 0; e.leapHit = false; HideTele(e); Sfx.Pounce(Mathf.Clamp01(1.3f - dist / 20f)); }
                    return true;
                case 2:
                {
                    e.leapT += dt;
                    float step = L.distance / L.time * dt; var before = e.p;
                    e.p += e.leapDir * step;
                    MazeMap.CollideGrid(ref e.p, e.r);
                    bool wall = (e.p - before).sqrMagnitude < step * step * 0.25f;
                    if (!e.leapHit && (pl.p - e.p).magnitude < e.r + game.cfg.player.radius + 0.35f && pl.dashT <= 0)
                    {   // dashing through the line dodges it (HurtPlayer ignores a dashing hero too)
                        e.leapHit = true;
                        game.HurtPlayer(e.damage * L.hitDamage);
                        if (!(game.run && Game.TV(3, 1))) pl.vel += e.leapDir * L.knock;   // talent: immune to pounce shove
                        game.AddShake(0.3f); Haptics.Heavy();
                    }
                    if (e.leapT >= L.time || wall)
                    {
                        e.leap = 3; e.leapT = 0; e.stunFor = e.leapHit ? L.hitRecover : L.missStun; e.leapCd = L.cooldown;
                        if (!e.leapHit && game.run && (pl.p - e.p).magnitude < 3f) Ops.R.pounceDodges++;   // a pounce that landed near the hero but missed
                        Fx.I.Smoke(MazeMap.ToWorld(e.p, 0.1f), 5, new Color(0.8f, 0.82f, 0.86f), 1.8f, 0.45f, 0.18f, 0.6f);
                    }
                    return true;
                }
                default:
                    e.leapT += dt;
                    if (e.leapT >= e.stunFor) { e.leap = 0; e.leapT = 0; }
                    return true;
            }
        }

        // sidestep when shot while stalking (cooldown); called by the game's bullet hits
        public void OnShot(Enemy e, Vector2 shotDir)
        {
            var L = game.cfg.leaper;
            if (!e.type.leaper || e.leap != 0 || e.dodgeCd > 0 || e.hp <= 0 || Random.value > L.dodgeChance) return;
            var side = new Vector2(-shotDir.y, shotDir.x) * (Random.value < 0.5f ? -1 : 1);
            e.k += side * L.dodgeSpeed; e.dodgeCd = L.dodgeCooldown;
        }

        public static bool ClearLine(Vector2 a, Vector2 b)
        {
            var d = b - a; float len = d.magnitude; int n = Mathf.CeilToInt(len / 0.5f);
            for (int i = 1; i < n; i++) { var p = a + d * (i / (float)n); if (MazeMap.ShotSolidAt(p.x, p.y)) return false; }
            return true;
        }

        // the telegraph: a red strip along the pounce path with a chevron at its end; blinks while it tracks, then
        // holds bright once the line is locked
        void ShowTele(Enemy e)
        {
            if (teleMat == null)
            {
                // alpha-blended (reads on the bright floor); soft across its width, even along the whole path
                var strip = new Texture2D(32, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LeapStrip" };
                for (int y = 0; y < 4; y++) for (int x = 0; x < 32; x++) { float u = Mathf.Abs(x / 31f - 0.5f) * 2; strip.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - u) * 1.6f))); }
                strip.Apply();
                teleMat = Mats.Fade(new Color(0.9f, 0.08f, 0.05f, 0.6f), strip);
                chevron = new Mesh { name = "Chevron" };
                chevron.vertices = new[] { new Vector3(0, 0, 0.6f), new Vector3(0.7f, 0, -0.3f), new Vector3(0, 0, 0f), new Vector3(-0.7f, 0, -0.3f) };
                chevron.triangles = new[] { 0, 1, 2, 0, 2, 3 }; chevron.uv = new[] { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f) }; chevron.RecalculateNormals(); chevron.RecalculateBounds();
            }
            Transform t;
            if (telePool.Count > 0) t = telePool.Pop();
            else
            {
                t = new GameObject("LeapTelegraph").transform; t.SetParent(transform, false);
                var strip = new GameObject("Strip"); strip.transform.SetParent(t, false);
                strip.AddComponent<MeshFilter>().sharedMesh = quad;
                var sr = strip.AddComponent<MeshRenderer>(); sr.sharedMaterial = teleMat; sr.shadowCastingMode = ShadowCastingMode.Off;
                var head = new GameObject("Head"); head.transform.SetParent(t, false);
                head.AddComponent<MeshFilter>().sharedMesh = chevron;
                var hr = head.AddComponent<MeshRenderer>(); hr.sharedMaterial = teleMat; hr.shadowCastingMode = ShadowCastingMode.Off;
            }
            t.gameObject.SetActive(true); e.tele = t;
        }

        void UpdateTele(Enemy e, LeaperTuning L)
        {
            if (e.tele == null) return;
            // free length along the line (stops at walls)
            float len = 0;
            while (len < L.distance) { var p = e.p + e.leapDir * (len + 0.4f); if (MazeMap.ShotSolidAt(p.x, p.y)) break; len += 0.4f; }
            len = Mathf.Max(1f, len);
            bool locked = e.leapT >= L.lockAt;
            float a = locked ? 0.8f : 0.4f + 0.25f * (Mathf.Sin(e.leapT * 40f) > 0 ? 1 : 0);
            teleMat.SetColor("_BaseColor", new Color(0.9f, 0.08f, 0.05f, a));
            var w = MazeMap.ToWorld(e.leapDir);
            e.tele.SetPositionAndRotation(MazeMap.ToWorld(e.p, 0.06f), Quaternion.LookRotation(w));
            var strip = e.tele.GetChild(0); strip.localPosition = new Vector3(0, 0, len * 0.5f); strip.localRotation = Quaternion.Euler(90, 0, 0); strip.localScale = new Vector3(0.9f, len, 1);
            var head = e.tele.GetChild(1); head.localPosition = new Vector3(0, 0.01f, len); head.localScale = Vector3.one * 1.1f;
        }

        // every few seconds within range: stop, swell, burst out minis; drips of pus all the while
        void TickBloater(Enemy e, float dist, float dt)
        {
            var B = game.cfg.bloater;
            if (e.cast > 0)
            {
                e.cast -= dt;
                if (e.cast <= 0)
                {
                    e.cast = -1;
                    bursts.Add(e); // spawned after the enemy loop (the list can't grow mid-foreach)
                    var fwd = new Vector2(Mathf.Sin(e.yaw), Mathf.Cos(e.yaw));
                    var at = MazeMap.ToWorld(e.p + fwd * e.r * 0.5f, 0.9f);
                    Fx.I.PusSplat(at, e.scale, MazeMap.ToWorld(fwd));   // big splat, blobs thrown where the minis go
                    GunFx.I.Ring(new Vector3(at.x, 0.06f, at.z), 2.4f * e.r, new Color(0.6f, 1f, 0.25f, 0.85f), 0.22f);
                    e.squash = 0;
                    if (dist < 12f) game.AddShake(0.25f);              // a small bump when it's close (on screen)
                    if (CombatAudio.I != null) CombatAudio.I.BloaterSpawn();
                }
            }
            else if (e.spawnT >= 1 && dist < B.spawnRange && e.minis < B.capPerBloater && miniCount < B.capTotal)
            {
                e.special -= dt;
                if (e.special <= 0) { e.cast = B.castTime; e.special = B.spawnEvery * Random.Range(0.85f, 1.15f); }
            }
            e.drip -= dt;
            if (e.drip <= 0 && e.spawnT >= 1)
            {
                e.drip = Random.Range(0.22f, 0.45f);
                var off = Random.insideUnitCircle * e.r * 0.6f;
                Fx.I.PusDrip(MazeMap.ToWorld(e.p + off, 0.55f * e.scale + Random.value * 0.3f), e.scale);
            }
        }

        // Bloater extras after the Walk clip has posed it: belly swell + shiver while bursting, the ooze ring
        // and up to 3 green lights pulsing with the sacs (the clip swells them twice a cycle)
        void LateUpdate()
        {
            bool cargo = CohesiveZone.For(game);
            int li = 0;
            foreach (var e in enemies)
            {
                if (!e.type.bloater || e.view.model == null) continue;
                var v = e.view;
                var family = cargo ? v.model.GetComponent<CohesiveFamilyPlayback>() : null;
                float ph = family != null ? family.LegacyBloaterPhase : v.walk != null ? v.walk.normalizedTime % 1f : Time.time % 1f;
                float pulse = Mathf.Pow(Mathf.Max(0, Mathf.Sin(ph * Mathf.PI * 4)), 2);
                if (v.belly != null && e.cast > 0)
                {
                    float k = 1 - e.cast / game.cfg.bloater.castTime, sw = 1 + 0.35f * Mathf.SmoothStep(0, 1, k);
                    var sc = v.belly.localScale * sw;
                    sc += Vector3.one * (Random.Range(-0.03f, 0.03f) * k);
                    if (family == null && (!cargo || e.frozen <= 0)) v.belly.localScale = sc; // Keep visual RNG consumption, but hold a frozen sampled pose.
                    pulse = Mathf.Max(pulse, k);
                }
                if (v.ooze != null)
                {
                    oozeMpb.SetColor("_BaseColor", cargo ? new Color(.39f, .52f, .24f, .10f + .10f * pulse) : new Color(0.5f, 1f, 0.2f, 0.25f + 0.35f * pulse));
                    v.ooze.SetPropertyBlock(oozeMpb);
                    v.ooze.transform.localScale = Vector3.one * (cargo ? 1.15f + .05f * pulse : 1.4f + 0.12f * pulse);
                }
                if (li < pusLights.Length && e.spawnT >= 1)
                {
                    var l = pusLights[li++]; l.enabled = true;
                    l.transform.position = v.root.position + Vector3.up * (1.2f * e.scale);
                    l.color = cargo ? new Color(.55f, .69f, .38f) : new Color(.5f, 1f, .25f);
                    l.intensity = cargo ? (.15f + .20f * pulse) * (Feel.ReduceFlashes ? .45f : 1f) : .9f + 1.4f * pulse;
                    l.range = cargo ? Mathf.Min(2.8f, 1.25f * e.scale) : 2.2f * e.scale;
                }
                // a low wet thud as each diagonal pair of feet lands (phase 0 and 0.5), now and then
                if (CombatAudio.I != null && v.walk != null && (family != null ? family.LegacyBloaterSpeed : v.walk.speed) > 0.3f && !(cargo && e.frozen > 0))
                {
                    bool step = (v.lastPhase < 0.5f && ph >= 0.5f) || ph < v.lastPhase;
                    if (step && Random.value < 0.6f) CombatAudio.I.BloaterStep((e.p - game.player.p).magnitude);
                }
                v.lastPhase = ph;
            }
            for (; li < pusLights.Length; li++) pusLights[li].enabled = false;
        }

        // push simulation state into the transforms
        public void Sync()
        {
            foreach (var e in enemies)
            {
                var v = e.view;
                float grow = 1 - Mathf.Pow(1 - e.spawnT, 3);
                float s = e.scale * ReadabilityFx.EnemyScaleNow * Mathf.Max(0.01f, grow);   // (readability: bodies drawn bigger, radius unchanged)
                float hopY = e.hop < 1 ? 4f * 0.8f * e.hop * (1 - e.hop) : 0; // a mini's arc out of the sac
                if (e.leap == 2) { float u = Mathf.Clamp01(e.leapT / game.cfg.leaper.time); hopY = 4f * 1.1f * u * (1 - u); } // the pounce arc
                v.root.SetPositionAndRotation(MazeMap.ToWorld(e.p, -(1 - grow) * 1.2f * e.scale + hopY), Quaternion.Euler(0, MazeMap.YawDeg(e.yaw), 0));
                v.root.localScale = Vector3.one * s;
                bool authoredFamily = CohesiveZone.For(game) && v.model != null && v.model.GetComponent<CohesiveFamilyPlayback>() != null;
                if (!authoredFamily && FeelFx.Squash && e.flash > 0.05f) { float q = 0.14f * e.flash; v.root.localScale = new Vector3(s * (1 + q), s * (1 - q), s * (1 + q)); }   // (feel: squash on hit)
                if (!authoredFamily && e.type.leaper && e.type.model == "drone")
                {   // (the placeholder Drone) long and lean; squashes down in the crouch, stretches out in the air, wobbles when stunned
                    float sy = 0.85f, sz = 1.35f, sx = 0.82f;
                    if (e.leap == 1) { float c = Mathf.Clamp01(e.leapT / 0.15f); sy *= 1 - 0.28f * c; sz *= 1 + 0.12f * c; sx *= 1 + 0.08f * c; }
                    else if (e.leap == 2) { sy *= 0.8f; sz *= 1.55f; }
                    else if (e.leap == 3 && !e.leapHit) sx *= 1 + 0.08f * Mathf.Sin(e.leapT * 40f);
                    v.root.localScale = new Vector3(s * sx, s * sy, s * sz);
                }
                if (e.squash >= 0)
                {   // squash to 0.9, pop to 1.1, settle (volume roughly kept)
                    e.squash += Time.deltaTime;
                    float t = e.squash, y = t < 0.07f ? Mathf.Lerp(1, 0.9f, t / 0.07f) : t < 0.17f ? Mathf.Lerp(0.9f, 1.1f, (t - 0.07f) / 0.1f) : Mathf.Lerp(1.1f, 1, (t - 0.17f) / 0.18f);
                    if (t > 0.35f) { e.squash = -1; y = 1; }
                    float xz = 1 / Mathf.Sqrt(y);
                    if (!authoredFamily) v.root.localScale = new Vector3(s * xz, s * y, s * xz);
                }
                if (v.nest)
                {   // breathing sacs, a shudder when hit
                    float pulse = 1 + 0.06f * Mathf.Sin(Time.time * 3.2f + e.p.x) + 0.08f * e.flash;
                    v.root.localScale = new Vector3(s * pulse, s * (2 - pulse), s * pulse);
                    bool nf = e.flash > 0.35f;
                    if (nf != v.flashing) { SetSkin(v, nf ? flashMat : typeMats[e.typeIndex]); v.flashing = nf; }
                    continue;
                }
                bool fl = e.flash > 0.35f;
                if (fl != v.flashing) { SetSkin(v, fl ? (e.type.bloater ? pusFlashMat : flashMat) : typeMats[e.typeIndex]); v.flashing = fl; }
                if (v.model != null) { SyncModel(e, v); continue; }
                float vm = Mathf.Min(1, e.v.magnitude / 3);
                float sw = Mathf.Sin(e.walk) * 0.75f * vm * Mathf.Rad2Deg;
                float lean = (e.atkAnim > 0 ? 0.35f : vm * 0.12f) * Mathf.Rad2Deg;
                float bob = Mathf.Abs(Mathf.Cos(e.walk)) * 0.05f * vm;
                v.body.localPosition = new Vector3(0, 0.73f + bob, 0); v.body.localRotation = Quaternion.Euler(lean, 0, 0);
                v.head.localPosition = new Vector3(0, 1.28f + bob, lean * Mathf.Deg2Rad * 0.4f); v.head.localRotation = Quaternion.Euler(lean, 0, 0);
                // positive X rotation swings a limb backwards; legs alternate
                v.legL.localRotation = Quaternion.Euler(-sw, 0, 0);
                v.legR.localRotation = Quaternion.Euler(sw, 0, 0);
                float armA = e.atkAnim > 0 ? -80f : sw * 0.8f - 14f, armB = e.atkAnim > 0 ? -80f : -sw * 0.8f - 14f;
                v.armL.localRotation = Quaternion.Euler(armA, 0, 0);
                v.armR.localRotation = Quaternion.Euler(armB, 0, 0);
            }
        }

        // the Leaper's clips follow its state: Walk (speed from the ground), Crouch (the 0.6 s coil), Leap, Land (the
        // stunned wobble after a miss, quicker after a hit), Sidestep (a dodge)
        void SyncLeaper(Enemy e, EnemyView v)
        {
            var L = game.cfg.leaper;
            string want; float speed = 1;
            if (e.spawnT < 1) { want = "Walk"; speed = 2f; }
            else if (e.leap == 1) { want = "Crouch"; speed = 0.6f / L.crouch; }
            else if (e.leap == 2) { want = "Leap"; speed = (14f / 30f) / L.time; }
            else if (e.leap == 3) { want = "Land"; speed = 0.8f / Mathf.Max(0.2f, e.stunFor); }
            else if (e.dodgeCd > game.cfg.leaper.dodgeCooldown - 0.35f) { want = "Sidestep"; }
            else
            {
                want = "Walk"; float ground = e.v.magnitude;
                speed = ground < 0.15f ? 0f : Mathf.Clamp(ground / ((e.type.stride > 0 ? e.type.stride : StrideLen) * modelFit[e.typeIndex] * e.scale), 0.5f, 4f);
            }
            if (v.clip != want)
            {
                var st = v.anim[want];
                st.wrapMode = want == "Walk" ? WrapMode.Loop : WrapMode.ClampForever;
                if (want != "Walk") st.time = 0;
                v.anim.CrossFade(want, want == "Leap" ? 0.05f : 0.1f);
                v.clip = want;
            }
            v.anim[want].speed = want == "Walk" ? Mathf.MoveTowards(v.anim[want].speed, speed, Time.deltaTime * 20f) : speed;
            v.model.localRotation = Quaternion.identity;
            v.model.localPosition = new Vector3(0, modelLift[e.typeIndex], 0);
        }

        // skinned model: Walk speed follows the ground speed (feet roughly planted), lunge on attack
        void SyncModel(Enemy e, EnemyView v)
        {
            if (CohesiveZone.For(game) && CohesiveCharacterPolicy.IsAuthoredFamilyPrefab(modelPrefabs[e.typeIndex]))
            {
                var family = CohesiveFamilyPlayback.Attach(v.model.gameObject, CohesiveCharacterPolicy.RoleFor(e.type));
                if (family != null)
                {
                    v.model.localRotation = Quaternion.identity;
                    v.model.localPosition = new Vector3(0, modelLift[e.typeIndex], 0);
                    bool nearMelee = Vector2.Distance(e.p, game.player.p) < e.r + game.cfg.player.radius + game.cfg.enemy.attackReach
                        && e.spawnT >= 1 && e.leap == 0 && !e.under && e.aiState == 0 && e.type.ai != "lancer";
                    if (family.Sync(e, game.cfg, Time.deltaTime, v.model.lossyScale.x, nearMelee)) return;
                }
            }
            if (CohesiveZone.Active && CohesiveCharacterPolicy.HasAuthoredSpitter && e.type.ai == "spitter")
            {
                var motion = CohesiveSpitterPlayback.Attach(v.model.gameObject);
                if (motion != null)
                {
                    v.model.localRotation = Quaternion.identity;
                    v.model.localPosition = new Vector3(0, modelLift[e.typeIndex], 0);
                    var toward = game.player.p - e.p;
                    if (motion.Casting && toward.sqrMagnitude > .001f) v.model.rotation = Quaternion.LookRotation(MazeMap.ToWorld(toward));
                    bool reverse = motion.Casting && Vector2.Dot(e.v, toward) < 0;
                    if (motion.Sync(Time.deltaTime, e.v.magnitude, v.model.lossyScale.x, e.frozen > 0, e.spawnT >= 1, reverse)) return;
                }
            }
            if (CohesiveZone.Active && v.anim != null)
            {
                v.anim.enabled = e.frozen <= 0;
                if (e.frozen > 0) return;
            }
            if (e.type.leaper && v.anim != null && v.anim["Crouch"] != null) { SyncLeaper(e, v); return; }
            float atk = e.atkAnim > 0 ? Mathf.Sin(Mathf.Clamp01(1 - e.atkAnim / 0.25f) * Mathf.PI) : 0;
            v.model.localRotation = Quaternion.Euler(atk * 20f, 0, 0);
            v.model.localPosition = new Vector3(0, modelLift[e.typeIndex], atk * 0.25f);
            if (v.walk == null) return;
            float ground = e.v.magnitude, want;
            if (e.spawnT < 1) want = 2f;                                    // scrabbling up out of the floor
            else if (e.hop < 1 || e.leap == 2) want = 3f;                  // a mini / a Leaper flailing through the air
            else if (e.leap == 1) want = 0f;                                // coiled
            else if (e.leap == 3) want = e.leapHit ? 0.4f : 0.15f;          // stunned after a miss
            else if (e.cast > 0) want = 0.15f;                              // a Bloater straining to burst
            else if (e.atkAnim > 0 || e.k.sqrMagnitude > 2.25f) want = 0.3f; // biting / knocked back
            else if (ground < 0.15f) want = 0f;
            else want = Mathf.Clamp(ground / ((e.type.stride > 0 ? e.type.stride : StrideLen) * modelFit[e.typeIndex] * e.scale), 0.5f, 4f);
            v.walk.speed = Mathf.MoveTowards(v.walk.speed, want, Time.deltaTime * 20f);
        }
    }
}
