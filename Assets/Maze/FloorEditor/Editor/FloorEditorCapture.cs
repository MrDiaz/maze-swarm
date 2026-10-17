using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maze.FloorEditing.Editor
{
    public static class FloorEditorCapture
    {
        static FloorEditorWindow window;
        static double ready;
        static string output;
        [Serializable] sealed class Evidence
        {
            public string utc, layout, previewError, image, captureError;
            public FloorPanelCapture.CaptureInfo panelCapture;
            public int objects, paletteCards, errors, warnings;
            public Rect window, canvas;
        }
        public static void Open()
        {
            output = Environment.GetEnvironmentVariable("MAZE_FLOOR_CAPTURE_OUTPUT") ?? "Library/MazeFloorEditor/Captures";
            Directory.CreateDirectory(output);
            window = ScriptableObject.CreateInstance<FloorEditorWindow>();
            window.titleContent = new GUIContent("Maze Swarm Floor Editor");
            window.minSize = new Vector2(1050, 660);
            window.position = new Rect(35, 65, 1450, 880);
            window.ShowUtility(); window.Focus();
            ready = EditorApplication.timeSinceStartup + 6;
            EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (window == null) { EditorApplication.update -= Tick; return; }
            if (EditorApplication.timeSinceStartup < ready || window.Canvas == null || window.Canvas.contentRect.width < 10) return;
            EditorApplication.update -= Tick;
            window.Load("cryo-station", 1); window.Canvas.Fit();
            var first = window.Document.objects.FirstOrDefault(o => o.assetId == "cryo-cooling-reactor") ?? window.Document.objects.FirstOrDefault(); if (first != null) window.Canvas.SetSelection(new[] { first.id });
            var preview = window.rootVisualElement.Q<FloorPreview>(); preview?.RenderNow(); preview?.FrameSelection(); preview?.RenderNow(); window.Repaint();
            window.rootVisualElement.schedule.Execute(Capture).ExecuteLater(1500);
        }
        static void Capture()
        {
            var rect = window.position;
            string path = Path.GetFullPath(Path.Combine(output, "floor-editor-actual.png"));
            bool captured = FloorPanelCapture.TryCapture(window, path, out var captureInfo, out var captureError);
            if (!captured) path = "";
            var validation = FloorDocuments.Validate(window.Document);
            var evidence = new Evidence { utc = DateTime.UtcNow.ToString("O"), layout = window.Document.layoutId, objects = window.Document.objects.Length, paletteCards = window.rootVisualElement.Query(className: "asset-card").ToList().Count, errors = validation.ErrorCount, warnings = validation.WarningCount, previewError = window.rootVisualElement.Q<FloorPreview>()?.LastError, image = path, captureError = captureError, panelCapture = captureInfo, window = rect, canvas = window.Canvas.worldBound };
            File.WriteAllText(Path.Combine(output, "editor-capture.json"), JsonUtility.ToJson(evidence, true));
            Debug.Log("Floor editor actual capture: " + path);
        }
    }
}
