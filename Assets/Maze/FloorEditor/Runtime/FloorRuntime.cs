using System.Collections.Generic;
using UnityEngine;

namespace Maze.FloorEditing
{
    public sealed class FloorRuntime : MonoBehaviour
    {
        readonly List<Material> materials = new();
        public static bool TryCurrent(out FloorDocument doc) => FloorDocuments.TryGetOverride(MazeMap.LayoutName(MazeMap.MapIndex), out doc);
        public static bool EditedGeometry => TryCurrent(out var doc) && FloorDocuments.RowsDifferFromOriginal(doc);
        public static int Spawn(Transform parent)
        {
            if (!TryCurrent(out var doc)) return 0;
            var root = new GameObject("Authored floor objects"); root.transform.SetParent(parent, false);
            // Resources rooms carry a mirrored FBX root; preserve world coordinates and unit scale.
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = new Vector3(1f / parent.lossyScale.x, 1f / parent.lossyScale.y, 1f / parent.lossyScale.z);
            var owner = root.AddComponent<FloorRuntime>();
            int count = 0;
            foreach (var item in doc.objects)
            {
                var go = FloorObjectVisuals.Spawn(item, root.transform, owner.materials);
                if (item.preserveBehavior && !FloorModelCatalog.IsLocalId(item.assetId))
                {
                    if (item.assetId == "cryo-gun-turret" || item.assetId == "cryo-flamethrower-turret") go.AddComponent<CryoHeroTurret>().Init(item.assetId == "cryo-flamethrower-turret");
                    else go.AddComponent<CryoPropMotion>().Init(item.assetId);
                }
                count++;
            }
            return count;
        }
        void OnDestroy() { foreach (var m in materials) if (m != null) Destroy(m); materials.Clear(); }
        // Keep collision authoritative while leaving room for the actual prop mesh above an island.
        public static List<RectInt> VisibleBlocks(char glyph)
        {
            if (!TryCurrent(out var doc) || (glyph != 'B' && glyph != 'C')) return MazeMap.Rects(c => c == glyph);
            var hidden = new HashSet<Vector2Int>();
            foreach (var o in doc.objects) if (o.legacyFit || o.collision != "none") foreach (var cell in FloorDocuments.FootprintCells(o)) hidden.Add(cell);
            var used = new bool[MazeMap.H, MazeMap.W]; var result = new List<RectInt>();
            bool Has(int x, int y) => x >= 0 && y >= 0 && x < MazeMap.W && y < MazeMap.H && !used[y, x] && MazeMap.grid[y, x] == glyph && !hidden.Contains(new Vector2Int(x, y));
            for (int y = 0; y < MazeMap.H; y++) for (int x = 0; x < MazeMap.W; x++)
            {
                if (!Has(x, y)) continue; int w = 1, h = 1; while (Has(x + w, y)) w++;
                bool row = true; while (row && y + h < MazeMap.H) { for (int i = 0; i < w; i++) if (!Has(x + i, y + h)) { row = false; break; } if (row) h++; }
                for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) used[y + j, x + i] = true;
                result.Add(new RectInt(x, y, w, h));
            }
            return result;
        }
    }
}
