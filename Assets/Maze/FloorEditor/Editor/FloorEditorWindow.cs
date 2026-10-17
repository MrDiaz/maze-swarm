using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maze.FloorEditing.Editor
{
    public sealed class FloorEditorWindow : EditorWindow
    {
        const string Root = "Assets/Maze/FloorEditor/Editor/";
        [SerializeField] string draftJson, savedJson, diskJson, sectorId = "cryo-station";
        [SerializeField] int floorNumber = 1;
        [SerializeField] List<string> undo = new(), redo = new();
        FloorDocument doc;
        FloorCanvas canvas;
        FloorPreview preview;
        ScrollView inspector, palette;
        Label status, heading, reportLabel;
        PopupField<string> zonePicker, floorPicker, categoryPicker;
        [SerializeField] string paletteCategory = "All models";
        readonly List<SectorDef> sectors = new();
        Catalog catalog;
        string before;
        bool refreshing;
        public FloorDocument Document => doc;
        public FloorCanvas Canvas => canvas;
        public bool Dirty => doc != null && FloorDocuments.ToJson(doc) != savedJson;

        [Serializable] public sealed class Catalog { public Entry[] entries; }
        [Serializable] public sealed class Entry
        {
            public string id, label, category, resourcePath, previewPath;
            public float[] defaultFootprintXZ;
            public float defaultY, defaultYaw, defaultMaxHeight = 3.4f;
            public bool unrigged;
            public int mobileTriangles;
        }

        [MenuItem("Maze/Floor Editor")]
        public static void Open() { var w = GetWindow<FloorEditorWindow>(); w.titleContent = new GUIContent("Floor Editor"); w.minSize = new Vector2(1050, 660); w.Show(); }
        void OnEnable()
        {
            if (!string.IsNullOrEmpty(draftJson)) { try { doc = JsonUtility.FromJson<FloorDocument>(draftJson); } catch (ArgumentException) { doc = null; } }
            FloorPlaytest.StatusChanged += UpdateStatus;
        }
        void OnDisable() { FloorPlaytest.StatusChanged -= UpdateStatus; preview?.Dispose(); }
        public void CreateGUI()
        {
            preview?.Dispose();
            var root = rootVisualElement; root.Clear(); root.AddToClassList("floor-editor");
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "FloorEditor.uss"); if (styles != null) root.styleSheets.Add(styles);
            var top = Row("topbar"); root.Add(top);
            var title = new Label("MAZE SWARM  /  FLOOR EDITOR"); title.AddToClassList("brand"); top.Add(title);
            top.Add(Button("Undo", UndoEdit)); top.Add(Button("Redo", RedoEdit));
            top.Add(Button("Save floor", Save, "primary")); top.Add(Button("Playtest draft", Play, "play")); top.Add(Button("Stop test", FloorPlaytest.Stop));
            var selector = Row("selectors"); root.Add(selector);
            sectors.Clear(); sectors.AddRange(SectorDefs.All.Where(s => !s.endless && s.floors != null && s.floors.Length > 0));
            int si = Math.Max(0, sectors.FindIndex(s => s.id == sectorId)); sectorId = sectors[si].id;
            zonePicker = new PopupField<string>("Zone", sectors.Select(s => s.name).ToList(), si); selector.Add(zonePicker);
            floorPicker = new PopupField<string>("Floor", FloorNames(sectors[si]), Mathf.Clamp(floorNumber - 1, 0, sectors[si].floors.Length - 1)); selector.Add(floorPicker);
            zonePicker.RegisterValueChangedCallback(e => { if (refreshing) return; var chosen = sectors[zonePicker.index]; SwitchFloor(chosen.id, 1); });
            floorPicker.RegisterValueChangedCallback(e => { if (!refreshing) SwitchFloor(sectorId, floorPicker.index + 1); });
            heading = new Label(); heading.AddToClassList("floor-heading"); selector.Add(heading);
            selector.Add(Button("Reload", Reload)); selector.Add(Button("Import JSON…", Import)); selector.Add(Button("Export draft…", Export));
            var cat = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "floor-palette.json"); catalog = cat == null ? new Catalog { entries = Array.Empty<Entry>() } : JsonUtility.FromJson<Catalog>(cat.text);
            var body = Row("body"); root.Add(body);
            var left = new VisualElement(); left.AddToClassList("palette-panel"); body.Add(left);
            var search = new ToolbarSearchField(); search.RegisterValueChangedCallback(e => BuildPalette(e.newValue)); left.Add(search);
            var categories = new List<string> { "All models", "Local batch" };
            categories.AddRange((catalog?.entries ?? Array.Empty<Entry>()).Where(e => FloorDocuments.AssetIds.Contains(e.id)).Select(e => e.category).Distinct());
            categoryPicker = new PopupField<string>("Show", categories, Math.Max(0, categories.IndexOf(paletteCategory))); categoryPicker.AddToClassList("category-picker");
            categoryPicker.RegisterValueChangedCallback(e => { paletteCategory = e.newValue; BuildPalette(search.value); }); left.Add(categoryPicker);
            palette = new ScrollView(); palette.AddToClassList("palette-scroll"); left.Add(palette);
            var centre = new VisualElement(); centre.AddToClassList("centre"); body.Add(centre);
            var tools = Row("tools"); centre.Add(tools);
            tools.Add(Button("Select / move", () => Tool(null, null))); tools.Add(Button("Fit", () => canvas.Fit()));
            tools.Add(Button("Rotate −15°", () => canvas.RotateSelection(-15))); tools.Add(Button("Rotate +15°", () => canvas.RotateSelection(15)));
            tools.Add(Button("Duplicate", Duplicate)); tools.Add(Button("Delete", Delete));
            var snap = new Toggle("Snap 0.5 m") { value = true }; snap.RegisterValueChangedCallback(e => canvas.Snap = e.newValue); tools.Add(snap);
            canvas = new FloorCanvas(); centre.Add(canvas);
            canvas.BeforeEdit = BeginEdit; canvas.Changed = CommitEdit; canvas.SelectionChanged = RefreshInspector; canvas.PlaceRequested = Place;
            var overlays = Row("overlays"); centre.Add(overlays);
            foreach (var label in new[] { "Collision", "Paths", "Markers" }) { var t = new Toggle(label) { value = true }; t.RegisterValueChangedCallback(e => { if (label == "Collision") canvas.ShowCollision = e.newValue; if (label == "Paths") canvas.ShowPaths = e.newValue; if (label == "Markers") canvas.ShowMarkers = e.newValue; canvas.MarkDirtyRepaint(); }); overlays.Add(t); }
            overlays.Add(new Label("Drag objects · Shift adds selection · Middle/Alt drag pans · Wheel zooms · Q/E rotates"));
            var right = new VisualElement(); right.AddToClassList("inspector-panel"); body.Add(right);
            preview = new FloorPreview(); preview.AddToClassList("preview"); right.Add(preview);
            inspector = new ScrollView(); inspector.AddToClassList("inspector"); right.Add(inspector);
            reportLabel = new Label(); reportLabel.AddToClassList("validation"); right.Add(reportLabel);
            status = new Label(); status.AddToClassList("status"); root.Add(status);
            root.RegisterCallback<KeyDownEvent>(Keys);

            BuildPalette("");
            if (doc == null) Load(sectorId, floorNumber); else RefreshAll();
            canvas.schedule.Execute(() => canvas.Fit()).ExecuteLater(200);
        }
        static VisualElement Row(string css) { var v = new VisualElement(); v.AddToClassList(css); return v; }
        static Button Button(string text, Action action, string css = null) { var b = new Button(action) { text = text }; if (css != null) b.AddToClassList(css); return b; }
        static List<string> FloorNames(SectorDef sector) => sector.floors.Select((f, i) => (i + 1) + " · " + f.spec).ToList();
        public string SavePath => "Assets/Maze/FloorEditor/Resources/FloorEditor/Floors/" + sectorId + "/" + floorNumber + ".json";
        void SwitchFloor(string sector, int floor)
        {
            if (Dirty && !EditorUtility.DisplayDialog("Unsaved floor", "Discard the current draft and load the selected floor?", "Discard draft", "Keep editing")) { SyncPickers(); return; }
            Load(sector, floor);
        }
        void SyncPickers()
        {
            refreshing = true; int si = sectors.FindIndex(s => s.id == sectorId); if (si < 0) si = 0;
            zonePicker.index = si; floorPicker.choices = FloorNames(sectors[si]); floorPicker.index = Mathf.Clamp(floorNumber - 1, 0, floorPicker.choices.Count - 1); refreshing = false;
        }
        public void Load(string sector, int floor)
        {
            sectorId = sector; floorNumber = floor; string layout = SectorDefs.Get(sector).floors[floor - 1].layout;
            diskJson = File.Exists(SavePath) ? File.ReadAllText(SavePath) : null;
            if (diskJson != null && !FloorDocuments.TryParse(diskJson, out doc, out var report)) { doc = FloorDocuments.Create(layout); Note("Saved override is invalid; original loaded. " + report); }
            else if (diskJson == null) doc = FloorDocuments.Create(layout);
            doc.sectorId = sector; doc.floorNumber = floor; savedJson = FloorDocuments.ToJson(doc); draftJson = savedJson; undo.Clear(); redo.Clear(); SyncPickers(); RefreshAll();
        }
        void Reload() { if (!Dirty || EditorUtility.DisplayDialog("Reload floor", "Discard unsaved draft edits?", "Reload", "Keep editing")) Load(sectorId, floorNumber); }
        public void BeginEdit() { before = FloorDocuments.ToJson(doc); }
        public void CommitEdit()
        {
            string current = FloorDocuments.ToJson(doc); if (before != null && current != before) { undo.Add(before); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear(); }
            before = null; draftJson = current; RefreshAll(false);
        }
        public void Edit(Action<FloorDocument> mutation) { BeginEdit(); mutation(doc); CommitEdit(); }
        public void UndoEdit() { if (undo.Count == 0) return; redo.Add(FloorDocuments.ToJson(doc)); Restore(undo[undo.Count - 1]); undo.RemoveAt(undo.Count - 1); }
        public void RedoEdit() { if (redo.Count == 0) return; undo.Add(FloorDocuments.ToJson(doc)); Restore(redo[redo.Count - 1]); redo.RemoveAt(redo.Count - 1); }
        void Restore(string json) { doc = JsonUtility.FromJson<FloorDocument>(json); draftJson = json; RefreshAll(); }
        public void Save()
        {
            try
            {
                string currentDisk = File.Exists(SavePath) ? File.ReadAllText(SavePath) : null;
                if (currentDisk != diskJson) { Note("Save blocked: the file changed outside this editor. Export your draft, then reload."); return; }
                var validation = FloorDocuments.Validate(doc);
                if (!validation.IsValid) { Note("Save blocked: " + validation); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                if (!FloorDocuments.TrySaveAtomic(SavePath, doc, out var error)) { Note("Save blocked: " + error); return; }
                AssetDatabase.ImportAsset(SavePath, ImportAssetOptions.ForceUpdate); diskJson = File.ReadAllText(SavePath); savedJson = FloorDocuments.ToJson(doc); draftJson = savedJson; RefreshAll(false); Note("Saved floor " + floorNumber + ". A previous version is retained beside the JSON when replacing a file.");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Note("Save blocked: " + error.Message); }
        }
        void Play() { if (!FloorPlaytest.Begin(doc, sectorId, floorNumber)) Note(FloorPlaytest.Status); else UpdateStatus(); }
        void Import()
        {
            var path = EditorUtility.OpenFilePanel("Import floor draft", "", "json"); if (string.IsNullOrEmpty(path)) return;
            if (!FloorDocuments.TryParse(File.ReadAllText(path), out var candidate, out var report)) { Note("Import rejected: " + report); return; }
            if (candidate.layoutId != doc.layoutId) { Note("Select a floor using layout " + candidate.layoutId + " before importing this draft."); return; }
            candidate.sectorId = sectorId; candidate.floorNumber = floorNumber; BeginEdit(); doc = candidate; CommitEdit();
        }
        void Export() { var p = EditorUtility.SaveFilePanel("Export floor draft", "", sectorId + "-floor-" + floorNumber + ".json", "json"); if (!string.IsNullOrEmpty(p)) { File.WriteAllText(p, FloorDocuments.ToJson(doc)); Note("Draft exported to " + p); } }
        void Tool(string asset, char? cell) { canvas.ActiveAssetId = asset; canvas.PaintCell = cell; Note(asset != null ? "Place " + asset + ": click the plan or drag a palette card into it." : cell.HasValue ? "Paint " + cell + ": drag on the grid. Boundary walls are protected." : "Select and drag objects. Shift adds to selection."); }
        public void SetPaletteCategory(string category) { if (categoryPicker != null && categoryPicker.choices.Contains(category)) { paletteCategory = category; categoryPicker.SetValueWithoutNotify(category); BuildPalette(""); } }
        void BuildPalette(string filter)
        {
            if (palette == null) return; palette.Clear(); palette.scrollOffset = Vector2.zero;
            palette.Add(new Label("OBJECTS · drag into plan") { name = "palette-heading" });
            string currentGroup = null;
            foreach (var e in catalog?.entries ?? Array.Empty<Entry>())
            {
                if (string.IsNullOrEmpty(e.resourcePath) || !FloorDocuments.AssetIds.Contains(e.id)) continue;
                if (paletteCategory == "Local batch" && !FloorModelCatalog.IsLocalId(e.id)) continue;
                if (paletteCategory != "All models" && paletteCategory != "Local batch" && e.category != paletteCategory) continue;
                if (!string.IsNullOrEmpty(filter) && !(e.label + e.id).ToLowerInvariant().Contains(filter.ToLowerInvariant())) continue;
                if (currentGroup != e.category) { currentGroup = e.category; var group = new Label(currentGroup); group.AddToClassList("section-title"); palette.Add(group); }
                var card = new VisualElement(); card.AddToClassList("asset-card");
                var image = new Image { image = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + e.previewPath), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("asset-image"); card.Add(image);
                card.name = "asset-" + e.id; card.Add(new Label(e.label));
                if (e.unrigged) { var badge = new Label("UNRIGGED · STATIC PREVIEW"); badge.AddToClassList("asset-warning"); card.Add(badge); }
                card.AddManipulator(new PaletteDrag(this, e.id)); palette.Add(card);
            }
            var label = new Label("GRID & GAMEPLAY MARKERS"); label.AddToClassList("section-title"); palette.Add(label);
            var cells = new (char glyph, string label)[] { ('.', "Floor / erase"), ('#', "Wall"), ('B', "Machinery island"), ('C', "Cargo cover"), ('r', "Railing"), ('D', "Functional door"), ('P', "Player start"), ('S', "Enemy spawn"), ('X', "Explosive barrel"), ('Z', "Hold objective"), ('n', "Nest objective"), ('L', "Exit lift"), ('@', "Boss start"), ('G', "Generator objective"), ('R', "Relay objective"), ('$', "Salvage objective"), ('!', "Detonate objective"), ('Q', "Uplink objective"), (':', "Escort route"), ('H', "Hack panel"), ('J', "Jump pad pair"), ('i', "Ice"), ('o', "Cryo vent"), ('e', "Egg field"), ('T', "Neutral turret"), ('+', "Med station"), ('_', "Pit"), ('~', "Blue zone") };
            foreach (var c in cells) if (string.IsNullOrEmpty(filter) || c.label.ToLowerInvariant().Contains(filter.ToLowerInvariant())) palette.Add(Button(c.glyph + "   " + c.label, () => Tool(null, c.glyph)));
        }
        public void Place(string id, Vector2 at)
        {
            var entry = catalog?.entries?.FirstOrDefault(e => e.id == id); if (entry == null) return;
            float Snap(float v) => canvas.Snap ? Mathf.Round(v * 2f) / 2f : v;
            var item = new PlacedObject { id = Guid.NewGuid().ToString("N"), assetId = id, name = entry.label, x = Snap(at.x), z = -Snap(at.y), y = entry.defaultY, yaw = entry.defaultYaw, footprintWidth = entry.defaultFootprintXZ?[0] ?? 2f, footprintDepth = entry.defaultFootprintXZ?[1] ?? 2f, maxHeight = entry.defaultMaxHeight, scale = 1f, collision = "none" };
            Edit(d => d.objects = d.objects.Concat(new[] { item }).ToArray()); canvas.SetSelection(new[] { item.id }); RefreshInspector();
        }
        void Duplicate()
        {
            var selected = doc.objects.Where(o => canvas.Selection.Contains(o.id)).ToArray(); if (selected.Length == 0) return; var ids = new List<string>();
            Edit(d => { var list = d.objects.ToList(); foreach (var item in selected) { var c = JsonUtility.FromJson<PlacedObject>(JsonUtility.ToJson(item)); c.id = Guid.NewGuid().ToString("N"); c.name += " copy"; c.x += .5f; c.z -= .5f; list.Add(c); ids.Add(c.id); } d.objects = list.ToArray(); }); canvas.SetSelection(ids); RefreshInspector();
        }
        void Delete() { if (canvas.Selection.Count == 0) return; Edit(d => d.objects = d.objects.Where(o => !canvas.Selection.Contains(o.id)).ToArray()); canvas.SetSelection(Array.Empty<string>()); RefreshInspector(); }
        void RefreshAll(bool resetSelection = true)
        {
            if (canvas == null || doc == null) return; var selected = resetSelection ? Array.Empty<string>() : canvas.Selection.ToArray(); canvas.SetDocument(doc); canvas.SetSelection(selected); preview.SetDocument(doc); RefreshInspector();
            heading.text = "Floor " + floorNumber + "  ·  " + doc.layoutId + (Dirty ? "  • unsaved" : "");
            var report = FloorDocuments.Validate(doc); reportLabel.text = report + "\n" + string.Join("\n", report.issues.Take(5).Select(i => i.message)); reportLabel.EnableInClassList("invalid", !report.IsValid); UpdateStatus();
        }
        void RefreshInspector()
        {
            if (inspector == null || doc == null) return; inspector.Clear(); preview?.SetSelection(canvas.Selection);
            var selected = doc.objects.Where(o => canvas.Selection.Contains(o.id)).ToArray();
            var zf = SectorDefs.Get(sectorId).floors[floorNumber - 1];
            if (selected.Length == 0)
            {
                inspector.Add(new Label("FLOOR · " + zf.spec) { name = "inspector-title" });
                var title = new TextField("Title") { value = doc.title, isDelayed = true }; title.RegisterValueChangedCallback(e => Edit(d => d.title = e.newValue)); inspector.Add(title);
                if (FloorDocuments.RowsDifferFromOriginal(doc)) inspector.Add(new Label("Edited collision: the game generates matching room geometry for this floor. The baked Cryo room is used only when its original grid still matches."));
                inspector.Add(new Label("2 m cells · " + doc.rows[0].Length + " × " + doc.rows.Length + "\n" + doc.objects.Length + " placed objects\n" + zf.param));
                inspector.Add(new Label("Enemy roster: " + string.Join(", ", zf.enemies?.Select(e => e.type + " " + e.weight) ?? Array.Empty<string>())));
                inspector.Add(new Label("Paint S to move enemy entry points. Paint the existing objective markers for this mode. Enemy budgets and event scripts stay in the current zone spec."));
                inspector.Add(new Label("Visual-only objects add no collision. Choose Solid footprint explicitly to add blocked cells. Wall and door grid tools control the actual gameplay map."));
                foreach (var o in doc.objects) inspector.Add(Button(o.name, () => { canvas.SetSelection(new[] { o.id }); canvas.FrameSelection(); RefreshInspector(); }));
                return;
            }
            inspector.Add(new Label(selected.Length == 1 ? selected[0].name : selected.Length + " objects selected") { name = "inspector-title" });
            if (selected.Length > 1) { inspector.Add(new Label("Drag to move together. Rotate, duplicate and delete apply to the selection.")); return; }
            var item = selected[0];
            if (FloorModelCatalog.TryGet(item.assetId, out var model))
            {
                inspector.Add(new Label(model.unrigged ? "UNRIGGED STATIC PREVIEW — no animation, attacks or AI." : "Local batch static prop · selected " + model.selectedVersion));
                int levelIndex = Array.IndexOf(model.sourceLevels, model.mobileFirstLevel);
                inspector.Add(new Label("Mobile default: source LOD" + model.mobileFirstLevel + " · " + model.triangles[levelIndex] + " triangles. Bounds measured from selected meshes."));
            }
            var name = new TextField("Name") { value = item.name, isDelayed = true }; name.RegisterValueChangedCallback(e => Edit(d => item.name = e.newValue)); inspector.Add(name);
            Number("X / metres", item.x, v => item.x = v); Number("Height", item.y, v => item.y = v); Number("Z / metres", item.z, v => item.z = v); Number("Yaw / degrees", item.yaw, v => item.yaw = v); Number("Uniform scale", item.scale, v => item.scale = Mathf.Clamp(v, .1f, 3f));
            Number("Fit width", item.footprintWidth, v => item.footprintWidth = Mathf.Max(.1f, v)); Number("Fit depth", item.footprintDepth, v => item.footprintDepth = Mathf.Max(.1f, v)); Number("Fit max height", item.maxHeight, v => item.maxHeight = Mathf.Max(.1f, v));
            var collision = new PopupField<string>("Collision", new List<string> { "none", "existing", "solid" }, Mathf.Max(0, new List<string> { "none", "existing", "solid" }.IndexOf(item.collision))); collision.RegisterValueChangedCallback(e => Edit(d => item.collision = e.newValue)); inspector.Add(collision);
            if (!FloorModelCatalog.IsLocalId(item.assetId)) { var behavior = new Toggle("Keep asset behavior") { value = item.preserveBehavior }; behavior.RegisterValueChangedCallback(e => Edit(d => item.preserveBehavior = e.newValue)); inspector.Add(behavior); }
            inspector.Add(new Label("none = decorative\nexisting = validate against base solids\nsolid = stamp conservative blocked cells\nProps use static visuals; neutral turret markers T retain gameplay behavior."));
        }
        void Number(string label, float value, Action<float> set) { var f = new FloatField(label) { value = value, isDelayed = true }; f.RegisterValueChangedCallback(e => Edit(d => set(e.newValue))); inspector.Add(f); }
        void Keys(KeyDownEvent e)
        {
            if (e.target is TextElement || e.target is TextField || e.target is FloatField) return;
            bool command = e.ctrlKey || e.commandKey;
            if (command && e.keyCode == KeyCode.Z) { if (e.shiftKey) RedoEdit(); else UndoEdit(); }
            else if (command && e.keyCode == KeyCode.Y) RedoEdit(); else if (command && e.keyCode == KeyCode.S) Save(); else if (command && e.keyCode == KeyCode.D) Duplicate();
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) Delete(); else if (e.keyCode == KeyCode.Q) canvas.RotateSelection(-15); else if (e.keyCode == KeyCode.E) canvas.RotateSelection(15); else if (e.keyCode == KeyCode.F) canvas.FrameSelection(); else if (e.keyCode == KeyCode.Escape) Tool(null, null); else return;
            e.StopPropagation();
        }
        void UpdateStatus() { if (status != null) status.text = FloorPlaytest.Active ? FloorPlaytest.Status : (Dirty ? "Unsaved draft · " : "Ready · ") + SavePath; }
        void Note(string text) { if (status != null) status.text = text; }
        sealed class PaletteDrag : PointerManipulator
        {
            readonly FloorEditorWindow owner; readonly string id; Vector2 start; bool down;
            public PaletteDrag(FloorEditorWindow w, string asset) { owner = w; id = asset; }
            protected override void RegisterCallbacksOnTarget() { target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerMoveEvent>(Move); }
            protected override void UnregisterCallbacksFromTarget() { target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerMoveEvent>(Move); }
            void Down(PointerDownEvent e) { if (e.button != 0) return; start = e.position; down = true; target.CapturePointer(e.pointerId); owner.Tool(id, null); target.AddToClassList("dragging"); e.StopPropagation(); }
            void Move(PointerMoveEvent e) { if (down) owner.Note("Drop " + id + " onto the floor plan"); }
            void Up(PointerUpEvent e) { if (!down) return; down = false; target.ReleasePointer(e.pointerId); target.RemoveFromClassList("dragging"); if ((new Vector2(e.position.x, e.position.y) - start).sqrMagnitude > 36 && owner.canvas.worldBound.Contains(e.position)) owner.Place(id, owner.canvas.WorldAtPanel(e.position)); e.StopPropagation(); }
        }
    }
}
