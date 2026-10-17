using UnityEngine;
using UnityEngine.UI;

namespace Maze
{
    // Blend only the empty padding around source-pixel text covers. The centre
    // stays opaque so no reference values show through beneath live text.
    [RequireComponent(typeof(Image))]
    public sealed class ResultsPatchFeather : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount < 4) return;
            var vertex = new UIVertex();
            mesh.PopulateUIVertex(ref vertex, 0);
            Vector2 uvMin = vertex.uv0, uvMax = vertex.uv0;
            Color32 color = vertex.color;
            for (int i = 1; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                uvMin = Vector2.Min(uvMin, vertex.uv0); uvMax = Vector2.Max(uvMax, vertex.uv0);
            }
            Rect r = ((RectTransform)transform).rect;
            float pad = Mathf.Min(3, Mathf.Min(r.width, r.height) / 4);
            float[] xs = { r.xMin, r.xMin + pad, r.xMax - pad, r.xMax };
            float[] ys = { r.yMin, r.yMin + pad, r.yMax - pad, r.yMax };
            mesh.Clear();
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                vertex = UIVertex.simpleVert;
                vertex.position = new Vector3(xs[x], ys[y]);
                vertex.uv0 = new Vector2(Mathf.Lerp(uvMin.x, uvMax.x, (xs[x] - r.xMin) / r.width),
                    Mathf.Lerp(uvMin.y, uvMax.y, (ys[y] - r.yMin) / r.height));
                vertex.color = color;
                if (x == 0 || x == 3 || y == 0 || y == 3) vertex.color.a = 0;
                mesh.AddVert(vertex);
            }
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                int i = y * 4 + x;
                mesh.AddTriangle(i, i + 4, i + 5); mesh.AddTriangle(i, i + 5, i + 1);
            }
        }
    }
}
