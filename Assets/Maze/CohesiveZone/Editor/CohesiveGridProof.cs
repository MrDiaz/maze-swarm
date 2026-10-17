#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Maze.FloorEditing;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Maze
{
    // Explicit, non-rendering proof. No automatic hook, scene/object creation, Prefs or Progress access.
    public static class CohesiveGridProof
    {
        const float Epsilon = .0015f;
        [Serializable] sealed class Baseline { public string baselineUtc, project, sourceManifestSha256; public BaselineFile[] files; public FloorIdentity[] floors; }
        [Serializable] sealed class BaselineFile { public string path, label, sha256, text; }
        [Serializable] sealed class FloorIdentity { public int floor; public string layoutId, goal, originalRowsSha256, floorAssetPath, floorAssetSha256; }
        [Serializable] sealed class SourceCheck { public string path, label, baselineSha256, currentFullSha256, mode; public bool same; }
        [Serializable] sealed class Glyph { public string glyph; public int count; public Vector2Int[] cells; }
        [Serializable] sealed class RectProof { public string kind; public RectInt cells; public Vector3 centerWorld, footprintWorld; }
        [Serializable] sealed class DoorProof { public RectInt cells; public int from, to; public Vector2 center; }
        [Serializable] sealed class Target
        {
            public string kind; public Vector2 plane; public Vector3 world; public int cell;
            public int closedDoorDistance, allDoorsOpenDistance, withBarrelsDistance;
        }
        [Serializable] sealed class ProbeFailure { public string reason, glyph; public Vector2Int cell; public Vector2 before, after; public float clearance; }
        [Serializable] sealed class ProbeProof
        {
            public float radius; public int adjacentFaces, samples, skippedAmbiguousStarts, probes, wallProbes, cargoProbes, failures;
            public float minimumClearance = 1000000; public List<ProbeFailure> examples = new List<ProbeFailure>();
        }
        [Serializable] sealed class RouteProof
        {
            public string target; public float radius, traveled; public int steps; public bool reached;
            public Vector2 start, end; public string failure;
        }
        [Serializable] sealed class FloorProof
        {
            public int floor, mapIndex, width, height, sourcePCount, walkableCells, flowOpenCells, closedReachableCells, openReachableCells, barrelReachableCells;
            public string sectorId, layoutId, goal, rowsSha256, gridSha256, tagsSha256, floorResource, floorAssetPath, floorAssetSha256;
            public string[] effectiveRows, gridRows, tagRows, authoredObjectIds;
            public bool hasFloorDocument, rowsDifferFromOriginal; public float cellMetres; public Vector2 worldSize, spawnPlane; public Vector3 spawnWorld;
            public List<Glyph> glyphs = new List<Glyph>(), marks = new List<Glyph>();
            public List<RectProof> collisionFootprints = new List<RectProof>();
            public List<DoorProof> doors = new List<DoorProof>();
            public List<Target> criticalTargets = new List<Target>();
            public List<ProbeProof> collision = new List<ProbeProof>();
            public List<RouteProof> routes = new List<RouteProof>();
            public List<string> limits = new List<string>();
        }
        [Serializable] sealed class Report
        {
            public string utc, baselineUtc, baselineManifestSha256, project, scope;
            public bool passed, staticStateRestored, unityBatchMode;
            public int originalMapIndex, restoredMapIndex; public string originalSectorId, restoredSectorId; public int originalFloor, restoredFloor;
            public CameraTuning defaultCamera;
            public List<SourceCheck> sources = new List<SourceCheck>();
            public List<FloorProof> floors = new List<FloorProof>();
            public List<string> errors = new List<string>();
            public string[] limits;
        }

        [MenuItem("Maze/Cohesive Zone/Non-rendering grid route proof")]
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("COHESIVE_OUT");
            string manifest = Environment.GetEnvironmentVariable("COHESIVE_GRID_BASELINE");
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(manifest))
                throw new BuildFailedException("Set COHESIVE_OUT and COHESIVE_GRID_BASELINE before running the proof.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
                throw new BuildFailedException("Run outside Play mode; the proof temporarily replaces static map data.");
            var report = new Report {
                utc = DateTime.UtcNow.ToString("o"), project = Directory.GetParent(Application.dataPath).FullName,
                unityBatchMode = Application.isBatchMode,
                scope = "Authoritative first-sector grid, objective anchors, existing flow and circle collision; no rendering, scenes, objects, preferences or progression writes.",
                limits = new[] {
                    "Grid and query proof only. Imported mesh fit, camera visibility, animation, gameplay input, sound and final rendered presentation require separate review.",
                    "Closed-door reachability is recorded separately; all-door-open reachability is mandatory. Opening every door here is a static probe, not a change to gameplay door policy.",
                    "Barrel mask is reconstructed from authored barrelSpots using the same cell occupancy contract as Barrels.Rebuild. Destructible barrel circle collisions and runtime hazard/event changes are outside this static map proof.",
                    "Radius routes use existing StepAlong and CollideGrid in 0.06 metre steps. They verify static clearance, not continuous collision at arbitrary velocities or full enemy steering.",
                    "Camera data is the current class default plus exact source/scene comparison to the initial task snapshot. Saved user camera preferences are neither read nor written."
                }
            };
            // Capture before even resolving the sector: Get initializes a static data cache.
            var mapState = new StaticState(typeof(MazeMap));
            var documentState = new StaticState(typeof(FloorDocuments));
            var sectorState = new StaticState(typeof(SectorDefs));
            report.originalMapIndex = MazeMap.MapIndex;
            report.originalSectorId = FloorDocuments.RuntimeSectorId; report.originalFloor = FloorDocuments.RuntimeFloorNumber;
            try
            {
                if (FloorDocuments.PlaytestActive) throw new InvalidOperationException("An active playtest override must be parked before proof of persisted runtime floor contexts.");
                var baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(manifest));
                if (baseline == null || baseline.files == null || baseline.floors == null || baseline.floors.Length != 5)
                    throw new InvalidDataException("Invalid initial-snapshot baseline manifest.");
                report.baselineUtc = baseline.baselineUtc; report.baselineManifestSha256 = Hash(File.ReadAllBytes(manifest));
                CompareSources(report, baseline);
                report.defaultCamera = new CameraTuning();
                var sector = SectorDefs.Get("cryo-station");
                Require(report, sector != null && sector.id == "cryo-station" && sector.floors != null && sector.floors.Length == 5, "First-sector ID/floor count changed.");
                if (sector == null || sector.floors == null || sector.floors.Length != 5) throw new InvalidDataException("Cannot inspect first-sector floors.");
                for (int n = 0; n < sector.floors.Length; n++)
                {
                    var expected = baseline.floors[n]; var floor = sector.floors[n];
                    Require(report, expected.floor == n + 1 && floor.layout == expected.layoutId && floor.goal.ToString() == expected.goal,
                        "Floor " + (n + 1) + " layout ID or goal differs from initial snapshot.");
                    InspectFloor(report, sector.id, n + 1, floor, expected);
                }
            }
            catch (Exception e) { report.errors.Add(e.ToString()); }
            finally
            {
                try
                {
                    // Never Init the old map: doing so would reset its old door state/array identity.
                    documentState.Restore(); mapState.Restore(); sectorState.Restore();
                    report.restoredMapIndex = MazeMap.MapIndex;
                    report.restoredSectorId = FloorDocuments.RuntimeSectorId; report.restoredFloor = FloorDocuments.RuntimeFloorNumber;
                    report.staticStateRestored = documentState.Matches() && mapState.Matches() && sectorState.Matches();
                    Require(report, report.staticStateRestored, "Static map/context/cache restoration failed.");
                }
                catch (Exception e) { report.errors.Add("Restoration: " + e); }
                report.passed = report.errors.Count == 0 && report.staticStateRestored && report.floors.Count == 5;
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "cohesive-grid-route-proof.json"), JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
            }
            if (!report.passed) throw new BuildFailedException("Cohesive grid proof failed; inspect cohesive-grid-route-proof.json: " + string.Join("; ", report.errors));
            Debug.Log("Cohesive grid proof passed: five floors; static map, runtime context and caches restored. Receipt: " + output);
        }

        static void CompareSources(Report report, Baseline baseline)
        {
            foreach (var item in baseline.files)
            {
                string path = Path.GetFullPath(Path.Combine(report.project, item.path));
                if (!path.StartsWith(report.project + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Baseline path escaped project.");
                byte[] bytes = File.ReadAllBytes(path);
                // JsonUtility may deserialize an absent/null optional string as empty.
                bool wholeFile = string.IsNullOrEmpty(item.text);
                var result = new SourceCheck { path = item.path, label = item.label, baselineSha256 = item.sha256, currentFullSha256 = Hash(bytes),
                    mode = wholeFile ? "complete bytes SHA256" : "exact camera source slice" };
                result.same = wholeFile ? result.currentFullSha256 == item.sha256 :
                    Hash(Encoding.UTF8.GetBytes(item.text)) == item.sha256 && Encoding.UTF8.GetString(bytes).Contains(item.text);
                report.sources.Add(result); Require(report, result.same, "Initial snapshot mismatch: " + item.path + " / " + item.label);
            }
        }

        static void InspectFloor(Report report, string sector, int number, ZoneFloor floor, FloorIdentity expected)
        {
            FloorDocuments.SetRuntimeContext(sector, number);
            int index = MazeMap.ZoneLayoutIndex(floor.layout);
            if (index < MazeMap.ZoneBase) throw new InvalidDataException("Unresolved floor layout " + floor.layout);
            MazeMap.MapIndex = index;
            var record = new FloorProof { sectorId = sector, floor = number, layoutId = floor.layout, goal = floor.goal.ToString(), mapIndex = index,
                floorResource = FloorDocuments.FloorResourceRoot + sector + "/" + number };
            report.floors.Add(record);
            var textAsset = Resources.Load<TextAsset>(record.floorResource);
            if (textAsset != null) { record.floorAssetPath = AssetDatabase.GetAssetPath(textAsset); record.floorAssetSha256 = Hash(textAsset.bytes); }
            Require(report, string.Equals(record.floorAssetPath ?? string.Empty, expected.floorAssetPath ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(record.floorAssetSha256 ?? string.Empty, expected.floorAssetSha256 ?? string.Empty, StringComparison.Ordinal),
                "Floor " + number + " persisted runtime FloorDocument differs from initial snapshot.");
            record.hasFloorDocument = FloorDocuments.TryGetOverride(floor.layout, out var document);
            Require(report, textAsset == null || record.hasFloorDocument, "Floor " + number + " runtime FloorDocument exists but was rejected.");
            if (record.hasFloorDocument)
            {
                Require(report, document.sectorId == sector && document.floorNumber == number && document.layoutId == floor.layout, "FloorDocument identity mismatch on floor " + number);
                record.rowsDifferFromOriginal = FloorDocuments.RowsDifferFromOriginal(document);
                record.authoredObjectIds = (document.objects ?? Array.Empty<PlacedObject>()).Select(p => p?.id).ToArray();
            }
            else { record.authoredObjectIds = Array.Empty<string>(); record.limits.Add("No floor-scoped FloorDocument asset; actual runtime API selected original ZoneData rows."); }
            record.effectiveRows = (string[])MazeMap.Rows.Clone(); record.rowsSha256 = HashRows(record.effectiveRows);
            if (textAsset == null) Require(report, record.rowsSha256 == expected.originalRowsSha256, "Floor " + number + " effective fallback rows differ from initial snapshot.");
            MazeMap.Init();
            record.width = MazeMap.W; record.height = MazeMap.H; record.cellMetres = MazeMap.C;
            record.worldSize = new Vector2(MazeMap.W * MazeMap.C, MazeMap.H * MazeMap.C);
            record.spawnPlane = MazeMap.playerSpawn; record.spawnWorld = MazeMap.ToWorld(MazeMap.playerSpawn);
            record.gridRows = GridRows(MazeMap.grid); record.tagRows = GridRows(MazeMap.tag);
            record.gridSha256 = HashRows(record.gridRows); record.tagsSha256 = HashRows(record.tagRows);
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++)
            {
                if (MazeMap.tag[z, x] == 'P') record.sourcePCount++;
                if (MazeMap.Walkable(x, z)) record.walkableCells++;
                if (MazeMap.FlowOpen(x, z)) record.flowOpenCells++;
            }
            Require(report, record.sourcePCount == 1, "Floor " + number + " needs exactly one preserved player spawn mark.");
            Require(report, CircleClearance(MazeMap.playerSpawn) + Epsilon >= .48f, "Floor " + number + " spawn lacks 0.48 metre clearance.");
            record.glyphs = Glyphs(MazeMap.tag);
            foreach (var key in MazeMap.marks.Keys.OrderBy(c => c)) record.marks.Add(new Glyph { glyph = key.ToString(), count = MazeMap.marks[key].Count, cells = MazeMap.marks[key].ToArray() });
            VerifyFootprints(report, record);
            foreach (var d in MazeMap.doors) record.doors.Add(new DoorProof { cells = d.r, center = d.center, from = d.from, to = d.to });
            AddTargets(record, "spawn", MazeMap.spawnPoints); AddTargets(record, "nest", MazeMap.nestSpots); AddTargets(record, "zone", MazeMap.zoneSpots);
            AddTargets(record, "exit", MazeMap.exitCells);
            if (MazeMap.hasBossSpot) AddTarget(record, "boss", MazeMap.bossSpot);
            if (floor.goal == Game.Goal.Hunt) Require(report, MazeMap.nestSpots.Count > 0, "Hunt floor lost nest anchors.");
            if (floor.goal == Game.Goal.Hold) Require(report, MazeMap.zoneSpots.Count > 0, "Hold floor lost zone anchors.");
            if (floor.goal == Game.Goal.Boss) Require(report, MazeMap.hasBossSpot, "Boss floor lost boss anchor.");
            MazeMap.ComputeFlow(MazeMap.playerSpawn);
            record.closedReachableCells = MazeMap.flow.Count(v => v >= 0);
            foreach (var target in record.criticalTargets) target.closedDoorDistance = Distance(MazeMap.flow, target.cell);
            foreach (float radius in new[] { .42f, .48f })
            {
                var proof = ProbeBoundaries(radius); record.collision.Add(proof);
                Require(report, proof.probes >= 200, "Floor " + number + " / radius " + radius + " produced fewer than 200 nonambiguous boundary probes.");
                Require(report, proof.wallProbes > 0 && proof.cargoProbes > 0, "Floor " + number + " / radius " + radius + " missed wall or cargo boundaries.");
                Require(report, proof.failures == 0, "Floor " + number + " / radius " + radius + " collision failures: " + proof.failures);
            }
            foreach (var d in MazeMap.doors) MazeMap.SetDoor(d, true);
            MazeMap.ComputeFlow(MazeMap.playerSpawn); var openField = (short[])MazeMap.flow.Clone();
            var queryField = new short[openField.Length]; MazeMap.FlowTo(MazeMap.playerSpawn, queryField);
            Require(report, openField.SequenceEqual(queryField), "Floor " + number + " ComputeFlow and FlowTo disagree for the same target.");
            record.openReachableCells = openField.Count(v => v >= 0);
            foreach (var target in record.criticalTargets)
            {
                target.allDoorsOpenDistance = Distance(openField, target.cell);
                Require(report, target.allDoorsOpenDistance >= 0, "Floor " + number + " unreachable critical " + target.kind + " at " + target.plane);
                if (target.closedDoorDistance < 0 && record.doors.Count == 0) Require(report, false, "Floor " + number + " has unreachable critical target without a door limit.");
                if (target.allDoorsOpenDistance < 0) continue;
                foreach (float radius in new[] { .42f, .48f })
                {
                    var route = WalkRoute(target, openField, radius); record.routes.Add(route);
                    Require(report, route.reached, "Floor " + number + " / radius " + radius + " route failed: " + target.kind + ": " + route.failure);
                }
            }
            foreach (var barrel in MazeMap.barrelSpots) { int cell = MazeMap.CellIndex(barrel); if (cell >= 0) MazeMap.barrelCells[cell]++; }
            MazeMap.ComputeFlow(MazeMap.playerSpawn); record.barrelReachableCells = MazeMap.flow.Count(v => v >= 0);
            foreach (var target in record.criticalTargets) target.withBarrelsDistance = Distance(MazeMap.flow, target.cell);
            Array.Clear(MazeMap.barrelCells, 0, MazeMap.barrelCells.Length);
            foreach (var d in MazeMap.doors) MazeMap.SetDoor(d, false);
            record.limits.Add(record.doors.Count == 0 ? "No door cells in this floor; closed and all-open route fields should be identical." : "Closed doors intentionally limit the initial flow field. Actual gameplay unlock/event order is not simulated.");
        }

        static void VerifyFootprints(Report report, FloorProof record)
        {
            var coverage = new int[MazeMap.W * MazeMap.H];
            foreach (var kind in new[] { "boundary", "cargo" })
            {
                var rects = kind == "boundary" ? MazeMap.Rects(c => c == '#') : MazeMap.Rects(c => c == 'B' || c == 'C' || c == 'r' || c == 'm');
                foreach (var rect in rects)
                {
                    record.collisionFootprints.Add(new RectProof { kind = kind, cells = rect,
                        centerWorld = MazeMap.ToWorld(new Vector2((rect.x + rect.width * .5f) * MazeMap.C, (rect.y + rect.height * .5f) * MazeMap.C)),
                        footprintWorld = new Vector3(rect.width * MazeMap.C, 0, rect.height * MazeMap.C) });
                    for (int z = rect.y; z < rect.yMax; z++) for (int x = rect.x; x < rect.xMax; x++) coverage[z * MazeMap.W + x]++;
                }
            }
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++)
            {
                char c = MazeMap.grid[z, x]; int expected = "#BCrm".IndexOf(c) >= 0 ? 1 : 0;
                Require(report, coverage[z * MazeMap.W + x] == expected, "Floor " + record.floor + " cargo/boundary rectangle misalignment at " + x + "," + z);
            }
            record.limits.Add("Footprints are real MazeMap.Rects results used by CargoEnvironment: exact solid-cell coverage checked independently. Actual instantiated mesh bounds are not measured by this non-rendering proof.");
        }

        static ProbeProof ProbeBoundaries(float radius)
        {
            var proof = new ProbeProof { radius = radius };
            var offsets = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };
            foreach (var offset in offsets)
                for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++)
                {
                    if (MazeMap.Walkable(x, z) || !MazeMap.Walkable(x + offset.x, z + offset.y)) continue;
                    proof.adjacentFaces++; Vector2 normal = offset;
                    foreach (float t in new[] { .2f, .5f, .8f })
                    {
                        proof.samples++;
                        var boundary = new Vector2((x + .5f) * MazeMap.C, (z + .5f) * MazeMap.C) + normal * (MazeMap.C * .5f);
                        boundary += new Vector2(-normal.y, normal.x) * ((t - .5f) * MazeMap.C);
                        var start = boundary + normal * (radius + .05f);
                        if (CircleClearance(start) + Epsilon < radius) { proof.skippedAmbiguousStarts++; continue; }
                        foreach (float fraction in new[] { .05f, .25f, .75f, 1.05f })
                        {
                            var before = boundary + normal * (radius * (1 - fraction)); var after = before;
                            MazeMap.CollideGrid(ref after, radius); proof.probes++;
                            if (MazeMap.grid[z, x] == '#') proof.wallProbes++;
                            if ("BCrm".IndexOf(MazeMap.grid[z, x]) >= 0) proof.cargoProbes++;
                            float clearance = CircleClearance(after); proof.minimumClearance = Mathf.Min(proof.minimumClearance, clearance);
                            string failure = !Finite(after) ? "nonfinite" : clearance + Epsilon < radius ? "circle remains in solid" :
                                Vector2.Dot(after - boundary, normal) < -Epsilon ? "escaped through wall" :
                                Vector2.Distance(after, start) > MazeMap.C * 1.5f ? "unexpected long displacement" : null;
                            if (failure == null) continue;
                            proof.failures++;
                            if (proof.examples.Count < 16) proof.examples.Add(new ProbeFailure { reason = failure, glyph = MazeMap.grid[z, x].ToString(), cell = new Vector2Int(x, z), before = before, after = after, clearance = clearance });
                        }
                    }
                }
            return proof;
        }

        // Independent geometry oracle: Euclidean distance from circle centre to union of solid cell AABBs.
        // It does not reproduce CollideGrid's push order, iteration count or escape-side selection.
        static float CircleClearance(Vector2 point)
        {
            if (!Finite(point)) return -1;
            float result = Mathf.Min(point.x, point.y, MazeMap.W * MazeMap.C - point.x, MazeMap.H * MazeMap.C - point.y);
            int cx = Mathf.FloorToInt(point.x / MazeMap.C), cz = Mathf.FloorToInt(point.y / MazeMap.C);
            for (int z = Math.Max(0, cz - 2); z <= Math.Min(MazeMap.H - 1, cz + 2); z++)
                for (int x = Math.Max(0, cx - 2); x <= Math.Min(MazeMap.W - 1, cx + 2); x++)
                {
                    if (MazeMap.Walkable(x, z)) continue;
                    float dx = Math.Max(Math.Max(x * MazeMap.C - point.x, 0), point.x - (x + 1) * MazeMap.C);
                    float dz = Math.Max(Math.Max(z * MazeMap.C - point.y, 0), point.y - (z + 1) * MazeMap.C);
                    result = Math.Min(result, Mathf.Sqrt(dx * dx + dz * dz));
                }
            return result;
        }

        static RouteProof WalkRoute(Target target, short[] field, float radius)
        {
            var result = new RouteProof { target = target.kind, radius = radius, start = target.plane };
            var p = target.plane; int stagnant = 0;
            for (int step = 0; step < 4000; step++)
            {
                if (Vector2.Distance(p, MazeMap.playerSpawn) <= .1f) { result.reached = true; break; }
                var previous = p; var direction = MazeMap.StepAlong(field, p, MazeMap.playerSpawn);
                p += direction * .06f; MazeMap.CollideGrid(ref p, radius); result.steps++;
                float moved = Vector2.Distance(p, previous); result.traveled += moved;
                if (!Finite(p) || CircleClearance(p) + Epsilon < radius) { result.failure = "collision clearance during actual flow descent"; break; }
                stagnant = moved < .0001f ? stagnant + 1 : 0;
                if (stagnant > 20 || direction.sqrMagnitude < .001f) { result.failure = "flow descent stalled"; break; }
            }
            result.end = p;
            if (!result.reached && result.failure == null) result.failure = "step limit exceeded";
            return result;
        }

        static void AddTargets(FloorProof record, string kind, List<Vector2> positions)
        { for (int i = 0; i < positions.Count; i++) AddTarget(record, kind + "-" + (i + 1), positions[i]); }
        static void AddTarget(FloorProof record, string kind, Vector2 p)
        { record.criticalTargets.Add(new Target { kind = kind, plane = p, world = MazeMap.ToWorld(p), cell = MazeMap.CellIndex(p) }); }
        static int Distance(short[] field, int cell) => cell < 0 || cell >= field.Length ? -1 : field[cell];
        static List<Glyph> Glyphs(char[,] grid)
        {
            var cells = new SortedDictionary<char, List<Vector2Int>>();
            for (int z = 0; z < grid.GetLength(0); z++) for (int x = 0; x < grid.GetLength(1); x++)
            { char c = grid[z, x]; if (!cells.TryGetValue(c, out var list)) cells[c] = list = new List<Vector2Int>(); list.Add(new Vector2Int(x, z)); }
            return cells.Select(kv => new Glyph { glyph = kv.Key.ToString(), count = kv.Value.Count, cells = kv.Value.ToArray() }).ToList();
        }
        static string[] GridRows(char[,] grid)
        {
            var rows = new string[grid.GetLength(0)];
            for (int z = 0; z < rows.Length; z++) { var row = new char[grid.GetLength(1)]; for (int x = 0; x < row.Length; x++) row[x] = grid[z, x]; rows[z] = new string(row); }
            return rows;
        }
        // LF-joined rows, no trailing newline. Receipt includes rows, so digest is independently reproducible.
        static string HashRows(string[] rows) => Hash(Encoding.UTF8.GetBytes(string.Join("\n", rows)));
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);
        static void Require(Report report, bool condition, string error) { if (!condition) report.errors.Add(error); }

        // Reflection is confined to saving/restoring exact pre-existing static references and collection contents.
        // MazeMap.Init replaces its arrays before touching them; old door objects and nested lists remain untouched.
        sealed class StaticState
        {
            sealed class Saved
            {
                public FieldInfo field; public object value; public object[] items; public DictionaryEntry[] entries;
            }
            readonly List<Saved> saved = new List<Saved>();
            public StaticState(Type type)
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.IsLiteral) continue;
                    var value = field.GetValue(null); var entry = new Saved { field = field, value = value };
                    if (value is IDictionary dictionary)
                    {
                        // IDictionary.GetEnumerator yields DictionaryEntry. IEnumerable on a generic
                        // Dictionary can instead yield KeyValuePair, so do not use LINQ Cast here.
                        var entries = new List<DictionaryEntry>(); foreach (DictionaryEntry item in dictionary) entries.Add(item);
                        entry.entries = entries.ToArray();
                    }
                    else if (value is IList list && !(value is Array) && !list.IsReadOnly && !list.IsFixedSize) entry.items = list.Cast<object>().ToArray();
                    saved.Add(entry);
                }
            }
            public void Restore()
            {
                foreach (var entry in saved)
                {
                    if (!entry.field.IsInitOnly) entry.field.SetValue(null, entry.value);
                    if (entry.entries != null) { var dictionary = (IDictionary)entry.value; dictionary.Clear(); foreach (var item in entry.entries) dictionary.Add(item.Key, item.Value); }
                    if (entry.items != null) { var list = (IList)entry.value; list.Clear(); foreach (var item in entry.items) list.Add(item); }
                }
            }
            public bool Matches()
            {
                foreach (var entry in saved)
                {
                    var now = entry.field.GetValue(null);
                    if (entry.field.FieldType.IsValueType ? !Equals(now, entry.value) : !ReferenceEquals(now, entry.value)) return false;
                    if (entry.entries != null)
                    {
                        var dictionary = (IDictionary)now; if (dictionary.Count != entry.entries.Length) return false;
                        foreach (var item in entry.entries) if (!dictionary.Contains(item.Key) || !Equals(dictionary[item.Key], item.Value)) return false;
                    }
                    if (entry.items != null && !((IList)now).Cast<object>().SequenceEqual(entry.items)) return false;
                }
                return true;
            }
        }
    }
}
#endif
