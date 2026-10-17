using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Presentation only: original Cryo floor one and every Cargo Depot route floor. No gameplay state.
    public class FirstCryoFx : MonoBehaviour
    {
        static Game game;
        static FirstCryoFx instance;
        public static bool Enabled = true;
        public static bool Active
        {
            get
            {
                if (game == null) game = FindAnyObjectByType<Game>();
                return Enabled && !Game.SimRun && game != null && game.run && (CohesiveZone.For(game) || game.floor == 1 && game.sector?.id == "cryo-station");
            }
        }
        public static bool Cargo => game != null && CohesiveZone.For(game);
        public static FirstCryoFx Get()
        {
            if (instance != null) return instance;
            var root = new GameObject("FirstCryoPresentation");
            if (Fx.I != null) root.transform.SetParent(Fx.I.transform, false);
            instance = root.AddComponent<FirstCryoFx>(); instance.Init(); return instance;
        }
        readonly System.Random random = new(9147); // VFX never consume the gameplay random stream.
        ParticleSystem mist, detonation, streak, crystals, metal, rings, flash;
        readonly Light[] lights = new Light[2];
        readonly float[] lightLife = new float[2];
        int nextLight;
        bool wasActive;
        static Mesh cube, crystal;
        static Material grate, frame, coldLed, snow, cargoGrate, cargoFrame;
        static Mesh frozenMark, frozenShadow;
        Material frozenMaterial, frozenShadowMaterial, acidBoundaryMaterial, cargoAcidBoundaryMaterial;
        static Mesh acidBoundaryMesh, cargoAcidBoundaryMesh;
        static readonly VfxKit.Look originalAcidLook = new VfxKit.Look { deep = new Color(.035f, .15f, .015f), mid = new Color(.28f, .54f, .045f), rim = new Color(.43f, .67f, .085f), stain = new Color(.055f, .07f, .025f, .52f), mode = 0, erode = .35f, flow = .8f };
        // Purple liquid ties the spit, impact and residue to the lavender creature.
        // Quiet suppresses generic green fumes and per-puddle ambient loops.
        static readonly VfxKit.Look cargoAcidLook = new VfxKit.Look { deep = new Color(.12f, .055f, .21f), mid = new Color(.47f, .24f, .67f), rim = new Color(.73f, .49f, .88f), stain = new Color(.10f, .055f, .16f, .40f), mode = 0, erode = .24f, flow = .45f, quiet = true };
        public static VfxKit.Look AcidLook => Cargo ? cargoAcidLook : originalAcidLook;
        public static Color AcidSplashColor => Cargo ? new Color(.70f, .43f, .88f) : VfxKit.AcidSplash;
        public static Color SpitWarningColor => Cargo ? new Color(.61f, .27f, .78f, .85f) : new Color(.5f, 1f, .2f, .8f);
        class FrozenHalo { public Enemy enemy; public Transform transform; public MeshRenderer renderer, shadow; public MaterialPropertyBlock block = new(), shadowBlock = new(); public bool keep; public float height; }
        readonly List<FrozenHalo> halos = new();
        class HopCue { public Enemy enemy; public Transform transform; public MeshRenderer renderer; public MaterialPropertyBlock block = new(); public bool keep; }
        readonly List<HopCue> hops = new(); static Mesh hopQuad; Material hopMaterial;
        public int HopCueCount => hops.FindAll(h => h.enemy != null && h.transform.gameObject.activeSelf).Count;
        float Range(float lo, float hi) => lo + (float)random.NextDouble() * (hi - lo);
        Vector3 Disc(float r) { float angle = Range(0, Mathf.PI * 2), distance = Mathf.Sqrt(Range(0, 1)) * r; return new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance); }
        public static Mesh FreezeSymbol => FrozenMark;
        public int FrozenMarkerCount => halos.FindAll(h => h.enemy != null && h.transform.gameObject.activeSelf).Count;
        public int FrozenMarkerPoolCount => halos.Count;
        public int LiveParticles => mist.particleCount + detonation.particleCount + streak.particleCount + crystals.particleCount + metal.particleCount + rings.particleCount + flash.particleCount;

        void Init()
        {
            var shader = Shader.Find("Maze/FirstCryoParticle");
            mist = Make("ColdVapour", shader, "Fx/Vfx/smoke_8x8", 180, 2, 8, .55f, 0, false);
            detonation = Make("RoundBlast", shader, "Fx/FirstCryo/explosion_4x2", 16, 0, 1, 0, 0, false);
            var atlas = detonation.textureSheetAnimation; atlas.enabled = true; atlas.numTilesX = 4; atlas.numTilesY = 2; atlas.animation = ParticleSystemAnimationType.WholeSheet; atlas.startFrame = 0; atlas.frameOverTime = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, .999f)); atlas.cycleCount = 1;
            var blastSize = detonation.sizeOverLifetime; blastSize.enabled = false;
            var blastColor = detonation.colorOverLifetime; var blastFade = new Gradient(); blastFade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .035f), new GradientAlphaKey(1, .55f), new GradientAlphaKey(.55f, .8f), new GradientAlphaKey(0, 1) }); blastColor.color = blastFade;
            streak = Make("ImpactStreak", shader, "Fx/Vfx/spark", 120, 0, 1, 0, 1.3f, false);
            crystals = Make("IceFragments", null, null, 160, 0, 1, 0, 1.3f, true);
            metal = Make("BarrelFragments", null, null, 80, 0, 1, 0, 1.8f, true);
            rings = Make("PressureFront", shader, "Fx/ring", 12, 2, 1, 0, 0, false);
            rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            flash = Make("LocalFlash", shader, "Fx/Vfx/glow", 8, 1, 1, 0, 0, false);
            var sr = streak.GetComponent<ParticleSystemRenderer>(); sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = .025f; sr.lengthScale = 1.15f;
            var size = rings.sizeOverLifetime; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .12f), new Keyframe(.6f, .9f), new Keyframe(1, 1)));
            for (int i = 0; i < lights.Length; i++)
            {
                var go = new GameObject("BlastLight" + i); go.transform.SetParent(transform, false);
                lights[i] = go.AddComponent<Light>(); lights[i].type = LightType.Point; lights[i].shadows = LightShadows.None; lights[i].enabled = false;
            }
        }
        ParticleSystem Make(string name, Shader shader, string texture, int cap, int mode, int grid, float span, float gravity, bool mesh)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = false; m.playOnAwake = false; m.maxParticles = cap; m.simulationSpace = ParticleSystemSimulationSpace.World; m.gravityModifier = gravity; m.startRotation3D = mesh;
            var emission = ps.emission; emission.enabled = false; var shape = ps.shape; shape.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, mesh ? .9f : .55f), new Keyframe(.28f, 1), new Keyframe(1, mesh ? .2f : 1.15f)));
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .04f), new GradientAlphaKey(.75f, .45f), new GradientAlphaKey(0, 1) });
            var color = ps.colorOverLifetime; color.enabled = true; color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = mesh;
            if (mesh) { renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = Crystal; renderer.sharedMaterial = Mats.ParticlesGloss(); }
            else
            {
                var art = Resources.Load<Texture2D>(texture);
                renderer.sharedMaterial = new Material(shader); renderer.sharedMaterial.SetTexture("_MainTex", art); renderer.sharedMaterial.SetFloat("_Mode", mode);
                // Missing art must never become an opaque white particle quad.
                renderer.enabled = art != null;
                if (art == null) Debug.LogWarning("FirstCryoFx: missing optional particle art " + texture);
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            if (grid > 1)
            {
                var sheet = ps.textureSheetAnimation; sheet.enabled = true; sheet.numTilesX = grid; sheet.numTilesY = grid;
                sheet.animation = ParticleSystemAnimationType.WholeSheet; sheet.startFrame = mode == 1 ? new ParticleSystem.MinMaxCurve(0) : new ParticleSystem.MinMaxCurve(0, 18);
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, span)); sheet.cycleCount = 1;
            }
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = mesh ? .1f : 1.8f;
            ps.Play(); return ps;
        }
        void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, float seconds, Color color)
        {
            if (ps == flash && Feel.ReduceFlashes) color.a *= .5f;
            ps.Emit(new ParticleSystem.EmitParams { position = at, velocity = velocity, startSize = size, startLifetime = seconds, startColor = color, rotation3D = ps == crystals || ps == metal ? new Vector3(Range(0, 360), Range(0, 360), Range(0, 360)) : Vector3.zero }, 1);
        }
        public void Vapour(Vector3 at, int count, float radius, bool cold = true)
        {
            bool cargo = Cargo;
            for (int i = 0; i < Mathf.Min(count, cargo ? 3 : 12); i++)
            {
                var offset = Disc(Mathf.Min(radius, cargo ? .55f : radius) * .45f);
                Emit(mist, at + offset + Vector3.up * .18f, offset * .35f + Vector3.up * Range(.4f, cargo ? .7f : 1.1f), cargo ? Range(.22f, .36f) : Range(.55f, .95f) * Mathf.Max(.6f, radius), cargo ? Range(.22f, .38f) : Range(.4f, .85f), cargo ? new Color(.60f, .86f, .94f, .13f) : cold ? new Color(.66f, .86f, .98f, .22f) : new Color(.48f, .46f, .43f, .16f));
            }
        }
        public void VentStream(Vector3 at, bool warning, float dt)
        {
            if (Cargo)
            {
                // Short cyan pressure streaks read as a service jet, without hiding nearby actors.
                if (Range(0, 1) > dt * (warning ? 3 : 10)) return;
                Emit(streak, at + Disc(.20f) + Vector3.up * .09f, Vector3.up * (warning ? .65f : 2.1f), warning ? .07f : .10f, warning ? .18f : .28f, new Color(.38f, .84f, .96f, warning ? .48f : .72f));
                if (!warning && Range(0, 1) < .24f) Emit(mist, at + Vector3.up * .20f, Vector3.up * .7f, .32f, .26f, new Color(.65f, .88f, .94f, .12f));
                return;
            }
            if (Range(0, 1) > dt * (warning ? 5 : 14)) return;
            Emit(mist, at + Disc(.28f) + Vector3.up * .18f, new Vector3(Range(-.12f, .12f), warning ? 1.1f : 2.2f, Range(-.12f, .12f)), warning ? .4f : .65f, warning ? .45f : .7f, new Color(.7f, .9f, 1, warning ? .14f : .24f));
        }
        public void FreezeBurst(Vector3 at, float radius, bool sound = true)
        {
            Emit(rings, new Vector3(at.x, .085f, at.z), Vector3.zero, radius * 2, .42f, new Color(.16f, .62f, .85f, .85f));
            Emit(flash, at + Vector3.up * .2f, Vector3.zero, Mathf.Min(radius, 2.4f), .09f, new Color(.55f, .85f, 1, .42f));
            for (int i = 0; i < (Cargo ? 12 : 24); i++)
            {
                var offset = Disc(radius * .45f); var outward = offset.sqrMagnitude > .001f ? offset.normalized : Vector3.right;
                Emit(crystals, at + offset + Vector3.up * .2f, outward * Range(2, 4.5f) + Vector3.up * Range(1, 2.3f), Cargo ? Range(.08f, .15f) : Range(.13f, .26f), Cargo ? Range(.25f, .42f) : Range(.4f, .72f), new Color(.4f, .8f, .98f, 1));
            }
            Vapour(at, Cargo ? 2 : 7, radius * .4f);
            if (sound) HazardAudio.Shot("sfx_cryo_vent_burst", at, .48f, 2, "first-cryo-freeze", .12f);
        }
        public void Blast(Vector3 at, float radius)
        {
            HazardAudio.Shot("sfx_explosion", at, .65f, 2);
            Emit(flash, at + Vector3.up * .55f, Vector3.zero, radius * .95f, .095f, new Color(1, .82f, .5f, .8f));
            Emit(rings, new Vector3(at.x, .085f, at.z), Vector3.zero, radius * 2, .3f, new Color(.75f, .37f, .12f, .65f));
            Emit(detonation, at + Vector3.up * .55f, Vector3.zero, radius * (Cargo ? 1.6f : 2.05f), Cargo ? .42f : .64f, Color.white);
            for (int i = 0; i < 18; i++) { var direction = Disc(1).normalized; Emit(streak, at + Vector3.up * .45f, direction * Range(5, 9) + Vector3.up * Range(.6f, 2), Range(.055f, .095f), Range(.15f, .3f), new Color(1, .6f, .18f, .85f)); }
            for (int i = 0; i < 8; i++) { var direction = Disc(1).normalized; Emit(metal, at + Vector3.up * .5f, direction * Range(3, 5) + Vector3.up * Range(1, 3), Range(.1f, .17f), Range(.4f, .7f), new Color(.28f, .24f, .2f)); }
            // The authored atlas carries the smoke tail, keeping the blast to one transparent body.
            int index = nextLight++ % lights.Length; var light = lights[index]; light.transform.position = at + Vector3.up; light.color = new Color(1, .55f, .2f); light.range = radius * (Cargo ? 1.4f : 2); light.intensity = Cargo ? (Feel.ReduceFlashes ? .65f : 1.5f) : Feel.ReduceFlashes ? 2 : 5; light.enabled = true; lightLife[index] = .1f;
        }
        public void AcidBoundary(Transform pool, float radius)
        {
            if (pool == null) return;
            acidBoundaryMesh ??= Fx.RingMesh(.972f, 1, 64);
            acidBoundaryMaterial ??= Mats.Fade(new Color(.08f, .25f, .015f, .8f));
            cargoAcidBoundaryMesh ??= Fx.RingMesh(.92f, 1, 64);
            cargoAcidBoundaryMaterial ??= Mats.Fade(new Color(.28f, .10f, .43f, .9f));
            var go = new GameObject(Cargo ? "CargoSpitBoundary" : "CryoAcidBoundary"); go.transform.SetParent(pool, false); go.transform.localPosition = new Vector3(0, .02f, 0); go.transform.localScale = Vector3.one * radius;
            go.AddComponent<MeshFilter>().sharedMesh = Cargo ? cargoAcidBoundaryMesh : acidBoundaryMesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = Cargo ? cargoAcidBoundaryMaterial : acidBoundaryMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        public void Clear()
        {
            foreach (var ps in new[] { mist, detonation, streak, crystals, metal, rings, flash }) if (ps != null) ps.Clear();
            foreach (var light in lights) if (light != null) light.enabled = false;
            foreach (var halo in halos) { halo.enemy = null; halo.transform.gameObject.SetActive(false); }
            foreach (var hop in hops) { hop.enemy = null; hop.transform.gameObject.SetActive(false); }
        }
        public static void ClearIfBuilt() { if (instance != null) instance.Clear(); }
        void LateUpdate()
        {
            if (!Active || game.swarm == null) return;
            UpdateHopCues();
            foreach (var halo in halos) halo.keep = false;
            foreach (var enemy in game.swarm.enemies)
            {
                if (enemy.dead || enemy.frozen <= 0 || enemy.type.nest || enemy.Hidden) continue;
                FrozenHalo halo = halos.Find(h => h.enemy == enemy) ?? halos.Find(h => h.enemy == null);
                if (halo == null)
                {
                    if (halos.Count >= (Cargo ? 64 : 24)) continue;
                    var go = new GameObject("FrozenSnowflake"); go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = FrozenMark;
                    frozenMaterial ??= Mats.Fade(new Color(.28f, .84f, 1, .95f));
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = frozenMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                    var backing = new GameObject("SnowflakeOutline"); backing.transform.SetParent(go.transform, false); backing.transform.localPosition = new Vector3(0, .006f, 0);
                    frozenShadow ??= MakeFrozenMark(1.75f); backing.AddComponent<MeshFilter>().sharedMesh = frozenShadow;
                    frozenShadowMaterial ??= Mats.Fade(new Color(.015f, .08f, .13f, .9f));
                    var shadow = backing.AddComponent<MeshRenderer>(); shadow.sharedMaterial = frozenShadowMaterial; shadow.shadowCastingMode = ShadowCastingMode.Off;
                    halos.Add(halo = new FrozenHalo { transform = go.transform, renderer = renderer, shadow = shadow });
                }
                if (Cargo && halo.enemy != enemy)
                {
                    // Measure once per pooled binding so a large Bloater cannot hide its state icon inside its body.
                    halo.height = Mathf.Max(1.25f, enemy.r * 1.8f);
                    if (enemy.view?.model != null)
                        foreach (var body in enemy.view.model.GetComponentsInChildren<Renderer>())
                            halo.height = Mathf.Max(halo.height, body.bounds.max.y - MazeMap.ToWorld(enemy.p).y + .20f);
                }
                halo.enemy = enemy; halo.keep = true; halo.transform.gameObject.SetActive(true);
                halo.transform.position = MazeMap.ToWorld(enemy.p, Cargo ? halo.height : Mathf.Max(1.25f, enemy.r * 1.8f));
                if (Camera.main != null) halo.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward, Camera.main.transform.up) * Quaternion.Euler(90, 0, 0);
                halo.transform.localScale = Vector3.one * .40f;
                halo.block.SetColor("_BaseColor", new Color(.28f, .84f, 1, .95f * Mathf.Clamp01(enemy.frozen / .2f))); halo.renderer.SetPropertyBlock(halo.block);
                halo.shadowBlock.SetColor("_BaseColor", new Color(.015f, .08f, .13f, .9f * Mathf.Clamp01(enemy.frozen / .2f))); halo.shadow.SetPropertyBlock(halo.shadowBlock);
            }
            foreach (var halo in halos) if (!halo.keep) { halo.enemy = null; halo.transform.gameObject.SetActive(false); }
        }
        void UpdateHopCues()
        {
            foreach (var cue in hops) cue.keep = false;
            foreach (var enemy in game.swarm.enemies)
            {
                if (enemy.dead || enemy.Hidden || enemy.type.ai != "tick" || enemy.aiState != 1) continue;
                var cue = hops.Find(h => h.enemy == enemy) ?? hops.Find(h => h.enemy == null);
                if (cue == null)
                {
                    if (hops.Count >= 8) continue;
                    hopQuad ??= new Mesh { name = "TickHopQuad", vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) }, uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0, 2, 1, 1, 2, 3 } };
                    hopMaterial ??= new Material(Shader.Find("Maze/FirstCryoTele")); hopMaterial.SetFloat("_Shape", 1); hopMaterial.SetFloat("_Friendly", 0);
                    var go = new GameObject("TickHopWarning"); go.transform.SetParent(transform, false); go.AddComponent<MeshFilter>().sharedMesh = hopQuad;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = hopMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                    hops.Add(cue = new HopCue { transform = go.transform, renderer = renderer });
                }
                cue.enemy = enemy; cue.keep = true; cue.transform.gameObject.SetActive(true);
                var delta = game.player.p - enemy.p; var direction = delta.sqrMagnitude > .0001f ? delta.normalized : Vector2.up;
                cue.transform.position = MazeMap.ToWorld((enemy.p + game.player.p) * .5f, .085f);
                cue.transform.rotation = Quaternion.Euler(90, MazeMap.YawDeg(Mathf.Atan2(direction.x, direction.y)), 0);
                cue.transform.localScale = new Vector3(.42f, Mathf.Max(.3f, delta.magnitude), 1);
                cue.block.SetColor("_BaseColor", new Color(.9f, .19f, .07f, .75f)); cue.block.SetFloat("_Progress", Mathf.Clamp01(enemy.aiT / .4f)); cue.renderer.SetPropertyBlock(cue.block);
            }
            foreach (var cue in hops) if (!cue.keep) { cue.enemy = null; cue.transform.gameObject.SetActive(false); }
        }
        static Mesh FrozenMark => frozenMark != null ? frozenMark : frozenMark = MakeFrozenMark(1);
        static Mesh MakeFrozenMark(float stroke)
        {
                var points = new List<Vector3>(); var indices = new List<int>();
                void Bar(Vector2 a, Vector2 b, float width)
                {
                    var direction = (b - a).normalized; var side = new Vector2(-direction.y, direction.x) * width * stroke * .5f; int index = points.Count;
                    foreach (var p in new[] { a - side, a + side, b - side, b + side }) points.Add(new Vector3(p.x, 0, p.y));
                    indices.AddRange(new[] { index, index + 1, index + 2, index + 2, index + 1, index + 3 });
                }
                for (int arm = 0; arm < 6; arm++)
                {
                    float angle = arm * Mathf.PI / 3; var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); var side = new Vector2(-direction.y, direction.x);
                    Bar(Vector2.zero, direction, .08f);
                    Bar(direction * .56f, direction * .8f + side * .2f, .06f); Bar(direction * .56f, direction * .8f - side * .2f, .06f);
                }
                var mesh = new Mesh { name = "FrozenSnowflake" }; mesh.SetVertices(points); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        void Update()
        {
            bool active = Active; if (wasActive && !active) Clear(); wasActive = active;
            for (int i = 0; i < lights.Length; i++) { if (!lights[i].enabled) continue; lightLife[i] -= Time.deltaTime; lights[i].intensity *= Mathf.Exp(-Time.deltaTime * 38); if (lightLife[i] <= 0) lights[i].enabled = false; }
        }
        void OnDestroy()
        {
            foreach (var ps in new[] { mist, detonation, streak, crystals, metal, rings, flash }) if (ps != null) Destroy(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial);
            if (instance == this) instance = null;
            if (frozenMaterial != null) Destroy(frozenMaterial);
            if (frozenShadowMaterial != null) Destroy(frozenShadowMaterial);
            if (acidBoundaryMaterial != null) Destroy(acidBoundaryMaterial);
            if (cargoAcidBoundaryMaterial != null) Destroy(cargoAcidBoundaryMaterial);
            if (hopMaterial != null) Destroy(hopMaterial);
        }
        static Mesh Crystal
        {
            get
            {
                if (crystal != null) return crystal;
                crystal = new Mesh { name = "FacetedFragment", vertices = new[] { new Vector3(0, .8f, 0), new Vector3(-.28f, 0, 0), new Vector3(0, 0, .28f), new Vector3(.28f, 0, 0), new Vector3(0, 0, -.28f), new Vector3(0, -.5f, 0) }, triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1 } };
                crystal.RecalculateNormals(); crystal.RecalculateBounds(); return crystal;
            }
        }
        public static void VentHardware(Transform parent, Vector2 at)
        {
            if (CryoSoftEnvironment.Active && !Cargo) return; // rounded grilles are authored into the selected room; rings and vent logic remain live
            if (cube == null) { var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); primitive.SetActive(false); cube = primitive.GetComponent<MeshFilter>().sharedMesh; Destroy(primitive); }
            grate ??= Mats.Lit(new Color(.14f, .2f, .25f), null, null, .7f, .35f);
            frame ??= Mats.Lit(new Color(.43f, .52f, .59f), null, null, .65f, .45f);
            coldLed ??= Mats.Unlit(new Color(.17f, .62f, .8f));
            cargoGrate ??= Mats.Lit(new Color(.12f, .20f, .29f), null, null, .85f, 0);
            cargoFrame ??= Mats.Lit(new Color(.84f, .82f, .70f), null, null, .85f, 0);
            var useGrate = Cargo ? cargoGrate : grate; var useFrame = Cargo ? cargoFrame : frame;
            var root = new GameObject(Cargo ? "CargoCoolingJet" : "CryoVentGrille").transform; root.SetParent(parent, false); root.position = MazeMap.ToWorld(at, 0);
            void Part(string name, Vector3 position, Vector3 scale, Material material)
            {
                var go = new GameObject(name); go.transform.SetParent(root, false); go.transform.localPosition = position; go.transform.localScale = scale; go.AddComponent<MeshFilter>().sharedMesh = cube;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            Part("Recess", new Vector3(0, .035f, 0), new Vector3(1.5f, .06f, 1.5f), useGrate);
            int slots = Cargo ? 4 : 8;
            for (int i = 0; i < slots; i++) Part("Louvre", new Vector3(0, .105f, -.55f + i * (1.1f / (slots - 1))), new Vector3(1.2f, .085f, Cargo ? .15f : .085f), useFrame);
            for (int side = -1; side <= 1; side += 2) { Part("EdgeRail", new Vector3(side * .7f, .08f, 0), new Vector3(.09f, .12f, 1.5f), useFrame); Part("ColdIndicator", new Vector3(side * .69f, .145f, 0), new Vector3(.06f, .02f, .42f), coldLed); }
        }
        public static void Snow(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); snow ??= Mats.ParticlesFade(Resources.Load<Texture2D>("Fx/Vfx/glow")); renderer.sharedMaterial = snow;
            var main = ps.main; main.maxParticles = 90; main.startSize = new ParticleSystem.MinMaxCurve(.018f, .055f); main.startColor = new Color(.77f, .88f, .95f, .32f); main.startLifetime = 4.5f;
            var emission = ps.emission; emission.rateOverTime = 14;
            var fade = ps.colorOverLifetime; fade.enabled = true; var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(1, .6f), new GradientAlphaKey(0, 1) }); fade.color = gradient;
        }
    }
}
