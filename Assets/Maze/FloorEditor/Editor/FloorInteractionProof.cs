using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maze.FloorEditing.Editor
{
    /// <summary>
    /// Drives actual UITK dispatch and manipulators on a disposable shown window.
    /// No Save, Edit(Action), document mutation, Play Mode, or PlayerPrefs calls.
    /// </summary>
    public static class FloorInteractionProof
    {
        [Serializable] public sealed class Check { public string name, details; public bool passed; }
        [Serializable] public sealed class EventTrace
        {
            public string step, eventType, pickedTarget, dispatchedTarget, captureAfter;
            public float x, y;
            public int pointerId, button;
            public string modifiers;
        }
        [Serializable] public sealed class Report
        {
            public string startedUtc, finishedUtc, unityVersion, status, error, screenshot, screenshotError;
            public string scope = "Synthetic pointer/key/wheel events dispatched through an attached live Editor UITK panel; not OS mouse automation.";
            public bool savedFloorFilesUnchanged, existingEditorWindowsUnchanged;
            public List<Check> checks = new List<Check>();
            public List<EventTrace> events = new List<EventTrace>();
        }
        sealed class Step { public string name; public Action action; public double settle; }
        sealed class ExistingWindow
        {
            public FloorEditorWindow window;
            public string json;
            public bool dirty;
        }

        static readonly List<Step> steps = new List<Step>();
        static readonly List<ExistingWindow> existing = new List<ExistingWindow>();
        static Dictionary<string, string> diskBefore;
        static FloorEditorWindow window;
        static EditorWindow previousFocus;
        static Report report;
        static string outputDirectory, firstId, secondId, beforeDrag, afterDrag, beforeGroup, beforeRotation, beforePaint;
        static string currentStep;
        static Vector2 firstDrop, secondDrop, dragFrom, dragTo, firstBeforeGroup, secondBeforeGroup, panBefore;
        static Vector2Int playerCell, enemyStart;
        static float firstYaw, secondYaw, zoomBefore;
        static Vector2 zoomAnchor;
        static VisualElement card;
        static int initialObjectCount, index, stage;
        static string TestAssetId => Environment.GetEnvironmentVariable("MAZE_FLOOR_INTERACTION_ASSET") ?? "cryo-supply-crate";
        static string beforeScale;
        static double nextTick, deadline;
        static bool running;

        [MenuItem("Maze/Floor Editor Proof/Run live interaction proof")]
        public static void Run()
        {
            if (running) { Debug.LogWarning("Floor interaction proof is already running."); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before running the floor interaction proof.");
            outputDirectory = Environment.GetEnvironmentVariable("MAZE_FLOOR_INTERACTION_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputDirectory)) outputDirectory = Path.Combine(Path.GetTempPath(), "maze-floor-interaction-proof");
            outputDirectory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(outputDirectory);
            report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, status = "running" };
            steps.Clear(); existing.Clear(); index = stage = 0;
            firstId = secondId = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<FloorEditorWindow>())
                existing.Add(new ExistingWindow { window = w, json = FloorDocuments.ToJson(w.Document), dirty = w.Dirty });
            diskBefore = SnapshotFloorFiles(); previousFocus = EditorWindow.focusedWindow;
            deadline = EditorApplication.timeSinceStartup + 120;
            nextTick = EditorApplication.timeSinceStartup + .15;
            running = true;
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
            try
            {
                window = ScriptableObject.CreateInstance<FloorEditorWindow>();
                window.hideFlags = HideFlags.DontSave;
                window.titleContent = new GUIContent("Floor Editor · interaction proof");
                window.minSize = new Vector2(1050, 660);
                window.position = new Rect(40, 60, 1440, 900);
                window.ShowUtility(); window.Focus(); window.Repaint();
            }
            catch (Exception ex) { Finish(ex); }
        }

        static void Tick()
        {
            if (!running || EditorApplication.timeSinceStartup < nextTick) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Live interaction proof exceeded 120 seconds; last step: " + currentStep);
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (window == null) throw new InvalidOperationException("The temporary proof window closed before completion.");
                if (stage == 0)
                {
                    if (window.Canvas?.panel == null || window.Canvas.contentRect.width < 200 || window.Canvas.contentRect.height < 200 || window.Document == null) return;
                    if (window.rootVisualElement.panel.contextType != ContextType.Editor) throw new InvalidOperationException("Proof window is not attached to an Editor panel.");
                    window.Canvas.Fit();
                    initialObjectCount = window.Document.objects.Length;
                    var positions = FindClearPoints(window.Document, 2);
                    firstDrop = positions[0]; secondDrop = positions[1];
                    CheckThat("attached-live-window", true, "Fresh utility window attached to Editor panel; canvas " + window.Canvas.contentRect.size + ".");
                    Search(TestAssetId); BuildSteps(); stage = 1; nextTick = EditorApplication.timeSinceStartup + .5; return;
                }
                if (index >= steps.Count) { Finish(null); return; }
                var step = steps[index++]; currentStep = step.name; step.action();
                window.Repaint(); nextTick = EditorApplication.timeSinceStartup + step.settle;
            }
            catch (Exception ex) { Finish(ex); }
        }

        static void Add(string name, Action action, double settle = .12) => steps.Add(new Step { name = name, action = action, settle = settle });

        static void BuildSteps()
        {
            Add("palette-card-ready", () =>
            {
                card = window.rootVisualElement.Q<VisualElement>(className: "asset-card");
                CheckThat("filtered-integrated-asset-card", card != null && card.worldBound.width > 30 && card.worldBound.height > 20, "Requested model palette card is present and laid out: " + TestAssetId);
                CheckThat("palette-card-visible", window.rootVisualElement.panel.Pick(card.worldBound.center) != null && card.worldBound.Overlaps(window.rootVisualElement.worldBound), "Actual palette target is visible.");
            });
            Add("palette-first-down", () => Pointer(EventType.MouseDown, card.worldBound.center));
            Add("palette-first-drag", () => Pointer(EventType.MouseDrag, window.Canvas.PanelAtWorld(firstDrop)));
            Add("palette-first-up", () => Pointer(EventType.MouseUp, window.Canvas.PanelAtWorld(firstDrop)));
            Add("assert-palette-drop", () =>
            {
                CheckThat("actual-palette-drag-created-object", window.Document.objects.Length == initialObjectCount + 1, "Object count " + initialObjectCount + " → " + window.Document.objects.Length + ".");
                var item = window.Document.objects.Last(); firstId = item.id;
                CheckThat("palette-drop-position", Near(new Vector2(item.x, -item.z), Snap(firstDrop)) && item.assetId == TestAssetId, "Asset " + item.assetId + " at " + new Vector2(item.x, -item.z));
            });
            AddButtonClick("select-tool", "Select / move");
            Add("drag-setup", () => { beforeDrag = Json(); dragFrom = Position(firstId); dragTo = dragFrom + new Vector2(1.13f, .86f); });
            Add("object-drag-down", () => Pointer(EventType.MouseDown, window.Canvas.PanelAtWorld(dragFrom)));
            Add("object-drag-move-one", () => Pointer(EventType.MouseDrag, window.Canvas.PanelAtWorld(Vector2.Lerp(dragFrom, dragTo, .5f))));
            Add("object-drag-move-two", () => Pointer(EventType.MouseDrag, window.Canvas.PanelAtWorld(dragTo)));
            Add("object-drag-up", () => Pointer(EventType.MouseUp, window.Canvas.PanelAtWorld(dragTo)));
            Add("assert-snapped-drag", () =>
            {
                var at = Position(firstId); afterDrag = Json();
                CheckThat("actual-canvas-drag-snaps-half-metre", Near(at, Snap(dragTo)) && !Near(at, dragFrom), "Expected " + Snap(dragTo) + "; got " + at + ".");
                CheckThat("drag-keeps-selection", window.Canvas.Selection.Contains(firstId), "Dragged object remains selected.");
            });
            AddButtonClick("undo-drag", "Undo");
            Add("assert-one-drag-undo", () => CheckThat("one-undo-restores-entire-drag", Json() == beforeDrag, "Exact full-document JSON equals snapshot before two move events."));
            AddButtonClick("redo-drag", "Redo");
            Add("assert-drag-redo", () => CheckThat("redo-restores-drag", Json() == afterDrag, "Exact full-document JSON equals drag result."));
            Add("palette-second-down", () => Pointer(EventType.MouseDown, card.worldBound.center));
            Add("palette-second-drag", () => Pointer(EventType.MouseDrag, window.Canvas.PanelAtWorld(secondDrop)));
            Add("palette-second-up", () => Pointer(EventType.MouseUp, window.Canvas.PanelAtWorld(secondDrop)));
            Add("assert-second-drop", () =>
            {
                CheckThat("second-palette-drop", window.Document.objects.Length == initialObjectCount + 2, "Second actual palette drag created one object.");
                secondId = window.Document.objects.Last().id;
            });
            AddButtonClick("select-tool-again", "Select / move");
            AddCanvasClick("select-first", () => Position(firstId));
            AddCanvasClick("shift-select-second", () => Position(secondId), EventModifiers.Shift);
            Add("assert-shift-selection", () => CheckThat("actual-shift-multiselect", window.Canvas.Selection.Count == 2 && window.Canvas.Selection.Contains(firstId) && window.Canvas.Selection.Contains(secondId), "Exactly the two proof props are selected."));
            Add("group-drag-setup", () => { beforeGroup = Json(); firstBeforeGroup = Position(firstId); secondBeforeGroup = Position(secondId); dragFrom = firstBeforeGroup; dragTo = dragFrom + new Vector2(.61f, 1.14f); });
            Add("group-drag-down", () => Pointer(EventType.MouseDown, window.Canvas.PanelAtWorld(dragFrom)));
            Add("group-drag-move", () => Pointer(EventType.MouseDrag, window.Canvas.PanelAtWorld(dragTo)));
            Add("group-drag-up", () => Pointer(EventType.MouseUp, window.Canvas.PanelAtWorld(dragTo)));
            Add("assert-group-move", () =>
            {
                var a = Position(firstId) - firstBeforeGroup; var b = Position(secondId) - secondBeforeGroup;
                CheckThat("group-drag-preserves-relative-offset", a.sqrMagnitude > .01f && Near(a, b), "First delta " + a + "; second delta " + b + ".");
            });
            AddButtonClick("undo-group", "Undo");
            Add("assert-group-undo", () => CheckThat("one-undo-restores-group-drag", Json() == beforeGroup, "Both object transforms restored by one actual toolbar undo click."));
            AddCanvasClick("select-first-for-rotation", () => Position(firstId));
            AddCanvasClick("shift-second-for-rotation", () => Position(secondId), EventModifiers.Shift);
            Add("rotation-setup", () => { beforeRotation = Json(); firstYaw = Item(firstId).yaw; secondYaw = Item(secondId).yaw; });
            Add("keyboard-rotate", () => Key(KeyCode.E, 'e'));
            Add("assert-keyboard-rotation", () => CheckThat("keyboard-e-rotates-selection", NearAngle(Item(firstId).yaw, firstYaw + 15) && NearAngle(Item(secondId).yaw, secondYaw + 15), "Both selected objects rotated 15 degrees through the window KeyDown handler."));
            AddButtonClick("undo-rotation", "Undo");
            Add("assert-rotation-undo", () => CheckThat("rotation-undo", Json() == beforeRotation, "Exact pre-rotation document restored."));
            AddCanvasClick("single-selection-for-scale", () => Position(firstId));
            Add("scale-through-inspector", () =>
            {
                beforeScale = Json();
                var field = window.rootVisualElement.Query<UnityEngine.UIElements.FloatField>().ToList().First(f => f.label == "Uniform scale");
                field.value = 1.35f;
                CheckThat("inspector-scales-requested-model", Mathf.Abs(Item(firstId).scale - 1.35f) < .001f, "Actual FloatField callback scales the selected model.");
            });
            AddButtonClick("undo-scale", "Undo");
            Add("assert-scale-undo", () => CheckThat("scale-undo-restores-document", Json() == beforeScale, "One actual Undo restores the complete pre-scale document."));
            AddButtonClick("redo-scale", "Redo");
            Add("assert-scale-redo", () => CheckThat("scale-redo-restores-value", Mathf.Abs(Item(firstId).scale - 1.35f) < .001f, "One actual Redo restores uniform scale."));
            Add("find-player-brush", () => Search("player start"), .3);
            AddButtonClick("player-brush", "P   Player start");
            Add("player-paint-setup", () => { beforePaint = Json(); playerCell = FindPlainSegment(window.Document, 1); CheckThat("player-brush-selected", window.Canvas.PaintCell == 'P', "The actual palette brush button selected P."); });
            AddCanvasClick("paint-player", () => CellCenter(playerCell));
            Add("assert-player-marker", () =>
            {
                int count = window.Document.rows.Sum(row => row.Count(c => c == 'P'));
                CheckThat("player-marker-relocates-once", count == 1 && window.Document.rows[playerCell.y][playerCell.x] == 'P', "Exactly one P remains after pointer painting.");
            });
            AddButtonClick("undo-player", "Undo");
            Add("assert-player-undo", () => CheckThat("player-paint-undo", Json() == beforePaint, "Original P and painted cell restored together."));
            Add("find-spawn-brush", () => Search("enemy spawn"), .3);
            AddButtonClick("enemy-brush", "S   Enemy spawn");
            Add("enemy-paint-setup", () => { beforePaint = Json(); enemyStart = FindPlainSegment(window.Document, 3); });
            Add("enemy-stroke-down", () => Pointer(EventType.MouseDown, window.Canvas.PanelAtWorld(CellCenter(enemyStart))));
            Add("enemy-stroke-move", () => Pointer(EventType.MouseDrag, window.Canvas.PanelAtWorld(CellCenter(enemyStart + new Vector2Int(2, 0)))));
            Add("enemy-stroke-up", () => Pointer(EventType.MouseUp, window.Canvas.PanelAtWorld(CellCenter(enemyStart + new Vector2Int(2, 0)))));
            Add("assert-continuous-spawn-paint", () =>
            {
                bool all = true; for (int x = 0; x < 3; x++) all &= window.Document.rows[enemyStart.y][enemyStart.x + x] == 'S';
                CheckThat("continuous-spawn-brush-fills-skipped-cell", all, "One pointer move spanning three cells painted every S cell.");
            });
            AddButtonClick("undo-enemy-stroke", "Undo");
            Add("assert-enemy-stroke-undo", () => CheckThat("one-undo-restores-whole-paint-stroke", Json() == beforePaint, "All three spawn cells restored by one undo click."));
            Add("boundary-paint-snapshot", () => beforePaint = Json());
            AddCanvasClick("paint-protected-boundary", () => new Vector2(1, 1));
            Add("assert-protected-boundary", () => CheckThat("paint-protects-boundary", Json() == beforePaint, "Pointer brush at boundary cell made no document changes."));
            AddButtonClick("select-before-view-tests", "Select / move");
            Add("zoom-setup", () => { zoomBefore = window.Canvas.PixelsPerMetre; zoomAnchor = window.Canvas.WorldAtPanel(window.Canvas.worldBound.center); });
            Add("wheel-zoom", () => Wheel(window.Canvas.worldBound.center, -2));
            Add("assert-cursor-anchored-zoom", () => CheckThat("wheel-zoom-keeps-cursor-world-anchor", window.Canvas.PixelsPerMetre > zoomBefore && Near(window.Canvas.WorldAtPanel(window.Canvas.worldBound.center), zoomAnchor, .02f), "Scale " + zoomBefore + " → " + window.Canvas.PixelsPerMetre + "; cursor world point preserved."));
            Add("pan-setup", () => { panBefore = window.Canvas.PanelAtWorld(new Vector2(8, 8)); dragFrom = window.Canvas.worldBound.center; dragTo = dragFrom + new Vector2(32, 18); });
            Add("middle-pan-down", () => Pointer(EventType.MouseDown, dragFrom, 2));
            Add("middle-pan-move", () => Pointer(EventType.MouseDrag, dragTo, 2));
            Add("middle-pan-up", () => Pointer(EventType.MouseUp, dragTo, 2));
            Add("assert-middle-pan", () => CheckThat("middle-drag-pans-plan", Near(window.Canvas.PanelAtWorld(new Vector2(8, 8)) - panBefore, new Vector2(32, 18), .2f), "Canvas world-to-panel transform moved by the pointer delta."));
            Add("restore-draft-baseline", () => window.Load("cryo-station", 1), .25);
            Add("restore-readable-palette", () => { Search(""); window.Canvas.Fit(); }, .5);
            Add("optional-window-screenshot", CaptureScreenshot, .2);
        }

        static void AddButtonClick(string name, string text)
        {
            Add(name + "-down", () => Pointer(EventType.MouseDown, Button(text).worldBound.center));
            Add(name + "-up", () => Pointer(EventType.MouseUp, Button(text).worldBound.center));
        }
        static void AddCanvasClick(string name, Func<Vector2> point, EventModifiers modifiers = EventModifiers.None)
        {
            Add(name + "-down", () => Pointer(EventType.MouseDown, window.Canvas.PanelAtWorld(point()), 0, modifiers));
            Add(name + "-up", () => Pointer(EventType.MouseUp, window.Canvas.PanelAtWorld(point()), 0, modifiers));
        }
        static Button Button(string text)
        {
            var found = window.rootVisualElement.Query<Button>().ToList().FirstOrDefault(b => b.text == text);
            if (found == null || found.worldBound.width < 2 || found.worldBound.height < 2) throw new InvalidOperationException("Visible button not found: " + text);
            return found;
        }
        static void Search(string text)
        {
            var search = window.rootVisualElement.Q<ToolbarSearchField>();
            if (search == null) throw new InvalidOperationException("Palette search field is missing.");
            // Fixture navigation only; editing gestures and toolbar actions use dispatched events.
            search.value = text;
        }
        static void Pointer(EventType type, Vector2 panelPosition, int button = 0, EventModifiers modifiers = EventModifiers.None)
        {
            var source = new Event { type = type, mousePosition = panelPosition, button = button, clickCount = 1, modifiers = modifiers };
            if (type == EventType.MouseDown) { using (var evt = PointerDownEvent.GetPooled(source)) Dispatch(evt, panelPosition, modifiers); }
            else if (type == EventType.MouseUp) { using (var evt = PointerUpEvent.GetPooled(source)) Dispatch(evt, panelPosition, modifiers); }
            else { using (var evt = PointerMoveEvent.GetPooled(source)) Dispatch(evt, panelPosition, modifiers); }
        }
        static void Dispatch<T>(PointerEventBase<T> evt, Vector2 panelPosition, EventModifiers modifiers) where T : PointerEventBase<T>, new()
        {
            var panel = window.rootVisualElement.panel;
            var picked = panel.Pick(panelPosition) ?? window.rootVisualElement;
            var captured = panel.GetCapturingElement(evt.pointerId) as VisualElement;
            var target = captured ?? picked;
            evt.target = target; target.SendEvent(evt);
            var after = panel.GetCapturingElement(evt.pointerId) as VisualElement;
            report.events.Add(new EventTrace { step = currentStep, eventType = evt.GetType().Name, pickedTarget = Describe(picked), dispatchedTarget = Describe(target), captureAfter = Describe(after), x = panelPosition.x, y = panelPosition.y, pointerId = evt.pointerId, button = evt.button, modifiers = modifiers.ToString() });
            if (evt is PointerDownEvent && currentStep != null && (currentStep.StartsWith("palette-") || currentStep.StartsWith("object-drag-") || currentStep.StartsWith("group-drag-")))
                CheckThat(currentStep + "-pointer-captured", after != null, "Pointer captured by " + Describe(after) + ".");
        }
        static void Key(KeyCode key, char character)
        {
            window.Canvas.Focus();
            using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key, character = character }))
            { evt.target = window.Canvas; window.Canvas.SendEvent(evt); }
        }
        static void Wheel(Vector2 at, float amount)
        {
            using (var evt = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = at, delta = new Vector2(0, amount) }))
            { evt.target = window.Canvas; window.Canvas.SendEvent(evt); }
        }
        static string Describe(VisualElement target) => target == null ? "none" : target.GetType().Name + "#" + target.name + "." + string.Join(".", target.GetClasses());
        static PlacedObject Item(string id) => window.Document.objects.First(o => o.id == id);
        static Vector2 Position(string id) { var o = Item(id); return new Vector2(o.x, -o.z); }
        static string Json() => FloorDocuments.ToJson(window.Document);
        static Vector2 CellCenter(Vector2Int cell) => new Vector2(cell.x * 2 + 1, cell.y * 2 + 1);
        static Vector2 Snap(Vector2 point) => new Vector2(Mathf.Round(point.x * 2) * .5f, Mathf.Round(point.y * 2) * .5f);
        static bool Near(Vector2 a, Vector2 b, float tolerance = .011f) => (a - b).sqrMagnitude <= tolerance * tolerance;
        static bool NearAngle(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b)) < .011f;
        static void CheckThat(string name, bool pass, string details)
        {
            report.checks.Add(new Check { name = name, passed = pass, details = details });
            if (!pass) throw new InvalidOperationException(name + ": " + details);
        }
        static Vector2[] FindClearPoints(FloorDocument doc, int count)
        {
            var points = new List<Vector2>();
            for (int y = 2; y < doc.rows.Length - 2; y++) for (int x = 2; x < doc.rows[y].Length - 2; x++)
            {
                if (doc.rows[y][x] != '.') continue;
                var at = CellCenter(new Vector2Int(x, y));
                if (points.Any(p => Vector2.Distance(p, at) < 8)) continue;
                bool clear = true;
                foreach (var o in doc.objects)
                {
                    if (o == null) continue;
                    float radius = Mathf.Max(o.footprintWidth, o.footprintDepth) * Mathf.Max(o.scale, .1f) * .5f + 1;
                    if (Vector2.Distance(at, new Vector2(o.x, -o.z)) < radius) { clear = false; break; }
                }
                if (!clear) continue;
                points.Add(at); if (points.Count == count) return points.ToArray();
            }
            // Appending a new visual prop still makes it the topmost hit target, even if dense baseline art leaves no empty pocket.
            for (int y = 2; y < doc.rows.Length - 2; y++) for (int x = 2; x < doc.rows[y].Length - 2; x++)
            {
                if (doc.rows[y][x] != '.') continue; var at = CellCenter(new Vector2Int(x, y));
                if (points.Any(p => Vector2.Distance(p, at) < 8)) continue;
                points.Add(at); if (points.Count == count) return points.ToArray();
            }
            throw new InvalidOperationException("Unable to find two separated interior floor cells for the disposable proof draft.");
        }
        static Vector2Int FindPlainSegment(FloorDocument doc, int length)
        {
            for (int y = 2; y < doc.rows.Length - 2; y++) for (int x = 2; x < doc.rows[y].Length - length - 1; x++)
            {
                bool clear = true; for (int i = 0; i < length; i++) clear &= doc.rows[y][x + i] == '.';
                if (clear) return new Vector2Int(x, y);
            }
            throw new InvalidOperationException("No plain floor segment is available for the marker-paint proof.");
        }
        static Dictionary<string, string> SnapshotFloorFiles()
        {
            const string root = "Assets/Maze/FloorEditor/Resources/FloorEditor";
            var files = new Dictionary<string, string>(); if (!Directory.Exists(root)) return files;
            foreach (var path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                using (var hash = SHA256.Create()) files[path] = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)));
            return files;
        }
        static bool SameFiles(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
        static void CaptureScreenshot()
        {
            if (Environment.GetEnvironmentVariable("MAZE_FLOOR_INTERACTION_SCREENSHOT") != "1") return;
            string path = Path.Combine(outputDirectory, "actual-editor-window.png");
            if (FloorPanelCapture.TryCapture(window, path, out _, out var error)) report.screenshot = path;
            else report.screenshotError = error;
        }

        static void Log(string message, string stack, LogType type)
        {
            if (!running || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)) return;
            report.checks.Add(new Check { name = "editor-log", passed = false, details = message + "\n" + stack });
        }
        static void Finish(Exception error)
        {
            if (!running) return; running = false; EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            if (error != null) report.error = error.ToString();
            try
            {
                report.savedFloorFilesUnchanged = SameFiles(diskBefore, SnapshotFloorFiles());
                report.existingEditorWindowsUnchanged = existing.All(e => e.window != null && e.window.Dirty == e.dirty && FloorDocuments.ToJson(e.window.Document) == e.json);
                report.checks.Add(new Check { name = "saved-floor-files-unchanged", passed = report.savedFloorFilesUnchanged, details = "Compared SHA256 and path sets for all saved floor JSON files." });
                report.checks.Add(new Check { name = "pre-existing-editor-windows-unchanged", passed = report.existingEditorWindowsUnchanged, details = "Compared each existing floor window's entire document JSON and dirty state." });
            }
            catch (Exception ex) { report.error = (report.error ?? "") + "\nPersistence verification: " + ex; }
            try { if (window != null) window.Close(); if (window != null) UnityEngine.Object.DestroyImmediate(window); if (previousFocus != null) previousFocus.Focus(); }
            catch (Exception ex) { report.error = (report.error ?? "") + "\nWindow cleanup: " + ex; }
            report.status = string.IsNullOrEmpty(report.error) && report.checks.All(c => c.passed) ? "passed" : "failed";
            File.WriteAllText(Path.Combine(outputDirectory, "interaction-proof.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("Floor interaction proof " + report.status + ": " + outputDirectory + " (" + report.checks.Count + " checks, " + report.events.Count + " dispatched pointer events)");
            if (Environment.GetEnvironmentVariable("MAZE_FLOOR_INTERACTION_EXIT") == "1") EditorApplication.Exit(report.status == "passed" ? 0 : 1);
        }
    }
}
