using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Particles (cube debris, sparks, smoke, goo), bullet tracers, explosions, scorch marks and goo splats.
    public class Fx : MonoBehaviour
    {
        public static Fx I;
        ParticleSystem debris, sparks, smoke, goo, splats;
        Mesh cube, ring, quad, blob, puffMesh;
        Weapon[] weapons;
        readonly Stack<Transform> tracerPool = new();
        Material scorchMat;
        readonly List<GameObject> scorches = new();
        Light boomLight;

        class BoomFx { public float t, R; public Vector3 p; public List<(Transform tr, Material m, Vector3 off, float s, float delay, float rise, Color c)> puffs = new(); public Transform core, ring; public Material coreMat, ringMat; }
        readonly List<BoomFx> booms = new();
        readonly Stack<(Transform, Material)> puffPool = new();

        // goo: blob splats queued to appear where a flung blob lands
        struct PendingSplat { public Vector3 p; public float t, size; public Color c; public bool quiet; }
        readonly List<PendingSplat> pendingSplats = new();
        const float GooGravity = 1.7f;
        static readonly Color GooPurple = new Color32(0x8a, 0x2b, 0xe2, 255), GooBright = new Color32(0xb0, 0x4c, 0xff, 255), GooAcid = new Color32(0x8f, 0xe0, 0x3a, 255);
        // the Bloater's pus
        static readonly Color Pus = new Color32(0x7c, 0xc8, 0x22, 255), PusBright = new Color32(0xb6, 0xf0, 0x4a, 255), PusDark = new Color32(0x3e, 0x74, 0x12, 255);
        static Color PusCol() { var c = Random.value < 0.3f ? PusBright : Pus; return Color.Lerp(c, PusDark, Random.value * 0.35f); }

        public void Init(Weapon[] weapons)
        {
            I = this;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            ring = RingMesh(0.86f, 1f, 48);

            var floorPlane = new GameObject("ParticleFloor").transform;
            floorPlane.SetParent(transform, false);
            var lit = Mats.Particles(false);
            debris = MakePS("Debris", lit, 2.2f, floorPlane, 1200);
            sparks = MakePS("Sparks", Mats.Particles(true), 1.4f, floorPlane, 400);
            smoke = MakePS("Smoke", lit, -0.12f, null, 200);
            blob = BlobMesh(8, 5);
            goo = MakePS("Goo", Mats.ParticlesGloss(), GooGravity, floorPlane, 1600);
            goo.GetComponent<ParticleSystemRenderer>().mesh = blob;
            var gm = goo.main; gm.startSize3D = true;
            var gc = goo.collision; gc.bounce = 0.05f; gc.dampen = 0.9f; gc.lifetimeLoss = 0.35f; // land with a plop and shrink away
            var grot = goo.rotationOverLifetime; grot.x = grot.y = grot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            splats = MakeSplats(Mats.ParticlesFade(ProcTex.Goo()), 700);
            var lv = smoke.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 2.5f; lv.multiplyDragByParticleSize = false;
            var sol = smoke.sizeOverLifetime; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.7f, 1.4f), new Keyframe(1, 0)));

            this.weapons = weapons;
            puffMesh = BlobMesh(16, 10); // explosion puffs (a mesh, not CreatePrimitive: its SphereCollider is stripped on iOS)
            var gf = new GameObject("GunFx"); gf.transform.SetParent(transform, false);
            gf.AddComponent<GunFx>().Init(cube);
            scorchMat = Mats.Fade(Color.white, ProcTex.Scorch());

            var lg = new GameObject("BoomLight");
            lg.transform.SetParent(transform, false);
            boomLight = lg.AddComponent<Light>();
            boomLight.type = LightType.Point; boomLight.color = new Color(1f, 0.54f, 0.23f); boomLight.range = 16f; boomLight.intensity = 0f; boomLight.shadows = LightShadows.None;
        }

        ParticleSystem MakePS(string name, Material mat, float gravity, Transform collidePlane, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity; main.startRotation3D = true;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f); rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f); rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.8f, 1), new Keyframe(1, 0)));
            if (collidePlane != null)
            {
                var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes;
                col.SetPlane(0, collidePlane); col.bounce = 0.3f; col.dampen = 0.45f; col.lifetimeLoss = 0f; col.radiusScale = 0.5f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = cube; r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.alignment = ParticleSystemRenderSpace.World;
            ps.Play();
            return ps;
        }

        // floor splats: flat billboards that spread in fast and fade out (one draw call for all of them)
        ParticleSystem MakeSplats(Material mat, int max)
        {
            var go = new GameObject("GooSplats");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.35f), new Keyframe(0.02f, 1f, 4f, 0f), new Keyframe(1, 1.06f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.55f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard; r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingFudge = 10; // under the flying blobs and smoke
            ps.Play();
            return ps;
        }

        // low-poly sphere for goo blobs
        static Mesh BlobMesh(int seg, int rings)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float th = Mathf.PI * j / rings;
                for (int i = 0; i <= seg; i++)
                {
                    float ph = Mathf.PI * 2 * i / seg;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * 0.5f);
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + seg + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b);
                    t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0);
            var n = new Vector3[v.Count]; for (int i = 0; i < n.Length; i++) n[i] = v[i].normalized;
            m.normals = n; m.RecalculateBounds();
            return m;
        }

        public static Mesh RingMesh(float inner, float outer, int seg)
        {
            var v = new Vector3[seg * 2]; var t = new int[seg * 6]; var uv = new Vector2[seg * 2];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                v[i * 2] = new Vector3(Mathf.Cos(a) * inner, 0, Mathf.Sin(a) * inner);
                v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * outer, 0, Mathf.Sin(a) * outer);
                int j = (i + 1) % seg;
                t[i * 6] = i * 2; t[i * 6 + 1] = j * 2; t[i * 6 + 2] = i * 2 + 1;
                t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = j * 2; t[i * 6 + 5] = j * 2 + 1;
            }
            var m = new Mesh { vertices = v, triangles = t, uv = uv };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
        public static Mesh Ring => I.ring;

        void Emit(ParticleSystem ps, Vector3 pos, int n, Color color, float speed, float life, float size, float up)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2, sp = speed * Random.Range(0.3f, 1f);
                ep.position = pos;
                ep.velocity = new Vector3(Mathf.Cos(a) * sp, Random.Range(0.3f, 1f) * up, Mathf.Sin(a) * sp);
                ep.startColor = color;
                ep.startLifetime = life * Random.Range(0.6f, 1f);
                ep.startSize = size * Random.Range(0.6f, 1.2f);
                ep.rotation3D = new Vector3(Random.value * 360, Random.value * 360, 0);
                ps.Emit(ep, 1);
            }
        }
        public void Debris(Vector3 p, int n, Color c, float speed, float life, float size, float up = 4f) => Emit(debris, p, n, c, speed, life, size, up);
        public void Sparks(Vector3 p, int n, Color c, float speed, float life, float size, float up = 4f) => Emit(sparks, p, n, c, speed, life, size, up);
        public void Smoke(Vector3 p, int n, Color c, float speed, float life, float size, float up = 1f) => Emit(smoke, p, n, c, speed, life, size, up);

        // ---- goo (the Drone's death and hits)
        static Color GooCol(float r)
        {
            var c = r < 0.28f ? GooBright : GooPurple;
            return Color.Lerp(c, new Color(0.32f, 0.08f, 0.5f), Random.value * 0.35f);
        }

        void Splat(Vector3 p, float size, Color c, float life = 4f)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(p.x, 0.025f + Random.value * 0.01f, p.z),
                velocity = Vector3.zero, startColor = c, startSize = size,
                rotation = Random.value * 360, startLifetime = life * Random.Range(0.9f, 1.1f),
            };
            splats.Emit(ep, 1);
        }

        void Blob(Vector3 p, Vector3 vel, float size, Color c, float life, bool splatOnLanding)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = p, velocity = vel, startColor = c, startLifetime = life,
                startSize3D = new Vector3(size * Random.Range(0.85f, 1.3f), size * Random.Range(0.6f, 0.95f), size * Random.Range(0.85f, 1.3f)),
                rotation3D = new Vector3(Random.value * 360, Random.value * 360, 0),
            };
            goo.Emit(ep, 1);
            if (!splatOnLanding) return;
            float g = 9.81f * GooGravity, tl = (vel.y + Mathf.Sqrt(vel.y * vel.y + 2 * g * Mathf.Max(0, p.y))) / g;
            pendingSplats.Add(new PendingSplat { p = p + new Vector3(vel.x, 0, vel.z) * tl, t = tl, size = size * Random.Range(2.8f, 4f), c = c });
        }

        // the Drone pops: glossy purple blobs arc out and splat, a few glowing acid-green flecks, a puddle that fades
        public void GooBurst(Vector3 center, float scale)
        {
            int n = Mathf.RoundToInt(11 * scale);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2, sp = Random.Range(1.2f, 4.2f) * scale;
                var vel = new Vector3(Mathf.Cos(a) * sp, Random.Range(2.2f, 5.5f), Mathf.Sin(a) * sp);
                Blob(center, vel, 0.15f * scale * Random.Range(0.6f, 1.3f), GooCol(Random.value), Random.Range(1.1f, 1.6f), i < 5);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = Random.value * Mathf.PI * 2, sp = Random.Range(2f, 5f) * scale;
                Blob(center, new Vector3(Mathf.Cos(a) * sp, Random.Range(3f, 6f), Mathf.Sin(a) * sp), 0.06f * scale, GooAcid, Random.Range(0.9f, 1.3f), false);
            }
            Sparks(center, 4, GooAcid, 3.5f * scale, 0.45f, 0.06f, 4);
            Splat(center, Random.Range(1.4f, 1.9f) * scale, Color.Lerp(GooPurple, new Color(0.3f, 0.06f, 0.45f), Random.value * 0.4f));
        }

        // a shotgun blast that hit several Drones: one bigger fan of goo thrown along the shot
        public void GooSpray(Vector3 center, Vector3 dir, int hits)
        {
            int n = Mathf.Min(6 + hits * 2, 24);
            for (int i = 0; i < n; i++)
            {
                var side = new Vector3(dir.z, 0, -dir.x) * Random.Range(-1.6f, 1.6f);
                var vel = dir * Random.Range(3f, 7.5f) + side + Vector3.up * Random.Range(1.5f, 4f);
                Blob(center + Random.insideUnitSphere * 0.4f, vel, Random.Range(0.08f, 0.17f), GooCol(Random.value), Random.Range(0.8f, 1.3f), i % 3 == 0);
            }
            Sparks(center, 3, GooAcid, 4f, 0.4f, 0.05f, 3);
        }

        // a Drone shoved by a blast smears goo along its slide: splats laid down as it passes
        public void GooSmear(Vector3 from, Vector3 dir, float len, float speed)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(len / 0.35f), 2, 8);
            for (int i = 0; i < n; i++)
            {
                float d = len * (i + 0.5f) / n;
                pendingSplats.Add(new PendingSplat
                {
                    p = from + dir * d + new Vector3(Random.Range(-0.08f, 0.08f), 0, Random.Range(-0.08f, 0.08f)),
                    t = d / Mathf.Max(1f, speed * 0.6f),
                    size = Mathf.Lerp(0.75f, 0.35f, i / (float)n) * Random.Range(0.85f, 1.15f),
                    c = Color.Lerp(GooPurple, new Color(0.3f, 0.06f, 0.45f), 0.3f + Random.value * 0.3f), quiet = true,
                });
            }
        }

        // a bullet hit: a couple of small drops (green pus on a Bloater)
        public void GooHit(Vector3 p, Vector3 dir, bool pus = false)
        {
            for (int i = 0; i < 2; i++)
            {
                var vel = dir * Random.Range(1.5f, 3f) + new Vector3(Random.Range(-1f, 1f), Random.Range(1.5f, 3f), Random.Range(-1f, 1f));
                Blob(p, vel, Random.Range(0.06f, 0.1f) * (pus ? 1.3f : 1), pus ? PusCol() : GooCol(Random.value), Random.Range(0.6f, 0.9f), i == 0 && Random.value < 0.35f);
            }
        }

        // ---- the Bloater's pus
        // a drip or bubble off the sacs: a small glob that lands as a little puddle fading behind it
        public void PusDrip(Vector3 p, float scale)
        {
            var vel = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.4f, 1.4f), Random.Range(-0.5f, 0.5f));
            Blob(p, vel, Random.Range(0.05f, 0.09f) * Mathf.Sqrt(scale), PusCol(), Random.Range(0.9f, 1.2f), Random.value < 0.6f);
        }

        // a sac bursting to let minis out: glossy pus thrown out along `dir` (where the minis leap), a big
        // puddle and a few splats around it
        public void PusSplat(Vector3 p, float scale, Vector3 dir)
        {
            int n = Random.Range(8, 13);
            var side = new Vector3(dir.z, 0, -dir.x);
            for (int i = 0; i < n; i++)
            {
                var vel = dir * Random.Range(2f, 5f) + side * Random.Range(-2.2f, 2.2f) + Vector3.up * Random.Range(2f, 4.5f);
                Blob(p, vel, Random.Range(0.1f, 0.2f), PusCol(), Random.Range(0.9f, 1.3f), i < 5);
            }
            for (int i = 0; i < 5; i++)
            {
                float a = Random.value * Mathf.PI * 2, sp = Random.Range(1f, 2.5f);
                Blob(p, new Vector3(Mathf.Cos(a) * sp, Random.Range(1.5f, 3f), Mathf.Sin(a) * sp), Random.Range(0.07f, 0.12f), PusCol(), Random.Range(0.7f, 1f), false);
            }
            Sparks(p, 5, PusBright, 3.5f, 0.4f, 0.06f, 4);
            Splat(new Vector3(p.x, 0, p.z) + dir * (1.1f * Mathf.Sqrt(scale)), Random.Range(2.3f, 2.9f) * Mathf.Sqrt(scale), Color.Lerp(Pus, PusDark, 0.3f), 5f); // out in front, not under the body
        }

        // the Bloater dies: chunky glossy pus blobs arc out and splat, a big puddle that fades over ~6 s
        public void PusBurst(Vector3 center, float scale)
        {
            for (int i = 0; i < 22; i++)
            {
                float a = Random.value * Mathf.PI * 2, sp = Random.Range(1.5f, 5.5f) * Mathf.Sqrt(scale);
                var vel = new Vector3(Mathf.Cos(a) * sp, Random.Range(2.5f, 6.5f), Mathf.Sin(a) * sp);
                Blob(center + Random.insideUnitSphere * 0.3f * scale, vel, Random.Range(0.14f, 0.3f), PusCol(), Random.Range(1.2f, 1.8f), i < 10);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = Random.value * Mathf.PI * 2, sp = Random.Range(2f, 5f);
                Blob(center, new Vector3(Mathf.Cos(a) * sp, Random.Range(3f, 7f), Mathf.Sin(a) * sp), 0.07f, PusBright, Random.Range(0.9f, 1.3f), false);
            }
            Sparks(center, 8, PusBright, 4.5f, 0.5f, 0.07f, 5);
            Splat(new Vector3(center.x, 0, center.z), Random.Range(1.6f, 1.9f) * scale, Color.Lerp(Pus, PusDark, 0.35f), 6f);
        }

        // ---- bullet tracers (pooled soft streak quads, see GunFx)
        public Transform GetTracer(int weapon)
        {
            Transform t;
            if (tracerPool.Count > 0) { t = tracerPool.Pop(); t.gameObject.SetActive(true); }
            else
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                t = go.transform;
            }
            GunFx.I.SetupTracer(t.GetComponent<MeshFilter>(), t.GetComponent<MeshRenderer>(), t, weapon, weapons[weapon].name);
            return t;
        }
        public void ReleaseTracer(Transform t) { t.gameObject.SetActive(false); tracerPool.Push(t); }

        // ---- explosions
        static readonly Color[] FireCols = { new Color(1f, 0.48f, 0.1f), new Color(1f, 0.6f, 0.18f), new Color(1f, 0.72f, 0.29f), new Color(0.88f, 0.27f, 0.11f) };

        (Transform, Material) GetPuff()
        {
            if (puffPool.Count > 0) { var p = puffPool.Pop(); p.Item1.gameObject.SetActive(true); return p; }
            var go = new GameObject("Puff");
            go.AddComponent<MeshFilter>().sharedMesh = puffMesh;
            go.transform.SetParent(transform, false);
            var m = Mats.Glow(Color.white);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return (go.transform, m);
        }

        public void Boom(Vector3 p, float R)
        {
            var b = new BoomFx { p = p, R = R };
            for (int i = 0; i < 9; i++)
            {
                var (tr, m) = GetPuff();
                float a = Random.value * Mathf.PI * 2, d = Random.value * R * 0.3f;
                b.puffs.Add((tr, m, new Vector3(Mathf.Cos(a) * d, 0.5f + Random.value * 1.1f, Mathf.Sin(a) * d), R * (0.16f + Random.value * 0.14f), Random.value * 0.08f, 1f + Random.value * 2f, FireCols[i % FireCols.Length]));
                tr.localScale = Vector3.one * 0.01f;
            }
            var core = GetPuff(); b.core = core.Item1; b.coreMat = core.Item2; b.core.position = p + Vector3.up * 0.9f;
            var rg = new GameObject("Shockwave");
            rg.transform.SetParent(transform, false);
            rg.AddComponent<MeshFilter>().sharedMesh = ring;
            b.ringMat = Mats.Glow(new Color(1f, 0.76f, 0.48f));
            var rmr = rg.AddComponent<MeshRenderer>(); rmr.sharedMaterial = b.ringMat; rmr.shadowCastingMode = ShadowCastingMode.Off;
            b.ring = rg.transform; b.ring.position = p + Vector3.up * 0.06f;
            booms.Add(b);

            var sc = new GameObject("Scorch");
            sc.transform.SetParent(transform, false);
            sc.AddComponent<MeshFilter>().sharedMesh = quad;
            var smr = sc.AddComponent<MeshRenderer>(); smr.sharedMaterial = scorchMat; smr.shadowCastingMode = ShadowCastingMode.Off;
            sc.transform.SetPositionAndRotation(p + Vector3.up * (0.02f + scorches.Count * 0.0005f), Quaternion.Euler(90, Random.value * 360, 0));
            sc.transform.localScale = Vector3.one * R * 1.1f;
            scorches.Add(sc);
            if (scorches.Count > 24) { Destroy(scorches[0]); scorches.RemoveAt(0); }

            Debris(p + Vector3.up * 0.8f, 18, new Color(0.7f, 0.15f, 0.12f), 9, 2.6f, 0.17f, 9);
            Debris(p + Vector3.up * 0.8f, 8, new Color(0.16f, 0.16f, 0.18f), 7, 2.6f, 0.13f, 8);
            Sparks(p + Vector3.up, 26, new Color(1f, 0.75f, 0.29f), 12, 0.45f, 0.09f, 7);
            Smoke(p + Vector3.up * 1.2f, 14, new Color(0.46f, 0.47f, 0.5f), 2.5f, 2.2f, 0.45f, 2);
            boomLight.transform.position = p + Vector3.up * 2; boomLight.intensity = 30f;
        }

        public void ClearAll()
        {
            debris.Clear(); sparks.Clear(); smoke.Clear(); goo.Clear(); splats.Clear();
            GunFx.I.ClearAll();
            pendingSplats.Clear();
            foreach (var s in scorches) Destroy(s);
            scorches.Clear();
            foreach (var b in booms) FinishBoom(b);
            booms.Clear();
            boomLight.intensity = 0;
        }

        void FinishBoom(BoomFx b)
        {
            foreach (var p in b.puffs) { p.tr.gameObject.SetActive(false); puffPool.Push((p.tr, p.m)); }
            b.core.gameObject.SetActive(false); puffPool.Push((b.core, b.coreMat));
            Destroy(b.ring.gameObject); Destroy(b.ringMat);
        }

        static void SetCol(Material m, Color c, float a) { c.a = Mathf.Clamp01(a); m.SetColor("_BaseColor", c); }

        public void Tick(float dt)
        {
            for (int i = pendingSplats.Count - 1; i >= 0; i--)
            {
                var s = pendingSplats[i]; s.t -= dt;
                if (s.t > 0) { pendingSplats[i] = s; continue; }
                Splat(s.p, s.size, s.c);
                if (!s.quiet && CombatAudio.I != null) CombatAudio.I.Drip(); // droplet blip as the blob lands
                pendingSplats[i] = pendingSplats[^1]; pendingSplats.RemoveAt(pendingSplats.Count - 1);
            }
            for (int i = booms.Count - 1; i >= 0; i--)
            {
                var b = booms[i]; b.t += dt;
                float t = b.t, R = b.R;
                foreach (var p in b.puffs)
                {
                    float lt = t - p.delay;
                    if (lt < 0) continue;
                    float k = 1 - Mathf.Pow(1 - Mathf.Min(1, lt / 0.25f), 3);
                    p.tr.position = b.p + new Vector3(p.off.x * (1 + k), p.off.y + lt * p.rise, p.off.z * (1 + k));
                    p.tr.localScale = Vector3.one * Mathf.Max(0.01f, p.s * (0.4f + k) * 2f);
                    SetCol(p.m, p.c, 0.9f * (1 - lt / 0.5f));
                }
                b.core.localScale = Vector3.one * Mathf.Max(0.01f, R * 0.6f * (1 - t / 0.25f));
                SetCol(b.coreMat, new Color(1f, 0.94f, 0.69f), 1 - t / 0.25f);
                b.ring.localScale = Vector3.one * R * Mathf.Min(1, t / 0.25f) * 1.05f;
                SetCol(b.ringMat, new Color(1f, 0.76f, 0.48f), 0.85f * (1 - t / 0.45f));
                if (t > 0.65f) { FinishBoom(b); booms.RemoveAt(i); }
            }
            boomLight.intensity *= Mathf.Exp(-9 * dt);
        }
    }
}
