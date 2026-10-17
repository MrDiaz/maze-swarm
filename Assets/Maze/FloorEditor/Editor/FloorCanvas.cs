using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maze.FloorEditing.Editor
{
    /// <summary>Top-down view in world metres. Positive plan Y is negative Unity Z.</summary>
    public sealed class FloorCanvas : VisualElement
    {
        public FloorDocument Document { get; private set; }
        public IReadOnlyCollection<string> Selection => selection;
        public Action BeforeEdit, Changed, SelectionChanged;
        public Action<string, Vector2> PlaceRequested;
        public Action<Vector2Int, char> HoveredCell;

        public string ActiveAssetId { get => activeAssetId; set { activeAssetId = value; MarkDirtyRepaint(); } }
        public char? PaintCell { get => paintCell; set { paintCell = value; MarkDirtyRepaint(); } }
        public bool Snap = true;
        public bool ShowCollision { get => showCollision; set { showCollision = value; MarkDirtyRepaint(); } }
        public bool ShowPaths { get => showPaths; set { showPaths = value; MarkDirtyRepaint(); } }
        public bool ShowMarkers { get => showMarkers; set { showMarkers = value; RebuildLabels(); MarkDirtyRepaint(); } }
        public float PixelsPerMetre => pixelsPerMetre;

        readonly HashSet<string> selection = new HashSet<string>();
        readonly List<PlanLabel> labels = new List<PlanLabel>();
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
        readonly CanvasManipulator interaction;
        string activeAssetId;
        char? paintCell;
        bool showCollision = true, showPaths = true, showMarkers = true;
        string[] effectiveRows = Array.Empty<string>();
        bool[] reachable = Array.Empty<bool>();
        Vector2 origin = new Vector2(24, 24), hoverLocal;
        float pixelsPerMetre = 12;
        bool haveHover, firstDocument = true;
        int width, height;
        bool boxVisible;
        Rect box;

        sealed class PlanLabel
        {
            public Label element;
            public Vector2 world;
            public bool prop;
        }

        public FloorCanvas()
        {
            name = "floorPlanCanvas";
            AddToClassList("floor-plan-canvas");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Maze/FloorEditor/Editor/FloorCanvas.uss");
            if (sheet != null) styleSheets.Add(sheet);
            focusable = true;
            tooltip = "Select and drag objects. Shift adds to selection. Drag empty floor to select a group. Middle mouse or Alt-drag pans; wheel zooms. Grid cells are 2 m; object snap is 0.5 m.";
            generateVisualContent += Draw;
            interaction = new CanvasManipulator(this);
            this.AddManipulator(interaction);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(_ => { if (firstDocument && Document != null) Fit(); else UpdateLabelPositions(); });
            RegisterCallback<PointerLeaveEvent>(_ => { haveHover = false; MarkDirtyRepaint(); });
            // AssetPreview renders asynchronously in the Editor. Its cache is read outside painting.
            schedule.Execute(RefreshPreviews).Every(700);
        }

        public void SetDocument(FloorDocument doc)
        {
            interaction.Cancel();
            bool different = Document == null || doc == null || Document.layoutId != doc.layoutId;
            Document = doc;
            if (doc == null) selection.Clear();
            else selection.RemoveWhere(id => !(doc.objects ?? Array.Empty<PlacedObject>()).Any(o => o != null && o.id == id));
            RebuildData();
            RebuildLabels();
            if (different || firstDocument) Fit();
            MarkDirtyRepaint();
        }

        /// <summary>Call after inspector edits to the current document.</summary>
        public void Refresh()
        {
            RebuildData();
            RebuildLabels();
            MarkDirtyRepaint();
        }

        public void SetSelection(IEnumerable<string> ids)
        {
            var next = new HashSet<string>(ids ?? Array.Empty<string>());
            next.RemoveWhere(id => !(Document?.objects ?? Array.Empty<PlacedObject>()).Any(o => o != null && o.id == id));
            if (selection.SetEquals(next)) return;
            selection.Clear(); selection.UnionWith(next);
            SelectionChanged?.Invoke();
            MarkDirtyRepaint();
        }

        public Vector2 WorldAtPanel(Vector2 panelPosition) => LocalToWorldMetres(this.WorldToLocal(panelPosition));
        public Vector2 PanelAtWorld(Vector2 worldPosition) => this.LocalToWorld(ToLocal(worldPosition));

        public bool ContainsPanelPosition(Vector2 panelPosition) => worldBound.Contains(panelPosition);

        /// <summary>Suitable for a palette's DragPerformEvent as well as pointer drops.</summary>
        public bool DropAsset(string assetId, Vector2 panelPosition)
        {
            if (Document == null || !ContainsPanelPosition(panelPosition) || string.IsNullOrEmpty(assetId)) return false;
            var at = WorldAtPanel(panelPosition);
            if (!InsideInterior(at)) return false;
            PlaceRequested?.Invoke(assetId, SnapPoint(at));
            return true;
        }

        public void Fit()
        {
            if (width <= 0 || height <= 0 || contentRect.width < 40 || contentRect.height < 40) return;
            pixelsPerMetre = Mathf.Clamp(Mathf.Min((contentRect.width - 64) / (width * 2f), (contentRect.height - 64) / (height * 2f)), .75f, 120f);
            origin = new Vector2((contentRect.width - width * 2 * pixelsPerMetre) * .5f, (contentRect.height - height * 2 * pixelsPerMetre) * .5f);
            firstDocument = false;
            UpdateLabelPositions(); MarkDirtyRepaint();
        }

        public void FrameSelection()
        {
            var selected = Selected().ToArray();
            if (selected.Length == 0) { Fit(); return; }
            var bounds = ObjectBounds(selected[0]);
            for (int i = 1; i < selected.Length; i++) bounds = Union(bounds, ObjectBounds(selected[i]));
            float w = Mathf.Max(bounds.width + 8, 12), h = Mathf.Max(bounds.height + 8, 12);
            pixelsPerMetre = Mathf.Clamp(Mathf.Min(contentRect.width / w, contentRect.height / h), 2, 80);
            origin = contentRect.size * .5f - bounds.center * pixelsPerMetre;
            UpdateLabelPositions(); MarkDirtyRepaint();
        }

        public void RotateSelection(float degrees)
        {
            var selected = Selected().ToArray();
            if (selected.Length == 0 || Mathf.Abs(degrees) < .0001f) return;
            BeforeEdit?.Invoke();
            foreach (var o in selected) o.yaw = Mathf.Repeat(o.yaw + degrees, 360);
            Refresh(); Changed?.Invoke();
        }

        public void MoveSelection(Vector2 deltaWorld)
        {
            var selected = Selected().ToArray();
            if (selected.Length == 0 || deltaWorld.sqrMagnitude < .0000001f) return;
            BeforeEdit?.Invoke();
            foreach (var o in selected) { o.x += deltaWorld.x; o.z -= deltaWorld.y; }
            Refresh(); Changed?.Invoke();
        }

        IEnumerable<PlacedObject> Selected() => (Document?.objects ?? Array.Empty<PlacedObject>()).Where(o => o != null && selection.Contains(o.id));

        void RebuildData()
        {
            effectiveRows = Array.Empty<string>(); reachable = Array.Empty<bool>(); width = height = 0;
            if (Document?.rows == null || Document.rows.Length == 0) return;
            height = Mathf.Min(Document.rows.Length, 256);
            width = Mathf.Min(Document.rows.Max(r => r?.Length ?? 0), 256);
            effectiveRows = FloorDocuments.BuildRows(Document);
            reachable = FloorDocuments.ReachableCells(Document);
        }

        void RefreshPreviews()
        {
            bool changed = false;
            foreach (var o in Document?.objects ?? Array.Empty<PlacedObject>())
            {
                if (o == null || string.IsNullOrEmpty(o.assetId) || previews.ContainsKey(o.assetId)) continue;
                if (FloorModelCatalog.IsLocalId(o.assetId))
                {
                    var thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Maze/FloorEditor/Editor/previews/" + o.assetId + ".png");
                    if (thumbnail != null) { previews.Add(o.assetId, thumbnail); changed = true; continue; }
                }
                var resource = FloorObjectVisuals.ResourceFor(o.assetId);
                var prefab = resource == null ? null : Resources.Load<GameObject>(resource);
                if (prefab == null) continue;
                var preview = AssetPreview.GetAssetPreview(prefab);
                if (preview != null) { previews.Add(o.assetId, preview); changed = true; }
            }
            if (changed) MarkDirtyRepaint();
        }

        void RebuildLabels()
        {
            foreach (var label in labels) label.element.RemoveFromHierarchy();
            labels.Clear();
            if (Document == null) return;
            for (int x = 0; x < width; x += 2) AddPlanLabel(x.ToString(), new Vector2(x * 2 + 1, -.7f), "floor-plan-ruler-label");
            for (int y = 0; y < height; y += 2) AddPlanLabel(y.ToString(), new Vector2(-.8f, y * 2 + 1), "floor-plan-ruler-label");
            if (ShowMarkers)
                for (int row = 0; row < height; row++) for (int col = 0; col < Math.Min(width, Document.rows[row]?.Length ?? 0); col++)
                {
                    char c = Document.rows[row][col];
                    if ("PSDZLGHR$!Q+@nXJ".IndexOf(c) < 0) continue;
                    AddPlanLabel(c.ToString(), new Vector2(col * 2 + 1, row * 2 + 1), c == 'P' ? "floor-plan-player-label" : c == 'S' ? "floor-plan-enemy-label" : "floor-plan-objective-label");
                }
            foreach (var o in Document.objects ?? Array.Empty<PlacedObject>())
            {
                if (!Drawable(o)) continue;
                var label = AddPlanLabel(ShortName(o), new Vector2(o.x, -o.z), "floor-plan-prop-label", true);
                label.tooltip = (o.name ?? o.assetId) + "\n" + o.assetId + "\nCollision: " + o.collision;
            }
            UpdateLabelPositions();
        }

        Label AddPlanLabel(string text, Vector2 world, string className, bool prop = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("floor-plan-label"); label.AddToClassList(className);
            Add(label); labels.Add(new PlanLabel { element = label, world = world, prop = prop });
            return label;
        }

        void UpdateLabelPositions()
        {
            foreach (var label in labels)
            {
                var p = ToLocal(label.world);
                label.element.style.translate = new StyleTranslate(new Translate(p.x - (label.prop ? 66 : 12), p.y + (label.prop ? 10 : -10), 0));
                label.element.EnableInClassList("floor-plan-label-hidden", label.prop ? pixelsPerMetre < 10 : pixelsPerMetre < 4);
            }
        }

        static string ShortName(PlacedObject o)
        {
            string text = !string.IsNullOrEmpty(o.name) ? o.name : (o.assetId ?? "object").Replace("cryo-", "").Replace('-', ' ');
            return text.Length > 21 ? text.Substring(0, 19) + "…" : text;
        }

        Vector2 ToLocal(Vector2 world) => origin + world * pixelsPerMetre;
        Vector2 LocalToWorldMetres(Vector2 local) => (local - origin) / pixelsPerMetre;
        Vector2 SnapPoint(Vector2 at) => Snap ? new Vector2(Mathf.Round(at.x * 2) * .5f, Mathf.Round(at.y * 2) * .5f) : at;
        bool InsideInterior(Vector2 at) => at.x >= 2 && at.y >= 2 && at.x < (width - 1) * 2 && at.y < (height - 1) * 2;

        void OnWheel(WheelEvent evt)
        {
            if (Document == null) return;
            var local = this.WorldToLocal(evt.mousePosition);
            var at = LocalToWorldMetres(local);
            pixelsPerMetre = Mathf.Clamp(pixelsPerMetre * Mathf.Pow(1.12f, -evt.delta.y), .75f, 120f);
            origin = local - at * pixelsPerMetre;
            UpdateLabelPositions(); MarkDirtyRepaint(); evt.StopPropagation();
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (contentRect.width < 1 || contentRect.height < 1 || Document == null) return;
            var p = ctx.painter2D;
            float cell = 2 * pixelsPerMetre;
            Rect visible = contentRect;
            for (int y = 0; y < height; y++) for (int x = 0; x < Math.Min(width, effectiveRows[y]?.Length ?? 0); x++)
            {
                var rect = new Rect(ToLocal(new Vector2(x * 2, y * 2)), new Vector2(cell, cell));
                if (!visible.Overlaps(rect)) continue;
                char c = effectiveRows[y][x];
                FillRect(p, rect, CellColor(c));
                bool solid = FloorDocuments.IsBaseSolid(c);
                int index = y * width + x;
                if (ShowPaths && !solid && index < reachable.Length)
                {
                    var color = reachable[index] ? new Color(.21f, .73f, .6f, .08f) : new Color(1f, .35f, .24f, .25f);
                    FillRect(p, rect, color);
                }
                if (ShowCollision && solid)
                {
                    StrokeRect(p, Inset(rect, 2), new Color(.83f, .65f, .35f, .42f), 1);
                    if (cell > 12 && c != '#') Line(p, rect.min + new Vector2(3, 3), rect.max - new Vector2(3, 3), new Color(.92f, .75f, .43f, .25f), 1);
                }
                if (ShowMarkers && (c == 'P' || c == 'S'))
                    Circle(p, rect.center, Mathf.Clamp(cell * .37f, 4, 17), c == 'P' ? new Color(.35f, 1, .82f, .8f) : new Color(1, .45f, .32f, .8f), 1.5f);
            }
            var bounds = new Rect(origin, new Vector2(width * cell, height * cell));
            if (cell >= 7)
            {
                var grid = new Color(.51f, .68f, .8f, .12f);
                for (int x = 0; x <= width; x++) Line(p, origin + new Vector2(x * cell, 0), origin + new Vector2(x * cell, height * cell), grid, 1);
                for (int y = 0; y <= height; y++) Line(p, origin + new Vector2(0, y * cell), origin + new Vector2(width * cell, y * cell), grid, 1);
            }
            StrokeRect(p, bounds, new Color(.36f, .57f, .72f, .9f), 1.5f);
            foreach (var o in Document.objects ?? Array.Empty<PlacedObject>()) if (o != null) DrawObject(ctx, p, o);
            if (haveHover && contentRect.Contains(hoverLocal))
            {
                var world = LocalToWorldMetres(hoverLocal);
                int x = Mathf.FloorToInt(world.x / 2), y = Mathf.FloorToInt(world.y / 2);
                if (x >= 0 && y >= 0 && x < width && y < height)
                {
                    var cellRect = new Rect(ToLocal(new Vector2(x * 2, y * 2)), new Vector2(cell, cell));
                    StrokeRect(p, Inset(cellRect, 1), paintCell.HasValue ? new Color(1, .86f, .44f, .95f) : new Color(.58f, .83f, .98f, .5f), 2);
                    if (!string.IsNullOrEmpty(activeAssetId) && !paintCell.HasValue)
                    {
                        var q = ToLocal(SnapPoint(world));
                        Line(p, q - Vector2.right * 8, q + Vector2.right * 8, new Color(.5f, 1, .8f), 2);
                        Line(p, q - Vector2.up * 8, q + Vector2.up * 8, new Color(.5f, 1, .8f), 2);
                    }
                }
            }
            if (boxVisible)
            {
                FillRect(p, box, new Color(.3f, .7f, 1, .12f));
                StrokeRect(p, box, new Color(.4f, .8f, 1, .9f), 1);
            }
        }

        void DrawObject(MeshGenerationContext ctx, Painter2D p, PlacedObject o)
        {
            if (!Drawable(o)) return;
            var bounds = ObjectBounds(o);
            if (!contentRect.Overlaps(new Rect(ToLocal(bounds.min), bounds.size * pixelsPerMetre))) return;
            var corners = ObjectCorners(o);
            for (int i = 0; i < corners.Length; i++) corners[i] = ToLocal(corners[i]);
            bool selected = selection.Contains(o.id);
            Color tint = selected ? new Color(.35f, .9f, 1, .2f) : o.collision == "solid" ? new Color(.95f, .68f, .28f, .16f) : new Color(.41f, .65f, .83f, .16f);
            Polygon(p, corners, tint, selected ? new Color(.39f, .94f, 1, 1) : new Color(.59f, .76f, .87f, .85f), selected ? 2.5f : 1.2f);
            var center = ToLocal(new Vector2(o.x, -o.z));
            float yaw = o.yaw * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
            float radius = Mathf.Min(EffectiveWidth(o), EffectiveDepth(o)) * pixelsPerMetre * .25f;
            radius = Mathf.Clamp(radius, 3, 14);
            if (previews.TryGetValue(o.assetId ?? "", out var image) && image != null && pixelsPerMetre >= 7)
            {
                float size = Mathf.Min(40, Mathf.Min(EffectiveWidth(o), EffectiveDepth(o)) * pixelsPerMetre * .76f);
                if (size >= 9) DrawTexture(ctx, new Rect(center - Vector2.one * size * .5f, Vector2.one * size), image);
            }
            else DrawPropSymbol(p, o.assetId ?? "", center, forward, radius);
            var nose = center + forward * Mathf.Max(radius + 3, EffectiveDepth(o) * pixelsPerMetre * .5f);
            Line(p, center + forward * radius * .5f, nose, new Color(.71f, .9f, 1, .95f), 1.5f);
            Line(p, nose, nose - forward * 5 + new Vector2(forward.y, -forward.x) * 3, new Color(.71f, .9f, 1, .95f), 1.5f);
            Line(p, nose, nose - forward * 5 - new Vector2(forward.y, -forward.x) * 3, new Color(.71f, .9f, 1, .95f), 1.5f);
            if (selected) foreach (var c in corners) FillRect(p, new Rect(c - Vector2.one * 2, Vector2.one * 4), new Color(.61f, 1, 1));
        }

        static void DrawTexture(MeshGenerationContext ctx, Rect rect, Texture texture)
        {
            var mesh = ctx.Allocate(4, 6, texture);
            var uv = new Rect(0, 0, 1, 1);
            var color = (Color32)Color.white;
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = color, uv = new Vector2(uv.xMin, uv.yMax) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = color, uv = new Vector2(uv.xMax, uv.yMax) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = color, uv = new Vector2(uv.xMax, uv.yMin) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = color, uv = new Vector2(uv.xMin, uv.yMin) });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2); mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
        }

        static void DrawPropSymbol(Painter2D p, string id, Vector2 at, Vector2 forward, float r)
        {
            Color c = new Color(.64f, .85f, .95f, .8f);
            if (id.Contains("turret")) { Circle(p, at, r, c, 1.5f); Line(p, at, at + forward * r * 1.5f, c, 3); }
            else if (id.Contains("tank") || id.Contains("reactor") || id.Contains("canister")) { Circle(p, at, r, c, 1.2f); Circle(p, at, r * .52f, c, 1); }
            else if (id.Contains("door")) { Line(p, at + new Vector2(-r, -r), at + new Vector2(-r, r), c, 2); Line(p, at + new Vector2(r, -r), at + new Vector2(r, r), c, 2); Line(p, at + Vector2.left * r, at + Vector2.right * r, c, 2); }
            else { StrokeRect(p, new Rect(at - Vector2.one * r, Vector2.one * r * 2), c, 1.2f); Line(p, at - Vector2.one * r, at + Vector2.one * r, c, 1); Line(p, at + new Vector2(-r, r), at + new Vector2(r, -r), c, 1); }
        }

        static float EffectiveWidth(PlacedObject o) => Mathf.Max(.8f, o.footprintWidth > 0 ? o.footprintWidth * o.scale : 1.5f * o.scale);
        static float EffectiveDepth(PlacedObject o) => Mathf.Max(.8f, o.footprintDepth > 0 ? o.footprintDepth * o.scale : 1.5f * o.scale);
        static bool Drawable(PlacedObject o) => o != null && Finite(o.x) && Finite(o.z) && Finite(o.yaw) && Finite(o.scale) && Finite(o.footprintWidth) && Finite(o.footprintDepth);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static Vector2[] ObjectCorners(PlacedObject o)
        {
            float w = EffectiveWidth(o) * .5f, d = EffectiveDepth(o) * .5f;
            float angle = (o.legacyFit ? 0 : o.yaw) * Mathf.Deg2Rad, co = Mathf.Cos(angle), si = Mathf.Sin(angle);
            var corners = new[] { new Vector2(-w, -d), new Vector2(w, -d), new Vector2(w, d), new Vector2(-w, d) };
            for (int i = 0; i < corners.Length; i++) { var v = corners[i]; corners[i] = new Vector2(o.x + v.x * co - v.y * si, -o.z + v.x * si + v.y * co); }
            return corners;
        }
        static Rect ObjectBounds(PlacedObject o)
        {
            var c = ObjectCorners(o); var min = c[0]; var max = c[0];
            foreach (var p in c) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        static Rect Inset(Rect r, float value) => new Rect(r.x + value, r.y + value, Mathf.Max(0, r.width - value * 2), Mathf.Max(0, r.height - value * 2));
        static void FillRect(Painter2D p, Rect rect, Color c) { p.fillColor = c; TraceRect(p, rect); p.Fill(); }
        static void StrokeRect(Painter2D p, Rect rect, Color c, float line) { p.strokeColor = c; p.lineWidth = line; TraceRect(p, rect); p.Stroke(); }
        static void TraceRect(Painter2D p, Rect r) { p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath(); }
        static void Line(Painter2D p, Vector2 from, Vector2 to, Color c, float line) { p.strokeColor = c; p.lineWidth = line; p.BeginPath(); p.MoveTo(from); p.LineTo(to); p.Stroke(); }
        static void Circle(Painter2D p, Vector2 at, float r, Color c, float line) { p.strokeColor = c; p.lineWidth = line; p.BeginPath(); p.Arc(at, r, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke(); }
        static void Polygon(Painter2D p, Vector2[] points, Color fill, Color stroke, float line) { p.fillColor = fill; p.strokeColor = stroke; p.lineWidth = line; p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.ClosePath(); p.Fill(); p.Stroke(); }
        static Color CellColor(char c)
        {
            switch (c)
            {
                case '#': return new Color(.14f, .21f, .29f);
                case 'B': return new Color(.24f, .31f, .38f);
                case 'C': return new Color(.29f, .35f, .4f);
                case 'r': return new Color(.31f, .3f, .24f);
                case 'D': return new Color(.48f, .38f, .22f);
                case '~': return new Color(.12f, .25f, .29f);
                case 'y': return new Color(.28f, .27f, .17f);
                case 'i': return new Color(.19f, .34f, .39f);
                case 'o': case 'e': return new Color(.2f, .29f, .32f);
                case 'P': return new Color(.13f, .29f, .27f);
                case 'S': return new Color(.31f, .19f, .2f);
                default: return new Color(.17f, .24f, .29f);
            }
        }

        sealed class CanvasManipulator : PointerManipulator
        {
            enum Gesture { None, Pan, Move, Paint, Box }
            readonly FloorCanvas view;
            Gesture gesture;
            int pointerId;
            Vector2 down, startOrigin, startWorld;
            Vector2Int lastPaint;
            bool transaction, changed, shiftBox;
            readonly Dictionary<string, Vector2> starts = new Dictionary<string, Vector2>();
            readonly HashSet<string> boxStartSelection = new HashSet<string>();

            public CanvasManipulator(FloorCanvas view) { this.view = view; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(CaptureOut);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(CaptureOut);
            }
            public void Cancel() { Finish(false); }

            void Down(PointerDownEvent e)
            {
                if (gesture != Gesture.None || view.Document == null || (e.button != 0 && e.button != 2)) return;
                view.Focus(); down = view.WorldToLocal(e.position); startWorld = view.LocalToWorldMetres(down); startOrigin = view.origin;
                pointerId = e.pointerId; transaction = changed = false;
                if (e.button == 2 || e.altKey) gesture = Gesture.Pan;
                else if (view.PaintCell.HasValue)
                {
                    gesture = Gesture.Paint; lastPaint = CellAt(startWorld); Paint(lastPaint);
                }
                else if (!string.IsNullOrEmpty(view.ActiveAssetId))
                {
                    if (view.InsideInterior(startWorld)) view.PlaceRequested?.Invoke(view.ActiveAssetId, view.SnapPoint(startWorld));
                    e.StopPropagation(); return;
                }
                else
                {
                    var hit = Hit(startWorld);
                    if (hit != null)
                    {
                        if (e.shiftKey)
                        {
                            var next = new HashSet<string>(view.selection); if (!next.Add(hit.id)) next.Remove(hit.id); view.SetSelection(next);
                            if (!view.selection.Contains(hit.id)) { e.StopPropagation(); return; }
                        }
                        else if (!view.selection.Contains(hit.id)) view.SetSelection(new[] { hit.id });
                        if (e.clickCount == 2) { view.FrameSelection(); e.StopPropagation(); return; }
                        starts.Clear(); foreach (var o in view.Selected()) starts[o.id] = new Vector2(o.x, -o.z);
                        gesture = Gesture.Move;
                    }
                    else
                    {
                        gesture = Gesture.Box; shiftBox = e.shiftKey;
                        boxStartSelection.Clear(); if (shiftBox) boxStartSelection.UnionWith(view.selection);
                        if (!shiftBox) view.SetSelection(Array.Empty<string>());
                    }
                }
                target.CapturePointer(pointerId); e.StopPropagation();
            }

            void Move(PointerMoveEvent e)
            {
                var local = view.WorldToLocal(e.position); view.hoverLocal = local; view.haveHover = true;
                var world = view.LocalToWorldMetres(local); var cell = CellAt(world);
                var doc = view.Document;
                char glyph = doc?.rows != null && cell.y >= 0 && cell.y < doc.rows.Length && cell.x >= 0 && cell.x < (doc.rows[cell.y]?.Length ?? 0) ? doc.rows[cell.y][cell.x] : ' ';
                view.HoveredCell?.Invoke(cell, glyph);
                if (gesture == Gesture.Pan)
                {
                    view.origin = startOrigin + local - down; view.UpdateLabelPositions();
                }
                else if (gesture == Gesture.Move && (local - down).sqrMagnitude > 4)
                {
                    Vector2 delta = world - startWorld;
                    if (view.Snap && starts.Count > 0)
                    {
                        var anchor = starts.First().Value; delta = view.SnapPoint(anchor + delta) - anchor;
                    }
                    if (transaction || delta.sqrMagnitude > .000001f)
                    {
                        BeginEdit();
                        foreach (var o in view.Selected()) if (starts.TryGetValue(o.id, out var initial)) { o.x = initial.x + delta.x; o.z = -(initial.y + delta.y); }
                        changed = true;
                        // Recompute occupancy while dragging; retained label positions follow the document.
                        view.RebuildData(); view.RebuildLabels();
                    }
                }
                else if (gesture == Gesture.Paint)
                {
                    PaintLine(lastPaint, cell); lastPaint = cell;
                }
                else if (gesture == Gesture.Box)
                {
                    view.boxVisible = (local - down).sqrMagnitude > 9;
                    view.box = Rect.MinMaxRect(Mathf.Min(down.x, local.x), Mathf.Min(down.y, local.y), Mathf.Max(down.x, local.x), Mathf.Max(down.y, local.y));
                }
                view.MarkDirtyRepaint();
                if (gesture != Gesture.None) e.StopPropagation();
            }

            void Up(PointerUpEvent e)
            {
                if (gesture == Gesture.None || e.pointerId != pointerId) return;
                Finish(true); e.StopPropagation();
            }
            void CaptureOut(PointerCaptureOutEvent e) { if (gesture != Gesture.None && e.pointerId == pointerId) Finish(true); }
            void Finish(bool publish)
            {
                if (gesture == Gesture.None) return;
                var finished = gesture; gesture = Gesture.None;
                if (finished == Gesture.Box && publish && view.boxVisible)
                {
                    var next = new HashSet<string>(boxStartSelection);
                    foreach (var o in view.Document?.objects ?? Array.Empty<PlacedObject>()) if (o != null && view.box.Contains(view.ToLocal(new Vector2(o.x, -o.z)))) next.Add(o.id);
                    view.SetSelection(next);
                }
                view.boxVisible = false;
                if (target.HasPointerCapture(pointerId)) target.ReleasePointer(pointerId);
                if (changed) { view.Refresh(); if (publish) view.Changed?.Invoke(); }
                transaction = changed = false;
                view.MarkDirtyRepaint();
            }
            void BeginEdit() { if (transaction) return; view.BeforeEdit?.Invoke(); transaction = true; }
            static Vector2Int CellAt(Vector2 world) => new Vector2Int(Mathf.FloorToInt(world.x / 2), Mathf.FloorToInt(world.y / 2));
            PlacedObject Hit(Vector2 world)
            {
                var objects = view.Document.objects ?? Array.Empty<PlacedObject>();
                for (int i = objects.Length - 1; i >= 0; i--)
                {
                    var o = objects[i]; if (!Drawable(o)) continue;
                    float dx = world.x - o.x, dy = world.y + o.z, rad = (o.legacyFit ? 0 : o.yaw) * Mathf.Deg2Rad;
                    float u = dx * Mathf.Cos(rad) + dy * Mathf.Sin(rad), v = -dx * Mathf.Sin(rad) + dy * Mathf.Cos(rad);
                    if (Mathf.Abs(u) <= EffectiveWidth(o) * .5f && Mathf.Abs(v) <= EffectiveDepth(o) * .5f) return o;
                }
                return null;
            }
            void Paint(Vector2Int at)
            {
                var doc = view.Document;
                if (!view.PaintCell.HasValue || doc?.rows == null || at.y <= 0 || at.y >= doc.rows.Length - 1 || at.x <= 0 || at.x >= (doc.rows[at.y]?.Length ?? 0) - 1) return;
                char c = view.PaintCell.Value;
                if (doc.rows[at.y][at.x] == c) return;
                BeginEdit();
                if (c == 'P')
                    for (int y = 1; y < doc.rows.Length - 1; y++)
                        if (doc.rows[y]?.IndexOf('P') >= 0) doc.rows[y] = doc.rows[y].Replace('P', '.');
                var row = doc.rows[at.y].ToCharArray(); row[at.x] = c; doc.rows[at.y] = new string(row);
                changed = true; view.RebuildData(); view.RebuildLabels();
            }
            void PaintLine(Vector2Int a, Vector2Int b)
            {
                // Keep a fast pointer stroke continuous without producing hundreds of undo records.
                int dx = Math.Abs(b.x - a.x), sx = a.x < b.x ? 1 : -1;
                int dy = -Math.Abs(b.y - a.y), sy = a.y < b.y ? 1 : -1, error = dx + dy;
                for (int count = 0; count < 1024; count++)
                {
                    Paint(a); if (a == b) break; int twice = 2 * error;
                    if (twice >= dy) { error += dy; a.x += sx; }
                    if (twice <= dx) { error += dx; a.y += sy; }
                }
            }
        }
    }
}
