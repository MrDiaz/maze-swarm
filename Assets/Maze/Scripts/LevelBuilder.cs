using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Builds the warehouse from MazeMap: one painted floor, buildings, crates, outer walls,
    // railings and props. All boxes are merged into one mesh per material (few draw calls on phones)
    // and use world-space UVs so every texture keeps its real-world scale on any block size.
    public class LevelBuilder
    {
        class Accum { public readonly List<Vector3> v = new(); public readonly List<Vector3> n = new(); public readonly List<Vector2> uv = new(); public readonly List<int> t = new(); public bool shadows = true; }
        readonly Dictionary<Material, Accum> accums = new();
        readonly System.Random srand = new System.Random(1337);
        float R => (float)srand.NextDouble();
        readonly Transform root;

        public Material side, roof, concrete, crate, crateCap, trim, door, hvac, glass, yellow, dark, sign, ground, floor;
        // Cryo Station "Frosted lab" dressing (null = the old warehouse look)
        public Material cryoPad, icicle, wallLight, drift, creep, sac;
        public bool Cryo => cryoPad != null;
        public readonly List<Vector3> creepEdges = new(); // steam wisp spots (where creep meets ice)

        public LevelBuilder(Transform root) { this.root = root; }

        Accum Get(Material m, bool shadows = true)
        {
            if (!accums.TryGetValue(m, out var a)) { a = new Accum { shadows = shadows }; accums[m] = a; }
            return a;
        }

        void Quad(Accum a, Vector3 c, Vector3 U, Vector3 V, Vector3 n, bool faceUV, Vector3 uvU, Vector3 uvV)
        {
            int i = a.v.Count;
            Vector3[] p = { c - U - V, c - U + V, c + U + V, c + U - V };
            Vector2[] fuv = { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
            for (int k = 0; k < 4; k++)
            {
                a.v.Add(p[k]); a.n.Add(n);
                a.uv.Add(faceUV ? fuv[k] : new Vector2(Vector3.Dot(p[k], uvU), Vector3.Dot(p[k], uvV)));
            }
            a.t.Add(i); a.t.Add(i + 1); a.t.Add(i + 2);
            a.t.Add(i); a.t.Add(i + 2); a.t.Add(i + 3);
        }

        // box in Unity world coords. sides and caps (top/bottom) can use different materials.
        public void Box(Vector3 center, Vector3 size, Material sideMat, Material capMat = null, bool faceUV = false, bool shadows = true)
        {
            capMat ??= sideMat;
            var h = size * 0.5f;
            var faces = new (Vector3 n, Vector3 U)[]
            {
                (Vector3.right, Vector3.forward), (Vector3.left, Vector3.back),
                (Vector3.forward, Vector3.left), (Vector3.back, Vector3.right),
                (Vector3.up, Vector3.right), (Vector3.down, Vector3.right),
            };
            foreach (var (n, Udir) in faces)
            {
                var Vdir = Vector3.Cross(Udir, n);
                var c = center + Vector3.Scale(n, h);
                var U = Udir * Mathf.Abs(Vector3.Dot(Udir, h));
                var V = Vdir * Mathf.Abs(Vector3.Dot(Vdir, h));
                var mat = Mathf.Abs(n.y) > 0.5f ? capMat : sideMat;
                Quad(Get(mat, shadows), c, U, V, n, faceUV, Udir, Vdir);
            }
        }

        public void Build(Texture2D floorTex)
        {
            float C = MazeMap.C;
            float mapW = MazeMap.W * C, mapH = MazeMap.H * C;

            // painted floor: a single quad, UV 0..1 across the whole map
            {
                var a = Get(floor);
                int i = a.v.Count;
                a.v.Add(new Vector3(0, 0, -mapH)); a.v.Add(new Vector3(0, 0, 0)); a.v.Add(new Vector3(mapW, 0, 0)); a.v.Add(new Vector3(mapW, 0, -mapH));
                a.uv.Add(new Vector2(0, 0)); a.uv.Add(new Vector2(0, 1)); a.uv.Add(new Vector2(1, 1)); a.uv.Add(new Vector2(1, 0));
                for (int k = 0; k < 4; k++) a.n.Add(Vector3.up);
                a.t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            // outside ground (fades into the fog)
            Box(new Vector3(mapW / 2, -0.06f, -mapH / 2), new Vector3(600, 0.1f, 600), ground, ground, false, false);

            BuildBlocks('B', 2.4f, 3.6f, true, side, roof);
            if (!(MazeMap.Facility && UseCrateProps && CrateProps())) BuildBlocks('C', 1.4f, 1.4f, false, crate, crateCap ?? crate);
            BuildBlocks('#', 3.4f, 3.4f, false, concrete, roof);
            if (Cryo) CryoDressing();

            // railings: dark pipes with posts
            foreach (var r in MazeMap.Rects(c => c == 'r'))
            {
                bool horiz = r.width >= r.height;
                float len = (horiz ? r.width : r.height) * C;
                var ctr = MazeMap.ToWorld(new Vector2((r.x + r.width / 2f) * C, (r.y + r.height / 2f) * C));
                foreach (var y in new[] { 0.55f, 1.0f })
                    Box(ctr + Vector3.up * y, horiz ? new Vector3(len, 0.09f, 0.09f) : new Vector3(0.09f, 0.09f, len), dark);
                int posts = Mathf.RoundToInt(len / 2.5f);
                for (int i = 0; i <= posts; i++)
                {
                    float t = -len / 2 + len / posts * i;
                    Box(ctr + (horiz ? new Vector3(t, 0.52f, 0) : new Vector3(0, 0.52f, t)), new Vector3(0.12f, 1.05f, 0.12f), dark);
                }
            }

            foreach (var kv in accums)
            {
                var a = kv.Value;
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "Maze_" + kv.Key.name };
                mesh.SetVertices(a.v); mesh.SetNormals(a.n); mesh.SetUVs(0, a.uv); mesh.SetTriangles(a.t, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = a.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        }

        // ---------------------------------------------------------------- Cryo Station dressing
        // icicles along the top edges, small warm wall lights, snow drifts banked against the base (decal strips that
        // don't block anything); each kind is one merged mesh
        void FrostEdges(Vector3 ctr, float w, float d, float h, bool lights)
        {
            var faces = new (Vector3 n, Vector3 along, float len)[] { (Vector3.back, Vector3.right, w), (Vector3.forward, Vector3.left, w), (Vector3.right, Vector3.forward, d), (Vector3.left, Vector3.back, d) };
            foreach (var (n, along, len) in faces)
            {
                var faceC = ctr + Vector3.Scale(n, new Vector3(w / 2, 0, d / 2));
                // icicles: little four-sided spikes hanging off the roof edge
                var ic = Get(icicle, false);
                for (float t = -len / 2 + 0.2f; t < len / 2 - 0.15f; t += 0.26f + R * 0.2f)
                {
                    if (R < 0.28f) continue;
                    float L = 0.15f + R * R * 0.6f, r = 0.05f + R * 0.05f;
                    var top = faceC + along * t + n * (0.06f + r) + Vector3.up * h;
                    Spike(ic, top, r, L, along, n);
                }
                // warm wall lights every ~5 m
                if (lights || R < 0.5f)
                    for (float t = -len / 2 + 2.2f; t < len / 2 - 1.5f; t += 4.5f + R * 2f)
                        Box(faceC + along * t + n * 0.05f + Vector3.up * Mathf.Min(2.1f, h - 0.45f), Abs(along * 0.16f + n * 0.08f + Vector3.up * 0.4f), wallLight, wallLight, true, false);
                // snow drift strip along the base, fading out across the floor
                float depth = 0.9f + R * 0.6f;
                DriftStrip(faceC + Vector3.up * 0.022f, along, n, len + depth * 0.8f, depth);
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        void Spike(Accum a, Vector3 top, float r, float L, Vector3 u, Vector3 n)
        {
            int i = a.v.Count;
            var corners = new[] { top - u * r - n * r, top + u * r - n * r, top + u * r + n * r, top - u * r + n * r };
            var tip = top + Vector3.down * L;
            for (int k = 0; k < 4; k++)
            {
                var p0 = corners[k]; var p1 = corners[(k + 1) % 4];
                var nn = Vector3.Cross(p1 - p0, tip - p0).normalized;
                if (Vector3.Dot(nn, (p0 + p1) * 0.5f - top) < 0) nn = -nn; // outward (the material is double-sided)
                a.v.Add(p0); a.v.Add(p1); a.v.Add(tip);
                a.n.Add(nn); a.n.Add(nn); a.n.Add(nn);
                a.uv.Add(Vector2.zero); a.uv.Add(Vector2.right); a.uv.Add(Vector2.up);
                a.t.Add(i + k * 3); a.t.Add(i + k * 3 + 2); a.t.Add(i + k * 3 + 1);
            }
        }

        void DriftStrip(Vector3 baseC, Vector3 along, Vector3 n, float len, float depth)
        {
            var a = Get(drift, false);
            int i = a.v.Count;
            float u0 = R * 10;
            var p = new[] { baseC - along * len / 2, baseC + along * len / 2, baseC + along * len / 2 + n * depth, baseC - along * len / 2 + n * depth };
            var uv = new[] { new Vector2(u0, 0), new Vector2(u0 + len / 3f, 0), new Vector2(u0 + len / 3f, 1), new Vector2(u0, 1) };
            for (int k = 0; k < 4; k++) { a.v.Add(p[k]); a.n.Add(Vector3.up); a.uv.Add(uv[k]); }
            // wind so the face points up whichever side the wall faces
            bool flip = Vector3.Dot(Vector3.Cross(p[1] - p[0], p[3] - p[0]), Vector3.up) < 0;
            if (flip) a.t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); else a.t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        // cyan cryo panels under ice on the old blue zones; creep (+ egg sacs) only at the spawns and along the outer
        // edge, away from the central lanes
        void CryoDressing()
        {
            float C = MazeMap.C;
            foreach (var r in MazeMap.Rects(c => c == '~'))
            {
                var ctr = MazeMap.ToWorld(new Vector2((r.x + r.width / 2f) * C, (r.y + r.height / 2f) * C));
                Quad(Get(cryoPad, false), ctr + Vector3.up * 0.012f, Vector3.right * (r.width * C / 2), Vector3.forward * (r.height * C / 2), Vector3.up, false, Vector3.right, Vector3.forward);
            }
            var spots = new List<(Vector2 p, float size)>();
            foreach (var s in MazeMap.spawnPoints) spots.Add((s, 8.5f + R * 2.5f));
            float mapW = MazeMap.W * C, mapH = MazeMap.H * C;
            var mid = MazeMap.playerSpawn;
            for (int k = 0; k < 40 && spots.Count < MazeMap.spawnPoints.Count + 12; k++)
            {
                // along the outer wall, never near the middle of the map
                float t = R; int side = (int)(R * 4);
                var p = side == 0 ? new Vector2(t * mapW, 3.2f) : side == 1 ? new Vector2(t * mapW, mapH - 3.2f) : side == 2 ? new Vector2(3.2f, t * mapH) : new Vector2(mapW - 3.2f, t * mapH);
                if ((p - mid).magnitude < 16) continue;
                bool close = false; foreach (var q in spots) if ((q.p - p).magnitude < 7) close = true;
                if (!close) spots.Add((p, 4.5f + R * 3f));
            }
            var ca = Get(creep, false); var sa = Get(sac, false);
            foreach (var (p, size) in spots)
            {
                var c = MazeMap.ToWorld(p, 0.03f + R * 0.004f);
                float ang = R * 360; var u = Quaternion.Euler(0, ang, 0) * Vector3.right * size / 2; var v = Quaternion.Euler(0, ang, 0) * Vector3.forward * size / 2;
                int i = ca.v.Count;
                var pts = new[] { c - u - v, c - u + v, c + u + v, c + u - v };
                var uvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                for (int k = 0; k < 4; k++) { ca.v.Add(pts[k]); ca.n.Add(Vector3.up); ca.uv.Add(uvs[k]); }
                ca.t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                // egg sacs clustered near the middle of the growth
                int ns = 3 + (int)(R * 5 * size / 6);
                for (int k = 0; k < ns; k++)
                {
                    var off = Random2() * size * 0.22f;
                    Egg(sa, c + new Vector3(off.x, 0.02f, off.y), 0.09f + R * 0.13f);
                }
                for (int k = 0; k < 4; k++) { var e = Random2().normalized * size * 0.4f; creepEdges.Add(c + new Vector3(e.x, 0.1f, e.y)); }
            }
        }

        Vector2 Random2() { float a = R * 6.283f, r = Mathf.Sqrt(R); return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }

        // a squashed low-poly sphere, half sunk into the creep
        void Egg(Accum a, Vector3 c, float r)
        {
            const int seg = 8, rings = 5;
            int i0 = a.v.Count;
            for (int j = 0; j <= rings; j++)
            {
                float th = Mathf.PI * j / rings;
                for (int i = 0; i <= seg; i++)
                {
                    float ph = Mathf.PI * 2 * i / seg;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    a.v.Add(c + Vector3.Scale(n, new Vector3(r, r * 0.8f, r))); a.n.Add(n); a.uv.Add(new Vector2(i / (float)seg, j / (float)rings));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int p = i0 + j * (seg + 1) + i, q = p + seg + 1;
                    a.t.Add(p); a.t.Add(p + 1); a.t.Add(q); a.t.Add(p + 1); a.t.Add(q + 1); a.t.Add(q);
                }
        }

        // Escape Test: the Blender-built cargo crate (Resources/Models/cargo_crate, a 1.4 m cube) on every 'C' cell, scaled
        // to ~1.9 m so one fills a 2 m cell: the grid, collisions, bullets and nav stay exactly as before. A random 90-degree
        // turn per cell (seeded by the cell, so it's stable), now and then a smaller crate stacked on a block (visual
        // only), and a soft contact shadow under each. All crates share one material (instanced).
        public static bool UseCrateProps = true; // (editor before/after shots)
        bool CrateProps()
        {
            var pf = Resources.Load<GameObject>("Models/cargo_crate");
            if (pf == null) return false;
            float C = MazeMap.C;
            var holder = new GameObject("Crates").transform; holder.SetParent(root, false);
            var shadowMat = Mats.Fade(new Color(0.05f, 0.06f, 0.08f, 0.42f), Holo.SoftSprite.texture);
            var tq = GameObject.CreatePrimitive(PrimitiveType.Quad); var quad = tq.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(tq);
            Material shared = null;
            Transform Place(Vector3 pos, float yaw, float scale)
            {
                var go = Object.Instantiate(pf, holder); Mats.ConvertGltf(go);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); go.transform.localScale = Vector3.one * scale;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    if (shared == null) { shared = r.sharedMaterial; shared.enableInstancing = true; shared.SetColor("_EmissionColor", shared.GetColor("_EmissionColor") * 0.6f); } // a faint navy glow, not a lamp
                    r.sharedMaterial = shared; r.shadowCastingMode = ShadowCastingMode.On;
                }
                return go.transform;
            }
            const float Cell = 1.9f / 1.4f; // crate scale for a 2 m cell
            foreach (var rc in MazeMap.Rects(c => c == 'C'))
            {
                for (int z = rc.y; z < rc.y + rc.height; z++)
                for (int x = rc.x; x < rc.x + rc.width; x++)
                {
                    var rng = new System.Random(x * 7919 + z * 104729 + 17);
                    var c = MazeMap.ToWorld(new Vector2((x + 0.5f) * C, (z + 0.5f) * C));
                    Place(c, rng.Next(4) * 90f, Cell * (0.97f + 0.03f * (float)rng.NextDouble()));
                    var sh = new GameObject("CrateShadow"); sh.transform.SetParent(holder, false);
                    sh.transform.SetPositionAndRotation(c + Vector3.up * 0.012f, Quaternion.Euler(90, 0, 0)); sh.transform.localScale = new Vector3(2.7f, 2.7f, 1);
                    sh.AddComponent<MeshFilter>().sharedMesh = quad;
                    var mr = sh.AddComponent<MeshRenderer>(); mr.sharedMaterial = shadowMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                }
                // a block of 4+ crates sometimes carries one more on top (seeded by the block)
                var br = new System.Random(rc.x * 31 + rc.y * 977 + 5);
                if (rc.width * rc.height >= 4 && br.NextDouble() < 0.35)
                {
                    int sx = rc.x + br.Next(rc.width), sz = rc.y + br.Next(rc.height);
                    var top = MazeMap.ToWorld(new Vector2((sx + 0.5f) * C, (sz + 0.5f) * C), 1.9f);
                    Place(top, br.Next(4) * 90f + (float)(br.NextDouble() * 24 - 12), 1.0f);
                }
            }
            return true;
        }

        void BuildBlocks(char ch, float hMin, float hMax, bool decor, Material sideMat, Material capMat)
        {
            float C = MazeMap.C;
            foreach (var r in MazeMap.Rects(c => c == ch))
            {
                float w = r.width * C, d = r.height * C;
                var ctr = MazeMap.ToWorld(new Vector2((r.x + r.width / 2f) * C, (r.y + r.height / 2f) * C));
                float h = hMin + R * (hMax - hMin);
                Box(ctr + Vector3.up * h / 2, new Vector3(w, h, d), sideMat, capMat);
                if (Cryo && ch != 'C') FrostEdges(ctr, w, d, h, ch == 'B');
                if (ch != 'C') Box(ctr + Vector3.up * 0.15f, new Vector3(w + 0.05f, 0.3f, d + 0.05f), trim, trim, false, false); // blue base trim
                if (!decor) continue;

                if (r.width * r.height >= 6 && R < 0.5f)
                {   // stepped roof section
                    float sw = w * (0.35f + R * 0.25f), sd = d * (0.4f + R * 0.3f);
                    Box(ctr + new Vector3((R - 0.5f) * (w - sw), h + 0.35f, (R - 0.5f) * (d - sd)), new Vector3(sw, 0.7f, sd), sideMat, capMat);
                }
                else if (r.width * r.height >= 4)
                {   // rooftop HVAC unit with a fan, skylights along the long axis
                    float ux = (R - 0.5f) * (w - 2), uz = (R - 0.5f) * (d - 2);
                    Box(ctr + new Vector3(ux, h + 0.35f, uz), new Vector3(1.3f, 0.7f, 1.0f), hvac);
                    Box(ctr + new Vector3(ux, h + 0.74f, uz), new Vector3(0.62f, 0.08f, 0.62f), dark);
                    if (r.width * r.height >= 8)
                    {
                        bool horiz = w >= d;
                        int n = Mathf.Max(1, Mathf.FloorToInt((horiz ? w : d) / 4) - 1);
                        for (int i = 0; i < n; i++)
                        {
                            float t = ((i + 1f) / (n + 1) - 0.5f) * (horiz ? w : d);
                            var p = horiz ? new Vector3(t, h + 0.06f, (uz < 0 ? 1 : -1) * d * 0.22f) : new Vector3((ux < 0 ? 1 : -1) * w * 0.22f, h + 0.06f, t);
                            if (Mathf.Abs(p.x - ux) < 1.4f && Mathf.Abs(p.z - uz) < 1.4f) continue;
                            Box(ctr + p, horiz ? new Vector3(0.9f, 0.12f, 1.6f) : new Vector3(1.6f, 0.12f, 0.9f), glass, glass, false, false);
                        }
                    }
                }
                // number plate on the camera-facing side (Unity -z)
                Box(ctr + new Vector3(-w / 2 + 0.75f, Mathf.Min(1.9f, h - 0.6f), -d / 2 - 0.02f), new Vector3(0.85f, 0.85f, 0.02f), sign, sign, true, false);

                // face decals: yellow stripes and blue roll-up doors
                var faces = new (Vector3 n, float len)[] { (Vector3.back, w), (Vector3.forward, w), (Vector3.right, d), (Vector3.left, d) };
                foreach (var (n, len) in faces)
                {
                    float roll = R, along = (R - 0.5f) * (len - 2);
                    if (n == Vector3.back) along = Mathf.Max(along, -w / 2 + 2); // keep clear of the sign
                    bool alongX = Mathf.Abs(n.z) > 0.5f;
                    var p = ctr + new Vector3(n.x * (w / 2 + 0.03f) + (alongX ? along : 0), 0, n.z * (d / 2 + 0.03f) + (alongX ? 0 : along));
                    if (roll < 0.3f)
                    {
                        float sh = Mathf.Min(1.4f, h - 0.6f);
                        Box(p + Vector3.up * (0.3f + sh / 2), alongX ? new Vector3(0.14f, sh, 0.06f) : new Vector3(0.06f, sh, 0.14f), yellow, yellow, false, false);
                    }
                    else if (roll < 0.55f && len >= 3)
                    {
                        float dw = Mathf.Min(2.4f, len - 0.8f);
                        Box(p + Vector3.up * 1.05f, alongX ? new Vector3(dw, 1.5f, 0.06f) : new Vector3(0.06f, 1.5f, dw), door, door, true, false);
                    }
                }
                // yellow floor line along a long edge
                if (R < 0.35f)
                {
                    bool horiz = w >= d;
                    float s = R < 0.5f ? -1 : 1, len = (horiz ? w : d) * 0.8f;
                    var p = ctr + (horiz ? new Vector3(0, 0.015f, s * (d / 2 + 0.7f)) : new Vector3(s * (w / 2 + 0.7f), 0.015f, 0));
                    var cell = MazeMap.ToPlane(p);
                    if (MazeMap.Walkable(Mathf.FloorToInt(cell.x / C), Mathf.FloorToInt(cell.y / C)))
                        Box(p, horiz ? new Vector3(len, 0.01f, 0.14f) : new Vector3(0.14f, 0.01f, len), yellow, yellow, false, false);
                }
            }
        }
    }
}
