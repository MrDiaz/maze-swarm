using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maze.FloorEditing.Editor
{
    public static class FloorEditorSaveProof
    {
        [Serializable] sealed class Report { public string utc; public List<string> tests = new(), errors = new(); public bool productionFixtureRemoved; }
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("MAZE_FLOOR_SAVE_PROOF_OUTPUT") ?? "Library/MazeFloorEditor/QA/Save";
            Directory.CreateDirectory(output);
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            FloorEditorWindow window = null;
            string path = null; bool ownFile = false; var createdDirectories = new List<string>(); var originalBackups = new HashSet<string>();
            int priorMap = MazeMap.MapIndex; string priorSector = FloorDocuments.RuntimeSectorId; int priorFloor = FloorDocuments.RuntimeFloorNumber;
            void Check(bool pass, string text) { if (!pass) throw new InvalidOperationException(text); report.tests.Add("PASS " + text); }
            try
            {
                window = ScriptableObject.CreateInstance<FloorEditorWindow>(); window.CreateGUI(); window.Load("cryo-station", 1); path = window.SavePath;
                Check(!File.Exists(path), "production test target has no existing authored override; never overwrites user floor data");
                if (Directory.Exists(Path.GetDirectoryName(path))) originalBackups.UnionWith(Directory.GetFiles(Path.GetDirectoryName(path), "1.json.*.bak"));
                string directory = Path.GetDirectoryName(path); while (!Directory.Exists(directory)) { createdDirectories.Add(directory); directory = Path.GetDirectoryName(directory); }
                window.Edit(d => { d.title = "QA TEMPORARY AUTHORING SAVE — removed after verification"; var row = d.rows[1].ToCharArray(); int i = Array.IndexOf(row, '.'); if (i >= 0) row[i] = 'i'; d.rows[1] = new string(row); });
                Check(FloorDocuments.Validate(window.Document).IsValid, "temporary authoring draft validates before actual UI Save");
                ownFile = true; window.Save();
                Check(File.Exists(path), "actual window Save creates the missing per-zone directory and JSON");
                Check(!window.Dirty, "successful actual Save clears the dirty flag");
                string expected = FloorDocuments.ToJson(window.Document, false);
                window.Load("cryo-station", 1);
                Check(FloorDocuments.ToJson(window.Document, false) == expected, "window Reload reads the exact saved document");
                FloorDocuments.ClearCache(); FloorDocuments.SetRuntimeContext("cryo-station", 1);
                MazeMap.MapIndex = MazeMap.ZoneLayoutIndex(window.Document.layoutId); MazeMap.Init();
                Check(FloorDocuments.TryGetOverride(window.Document.layoutId, out var loaded) && loaded.title == window.Document.title, "ordinary Resources loader finds the actual saved floor-specific override");
                Check(MazeMap.Rows.SequenceEqual(FloorDocuments.BuildRows(window.Document)), "ordinary runtime MazeMap consumes rows saved by the authoring window");
                Check(ReferenceEquals(MazeMap.Rows, MazeMap.Rows), "repeated runtime row queries reuse one rasterized grid without per-cell allocation");
                FloorDocuments.SetRuntimeContext("cryo-station", 4);
                Check(!FloorDocuments.TryGetOverride(window.Document.layoutId, out _), "saving Cryo floor 1 does not override another floor sharing its layout");
                window.Edit(d => d.title += " second revision"); window.Save();
                Check(Directory.GetFiles(Path.GetDirectoryName(path), "1.json.*.bak").Count(f => !originalBackups.Contains(f)) == 1, "actual Save replacement preserves one complete previous revision");
                var external = FloorDocuments.Clone(window.Document); external.title = "QA external writer revision"; string externalJson = FloorDocuments.ToJson(external); File.WriteAllText(path, externalJson);
                window.Edit(d => d.title = "QA blocked stale editor revision"); window.Save();
                Check(File.ReadAllText(path) == externalJson && window.Dirty, "actual Save blocks an external-write conflict without overwriting or clearing dirty state");
            }
            catch (Exception error) { report.errors.Add(error.ToString()); }
            finally
            {
                if (ownFile && path != null)
                {
                    foreach (var file in (Directory.Exists(Path.GetDirectoryName(path)) ? Directory.GetFiles(Path.GetDirectoryName(path), "1.json.*.bak") : Array.Empty<string>()).Where(f => !originalBackups.Contains(f))) { File.Delete(file); if (File.Exists(file + ".meta")) File.Delete(file + ".meta"); }
                    AssetDatabase.DeleteAsset(path);
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                    foreach (var dir in createdDirectories) if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) { Directory.Delete(dir); if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta"); }
                    AssetDatabase.Refresh();
                    report.productionFixtureRemoved = !File.Exists(path);
                }
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                FloorDocuments.ClearCache(); if (string.IsNullOrEmpty(priorSector)) FloorDocuments.ClearRuntimeContext(); else FloorDocuments.SetRuntimeContext(priorSector, priorFloor);
                MazeMap.MapIndex = priorMap; MazeMap.Init();
                File.WriteAllText(Path.Combine(output, "save-proof.json"), JsonUtility.ToJson(report, true));
                if (Application.isBatchMode) EditorApplication.Exit(report.errors.Count == 0 && report.productionFixtureRemoved ? 0 : 1);
            }
        }
    }
}
