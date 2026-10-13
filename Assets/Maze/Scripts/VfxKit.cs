using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Hazard VFX, batch 1 (2026-10-13): acid / poison liquid, lava, charred ground (shader Maze/VfxPool), laser beams and floor
    // glow strips (Maze/VfxBeam), and one pooled particle system per sprite (Maze/VfxParticle): smoke and fire flipbooks
    // (8x8 at 2048), sparks, embers, glows, bubbles, droplets. Textures live in Resources/Fx/Vfx. Built once by Fx.Init.
    // Preview of the intended look: https://claude.ai/artifact/NGxReqCqH7Tqro7u9c6eBZ (same maths, same textures).
    // Batch 2: Fx.Sparks / Fx.Smoke draw here instead of cubes (FxSparks, FxSmoke), explosions (Explosion: fireball flipbook, flash,
    // sparks, embers, smoke), and the attack-warning material (TeleMat, shader Maze/VfxTele).
    public class VfxKit : MonoBehaviour
    {
        public static VfxKit I;

        // ---- looks (colours in gamma, as authored in the preview)
        public struct Look { public Color deep, mid, rim, stain; public float mode, erode, flow; public Vector2 dir; }
        static Color C(float r, float g, float b, float a = 1) => new Color(r, g, b, a);
        public static readonly Look Acid = new Look { deep = C(0.07f, 0.24f, 0.02f), mid = C(0.55f, 0.95f, 0.12f), rim = C(0.82f, 1f, 0.3f), stain = C(0.09f, 0.1f, 0.04f, 0.62f), mode = 0, erode = 0.75f, flow = 1f };
        public static readonly Look AcidPool = new Look { deep = Acid.deep, mid = Acid.mid, rim = Acid.rim, stain = Acid.stain, mode = 0, erode = 0.9f, flow = 0.8f };
        public static readonly Look Slag = new Look { deep = C(0.07f, 0.05f, 0.045f), mid = C(0.75f, 0.12f, 0.02f), rim = C(1f, 0.7f, 0.25f), stain = C(0.05f, 0.04f, 0.035f, 0.7f), mode = 1, erode = 0.35f, flow = 1.3f, dir = new Vector2(0, 1) };
        public static readonly Look Char = new Look { deep = C(0.05f, 0.045f, 0.04f), mid = C(0.6f, 0.12f, 0.02f), rim = C(1f, 0.55f, 0.12f), stain = C(0.06f, 0.05f, 0.045f, 0.7f), mode = 2, erode = 1.1f, flow = 0.5f };
        public static readonly Color AcidSplash = C(0.72f, 1f, 0.3f), AcidFume = C(0.75f, 0.95f, 0.45f);
        public static readonly Color GasLight = C(0.82f, 0.95f, 0.32f), GasDark = C(0.55f, 0.66f, 0.16f), GasMote = C(0.75f, 1f, 0.25f);


        Texture2D noise;
        Shader poolSh, beamSh, teleSh;
        public ParticleSystem smoke, fire, spark, ember, glow, bubble, drop, blast;
        static Mesh beamMesh;

        public void Init()
        {
            I = this;
            poolSh = Shader.Find("Maze/VfxPool"); beamSh = Shader.Find("Maze/VfxBeam");
            var partSh = Shader.Find("Maze/VfxParticle");
            if (poolSh == null || beamSh == null || partSh == null) Debug.LogError("Maze: VFX shaders missing (Resources/Shaders/Vfx*.shader)");
            noise = Tex("vfx_noise"); if (noise != null) noise.wrapMode = TextureWrapMode.Repeat;

            smoke  = Make("VfxSmoke", partSh, Tex("smoke_8x8"), false, 260, 0f, 8, 0.5f, Curve(0.6f, 1f), Fade(0.3f, 0.5f));
            fire   = Make("VfxFire", partSh, Tex("fire_8x8"), true, 240, 0f, 8, 0.5f, Curve(0.8f, 1.05f), Fade(0.15f, 0.55f));
            spark  = Make("VfxSpark", partSh, Tex("ember"), true, 300, 1.2f, 1, 0, Curve(1f, 0.4f), Fade(0.02f, 0.5f));
            ember  = Make("VfxEmber", partSh, Tex("ember"), true, 260, 0f, 1, 0, Curve(1f, 0.4f), Fade(0.05f, 0.6f));
            glow   = Make("VfxGlow", partSh, Tex("glow"), true, 80, 0f, 1, 0, Curve(0.4f, 1f), Fade(0.1f, 0.3f));
            bubble = Make("VfxBubble", partSh, Tex("bubble"), false, 160, 0f, 1, 0, new AnimationCurve(new Keyframe(0, 0.1f), new Keyframe(0.3f, 0.6f), new Keyframe(1, 1f)), Fade(0.1f, 0.88f));
            drop   = Make("VfxDrop", partSh, Tex("droplet"), false, 160, 1.8f, 1, 0, Curve(1f, 0.4f), Fade(0.05f, 0.8f));
            var sr = spark.GetComponent<ParticleSystemRenderer>(); sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.06f; sr.lengthScale = 2.2f;
            // fireballs: the smoke flipbook drawn additive and masked by its own alpha, coloured white-hot to orange to dark red over life
            blast  = Make("VfxBlast", partSh, Tex("smoke_8x8"), true, 120, 0f, 8, 0.3f, new AnimationCurve(new Keyframe(0, 0.4f, 0, 1.8f), new Keyframe(1, 1f, 0, 0)), Fade(0.05f, 0.45f));
            var bg = new Gradient();
            bg.SetKeys(new[] { new GradientColorKey(C(1f, 0.78f, 0.45f), 0), new GradientColorKey(C(1f, 0.58f, 0.16f), 0.08f), new GradientColorKey(C(1f, 0.42f, 0.16f), 0.4f),
                               new GradientColorKey(C(0.75f, 0.2f, 0.05f), 0.42f), new GradientColorKey(C(0.15f, 0.02f, 0.05f), 1f) },
                       new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.05f), new GradientAlphaKey(1, 0.45f), new GradientAlphaKey(0, 1) });
            var bcol = blast.colorOverLifetime; bcol.color = bg;
            var bm = blast.GetComponent<ParticleSystemRenderer>().sharedMaterial; bm.SetFloat("_MulTexAlpha", 1); bm.SetFloat("_FloorFade", 0.9f); bm.SetFloat("_Gain", 3f);   // additive sums ~3x dimmer in a Linear project than in the gamma preview
            spark.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetFloat("_Gain", 1.8f);   // kill / crit streaks read dim in Linear
            smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetFloat("_FloorFade", 0.6f);   // big sprites fade into the floor
            fire.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetFloat("_FloorFade", 0.12f);
            glow.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetFloat("_FloorFade", 0.5f);
            Drag(smoke, 1.2f); Drag(ember, 0.4f); Drag(blast, 5f);
            teleSh = Shader.Find("Maze/VfxTele"); if (teleSh == null) Debug.LogError("Maze: VfxTele shader missing");
            var dc = drop.collision; dc.enabled = true; dc.type = ParticleSystemCollisionType.Planes; dc.SetPlane(0, transform); dc.bounce = 0f; dc.dampen = 1f; dc.lifetimeLoss = 0.5f;
        }

        static Texture2D Tex(string n) { var t = Resources.Load<Texture2D>("Fx/Vfx/" + n); if (t == null) Debug.LogWarning("Maze: missing Fx/Vfx/" + n); return t; }
        static AnimationCurve Curve(float a, float b) => new AnimationCurve(new Keyframe(0, a), new Keyframe(1, b));
        static Gradient Fade(float fin, float fout)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, fin), new GradientAlphaKey(1, fout), new GradientAlphaKey(0, 1) });
            return g;
        }
        static void Drag(ParticleSystem ps, float d) { var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = d; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false; }

        ParticleSystem Make(string name, Shader sh, Texture2D tex, bool additive, int max, float gravity, int grid, float sheetSpan, AnimationCurve size, Gradient fade)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var shp = ps.shape; shp.enabled = false;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, size);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = fade;
            if (grid > 1)
            {   // looping flipbook from a random frame
                var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid;
                ts.numTilesX = grid; ts.numTilesY = grid; ts.animation = ParticleSystemAnimationType.WholeSheet;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, sheetSpan));
                ts.startFrame = new ParticleSystem.MinMaxCurve(0, grid * grid - 0.01f);
            }
            var m = new Material(sh) { name = name };
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetFloat("_Additive", additive ? 1 : 0);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingFudge = additive ? -10 : 0;   // glows in front of the smoke they sit in
            ps.Play();
            return ps;
        }

        public void ClearAll() { foreach (var ps in new[] { smoke, fire, spark, ember, glow, bubble, drop, blast }) if (ps != null) ps.Clear(); }

        static float R(float a, float b) => Random.Range(a, b);
        static Vector2 Disc(float r) => Random.insideUnitCircle * r;

        void One(ParticleSystem ps, Vector3 p, Vector3 v, float size, float life, Color c, float rotDeg = -1)
        {
            var ep = new ParticleSystem.EmitParams { position = p, velocity = v, startSize = size, startLifetime = life, startColor = c, rotation = rotDeg < 0 ? Random.value * 360f : rotDeg };
            ps.Emit(ep, 1);
        }

        // ================================================================ one-shot effects
        // a liquid hitting the floor: droplets flung out, a flash, a puff of fumes
        public void Splash(Vector3 at, Color c, float r)
        {
            int n = Mathf.RoundToInt(Mathf.Clamp(r * 12, 6, 28));
            for (int i = 0; i < n; i++) { float a = R(0, Mathf.PI * 2), s = R(2, 6) * Mathf.Min(1, r / 2f); One(drop, at + Vector3.up * 0.2f, new Vector3(Mathf.Cos(a) * s, R(2.5f, 6f), Mathf.Sin(a) * s), R(0.16f, 0.3f), R(0.5f, 0.9f), c); }
            One(glow, at + Vector3.up * 0.1f, Vector3.zero, r * 2.5f, 0.5f, new Color(c.r * 0.8f, c.g, c.b * 0.7f, 0.8f));
            for (int i = 0; i < 4; i++) { var d = Disc(r * 0.55f); One(smoke, at + new Vector3(d.x, 0.4f, d.y), new Vector3(d.x, R(0.6f, 1.2f), d.y), R(1.6f, 2.2f), R(0.9f, 1.4f), new Color(0.8f, 0.95f, 0.5f, 0.5f)); }
        }

        public void Sparks(Vector3 at, int n, Color c, float speed, float life = 0.35f, float size = 0.28f)
        {
            for (int i = 0; i < n; i++) One(spark, at, new Vector3(R(-1, 1) * speed, R(0, 1) * speed, R(-1, 1) * speed), size * R(0.7f, 1.1f), life * R(0.7f, 1.3f), c);
        }
        public void Flash(Vector3 at, float size, Color c, float life = 0.35f) => One(glow, at, Vector3.zero, size, life, c);
        public void Puff(Vector3 at, Vector3 vel, float size, float life, Color c) => One(smoke, at, vel, size, life, c);

        // ================================================================ batch 2: shared sparks, smoke, explosions, warnings
        // Fx.Sparks with the same arguments (spread, colour, speed, life, cube size): glowing streaks instead of cubes, a small flash for big bursts
        public void FxSparks(Vector3 at, int n, Color c, float speed, float life, float size, float up)
        {
            float s = Mathf.Clamp(size * 3f, 0.16f, 0.42f); c.a = 1;
            for (int i = 0; i < n; i++)
            {
                float a = R(0, Mathf.PI * 2), sp = speed * R(0.3f, 1f);
                One(spark, at, new Vector3(Mathf.Cos(a) * sp, R(0.3f, 1f) * up, Mathf.Sin(a) * sp), s * R(0.7f, 1.1f), life * R(0.6f, 1f), c);
            }
            if (n >= 8) One(glow, at, Vector3.zero, 0.8f + speed * 0.15f, 0.14f, new Color(c.r, c.g, c.b, 0.55f));
        }
        // Fx.Smoke with the same arguments: soft billowing puffs (dust, steam, smoke), fewer and larger than the cubes were
        public void FxSmoke(Vector3 at, int n, Color c, float speed, float life, float size, float up)
        {
            int m = Mathf.Max(1, Mathf.CeilToInt(n * 0.6f)); float s = size * 3.5f + 0.3f; c.a = 0.5f;
            for (int i = 0; i < m; i++)
            {
                float a = R(0, Mathf.PI * 2), sp = speed * R(0.3f, 1f) * 0.8f;
                One(smoke, at + new Vector3(0, 0.2f, 0), new Vector3(Mathf.Cos(a) * sp, R(0.3f, 1f) * up, Mathf.Sin(a) * sp), s * R(0.8f, 1.2f), life * 1.5f + 0.3f, c);
            }
        }
        // a fireball: hot blasts that swell and darken, a flash, sparks, embers, a column of smoke (R = blast radius in metres, at = floor point)
        public void Explosion(Vector3 at, float r)
        {
            for (int i = 0; i < 12; i++)
            {
                var d = Disc(r * 0.3f); float s = R(3f, 6f) * r / 2.5f;
                One(blast, at + new Vector3(d.x, R(1.3f, 1.9f), d.y), new Vector3(d.x * s, R(1f, 3f), d.y * s), r * 1.25f, R(0.5f, 0.8f), new Color(1, 1, 1, 0.8f));
            }
            One(glow, at + Vector3.up, Vector3.zero, r * 2f, 0.22f, new Color(1f, 0.7f, 0.4f, 0.45f));
            One(glow, at + Vector3.up * 1.6f, Vector3.zero, r * 2.6f, 0.6f, new Color(1f, 0.45f, 0.12f, 0.3f));
            for (int i = 0; i < 30; i++) { float a = R(0, Mathf.PI * 2), s = R(6f, 13f); One(spark, at + Vector3.up, new Vector3(Mathf.Cos(a) * s, R(2f, 9f), Mathf.Sin(a) * s), 0.32f, R(0.3f, 0.6f), new Color(1f, 0.75f, 0.35f, 1f)); }
            for (int i = 0; i < 14; i++) One(ember, at + new Vector3(R(-1f, 1f), 1f, R(-1f, 1f)), new Vector3(R(-3f, 3f), R(3f, 6f), R(-3f, 3f)), 0.15f, R(1f, 1.8f), new Color(1f, 0.6f, 0.2f, 1f));
            for (int i = 0; i < 8; i++) { var d = Disc(r * 0.35f); One(smoke, at + new Vector3(d.x, R(1f, 1.8f), d.y), new Vector3(d.x * 0.6f, R(1f, 2f), d.y * 0.6f), r * 1.5f, R(2f, 2.8f), new Color(0.24f, 0.23f, 0.23f, 0.6f)); }
        }
        // the attack-warning material for EnemyAI.Tele (keeps the _BaseColor name the Tele code sets)
        public Material TeleMat() => teleSh != null ? new Material(teleSh) { name = "VfxTele" } : null;

        // ================================================================ continuous emitters (call every frame with dt)
        // poison gas cloud (spore pods, toxic vents): churning low cloud with a darker core and drifting motes
        public void Gas(Vector3 at, float r, float dt, float strength = 1f)
        {
            HazardAudio.Loop("amb_toxic_gas", new Vector2Int(Mathf.RoundToInt(at.x / 3f), Mathf.RoundToInt(at.z / 3f)), at);   // one voice per ~3 m cloud
            if (Random.value < dt * 14f * strength)
            {
                var d = Disc(r * 0.85f); bool dark = Random.value < 0.4f; var c = dark ? GasDark : GasLight; c.a = dark ? 0.42f : 0.34f;
                One(smoke, at + new Vector3(d.x, R(0.4f, 1.1f), d.y), new Vector3(d.x * 0.08f + R(-0.15f, 0.15f), R(0.05f, 0.22f), d.y * 0.08f + R(-0.15f, 0.15f)), R(3.4f, 4.4f), R(3.2f, 4.6f), c);
            }
            if (Random.value < dt * 10f * strength) { var d = Disc(r * 0.9f); One(ember, at + new Vector3(d.x, R(0.2f, 1.4f), d.y), new Vector3(R(-0.3f, 0.3f), R(0.1f, 0.4f), R(-0.3f, 0.3f)), 0.12f, R(1.5f, 2.5f), new Color(GasMote.r, GasMote.g, GasMote.b, 0.8f)); }
        }

        // a pool's own life: bubbles + fumes (liquid), heat glow + embers + smoke (lava), flames + embers + smoke + firelight (char)
        public void Ambient(VfxPool p, float dt)
        {
            float area = p.Area, k = Mathf.Min(area / 12f, 4f);   // rate scales with size, capped (fire is the fill-rate risk)
            switch ((int)p.look.mode)
            {
                case 0:
                    if (Random.value < dt * 9f * k) { var a = p.RandomPoint(0.8f); bool big = Random.value < 0.3f; One(bubble, a + Vector3.up * 0.06f, Vector3.zero, big ? 0.42f : 0.24f, R(0.5f, 1.1f), new Color(0.75f, 1f, 0.35f, 0.95f)); }
                    if (Random.value < dt * 2.5f * k) { var a = p.RandomPoint(0.75f); One(smoke, a + Vector3.up * 0.15f, new Vector3(R(-0.1f, 0.1f), R(0.35f, 0.7f), R(-0.1f, 0.1f)), R(1.4f, 1.7f), R(1.6f, 2.4f), new Color(AcidFume.r, AcidFume.g, AcidFume.b, 0.22f)); }
                    break;
                case 1:
                    if (Random.value < dt * 1.2f * k) One(glow, p.RandomPoint(0.7f) + Vector3.up * 0.15f, Vector3.zero, 3f, 1.2f, new Color(1f, 0.4f, 0.08f, 0.22f));
                    if (Random.value < dt * 1f * k) { var a = p.RandomPoint(0.7f); for (int i = 0; i < 6; i++) One(spark, a + Vector3.up * 0.1f, new Vector3(R(-1, 1), R(2, 4), R(-1, 1)), 0.12f, R(0.5f, 0.9f), new Color(1f, 0.65f, 0.25f, 1f)); }
                    if (Random.value < dt * 0.8f * k) One(smoke, p.RandomPoint(0.7f) + Vector3.up * 0.6f, new Vector3(0.2f, R(0.6f, 1f), 0), 2.4f, 2.4f, new Color(0.3f, 0.27f, 0.26f, 0.25f));
                    break;
                default:
                    if (Random.value < dt * 8f * k) { var a = p.RandomPoint(0.85f); float s = R(1.3f, 2.3f); One(fire, a + Vector3.up * (s * 0.42f), new Vector3(0, R(0.5f, 1.1f), 0), s * 1.1f, R(0.6f, 1f), Color.white, 360f + R(-8f, 8f)); }   // flames stay upright
                    if (Random.value < dt * 3.5f * k) One(ember, p.RandomPoint(0.8f) + Vector3.up * 0.5f, new Vector3(R(-0.5f, 0.5f), R(2f, 3.6f), R(-0.5f, 0.5f)), 0.13f, R(1f, 1.8f), new Color(1f, 0.6f, 0.2f, 1f));
                    if (Random.value < dt * 1.2f * k) One(smoke, p.RandomPoint(0.7f) + Vector3.up * 1.7f, new Vector3(0.25f, R(0.9f, 1.4f), 0), 3f, R(2f, 2.8f), new Color(0.22f, 0.2f, 0.2f, 0.42f));
                    if (Random.value < dt * 1.5f * k) One(glow, p.RandomPoint(0.3f) + Vector3.up * 0.2f, Vector3.zero, 7.5f, 0.6f, new Color(1f, 0.42f, 0.1f, 0.22f));
                    break;
            }
        }

        // ================================================================ surfaces
        Material PoolMat(Look l)
        {
            var m = new Material(poolSh) { name = "VfxPool" };
            m.SetTexture("_Noise", noise);
            m.SetColor("_Deep", l.deep); m.SetColor("_Mid", l.mid); m.SetColor("_Rim", l.rim); m.SetColor("_Stain", l.stain);
            m.SetFloat("_Mode", l.mode); m.SetFloat("_Erode", l.erode); m.SetFloat("_Flow", l.flow); m.SetVector("_Dir", l.dir);
            m.SetFloat("_Seed", Random.Range(0f, 40f)); m.SetFloat("_Life", 1);
            return m;
        }
        static Mesh Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var m = new Mesh { vertices = new[] { a, b, c, d }, triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 } };   // both faces
            m.RecalculateBounds(); return m;
        }
        VfxPool Spawn(Transform parent, string name, Mesh mesh, Material m, Vector3 pos)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.rotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return new VfxPool { tr = go.transform, m = m, mesh = mesh };
        }

        // a round pool at a map point (radius in metres)
        public VfxPool Pool(Transform parent, Vector2 at, float r, Look look, float y = 0.035f)
        {
            float h = r + 0.7f;   // room for the stain
            var mesh = Quad(new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(-h, 0, h));
            var m = PoolMat(look); m.SetFloat("_Shape", 0); m.SetVector("_Half", new Vector4(r, r, 0, 0));
            var p = Spawn(parent, "VfxPool", mesh, m, MazeMap.ToWorld(at, y)); p.look = look; p.center = p.tr.position; p.r = r;
            return p;
        }

        // map cells (acid 'a', slag '*', fire 'f'): one surface over their bounds, shaped by a blurred cell mask so neighbours merge
        public VfxPool Cells(Transform parent, List<Vector2Int> cells, Look look, float y = 0.03f)
        {
            if (cells == null || cells.Count == 0) return null;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (var c in cells) { x0 = Mathf.Min(x0, c.x); y0 = Mathf.Min(y0, c.y); x1 = Mathf.Max(x1, c.x); y1 = Mathf.Max(y1, c.y); }
            x0--; y0--; x1++; y1++;                                 // one cell of padding for the soft edge and the stain
            const int ppc = 8; int W = (x1 - x0 + 1) * ppc, H = (y1 - y0 + 1) * ppc;
            var px = new float[W * H];
            foreach (var c in cells)
                for (int j = 0; j < ppc; j++) for (int i = 0; i < ppc; i++) px[((c.y - y0) * ppc + j) * W + (c.x - x0) * ppc + i] = 1f;
            px = Blur(Blur(px, W, H, 2), W, H, 2);
            var tex = new Texture2D(W, H, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "VfxCellMask" };
            var bytes = new byte[W * H]; for (int k = 0; k < bytes.Length; k++) bytes[k] = (byte)Mathf.RoundToInt(Mathf.Clamp01(px[k]) * 255f);
            tex.LoadRawTextureData(bytes); tex.Apply(false, true);

            float C = MazeMap.C;
            Vector2 m0 = new Vector2(x0 * C, y0 * C), m1 = new Vector2((x1 + 1) * C, (y1 + 1) * C);
            Vector3 w00 = MazeMap.ToWorld(m0, y), w11 = MazeMap.ToWorld(m1, y), w10 = MazeMap.ToWorld(new Vector2(m1.x, m0.y), y), w01 = MazeMap.ToWorld(new Vector2(m0.x, m1.y), y);
            var org = (w00 + w11) * 0.5f;
            var mesh = Quad(w00 - org, w10 - org, w11 - org, w01 - org);
            var m = PoolMat(look); m.SetFloat("_Shape", 2); m.SetTexture("_Mask", tex); m.SetFloat("_MaskScale", 2f);
            // world -> mask uv; dx / dz may be negative when the map axis is flipped in world space
            float dx = w11.x - w00.x, dz = w11.z - w00.z;
            m.SetVector("_MaskRect", new Vector4(w00.x, w00.z, 1f / dx, 1f / dz));
            if (look.mode == 0) m.SetFloat("_Erode", 0.5f);
            var p = Spawn(parent, "VfxCells", mesh, m, org); p.look = look; p.mask = tex; p.center = org;
            p.cells = new List<Vector3>(); foreach (var c in cells) p.cells.Add(MazeMap.ToWorld(MazeMap.Center(c.x, c.y), y));
            return p;
        }

        static float[] Blur(float[] src, int W, int H, int r)
        {   // separable box blur
            var tmp = new float[src.Length]; var dst = new float[src.Length]; float inv = 1f / (2 * r + 1);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { float s = 0; for (int k = -r; k <= r; k++) s += src[y * W + Mathf.Clamp(x + k, 0, W - 1)]; tmp[y * W + x] = s * inv; }
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { float s = 0; for (int k = -r; k <= r; k++) s += tmp[Mathf.Clamp(y + k, 0, H - 1) * W + x]; dst[y * W + x] = s * inv; }
            return dst;
        }

        // ================================================================ beams
        public static Mesh BeamMesh
        {
            get
            {
                if (beamMesh != null) return beamMesh;
                const int seg = 24; var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var t = new List<int>();
                for (int i = 0; i <= seg; i++)
                {
                    float z = i / (float)seg; v[i * 2] = new Vector3(0, 0, z); v[i * 2 + 1] = new Vector3(0, 0, z);
                    uv[i * 2] = new Vector2(-1, z); uv[i * 2 + 1] = new Vector2(1, z);
                    if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
                }
                beamMesh = new Mesh { name = "VfxBeam", vertices = v, uv = uv, triangles = t.ToArray() };
                beamMesh.bounds = new Bounds(new Vector3(0, 0, 0.5f), new Vector3(3, 3, 1.2f));   // the shader widens it sideways
                return beamMesh;
            }
        }
        public VfxBeam Beam(Transform parent, Color c, float width, float core = 0.1f, float scroll = 1.4f, bool flat = false)
        {
            var go = new GameObject(flat ? "VfxStrip" : "VfxBeam"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = BeamMesh;
            var m = new Material(beamSh) { name = "VfxBeam" };
            m.SetTexture("_Noise", noise); m.SetColor("_Color", c); m.SetFloat("_Width", width); m.SetFloat("_Core", core); m.SetFloat("_Scroll", scroll); m.SetFloat("_Flat", flat ? 1 : 0);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return new VfxBeam { tr = go.transform, m = m };
        }

        // a laser fence from its '|' cells: straight runs, a post every 2 cells, three beams and a floor strip per run
        public VfxFence Fence(Transform parent, List<Vector2Int> cells, Color c)
        {
            var f = new VfxFence { kit = this };
            var set = new HashSet<Vector2Int>(cells); var used = new HashSet<Vector2Int>();
            var postMat = Mats.Lit(new Color(0.17f, 0.19f, 0.22f), null, null, 0.55f, 0.6f);
            f.capMat = Mats.Unlit(c);
            float Cs = MazeMap.C;
            foreach (var start in cells)
            {
                if (used.Contains(start)) continue;
                bool vertical = set.Contains(start + Vector2Int.up) || set.Contains(start + Vector2Int.down);
                var step = vertical ? Vector2Int.up : Vector2Int.right;
                var a = start; while (set.Contains(a - step)) a -= step;
                var b = a; used.Add(b); while (set.Contains(b + step)) { b += step; used.Add(b); }
                int n = vertical ? b.y - a.y + 1 : b.x - a.x + 1;
                Vector2 ca = MazeMap.Center(a.x, a.y), cb = MazeMap.Center(b.x, b.y), dir = ((Vector2)step).normalized;
                Vector2 pa = ca - dir * Cs * 0.5f, pb = cb + dir * Cs * 0.5f;
                for (int k = 0; k <= n; k += 2) Post(parent, f, postMat, Vector2.Lerp(pa, pb, k / (float)n));
                if (n % 2 == 1) Post(parent, f, postMat, pb);
                foreach (var h in new[] { 0.35f, 0.8f, 1.25f })
                {
                    var bm = Beam(parent, c, 0.6f, 0.1f, 1.4f); bm.Set(MazeMap.ToWorld(pa, h), MazeMap.ToWorld(pb, h)); f.beams.Add(bm);
                }
                var st = Beam(parent, c, 1.2f, 0.06f, 0.6f, true); st.Set(MazeMap.ToWorld(pa, 0.04f), MazeMap.ToWorld(pb, 0.04f)); st.baseI = 0.5f; f.beams.Add(st);
                f.runs.Add((MazeMap.ToWorld(pa, 0), MazeMap.ToWorld(pb, 0)));
            }
            f.color = c;
            return f;
        }
        void Post(Transform parent, VfxFence f, Material mat, Vector2 at)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(p.GetComponent<Collider>()); p.name = "FencePost";
            p.transform.SetParent(parent, false); p.transform.position = MazeMap.ToWorld(at, 0.85f); p.transform.localScale = new Vector3(0.34f, 1.7f, 0.34f);
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
            foreach (var h in new[] { 0.35f, 0.8f, 1.25f })
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(cap.GetComponent<Collider>()); cap.name = "Emitter";
                cap.transform.SetParent(parent, false); cap.transform.position = MazeMap.ToWorld(at, h); cap.transform.localScale = new Vector3(0.38f, 0.1f, 0.38f);
                var mr = cap.GetComponent<MeshRenderer>(); mr.sharedMaterial = f.capMat; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    public class VfxPool
    {
        public Transform tr; public Material m; public Mesh mesh; public Texture2D mask;
        public VfxKit.Look look; public Vector3 center; public float r; public List<Vector3> cells;
        static readonly int LifeId = Shader.PropertyToID("_Life");
        public float Area => cells != null ? cells.Count * MazeMap.C * MazeMap.C : Mathf.PI * r * r;
        // 0 = gone, 1 = full size (grows in, shrinks and fades out)
        public void SetLife(float k) { if (m != null) m.SetFloat(LifeId, Mathf.Clamp01(k)); }
        public Vector3 RandomPoint(float spread)
        {
            if (cells != null && cells.Count > 0) { var c = cells[Random.Range(0, cells.Count)]; var d = Random.insideUnitCircle * MazeMap.C * 0.5f * spread * 1.2f; return c + new Vector3(d.x, 0, d.y); }
            var e = Random.insideUnitCircle * r * spread; return center + new Vector3(e.x, 0, e.y);
        }
        public void Ambient(float dt)
        {
            if (VfxKit.I == null || Game.SimRun) return; VfxKit.I.Ambient(this, dt);
            HazardAudio.Loop(look.mode == 0 ? "amb_acid_pool" : look.mode == 1 ? "amb_molten_slag" : "amb_fire", this, cells != null ? HazardAudio.Nearest(cells, center) : center);   // the pool's sound, from its point nearest the hero
        }
        public void Kill()
        {
            if (tr != null) Object.Destroy(tr.gameObject);
            if (m != null) Object.Destroy(m);
            if (mask != null) Object.Destroy(mask);
            if (mesh != null) Object.Destroy(mesh);
            tr = null; m = null;
        }
    }

    public class VfxBeam
    {
        public Transform tr; public Material m; public float baseI = 1f;
        static readonly int IntensityId = Shader.PropertyToID("_Intensity"), WidthId = Shader.PropertyToID("_Width");
        public void Set(Vector3 a, Vector3 b)
        {
            var d = b - a; float L = d.magnitude; if (L < 1e-3f) { tr.gameObject.SetActive(false); return; }
            tr.position = a; tr.rotation = Quaternion.LookRotation(d / L, Vector3.up); tr.localScale = new Vector3(1, 1, L);
        }
        public float Intensity { set { m.SetFloat(IntensityId, value * baseI); bool on = value > 0.01f; if (tr.gameObject.activeSelf != on) tr.gameObject.SetActive(on); } }
        public float Width { set => m.SetFloat(WidthId, value); }
        public bool Visible { set { if (tr.gameObject.activeSelf != value) tr.gameObject.SetActive(value); } }
    }

    public class VfxFence
    {
        public VfxKit kit; public readonly List<VfxBeam> beams = new(); public readonly List<(Vector3 a, Vector3 b)> runs = new();
        public Material capMat; public Color color; bool wasOn;
        // f: 0 off .. 1 on (flicker values in between); on: the fence is live this frame (sparks)
        public void Set(float f, bool on, float dt)
        {
            foreach (var b in beams) b.Intensity = f;
            if (capMat != null) capMat.SetColor("_BaseColor", Color.Lerp(color * 0.3f, color, on ? 1 : f));
            if (Game.SimRun || kit == null) { wasOn = on; return; }
            foreach (var (a, b) in runs)
            {
                if (on && Random.value < dt * 10f * Mathf.Max(1, (b - a).magnitude / 8f))
                    kit.Sparks(Vector3.Lerp(a, b, Random.value) + Vector3.up * new[] { 0.35f, 0.8f, 1.25f }[Random.Range(0, 3)], 3, new Color(1f, 0.45f, 0.5f), 2.5f, 0.25f, 0.2f);
                if (on && !wasOn) for (int i = 0; i < 20; i++) kit.Sparks(Vector3.Lerp(a, b, Random.value) + Vector3.up * Random.Range(0.3f, 1.3f), 1, new Color(1f, 0.5f, 0.55f), 3.5f, 0.3f, 0.22f);
            }
            wasOn = on;
        }
    }
}
