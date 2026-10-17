using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Maze.FloorEditing.Editor
{
    [InitializeOnLoad]
    public static class LocalBatchProof
    {
        const string Key = "Maze.LocalBatch20261016.Proof";
        static string Out => Environment.GetEnvironmentVariable("MAZE_BATCH_OUTPUT") ?? "Library/MazeFloorEditor/LocalBatchProof";
        [Serializable] sealed class Report { public string utc, prefsBefore, prefsAfter; public List<string> checks = new(), errors = new(); public bool preferencesRestored, fixtureRemoved; }
        static Report report;
        static double started, deadline;
        static bool observed, stopping;
        static LocalBatchProof() { if (SessionState.GetBool(Key, false)) { Load(); EditorApplication.update += Tick; } }
        static void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); report.checks.Add(text); }
        static void Save() => File.WriteAllText(Path.Combine(Out, "batch-proof.json"), JsonUtility.ToJson(report, true));
        static void Load() { if (report == null) report = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Out, "batch-proof.json"))); }
        public static FloorDocument Showcase()
        {
            var doc = FloorDocuments.Create("cryo-station/cryo-a", "cryo-station", 1);
            doc.title = "LOCAL BATCH — UNSAVED ASSET REVIEW";
            doc.objects = FloorModelCatalog.Entries.Select((m, i) => new PlacedObject {
                id = "qa-" + m.id, assetId = m.id, name = "QA " + m.id,
                x = 5 + i % 5 * 8, z = -(5 + i / 5 * 6), y = 0,
                yaw = i % 3 * 15, scale = 1, footprintWidth = m.sizeX, footprintDepth = m.sizeZ,
                maxHeight = m.sizeY, collision = "none", preserveBehavior = false, legacyFit = false
            }).ToArray();
            return doc;
        }
        public static void Run()
        {
            Directory.CreateDirectory(Out); report = new Report { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                var prefs = FloorPreferenceProtection.Capture(); report.prefsBefore = FloorPreferenceProtection.Digest(prefs);
                File.WriteAllText(Path.Combine(Out, "prefs-before.private.json"), JsonUtility.ToJson(prefs, true));
                Check(FloorModelCatalog.Entries.Length == 20 && FloorModelCatalog.Entries.Count(m => m.unrigged) == 3, "Twenty local models, including three clearly classified unrigged previews");
                var root = new GameObject("Local batch proof owner");
                try
                {
                    foreach (var m in FloorModelCatalog.Entries)
                    {
                        Check(FloorDocuments.AssetIds.Contains(m.id) && FloorObjectVisuals.ResourceFor(m.id) == m.resourcePath, m.slug + " stable ID resolves through existing placement factory");
                        var prefab = Resources.Load<GameObject>(m.resourcePath); Check(prefab != null, m.slug + " prefab is present in runtime Resources");
                        var collider = prefab.GetComponent<BoxCollider>(); Check(collider != null && !collider.enabled && Mathf.Abs(collider.center.x) < .001f && Mathf.Abs(collider.center.z) < .001f && Mathf.Abs(collider.center.y - collider.size.y * .5f) < .001f, m.slug + " measured placement box is disabled and floor-centred");
                        var group = prefab.GetComponent<LODGroup>(); var lods = group.GetLODs();
                        Check(lods.Length == m.sourceLevels.Count(l => l >= m.mobileFirstLevel) && lods.All(l => l.renderers.Length == 1), m.slug + " mobile LOD chain has one renderer per level");
                        Check(m.slug != "cryo-valve-manifold" || m.mobileFirstLevel == 1 && m.triangles[1] == 3000, "Manifold mobile default uses the selected 3000-triangle alternative: " + m.slug);
                        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                            Check(!mesh.isReadable, m.slug + " generated mesh does not retain runtime CPU readability");
                            var material = renderer.sharedMaterial;
                            Check(material.shader.name == "Universal Render Pipeline/Lit" && material.shader.isSupported && material.GetFloat("_Cull") == 2 && material.enableInstancing, m.slug + " supported opaque URP material has backface culling and instancing");
                            foreach (var property in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap" })
                            {
                                var texture = material.GetTexture(property) as Texture2D; Check(texture != null && texture.mipmapCount > 1, m.slug + " " + property + " texture/mips are loaded");
                                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                                Check(importer != null && !importer.isReadable && importer.sRGBTexture == (property == "_BaseMap") && (property != "_BumpMap" || importer.textureType == TextureImporterType.NormalMap && !importer.flipGreenChannel), m.slug + " " + property + " color space/normal convention is correct");
                                foreach (var platform in new[] { "iPhone", "Android" }) { var settings = importer.GetPlatformTextureSettings(platform); Check(settings.overridden && settings.format == TextureImporterFormat.ASTC_6x6 && settings.maxTextureSize <= 1024, m.slug + " " + property + " " + platform + " mobile compression verified"); }
                            }
                        }
                        var item = Showcase().objects.First(o => o.assetId == m.id); item.x = 22; item.z = -16; item.yaw = 37; item.scale = 1.25f;
                        var instance = FloorObjectVisuals.Spawn(item, root.transform, new List<Material>());
                        Check(instance.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && instance.GetComponentsInChildren<Animator>(true).Length == 0 && instance.GetComponentsInChildren<Collider>(true).Length == 0, m.slug + " placed model stays static and strips review/physics colliders");
                        var footprint = new HashSet<Vector2Int>(FloorDocuments.FootprintCells(item));
                        foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) foreach (var corner in Corners(r.localBounds))
                        {
                            var point = r.transform.TransformPoint(corner);
                            Check(footprint.Contains(new Vector2Int(Mathf.FloorToInt(point.x / 2), Mathf.FloorToInt(-point.z / 2))), m.slug + " rotated/scaled visual corner is inside conservative gameplay footprint");
                        }
                        Object.DestroyImmediate(instance);
                    }
                }
                finally { Object.DestroyImmediate(root); }
                WindowRoundtrip();
                var draft = Showcase(); Check(FloorDocuments.Validate(draft).IsValid, "Twenty-model showcase validates without replacing its original layout rows");
                SessionState.SetBool(Key, true); SessionState.SetFloat(Key + ".deadline", (float)(EditorApplication.timeSinceStartup + 120)); Save();
                if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity", OpenSceneMode.Single);
                Check(FloorPlaytest.Begin(draft, "cryo-station", 1), "Protected gameplay playtest accepts all twenty saved model IDs: " + FloorPlaytest.Status);
                Save();
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
            }
            catch (Exception error) { report.errors.Add(error.ToString()); Finish(); }
        }
        static void WindowRoundtrip()
        {
            var window = ScriptableObject.CreateInstance<FloorEditorWindow>(); string path = null; bool owned = false; var created = new List<string>(); var backups = new HashSet<string>();
            int priorMap = MazeMap.MapIndex; string priorSector = FloorDocuments.RuntimeSectorId; int priorFloor = FloorDocuments.RuntimeFloorNumber;
            try
            {
                window.CreateGUI(); window.Load("cryo-station", 1); window.SetPaletteCategory("Local batch");
                Check(window.rootVisualElement.Query(className: "asset-card").ToList().Count == 20, "Actual editor palette exposes twenty new models under Local batch");
                Check(window.rootVisualElement.Query(className: "asset-warning").ToList().Count == 3, "Actual enemy cards carry three UNRIGGED STATIC PREVIEW badges");
                window.Edit(d => { d.title = "QA TEMPORARY LOCAL MODEL PERSISTENCE"; d.objects = Array.Empty<PlacedObject>(); });
                foreach (var item in Showcase().objects) window.Place(item.assetId, new Vector2(item.x, -item.z));
                Check(window.Document.objects.Length == 20, "Actual placement callback places every new model at measured world scale");
                string initial = FloorDocuments.ToJson(window.Document); window.Canvas.RotateSelection(15); string rotated = FloorDocuments.ToJson(window.Document);
                window.UndoEdit(); Check(FloorDocuments.ToJson(window.Document) == initial, "Actual Undo restores new-model rotation"); window.RedoEdit(); Check(FloorDocuments.ToJson(window.Document) == rotated, "Actual Redo restores new-model rotation");
                window.Edit(d => d.objects[0].scale = 1.2f);
                path = window.SavePath; Check(!File.Exists(path), "Temporary persistence test will not overwrite any authored user floor");
                string dir = Path.GetDirectoryName(path); while (!Directory.Exists(dir)) { created.Add(dir); dir = Path.GetDirectoryName(dir); }
                if (Directory.Exists(Path.GetDirectoryName(path))) backups.UnionWith(Directory.GetFiles(Path.GetDirectoryName(path), "1.json.*.bak"));
                owned = true; window.Save(); Check(File.Exists(path) && !window.Dirty, "Actual Save persists all twenty new stable IDs");
                string expected = FloorDocuments.ToJson(window.Document); window.Load("cryo-station", 1); Check(FloorDocuments.ToJson(window.Document) == expected, "Actual Reload retains new model positions, rotation, scale and identity");
                FloorDocuments.ClearCache(); FloorDocuments.SetRuntimeContext("cryo-station", 1); MazeMap.MapIndex = MazeMap.ZoneLayoutIndex(window.Document.layoutId); MazeMap.Init();
                Check(FloorDocuments.TryGetOverride(window.Document.layoutId, out var loaded) && loaded.objects.Length == 20 && loaded.objects.All(o => FloorObjectVisuals.IsSupported(o.assetId)), "Ordinary saved-floor runtime loader resolves all twenty placements");
            }
            finally
            {
                if (owned && path != null)
                {
                    if (Directory.Exists(Path.GetDirectoryName(path))) foreach (var backup in Directory.GetFiles(Path.GetDirectoryName(path), "1.json.*.bak").Where(f => !backups.Contains(f))) { File.Delete(backup); if (File.Exists(backup + ".meta")) File.Delete(backup + ".meta"); }
                    AssetDatabase.DeleteAsset(path); if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                    foreach (var dir in created) if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) { Directory.Delete(dir); if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta"); }
                }
                Object.DestroyImmediate(window); FloorDocuments.ClearCache(); if (string.IsNullOrEmpty(priorSector)) FloorDocuments.ClearRuntimeContext(); else FloorDocuments.SetRuntimeContext(priorSector, priorFloor); MazeMap.MapIndex = priorMap; MazeMap.Init();
                report.fixtureRemoved = path == null || !File.Exists(path);
            }
        }
        static IEnumerable<Vector3> Corners(Bounds b) { for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) yield return b.center + Vector3.Scale(b.extents, new Vector3(x,y,z)); }
        static void Tick()
        {
            Load();
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".deadline", 0)) throw new TimeoutException(FloorPlaytest.Status);
                if (stopping || SessionState.GetBool(Key + ".stopping", false)) { if (!EditorApplication.isPlayingOrWillChangePlaymode && !FloorPlaytest.Active) Finish(); return; }
                if (!EditorApplication.isPlaying) return;
                var game = Object.FindAnyObjectByType<Game>(); if (game == null || !game.run || !Game.TestRun) return;
                if (!observed)
                {
                    var objects = Object.FindObjectsByType<Transform>().Where(t => t.name.StartsWith("QA local-20261016-", StringComparison.Ordinal)).ToArray();
                    Check(objects.Length == 20 && objects.All(t => t.GetComponentsInChildren<Renderer>(true).Length > 0), "Actual protected gameplay instantiates all twenty local placements with their renderers");
                    Check(objects.All(t => t.GetComponentsInChildren<MonoBehaviour>(true).Length == 0), "Enemy preview models have no runtime AI or animation behavior");
                    observed = true; started = EditorApplication.timeSinceStartup; Save();
                }
                if (EditorApplication.timeSinceStartup - started > 2) { stopping = true; SessionState.SetBool(Key + ".stopping", true); Save(); FloorPlaytest.Stop(); }
            }
            catch (Exception error) { report.errors.Add(error.ToString()); stopping = true; SessionState.SetBool(Key + ".stopping", true); Save(); FloorPlaytest.Stop(); if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); }
        }
        static void Finish()
        {
            Directory.CreateDirectory(Out); report ??= new Report();
            try
            {
                if (File.Exists(Path.Combine(Out, "prefs-before.private.json")))
                {
                    var prefs = JsonUtility.FromJson<FloorPreferenceSnapshot>(File.ReadAllText(Path.Combine(Out, "prefs-before.private.json"))); FloorPreferenceProtection.Restore(prefs);
                    report.prefsAfter = FloorPreferenceProtection.Digest(FloorPreferenceProtection.Capture()); report.preferencesRestored = report.prefsAfter == report.prefsBefore;
                    Check(report.preferencesRestored, "All original saved progression and typed game preferences match after testing");
                }
            }
            catch (Exception error) { report.errors.Add(error.ToString()); }
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".stopping", false); EditorApplication.update -= Tick; Save();
            EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
        }
    }
}
