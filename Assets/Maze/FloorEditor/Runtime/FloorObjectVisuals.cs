using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.FloorEditing
{
    // Shared art-only factory. A placed copy never silently adds combat, goal, audio,
    // motion or collision behavior. The caller owns returned objects and materials.
    // Parent should have unit world scale; saved x/y/z are ordinary world metres.
    public static class FloorObjectVisuals
    {
        public const float CryoRealCap = 1.3f;
        static readonly Color PropTint = new Color(.86f, .90f, .95f);

        public static bool IsSupported(string assetId)
        {
            if (FloorModelCatalog.IsLocalId(assetId)) return FloorModelCatalog.TryGet(assetId, out _);
            switch (assetId)
            {
                case "cryo-blast-door": case "cryo-coolant-pumping-station":
                case "cryo-cooling-reactor": case "cryo-flamethrower-turret":
                case "cryo-freeze-canister": case "cryo-gun-turret":
                case "cryo-low-coolant-tank": case "cryo-maintenance-cart":
                case "cryo-pod": case "cryo-supply-crate": return true;
                default: return false;
            }
        }

        public static string ResourceFor(string assetId)
        {
            if (FloorModelCatalog.IsLocalId(assetId)) return FloorModelCatalog.TryGet(assetId, out var entry) ? entry.resourcePath : null;
            return IsSupported(assetId) ? "CryoProps/" + assetId : null;
        }

        public static GameObject Spawn(PlacedObject item, Transform parent, List<Material> ownedMaterials)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (ownedMaterials == null) throw new ArgumentNullException(nameof(ownedMaterials));
            if (FloorModelCatalog.IsLocalId(item.assetId)) return SpawnLocal(item, parent);
            string resource = ResourceFor(item.assetId);
            if (resource == null) throw new ArgumentException("Unsupported integrated visual asset: " + item.assetId);
            var source = Resources.Load<GameObject>(resource);
            if (source == null) throw new InvalidOperationException("Missing floor object resource: " + resource);
            var root = new GameObject(string.IsNullOrEmpty(item.name) ? item.assetId : item.name);
            root.SetActive(false); // cloning into an inactive hierarchy prevents imported OnEnable/Awake execution
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(item.x, item.y, item.z), Quaternion.Euler(0, item.legacyFit ? item.yaw : 0, 0));
            int materialStart = ownedMaterials.Count;
            try
            {
                var model = Object.Instantiate(source, root.transform, false);
                model.name = item.assetId + " visual";
                RemoveBehaviors(model);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("Floor object has no renderers: " + item.assetId);
                // Same order as CryoProps.Spawn: native transform + requested yaw, then rotated
                // world-axis renderer fit. Uniform user scale is an explicit extra transform.
                if (item.footprintWidth > 0 && item.footprintDepth > 0)
                {
                    var bounds = BoundsOf(renderers);
                    float fit = Mathf.Min(item.footprintWidth / Mathf.Max(.01f, bounds.size.x),
                        item.footprintDepth / Mathf.Max(.01f, bounds.size.z),
                        (item.maxHeight > 0 ? item.maxHeight : 3.4f) / Mathf.Max(.01f, bounds.size.y));
                    if (item.assetId != "cryo-blast-door") fit = Mathf.Min(fit, CryoRealCap);
                    model.transform.localScale *= fit;
                }
                root.transform.rotation = Quaternion.Euler(0, item.yaw, 0);
                root.transform.localScale = Vector3.one * item.scale;
                var cache = new Dictionary<string, Material>();
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        string sourceName = materials[slot] != null ? materials[slot].name : "";
                        string key = MaterialKey(item.assetId, sourceName);
                        if (!cache.TryGetValue(key, out var material))
                        {
                            var shader = Shader.Find("Maze/CryoSoftSurface");
                            if (shader == null) throw new InvalidOperationException("Missing Maze/CryoSoftSurface shader.");
                            material = new Material(shader) { name = key, hideFlags = HideFlags.DontSave };
                            ConfigureMaterial(material, item.assetId, sourceName);
                            cache.Add(key, material); ownedMaterials.Add(material);
                        }
                        materials[slot] = material;
                    }
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                root.SetActive(true);
                return root;
            }
            catch
            {
                DestroyOwned(root);
                for (int i = ownedMaterials.Count - 1; i >= materialStart; i--)
                { DestroyOwned(ownedMaterials[i]); ownedMaterials.RemoveAt(i); }
                throw;
            }
        }

        static GameObject SpawnLocal(PlacedObject item, Transform parent)
        {
            if (!FloorModelCatalog.TryGet(item.assetId, out var definition)) throw new ArgumentException("Unknown local model: " + item.assetId);
            var source = Resources.Load<GameObject>(definition.resourcePath);
            if (source == null) throw new InvalidOperationException("Missing local model prefab: " + definition.resourcePath);
            var root = new GameObject(string.IsNullOrEmpty(item.name) ? definition.label : item.name);
            root.SetActive(false); root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(item.x, item.y, item.z), Quaternion.identity);
            try
            {
                var model = Object.Instantiate(source, root.transform, false); RemoveBehaviors(model);
                var rs = model.GetComponentsInChildren<Renderer>(true); var bounds = BoundsOf(rs);
                float fit = 1;
                if (item.footprintWidth > 0 && item.footprintDepth > 0)
                    fit = Mathf.Min(item.footprintWidth / Mathf.Max(.01f, bounds.size.x), item.footprintDepth / Mathf.Max(.01f, bounds.size.z), (item.maxHeight > 0 ? item.maxHeight : definition.sizeY) / Mathf.Max(.01f, bounds.size.y));
                model.transform.localScale *= fit;
                root.transform.rotation = Quaternion.Euler(0, item.yaw, 0);
                root.transform.localScale = Vector3.one * item.scale;
                root.SetActive(true); return root;
            }
            catch { DestroyOwned(root); throw; }
        }

        public static string MaterialKey(string prop, string sourceName)
        {
            sourceName = sourceName ?? "";
            return sourceName.StartsWith("Cap_Navy", StringComparison.Ordinal) || sourceName.StartsWith("Wheel_Rim", StringComparison.Ordinal) ? "Soft_Navy"
                : sourceName.StartsWith("Wheel_Hub", StringComparison.Ordinal) ? "Soft_Cyan"
                : sourceName.StartsWith("Wheel_Tyre", StringComparison.Ordinal) ? "Prop_Tyre" : "Prop_" + prop;
        }

        // Copies CryoProps' material mapping and the current room's prop-specific reference
        // appearance. It only edits this transient Material, never RenderSettings or a Volume.
        public static void ConfigureMaterial(Material material, string prop, string sourceName)
        {
            string key = MaterialKey(prop, sourceName);
            material.SetFloat("_UseVertexAO", 0); material.SetFloat("_Gloss", .35f);
            material.SetFloat("_RefMode", 1); material.SetFloat("_Shoulder", .92f);
            material.SetColor("_SkyFill", new Color(.80f, .82f, .97f));
            material.SetColor("_GroundFill", new Color(.47f, .51f, .67f));
            material.SetColor("_KeyColor", new Color(1f, .97f, .92f));
            material.SetColor("_ShadowTint", new Color(.07f, .10f, .27f));
            material.SetFloat("_AmbientStrength", .50f); material.SetFloat("_KeyGain", 1.24f);
            material.SetFloat("_ShadowOpacity", .90f); material.SetFloat("_ShadowSpread", 2.2f);
            material.SetFloat("_ShadowTaps", 8); material.SetFloat("_WarmSpill", .22f);
            if (key == "Soft_Navy")
            {
                material.SetColor("_BaseColor", new Color32(0x1c, 0x32, 0x68, 255));
                material.SetFloat("_Sheen", .35f); material.SetFloat("_AOIntensity", .75f);
            }
            else if (key == "Soft_Cyan")
            {
                material.SetColor("_BaseColor", new Color32(0x5f, 0xdc, 0xef, 255));
                material.SetColor("_EmissionColor", new Color(0x10 / 255f, 0xdc / 255f, 0xf5 / 255f) * 2f);
                material.SetFloat("_EmitAfter", 1); material.SetFloat("_Sheen", 0);
                material.SetFloat("_AOIntensity", .75f);
            }
            else
            {
                if (key == "Prop_Tyre") material.SetColor("_BaseColor", new Color(.10f, .12f, .18f));
                else
                {
                    material.SetTexture("_BaseMap", Resources.Load<Texture2D>("CryoProps/" + prop + "_base"));
                    var normal = Resources.Load<Texture2D>("CryoProps/" + prop + "_normal");
                    if (normal != null) { material.SetTexture("_BumpMap", normal); material.SetFloat("_NormalStrength", .65f); }
                    material.SetColor("_BaseColor", PropTint);
                }
                material.SetFloat("_Sheen", key == "Prop_Tyre" ? 0 : .55f);
                material.SetFloat("_TexGlow", 1.6f); material.SetFloat("_EmitAfter", 1);
                material.SetFloat("_AOIntensity", 0);
            }
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            // Local bounds work before activation, even in an isolated preview scene.
            Bounds result = default; bool first = true;
            foreach (var renderer in renderers)
            {
                var local = renderer.localBounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var corner = renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                    if (first) { result = new Bounds(corner, Vector3.zero); first = false; }
                    else result.Encapsulate(corner);
                }
            }
            return result;
        }

        static void RemoveBehaviors(GameObject model)
        {
            foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true))
            { script.enabled = false; DestroyOwned(script); }
            foreach (var animation in model.GetComponentsInChildren<Animator>(true))
            { animation.enabled = false; DestroyOwned(animation); }
            foreach (var animation in model.GetComponentsInChildren<Animation>(true))
            { animation.enabled = false; DestroyOwned(animation); }
            foreach (var audio in model.GetComponentsInChildren<AudioSource>(true))
            { audio.playOnAwake = false; audio.enabled = false; audio.Stop(); DestroyOwned(audio); }
            foreach (var light in model.GetComponentsInChildren<Light>(true))
            { light.enabled = false; DestroyOwned(light); }
            foreach (var particle in model.GetComponentsInChildren<ParticleSystem>(true))
            { var main = particle.main; main.playOnAwake = false; particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); DestroyOwned(particle); }
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            { collider.enabled = false; DestroyOwned(collider); }
            foreach (var rigidbody in model.GetComponentsInChildren<Rigidbody>(true))
            { rigidbody.isKinematic = true; rigidbody.detectCollisions = false; DestroyOwned(rigidbody); }
        }

        public static void DestroyOwned(Object value)
        { if (value == null) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
