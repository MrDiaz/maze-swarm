using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.EditorTools
{
    // Gunfire kit (approved chibi art direction, 2026-10-14): bakes the flat stylized textures procedurally, then the
    // materials, tracer meshes and particle prefabs the runtime GunfireKit loads from Resources/Gunfire.
    //   Textures  gf_flash.png   512x256  top row: camera-facing stars (cols 0-1 rifle, 2-3 minigun);
    //                                     bottom row: flat flames lying along the barrel (+u = forward), same columns
    //             gf_tracer.png  128x256  col 0 rifle (two short dashes), col 1 minigun (one thin dash); head at v = 1
    //             gf_spark.png   32x128   a teardrop streak, head at v = 1 (stretched billboards)
    //             gf_puff.png    128x128  a two-tone cream puff (impacts)
    //   Palette   core #fff3b0, mid #ffd23f, rim #ff9a1f (rifle); warmer for the minigun. Crisp edges, a faint warm halo,
    //             all values <= 1: no bloom outside the Cryo room, at most a slight glow inside it (threshold 1.7).
    //   Unity -batchmode -quit -projectPath . -executeMethod Maze.EditorTools.GunfireBuilder.Build
    public static class GunfireBuilder
    {
        const string Root = "Assets/Maze/Gunfire/Resources/Gunfire";
        static readonly Color Core = Hex("#fff3b0"), Mid = Hex("#ffd23f"), Rim = Hex("#ff9a1f");
        static readonly Color HCore = Hex("#ffe9a6"), HMid = Hex("#ffc23a"), HRim = Hex("#ff861a");
        static readonly Color Hot = Hex("#fffdf2");

        [MenuItem("Maze/Gunfire/Rebuild gunfire kit")]
        public static void Build()
        {
            foreach (var d in new[] { "Textures", "Materials", "Meshes", "Prefabs" }) Directory.CreateDirectory(Path.Combine(Root, d));
            var flash = SaveTex("gf_flash", FlashSheet(), 512);
            var tracer = SaveTex("gf_tracer", TracerTex(), 256);
            var spark = SaveTex("gf_spark", SparkTex(), 128);
            var puff = SaveTex("gf_puff", PuffTex(), 128);
            var glow = SaveTex("gf_glow", GlowTex(), 128);

            var mFlash = SaveMat("GF_Flash", "Universal Render Pipeline/Particles/Unlit", flash);
            var mSpark = SaveMat("GF_Spark", "Universal Render Pipeline/Particles/Unlit", spark);
            var mPuff = SaveMat("GF_Puff", "Universal Render Pipeline/Particles/Unlit", puff);
            var mGlow = SaveMat("GF_Glow", "Universal Render Pipeline/Particles/Unlit", glow);
            var mTracer = SaveMat("GF_Tracer", "Universal Render Pipeline/Unlit", tracer);

            var rifleMesh = SaveMesh("GF_TracerRifle", 0); var miniMesh = SaveMesh("GF_TracerMinigun", 1);

            Particles("GF_RifleStar", mFlash, 8, ParticleSystemRenderMode.Billboard, 0, 0, 2, 0);
            Particles("GF_RifleFlame", mFlash, 8, ParticleSystemRenderMode.HorizontalBillboard, 1, 0, 2, 0);
            Particles("GF_MinigunStar", mFlash, 8, ParticleSystemRenderMode.Billboard, 0, 2, 4, 0);
            Particles("GF_MinigunFlame", mFlash, 8, ParticleSystemRenderMode.HorizontalBillboard, 1, 2, 4, 0);
            Particles("GF_ImpactStar", mFlash, 16, ParticleSystemRenderMode.Billboard, 0, 1, 2, 0);
            Particles("GF_ImpactSparks", mSpark, 96, ParticleSystemRenderMode.Stretch, -1, 0, 0, 0.6f);
            Particles("GF_ImpactPuff", mPuff, 16, ParticleSystemRenderMode.Billboard, -1, 0, 0, -0.05f);
            Particles("GF_FlashGlow", mGlow, 8, ParticleSystemRenderMode.Billboard, -1, 0, 0, 0);
            TracerPrefab(mTracer, rifleMesh);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"GunfireBuilder: kit written to {Root} (4 textures, 4 materials, 2 meshes, 8 prefabs)");
        }

        // ---------------------------------------------------------------- painting
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static float S(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        // inside colour from t (0 centre .. 1 edge): a broad warm-ivory core, yellow body, an orange rim band
        static Color Ramp(float t, bool heavy)
        {
            Color c0 = heavy ? HCore : Core, c1 = heavy ? HMid : Mid, c2 = heavy ? HRim : Rim;
            var c = Color.Lerp(Hot, c0, S(0.06f, 0.2f, t));   // v2: a white-hot pinpoint at the very centre
            c = Color.Lerp(c, c1, S(0.22f, 0.48f, t));   // a small ivory centre, mostly saturated yellow
            return Color.Lerp(c, c2, S(0.74f, 0.94f, t));
        }
        // composite: a shape pixel (edge distance in px, inside t) over a faint halo in the rim colour
        static Color Px(float edgePx, float t, bool heavy, float halo = 0.2f, float haloPx = 6f)
        {
            float a = Mathf.Clamp01(edgePx / 1.4f + 0.5f);
            var rim = heavy ? HRim : Rim;
            float ha = edgePx < 0 ? halo * Mathf.Exp(edgePx / haloPx) : 0;
            var c = Ramp(Mathf.Clamp01(t), heavy);
            float outA = a + ha * (1 - a);
            if (outA <= 0.0001f) return new Color(rim.r, rim.g, rim.b, 0);
            var col = (c * a + rim * ha * (1 - a)) / outA; col.a = outA; return col;
        }
        static void Put(Color[] px, int w, int x, int y, Color c) { var o = px[y * w + x]; float a = c.a + o.a * (1 - c.a); if (a <= 0) return; var r = (c * c.a + o * o.a * (1 - c.a)) / a; r.a = a; px[y * w + x] = r; }

        // rounded star: n lobes, inner radius ratio, tip sharpness, rotation
        static void Star(Color[] px, int W, int ox, int oy, int N, int n, float inner, float sharp, float rot, float R, bool heavy, float squashY = 1f)
        {
            float c = N * 0.5f;
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - c, dy = (y + 0.5f - c) / squashY, r = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx) + rot;
                float rr = R * N * (inner + (1 - inner) * Mathf.Pow(Mathf.Abs(Mathf.Cos(n * th * 0.5f)), sharp));
                Put(px, W, ox + x, oy + y, Px(rr - r, r / rr, heavy, 0.12f, 5f));
            }
        }
        // flat flame lying along +x: petals (base x, angle, length, width) in cell units (x 0..1, y -0.5..0.5)
        static void Flame(Color[] px, int W, int ox, int oy, int N, (float bx, float a, float L, float Wd)[] petals, bool heavy)
        {
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N - 0.5f;
                float best = 9f, bestEdge = -99f, bestU = 1;
                foreach (var p in petals)
                {
                    float qx = (u - p.bx) * Mathf.Cos(-p.a) - v * Mathf.Sin(-p.a), qy = (u - p.bx) * Mathf.Sin(-p.a) + v * Mathf.Cos(-p.a);
                    float s = qx / p.L; if (s < -0.02f || s > 1.02f) { continue; }
                    float sc = Mathf.Clamp01(s);
                    float w = p.Wd * 2.5f * Mathf.Pow(sc, 0.5f) * Mathf.Pow(1 - sc, 0.9f) + 0.004f;
                    float t = Mathf.Abs(qy) / w, edge = (w - Mathf.Abs(qy)) * N;
                    if (s < 0 || s > 1) edge = Mathf.Min(edge, -Mathf.Abs(s < 0 ? s : s - 1) * p.L * N);
                    if (edge > bestEdge) { bestEdge = edge; best = t; bestU = sc; }
                }
                if (bestEdge < -40) continue;
                float tc = 1 - (1 - best) * (1 - 0.85f * Mathf.Pow(bestU, 1.6f));   // hot at the muzzle and the petal's middle, orange toward the tips
                Put(px, W, ox + x, oy + y, Px(bestEdge, tc, heavy, 0.1f, 4f));
            }
        }
        static Color[] FlashSheet()
        {
            int W = 512, H = 256, N = 128; var px = new Color[W * H];
            // top row (v high = grid row 0): stars
            Star(px, W, 0, 128, N, 5, 0.46f, 2.2f, 0.31f, 0.40f, false);
            Star(px, W, 128, 128, N, 4, 0.40f, 2.6f, 0.785f, 0.42f, false);
            Star(px, W, 256, 128, N, 4, 0.44f, 2.4f, 0.2f, 0.38f, true);
            Star(px, W, 384, 128, N, 6, 0.52f, 2.0f, 0.0f, 0.36f, true);
            // bottom row (grid row 1): flames along +x (the muzzle at x = 0)
            // a three-prong forward burst: the long centre tongue, two shorter tongues splaying forward from part-way along
            Flame(px, W, 0, 0, N, new[] { (0.02f, 0f, 0.95f, 0.17f), (0.16f, 0.36f, 0.52f, 0.10f), (0.16f, -0.36f, 0.52f, 0.10f) }, false);
            Flame(px, W, 128, 0, N, new[] { (0.02f, 0f, 0.85f, 0.19f), (0.20f, 0.28f, 0.58f, 0.09f), (0.20f, -0.28f, 0.58f, 0.09f) }, false);
            Flame(px, W, 256, 0, N, new[] { (0.02f, 0f, 0.66f, 0.22f), (0.14f, 0.46f, 0.40f, 0.11f), (0.14f, -0.46f, 0.40f, 0.11f) }, true);
            Flame(px, W, 384, 0, N, new[] { (0.02f, 0f, 0.56f, 0.24f), (0.12f, 0.30f, 0.46f, 0.10f), (0.12f, -0.30f, 0.46f, 0.10f), (0.10f, 0.75f, 0.28f, 0.09f), (0.10f, -0.75f, 0.28f, 0.09f) }, true);
            return px;
        }
        static Color[] TracerTex()
        {
            int W = 128, H = 256, CW = 64; var px = new Color[W * H];
            for (int col = 0; col < 2; col++)
            {
                bool heavy = col == 1;
                var dashes = heavy ? new[] { (0.30f, 0.98f, 10f, 1f) } : new[] { (0.50f, 0.98f, 10f, 1f), (0.10f, 0.38f, 8f, 0.8f) };   // v2: the quad is wider (room for the head glow), the core keeps its world width
                for (int y = 0; y < H; y++) for (int x = 0; x < CW; x++)
                {
                    float fx = x + 0.5f - CW * 0.5f, fy = y + 0.5f;
                    // v2: a faint soft glow around the head (no hard edge; fades out well inside the quad)
                    float gx = fx / 17f, gy = (fy - 0.93f * H) / 30f, g = 0.42f * Mathf.Exp(-(gx * gx + gy * gy) * 1.6f);
                    if (g > 0.003f) { var gc = heavy ? HMid : Mid; gc.a = g; Put(px, W, col * CW + x, y, gc); }
                    foreach (var (v0, v1, r0, alpha) in dashes)
                    {
                        float y0 = v0 * H, y1 = v1 * H, s = Mathf.Clamp01((fy - y0) / (y1 - y0));
                        float r = r0 * (0.5f + 0.5f * s);
                        float cy = Mathf.Clamp(fy, y0 + r, y1 - r), d = Mathf.Sqrt(fx * fx + (fy - cy) * (fy - cy));
                        float t = Mathf.Lerp(0.98f, 0.05f, Mathf.Pow(s, 0.8f)) * 0.75f + 0.25f * Mathf.Clamp01(d / r);   // head ivory, tail orange
                        var c = Px(r - d, t, heavy, 0.28f, 4f); c.a *= alpha;
                        Put(px, W, col * CW + x, y, c);
                    }
                }
            }
            return px;
        }
        static Color[] SparkTex()
        {
            int W = 32, H = 128; var px = new Color[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f - W * 0.5f, s = (y + 0.5f) / H;   // s: 0 tail .. 1 head
                float r = 9f * Mathf.Pow(Mathf.Clamp01(s), 1.3f) * (1 - S(0.9f, 1f, s) * 0.6f) + 0.5f;
                float d = Mathf.Abs(fx); if (s > 0.94f) d = Mathf.Sqrt(fx * fx + ((s - 0.94f) * H) * ((s - 0.94f) * H));
                Put(px, W, x, y, Px(r - d, Mathf.Lerp(1f, 0.1f, s), false, 0.15f, 3f));
            }
            return px;
        }
        // v2: the soft glow behind a flash: a white-hot centre into warm yellow, then a gaussian falloff to nothing
        static Color[] GlowTex()
        {
            int N = 128; var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1, d = Mathf.Sqrt(u * u + v * v);
                var c = Color.Lerp(Hot, Mid, S(0.06f, 0.3f, d)); c = Color.Lerp(c, Rim, S(0.35f, 0.8f, d));
                c.a = Mathf.Clamp01(0.95f * Mathf.Exp(-d * d * 5.5f) * (1 - S(0.85f, 1f, d)));
                px[y * N + x] = c;
            }
            return px;
        }
        static Color[] PuffTex()
        {
            int N = 128; var px = new Color[N * N];
            var blobs = new[] { (0.50f, 0.44f, 0.29f), (0.31f, 0.50f, 0.19f), (0.69f, 0.52f, 0.20f), (0.52f, 0.66f, 0.20f) };
            Color lit = Hex("#fff6e2"), shade = Hex("#ead9bd");
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N, best = -9; float nx = 0, ny = 0;
                foreach (var (cx, cy, r) in blobs) { float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)); if (r - d > best) { best = r - d; nx = (u - cx) / r; ny = (v - cy) / r; } }
                float edge = best * N, a = Mathf.Clamp01(edge / 1.4f + 0.5f);
                if (a <= 0) continue;
                float light = (v - 0.42f) * 2.2f - (u - 0.5f) * 0.8f;   // one soft top-left light over the whole puff (no seams between lobes)
                var c = Color.Lerp(shade, lit, S(-0.35f, 0.25f, light)); c.a = a * 0.92f;
                px[y * N + x] = c;
            }
            return px;
        }

        // ---------------------------------------------------------------- assets
        static Texture2D SaveTex(string name, Color[] px, int max)
        {
            int n = px.Length, w = name == "gf_flash" ? 512 : name == "gf_tracer" ? 128 : name == "gf_spark" ? 32 : 128, h = n / w;   // (gf_puff, gf_glow: 128)
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
            string path = $"{Root}/Textures/{name}.png"; File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default; imp.alphaIsTransparency = true; imp.sRGBTexture = true; imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp; imp.filterMode = FilterMode.Bilinear; imp.maxTextureSize = max; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material SaveMat(string name, string shader, Texture tex)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader);
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }
        // flat quad in XZ, length along +Z (head at +Z), uv column `col` of gf_tracer.png
        static Mesh SaveMesh(string name, int col)
        {
            string path = $"{Root}/Meshes/{name}.asset";
            float u0 = col * 0.5f + 0.004f, u1 = (col + 1) * 0.5f - 0.004f;
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) };
            m.uv = new[] { new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u0, 1), new Vector2(u1, 1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(m, path); return m;
        }
        static void TracerPrefab(Material mat, Mesh mesh)
        {
            var go = new GameObject("GF_Tracer");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/GF_Tracer.prefab"); Object.DestroyImmediate(go);
        }
        // a pooled emitter: no emission or shape (fed by Emit), bounded particles, a fade, an optional flipbook row
        static void Particles(string name, Material mat, int max, ParticleSystemRenderMode mode, int row, int f0, int f1, float gravity)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.maxParticles = max; main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = 0.1f; main.startSpeed = 0;
            if (mode == ParticleSystemRenderMode.HorizontalBillboard) main.startSize3D = true;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = mode; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (mode == ParticleSystemRenderMode.Stretch) { r.lengthScale = 1.5f; r.velocityScale = 0.03f; }
            if (name == "GF_FlashGlow") r.sortingFudge = -10;   // behind the flash, in front of puffs
            if (row >= 0)
            {
                r.sortingFudge = -20;   // flashes in front of puffs
                var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 4; ts.numTilesY = 2;
                ts.animation = ParticleSystemAnimationType.SingleRow; ts.rowMode = ParticleSystemAnimationRowMode.Custom; ts.rowIndex = row;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(0); ts.startFrame = new ParticleSystem.MinMaxCurve(f0, f1 - 0.001f);
            }
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            float hold = row >= 0 || name == "GF_FlashGlow" ? 0.8f : name.Contains("Puff") ? 0.35f : 0.5f;
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, hold), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = name == "GF_FlashGlow" ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.8f), new Keyframe(0.3f, 1.05f), new Keyframe(1, 0.6f)))
                    : name.Contains("Puff") ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.55f, 0, 2.2f), new Keyframe(1, 1f, 0.2f, 0)))
                    : row >= 0 ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.75f), new Keyframe(0.3f, 1.05f), new Keyframe(0.7f, 1f), new Keyframe(1, 0.35f)))   // shrinks as it fades: no half-faded yellow going grey over the blue floor
                    : new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.4f)));
            PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{name}.prefab"); Object.DestroyImmediate(go);
        }
    }
}
