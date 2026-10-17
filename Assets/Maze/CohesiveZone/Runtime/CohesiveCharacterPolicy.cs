using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Shared presentation policy; no preference/save/stat/camera writes.
    // Requires the integration owner's CohesiveZone.Enabled gate.
    public static class CohesiveCharacterPolicy
    {
        public static bool Enabled => CohesiveZone.Enabled;
        public const string RebuiltHeroResource = "Models/hero_vanguard_chibi";
        public const string EngineerResource = "Models/hero_engineer_chibi";
        public const float EngineerHeight = 1.65f;
        public const float EngineerWalkGroundSpeed = .65f, EngineerJogGroundSpeed = 2.5f, EngineerRunGroundSpeed = 6.48f; // approved gait011 measurements at1.65m
        public static readonly Vector3 EngineerHipGrip = new Vector3(.06f, .35f, .13f);
        public static readonly Vector3 ChibiGunGrip = new Vector3(.04f, -.14f, 0);
        public static readonly Vector3 ChibiGunSupport = new Vector3(-.003038414f, -.14f, -.258230484f);
        public static readonly Vector3 ChibiGunMuzzle = new Vector3(-.919999958f, 0, 0);
        public static float SelectedHeroHeight => Prefs.Engineer ? EngineerHeight : HeroHeight;
        public static Vector3 SelectedHipGrip => Prefs.Engineer ? EngineerHipGrip : HipGrip;
        public static bool PreserveAuthoredHero => Prefs.Engineer || UseRebuiltVanguard;
        public const string RebuiltGunResource = "Models/minigun_chibi";
        public const string SpitterResource = "Models/cohesive_lavender_spitter";
        public const float HeroHeight = 1.76f;
        public const float GunLength = .82f;
        public static readonly Vector3 HipGrip = new Vector3(.06f, .44f, .10f); // reachable separated contacts for retained short chibi arms

        static GameObject heroPrefab, gunPrefab, spitterPrefab;
        static bool triedHero, triedGun, triedSpitter;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        static GameObject Load(ref GameObject value, ref bool tried, string path)
        {
            if (!tried) { tried = true; value = Resources.Load<GameObject>(path); }
            return value;
        }
        public static bool UseRebuiltVanguard => Enabled &&
            Load(ref heroPrefab, ref triedHero, RebuiltHeroResource) != null &&
            Load(ref gunPrefab, ref triedGun, RebuiltGunResource) != null;
        public static string HeroResource => Prefs.Engineer ? EngineerResource : UseRebuiltVanguard ? RebuiltHeroResource :
            (Prefs.VanguardMeshy ? "Models/hero_vanguard_meshy" : "Models/hero_vanguard");
        public static string GunResource => Prefs.Engineer || UseRebuiltVanguard ? RebuiltGunResource : "Models/minigun";
        public static bool HasAuthoredSpitter => Enabled &&
            Load(ref spitterPrefab, ref triedSpitter, SpitterResource) != null;
        public static bool IsAuthoredSpitterPrefab(GameObject value) =>
            HasAuthoredSpitter && value == spitterPrefab;
        static readonly Dictionary<string, GameObject> familyPrefabs = new Dictionary<string, GameObject>();
        public static string RoleFor(EnemyType type)
        {
            if (type == null || type.nest) return null;
            string n = type.name ?? "", ai = type.ai ?? "";
            if (ai == "spitter" || n == "spitter") return "spitter";
            if (ai == "tick" || n == "tick") return "tick";
            if (ai == "carapace" || n == "carapace") return "carapace";
            if (type.leaper || n == "leaper") return "runner";
            if (type.bloater || n == "bloater" || n == "boss_bloater") return "carrier";
            if (type.mini || n == "mini" || n == "grunt") return "grunt";
            return null;
        }
        // These three helpers intentionally do not inspect active scene/run state.
        public static string ResourceFor(EnemyType type)
        {
            string role = RoleFor(type);
            return role == null ? null : role == "spitter" ? SpitterResource : "Models/cargo-family-" + role;
        }
        public static GameObject AuthoredEnemyPrefab(EnemyType type)
        {
            string path = ResourceFor(type); if (path == null) return null;
            if (path == SpitterResource) return Load(ref spitterPrefab, ref triedSpitter, path);
            if (!familyPrefabs.TryGetValue(path, out var result))
            { result = Resources.Load<GameObject>(path); familyPrefabs[path] = result; }
            return result;
        }
        public static bool IsAuthoredFamilyPrefab(GameObject value)
        {
            if (value == null) return false;
            foreach (var item in familyPrefabs) if (item.Value == value) return true;
            return false;
        }
        public static GameObject EnemyPrefab(EnemyType type, string originalResource)
        {
            if (CohesiveZone.Active)
            {
                var authored = AuthoredEnemyPrefab(type); if (authored != null) return authored;
            }
            return Resources.Load<GameObject>(originalResource);
        }

        // Deliberately preserve the authored rebuild's proportions and skeleton. Existing
        // Player clips/root-motion removal/two-arm IK continue to control the pose.
        // URP/Lit also receives the isolated hangar's additional point lights; the existing
        // ChibiToon shader only samples its main light/SH and ignores those point lights.
        public static void ApplyHeroMaterials(Transform root)
        {
            if (!PreserveAuthoredHero || root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var source = r.sharedMaterials;
                var next = new Material[source.Length];
                bool changed = false;
                for (int i = 0; i < source.Length; i++)
                {
                    var m = source[i];
                    if (m == null || m.name.StartsWith("Cohesive_")) { next[i] = m; continue; }
                    // Engineer body only; the separate rigid gun and Heavy retain their existing finish.
                    next[i] = Plastic(m, Prefs.Engineer && r is SkinnedMeshRenderer ? "Engineer" : "Hero", Color.white, true);
                    changed |= next[i] != m;
                }
                if (changed) r.sharedMaterials = next;
            }
        }

        // A restrained interim palette for reused rigged enemies. It removes noisy old
        // surface detail but does not pretend their detailed silhouettes were rebuilt.
        public static Color EnemyColor(string role)
        {
            string n = (role ?? "").ToLowerInvariant();
            if (n.Contains("tick")) return new Color32(225, 141, 87, 255);
            if (n.Contains("carapace")) return new Color32(79, 86, 126, 255);
            if (n.Contains("leaper")) return new Color32(175, 112, 154, 255);
            if (n.Contains("bloater") || n.Contains("gravid")) return new Color32(145, 111, 178, 255);
            if (n.Contains("nest") || n.Contains("spore")) return new Color32(135, 93, 164, 255);
            return new Color32(135, 111, 172, 255);
        }

        internal static Material Plastic(Material source, string role, Color color, bool retainAlbedo)
        {
            // Runtime-owned shared materials; never modify an imported material asset.
            Texture texture = null; Vector2 tiling = Vector2.one, offset = Vector2.zero;
            if (retainAlbedo && source != null)
            {
                string p = source.HasProperty("_BaseMap") ? "_BaseMap" : source.HasProperty("_MainTex") ? "_MainTex" : null;
                if (p != null) { texture = source.GetTexture(p); tiling = source.GetTextureScale(p); offset = source.GetTextureOffset(p); }
                if (source.HasProperty("_BaseColor")) color *= source.GetColor("_BaseColor");
            }
            string key = role + "|" + (texture != null ? texture.GetHashCode() : 0) + "|" +
                ColorUtility.ToHtmlStringRGBA(color) + "|" + tiling + "|" + offset;
            if (materials.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = Mats.Lit(color, texture, null, role == "Engineer" ? .15f : .30f, 0);
            m.name = "Cohesive_" + role; m.enableInstancing = true;
            // Clear optional channels inherited from a template: only authored albedo remains.
            m.SetTexture("_BumpMap", null); m.SetTexture("_MetallicGlossMap", null);
            m.SetTexture("_EmissionMap", null); m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.DisableKeyword("_EMISSION");
            m.SetFloat("_Cull", 2); m.SetFloat("_Surface", 0); m.SetFloat("_ZWrite", 1);
            m.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
            m.SetTextureScale("_BaseMap", tiling); m.SetTextureOffset("_BaseMap", offset);
            materials[key] = m; return m;
        }

        // Includes all skin/rigid mesh parts in the model's local coordinates, rather than
        // assuming renderer zero covers the whole character. No CPU vertex readback needed.
        public static void SetEngineerAnimatedBounds(Transform root)
        {
            var authored = new Bounds(new Vector3(0, .50f, .14f), new Vector3(.92f, 1.14f, 1.34f));
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var matrix = skin.transform.worldToLocalMatrix * root.localToWorldMatrix;
                Bounds local = new Bounds(matrix.MultiplyPoint3x4(authored.center), Vector3.zero);
                for (int k = 0; k < 8; k++) local.Encapsulate(matrix.MultiplyPoint3x4(authored.center + Vector3.Scale(authored.extents,
                    new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1))));
                skin.localBounds = local;
            }
        }

        public static bool TryMeshBounds(Transform root, out Bounds bounds)
        {
            bounds = default; bool found = false;
            if (root == null) return false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh :
                    r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                if (mesh == null) continue;
                Bounds b = mesh.bounds; Matrix4x4 matrix = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
            }
            return found;
        }
    }

    // One instance on the enemy MODEL, so its material arrays remain intact across flash,
    // death and pooled reuse. In particular the authored spitter's eyes/belly keep their atlas.
    public sealed class CohesiveEnemyStyle : MonoBehaviour
    {
        Renderer[] renderers;
        Material[][] normal, flash;
        bool flashing;
        public static CohesiveEnemyStyle Attach(GameObject model, string role, bool retainAlbedo)
        {
            if (!CohesiveCharacterPolicy.Enabled || model == null) return null;
            var view = model.GetComponent<CohesiveEnemyStyle>() ?? model.AddComponent<CohesiveEnemyStyle>();
            view.Configure(role, retainAlbedo); return view;
        }
        void Configure(string role, bool retainAlbedo)
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            normal = new Material[renderers.Length][]; flash = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                var source = renderers[i].sharedMaterials;
                normal[i] = new Material[source.Length]; flash[i] = new Material[source.Length];
                for (int j = 0; j < source.Length; j++)
                {
                    normal[i][j] = CohesiveCharacterPolicy.Plastic(source[j], "Enemy_" + role,
                        retainAlbedo ? Color.white : CohesiveCharacterPolicy.EnemyColor(role), retainAlbedo);
                    flash[i][j] = CohesiveCharacterPolicy.Plastic(null, "HitFlash", new Color(.96f, .98f, 1f), false);
                }
                renderers[i].sharedMaterials = normal[i];
            }
            flashing = false;
        }
        public void SetFlash(bool value)
        {
            if (normal == null || flashing == value) return;
            flashing = value;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null)
                renderers[i].sharedMaterials = value ? flash[i] : normal[i];
        }
        void OnDisable() => SetFlash(false);
    }
}
