using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Maze.FloorEditing
{
    [Serializable]
    public sealed class FloorDocument
    {
        public int schemaVersion = 1;
        public string layoutId;
        // Optional floor identity: omit together for explicit legacy layout-scoped documents.
        public string sectorId;
        public int floorNumber;
        public string title;
        // Same top-row-first v2 legend and two-metre cells consumed by MazeMap.
        public string[] rows = Array.Empty<string>();
        public PlacedObject[] objects = Array.Empty<PlacedObject>();
    }

    [Serializable]
    public sealed class PlacedObject
    {
        public string id, assetId, name;
        // Unity world metres: x east, y height, negative z south (row axis).
        public float x, y, z, yaw, scale = 1f;
        // Fit dimensions BEFORE uniform scale. Zero dimensions retain the model's native size.
        public float footprintWidth, footprintDepth, maxHeight = 3.4f;
        // none = explicit visual only; existing = must cover blocked base cells;
        // solid = add conservative 'C' collision cells to the effective grid.
        public string collision = "none";
        // Imported anchors retain existing ally-turret and door/machine behavior; new props are decorative by default.
        public bool preserveBehavior;
        // Imported anchors fit their rotated world bounds; new props fit before yaw.
        public bool legacyFit;
    }

    [Serializable]
    public sealed class FloorIssue
    {
        public string severity, code, message, objectId;
        public int column = -1, row = -1;
        public FloorIssue(string level, string key, string text, int x = -1, int z = -1, string id = null)
        { severity = level; code = key; message = text; column = x; row = z; objectId = id; }
    }

    [Serializable]
    public sealed class FloorValidation
    {
        public List<FloorIssue> issues = new List<FloorIssue>();
        public int width, height, walkableCells, reachableCells;
        public bool IsValid { get { foreach (var i in issues) if (i.severity == "error") return false; return true; } }
        public int ErrorCount { get { int n = 0; foreach (var i in issues) if (i.severity == "error") n++; return n; } }
        public int WarningCount { get { int n = 0; foreach (var i in issues) if (i.severity == "warning") n++; return n; } }
        public void Error(string code, string message, int x = -1, int z = -1, string id = null)
        { issues.Add(new FloorIssue("error", code, message, x, z, id)); }
        public void Warn(string code, string message, int x = -1, int z = -1, string id = null)
        { issues.Add(new FloorIssue("warning", code, message, x, z, id)); }
        public override string ToString() => ErrorCount + " error(s), " + WarningCount + " warning(s)";
    }

    public static partial class FloorDocuments
    {
        public const int SchemaVersion = 1;
        public const float CellSize = 2f;
        public const string ResourceRoot = "FloorEditor/Layouts/";
        public const string FloorResourceRoot = "FloorEditor/Floors/";
        public static readonly string[] AssetIds = {
            "cryo-blast-door", "cryo-coolant-pumping-station", "cryo-cooling-reactor", "cryo-flamethrower-turret",
            "cryo-freeze-canister", "cryo-gun-turret", "cryo-low-coolant-tank", "cryo-maintenance-cart", "cryo-pod", "cryo-supply-crate",
            "local-20261016-charging-brute",
            "local-20261016-ranged-spitter",
            "local-20261016-nest-carrier",
            "local-20261016-cryo-relay-cabinet",
            "local-20261016-cryo-diagnostic-console",
            "local-20261016-cryo-ventilation-unit",
            "local-20261016-cryo-emergency-cabinet",
            "local-20261016-cryo-pipe-riser",
            "local-20261016-cryo-valve-manifold",
            "local-20261016-cryo-pipe-elbow",
            "local-20261016-cryo-trench-cover",
            "local-20261016-cryo-warning-bollard",
            "local-20261016-industrial-cover-barricade",
            "local-20261016-industrial-cable-spool",
            "local-20261016-industrial-parts-hopper",
            "local-20261016-industrial-battery-rack",
            "local-20261016-fungal-spore-vent",
            "local-20261016-fungal-biomass-boulder",
            "local-20261016-ruins-reinforced-pillar",
            "local-20261016-ruins-broken-wall-cover"
        };
        static FloorDocument playtest;
        static readonly Dictionary<string, FloorDocument> cache = new Dictionary<string, FloorDocument>();
        static readonly Dictionary<FloorDocument, string[]> effectiveRows = new Dictionary<FloorDocument, string[]>();
        public static string RuntimeSectorId { get; private set; }
        public static int RuntimeFloorNumber { get; private set; }
        public static void SetRuntimeContext(string sectorId, int floorNumber)
        { RuntimeSectorId = sectorId; RuntimeFloorNumber = floorNumber; ClearCache(); }
        public static void ClearRuntimeContext() => SetRuntimeContext(null, 0);
        public static void ClearCache() { cache.Clear(); effectiveRows.Clear(); }
        public static bool PlaytestActive => playtest != null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTransientPlaytest() { playtest = null; ClearRuntimeContext(); }
        [Serializable] sealed class Anchor { public string prop, name; public float x, y, z, yaw, fw, fd, maxh; }
        [Serializable] sealed class AnchorLayout { public string layout; public Anchor[] anchors; }

        public static FloorDocument Create(string layoutId, string sectorId = null, int floorNumber = 0)
        {
            if (layoutId == null || !ZoneData.Layouts.TryGetValue(layoutId, out var rows))
                throw new ArgumentException("Unknown canonical layout: " + layoutId, nameof(layoutId));
            var doc = new FloorDocument { layoutId = layoutId, sectorId = sectorId, floorNumber = floorNumber, title = layoutId, rows = NormalizeOriginal(rows) };
            if (layoutId.StartsWith("cryo-station/", StringComparison.Ordinal))
            {
                var source = Resources.Load<TextAsset>("CryoSoft/CryoSoftRoom_" + layoutId.Substring("cryo-station/".Length) + "_props");
                if (source != null)
                {
                    var anchors = JsonUtility.FromJson<AnchorLayout>(source.text)?.anchors;
                    if (anchors != null)
                    {
                        var objects = new List<PlacedObject>(); int index = 0;
                        foreach (var a in anchors) objects.Add(new PlacedObject {
                            id = "original-" + (++index).ToString("D3"), assetId = a.prop, name = a.name,
                            x = a.x, y = a.y, z = a.z, yaw = a.yaw, footprintWidth = a.fw,
                            footprintDepth = a.fd, maxHeight = a.maxh, collision = "none", scale = 1f, preserveBehavior = true, legacyFit = true
                        });
                        doc.objects = objects.ToArray();
                    }
                }
            }
            return doc;
        }

        // Mirrors MazeMap.Init's padding and forced boundary exactly; does not alter ZoneData.
        public static string[] NormalizeOriginal(string[] original)
        {
            if (original == null || original.Length == 0) return Array.Empty<string>();
            int width = 0; foreach (var row in original) width = Math.Max(width, row?.Length ?? 0);
            var rows = new string[original.Length];
            for (int y = 0; y < rows.Length; y++)
            {
                var a = (original[y] ?? "").PadRight(width, '.').ToCharArray();
                for (int x = 0; x < width; x++) if (x == 0 || y == 0 || x == width - 1 || y == rows.Length - 1) a[x] = '#';
                rows[y] = new string(a);
            }
            return rows;
        }

        public static FloorDocument Clone(FloorDocument doc) => doc == null ? null : JsonUtility.FromJson<FloorDocument>(JsonUtility.ToJson(doc));
        public static string ToJson(FloorDocument doc, bool pretty = true) => JsonUtility.ToJson(doc, pretty);
        public static bool TryParse(string json, out FloorDocument doc, out FloorValidation report)
        {
            doc = null; report = new FloorValidation();
            try
            {
                if (json == null || json.Length > 4 * 1024 * 1024) throw new FormatException("Document is empty or exceeds 4 MiB.");
                StrictFloorJson.Check(json);
                var parsed = JsonUtility.FromJson<FloorDocument>(json);
                report = Validate(parsed);
                if (!report.IsValid) return false;
                doc = parsed; return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is OverflowException)
            { report.Error("json.schema", e.Message); return false; }
        }

        public static void SetPlaytestOverride(FloorDocument doc)
        {
            var check = Validate(doc);
            if (!check.IsValid) throw new ArgumentException("Invalid playtest floor: " + check);
            playtest = Clone(doc); effectiveRows.Clear();
        }
        public static void ClearPlaytestOverride() { playtest = null; effectiveRows.Clear(); }
        public static bool TryGetOverride(string layoutId, out FloorDocument doc)
        {
            bool floorScoped = !string.IsNullOrEmpty(RuntimeSectorId) && RuntimeFloorNumber > 0;
            if (playtest != null && playtest.layoutId == layoutId && (!floorScoped || (playtest.sectorId == RuntimeSectorId && playtest.floorNumber == RuntimeFloorNumber)))
            { doc = playtest; return true; }
            doc = null;
            if (layoutId == null || !ZoneData.Layouts.ContainsKey(layoutId)) return false;
            // Context-aware gameplay never falls through to a layout-wide edit shared by other floors.
            string resource = floorScoped ? FloorResourceRoot + RuntimeSectorId + "/" + RuntimeFloorNumber : ResourceRoot + layoutId;
            if (cache.TryGetValue(resource, out var cached)) { doc = cached; return cached != null && cached.layoutId == layoutId; }
            var source = Resources.Load<TextAsset>(resource);
            if (source == null) { cache[resource] = null; return false; }
            if (TryParse(source.text, out var parsed, out var report) && parsed.layoutId == layoutId &&
                (!floorScoped || (parsed.sectorId == RuntimeSectorId && parsed.floorNumber == RuntimeFloorNumber)))
            { cache[resource] = parsed; doc = parsed; return true; }
            cache[resource] = null;
            Debug.LogError("Floor authoring override rejected for " + resource + ": " + report);
            return false;
        }
        public static string[] GetRowsOrOriginal(string layoutId, string[] original)
        {
            if (!TryGetOverride(layoutId, out var doc)) return original;
            // MazeMap asks for Rows for each cell while building. Rasterize a validated runtime
            // document once per context, rather than allocating an entire grid on every query.
            if (!effectiveRows.TryGetValue(doc, out var rows)) { rows = BuildRows(doc); effectiveRows[doc] = rows; }
            return rows;
        }
        public static bool RowsDifferFromOriginal(FloorDocument doc)
        {
            if (doc == null || !ZoneData.Layouts.TryGetValue(doc.layoutId ?? "", out var original)) return true;
            var a = NormalizeOriginal(original); var b = BuildRows(doc);
            if (a.Length != b.Length) return true;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return true;
            return false;
        }

        // Keeps the base grid recoverable when a solid object is moved or deleted.
        public static string[] BuildRows(FloorDocument doc)
        {
            if (doc?.rows == null) return Array.Empty<string>();
            var rows = (string[])doc.rows.Clone();
            var chars = new char[rows.Length][]; for (int r = 0; r < rows.Length; r++) chars[r] = (rows[r] ?? "").ToCharArray();
            foreach (var item in doc.objects ?? Array.Empty<PlacedObject>())
                if (item != null && item.collision == "solid")
                    foreach (var cell in FootprintCells(item))
                        if (cell.y > 0 && cell.y < chars.Length - 1 && cell.x > 0 && cell.x < chars[cell.y].Length - 1)
                        {
                            char c = chars[cell.y][cell.x];
                            if (c == '.' || c == '~' || c == 'y' || c == 'B' || c == 'C' || c == 'r') chars[cell.y][cell.x] = 'C';
                        }
            for (int r = 0; r < rows.Length; r++) rows[r] = new string(chars[r]);
            return rows;
        }

        // An explicit save only; no runtime auto-save, no PlayerPrefs, no zone source generation.
        // A failed replace never deletes/truncates the existing destination. Temp files use the same volume.
        public static bool TrySaveAtomic(string path, FloorDocument doc, out string error)
        {
            error = null; string temp = null;
            try
            {
                var report = Validate(doc); if (!report.IsValid) { error = "Validation failed: " + report; return false; }
                if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Save path must end in .json.");
                path = Path.GetFullPath(path); string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Save directory does not exist: " + directory);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to replace a linked file.");
                byte[] bytes = new UTF8Encoding(false).GetBytes(ToJson(doc) + "\n");
                temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                // Re-parse the durable candidate before replacing anything.
                if (!TryParse(File.ReadAllText(temp), out _, out var serialized)) throw new IOException("Serialized candidate rejected: " + serialized);
                if (File.Exists(path))
                {
                    string backup = path + "." + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".bak";
                    File.Replace(temp, path, backup);
                }
                else File.Move(temp, path);
                temp = null; ClearCache(); return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
            { error = e.Message; return false; }
            finally { if (temp != null) { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
        }
    }
}
