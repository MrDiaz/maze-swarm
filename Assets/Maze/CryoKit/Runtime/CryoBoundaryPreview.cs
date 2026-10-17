using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze
{
    // Art-review proof only. Ordinary runs/builds keep the existing boundary presentation.
    public static class CryoBoundaryPreview
    {
        public static bool Enabled;
        public static bool Active
        {
            get
            {
                if (!Enabled || MazeMap.LayoutName(MazeMap.MapIndex) != "cryo-station/cryo-a") return false;
                var g = Object.FindAnyObjectByType<Game>();
                return g != null && g.run && g.floor == 1 && g.sector?.id == "cryo-station";
            }
        }
        public static bool Perimeter(Rect r) => r.xMin < .01f || r.xMax > MazeMap.W * MazeMap.C - .01f || r.yMax > -.01f || r.yMin < -MazeMap.H * MazeMap.C + .01f;
    }
    public sealed class CryoBoundarySceneInfo : MonoBehaviour
    {
        public readonly List<Rect> wallFootprints = new();
        public readonly List<GameObject> serviceHardware = new();
        public int bays, cutawayBays, boxes;
    }
    public sealed class CryoBoundaryTreatment
    {
        readonly LevelBuilder builder;
        readonly Material enamel, steel, frame, recess, glazing, accent;
        readonly CryoBoundarySceneInfo info;
        public CryoBoundaryTreatment(Transform root, LevelBuilder builder, CryoKitSceneInfo lifetime, Material paint, Material metal, Material guard)
        {
            this.builder = builder;
            var owner = new GameObject("Cryo boundary architecture — opt-in floor 1 proof"); owner.transform.SetParent(root, false);
            info = owner.AddComponent<CryoBoundarySceneInfo>();
            Material Copy(Material source, string name, Color? tint = null)
            {
                var m = new Material(source) { name = "Cryo boundary " + name }; m.SetFloat("_UseAOMap", 0);
                if (tint.HasValue) m.SetVector("_BaseColor", tint.Value);
                lifetime.ownedMaterials.Add(m); return m;
            }
            enamel = Copy(paint, "enamel", new Color(.58f, .69f, .73f, 1));
            steel = Copy(metal, "steel"); frame = Copy(guard, "frame");
            recess = Copy(guard, "service recess", new Color(.10f, .17f, .20f, 1));
            glazing = Copy(paint, "sealed frosted observation panel", new Color(.28f, .57f, .64f, 1));
            accent = Copy(metal, "coolant identification", new Color(.12f, .56f, .62f, 1));
        }
        static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        void Part(Rect r, Vector3 along, Vector3 inward, float u, float y, float depthAt, float width, float height, float depth, Material material)
        {
            var centre = new Vector3(r.center.x, y, r.center.y) + along * u + inward * depthAt;
            builder.Box(centre, Abs(along * width + Vector3.up * height + inward * depth), material);
            info.boxes++;
        }
        public bool Build(Rect r)
        {
            if (!CryoBoundaryPreview.Perimeter(r)) return false;
            info.wallFootprints.Add(r);
            bool horizontal = r.width > r.height;
            Vector3 along = horizontal ? Vector3.right : Vector3.forward;
            Vector3 inward = horizontal ? (r.yMax > -.01f ? Vector3.back : Vector3.forward) : (r.xMin < .01f ? Vector3.right : Vector3.left);
            float length = horizontal ? r.width : r.height;
            int count = Mathf.CeilToInt(length / 6f); float span = length / count;
            Part(r, along, inward, 0, .09f, 0, length - .10f, .18f, 1.80f, frame);
            for (int i = 0; i < count; i++)
            {
                float u = -length / 2 + span * (i + .5f);
                float z = r.center.y + along.z * u;
                bool cutaway = inward.z > .5f || (!horizontal && z < -MazeMap.H * MazeMap.C + 12f);
                float height = cutaway ? .58f : horizontal ? 3.15f : 2.65f;
                info.bays++; if (cutaway) info.cutawayBays++;
                float w = span - .14f;
                if (cutaway)
                {
                    // Low section line represents the omitted foreground wall, not a new route.
                    Part(r, along, inward, u, height / 2, .48f, w, height, .58f, frame);
                    Part(r, along, inward, u, height + .035f, .48f, w, .07f, .65f, steel);
                    Part(r, along, inward, u, .40f, .795f, w - .45f, .055f, .035f, accent);
                    continue;
                }
                Part(r, along, inward, u, height / 2, -.72f, w, height, .16f, enamel);
                Part(r, along, inward, u, height - .10f, -.25f, w, .20f, 1.00f, frame);
                Part(r, along, inward, u - w / 2 + .12f, height / 2, .05f, .24f, height, 1.48f, frame);
                Part(r, along, inward, u + w / 2 - .12f, height / 2, .05f, .24f, height, 1.48f, frame);
                Part(r, along, inward, u, .50f, .10f, w - .45f, .64f, 1.50f, enamel);
                bool observation = i % 3 == 1;
                if (observation)
                {
                    Part(r, along, inward, u, 1.77f, .40f, w - .75f, 1.45f, .12f, glazing);
                    foreach (float edge in new[] { -1f, 1f }) Part(r, along, inward, u + edge * (w - .9f) / 2, 1.77f, .52f, .07f, 1.6f, .08f, steel);
                    foreach (float y in new[] { .99f, 2.55f }) Part(r, along, inward, u, y, .52f, w - .75f, .08f, .08f, steel);
                    Part(r, along, inward, u, 1.77f, .53f, .075f, 1.45f, .08f, frame);
                }
                else
                {
                    Part(r, along, inward, u, 1.76f, -.35f, w - .65f, 1.65f, .16f, recess);
                    float panelWidth = Mathf.Min(2.15f, w - 1.1f);
                    if (i % 3 == 0) for (int slat = 0; slat < 7; slat++) Part(r, along, inward, u, 1.28f + slat * .15f, .15f, panelWidth, .065f, .10f, steel);
                    else
                    {
                        Part(r, along, inward, u, 1.76f, .25f, panelWidth, 1.30f, .14f, enamel);
                        Part(r, along, inward, u + panelWidth * .32f, 1.76f, .335f, .055f, 1.0f, .035f, steel);
                    }
                }
                Part(r, along, inward, u, .87f, .795f, w - .65f, .06f, .035f, accent);
            }
            return true;
        }
        public void Dress(List<Rect> walls, Func<int, Rect, Vector3, float, bool, GameObject> place)
        {
            foreach (var r in walls)
            {
                bool north = r.yMax > -.01f && r.width > 8;
                bool west = r.xMin < .01f && r.height > 8;
                if (!north && !west) continue;
                if (north)
                {
                    Add(10, r, new Vector3(2.75f, 1.05f, -1.48f), 180);
                    Add(10, r, new Vector3(35.75f, 1.05f, -1.48f), 180);
                    Add(14, r, new Vector3(23, 1.67f, -1.70f), 180);
                }
                else Add(10, r, new Vector3(1.48f, .95f, -11f), 90);
            }
            void Add(int id, Rect allowed, Vector3 at, float yaw)
            {
                var go = place(id, allowed, at, yaw, true);
                if (go == null) return;
                go.transform.SetParent(info.transform, true); info.serviceHardware.Add(go);
            }
        }
    }
}
