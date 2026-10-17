#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Maze.FloorEditing.Editor
{
    /// <summary>
    /// Captures the existing floor EditorWindow's attached UI Toolkit panel directly.
    /// No desktop/screen APIs, native window pixels, cloned controls or reparenting.
    /// This deliberately uses Unity6000.6's internal panel API, with checked reflection.
    /// </summary>
    public static class FloorPanelCapture
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable]
        public sealed class CaptureInfo
        {
            public string source = "Actual attached floor EditorWindow UI Toolkit panel, directly rendered to a transient RenderTexture and cropped to rootVisualElement";
            public string unityVersion, windowType, panelType, path;
            public int width, height, fullPanelWidth, fullPanelHeight;
            public float pixelsPerPoint;
            public bool desktopCapture = false, clonedControls = false;
        }

        // Invoke on Unity's main thread, outside an active GUI rendering callback
        // (EditorApplication.delayCall or the existing editor proof update is appropriate).
        public static bool TryCapture(EditorWindow window, string pngPath, out CaptureInfo info, out string error)
        {
            info = null; error = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture previousTarget = RenderTexture.active;
            Camera previousCamera = Camera.current;
            Event previousEvent = Event.current;
            Event renderEvent = null;
            object savedGUI = null;
            MethodInfo restoreGUI = null;
            bool matricesPushed = false;
            try
            {
                if (window == null || window.GetType().FullName != "Maze.FloorEditing.Editor.FloorEditorWindow")
                    throw new ArgumentException("Only the Maze floor EditorWindow may be captured.");
                if (previousEvent != null && previousEvent.type == EventType.Repaint)
                    throw new InvalidOperationException("Schedule capture after the current GUI repaint has finished.");
                var root = window.rootVisualElement;
                object panel = root.panel;
                if (panel == null) throw new InvalidOperationException("Floor EditorWindow must be shown and attached before capture.");
                if (string.IsNullOrWhiteSpace(pngPath) || !string.Equals(Path.GetExtension(pngPath), ".png", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Capture path must end in .png.");
                pngPath = Path.GetFullPath(pngPath);
                if (!Directory.Exists(Path.GetDirectoryName(pngPath))) throw new DirectoryNotFoundException("Create the capture directory first.");

                Type panelType = panel.GetType();
                MethodInfo repaint = FindMethod(panelType, "Repaint");
                MethodInfo render = FindMethod(panelType, "Render");
                var scaleProperty = FindProperty(panelType, "scaledPixelsPerPoint");
                if (repaint == null || render == null || scaleProperty == null)
                    throw new NotSupportedException("Installed Unity panel API does not match the checked capture path.");
                float scale = Convert.ToSingle(scaleProperty.GetValue(panel));
                if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale)) throw new InvalidOperationException("Panel scale is invalid.");
                var publicPanel = (IPanel)panel;
                Rect panelRect = publicPanel.visualTree.layout;
                Rect crop = root.worldBound;
                int fullWidth = Mathf.CeilToInt(panelRect.width * scale), fullHeight = Mathf.CeilToInt(panelRect.height * scale);
                if (fullWidth < 2 || fullHeight < 2 || fullWidth > 8192 || fullHeight > 8192)
                    throw new InvalidOperationException("Attached panel dimensions must be between 2 and 8192 pixels.");
                int left = Mathf.Clamp(Mathf.RoundToInt(crop.xMin * scale), 0, fullWidth - 1);
                int right = Mathf.Clamp(Mathf.RoundToInt(crop.xMax * scale), left + 1, fullWidth);
                int bottom = Mathf.Clamp(fullHeight - Mathf.RoundToInt(crop.yMax * scale), 0, fullHeight - 1);
                int top = Mathf.Clamp(fullHeight - Mathf.RoundToInt(crop.yMin * scale), bottom + 1, fullHeight);

                Type savedType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SavedGUIState");
                MethodInfo saveGUI = savedType?.GetMethod("Create", Members, null, Type.EmptyTypes, null);
                restoreGUI = savedType?.GetMethod("ApplyAndForget", Members, null, Type.EmptyTypes, null);
                if (saveGUI == null || restoreGUI == null)
                    throw new NotSupportedException("Unity SavedGUIState API is unavailable; capture was not attempted.");
                savedGUI = saveGUI.Invoke(null, null);

                target = new RenderTexture(fullWidth, fullHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "Floor editor panel capture", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                if (!target.Create()) throw new InvalidOperationException("Could not allocate the transient capture render target.");
                GL.PushMatrix(); matricesPushed = true;
                Camera.SetupCurrent(null);
                RenderTexture.active = target;
                GL.LoadPixelMatrix();
                GL.Clear(true, true, new Color(.055f, .085f, .13f, 1), .99f);

                // Matches VisualTreeAssetEditor.RenderStaticPreview in this installed Unity.
                // SavedGUIState restores these temporary IMGUI clips before returning.
                Type clipType = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
                MethodInfo clipCount = clipType?.GetMethod("Internal_GetCount", Members, null, Type.EmptyTypes, null);
                MethodInfo popClip = clipType?.GetMethod("Pop", Members, null, Type.EmptyTypes, null);
                if (clipCount != null && popClip != null)
                    for (int n = Convert.ToInt32(clipCount.Invoke(null, null)); n > 0; n--) popClip.Invoke(null, null);
                if (previousEvent == null)
                { renderEvent = new Event { type = EventType.Repaint }; Event.current = renderEvent; }

                repaint.Invoke(panel, null);
                RenderTexture.active = target;
                GL.Viewport(new Rect(0, 0, fullWidth, fullHeight));
                render.Invoke(panel, null);
                RenderTexture.active = target;
                pixels = new Texture2D(right - left, top - bottom, TextureFormat.RGBA32, false, false)
                { name = "Floor editor panel capture readback", hideFlags = HideFlags.HideAndDontSave };
                pixels.ReadPixels(new Rect(left, bottom, right - left, top - bottom), 0, 0, false);
                pixels.Apply(false, false);
                byte[] png = pixels.EncodeToPNG();
                if (png == null || png.Length == 0) throw new InvalidOperationException("Unity returned an empty panel PNG.");
                File.WriteAllBytes(pngPath, png);
                info = new CaptureInfo {
                    unityVersion = Application.unityVersion, windowType = window.GetType().FullName,
                    panelType = panelType.FullName, path = pngPath,
                    width = pixels.width, height = pixels.height,
                    fullPanelWidth = fullWidth, fullPanelHeight = fullHeight, pixelsPerPoint = scale
                };
                return true;
            }
            catch (TargetInvocationException e)
            { error = (e.InnerException ?? e).ToString(); return false; }
            catch (Exception e)
            { error = e.ToString(); return false; }
            finally
            {
                try { if (savedGUI != null && restoreGUI != null) restoreGUI.Invoke(savedGUI, null); }
                finally
                {
                    try { if (matricesPushed) GL.PopMatrix(); }
                    finally
                    {
                        Camera.SetupCurrent(previousCamera);
                        RenderTexture.active = previousTarget;
                        if (renderEvent != null) Event.current = previousEvent;
                        if (pixels != null) Object.DestroyImmediate(pixels);
                        if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                    }
                }
            }
        }

        static MethodInfo FindMethod(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(name, Members | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method != null) return method;
            }
            return null;
        }
        static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, Members | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
    }
}
#endif
