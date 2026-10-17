using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze
{
    [Serializable]
    public class CryoKitPlacement
    {
        public string id;
        public GameObject instance;
        public Rect allowedXZ;
        public bool mounted;
        public int triangles;
    }

    // Art-only lifetime owner. The existing MazeMap grid remains the collision authority.
    public class CryoKitSceneInfo : MonoBehaviour
    {
        public int nativeTriangles, nativeInstances;
        public List<string> usedIds = new();
        public List<CryoKitPlacement> placements = new();
        public readonly List<Material> ownedMaterials = new();
        void OnDestroy() { foreach (var m in ownedMaterials) if (m != null) Destroy(m); }
    }

    /// <summary>Scoped Cryo Station visual kit; no routes, doors, hazards or gameplay RNG changes.</summary>
    public sealed class CryoKitEnvironment
    {
        public static bool On = true;
        const int TriangleBudget = 280000, InstanceBudget = 72;
        static readonly string[] Slugs = {
            "wall_module", "inside_corner", "support_pillar", "floor_panel", "floor_trench_grate",
            "sliding_bulkhead", "hero_pod_cradle", "cryo_service_cabinet", "cooling_reservoir_pumps",
            "coolant_manifold_elbow", "overhead_cable_tray", "vent_fan_unit", "research_workstation",
            "wall_control_terminal", "low_cover_cabinet", "cargo_crate", "coolant_canister",
            "ceiling_light_fixture", "emergency_beacon", "fractured_cryo_breach" };
        readonly Transform root;
        readonly CryoKitSceneInfo info;
        readonly LevelBuilder builder;
        readonly Material paint, metal, guard;
        readonly List<Rect> walls = new();
        readonly CryoBoundaryTreatment boundaryProof;
        int buildings, crates;

        public static bool For(bool run, SectorDef sector) => On && run && sector?.id == "cryo-station" &&
            MazeMap.V2 && MazeMap.LayoutName(MazeMap.MapIndex).StartsWith("cryo-station/", StringComparison.Ordinal) &&
            Resources.Load<GameObject>("CryoKit/Prefabs/07_hero_pod_cradle") != null;

        public CryoKitEnvironment(Transform level, LevelBuilder builder)
        {
            this.builder = builder;
            root = new GameObject("Cryo Station — native art kit").transform;
            root.SetParent(level, false); info = root.gameObject.AddComponent<CryoKitSceneInfo>();
            var materials = Resources.Load<GameObject>("CryoKit/Prefabs/01_wall_module")
                .GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            paint = Clone(materials.First(m => m.name.Contains("Ceramic_Enamel")), "Cryo structural enamel");
            metal = Clone(materials.First(m => m.name.Contains("Brushed_Steel")), "Cryo service deck steel");
            guard = Clone(materials.First(m => m.name.Contains("Anodized_Frame")), "Cryo island housing");
            if (CryoBoundaryPreview.Active) boundaryProof = new CryoBoundaryTreatment(root, builder, info, paint, metal, guard);
        }
        Material Clone(Material source, string name)
        {
            var m = new Material(source) { name = name };
            // Merged backing has metric UV0, not the native mesh-specific AO atlas in UV1.
            m.SetFloat("_UseAOMap", 0); info.ownedMaterials.Add(m); return m;
        }
        static Rect World(RectInt r) => new(r.x * MazeMap.C, -(r.y + r.height) * MazeMap.C,
            r.width * MazeMap.C, r.height * MazeMap.C);
        static Vector3 At(Rect r, float x = 0, float z = 0, float y = .12f) => new(r.center.x + x, y, r.center.y + z);
        void Box(Rect r, float y, float height, Material m, float inset = 0)
        {
            builder.Box(At(r, 0, 0, y), new Vector3(Mathf.Max(.1f, r.width - inset * 2), height,
                Mathf.Max(.1f, r.height - inset * 2)), m);
        }
        void RestrictedIsland(Rect r, bool low)
        {
            Box(r, .055f, .11f, metal);
            // Continuous low housing makes the whole existing solid grid footprint legible.
            float h = low ? .56f : .72f, t = .14f;
            builder.Box(At(r, 0, -r.height / 2 + t / 2, h / 2), new Vector3(r.width, h, t), guard);
            builder.Box(At(r, 0, r.height / 2 - t / 2, h / 2), new Vector3(r.width, h, t), guard);
            builder.Box(At(r, -r.width / 2 + t / 2, 0, h / 2), new Vector3(t, h, r.height), guard);
            builder.Box(At(r, r.width / 2 - t / 2, 0, h / 2), new Vector3(t, h, r.height), guard);
            // Cap rails and seam posts reuse the kit's metal finish; all remain inside the grid.
            foreach (float z in new[] { -r.height / 2 + .08f, r.height / 2 - .08f })
                builder.Box(At(r, 0, z, h + .03f), new Vector3(r.width, .06f, .16f), metal);
        }
        public bool TryBuildObstacle(char ch, RectInt grid)
        {
            var r = World(grid);
            if (CryoMachineryPreview.Fits(ch, r)) { CryoMachineryPreview.Build(root, r); return true; }
            if (ch == '#')
            {
                walls.Add(r);
                if (boundaryProof != null && boundaryProof.Build(r)) return true;
                Box(r, 1.78f, 3.56f, paint, .20f);
                Box(r, .12f, .24f, guard, .03f); return true;
            }
            if (ch != 'B' && ch != 'C') return false;
            RestrictedIsland(r, ch == 'C');
            if (ch == 'C') { DressCargo(r, crates++); return true; }
            string layout = MazeMap.LayoutName(MazeMap.MapIndex);
            if (layout.EndsWith("cryo-a", StringComparison.Ordinal)) DressA(r);
            else DressLab(r, buildings++);
            return true;
        }
        void DressA(Rect r)
        {
            if (r.width > 5)
            {
                Place(4, r, At(r, -.8f, -.55f), 0, true);
                Place(5, r, At(r, 1.6f, .85f), 0, true);
                Place(7, r, At(r, .60f, -.45f), 180);
                Place(10, r, At(r, -2.0f, .55f), 180);
                Place(8, r, At(r, 2.35f, 1.42f), 180);
            }
            else if (r.center.x > 20)
            {
                Place(9, r, At(r, 0, .28f), 180);
                Place(17, r, At(r, -1.45f, -1.43f), 0);
                Place(3, r, At(r, -1.58f, .0f), 0);
                Place(3, r, At(r, 1.58f, .0f), 0);
                Place(11, r, At(r, 0, .0f, 3.92f), 0, true);
                Place(18, r, At(r, 0, .08f, 3.70f), 0, true);
            }
            else
            {
                Place(13, r, At(r, -.75f, -1.24f), 180);
                Place(15, r, At(r, .0f, 1.35f), 180);
                Place(20, r, At(r, .78f, -.18f), 180);
                Place(8, r, At(r, -1.20f, .60f), 180);
            }
        }
        void DressLab(Rect r, int index)
        {
            if (r.width >= 6 && index % 2 == 0)
            {
                Place(7, r, At(r, -1.5f, -.2f), 180);
                Place(9, r, At(r, 1.65f, .3f), 180);
            }
            else if (r.width >= 6)
            {
                Place(13, r, At(r, -2.6f, -1.25f), 180);
                Place(15, r, At(r, 0, -.9f), 180);
                Place(8, r, At(r, 2.65f, .7f), 180);
                Place(16, r, At(r, -2.4f, 1.1f), 0);
            }
            else if (index % 3 == 0) Place(7, r, At(r, 0, -.25f), 180);
            else if (index % 3 == 1) Place(9, r, At(r, 0, .2f), 180);
            else { Place(13, r, At(r, -.8f, -1.2f), 180); Place(15, r, At(r, 0, 1.1f), 180); Place(20, r, At(r, .65f, -.1f), 180); }
        }
        void DressCargo(Rect r, int index)
        {
            Place(16, r, At(r, -Mathf.Min(.8f, r.width / 2 - .65f), -.65f), 0);
            Place(16, r, At(r, Mathf.Min(.8f, r.width / 2 - .65f), .65f), 180);
            if (r.width >= 3.8f && index == 0) Place(15, r, At(r, .1f, .0f), 180);
        }
        public void Complete()
        {
            // A sealed service facade, never a gameplay door. Cryo layouts have no D cells.
            Rect north = walls.FirstOrDefault(r => r.yMax > -.01f && r.width >= 8);
            if (boundaryProof == null && north.width > 0)
            {
                Place(6, north, new Vector3(MazeMap.W * MazeMap.C * .52f, 0, -1.60f), 180, true);
                Place(12, north, new Vector3(13, 2.10f, -1.67f), 180, true);
                Place(14, north, new Vector3(27, 1.65f, -1.80f), 180, true);
                Place(19, north, new Vector3(20.9f, 2.65f, -1.83f), 180, true);
            }
            Rect corner = new(0, -2, 2, 2);
            if (boundaryProof == null) Place(2, corner, new Vector3(1, 0, -1), 90, true);
            var candidates = new List<(Rect r, Vector3 p, float yaw, float priority)>();
            foreach (var r in walls)
            {
                if (boundaryProof != null && CryoBoundaryPreview.Perimeter(r)) continue;
                Edge(r, Vector3.back, r.width, new Vector3(r.center.x, 0, r.yMin));
                Edge(r, Vector3.forward, r.width, new Vector3(r.center.x, 0, r.yMax));
                Edge(r, Vector3.left, r.height, new Vector3(r.xMin, 0, r.center.y));
                Edge(r, Vector3.right, r.height, new Vector3(r.xMax, 0, r.center.y));
            }
            void Edge(Rect r, Vector3 normal, float length, Vector3 edge)
            {
                Vector3 along = Mathf.Abs(normal.z) > .5f ? Vector3.right : Vector3.forward;
                for (float t = -length / 2 + 2; t <= length / 2 - 2 + .001f; t += 4)
                {
                    Vector3 p = edge + along * t;
                    bool open = true;
                    foreach (float check in new[] { -1.7f, -.6f, .6f, 1.7f })
                    {
                        var plane = MazeMap.ToPlane(p + along * check + normal * .12f);
                        if (!MazeMap.Walkable(Mathf.FloorToInt(plane.x / MazeMap.C), Mathf.FloorToInt(plane.y / MazeMap.C))) { open = false; break; }
                    }
                    if (!open) continue;
                    Vector3 position = p - normal * .25f;
                    float priority = Vector2.Distance(new Vector2(p.x, -p.z), MazeMap.playerSpawn) + (normal.z < 0 ? -5 : 3);
                    candidates.Add((r, position, Quaternion.LookRotation(normal).eulerAngles.y, priority));
                }
            }
            boundaryProof?.Dress(walls, Place);
            int count = 0;
            foreach (var c in candidates.OrderBy(c => c.priority))
            {
                if (count >= 20) break;
                // Reserve the north facade's opening-sized decorative span.
                if (c.r == north && Mathf.Abs(c.p.x - MazeMap.W * MazeMap.C * .52f) < 4) continue;
                if (Place(1, c.r, c.p, c.yaw, true) != null) count++;
            }
        }
        GameObject Place(int number, Rect allowed, Vector3 position, float yaw, bool mounted = false)
        {
            string id = number.ToString("00");
            var prefab = Resources.Load<GameObject>("CryoKit/Prefabs/" + id + "_" + Slugs[number - 1]);
            if (prefab == null) throw new InvalidOperationException("Missing Cryo native prefab " + id);
            int triangles = prefab.GetComponentsInChildren<MeshFilter>().Sum(m => (int)m.sharedMesh.GetIndexCount(0) / 3);
            // Native prefabs may use multiple submeshes.
            triangles = prefab.GetComponentsInChildren<MeshFilter>().Sum(m => Enumerable.Range(0, m.sharedMesh.subMeshCount).Sum(s => (int)m.sharedMesh.GetIndexCount(s) / 3));
            if (info.nativeTriangles + triangles > TriangleBudget || info.nativeInstances >= InstanceBudget) return null;
            var go = Object.Instantiate(prefab, root); go.name = id + " " + Slugs[number - 1];
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds BoundsOf() { var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); return b; }
            var bounds = BoundsOf();
            float fit = Mathf.Min(1, allowed.width / bounds.size.x, allowed.height / bounds.size.z);
            if (fit < .65f) { Object.Destroy(go); return null; }
            go.transform.localScale *= fit;
            bounds = BoundsOf(); Vector3 shift = Vector3.zero;
            if (bounds.min.x < allowed.xMin) shift.x += allowed.xMin - bounds.min.x;
            if (bounds.max.x > allowed.xMax) shift.x += allowed.xMax - bounds.max.x;
            if (bounds.min.z < allowed.yMin) shift.z += allowed.yMin - bounds.min.z;
            if (bounds.max.z > allowed.yMax) shift.z += allowed.yMax - bounds.max.z;
            go.transform.position += shift;
            info.nativeTriangles += triangles; info.nativeInstances++;
            if (!info.usedIds.Contains(id)) info.usedIds.Add(id);
            info.placements.Add(new CryoKitPlacement { id = id, instance = go, allowedXZ = allowed, mounted = mounted, triangles = triangles });
            return go;
        }
    }
}
