using System;
using UnityEngine;

namespace Maze
{
    // One reversible art-review replacement only; disabled by default until visual acceptance.
    public static class CryoMachineryPreview
    {
        public static bool Enabled;
        public const string Resource = "CryoKit/Prefabs/21_structural_thermal_island_6x4";
        public static bool Fits(char type, Rect area) => Enabled && type == 'B' &&
            MazeMap.LayoutName(MazeMap.MapIndex) == "cryo-station/cryo-a" &&
            Mathf.Abs(area.xMin - 20) < .001f && Mathf.Abs(area.yMin + 14) < .001f &&
            Mathf.Abs(area.width - 6) < .001f && Mathf.Abs(area.height - 4) < .001f;
        public static GameObject Build(Transform parent, Rect area)
        {
            var prefab = Resources.Load<GameObject>(Resource);
            if (prefab == null) throw new InvalidOperationException("Import the v2 machinery proof before enabling preview.");
            var go = UnityEngine.Object.Instantiate(prefab, parent);
            go.name = "V2 machinery block — one footprint preview";
            go.transform.SetPositionAndRotation(new Vector3(23, 0, -12), Quaternion.Euler(0, 180, 0));
            // Authored 6 x 4 m footprint, 3.808 m overall height and ground-centre pivot, at scale 1.
            var info = go.AddComponent<CryoMachineryPreviewInfo>(); info.allowedXZ = area;
            return go;
        }
    }
    public sealed class CryoMachineryPreviewInfo : MonoBehaviour { public Rect allowedXZ; }
}
