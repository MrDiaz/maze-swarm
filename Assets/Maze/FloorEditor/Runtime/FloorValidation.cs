using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maze.FloorEditing
{
    public static partial class FloorDocuments
    {
        // Union of the current generated v2 maps and documented drop-zone legend. In v2 V is a conveyor.
        public const string Legend = "#BCr.~yPSXDHLZcoenm%TgK|><^Vk_Jwa*iuqFIRG$!:Q+@fdp";
        public static bool IsBaseSolid(char c) => c == '#' || c == 'B' || c == 'C' || c == 'r' || c == 'D' || c == '_' || c == '|' || c == 'm';
        static bool Walk(char c, bool eventual, bool enemy)
        {
            if (c == 'D') return true; // report routes with doors open; closed-room checks are separate
            if (eventual && (c == 'm' || c == '|')) return true;
            return !IsBaseSolid(c) && (!enemy || c != '*');
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool HasErrors(FloorValidation r) => !r.IsValid;

        public static FloorValidation Validate(FloorDocument doc)
        {
            var report = new FloorValidation();
            if (doc == null) { report.Error("document.null", "Floor document is missing."); return report; }
            if (doc.schemaVersion != SchemaVersion) report.Error("document.version", "Only schemaVersion 1 is supported.");
            if (string.IsNullOrEmpty(doc.layoutId) || !ZoneData.Layouts.ContainsKey(doc.layoutId)) report.Error("layout.unknown", "Select a canonical zone/layout ID.");
            if (!string.IsNullOrEmpty(doc.sectorId) || doc.floorNumber != 0)
            {
                var sector = SectorDefs.Get(doc.sectorId);
                if (sector?.floors == null || doc.floorNumber < 1 || doc.floorNumber > sector.floors.Length)
                    report.Error("floor.identity", "sectorId/floorNumber must identify an existing authored floor (or both be absent).");
                else if (sector.floors[doc.floorNumber - 1].layout != doc.layoutId)
                    report.Error("floor.layout", "This floor's canonical layout is " + sector.floors[doc.floorNumber - 1].layout + ".");
            }
            if (doc.title == null || doc.title.Length > 128 || doc.title.IndexOf('\0') >= 0) report.Error("title.invalid", "Title must be a string of at most 128 characters.");
            if (doc.rows == null || doc.rows.Length < 3 || doc.rows.Length > 256)
            { report.Error("rows.size", "Floor needs 3–256 rows."); return report; }
            int height = report.height = doc.rows.Length, width = report.width = doc.rows[0]?.Length ?? 0;
            if (width < 3 || width > 256) { report.Error("rows.width", "Floor needs 3–256 columns."); return report; }
            int player = -1, playerCount = 0, spawns = 0, jumps = 0;
            for (int z = 0; z < height; z++)
            {
                if (doc.rows[z] == null || doc.rows[z].Length != width) { report.Error("rows.rectangle", "All rows must have the same width.", -1, z); continue; }
                for (int x = 0; x < width; x++)
                {
                    char c = doc.rows[z][x];
                    if (Legend.IndexOf(c) < 0) report.Error("cell.unknown", "Unknown v2 marker '" + c + "'.", x, z);
                    if ((x == 0 || z == 0 || x == width - 1 || z == height - 1) && c != '#') report.Error("bounds.open", "Boundary cells must stay solid '#'.", x, z);
                    if (c == 'P') { player = z * width + x; playerCount++; }
                    if (c == 'S') spawns++;
                    if (c == 'J') jumps++;
                }
            }
            if (playerCount != 1) report.Error("spawn.player", "Exactly one player spawn P is required; found " + playerCount + ".");
            if (spawns == 0) report.Error("spawn.enemy", "At least one enemy spawn S is required.");
            if ((jumps & 1) != 0) report.Error("jump.unpaired", "Jump pads J must occur in pairs in reading order.");
            if (doc.objects == null || doc.objects.Length > 2048)
            { report.Error("objects.size", "Objects must be an array with at most 2048 entries."); return report; }
            // Shape/legend errors make path and footprint queries unsafe; return useful primary errors first.
            if (HasErrors(report)) return report;
            ValidateObjects(doc, report);
            string[] effective = BuildRows(doc);
            if (player < 0) return report;
            var seen = Flood(effective, player, false, false);
            var enemySeen = Flood(effective, player, false, true);
            var eventual = Flood(effective, player, true, false);
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                char c = effective[z][x]; int i = z * width + x;
                if (Walk(c, false, false)) { report.walkableCells++; if (seen[i]) report.reachableCells++; }
                if (c == 'S' && !enemySeen[i]) report.Error("spawn.sealed", "Enemy spawn is sealed from the player with doors open (including slag avoidance). Runtime would discard it.", x, z);
                if ("LZRG$!QH+@".IndexOf(c) >= 0)
                {
                    if (!eventual[i]) report.Error("objective.unreachable", "Marker " + c + " has no four-neighbour route from P even with doors/fences/hive walls open.", x, z);
                    else if (!seen[i]) report.Warn("objective.gated", "Marker " + c + " depends on a destructible wall or live fence; verify its unlock event in playtest.", x, z);
                }
                if (c == 'n' && !seen[i]) report.Error("nest.unreachable", "Hunt nest n is inaccessible with doors open.", x, z);
            }
            if (report.reachableCells < report.walkableCells) report.Warn("floor.islands", (report.walkableCells - report.reachableCells) + " traversable cell(s) lie outside the initial open-door component. Check the path overlay and mechanic unlocks.");
            ValidateDoors(effective, report);
            ValidateGoalMarkers(doc, report);
            // This is static four-neighbour grid analysis, not a NavMesh or actor-radius proof.
            return report;
        }

        static void ValidateObjects(FloorDocument doc, FloorValidation report)
        {
            var ids = new HashSet<string>(); var knownAssets = new HashSet<string>(AssetIds);
            foreach (var item in doc.objects)
            {
                if (item == null) { report.Error("object.null", "Objects cannot contain null entries."); continue; }
                string id = item.id;
                if (string.IsNullOrWhiteSpace(id) || id.Length > 96 || !ids.Add(id)) report.Error("object.id", "Object ID must be unique, non-empty and at most 96 characters.", id: id);
                if (item.name == null || item.name.Length > 128) report.Error("object.name", "Object name must be a string of at most 128 characters.", id: id);
                if (!knownAssets.Contains(item.assetId ?? "")) report.Error("object.asset", "Unknown integrated object asset: " + item.assetId, id: id);
                if (FloorModelCatalog.IsLocalId(item.assetId) && item.preserveBehavior) report.Error("object.behavior", "Local batch models are static previews; gameplay behavior must stay disabled.", id: id);
                bool finite = Finite(item.x) && Finite(item.y) && Finite(item.z) && Finite(item.yaw) && Finite(item.scale) && Finite(item.footprintWidth) && Finite(item.footprintDepth) && Finite(item.maxHeight);
                if (!finite) { report.Error("object.finite", "All transform and fit values must be finite.", id: id); continue; }
                if (item.x < 0 || item.z > 0 || item.x > report.width * CellSize || -item.z > report.height * CellSize || item.y < -1 || item.y > 12)
                    report.Error("object.bounds", "Object centre lies outside the floor or allowed height (-1 to 12 m).", id: id);
                if (item.scale < .1f || item.scale > 4f) report.Error("object.scale", "Uniform scale must be 0.1–4.", id: id);
                if (Math.Abs(item.yaw) > 36000) report.Error("object.rotation", "Yaw must stay within ±36000 degrees.", id: id);
                if (item.footprintWidth < 0 || item.footprintWidth > 32 || item.footprintDepth < 0 || item.footprintDepth > 32 || item.maxHeight < 0 || item.maxHeight > 16)
                    report.Error("object.fit", "Fit dimensions must be non-negative (width/depth ≤32 m, height ≤16 m).", id: id);
                if (item.collision != "none" && item.collision != "existing" && item.collision != "solid") report.Error("object.collision", "Collision must be none, existing or solid.", id: id);
                if (item.collision == "none") continue;
                if (item.footprintWidth <= 0 || item.footprintDepth <= 0)
                { report.Error("object.footprint", "Collision objects require positive fit width and depth.", id: id); continue; }
                if (item.scale < .1f || item.scale > 4f || item.footprintWidth > 32 || item.footprintDepth > 32) continue;
                foreach (var cell in FootprintCells(item))
                {
                    int x = cell.x, z = cell.y;
                    if (x <= 0 || z <= 0 || x >= report.width - 1 || z >= report.height - 1)
                    { report.Error("object.bounds", "Collision footprint touches or crosses the boundary wall.", x, z, id); continue; }
                    char c = doc.rows[z][x];
                    if (item.collision == "existing" && !IsBaseSolid(c)) report.Error("object.unbacked", "Existing-collision object crosses walkable cell " + c + ". Move it to a blocked footprint or choose Solid.", x, z, id);
                    if (item.collision == "solid" && ".~yBCr".IndexOf(c) < 0) report.Error("object.marker", "Solid object would overwrite protected marker '" + c + "'.", x, z, id);
                }
            }
            // One occupancy pass keeps interactive validation bounded even with large selections.
            var occupied = new Dictionary<Vector2Int, string>();
            foreach (var item in doc.objects)
            {
                if (item == null || item.collision == "none") continue;
                var warned = new HashSet<string>();
                foreach (var cell in FootprintCells(item))
                {
                    if (occupied.TryGetValue(cell, out var other))
                    { if (warned.Add(other)) report.Warn("object.overlap", "Collision footprint overlaps " + other + ".", id: item.id); }
                    else occupied[cell] = item.id ?? "(unnamed)";
                }
            }
        }

        public static List<Vector2Int> FootprintCells(PlacedObject item)
        {
            var result = new List<Vector2Int>();
            if (item == null || !Finite(item.x) || !Finite(item.z) || !Finite(item.yaw) || !Finite(item.scale) || !Finite(item.footprintWidth) || !Finite(item.footprintDepth)) return result;
            float w = item.footprintWidth * item.scale, d = item.footprintDepth * item.scale;
            if (w <= 0 || d <= 0 || w > 128 || d > 128 || Math.Abs(item.x) > 4096 || Math.Abs(item.z) > 4096) return result;
            float rad = (item.legacyFit ? 0 : item.yaw) * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float ex = Math.Abs(cos) * w / 2 + Math.Abs(sin) * d / 2;
            float ey = Math.Abs(sin) * w / 2 + Math.Abs(cos) * d / 2;
            float px = item.x, py = -item.z;
            int left = Mathf.FloorToInt((px - ex + .0001f) / CellSize), right = Mathf.FloorToInt((px + ex - .0001f) / CellSize);
            int top = Mathf.FloorToInt((py - ey + .0001f) / CellSize), bottom = Mathf.FloorToInt((py + ey - .0001f) / CellSize);
            for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
            {
                float dx = (x + .5f) * CellSize - px, dy = (y + .5f) * CellSize - py;
                // Separating axes of the grid cell and the rotated object footprint.
                if (Math.Abs(dx) >= ex + CellSize / 2 - .0001f || Math.Abs(dy) >= ey + CellSize / 2 - .0001f) continue;
                float u = dx * cos + dy * sin, v = -dx * sin + dy * cos;
                float cellProjection = (Math.Abs(cos) + Math.Abs(sin)) * CellSize / 2;
                if (Math.Abs(u) >= w / 2 + cellProjection - .0001f || Math.Abs(v) >= d / 2 + cellProjection - .0001f) continue;
                result.Add(new Vector2Int(x, y));
            }
            return result;
        }

        public static bool[] ReachableCells(FloorDocument doc, bool enemyPaths = false)
        {
            if (doc?.rows == null || doc.rows.Length == 0 || doc.rows[0] == null) return Array.Empty<bool>();
            var rows = BuildRows(doc); int width = rows[0].Length;
            for (int z = 0; z < rows.Length; z++)
            {
                if (rows[z] == null || rows[z].Length != width) return Array.Empty<bool>();
                for (int x = 0; x < width; x++) if (rows[z][x] == 'P') return Flood(rows, z * width + x, false, enemyPaths);
            }
            return new bool[width * rows.Length];
        }
        static bool[] Flood(string[] rows, int start, bool eventual, bool enemy)
        {
            int w = rows[0].Length, h = rows.Length; var seen = new bool[w * h]; var queue = new Queue<int>();
            if (start < 0 || start >= seen.Length) return seen;
            seen[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(), x = at % w, y = at / w;
                foreach (int next in new[] { x > 0 ? at - 1 : -1, x < w - 1 ? at + 1 : -1, y > 0 ? at - w : -1, y < h - 1 ? at + w : -1 })
                {
                    if (next < 0 || seen[next] || !Walk(rows[next / w][next % w], eventual, enemy)) continue;
                    seen[next] = true; queue.Enqueue(next);
                }
            }
            return seen;
        }
        static void ValidateDoors(string[] rows, FloorValidation report)
        {
            int w = rows[0].Length, h = rows.Length; var visited = new HashSet<int>();
            for (int z = 1; z < h - 1; z++) for (int x = 1; x < w - 1; x++)
            {
                int first = z * w + x; if (rows[z][x] != 'D' || !visited.Add(first)) continue;
                var pending = new Queue<int>(); pending.Enqueue(first); int minx = x, maxx = x, miny = z, maxy = z, count = 0;
                while (pending.Count > 0)
                {
                    int p = pending.Dequeue(), xx = p % w, yy = p / w; count++;
                    minx = Math.Min(minx, xx); maxx = Math.Max(maxx, xx); miny = Math.Min(miny, yy); maxy = Math.Max(maxy, yy);
                    foreach (int n in new[] { p - 1, p + 1, p - w, p + w })
                        if (n >= 0 && n < w * h && rows[n / w][n % w] == 'D' && visited.Add(n)) pending.Enqueue(n);
                }
                if (count != (maxx - minx + 1) * (maxy - miny + 1) || (maxx > minx && maxy > miny))
                { report.Error("door.shape", "Door cells must form one straight, one-cell-thick span.", x, z); continue; }
                bool alongX = maxx > minx;
                if (maxx == minx && maxy == miny)
                    alongX = IsBaseSolid(rows[z][x - 1]) && IsBaseSolid(rows[z][x + 1]);
                bool anchors = alongX ? IsBaseSolid(rows[miny][minx - 1]) && IsBaseSolid(rows[miny][maxx + 1]) : IsBaseSolid(rows[miny - 1][minx]) && IsBaseSolid(rows[maxy + 1][minx]);
                if (!anchors) report.Error("door.anchor", "Door span must meet a solid wall at both ends.", x, z);
                bool sideA = false, sideB = false;
                if (alongX) for (int xx = minx; xx <= maxx; xx++) { sideA |= Walk(rows[miny - 1][xx], false, false); sideB |= Walk(rows[maxy + 1][xx], false, false); }
                else for (int yy = miny; yy <= maxy; yy++) { sideA |= Walk(rows[yy][minx - 1], false, false); sideB |= Walk(rows[yy][maxx + 1], false, false); }
                if (!sideA || !sideB) report.Error("door.passage", "Door needs traversable floor on both sides of its opening.", x, z);
            }
        }
        static void ValidateGoalMarkers(FloorDocument doc, FloorValidation report)
        {
            if (!ZoneData.Layouts.TryGetValue(doc.layoutId, out var original)) return;
            string old = string.Concat(original), now = string.Concat(doc.rows);
            // Existing scripts target these letters by index or group; deleting the final one silently breaks them.
            foreach (char marker in "LZRG$!Q:@nH")
                if (old.IndexOf(marker) >= 0 && now.IndexOf(marker) < 0)
                    report.Error("objective.removed", "Layout's existing objective/script marker '" + marker + "' was removed entirely; floor mode and scripts are unchanged.");
            if (old.IndexOf('D') >= 0 && now.IndexOf('D') < 0) report.Warn("door.removed", "All authored blast doors were removed. Verify room sequencing and floor events.");
            if (old.IndexOf('J') >= 0 && now.IndexOf('J') < 0) report.Warn("jump.removed", "All jump pads were removed. Verify routes formerly relying on paired pads.");
        }
    }
}
