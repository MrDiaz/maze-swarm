using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Cryo floors dressed as one working station (2026-10-16, Manuel: "the floors in Cryo feel disconnected"). Toggle:
    // ReadabilityFx.CryoDressing. Visual only, on top of CryoProps.Spawn: walkable cells, collision, layout, spawns, hazards and
    // the hero turrets' positions are untouched; nothing new stands in a walking lane (floor inlays are flat).
    //   - each prop island gets a purpose from what stands on it (reactor core, pod bay, pump station, coolant store, storage,
    //     maintenance, turret post) and is filled along its long axis with a matching cluster of the existing Meshy props
    //   - every prop on an island faces the room on the grid (small deliberate jitter on crates only), sitting on one line
    //   - the recurring motif: a navy coolant pipe with a cyan light line along every wall base, and a flat cyan coolant inlay
    //     from each island to its nearest wall; brackets on the pipe every 4 m
    //   - grounding: a soft contact shadow under every prop, a cyan glow pool under the machines, frost drifts at the wall feet
    // Skipped on floors the Floor Editor owns (FloorRuntime). Budget: at most ~90k extra triangles per floor.
    public static class CryoDressing
    {
        public static int ExtraTris, ExtraProps;   // (checks)
        static System.Random rng = new(1);
        static float Jit(float a) => (float)(rng.NextDouble() * 2 - 1) * a;
        const int TriBudget = 90000;
        static readonly Dictionary<string, int> propTris = new() {
            { "cryo-pod", 18000 }, { "cryo-supply-crate", 12000 }, { "cryo-freeze-canister", 12000 }, { "cryo-low-coolant-tank", 15000 },
            { "cryo-maintenance-cart", 21000 }, { "cryo-coolant-pumping-station", 22000 }, { "cryo-cooling-reactor", 25000 } };
        // what an island holds decides what it gets (order = priority)
        static readonly (string has, string purpose, string[] extras)[] Recipes = {
            ("cryo-cooling-reactor", "reactor core", new[] { "cryo-low-coolant-tank", "cryo-freeze-canister" }),
            ("cryo-pod", "pod bay", new[] { "cryo-pod", "cryo-freeze-canister" }),
            ("cryo-coolant-pumping-station", "pump station", new[] { "cryo-freeze-canister", "cryo-freeze-canister" }),
            ("cryo-low-coolant-tank", "coolant store", new[] { "cryo-freeze-canister", "cryo-supply-crate" }),
            ("cryo-maintenance-cart", "maintenance", new[] { "cryo-supply-crate" }),
            ("cryo-supply-crate", "storage", new[] { "cryo-supply-crate", "cryo-freeze-canister" }),
        };

        class Island { public RectInt cells; public Rect world; public List<Transform> props = new(); public string purpose; }

        public static void Dress(Transform box, List<(string prop, Transform t)> placed, string id, float doorX = -99)
        {
            ExtraTris = 0; ExtraProps = 0; rng = new System.Random(id.GetHashCode());   // (its own random: the sims' stream is untouched)
            if (!ReadabilityFx.CryoDressing || MazeMap.W == 0) return;
            var root = new GameObject("Cryo dressing").transform; root.SetParent(box.parent, false);   // world metres (the room root's mirror is undone below)
            root.localScale = box.localScale;
            var islands = FindIslands();
            // which island each placed prop stands on (the door and walls' props have none)
            foreach (var (prop, t) in placed)
            {
                var p = new Vector2(t.position.x, t.position.z);
                foreach (var isl in islands) if (isl.world.Contains(p)) { isl.props.Add(t); break; }
            }
            var center = new Vector2(MazeMap.W * MazeMap.C * .5f, -MazeMap.H * MazeMap.C * .5f);   // the room centre (world x, z)
            var mats = new Mats3();
            foreach (var isl in islands)
            {
                if (isl.props.Count == 0) continue;
                bool alongX = isl.world.width >= isl.world.height;
                var ic = isl.world.center;
                // face the room: along the cross axis, toward the centre
                float yaw = alongX ? (center.y > ic.y ? 0f : 180f) : (center.x > ic.x ? 90f : 270f);
                string[] extras = null;
                foreach (var r in Recipes) { if (isl.props.Exists(t => PropOf(t) == r.has)) { isl.purpose = r.purpose; extras = r.extras; break; } }
                bool turret = isl.props.Exists(t => t.GetComponent<CryoHeroTurret>() != null);
                if (turret) { isl.purpose = "turret post"; extras = new[] { "cryo-supply-crate" }; }
                // the props already there: on the grid facing the room (the turrets keep their own aim; the crates a small jitter)
                foreach (var t in isl.props)
                {
                    if (t.GetComponent<CryoHeroTurret>() != null) continue;
                    float j = PropOf(t) == "cryo-supply-crate" ? Jit(4f) : 0f;
                    t.rotation = Quaternion.Euler(0, yaw + j, 0);
                }
                // fill the island's free run along its long axis
                if (extras != null) Fill(isl, alongX, yaw, extras, box);
                if (ReadabilityFx.TallIslands) Raise(root, isl, mats);
                // grounding and the glow pool
                foreach (var t in isl.props) Ground(t, root, mats);
                if (isl.purpose == "reactor core" || isl.purpose == "pod bay" || isl.purpose == "pump station") Disc(root, new Vector3(ic.x, .035f, ic.y), new Vector2(isl.world.width * .42f, isl.world.height * .42f), mats.glow);
                // the coolant inlay to the nearest wall
                Inlay(root, isl, mats);
                if (ReadabilityFx.TallIslands) SideModules(root, isl);
            }
            WallPipes(root, mats, doorX);
            if (ReadabilityFx.StandaloneTurrets) foreach (var (prop, t) in placed) if (t.GetComponent<CryoHeroTurret>() != null) Pad(root, t.position);
            // (bare pillars: a tower dressing was tried and dropped, it read as a speck at the run camera)
            if (ReadabilityFx.CryoWallVariety) Walls(root, islands, mats, doorX);
            Merge(root);
            Debug.Log($"CryoDressing: {islands.Count} islands, {ExtraProps} extra props (~{ExtraTris} tris): " + string.Join(", ", islands.FindAll(i => i.purpose != null).ConvertAll(i => i.purpose)));
        }

        static string PropOf(Transform t) { var n = t.name.Replace("(Clone)", ""); foreach (var k in propTris.Keys) if (n.StartsWith(k)) return k; var m = t.GetComponent<CryoPropMotion>(); return m != null ? m.Prop : n; }

        static List<Island> FindIslands()
        {
            var list = new List<Island>(); var seen = new bool[MazeMap.W, MazeMap.H];
            for (int z = 1; z < MazeMap.H - 1; z++) for (int x = 1; x < MazeMap.W - 1; x++)
            {
                if (seen[x, z] || MazeMap.Walkable(x, z)) continue;
                int x0 = x, x1 = x, z0 = z, z1 = z; var st = new Stack<Vector2Int>(); st.Push(new Vector2Int(x, z)); seen[x, z] = true; bool border = false;
                while (st.Count > 0)
                {
                    var c = st.Pop(); x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.y); z1 = Mathf.Max(z1, c.y);
                    foreach (var d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    {
                        var n = c + d; if (n.x < 0 || n.y < 0 || n.x >= MazeMap.W || n.y >= MazeMap.H) continue;
                        if (n.x == 0 || n.y == 0 || n.x == MazeMap.W - 1 || n.y == MazeMap.H - 1) { if (!MazeMap.Walkable(n.x, n.y)) border = true; continue; }
                        if (seen[n.x, n.y] || MazeMap.Walkable(n.x, n.y)) continue; seen[n.x, n.y] = true; st.Push(n);
                    }
                }
                if (border || (x1 - x0 + 1) * (z1 - z0 + 1) < 2) continue;
                // world rect (plane y = -world z)
                float C = MazeMap.C; var r = new Rect(x0 * C, -(z1 + 1) * C, (x1 - x0 + 1) * C, (z1 - z0 + 1) * C);
                list.Add(new Island { cells = new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1), world = r });
            }
            return list;
        }

        static Bounds WorldBounds(Transform t) { var rs = t.GetComponentsInChildren<Renderer>(); var b = new Bounds(t.position, Vector3.zero); bool f = true; foreach (var r in rs) { if (f) { b = r.bounds; f = false; } else b.Encapsulate(r.bounds); } return b; }

        static void Fill(Island isl, bool alongX, float yaw, string[] extras, Transform box)
        {
            const float inset = .55f, gap = .25f;
            float a0 = (alongX ? isl.world.xMin : isl.world.yMin) + inset, a1 = (alongX ? isl.world.xMax : isl.world.yMax) - inset;
            float cross = alongX ? isl.world.center.y : isl.world.center.x, depth = (alongX ? isl.world.height : isl.world.width) - 2 * inset;
            var used = new List<(float a, float b)>();
            foreach (var t in isl.props) { var b = WorldBounds(t); used.Add(alongX ? (b.min.x - gap, b.max.x + gap) : (b.min.z - gap, b.max.z + gap)); }
            foreach (var prop in extras)
            {
                if (ExtraTris + propTris[prop] > TriBudget) break;
                var src = Resources.Load<GameObject>("CryoProps/" + prop); if (src == null) continue;
                var go = Object.Instantiate(src, box, false); go.name = prop + " (dressing)";
                go.transform.rotation = Quaternion.Euler(0, yaw + (prop == "cryo-supply-crate" ? Jit(4f) : 0f), 0);
                var b0 = WorldBounds(go.transform); float s = Mathf.Min(1.15f, depth / Mathf.Max(.01f, alongX ? b0.size.z : b0.size.x), 2.4f / Mathf.Max(.01f, b0.size.y));
                go.transform.localScale *= s; var b = WorldBounds(go.transform); float w = (alongX ? b.size.x : b.size.z) + gap;
                // the first free slot from the end nearest the room centre's long-axis position... in reading order: from a0 upward
                float pos = float.NaN;
                for (float p = a0; p + w <= a1 + .001f; p += .1f) { bool ok = true; foreach (var u in used) if (p < u.b && p + w > u.a) { ok = false; break; } if (ok) { pos = p; break; } }
                if (float.IsNaN(pos)) { Object.Destroy(go); continue; }
                float c = pos + w * .5f; used.Add((pos, pos + w));
                var wpos = alongX ? new Vector3(c, .33f, cross) : new Vector3(cross, .33f, c);
                var off = go.transform.position - b.center; off.y = 0; go.transform.position = wpos + off;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var sm = r.sharedMaterials; for (int i = 0; i < sm.Length; i++) sm[i] = CryoProps.Mat(prop, sm[i] != null ? sm[i].name : ""); r.sharedMaterials = sm;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                }
                go.AddComponent<CryoPropMotion>().Init(prop);
                isl.props.Add(go.transform); ExtraTris += propTris[prop]; ExtraProps++;
            }
        }

        // ---------------------------------------------------------------- the pieces
        class Mats3
        {
            public Material shadow = Mats.Fade(new Color(.03f, .04f, .1f, .28f)), glow = Mats.Glow(new Color(.35f, .85f, 1f, .16f)),
                inlay = Mats.Glow(new Color(.4f, .92f, 1f, .75f)), inlayEdge = Mats.Fade(new Color(.08f, .14f, .32f, .7f)),
                pipe, line = Mats.Glow(new Color(.4f, .95f, 1f, 1f)), frost = Mats.Fade(new Color(.93f, .97f, 1f, .92f));
            public Mats3()
            {
                pipe = new Material(Shader.Find("Maze/CryoSoftSurface")) { name = "Dress_Pipe" }; pipe.SetColor("_BaseColor", new Color(.16f, .24f, .5f)); pipe.SetFloat("_UseVertexAO", 0); pipe.SetFloat("_Gloss", .5f);
                foreach (var m in new[] { shadow, glow, inlay, inlayEdge, line, frost }) m.enableInstancing = true;
            }
        }
        static Mesh quad;
        static GameObject Piece(Transform parent, string name, Mesh mesh, Material m, Vector3 pos, Vector3 scale, Quaternion rot, bool shadows = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, true); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = shadows; return go;
        }
        static Mesh Prim(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(g); return m; }
        static Mesh cyl, sph;
        static void Ground(Transform t, Transform root, Mats3 m)
        {
            var b = WorldBounds(t); if (b.size.x < .05f) return;
            Disc(root, new Vector3(b.center.x, b.min.y + .012f, b.center.z), new Vector2(b.size.x * .62f, b.size.z * .62f), m.shadow);
        }
        static void Disc(Transform root, Vector3 at, Vector2 r, Material m) { Piece(root, "Dress_Disc", ZoneHazard.Disc, m, at, new Vector3(r.x, 1, r.y), Quaternion.identity); }
        static void Inlay(Transform root, Island isl, Mats3 m)
        {
            // from the island's long edge nearest a wall, straight to that wall (flat on the floor: never an obstacle)
            float C = MazeMap.C, W = MazeMap.W * C, H = MazeMap.H * C; var r = isl.world;
            float dL = r.xMin - C, dR = W - C - r.xMax, dT = -C - r.yMax, dB = r.yMin + H - C;   // plane y is -world z: the far wall is at y = -C .. the near at -(H - C)
            float best = Mathf.Min(Mathf.Min(dL, dR), Mathf.Min(Mathf.Abs(dT), Mathf.Abs(dB)));
            Vector3 a, bb;
            if (best == dL) { a = new Vector3(r.xMin, 0, r.center.y); bb = new Vector3(C, 0, r.center.y); }
            else if (best == dR) { a = new Vector3(r.xMax, 0, r.center.y); bb = new Vector3(W - C, 0, r.center.y); }
            else if (best == Mathf.Abs(dT)) { a = new Vector3(r.center.x, 0, r.yMax); bb = new Vector3(r.center.x, 0, -C); }
            else { a = new Vector3(r.center.x, 0, r.yMin); bb = new Vector3(r.center.x, 0, -(H - C)); }
            float len = (bb - a).magnitude; if (len < .3f) return;
            var mid = (a + bb) * .5f; var rot = Quaternion.LookRotation((bb - a).normalized, Vector3.up) * Quaternion.Euler(90, 0, 0);
            quad ??= Prim(PrimitiveType.Quad);
            Piece(root, "Dress_InlayEdge", quad, m.inlayEdge, mid + Vector3.up * .021f, new Vector3(.38f, len, 1), rot);
            Piece(root, "Dress_Inlay", quad, m.inlay, mid + Vector3.up * .024f, new Vector3(.15f, len, 1), rot);
        }
        static void WallPipes(Transform root, Mats3 m, float doorX)
        {
            cyl ??= Prim(PrimitiveType.Cylinder); sph ??= Prim(PrimitiveType.Sphere);
            float C = MazeMap.C, W = MazeMap.W * C, H = MazeMap.H * C, inset = C + .14f;   // just in front of the wall face (no collider: never an obstacle)
            var sides = new[] { (new Vector3(inset, 0, -inset), new Vector3(W - inset, 0, -inset)), (new Vector3(W - inset, 0, -inset), new Vector3(W - inset, 0, -(H - inset))),
                                (new Vector3(W - inset, 0, -(H - inset)), new Vector3(inset, 0, -(H - inset))), (new Vector3(inset, 0, -(H - inset)), new Vector3(inset, 0, -inset)) };
            int si = 0;
            foreach (var (a, b) in sides)
            {
                var d = b - a; float len = d.magnitude; var rot = Quaternion.FromToRotation(Vector3.up, d.normalized);
                // the far wall (si 0) leaves a gap for the blast door's bay
                var runs = new List<(Vector3, Vector3)>();
                if (si == 0 && doorX > 0) { runs.Add((a, new Vector3(doorX - 2.8f, 0, a.z))); runs.Add((new Vector3(doorX + 2.8f, 0, a.z), b)); } else runs.Add((a, b));
                foreach (var (ra, rb) in runs)
                {
                    float rl = (rb - ra).magnitude; if (rl < .5f) continue; var mid = (ra + rb) * .5f;
                    Piece(root, "Dress_Pipe", cyl, m.pipe, mid + Vector3.up * .2f, new Vector3(.22f, rl * .5f, .22f), rot, true);
                    Piece(root, "Dress_PipeLine", cyl, m.line, mid + Vector3.up * .315f, new Vector3(.06f, rl * .5f, .06f), rot);
                    foreach (var end in new[] { ra, rb }) Piece(root, "Dress_PipeCap", sph, m.pipe, end + Vector3.up * .2f, Vector3.one * .3f, Quaternion.identity, true);
                }
                for (float t = 2f; t < len - 1f; t += 4f)
                {
                    var p = a + d.normalized * t; if (si == 0 && doorX > 0 && Mathf.Abs(p.x - doorX) < 3.2f) continue;
                    Piece(root, "Dress_Bracket", cyl, m.pipe, p + Vector3.up * .2f, new Vector3(.32f, .07f, .32f), rot, true);
                    // a frost drift at the wall foot every other bracket (on the wall side of the pipe)
                    if ((int)(t / 4f + si) % 2 == 0) { var inward = Vector3.Cross(Vector3.up, d.normalized); Piece(root, "Dress_Frost", sph, m.frost, p - inward * .12f + Vector3.up * .02f, new Vector3(1.1f, .16f, .45f), Quaternion.LookRotation(d.normalized)); }
                }
                si++;
            }
        }
        // a bare pillar (no props) dressed as a coolant tower: a glowing cyan band round it and a capped pipe stub on top
        static void Tower(Transform root, Island isl, Mats3 m)
        {
            cyl ??= Prim(PrimitiveType.Cylinder); quad ??= Prim(PrimitiveType.Quad);
            var r = isl.world; float hw = r.width * .5f - .12f, hd = r.height * .5f - .12f, y = .78f; var c = new Vector3(r.center.x, 0, r.center.y);
            foreach (var (o, rot, len) in new[] { (new Vector3(0, y, -hd - .01f), Quaternion.Euler(0, 0, 0), r.width - .5f), (new Vector3(0, y, hd + .01f), Quaternion.Euler(0, 180, 0), r.width - .5f),
                                                   (new Vector3(-hw - .01f, y, 0), Quaternion.Euler(0, 90, 0), r.height - .5f), (new Vector3(hw + .01f, y, 0), Quaternion.Euler(0, 270, 0), r.height - .5f) })
                Piece(root, "Dress_TowerBand", quad, m.line, c + o, new Vector3(len, .07f, 1), rot);
            Piece(root, "Dress_TowerStub", cyl, m.pipe, c + Vector3.up * 1.35f, new Vector3(.34f, .28f, .34f), Quaternion.identity, true);
            Piece(root, "Dress_TowerStubLine", cyl, m.line, c + Vector3.up * 1.5f, new Vector3(.36f, .03f, .36f), Quaternion.identity);
        }
    // every dressing piece merged into one mesh per material (a handful of draws instead of ~85)
        static void Merge(Transform root)
        {
            var groups = new Dictionary<(Material, bool), List<CombineInstance>>();
            var kids = new List<Transform>(); foreach (Transform k in root) kids.Add(k);
            var w2l = root.worldToLocalMatrix;
            foreach (var k in kids)
            {
                var mf = k.GetComponent<MeshFilter>(); var mr = k.GetComponent<MeshRenderer>(); if (mf == null || mr == null || mf.sharedMesh == null) continue;
                var key = (mr.sharedMaterial, mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<CombineInstance>();
                l.Add(new CombineInstance { mesh = mf.sharedMesh, transform = w2l * k.localToWorldMatrix });
                Object.Destroy(k.gameObject);
            }
            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = "Dressing_" + kv.Key.Item1.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject(mesh.name); go.transform.SetParent(root, false); go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = kv.Key.Item1;
                r.shadowCastingMode = kv.Key.Item2 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = kv.Key.Item2;
            }
        }
        // ---------------------------------------------------------------- wall variety (ReadabilityFx.CryoWallVariety)
        // modules on the inner faces of the far, left and right walls (the near wall faces away from the run camera), one every
        // 5 m, picked by the island nearest it: consoles by the reactor / pumps, frosted windows by the pod bay, vent racks by
        // storage and maintenance, otherwise a window / vent / lit panel rhythm; hazard trim either side of the blast door;
        // loader frames in the walkable corners; chunky coolant columns with a lit collar on the bare pillars
        static Material wNavy, wDark, wGlow, wWindow, wAmber, wHazard, wPale;
        static void WallMats()
        {
            if (wNavy != null) return;
            Material Surf(Color c, float gloss) { var m = new Material(Shader.Find("Maze/CryoSoftSurface")) { name = "Wall_" + c }; m.SetColor("_BaseColor", c); m.SetFloat("_UseVertexAO", 0); m.SetFloat("_Gloss", gloss); return m; }
            wNavy = Surf(new Color(.16f, .24f, .5f), .45f); wDark = Surf(new Color(.07f, .1f, .2f), .3f); wPale = Surf(new Color(.84f, .9f, .98f), .4f);
            wGlow = Mats.Glow(new Color(.4f, .95f, 1f, 1f)); wWindow = Mats.Glow(new Color(.62f, .88f, 1f, .78f)); wAmber = Mats.Glow(new Color(1f, .72f, .25f, 1f));
            var tex = new Texture2D(64, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 8; y++) for (int x = 0; x < 64; x++) tex.SetPixel(x, y, ((x + y * 2) / 8) % 2 == 0 ? new Color(1f, .78f, .15f) : new Color(.08f, .09f, .14f));
            tex.Apply(); wHazard = Mats.Unlit(Color.white, tex);
        }
        static Mesh cube;
        static void Box(Transform root, Material m, Vector3 c, Vector3 size, Quaternion rot, bool sh = true) { cube ??= Prim(PrimitiveType.Cube); Piece(root, "Wall_Box", cube, m, c, size, rot, sh); }
        static void Walls(Transform root, List<Island> islands, Mats3 mats, float doorX)
        {
            WallMats(); cyl ??= Prim(PrimitiveType.Cylinder); quad ??= Prim(PrimitiveType.Quad);
            float C = MazeMap.C, W = MazeMap.W * C, H = MazeMap.H * C;
            // (start, direction along the wall, inward normal) for the far, left and right walls; faces at the walkable edge
            var walls = new[] { (new Vector3(C, 0, -C), Vector3.right, Vector3.back, W - 2 * C), (new Vector3(C, 0, -(H - C)), Vector3.forward, Vector3.right, H - 2 * C), (new Vector3(W - C, 0, -(H - C)), Vector3.forward, Vector3.left, H - 2 * C) };
            int k = 0;
            foreach (var (start, along, inward, len) in walls)
            {
                var rot = Quaternion.LookRotation(inward, Vector3.up);
                for (float t = 2.5f; t < len - 1.5f; t += 5f, k++)
                {
                    var p = start + along * t;
                    if (along == Vector3.right && doorX > 0 && Mathf.Abs(p.x - doorX) < 3.4f) continue;   // the door bay
                    // the island nearest this spot decides the module
                    string near = null; float bd = 9f;
                    foreach (var isl in islands) { if (isl.purpose == null) continue; float d = (isl.world.center - new Vector2(p.x, p.z)).magnitude; if (d < bd) { bd = d; near = isl.purpose; } }
                    string kind = near == "reactor core" || near == "pump station" ? "console" : near == "pod bay" ? "window" : near == "storage" || near == "maintenance" || near == "coolant store" ? "rack" : new[] { "window", "vent", "panel" }[k % 3];
                    Module(root, kind, p + inward * .06f, rot, along);
                }
            }
            // hazard trim either side of the blast door
            if (doorX > 0) foreach (float sx in new[] { -2.75f, 2.75f })
                Piece(root, "Wall_Hazard", quad, wHazard, new Vector3(doorX + sx, .75f, -C - .13f), new Vector3(.32f, 1.4f, 1), Quaternion.Euler(0, 0, 90), false);
            // walkable interior corners: a loader frame against the wall and dock chevrons on the floor
            foreach (var (cx, cz, ix, iz) in new[] { (1, 1, 1, 1), (MazeMap.W - 2, 1, -1, 1), (1, MazeMap.H - 2, 1, -1), (MazeMap.W - 2, MazeMap.H - 2, -1, -1) })
            {
                if (!MazeMap.Walkable(cx, cz) || !MazeMap.Walkable(cx + ix, cz) || !MazeMap.Walkable(cx, cz + iz)) continue;
                var c = MazeMap.ToWorld(MazeMap.Center(cx, cz), 0); var back = new Vector3(-ix, 0, iz).normalized;   // toward the corner
                var wallP = c + new Vector3(-ix * (C * .5f - .2f), 0, iz * (C * .5f - .2f));
                foreach (var side in new[] { new Vector3(ix, 0, 0), new Vector3(0, 0, -iz) }) Box(root, wNavy, wallP + side * .55f + Vector3.up * .8f, new Vector3(.16f, 1.6f, .16f), Quaternion.identity);
                Box(root, wNavy, wallP + Vector3.up * 1.62f, new Vector3(1.3f, .14f, .14f), Quaternion.LookRotation(back) * Quaternion.Euler(0, 90, 0));
                Box(root, wGlow, wallP + Vector3.up * 1.62f - back * .08f, new Vector3(1.1f, .05f, .02f), Quaternion.LookRotation(back) * Quaternion.Euler(0, 90, 0), false);
                for (int i = 0; i < 3; i++)
                    Piece(root, "Wall_Chevron", quad, wHazard, c - back * (.2f + .45f * i) + Vector3.up * .022f, new Vector3(.9f, .14f, 1), Quaternion.LookRotation(Vector3.down, back), false);
            }
            // bare pillars: a lit collar and a chunky coolant column on top
            foreach (var isl in islands)
            {
                if (isl.props.Count > 0 || isl.cells.width < 2 || isl.cells.height < 2) continue;
                var c = new Vector3(isl.world.center.x, 0, isl.world.center.y); float w = isl.world.width - .1f, d = isl.world.height - .1f;
                Box(root, wNavy, c + Vector3.up * .62f, new Vector3(w, .34f, d), Quaternion.identity);
                Box(root, wGlow, c + Vector3.up * .62f, new Vector3(w + .04f, .07f, d + .04f), Quaternion.identity, false);
                Piece(root, "Wall_Column", cyl, wPale, c + Vector3.up * 1.9f, new Vector3(.8f, .7f, .8f), Quaternion.identity, true);
                Piece(root, "Wall_ColumnCap", cyl, wNavy, c + Vector3.up * 2.62f, new Vector3(.9f, .06f, .9f), Quaternion.identity, true);
                foreach (float y in new[] { 1.55f, 2.25f }) Piece(root, "Wall_ColumnRing", cyl, wGlow, c + Vector3.up * y, new Vector3(.84f, .035f, .84f), Quaternion.identity);
            }
        }
        static void Module(Transform root, string kind, Vector3 p, Quaternion rot, Vector3 along)
        {
            Vector3 L(float x, float y, float z) => p + rot * new Vector3(x, y, z);   // local: x along the wall, y up, z out of the wall
            switch (kind)
            {
                case "window":   // a frosted viewing window with a cold glow behind it
                    Box(root, wNavy, L(0, .95f, .05f), new Vector3(1.5f, .8f, .1f), rot);
                    Piece(root, "Wall_Glass", quad, wWindow, L(0, .95f, .105f), new Vector3(1.28f, .6f, 1), rot * Quaternion.Euler(0, 180, 0), false);
                    Box(root, wNavy, L(0, .95f, .11f), new Vector3(.06f, .6f, .03f), rot);
                    Box(root, wPale, L(0, .5f, .13f), new Vector3(1.55f, .08f, .16f), rot);   // sill
                    break;
                case "console":   // a wall console: a screen, an amber status light, a ledge
                    Box(root, wNavy, L(0, .85f, .08f), new Vector3(1.0f, .7f, .16f), rot);
                    Piece(root, "Wall_Screen", quad, wGlow, L(-.08f, .9f, .165f), new Vector3(.62f, .36f, 1), rot * Quaternion.Euler(0, 180, 0), false);
                    Box(root, wAmber, L(.36f, .9f, .17f), new Vector3(.09f, .09f, .03f), rot, false);
                    Box(root, wPale, L(0, .5f, .2f), new Vector3(1.05f, .07f, .3f), rot);
                    break;
                case "rack":   // a vent rack with a pipe junction box
                    Box(root, wDark, L(-.3f, .8f, .05f), new Vector3(.9f, .9f, .1f), rot);
                    for (int i = 0; i < 4; i++) Box(root, wPale, L(-.3f, .5f + .2f * i, .1f), new Vector3(.8f, .05f, .05f), rot);
                    Box(root, wNavy, L(.55f, .45f, .12f), new Vector3(.42f, .42f, .24f), rot);
                    Box(root, wGlow, L(.55f, .55f, .245f), new Vector3(.12f, .12f, .02f), rot, false);
                    break;
                case "vent":
                    Box(root, wDark, L(0, .75f, .05f), new Vector3(1.1f, .6f, .1f), rot);
                    for (int i = 0; i < 3; i++) Box(root, wPale, L(0, .58f + .17f * i, .1f), new Vector3(1.0f, .05f, .05f), rot);
                    break;
                default:   // a lit cyan panel
                    Box(root, wNavy, L(0, .9f, .05f), new Vector3(.34f, 1.2f, .1f), rot);
                    Box(root, wGlow, L(0, .9f, .105f), new Vector3(.12f, 1.0f, .02f), rot, false);
                    break;
            }
        }
        // ---------------------------------------------------------------- standalone turrets (ReadabilityFx.StandaloneTurrets)
        // the open-floor cell nearest the old post with a full ring of open neighbours (2 cells), plain floor ('.'), away from the
        // spawns and the hero's start; it becomes 'B' (solid, cover) so nothing walks through the emplacement
        static readonly List<Vector2Int> taken = new();
        public static void NewFloor() => taken.Clear();
        public static bool TurretSpot(float wx, float wz, out float x, out float z)
        {
            x = wx; z = wz; if (MazeMap.grid == null) return false;
            float C = MazeMap.C; var old = new Vector2(wx, -wz); Vector2Int best = new(-1, -1); float bd = 1e9f;
            for (int cz = 2; cz < MazeMap.H - 2; cz++) for (int cx = 2; cx < MazeMap.W - 2; cx++)
            {
                if (MazeMap.grid[cz, cx] != '.') continue; bool ok = true;
                for (int dz = -2; dz <= 2 && ok; dz++) for (int dx = -2; dx <= 2 && ok; dx++) if (!MazeMap.Walkable(cx + dx, cz + dz) || (Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1 && MazeMap.grid[cz + dz, cx + dx] != '.' && !(dx == 0 && dz == 0))) ok = false;
                if (!ok) continue;
                var c = MazeMap.Center(cx, cz);
                if ((c - MazeMap.playerSpawn).magnitude < 7f) continue;
                foreach (var sp in MazeMap.spawnPoints) if ((c - sp).magnitude < 6f) { ok = false; break; }
                foreach (var tk in taken) if (Mathf.Abs(tk.x - cx) + Mathf.Abs(tk.y - cz) < 6) { ok = false; break; }
                if (!ok) continue;
                float d = (c - old).magnitude; if (d < bd) { bd = d; best = new Vector2Int(cx, cz); }
            }
            if (best.x < 0) return false;
            MazeMap.grid[best.y, best.x] = 'B'; taken.Add(best);
            var w = MazeMap.Center(best.x, best.y); x = w.x; z = -w.y;
            if (Application.isEditor) Debug.Log($"CryoDressing: turret post moved from ({wx:0.0}, {wz:0.0}) to cell {best} ({x:0.0}, {z:0.0})");
            return true;
        }
        static void Pad(Transform root, Vector3 at)
        {
            WallMats(); cyl ??= Prim(PrimitiveType.Cylinder); quad ??= Prim(PrimitiveType.Quad);
            Piece(root, "Turret_Pad", cyl, wNavy, new Vector3(at.x, .05f, at.z), new Vector3(1.75f, .05f, 1.75f), Quaternion.identity, true);
            Piece(root, "Turret_PadRim", cyl, wPale, new Vector3(at.x, .04f, at.z), new Vector3(1.95f, .04f, 1.95f), Quaternion.identity, true);
            for (int i = 0; i < 4; i++)   // short hazard markings on the floor round the pad
            {
                var dir = Quaternion.Euler(0, 45 + 90 * i, 0) * Vector3.forward;
                Piece(root, "Turret_Mark", quad, wHazard, at + dir * 1.25f + Vector3.up * .022f, new Vector3(.55f, .16f, 1), Quaternion.LookRotation(Vector3.down, dir), false);
            }
        }

        // ---------------------------------------------------------------- tall islands (ReadabilityFx.TallIslands)
        // the island becomes a raised block in the walls' rounded style (ivory body, navy deck, the cyan line round it), ~1 m
        // tall; its machines stand on the deck. Footprint, collision and cover are the island's as before ('B' cells already
        // block movement and shots)
        const float Deck = 1.0f;
        static Material ivory;
        static void Raise(Transform root, Island isl, Mats3 m)
        {
            WallMats(); cube ??= Prim(PrimitiveType.Cube); cyl ??= Prim(PrimitiveType.Cylinder);
            if (ivory == null) { ivory = new Material(Shader.Find("Maze/CryoSoftSurface")) { name = "Island_Ivory" }; ivory.SetColor("_BaseColor", new Color(.9f, .94f, 1f)); ivory.SetFloat("_UseVertexAO", 0); ivory.SetFloat("_Gloss", .45f); }
            var r = isl.world; float w = r.width - .1f, d = r.height - .1f, rad = .38f, h = Deck; var c = new Vector3(r.center.x, 0, r.center.y);
            // a rounded block: two crossed boxes and four corner cylinders
            Piece(root, "Island_Body", cube, ivory, c + Vector3.up * h * .5f, new Vector3(w - 2 * rad, h, d), Quaternion.identity, true);
            Piece(root, "Island_Body", cube, ivory, c + Vector3.up * h * .5f, new Vector3(w, h, d - 2 * rad), Quaternion.identity, true);
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                Piece(root, "Island_Corner", cyl, ivory, c + new Vector3(sx * (w * .5f - rad), h * .5f, sz * (d * .5f - rad)), new Vector3(rad * 2, h * .5f, rad * 2), Quaternion.identity, true);
            Piece(root, "Island_Deck", cube, wNavy, c + Vector3.up * (h + .01f), new Vector3(w - .36f, .04f, d - .36f), Quaternion.identity, true);
            Piece(root, "Island_Base", cube, wNavy, c + Vector3.up * .08f, new Vector3(w + .06f, .16f, d - 2 * rad + .06f), Quaternion.identity, true);
            Piece(root, "Island_Base", cube, wNavy, c + Vector3.up * .08f, new Vector3(w - 2 * rad + .06f, .16f, d + .06f), Quaternion.identity, true);
            Piece(root, "Island_Line", cube, m.line, c + Vector3.up * (h * .62f), new Vector3(w - 2 * rad + .02f, .045f, d + .02f), Quaternion.identity);
            Piece(root, "Island_Line", cube, m.line, c + Vector3.up * (h * .62f), new Vector3(w + .02f, .045f, d - 2 * rad + .02f), Quaternion.identity);
            // the machines stand on the deck
            foreach (var t in isl.props) { var p = t.position; if (p.y < Deck) { p.y += Deck - .33f; t.position = p; } }
        }
        // the island's camera-facing side and its two ends get modules that match its purpose
        static void SideModules(Transform root, Island isl)
        {
            var r = isl.world; var c = new Vector3(r.center.x, 0, r.center.y);
            string kind = isl.purpose == "reactor core" || isl.purpose == "pump station" ? "console" : isl.purpose == "pod bay" ? "window" : isl.purpose == "turret post" ? "panel" : "rack";
            float s = .1f;   // modules are built for 1.3 m walls: scale their height to the 1 m deck by mounting them a little lower
            Module(root, kind, c + new Vector3(0, -s, -r.height * .5f + .02f), Quaternion.LookRotation(Vector3.back), Vector3.right);
            if (r.width >= 5f) foreach (float sx in new[] { -1f, 1f }) Module(root, "panel", c + new Vector3(sx * r.width * .5f - sx * .02f, -s, 0), Quaternion.LookRotation(new Vector3(sx, 0, 0)), Vector3.forward);
        }
    }
}
