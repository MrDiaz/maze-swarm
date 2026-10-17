using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Gunfire visuals: per-weapon muzzle flash (a camera-facing star + side-on flame cones along the
    // barrel, soft alpha-blended textures from Resources/Fx baked by Tools/fx_textures.py), a warm floor-light
    // pop, a smoke wisp, ejected casings (brass; a red hull on the shotgun's pump), streak sparks for
    // impacts and the shotgun, and the soft tracer streak meshes. Everything is a pooled particle system
    // fed with Emit(EmitParams) (no per-shot allocations), one material per effect type.
    public class GunFx : MonoBehaviour
    {
        public static GunFx I;

        // flash style per weapon: 0 = AK three-prong, 1 = SMG tight, 2 = shotgun wide burst
        public static int Style(string weapon) => weapon == "SMG" ? 1 : weapon == "SHOTGUN" ? 2 : 0;

        ParticleSystem[] stars, cones;
        ParticleSystem sparks, smoke, casings, rings;
        Transform anchor;                     // follows the muzzle, so a flash moves with the gun
        readonly Light[] lights = new Light[2];
        readonly float[] lightT = new float[2];
        int nextLight;
        Mesh[] tracerMeshes;
        Material tracerMat;
        float hullT = -1; Vector3 hullAt, hullRight, lastDir = Vector3.forward;
        // the approved chibi gunfire kit (Assets/Maze/Gunfire) for the rifle and the Heavy's minigun; null = the old look
        GunfireKit kit; Game game; int kitTracerFrame = -1;
        bool Heavy { get { if (game == null) game = FindAnyObjectByType<Game>(); return game != null && game.player != null && game.VgMinigun; } }

        static readonly Color Brass = new Color(0.86f, 0.64f, 0.24f), Hull = new Color(0.72f, 0.09f, 0.07f);
        const float LightLife = 0.06f;

        // per style: star size, cone length, cone count, smoke chance, light intensity
        static readonly float[] StarSize = { 1.8f, 1.0f, 2.7f };
        static readonly float[] ConeLen = { 2.5f, 1.35f, 2.4f };
        static readonly int[] ConeCount = { 2, 1, 3 };
        static readonly float[] SmokeChance = { 0.8f, 0.35f, 1f };
        static readonly float[] LightPeak = { 2.4f, 1.5f, 5f }, LightRange = { 4.5f, 3.5f, 7f };
        static readonly Color[] LightCol = { new Color(1f, 0.68f, 0.32f), new Color(1f, 0.72f, 0.38f), new Color(1f, 0.55f, 0.2f) };

        public void Init(Mesh cube)
        {
            I = this;
            var starTex = Resources.Load<Texture2D>("Fx/flash_star");
            var coneTex = Resources.Load<Texture2D>("Fx/flash_cone");
            var streakTex = Resources.Load<Texture2D>("Fx/streaks");
            var puffTex = Resources.Load<Texture2D>("Fx/puff");
            if (starTex == null || coneTex == null || streakTex == null || puffTex == null)
                Debug.LogWarning("Maze: Resources/Fx textures missing (run Tools/fx_textures.py)");

            anchor = new GameObject("MuzzleAnchor").transform;
            anchor.SetParent(transform, false);

            // alpha-blended, not additive: additive light disappears on the near-white maze floor
            var starMat = Mats.ParticlesFade(starTex);
            var coneMat = Mats.ParticlesFade(coneTex);
            stars = new ParticleSystem[3]; cones = new ParticleSystem[3];
            for (int s = 0; s < 3; s++)
            {
                stars[s] = Flash("Star" + s, starMat, s, ParticleSystemRenderMode.Billboard);
                cones[s] = Flash("Cone" + s, coneMat, s, ParticleSystemRenderMode.HorizontalBillboard);
            }

            var sparkMat = Mats.ParticlesFade(streakTex);
            sparks = Make("StreakSparks", sparkMat, 160, ParticleSystemSimulationSpace.World, 1.2f);
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch; sr.lengthScale = 1.2f; sr.velocityScale = 0.035f;
            Sheet(sparks, 4, 1, 3, 3); // the spark column of streaks.png
            FadeOut(sparks, 0.5f);

            smoke = Make("Wisps", Mats.ParticlesFade(puffTex), 64, ParticleSystemSimulationSpace.World, -0.05f);
            smoke.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
            var sl = smoke.sizeOverLifetime; sl.enabled = true;
            sl.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.6f)));
            var sv = smoke.limitVelocityOverLifetime; sv.enabled = true; sv.drag = 3f; sv.multiplyDragByParticleSize = false;
            FadeOut(smoke, 0.25f);

            // shotgun shockwave: a faint ring that snaps out from the muzzle
            rings = Make("Shockwave", Mats.ParticlesFade(Resources.Load<Texture2D>("Fx/ring")), 4, ParticleSystemSimulationSpace.World, 0);
            rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var rs = rings.sizeOverLifetime; rs.enabled = true;
            rs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.15f, 0, 4f), new Keyframe(1, 1f, 0.3f, 0)));
            FadeOut(rings, 0f);

            casings = Make("Casings", Mats.Particles(false), 40, ParticleSystemSimulationSpace.World, 1.6f);
            var cr = casings.GetComponent<ParticleSystemRenderer>();
            cr.renderMode = ParticleSystemRenderMode.Mesh; cr.mesh = cube; cr.alignment = ParticleSystemRenderSpace.World;
            var cm = casings.main; cm.startSize3D = true; cm.startRotation3D = true;
            var floor = new GameObject("CasingFloor").transform; floor.SetParent(transform, false);
            var col = casings.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes; col.SetPlane(0, floor);
            col.bounce = 0.35f; col.dampen = 0.5f; col.lifetimeLoss = 0; col.radiusScale = 0.3f;
            var rot = casings.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-14f, 14f); rot.y = new ParticleSystem.MinMaxCurve(-10f, 10f); rot.z = new ParticleSystem.MinMaxCurve(-14f, 14f);
            var cs = casings.sizeOverLifetime; cs.enabled = true; // opaque: fade by shrinking at the end
            cs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.75f, 1), new Keyframe(1, 0)));
            casings.GetComponent<ParticleSystemRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            for (int i = 0; i < lights.Length; i++)
            {
                var lg = new GameObject("MuzzleLight" + i);
                lg.transform.SetParent(transform, false);
                var l = lights[i] = lg.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.68f, 0.32f); l.range = 4.5f; l.intensity = 0; l.shadows = LightShadows.None;
                l.enabled = false;
            }

            // tracers: one soft streak quad per weapon (flat on the shot plane), one material
            tracerMat = Mats.Fade(Color.white, streakTex);
            tracerMeshes = new Mesh[3];
            for (int i = 0; i < 3; i++) tracerMeshes[i] = StreakQuad(i);
            kit = GunfireKit.Load(transform, anchor);
        }

        // flat quad in XZ, length along +Z (head at +Z), uv from column `col` of streaks.png
        static Mesh StreakQuad(int col)
        {
            float u0 = col / 4f + 0.002f, u1 = (col + 1) / 4f - 0.002f;
            var m = new Mesh { name = "Streak" + col };
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) };
            m.uv = new[] { new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u0, 1), new Vector2(u1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // tracer look for a weapon index (cfg order RIFLE, SHOTGUN, SMG = streaks.png columns 0..2)
        public void SetupTracer(MeshFilter mf, MeshRenderer mr, Transform t, int weapon, string name)
        {
            mr.enabled = true;   // (the kit hides some Barrage tracers; pooled quads come back visible)
            if (kit != null && name == "RIFLE") { kit.SetupTracer(mf, mr, t, Heavy, Abilities.BarrageT > 0); kitTracerFrame = Time.frameCount; return; }
            mf.sharedMesh = tracerMeshes[Mathf.Clamp(weapon, 0, 2)];
            mr.sharedMaterial = tracerMat;
            int s = Style(name);
            t.localScale = s == 1 ? new Vector3(0.2f, 1, 1.3f) : s == 2 ? new Vector3(0.11f, 1, 0.75f) : new Vector3(0.24f, 1, 2f);
        }

        ParticleSystem Make(string name, Material mat, int max, ParticleSystemSimulationSpace space, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = space; main.gravityModifier = gravity;
            if (space == ParticleSystemSimulationSpace.Custom) main.customSimulationSpace = anchor;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        ParticleSystem Flash(string name, Material mat, int style, ParticleSystemRenderMode mode)
        {
            var ps = Make(name, mat, 8, ParticleSystemSimulationSpace.Custom, 0);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode; r.sortingFudge = -20; // in front of smoke
            Sheet(ps, 3, 3, style, -1);
            if (mode == ParticleSystemRenderMode.HorizontalBillboard) { var m = ps.main; m.startSize3D = true; }
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.75f), new Keyframe(0.35f, 1.05f), new Keyframe(1, 1.15f)));
            FadeOut(ps, 0f);
            return ps;
        }

        // flipbook on a grid: `row` fixed; frame = column `frame`, or random per particle when < 0
        static void Sheet(ParticleSystem ps, int cols, int rows, int row, int frame)
        {
            var ts = ps.textureSheetAnimation; ts.enabled = true;
            ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = cols; ts.numTilesY = rows;
            ts.animation = ParticleSystemAnimationType.SingleRow;
            if (rows > 1) { ts.rowMode = ParticleSystemAnimationRowMode.Custom; ts.rowIndex = row; }
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(0);
            ts.startFrame = frame >= 0 ? new ParticleSystem.MinMaxCurve(frame) : new ParticleSystem.MinMaxCurve(0, cols - 0.001f);
        }

        // alpha holds, then drops to 0 by the end (hold = fraction of the life at full alpha)
        static void FadeOut(ParticleSystem ps, float hold)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(hold > 0 ? 1 : 0.55f, hold > 0 ? hold : 0.3f), new GradientAlphaKey(0, 1) });
            c.color = g;
        }

        // the gun's muzzle this frame (Player.LateUpdate)
        public void FollowMuzzle(Vector3 p) => anchor.position = p;

        // a shot: muzzle = world muzzle point, dir = world barrel direction (flat); pump: the shotgun
        // racks after this shot (not after its last shell: the reload's rack ejects that one, see Rack)
        public void Shot(int style, Vector3 muzzle, Vector3 dir, bool pump = true)
        {
            lastDir = dir;
            anchor.position = muzzle;
            if (kit != null && style == 0 && kitTracerFrame == Time.frameCount) { KitShot(muzzle, dir); return; }   // the rifle / minigun this frame
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // world yaw of the barrel (0 = +Z)
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            // star: camera-facing, random roll and size, nudged off the barrel so the hero doesn't hide it
            ep.position = dir * 0.12f + Vector3.up * 0.12f; ep.velocity = Vector3.zero;
            ep.startLifetime = Random.Range(0.04f, 0.06f);
            ep.startSize = StarSize[style] * Random.Range(0.8f, 1.15f);
            ep.rotation = Random.value * 360f;
            ep.startColor = Color.white;
            stars[style].Emit(ep, 1);

            // cones: flat on the floor plane, lying along the barrel (texture muzzle end at the gun)
            ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < ConeCount[style]; i++)
            {
                float len = ConeLen[style] * Random.Range(0.8f, 1.15f) * (i == 0 ? 1 : 0.75f);
                float spread = i == 0 ? 0 : style == 2 ? (i == 1 ? -13f : 13f) + Random.Range(-3f, 3f) : Random.Range(-14f, 14f);
                float a = (yaw + spread) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                ep.position = d * (len * 0.5f - 0.22f); // overlap the star at the muzzle
                ep.startSize3D = new Vector3(len, len * 0.5f, 1);
                ep.rotation = yaw + spread - 90f; // horizontal billboard: texture +u along the barrel
                ep.startLifetime = Random.Range(0.04f, 0.055f);
                ep.startColor = new Color(1, 1, 1, i == 0 ? 1 : 0.7f);
                cones[style].Emit(ep, 1);
            }

            // shotgun: a dense spray of hot sparks in the pellets' cone (+-17 deg), slower burning powder
            // flecks, and a faint shockwave ring
            if (style == 2)
            {
                for (int i = 0; i < 14; i++)
                {
                    float a = (yaw + Random.Range(-17f, 17f)) * Mathf.Deg2Rad;
                    Spark(muzzle, new Vector3(Mathf.Sin(a), Random.Range(-0.05f, 0.12f), Mathf.Cos(a)) * Random.Range(14f, 28f), Random.Range(0.07f, 0.15f), Random.Range(0.06f, 0.09f), Color.white);
                }
                for (int i = 0; i < 6; i++)
                {
                    float a = (yaw + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
                    Spark(muzzle, new Vector3(Mathf.Sin(a), Random.Range(0.05f, 0.35f), Mathf.Cos(a)) * Random.Range(5f, 10f), Random.Range(0.25f, 0.4f), 0.05f, new Color(1f, 0.62f, 0.35f));
                }
                var rp = new ParticleSystem.EmitParams { position = muzzle + dir * 0.35f, startLifetime = 0.14f, startSize = 2.8f, startColor = new Color(1, 1, 1, 0.5f), rotation = Random.value * 360f };
                rings.Emit(rp, 1);
            }

            // shotgun: a heavy puff that rolls forward and hangs, plus a low blast of dust
            if (style == 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                    float a = (yaw + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
                    var fd = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    ep.position = muzzle + fd * Random.Range(0.2f, 0.6f);
                    ep.velocity = fd * Random.Range(1.6f, 2.8f) + new Vector3(0, Random.Range(0.2f, 0.5f), 0);
                    ep.startLifetime = Random.Range(0.75f, 0.95f);
                    ep.startSize = Random.Range(0.9f, 1.25f);
                    ep.rotation = Random.value * 360f;
                    ep.startColor = new Color(0.8f, 0.79f, 0.76f, 0.45f);
                    smoke.Emit(ep, 1);
                }
                ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                ep.position = new Vector3(muzzle.x, 0.25f, muzzle.z) + dir * 0.9f;
                ep.velocity = dir * 1.2f; ep.startLifetime = 0.6f; ep.startSize = 1.6f; ep.rotation = Random.value * 360f;
                ep.startColor = new Color(0.86f, 0.84f, 0.8f, 0.3f);
                smoke.Emit(ep, 1);
            }
            // smoke wisp drifting off the muzzle
            else if (Random.value < SmokeChance[style])
            {
                ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                ep.position = muzzle + dir * 0.15f;
                ep.velocity = dir * Random.Range(0.6f, 1.2f) + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.3f, 0.6f), Random.Range(-0.2f, 0.2f));
                ep.startLifetime = Random.Range(0.45f, 0.6f);
                ep.startSize = style == 2 ? 0.75f : style == 1 ? 0.35f : 0.5f;
                ep.rotation = Random.value * 360f;
                ep.startColor = new Color(0.82f, 0.82f, 0.8f, style == 2 ? 0.42f : 0.3f);
                smoke.Emit(ep, 1);
            }

            // floor-light pop (2 pooled lights, round robin)
            var l = lights[nextLight];
            l.transform.position = muzzle + dir * 0.4f + Vector3.up * 0.2f;
            l.enabled = true; lightT[nextLight] = LightLife * (style == 2 ? 1.6f : 1); l.intensity = LightPeak[style] * Random.Range(0.85f, 1.1f);
            l.range = LightRange[style]; l.color = LightCol[style];
            nextLight = (nextLight + 1) % lights.Length;

            // casing out to the right (AK, SMG); the shotgun's hull comes out on the pump
            var right = new Vector3(dir.z, 0, -dir.x);
            if (style != 2) Casing(muzzle - dir * 0.45f, right, Brass, new Vector3(0.035f, 0.035f, 0.075f));
            else if (pump) { hullT = PumpBack; hullRight = right; hullAt = muzzle - dir * 0.4f; }
        }

        // the kit's rifle / minigun shot: the star + flame, a small warm floor-light pop, a casing; no smoke
        void KitShot(Vector3 muzzle, Vector3 dir)
        {
            bool heavy = Heavy, barrage = Abilities.BarrageT > 0;
            kit.Shot(heavy, barrage, dir);
            var l = lights[nextLight];
            l.transform.position = muzzle + dir * 0.35f + Vector3.up * 0.2f;
            l.enabled = true; lightT[nextLight] = LightLife; l.intensity = (heavy ? 0.9f : 1.3f) * Random.Range(0.85f, 1.1f);
            l.range = heavy ? 2.6f : 3.2f; l.color = new Color(1f, 0.72f, 0.36f);
            nextLight = (nextLight + 1) % lights.Length;
            Casing(muzzle - dir * 0.45f, new Vector3(dir.z, 0, -dir.x), Brass, new Vector3(0.035f, 0.035f, 0.075f));
        }

        // a bullet hitting an enemy (the kit's small spark; nothing in the old look)
        public void HitEnemy(Vector3 p, Vector3 back) { if (kit != null) kit.Impact(p, back, true, 2); }
        public GunfireKit Kit => kit;

        // the hull flips out at the pump's back stroke: 0.44 s after the shot (the pump sound starts at 0.34)
        public const float PumpBack = 0.44f;

        // a flat ring that snaps out on the floor (the Bloater's sac burst uses it too)
        public void Ring(Vector3 p, float size, Color c, float life)
        {
            var rp = new ParticleSystem.EmitParams { position = p, startLifetime = life, startSize = size, startColor = c, rotation = Random.value * 360f };
            rings.Emit(rp, 1);
        }

        // the reload's final rack: the last hull comes out on its back stroke
        public void Rack() { hullT = 0.1f; hullAt = anchor.position - lastDir * 0.4f; hullRight = new Vector3(lastDir.z, 0, -lastDir.x); }

        void EjectHull(Vector3 at, Vector3 right) => Casing(at, right, Hull, new Vector3(0.06f, 0.06f, 0.12f));

        void Casing(Vector3 at, Vector3 right, Color c, Vector3 size)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = right * Random.Range(1.8f, 2.8f) + Vector3.up * Random.Range(2.2f, 3.2f) + new Vector3(Random.Range(-0.4f, 0.4f), 0, Random.Range(-0.4f, 0.4f)),
                startLifetime = Random.Range(1.0f, 1.3f), startColor = c, startSize3D = size,
                rotation3D = new Vector3(Random.value * 360, Random.value * 360, Random.value * 360),
            };
            casings.Emit(ep, 1);
        }

        void Spark(Vector3 p, Vector3 v, float life, float size) => Spark(p, v, life, size, Color.white);
        void Spark(Vector3 p, Vector3 v, float life, float size, Color c)
        {
            var ep = new ParticleSystem.EmitParams { position = p, velocity = v, startLifetime = life, startSize = size, startColor = c };
            sparks.Emit(ep, 1);
        }

        // a bullet hitting a wall or a barrel: a little burst of streak sparks back off the surface
        public void Impact(Vector3 p, Vector3 back, int n = 5)
        {
            if (kit != null) { kit.Impact(p, back, false, n); return; }
            for (int i = 0; i < n; i++)
            {
                var v = (back + Random.insideUnitSphere * 0.9f); v.y = Mathf.Abs(v.y) * 0.8f + 0.2f;
                Spark(p, v.normalized * Random.Range(4f, 9f), Random.Range(0.12f, 0.24f), Random.Range(0.05f, 0.08f));
            }
        }

        // scaled time: pauses and slow motion freeze it along with the particles
        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < lights.Length; i++)
            {
                if (!lights[i].enabled) continue;
                lightT[i] -= dt;
                if (lightT[i] <= 0) { lights[i].enabled = false; continue; }
                lights[i].intensity *= Mathf.Exp(-dt * 40f);
            }
            if (hullT >= 0) { hullT -= dt; if (hullT < 0) EjectHull(hullAt, hullRight); }
        }

        public void ClearAll()
        {
            foreach (var ps in stars) ps.Clear();
            foreach (var ps in cones) ps.Clear();
            sparks.Clear(); smoke.Clear(); casings.Clear(); rings.Clear();
            foreach (var l in lights) l.enabled = false;
            hullT = -1;
            if (kit != null) kit.ClearAll();
        }
    }
}
