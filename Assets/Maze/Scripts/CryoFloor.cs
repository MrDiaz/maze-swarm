using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Cryo Station's deck (Design/Concepts/2026-10-13-cryo-floor, WIRE-IN.md): a seamless 8 m tile (2048 px) in world-space
    // UVs with its normal, metal/smoothness (1 - rough), AO and a 30 m macro variation; the gameplay markings (ice pads /
    // spawn docks, hazard stripes, the exit pad) painted onto a transparent overlay above it; and decals batched by
    // material: frost drifts along every wall foot, worn hazard trim and a few cyan light strips along wall runs, and
    // vent grates in open floor. No shadows, no collision. Other zones keep the painted ProcTex floor.
    public static class CryoFloor
    {
        public static bool On = true;   // false = the old painted floor in Cryo too (before/after captures)
        const string Dir = "Cryo/floor2/";
        public const float Tile = 8f;   // metres per floor tile
        static Texture2D T(string n) => Resources.Load<Texture2D>(Dir + n);
        public static bool For(SectorDef s) => On && s != null && s.id == "cryo-station" && T("cryo_floor_albedo") != null;

        static Material Template(string n, string fallback)
        {
            var t = Resources.Load<Material>("Materials/" + n);
            return t != null ? new Material(t) : new Material(Shader.Find(fallback));
        }

        public static Material Floor()
        {
            var m = Template("MazeCryoFloor", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", new Color(0.95f, 0.9f, 0.85f));   // the zone's cold blue light over-blues the deck: pulled back to the preview's grey-blue
            m.SetTexture("_BaseMap", T("cryo_floor_albedo")); m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTexture("_BumpMap", T("cryo_floor_normal")); m.SetFloat("_BumpScale", 1.6f); m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", T("cryo_floor_ms")); m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f);
            m.SetTexture("_OcclusionMap", T("cryo_floor_ao")); m.SetFloat("_OcclusionStrength", 0.6f); m.EnableKeyword("_OCCLUSIONMAP");
            // the macro variation (0.93 to 1.05, baked as a x2 detail grey), one repeat per 30 m of the 8 m UV
            m.SetTexture("_DetailAlbedoMap", T("cryo_floor_macro")); m.SetTextureScale("_DetailAlbedoMap", Vector2.one * (Tile / 30f));
            m.SetFloat("_DetailAlbedoMapScale", 1f); m.EnableKeyword("_DETAIL_MULX2");
            return m;
        }

        static Material Decal(string tex, float smooth, int queue, string emit = null, float emitK = 0, string normal = null)
        {
            var m = Template(normal != null ? "MazeLitFadeNormalEmit" : emit != null ? "MazeLitFadeEmit" : "MazeLitFade", "Universal Render Pipeline/Lit");
            if (Resources.Load<Material>("Materials/MazeLitFade") == null) Mats.MakeTransparent(m, false);
            m.SetColor("_BaseColor", Color.white); m.SetTexture("_BaseMap", tex != null ? T(tex) : null);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            if (normal != null) { m.SetTexture("_BumpMap", T(normal)); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            if (emit != null) { m.SetTexture("_EmissionMap", T(emit)); m.SetColor("_EmissionColor", Color.white * emitK); m.EnableKeyword("_EMISSION"); }
            m.renderQueue = queue;
            return m;
        }

        class Acc { public readonly List<Vector3> v = new(); public readonly List<Vector2> uv = new(); public readonly List<int> t = new(); }
        static void Quad(Acc a, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
        {
            int i = a.v.Count;
            a.v.Add(p0); a.v.Add(p1); a.v.Add(p2); a.v.Add(p3); a.uv.Add(u0); a.uv.Add(u1); a.uv.Add(u2); a.uv.Add(u3);
            a.t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        static void Emit(Transform root, string name, Material m, Acc a)
        {
            if (a.v.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(a.v); mesh.SetUVs(0, a.uv); mesh.SetTriangles(a.t, 0);
            var n = new Vector3[a.v.Count]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up; mesh.normals = n;
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
        }

        static bool Wall(int x, int z)
        {
            if (x < 0 || z < 0 || x >= MazeMap.W || z >= MazeMap.H) return true;
            char c = MazeMap.grid[z, x]; return c == '#' || c == 'B' || c == 'm';
        }
        static bool Border(int x, int z) => x < 0 || z < 0 || x >= MazeMap.W || z >= MazeMap.H;
        static bool Floorish(int x, int z) => x >= 0 && z >= 0 && x < MazeMap.W && z < MazeMap.H && MazeMap.Walkable(x, z);

        // a cell edge in plane space: the wall is on side d of cell (x, z); returns the edge's two ends (plane, along +u)
        static readonly Vector2Int[] Dirs = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };

        public static GameObject Build(Transform parent)
        {
            float C = MazeMap.C; int W = MazeMap.W, H = MazeMap.H;
            var root = new GameObject("CryoFloorDecals").transform; root.SetParent(parent, false);

            // ---- the markings overlay: one quad over the map, a transparent texture with the gameplay paint
            const int ppu = 16; float cs = C * ppu; int FW = Mathf.RoundToInt(W * cs), FH = Mathf.RoundToInt(H * cs);
            var px = new Color32[FW * FH];
            void Rect(float x0, float y0, float w, float h, Color32 c)
            {
                int xa = Mathf.Max(0, Mathf.RoundToInt(x0)), ya = Mathf.Max(0, Mathf.RoundToInt(y0)), xb = Mathf.Min(FW, Mathf.RoundToInt(x0 + w)), yb = Mathf.Min(FH, Mathf.RoundToInt(y0 + h));
                for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++) px[y * FW + x] = c;
            }
            foreach (var r in MazeMap.Rects(ch => ch == '~'))
            {   // spawn docks / pads: the deep blue they had on the painted floor, a dark inset border
                Rect(r.x * cs, r.y * cs, r.width * cs, r.height * cs, new Color32(0x24, 0x45, 0xaa, 235));
                float t = 4;
                var e = new Color32(0x1e, 0x3c, 0x78, 230);
                Rect(r.x * cs + 3, r.y * cs + 3, r.width * cs - 6, t, e); Rect(r.x * cs + 3, (r.y + r.height) * cs - 3 - t, r.width * cs - 6, t, e);
                Rect(r.x * cs + 3, r.y * cs + 3, t, r.height * cs - 6, e); Rect((r.x + r.width) * cs - 3 - t, r.y * cs + 3, t, r.height * cs - 6, e);
            }
            foreach (var sp in MazeMap.spawnPoints)
            {   // the dock square at every enemy entrance (as the painted floor)
                float x0 = (sp.x - 1.6f) * ppu, y0 = (sp.y - 1.6f) * ppu, sz = 3.2f * ppu, lw = ppu * 0.12f; var w = new Color32(255, 255, 255, 170);
                Rect(x0, y0, sz, lw, w); Rect(x0, y0 + sz - lw, sz, lw, w); Rect(x0, y0, lw, sz, w); Rect(x0 + sz - lw, y0, lw, sz, w);
            }
            foreach (var r in MazeMap.Rects(ch => ch == 'E'))
            {
                Rect(r.x * cs, r.y * cs, r.width * cs, r.height * cs, new Color32(0x3f, 0xd6, 0xa0, 235));
                float lw = ppu * 0.25f; var w = new Color32(255, 255, 255, 220);
                Rect(r.x * cs, r.y * cs, r.width * cs, lw, w); Rect(r.x * cs, (r.y + r.height) * cs - lw, r.width * cs, lw, w);
                Rect(r.x * cs, r.y * cs, lw, r.height * cs, w); Rect((r.x + r.width) * cs - lw, r.y * cs, lw, r.height * cs, w);
            }
            foreach (var r in MazeMap.Rects(ch => ch == 'y' || ch == 'D' || ch == 'd'))
            {   // hazard zones / doors: yellow and black diagonal stripes, slightly worn
                float sw = ppu * 0.45f;
                for (int y = (int)(r.y * cs); y < (r.y + r.height) * cs && y < FH; y++)
                    for (int x = (int)(r.x * cs); x < (r.x + r.width) * cs && x < FW; x++)
                    {
                        bool yel = (int)((x + y) / sw) % 2 == 0;
                        float wear = 0.5f + 0.25f * Mathf.Sin(x * 0.05f + Mathf.Sin(y * 0.03f) * 3) + 0.25f * Mathf.Sin(y * 0.071f + x * 0.013f);
                        byte a = (byte)(Mathf.Lerp(235, 150, Mathf.SmoothStep(0.55f, 0.95f, wear)));
                        px[y * FW + x] = yel ? new Color32(0xe9, 0xc2, 0x41, a) : new Color32(0x26, 0x28, 0x2c, a);
                    }
            }
            var marks = new Texture2D(FW, FH, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, name = "CryoMarks" };
            var flip = new Color32[px.Length]; for (int y = 0; y < FH; y++) System.Array.Copy(px, y * FW, flip, (FH - 1 - y) * FW, FW);   // plane y runs down the texture (as ProcTex)
            marks.SetPixels32(flip); marks.Apply(true, true);
            var mm = Decal(null, 0.45f, 3000); mm.SetTexture("_BaseMap", marks);
            var ma = new Acc(); float mapW = W * C, mapH = H * C, y0m = 0.004f;
            Quad(ma, new Vector3(0, y0m, -mapH), new Vector3(0, y0m, 0), new Vector3(mapW, y0m, 0), new Vector3(mapW, y0m, -mapH), new(0, 0), new(0, 1), new(1, 1), new(1, 0));
            Emit(root, "CryoMarks", mm, ma);

            // ---- wall runs: per side, contiguous floor cells with a wall on that side
            var runs = new List<(Vector2Int a, int len, int d, bool border)>();
            for (int d = 0; d < 4; d++)
            {
                var dir = Dirs[d]; bool horiz = dir.y != 0;   // the wall is above/below: the run goes along x
                int outer = horiz ? H : W, inner = horiz ? W : H;
                for (int o = 0; o < outer; o++)
                {
                    int start = -1; bool border = false;
                    for (int i = 0; i <= inner; i++)
                    {
                        int x = horiz ? i : o, z = horiz ? o : i;
                        bool ok = i < inner && Floorish(x, z) && Wall(x + dir.x, z + dir.y);
                        if (ok && start < 0) { start = i; border = Border(x + dir.x, z + dir.y); }
                        if (!ok && start >= 0) { runs.Add((horiz ? new Vector2Int(start, o) : new Vector2Int(o, start), i - start, d, border)); start = -1; }
                    }
                }
            }
            var frost = new Acc(); var hazard = new Acc(); var strip = new Acc();
            int ri = 0;
            foreach (var (a, len, d, border) in runs)
            {
                var dir = Dirs[d]; bool horiz = dir.y != 0;
                // plane-space: the run's wall edge line and the inward normal (away from the wall)
                Vector2 along = horiz ? Vector2.right : Vector2.up, inward = -new Vector2(dir.x, dir.y);
                Vector2 cell0 = new Vector2(a.x * C, a.y * C);
                Vector2 edge0 = cell0 + new Vector2(dir.x > 0 ? C : 0, dir.y > 0 ? C : 0);   // the wall-side edge of the first cell
                float L = len * C;
                void Strip(Acc acc, float off, float depth, float y, float inset, float uScale)
                {   // u along the run in world metres / uScale (tiles seamlessly between neighbours), v 1 at the wall side
                    var p0 = edge0 + along * inset + inward * off; var p1 = edge0 + along * (L - inset) + inward * off;
                    var q0 = p0 + inward * depth; var q1 = p1 + inward * depth;
                    float s0 = Vector2.Dot(p0, along) / uScale, s1 = Vector2.Dot(p1, along) / uScale;
                    Quad(acc, MazeMap.ToWorld(p0, y), MazeMap.ToWorld(p1, y), MazeMap.ToWorld(q1, y), MazeMap.ToWorld(q0, y), new(s0, 1), new(s1, 1), new(s1, 0), new(s0, 0));
                }
                Strip(frost, 0f, 0.65f, 0.007f, 0f, 2f);
                if (!border && len >= 2) Strip(hazard, 0.8f, 0.3f, 0.009f, 0.3f, 2f);
                if (len >= 3 && (ri++ % 3) == 0) Strip(strip, 0.25f, 0.08f, 0.011f, 0.6f, 2f);
            }
            Emit(root, "CryoFrost", Decal("frost_drift", 0.35f, 3001), frost);
            Emit(root, "CryoHazardTrim", Decal("hazard_albedo", 0.3f, 3002), hazard);
            Emit(root, "CryoStrips", Decal("strip_albedo", 0.5f, 3003, "strip_emissive", 1.6f), strip);

            // ---- vent grates: a few 1.5 m squares in open floor, spaced out, never on paint or near the hero's spawn
            var rnd = new System.Random(MazeMap.MapIndex * 7919 + 17); var grates = new Acc(); var picked = new List<Vector2>();
            var cand = new List<Vector2Int>();
            for (int z = 1; z < H - 1; z++)
                for (int x = 1; x < W - 1; x++)
                {
                    bool ok = true;
                    for (int dz = -1; dz <= 1 && ok; dz++) for (int dx = -1; dx <= 1 && ok; dx++) { if (!Floorish(x + dx, z + dz)) ok = false; else { char c = MazeMap.grid[z + dz, x + dx]; if (c == '~' || c == 'y' || c == 'D' || c == 'd' || c == 'E') ok = false; } }
                    if (ok) cand.Add(new Vector2Int(x, z));
                }
            for (int k = cand.Count - 1; k > 0; k--) { int j = rnd.Next(k + 1); (cand[k], cand[j]) = (cand[j], cand[k]); }
            foreach (var c in cand)
            {
                if (picked.Count >= 6) break;
                var p = new Vector2((c.x + 0.5f) * C, (c.y + 0.5f) * C);
                if ((p - MazeMap.playerSpawn).magnitude < 4f) continue;
                bool near = false; foreach (var q in picked) if ((q - p).magnitude < 10f) near = true; if (near) continue;
                picked.Add(p); float h = 0.75f, y = 0.010f;
                Quad(grates, MazeMap.ToWorld(p + new Vector2(-h, h), y), MazeMap.ToWorld(p + new Vector2(-h, -h), y), MazeMap.ToWorld(p + new Vector2(h, -h), y), MazeMap.ToWorld(p + new Vector2(h, h), y), new(0, 0), new(0, 1), new(1, 1), new(1, 0));
            }
            Emit(root, "CryoGrates", Decal("grate_albedo", 0.45f, 3004, "grate_emissive", 1.6f, "grate_normal"), grates);
            return root.gameObject;
        }
    }
}
