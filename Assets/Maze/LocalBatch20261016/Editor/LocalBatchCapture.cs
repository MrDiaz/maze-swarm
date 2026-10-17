using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maze.FloorEditing.Editor
{
    public static class LocalBatchCapture
    {
        static FloorEditorWindow window;
        static double ready;
        static string output;
        [Serializable] sealed class Evidence
        {
            public string utc, source = "Actual attached Unity EditorWindow; temporary unsaved review placements", previewError;
            public int placedModels, visibleCards, unriggedCards, validationErrors;
            public bool runtimePrefabsExcludeSourceGlbs, runtimePrefabsExcludeRawMaterialMaps;
            public int runtimeMeshes, runtimePbrTextures;
            public FloorPanelCapture.CaptureInfo props, enemies;
            public string propsError, enemiesError;
        }
        static Evidence evidence;
        static Label reviewBanner;
        public static void Open()
        {
            output = Environment.GetEnvironmentVariable("MAZE_BATCH_OUTPUT") ?? "Library/MazeFloorEditor/LocalBatchCapture";
            Directory.CreateDirectory(output); evidence = new Evidence { utc = DateTime.UtcNow.ToString("O") };
            var dependencies = FloorModelCatalog.Entries.SelectMany(m => AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(Resources.Load<GameObject>(m.resourcePath)), true)).Distinct().ToArray();
            evidence.runtimePrefabsExcludeSourceGlbs = dependencies.All(p => !p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
            evidence.runtimePrefabsExcludeRawMaterialMaps = dependencies.All(p => !p.EndsWith("/mr.png", StringComparison.OrdinalIgnoreCase));
            evidence.runtimeMeshes = dependencies.Count(p => p.Contains("/LocalBatch20261016/Generated/Meshes/"));
            evidence.runtimePbrTextures = dependencies.Count(p => p.Contains("/LocalBatch20261016/Textures/") && p.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            window = ScriptableObject.CreateInstance<FloorEditorWindow>(); window.titleContent = new GUIContent("Floor Editor · local model integration");
            window.minSize = new Vector2(1050, 660); window.position = new Rect(40, 60, 1480, 900); window.ShowUtility(); window.Focus();
            ready = EditorApplication.timeSinceStartup + 5; EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (window == null || EditorApplication.timeSinceStartup < ready || window.Canvas == null || window.Canvas.contentRect.width < 10) return;
            EditorApplication.update -= Tick;
            var doc = LocalBatchProof.Showcase();
            foreach (var o in doc.objects)
            {
                var m = FloorModelCatalog.Entries.First(d => d.id == o.assetId);
                o.name = m.label.Replace("Cryo ", "").Replace("Industrial ", "").Replace("Fungal ", "").Replace("Ruins ", "");
            }
            window.Edit(d => { d.title = doc.title; d.objects = doc.objects; });
            reviewBanner = new Label("ASSET REVIEW DRAFT · temporary unsaved placements · original floor layout unchanged");
            reviewBanner.AddToClassList("status"); window.rootVisualElement.Insert(1, reviewBanner);
            window.SetPaletteCategory("Local batch"); window.Canvas.Fit();
            var manifold = doc.objects.First(o => o.assetId.EndsWith("cryo-valve-manifold", StringComparison.Ordinal));
            window.Canvas.SetSelection(new[] { manifold.id }); Frame();
            window.rootVisualElement.schedule.Execute(() => window.Canvas.Fit()).ExecuteLater(300);
            window.rootVisualElement.schedule.Execute(CaptureProps).ExecuteLater(1800);
        }
        static void Frame()
        {
            var preview = window.rootVisualElement.Q<FloorPreview>(); preview.RenderNow(); preview.FrameSelection(); preview.RenderNow(); window.Repaint();
        }
        static void CaptureProps()
        {
            var target = window.rootVisualElement.Q<VisualElement>("asset-local-20261016-cryo-valve-manifold");
            var scroll = window.rootVisualElement.Query<ScrollView>().ToList().First(v => v.ClassListContains("palette-scroll"));
            if (target != null) scroll.ScrollTo(target);
            window.rootVisualElement.schedule.Execute(() => {
                FloorPanelCapture.TryCapture(window, Path.GetFullPath(Path.Combine(output, "floor-editor-local-props.png")), out evidence.props, out evidence.propsError);
                evidence.placedModels = window.Document.objects.Length;
                evidence.visibleCards = window.rootVisualElement.Query(className:"asset-card").ToList().Count;
                evidence.validationErrors = FloorDocuments.Validate(window.Document).ErrorCount;
                evidence.previewError = window.rootVisualElement.Q<FloorPreview>().LastError;
                window.SetPaletteCategory("Enemies · unrigged previews");
                var brute = window.Document.objects.First(o => o.assetId.EndsWith("charging-brute", StringComparison.Ordinal));
                window.Canvas.SetSelection(new[] { brute.id }); Frame();
                window.rootVisualElement.schedule.Execute(CaptureEnemies).ExecuteLater(1200);
            }).ExecuteLater(500);
        }
        static void CaptureEnemies()
        {
            FloorPanelCapture.TryCapture(window, Path.GetFullPath(Path.Combine(output, "floor-editor-unrigged-previews.png")), out evidence.enemies, out evidence.enemiesError);
            evidence.unriggedCards = window.rootVisualElement.Query(className:"asset-warning").ToList().Count;
            File.WriteAllText(Path.Combine(output, "editor-capture.json"), JsonUtility.ToJson(evidence, true));
            // Return this owned window to the real untouched floor after capturing the temporary review.
            window.Load("cryo-station", 1); window.SetPaletteCategory("Local batch"); window.Canvas.Fit();
            reviewBanner?.RemoveFromHierarchy();
            Debug.Log("Local model Editor capture complete: " + output);
        }
    }
}
