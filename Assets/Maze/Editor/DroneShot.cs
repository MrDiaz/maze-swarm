using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.EditorTools
{
    // Batch check renders for the Drone (no game loop): a side-view strip of the Walk cycle, and a
    // game-camera view of a few walking drones next to goo bursts at different ages.
    // Unity -batchmode -quit -projectPath . -executeMethod Maze.EditorTools.DroneShot.Render  (not -nographics)
    public static class DroneShot
    {
        public static void Render()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.75f, 0.77f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.56f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.35f, 0.38f);
            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional; lg.intensity = 1.6f; lg.shadows = LightShadows.Soft;
            lg.transform.rotation = Quaternion.Euler(55, -35, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 4;
            floor.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.86f, 0.87f, 0.89f));

            var prefab = Resources.Load<GameObject>("Models/drone");
            if (prefab == null) { Debug.LogError("DroneShot: no drone model"); return; }
            var cfg = new MazeConfig();
            float fit = 0;
            GameObject Drone(Vector3 p, float yaw, float phase, float scale = 1f)
            {
                var go = Object.Instantiate(prefab);
                Mats.ConvertGltf(go);
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                smr.quality = SkinQuality.Bone2;
                var tex = smr.sharedMaterial.GetTexture("_BaseMap"); if (tex != null) tex.filterMode = FilterMode.Point;
                if (fit == 0) { var b = smr.sharedMesh.bounds; fit = 2.4f * cfg.enemy.radius / Mathf.Max(b.size.x, b.size.z); Debug.Log($"DroneShot: mesh bounds {b.size:F2}, fit {fit:F3}, tex {(tex != null ? tex.width + "x" + tex.height + " " + tex.filterMode : "none")}, verts {smr.sharedMesh.vertexCount}, bones {smr.bones.Length}"); }
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
                go.transform.localScale = Vector3.one * fit * scale;
                var anim = go.GetComponentInChildren<Animation>();
                if (anim == null) { Debug.LogError("DroneShot: no Animation component"); return go; }
                var st = anim["Walk"];
                if (st == null) { Debug.LogError("DroneShot: no Walk clip"); return go; }
                st.enabled = true; st.weight = 1; st.time = phase * st.length;
                anim.Sample();
                return go;
            }

            var camGo = new GameObject("Cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.89f, 0.9f, 0.92f);
            Directory.CreateDirectory("Logs/DroneShot");

            // 1) Walk strip: 6 phases, side view, close
            var strip = new GameObject("Strip");
            for (int i = 0; i < 6; i++) Drone(new Vector3(i * 1.25f, 0, 20), 90, i / 6f).transform.SetParent(strip.transform, true);
            cam.fieldOfView = 26; cam.transform.SetPositionAndRotation(new Vector3(3.1f, 0.9f, 20 - 7.5f), Quaternion.Euler(3, 0, 0));
            Shot(cam, "Logs/DroneShot/walk_strip.png", 1800, 420); // first render warms shaders up
            Shot(cam, "Logs/DroneShot/walk_strip.png", 1800, 420);
            // 2) 3/4 close-up, one drone
            cam.fieldOfView = 30; cam.transform.SetPositionAndRotation(new Vector3(2.2f, 1.6f, 20 - 2.6f), Quaternion.LookRotation(new Vector3(0, 0.3f, 20) - new Vector3(2.2f, 1.6f, 17.4f)));
            Shot(cam, "Logs/DroneShot/closeup.png", 900, 700);
            Object.DestroyImmediate(strip);

            // 3) game view: walking pack + one swelling + goo bursts at different ages
            for (int i = 0; i < 7; i++) Drone(new Vector3(-3 + (i % 4) * 1.3f + (i / 4) * 0.6f, 0, -1.5f - (i / 4) * 1.3f), 200 + i * 9, i * 0.37f % 1);
            var sw = Drone(new Vector3(0.8f, 0, 1.2f), 160, 0.2f, 1.25f);
            var swm = Mats.Lit(new Color(0.78f, 0.5f, 1f), null, null, 0.8f);
            sw.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial = swm;

            var fx = new GameObject("Fx").AddComponent<Fx>(); fx.Init(cfg.weapons);
            var systems = fx.GetComponentsInChildren<ParticleSystem>();
            void Step(float t) { for (float s = 0; s < t; s += 1 / 60f) { fx.Tick(1 / 60f); foreach (var ps in systems) ps.Simulate(1 / 60f, true, false, false); } }
            Fx.I.GooBurst(new Vector3(3.5f, 0.45f, 1.5f), 1); Step(0.5f);   // oldest: landed, splats out
            Fx.I.GooBurst(new Vector3(-2.2f, 0.45f, 2.0f), 1); Step(0.22f); // blobs mid-arc
            Fx.I.GooBurst(new Vector3(2.0f, 0.45f, -2.6f), 1); Step(0.06f); // just popped
            // a puddle 3 s old next to fresh ones
            cam.fieldOfView = 50;
            cam.transform.SetPositionAndRotation(new Vector3(0, 11, -9), Quaternion.Euler(50, 0, 0));
            Shot(cam, "Logs/DroneShot/game.png", 1400, 800);
            cam.fieldOfView = 34;
            cam.transform.SetPositionAndRotation(new Vector3(0.5f, 5.5f, -6.5f), Quaternion.Euler(35, 0, 0));
            Shot(cam, "Logs/DroneShot/game_close.png", 1400, 800);
            // the hero for comparison (same material conversion)
            var hp = Resources.Load<GameObject>("Models/hero");
            if (hp != null)
            {
                var hero = Object.Instantiate(hp);
                var before = hero.GetComponentInChildren<Renderer>().sharedMaterial.shader.name;
                Mats.ConvertGltf(hero);
                Debug.Log($"DroneShot: hero shader {before} -> {hero.GetComponentInChildren<Renderer>().sharedMaterial.shader.name}");
                var hb = hero.GetComponentInChildren<Renderer>().bounds;
                hero.transform.localScale *= 1.6f / hb.size.y;
                hero.transform.position = new Vector3(-0.8f, 0, 0.3f);
                Shot(cam, "Logs/DroneShot/game_close_hero.png", 1400, 800);
                Object.DestroyImmediate(hero);
            }
            Step(2.6f);
            Shot(cam, "Logs/DroneShot/game_late.png", 1400, 800);
            Debug.Log("DroneShot: done");
        }

        static void Shot(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.aspect = (float)w / h;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            Debug.Log("DroneShot: wrote " + path);
        }
    }
}
