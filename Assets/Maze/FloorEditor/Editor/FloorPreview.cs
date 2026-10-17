#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Maze.FloorEditing.Editor
{
    /// <summary>Disposable authoring view. All geometry and lights live in PreviewRenderUtility's scene.</summary>
    public sealed class FloorPreview : VisualElement, IDisposable
    {
        const string SheetPath = "Assets/Maze/FloorEditor/Editor/FloorPreview.uss";
        const string Help = "Cutaway grid shell · Real prop meshes · Drag: orbit · Right drag: pan · Wheel: zoom";
        readonly Image image;
        readonly Label caption;
        readonly HashSet<string> selection = new HashSet<string>();
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly Dictionary<string, Bounds> objectBounds = new Dictionary<string, Bounds>();
        PreviewRenderUtility preview;
        GameObject geometryRoot, selectionRoot;
        FloorDocument document;
        IVisualElementScheduledItem renderSchedule;
        Vector3 floorCenter, pivot;
        float radius = 20, yaw = -28, pitch = 58, zoom = 1;
        bool topView, dirty = true, disposed, needsRebuild = true;
        public Texture PreviewTexture => image.image;
        public string LastError { get; private set; }

        public FloorPreview()
        {
            AddToClassList("floor-preview");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(SheetPath);
            if (sheet != null) styleSheets.Add(sheet);
            var toolbar = new VisualElement(); toolbar.AddToClassList("floor-preview-toolbar");
            toolbar.Add(new Button(ResetCamera) { text = "Orbit / reset", tooltip = "Frame the entire floor in perspective." });
            toolbar.Add(new Button(SetTopView) { text = "Top", tooltip = "Orthographic view with row zero at the top." });
            toolbar.Add(new Button(FrameSelection) { text = "Frame selection", tooltip = "Centre the preview on selected objects." });
            Add(toolbar);
            image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Position };
            image.AddToClassList("floor-preview-image");
            image.AddManipulator(new OrbitManipulator(this));
            image.RegisterCallback<GeometryChangedEvent>(_ => RequestRender());
            Add(image);
            caption = new Label(Help); caption.AddToClassList("floor-preview-caption"); Add(caption);
            RegisterCallback<AttachToPanelEvent>(_ => {
                if (disposed) return;
                renderSchedule = schedule.Execute(RenderIfDirty).Every(33);
                needsRebuild = true; RequestRender();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => {
                renderSchedule?.Pause(); renderSchedule = null;
                ReleasePreview(); needsRebuild = true;
            });
        }

        public void SetDocument(FloorDocument doc)
        {
            if (disposed) return;
            string previousLayout = document?.layoutId;
            document = FloorDocuments.Clone(doc);
            needsRebuild = true;
            if (document == null) { ReleasePreview(); return; }
            if (document != null && document.rows != null && document.rows.Length > 0)
            {
                int width = 0; foreach (string row in document.rows) width = Math.Max(width, row?.Length ?? 0);
                floorCenter = new Vector3(width * FloorDocuments.CellSize * .5f, 0, -document.rows.Length * FloorDocuments.CellSize * .5f);
                radius = Mathf.Max(3, new Vector2(width, document.rows.Length).magnitude * FloorDocuments.CellSize * .5f);
            }
            if (previousLayout != document?.layoutId) ResetCamera();
            RequestRender();
        }

        public void SetSelection(IEnumerable<string> ids)
        {
            if (disposed) return;
            selection.Clear();
            if (ids != null) foreach (string id in ids) if (!string.IsNullOrEmpty(id)) selection.Add(id);
            if (!needsRebuild && preview != null) RebuildSelection();
            RequestRender();
        }

        public void ResetCamera()
        { pivot = floorCenter; yaw = -28; pitch = 58; zoom = 1; topView = false; RequestRender(); }
        public void SetTopView()
        { pivot = floorCenter; topView = true; zoom = 1; RequestRender(); }
        public void FrameSelection()
        {
            bool any = false; var bounds = new Bounds();
            foreach (string id in selection)
                if (objectBounds.TryGetValue(id, out var b)) { if (!any) bounds = b; else bounds.Encapsulate(b); any = true; }
            if (!any) { ResetCamera(); return; }
            pivot = bounds.center;
            zoom = Mathf.Clamp(Mathf.Max(1.6f, bounds.extents.magnitude * .95f) / Mathf.Max(1, radius), .025f, 6f);
            RequestRender();
        }

        public void RequestRender() { if (!disposed) dirty = true; }
        void RenderIfDirty()
        {
            if (disposed || !dirty || panel == null || !visible || image.contentRect.width < 2 || image.contentRect.height < 2) return;
            RenderNow();
        }

        /// <summary>Renders once; may also be called by an editor proof after assigning a document.</summary>
        public void RenderNow()
        {
            if (disposed || document == null) return;
            bool begun = false;
            try
            {
                EnsurePreview();
                if (needsRebuild) RebuildGeometry();
                int width = Mathf.Clamp(Mathf.RoundToInt(image.contentRect.width), 64, 1920);
                int height = Mathf.Clamp(Mathf.RoundToInt(image.contentRect.height), 64, 1440);
                // A detached proof can explicitly render a normal landscape image.
                if (image.contentRect.width < 2 || image.contentRect.height < 2) { width = 960; height = 540; }
                preview.BeginPreview(new Rect(0, 0, width, height), GUIStyle.none); begun = true;
                var camera = preview.camera;
                float aspect = (float)width / height;
                float fit = radius * 1.12f * Mathf.Max(1, 1 / aspect);
                camera.aspect = aspect;
                camera.orthographic = topView;
                camera.orthographicSize = fit * zoom;
                camera.fieldOfView = 36;
                float distance = fit / Mathf.Sin(camera.fieldOfView * Mathf.Deg2Rad * .5f) * zoom;
                Quaternion rotation = topView ? Quaternion.Euler(90, 0, 0) : Quaternion.Euler(pitch, yaw, 0);
                camera.transform.SetPositionAndRotation(pivot + rotation * (Vector3.back * distance), rotation);
                camera.nearClipPlane = .03f;
                camera.farClipPlane = Mathf.Max(200, distance + radius * 6 + 50);
                preview.Render(true, true);
                image.image = preview.EndPreview(); begun = false;
                image.MarkDirtyRepaint();
                LastError = null;
                caption.text = Help;
                caption.RemoveFromClassList("floor-preview-error");
                dirty = false;
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + ": " + e.Message;
                caption.text = "Preview unavailable: " + LastError;
                caption.AddToClassList("floor-preview-error");
                dirty = false;
            }
            finally
            {
                if (begun) { try { preview.EndPreview(); } catch (Exception) { } }
            }
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        void EnsurePreview()
        {
            if (preview != null) return;
            preview = new PreviewRenderUtility();
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.055f, .085f, .13f, 1);
            preview.camera.allowHDR = true;
            preview.camera.allowMSAA = false;
            preview.camera.cameraType = CameraType.Preview;
            preview.ambientColor = new Color(.32f, .39f, .48f, 1);
            preview.lights[0].type = LightType.Directional;
            preview.lights[0].color = new Color(1, .95f, .87f);
            preview.lights[0].intensity = 1.15f;
            preview.lights[0].transform.rotation = Quaternion.Euler(48, -36, 0);
            preview.lights[1].type = LightType.Directional;
            preview.lights[1].color = new Color(.52f, .74f, 1);
            preview.lights[1].intensity = .65f;
            preview.lights[1].transform.rotation = Quaternion.Euler(24, 145, 0);
        }

        void RebuildGeometry()
        {
            ClearGeometry();
            geometryRoot = new GameObject("Floor editor isolated preview") { hideFlags = HideFlags.HideAndDontSave };
            geometryRoot.SetActive(false);
            preview.AddSingleGO(geometryRoot);
            string[] rows = FloorDocuments.BuildRows(document);
            if (rows.Length == 0) { geometryRoot.SetActive(true); needsRebuild = false; return; }
            int width = 0; foreach (string row in rows) width = Math.Max(width, row?.Length ?? 0);
            float cell = FloorDocuments.CellSize;
            var batch = new BoxBatch();
            Color floor = new Color(.30f, .38f, .45f), grid = new Color(.16f, .23f, .30f);
            batch.Box(new Vector3(width * cell * .5f, -.09f, -rows.Length * cell * .5f), new Vector3(width * cell, .16f, rows.Length * cell), floor);
            for (int x = 0; x <= width; x++) batch.Box(new Vector3(x * cell, .007f, -rows.Length * cell * .5f), new Vector3(.028f, .018f, rows.Length * cell), grid);
            for (int z = 0; z <= rows.Length; z++) batch.Box(new Vector3(width * cell * .5f, .007f, -z * cell), new Vector3(width * cell, .018f, .028f), grid);
            for (int z = 0; z < rows.Length; z++) for (int x = 0; x < (rows[z]?.Length ?? 0); x++)
            {
                char c = rows[z][x]; var at = new Vector3((x + .5f) * cell, 0, -(z + .5f) * cell);
                if (FloorDocuments.IsBaseSolid(c))
                {
                    float h = c == '#' ? 1.5f : c == 'B' ? 1.0f : c == 'r' ? .65f : c == 'D' ? 1.8f : .6f;
                    Color color = c == 'D' ? new Color(.63f, .40f, .13f) : c == '#' ? new Color(.31f, .40f, .50f) : c == 'r' ? new Color(.15f, .24f, .31f) : new Color(.42f, .51f, .57f);
                    batch.Box(at + Vector3.up * (h * .5f), new Vector3(cell - .05f, h, cell - .05f), color);
                }
                else if (c == '~' || c == 'y')
                    batch.Box(at + Vector3.up * .018f, new Vector3(cell - .06f, .025f, cell - .06f), c == '~' ? new Color(.22f, .42f, .53f) : new Color(.53f, .43f, .16f));
                if (c == 'P') Marker(batch, at, new Color(.10f, .94f, .95f), true);
                else if (c == 'S') Marker(batch, at, new Color(1, .31f, .23f), false);
                else if (c != '.' && c != '~' && c != 'y' && !FloorDocuments.IsBaseSolid(c))
                    Marker(batch, at, new Color(1, .75f, .21f), false);
            }
            batch.Flush(geometryRoot.transform, ownedMaterials, ownedMeshes, "Grid shell");
            foreach (var item in document.objects ?? Array.Empty<PlacedObject>())
            {
                if (item == null || !Finite(item.x) || !Finite(item.y) || !Finite(item.z) || !Finite(item.yaw) || !Finite(item.scale) || !Finite(item.footprintWidth) || !Finite(item.footprintDepth) || !Finite(item.maxHeight) || item.scale <= 0) continue;
                GameObject go = FloorObjectVisuals.Spawn(item, geometryRoot.transform, ownedMaterials);
                if (go == null) continue;
                // Never let preview clones run scene logic, sound, animation, particles, or lights.
                foreach (var behaviour in go.GetComponentsInChildren<Behaviour>(true)) Object.DestroyImmediate(behaviour);
                foreach (var particles in go.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(particles);
                foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                bool first = true; var bounds = new Bounds();
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                    var local = renderer.localBounds;
                    for (int bx = -1; bx <= 1; bx += 2)
                    for (int by = -1; by <= 1; by += 2)
                    for (int bz = -1; bz <= 1; bz += 2)
                    {
                        Vector3 corner = renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(bx, by, bz)));
                        if (first) { bounds = new Bounds(corner, Vector3.zero); first = false; }
                        else bounds.Encapsulate(corner);
                    }
                }
                if (!first && !string.IsNullOrEmpty(item.id)) objectBounds[item.id] = bounds;
            }
            geometryRoot.SetActive(true);
            needsRebuild = false;
            RebuildSelection();
        }

        static void Marker(BoxBatch batch, Vector3 at, Color color, bool player)
        {
            float radius = player ? .46f : .38f;
            batch.Box(at + Vector3.up * .08f, new Vector3(radius * 2, .12f, .14f), color);
            batch.Box(at + Vector3.up * .08f, new Vector3(.14f, .12f, radius * 2), color);
            if (player) batch.Box(at + new Vector3(0, .55f, 0), new Vector3(.28f, .9f, .28f), color);
        }

        void RebuildSelection()
        {
            if (selectionRoot != null) Object.DestroyImmediate(selectionRoot);
            ClearSelectionResources();
            if (geometryRoot == null) return;
            selectionRoot = new GameObject("Selection bounds") { hideFlags = HideFlags.HideAndDontSave };
            selectionRoot.transform.SetParent(geometryRoot.transform, false);
            var batch = new BoxBatch(); Color amber = new Color(1, .77f, .18f);
            foreach (string id in selection)
                if (objectBounds.TryGetValue(id, out var b))
                {
                    Vector3 c = b.center, s = b.size + Vector3.one * .12f;
                    float low = Mathf.Max(.08f, b.min.y + .03f), high = b.max.y + .10f;
                    foreach (float y in new[] { low, high })
                    {
                        batch.Box(new Vector3(c.x, y, c.z - s.z * .5f), new Vector3(s.x, .055f, .055f), amber);
                        batch.Box(new Vector3(c.x, y, c.z + s.z * .5f), new Vector3(s.x, .055f, .055f), amber);
                        batch.Box(new Vector3(c.x - s.x * .5f, y, c.z), new Vector3(.055f, .055f, s.z), amber);
                        batch.Box(new Vector3(c.x + s.x * .5f, y, c.z), new Vector3(.055f, .055f, s.z), amber);
                    }
                }
            // Selection meshes/materials are reclaimed on every update as well as window disposal.
            batch.Flush(selectionRoot.transform, selectionMaterials, selectionMeshes, "Selection");
        }

        readonly List<Material> selectionMaterials = new List<Material>();
        readonly List<Mesh> selectionMeshes = new List<Mesh>();
        void ClearSelectionResources()
        {
            foreach (var mesh in selectionMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
            foreach (var material in selectionMaterials) if (material != null) Object.DestroyImmediate(material);
            selectionMeshes.Clear(); selectionMaterials.Clear();
        }
        void ClearGeometry()
        {
            if (geometryRoot != null) Object.DestroyImmediate(geometryRoot);
            geometryRoot = null; selectionRoot = null;
            ClearSelectionResources();
            foreach (var mesh in ownedMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
            foreach (var material in ownedMaterials) if (material != null) Object.DestroyImmediate(material);
            ownedMeshes.Clear(); ownedMaterials.Clear(); objectBounds.Clear();
        }
        void ReleasePreview()
        {
            image.image = null;
            ClearGeometry();
            if (preview != null) { preview.Cleanup(); preview = null; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; renderSchedule?.Pause(); renderSchedule = null;
            ReleasePreview(); document = null;
        }

        sealed class OrbitManipulator : PointerManipulator
        {
            readonly FloorPreview owner;
            int pointerId = -1, button;
            Vector2 previous;
            public OrbitManipulator(FloorPreview owner) { this.owner = owner; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(CaptureOut);
                target.RegisterCallback<WheelEvent>(Wheel);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(CaptureOut);
                target.UnregisterCallback<WheelEvent>(Wheel);
            }
            void Down(PointerDownEvent e)
            {
                if (pointerId >= 0 || e.button < 0 || e.button > 2) return;
                pointerId = e.pointerId; button = e.button; previous = e.position;
                target.CapturePointer(pointerId); e.StopPropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointerId || !target.HasPointerCapture(pointerId)) return;
                Vector2 now = e.position, delta = now - previous; previous = now;
                if (button == 0 && !e.shiftKey)
                {
                    owner.topView = false;
                    owner.yaw += delta.x * .35f;
                    owner.pitch = Mathf.Clamp(owner.pitch - delta.y * .35f, 15, 85);
                }
                else
                {
                    float unit = owner.radius * owner.zoom * 2.2f / Mathf.Max(100, target.contentRect.height);
                    Quaternion rotation = Quaternion.Euler(0, owner.topView ? 0 : owner.yaw, 0);
                    Vector3 right = rotation * Vector3.right;
                    Vector3 forward = rotation * Vector3.forward;
                    owner.pivot += (-right * delta.x + forward * delta.y) * unit;
                }
                owner.RequestRender(); e.StopPropagation();
            }
            void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointerId) return;
                int release = pointerId; pointerId = -1;
                if (target.HasPointerCapture(release)) target.ReleasePointer(release);
                e.StopPropagation();
            }
            void CaptureOut(PointerCaptureOutEvent e) { if (e.pointerId == pointerId) pointerId = -1; }
            void Wheel(WheelEvent e)
            { owner.zoom = Mathf.Clamp(owner.zoom * Mathf.Exp(e.delta.y * .06f), .025f, 6); owner.RequestRender(); e.StopPropagation(); }
        }

        sealed class BoxBatch
        {
            sealed class Data
            {
                public readonly List<Vector3> vertices = new List<Vector3>();
                public readonly List<Vector3> normals = new List<Vector3>();
                public readonly List<Vector2> uvs = new List<Vector2>();
                public readonly List<int> triangles = new List<int>();
            }
            readonly Dictionary<Color, Data> groups = new Dictionary<Color, Data>();
            public void Box(Vector3 center, Vector3 size, Color color)
            {
                if (!groups.TryGetValue(color, out var data)) groups.Add(color, data = new Data());
                Vector3 h = size * .5f;
                Face(data, center + Vector3.right * h.x, Vector3.back * h.z, Vector3.up * h.y, Vector3.right);
                Face(data, center + Vector3.left * h.x, Vector3.forward * h.z, Vector3.up * h.y, Vector3.left);
                Face(data, center + Vector3.up * h.y, Vector3.right * h.x, Vector3.back * h.z, Vector3.up);
                Face(data, center + Vector3.down * h.y, Vector3.right * h.x, Vector3.forward * h.z, Vector3.down);
                Face(data, center + Vector3.forward * h.z, Vector3.right * h.x, Vector3.up * h.y, Vector3.forward);
                Face(data, center + Vector3.back * h.z, Vector3.left * h.x, Vector3.up * h.y, Vector3.back);
            }
            static void Face(Data data, Vector3 c, Vector3 u, Vector3 v, Vector3 normal)
            {
                int start = data.vertices.Count;
                data.vertices.Add(c - u - v); data.vertices.Add(c + u - v); data.vertices.Add(c + u + v); data.vertices.Add(c - u + v);
                for (int i = 0; i < 4; i++) data.normals.Add(normal);
                data.uvs.Add(Vector2.zero); data.uvs.Add(Vector2.right); data.uvs.Add(Vector2.one); data.uvs.Add(Vector2.up);
                data.triangles.Add(start); data.triangles.Add(start + 1); data.triangles.Add(start + 2);
                data.triangles.Add(start); data.triangles.Add(start + 2); data.triangles.Add(start + 3);
            }
            public void Flush(Transform parent, List<Material> materials, List<Mesh> meshes, string label)
            {
                Shader shader = Shader.Find("Maze/CryoSoftSurface") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("No supported preview surface shader is available.");
                foreach (var group in groups)
                {
                    var material = new Material(shader) { name = "Floor preview " + label, hideFlags = HideFlags.HideAndDontSave };
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", group.Key);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", group.Key);
                    if (material.HasProperty("_UseVertexAO")) material.SetFloat("_UseVertexAO", 0);
                    if (material.HasProperty("_Gloss")) material.SetFloat("_Gloss", .15f);
                    materials.Add(material);
                    var mesh = new Mesh { name = "Floor preview " + label, hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(group.Value.vertices); mesh.SetNormals(group.Value.normals);
                    mesh.SetUVs(0, group.Value.uvs); mesh.SetTriangles(group.Value.triangles, 0);
                    mesh.RecalculateBounds(); meshes.Add(mesh);
                    var go = new GameObject(label) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                }
            }
        }
    }
}
#endif
