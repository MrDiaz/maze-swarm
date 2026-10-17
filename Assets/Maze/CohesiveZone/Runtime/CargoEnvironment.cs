using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Modular floor and low boundaries follow the same authoritative grid as collision/pathing.
    // Detailed cargo models are local-pipeline deliveries, never procedural substitutes.
    public sealed class CargoEnvironment : MonoBehaviour
    {
        readonly List<Object> owned = new();
        readonly Dictionary<string, Mesh> meshes = new();
        readonly Dictionary<string, Material> materials = new();
        readonly Dictionary<Material, Material> pipelineMaterials = new();
        public int pipelineProps;
        Transform modular;
        static Light originalSun;
        static Quaternion originalSunRotation;
        static float originalShadowStrength;
        static readonly Color Blue = new Color32(68, 158, 227, 255);

        public static void Build(Transform parent)
        {
            var env = parent.gameObject.AddComponent<CargoEnvironment>();
            env.Make();
        }
        Material Mat(string name, Color color, bool unlit = false)
        {
            if (materials.TryGetValue(name, out var m)) return m;
            m = unlit ? Mats.Unlit(color) : Mats.Lit(color, null, null, .23f);
            m.name = "Cargo " + name; m.enableInstancing = true;
            owned.Add(m); materials.Add(name, m); return m;
        }
        static Color Hex(uint value) => new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
        void Make()
        {
            float w = MazeMap.W * MazeMap.C, d = MazeMap.H * MazeMap.C;
            var navy = Mat("navy", Hex(0x273d65)); var cream = Mat("cream", Hex(0xf0d9bd));
            var orange = Mat("orange", Hex(0xf58626)); var floor = Mat("floor", Hex(0x415c87));
            Box("Quiet blue surroundings", new Vector3(w / 2, -.5f, -d / 2), new Vector3(600, .2f, 600), Mat("sky blue", Blue, true), .02f, false);
            Box("Deck foundation", new Vector3(w / 2, -.20f, -d / 2), new Vector3(w, .35f, d), navy, .12f);
            // One two-metre square per cell keeps the visual scale tied to the existing map.
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++)
            {
                var c = MazeMap.grid[z, x];
                if (c == '_' || c == '#') continue;
                var m = ((x * 17 + z * 11) % 13 == 0) ? Mat("floor alternate", Hex(0x435f8b)) : floor;
                Box("Deck tile", MazeMap.ToWorld(MazeMap.Center(x, z), -.025f), new Vector3(1.974f, .12f, 1.974f), m, .065f, false);
            }
            foreach (var rect in MazeMap.Rects(c => c == '#')) Boundary(rect, navy, cream, orange);
            int index = 0;
            foreach (var rect in MazeMap.Rects(c => c == 'B' || c == 'C' || c == 'r' || c == 'm'))
            {
                var size = new Vector3(rect.width * MazeMap.C, .20f, rect.height * MazeMap.C);
                var center = MazeMap.ToWorld(new Vector2((rect.x + rect.width * .5f) * MazeMap.C, (rect.y + rect.height * .5f) * MazeMap.C));
                Box("Reserved cargo footprint", center + Vector3.up * .1f, size, navy, .1f);
                Dress(rect, center, index++, cream, orange);
            }
            if (modular != null) StaticBatchingUtility.Combine(modular.gameObject);
            Debug.Log($"Cargo presentation: {MazeMap.LayoutName(MazeMap.MapIndex)}, {pipelineProps} local-pipeline props; authoritative grid {MazeMap.W}x{MazeMap.H}");
        }
        void Boundary(RectInt rect, Material navy, Material cream, Material orange)
        {
            // Boundary is deliberately below hero shoulder height so rear-lane actors stay visible.
            for (int z = rect.y; z < rect.yMax; z++) for (int x = rect.x; x < rect.xMax; x++)
            {
                var p = MazeMap.ToWorld(MazeMap.Center(x, z));
                Box("Boundary foot", p + Vector3.up * .10f, new Vector3(2, .2f, 2), navy, .12f);
                Box("Boundary block", p + Vector3.up * .61f, new Vector3(1.97f, 1.02f, 1.97f), ((x + z) % 4 == 0 ? cream : orange), .17f);
            }
        }
        void Dress(RectInt rect, Vector3 center, int index, Material cream, Material orange)
        {
            var paths = new[] { "cargo-crate", "cargo-battery", "cargo-tank", "cargo-barrier", "cargo-conveyor" };
            // First prop fills the entire reserved footprint. Additional units stay inside that footprint.
            int nx = Mathf.Max(1, rect.width), nz = Mathf.Max(1, rect.height);
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                var p = center + new Vector3((x - (nx - 1) * .5f) * 2, .2f, -(z - (nz - 1) * .5f) * 2);
                string path = paths[(index + x + z * 2) % paths.Length];
                var prefab = Resources.Load<GameObject>("CohesiveZone/Models/" + path);
                if (prefab == null) prefab = Resources.Load<GameObject>("CohesiveZone/Models/cargo-crate");
                if (prefab == null) continue; // Missing delivery is reported by the build validator; no disguised detailed fallback.
                var go = Instantiate(prefab, transform, false); go.name = "Local pipeline " + path;
                go.transform.localPosition = Vector3.zero;
                Mats.ConvertGltf(go);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var source = r.sharedMaterials;
                    var shared = new Material[source.Length];
                    for (int i = 0; i < source.Length; i++)
                    {
                        if (source[i] == null) continue;
                        if (!pipelineMaterials.TryGetValue(source[i], out var material))
                        {
                            material = new Material(source[i]) { name = "Cargo pipeline " + source[i].name, enableInstancing = true };
                            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
                            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .25f);
                            pipelineMaterials.Add(source[i], material); owned.Add(material);
                        }
                        shared[i] = material;
                    }
                    r.sharedMaterials = shared;
                }
                var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { Destroy(go); continue; }
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float height = path == "cargo-tank" ? 1.25f : path == "cargo-barrier" ? .9f : 1.05f;
                float s = Mathf.Min(1.85f / Mathf.Max(.01f, b.size.x), 1.85f / Mathf.Max(.01f, b.size.z), height / Mathf.Max(.01f, b.size.y));
                go.transform.localScale *= s;
                go.transform.localPosition = p + new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
                pipelineProps++;
            }
        }
        public static void Lighting(Light sun, Camera camera)
        {
            Biome.Clear();
            if (originalSun == null && sun != null) { originalSun = sun; originalSunRotation = sun.transform.rotation; originalShadowStrength = sun.shadowStrength; }
            if (sun != null) { sun.color = new Color(1f, .94f, .85f); sun.intensity = 1.15f; sun.transform.rotation = Quaternion.Euler(52, -32, 0); sun.shadowStrength = .75f; }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.62f, .70f, .88f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .57f, .75f);
            RenderSettings.ambientGroundColor = new Color(.30f, .38f, .55f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Blue; RenderSettings.fogStartDistance = 120; RenderSettings.fogEndDistance = 180;
            if (camera != null) camera.backgroundColor = Blue;
        }
        public static void RestoreLighting()
        {
            if (originalSun != null) { originalSun.transform.rotation = originalSunRotation; originalSun.shadowStrength = originalShadowStrength; }
            originalSun = null;
        }
        public static void TractionPlates(Transform root, List<Vector2Int> cells)
        {
            var go = new GameObject("Cargo slick service plates"); go.transform.SetParent(root, false);
            var owner = go.AddComponent<CargoEnvironment>();
            var m = owner.Mat("slick cyan", Hex(0x55b8d0));
            var stripe = owner.Mat("traction marking", Hex(0xb7f0e9));
            foreach (var cell in cells)
            {
                var p = MazeMap.ToWorld(MazeMap.Center(cell.x, cell.y), .07f);
                owner.Box("Slick plate", p, new Vector3(1.91f, .045f, 1.91f), m, .08f, false);
                for (int i = -1; i <= 1; i++) owner.Box("Traction stripe", p + new Vector3(i * .45f, .028f, 0), new Vector3(.055f, .012f, .78f), stripe, .006f, false);
            }
            if (owner.modular != null) StaticBatchingUtility.Combine(owner.modular.gameObject);
        }
        void Box(string label, Vector3 center, Vector3 dimensions, Material material, float radius, bool cast = true)
        {
            string key = dimensions.ToString("F3") + radius.ToString("F3");
            if (!meshes.TryGetValue(key, out var mesh)) { mesh = Rounded(dimensions, radius); meshes.Add(key, mesh); owned.Add(mesh); }
            if (modular == null) { modular = new GameObject("Cargo modular deck and boundary").transform; modular.SetParent(transform, false); }
            var go = new GameObject(label); go.transform.SetParent(modular, false); go.transform.localPosition = center;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
        }
        static Mesh Rounded(Vector3 size, float radius)
        {
            radius = Mathf.Min(radius, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .49f);
            var half = size * .5f; var inner = half - Vector3.one * radius;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            // Samples cluster at the bevel edges rather than rounding a box into a ball.
            foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                var u = Mathf.Abs(n.y) > .5f ? Vector3.right : Vector3.up; var v = Vector3.Cross(n, u);
                int start = vertices.Count;
                float hu = Vector3.Dot(half, new Vector3(Mathf.Abs(u.x), Mathf.Abs(u.y), Mathf.Abs(u.z)));
                float hv = Vector3.Dot(half, new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z)));
                float[] us = { -hu, -hu + radius * .3f, -hu + radius, hu - radius, hu - radius * .3f, hu };
                float[] vs = { -hv, -hv + radius * .3f, -hv + radius, hv - radius, hv - radius * .3f, hv };
                for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++)
                {
                    var p = Vector3.Scale(n, half) + u * us[x] + v * vs[y];
                    var q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    var normal = (p - q).normalized; vertices.Add(q + normal * radius); normals.Add(normal); uv.Add(new Vector2(x / 5f, y / 5f));
                }
                for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) { int a = start + y * 6 + x; triangles.AddRange(new[] { a, a + 1, a + 7, a, a + 7, a + 6 }); }
            }
            var mesh = new Mesh { name = "Cargo modular rounded box" }; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); return mesh;
        }
        void OnDestroy() { foreach (var item in owned) if (item != null) Destroy(item); }
    }
}
