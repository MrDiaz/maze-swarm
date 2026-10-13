using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Renders the sector-select / briefing art from the real game: an overhead view of the level
    // (Resources/UI/arena_cryo.png) and transparent enemy portraits (Resources/UI/Portraits/*.png).
    // Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.MapSelectArt.Run   (no -quit)
    [InitializeOnLoad]
    public static class MapSelectArt
    {
        const string Key = "Maze.MapSelectArt.Active";
        static int frames;

        static MapSelectArt() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        // only the Iron Vanguard squad portrait (Resources/UI/Portraits/vanguard.png)
        public static void RunVanguard() { SessionState.SetBool(Key + ".vanguard", true); Run(); }
        public static void RunLeaper() { SessionState.SetBool(Key + ".leaper", true); Run(); }
        // only the Wanderer Scout squad portrait (Resources/UI/Portraits/scout.png)
        public static void RunScout() { SessionState.SetBool(Key + ".scout", true); Run(); }

        [MenuItem("Maze/Render Map Select Art", priority = 41)]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            var g = UnityEngine.Object.FindAnyObjectByType<Game>();
            if (g == null || g.menu == null || ++frames < 20) return;
            try
            {
                if (SessionState.GetBool(Key + ".vanguard", false)) { Directory.CreateDirectory("Assets/Maze/Resources/UI/Portraits"); Vanguard(); }
                else if (SessionState.GetBool(Key + ".scout", false)) Portrait("scout", "hero_scout", 1f, null, 420, 560, null, 0f, 12, 2.1f);
                else if (SessionState.GetBool(Key + ".leaper", false)) Portrait("leaper", "leaper", 1f, null, 384, 384, "walk", 0.2f, 22, 2.4f);
                else { Arena(g, "arena_cryo"); g.LoadMap(1); Arena(g, "arena_escape"); g.LoadMap(0); Portraits(); }
            }
            catch (Exception e) { Debug.LogError("MapSelectArt: " + e); }
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".vanguard", false); SessionState.SetBool(Key + ".leaper", false); SessionState.SetBool(Key + ".scout", false);
            EditorApplication.update -= Tick;
            Debug.Log("MapSelectArt: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }

        // the whole maze from straight above, orthographic, slightly tilted for depth
        static void Arena(Game g, string file)
        {
            g.player.SetVisible(false);
            float w = MazeMap.W * MazeMap.C, h = MazeMap.H * MazeMap.C;
            var center = new Vector3(w / 2, 0, -h / 2);
            var cam = NewCam();
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(h, w * 0.75f) * 0.52f;
            cam.transform.SetPositionAndRotation(center + new Vector3(0, 80, -12), Quaternion.Euler(82, 0, 0));
            cam.backgroundColor = new Color(0.05f, 0.09f, 0.14f, 1);
            Save(cam, 1024, 768, $"Assets/Maze/Resources/UI/{file}.png");
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            g.player.SetVisible(true);
        }

        static void Portraits()
        {
            Directory.CreateDirectory("Assets/Maze/Resources/UI/Portraits");
            Portrait("drone", "drone", 1f, null);
            Portrait("bloater", "bloater", 1f, null);
            Portrait("mini", "drone", 1f, new Color(0.62f, 1.15f, 0.5f));
            Portrait("leaper", "leaper", 1f, null, 384, 384, "walk", 0.2f, 22, 2.4f);
            Portrait("hero", "hero", 1f, null, 420, 560, "walk", 0f, 12, 2.1f);
            Vanguard();
        }

        // Iron Vanguard (static T-pose preview until the rigged biped arrives) with his minigun at the hip
        static void Vanguard() => Portrait("vanguard", "vanguard", 1f, null, 420, 560, null, 0f, 12, 2.1f, go =>
        {
            var gp = Resources.Load<GameObject>("Models/minigun");
            if (gp == null) return;
            var b = new Bounds(); bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            var gun = UnityEngine.Object.Instantiate(gp, go.transform);
            Mats.ConvertGltf(gun);
            float h = b.size.y, len = h * 1.25f / 1.9f;      // a 1.25 m gun on a ~1.9 m giant
            gun.transform.localScale = Vector3.one * (len / 1.9f);
            gun.transform.rotation = Quaternion.Euler(-6, 32, 0); // muzzle (model -X) to the left and toward the camera, showing its length
            gun.transform.position = new Vector3(b.center.x + 0.04f * h, b.min.y + 0.45f * h, b.center.z + 0.24f * h);
        });
        static void Portrait(string name, string model, float scale, Color? tint) => Portrait(name, model, scale, tint, 384, 384, null, 0.3f, 28, 2.6f);
        static void Portrait(string name, string model, float scale, Color? tint, int w, int h, string clip, float at01, float pitch, float dist, Action<GameObject> extra = null)
        {
            var prefab = Resources.Load<GameObject>("Models/" + model);
            if (prefab == null) { Debug.LogWarning("MapSelectArt: no Models/" + model); return; }
            var at = new Vector3(0, -300, 0);
            var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.Euler(0, model == "hero_scout" ? -30 : model == "hero" || model == "vanguard" || model == "leaper" ? 200 - 180 : 150, 0));
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true; // bounds from the posed skin
            // a key light from the camera side (the scene sun alone leaves them dark)
            var key = new GameObject("Key").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.1f; key.color = new Color(0.85f, 0.93f, 1f);
            key.transform.rotation = Quaternion.Euler(30, 160, 0);
            Mats.ConvertGltf(go);
            if (tint.HasValue)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                { var m = new Material(r.sharedMaterial); m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint.Value); r.sharedMaterial = m; }
            var anim = go.GetComponentInChildren<Animation>();
            if (anim != null)
                foreach (AnimationState st in anim)
                {
                    if (clip != null && !st.name.ToLower().Contains(clip)) continue;
                    st.enabled = true; st.weight = 1; st.normalizedTime = at01; anim.Sample(); break;
                }
            extra?.Invoke(go);
            var b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            var cam = NewCam();
            cam.fieldOfView = 24;
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            var dir = Quaternion.Euler(pitch, 0, 0) * Vector3.back;
            cam.transform.position = b.center - dir * (size * dist);
            cam.transform.LookAt(b.center);
            cam.backgroundColor = new Color(0, 0, 0, 0);
            Save(cam, w, h, $"Assets/Maze/Resources/UI/Portraits/{name}.png");
            UnityEngine.Object.DestroyImmediate(key.gameObject);
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            UnityEngine.Object.DestroyImmediate(go);
        }

        static Camera NewCam()
        {
            var c = new GameObject("ArtCam").AddComponent<Camera>();
            c.enabled = false; c.clearFlags = CameraClearFlags.SolidColor; c.nearClipPlane = 0.1f; c.farClipPlane = 600;
            return c;
        }

        static void Save(Camera cam, int w, int h, string path)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release();
            Debug.Log("MapSelectArt: wrote " + path);
        }
    }
}
