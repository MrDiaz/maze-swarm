using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
namespace Maze
{
    public static class CryoSoftEnvironment
    {
#if UNITY_EDITOR
        public static bool Enabled;   // editor: Maze > Cryo Environment Review
#else
        public static bool Enabled = true;   // device builds: the rounded room on the first Cryo floor (for now, Manuel 2026-10-14)
#endif
        public const string Resource = "CryoSoft/CryoSoftRoom";
        // every Cryo Station layout has its room (2026-10-14): cryo-a keeps the original first-floor room; cryo-b / cryo-c /
        // cryo-boss come from the same Blender kit (Tools/CryoSoft/source/build_soft_cryo.py --layout <id>)
        public static bool Objects = true;   // the Meshy Cryo objects on the kit rooms' anchors (2026-10-15); false = the kit's own machines (the earlier rooms)
        public static string ResourceFor(string layout)
        {
            if (layout == null || !layout.StartsWith("cryo-station/")) return null;
            string id = layout.Substring("cryo-station/".Length);
            if (Objects && Resources.Load<GameObject>("CryoSoft/CryoSoftRoomProps_" + id) != null) return "CryoSoft/CryoSoftRoomProps_" + id;
            return id == "cryo-a" ? Resource : "CryoSoft/CryoSoftRoom_" + id;
        }
        static string CurrentResource => ResourceFor(MazeMap.LayoutName(MazeMap.MapIndex));
        public static bool For(bool run, SectorDef sector, int floor) => Enabled && !FloorEditing.FloorRuntime.EditedGeometry && run && sector?.id == "cryo-station" && CurrentResource is string r && Resources.Load<GameObject>(r) != null;
        public static bool Active { get { var g = Object.FindAnyObjectByType<Game>(); return g != null && For(g.run, g.sector, g.floor); } }
        public static GameObject Build(Transform parent)
        {
            string res = CurrentResource; var go = Object.Instantiate(Resources.Load<GameObject>(res), parent, false);
            if (res.Contains("RoomProps_")) CryoProps.Spawn(go, res.Substring(res.IndexOf("RoomProps_") + "RoomProps_".Length));   // before the scene info: it adopts the props' materials too
            go.name = "Cryo — rounded environment review variant"; go.AddComponent<CryoSoftSceneInfo>(); return go;
        }
    }
    // on a kit room prefab: its baked floor contact map and the floor size it spans (metres)
    public sealed class CryoSoftSceneInfo : MonoBehaviour
    {
        // Transient room lighting only; originals and material assets are never edited by this component.
        public static bool VisualsActive => owner != null;
        static CryoSoftSceneInfo owner; static Light originalSun; static Quaternion originalRotation; static float originalShadowDistance = -1, originalShadowStrength = -1;
        static UnityEngine.Rendering.Universal.UniversalAdditionalCameraData originalCameraData; static bool originalCameraPost;
        Game sceneGame;
        readonly Dictionary<string, Color> baseColors = new();
        // HDR glow (approved 2026-10-14): a global bloom Volume that exists only while this room does, and post-processing
        // switched on for the game camera only meanwhile; every other zone renders with post off, as before.
        public static bool Glow = true;
        GameObject volumeGo; UnityEngine.Rendering.Universal.UniversalAdditionalCameraData camData; bool camPostWas;
        void GlowOn()
        {
            if (!Glow || volumeGo != null || Camera.main == null) { if (Glow && volumeGo == null) Debug.LogWarning("Cryo glow: no main camera yet"); return; }
            camData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(Camera.main);   // (adds the URP camera data if the game camera has none yet: the real game camera didn't, so post never switched on)
            camPostWas = camData == originalCameraData ? originalCameraPost : camData.renderPostProcessing; camData.renderPostProcessing = true;
            Debug.Log($"Cryo glow: bloom on (camera HDR {Camera.main.allowHDR}, post was {camPostWas})");
            volumeGo = new GameObject("Cryo room glow (bloom)"); volumeGo.transform.SetParent(transform, false);
            var vol = volumeGo.AddComponent<UnityEngine.Rendering.Volume>(); vol.isGlobal = true; vol.priority = 10;
            var prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>(); vol.sharedProfile = prof;
            var bloom = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.threshold.Override(1.7f); bloom.intensity.Override(1.4f); bloom.scatter.Override(0.5f); bloom.tint.Override(new Color(1f, .97f, .94f)); bloom.highQualityFiltering.Override(false);   // threshold is gamma: 1.7 ~ linear 3.2; its soft knee starts ~1.6, well above the whites (<= 1.17 after the shoulder): only the practical lights pass; low scatter keeps the halo tight so the darks stay dark
        }
        void GlowOff()
        {
            if (camData != null && owner == this) camData.renderPostProcessing = camPostWas;
            if (volumeGo != null) { var v = volumeGo.GetComponent<UnityEngine.Rendering.Volume>(); if (v != null && v.sharedProfile != null) Destroy(v.sharedProfile); Destroy(volumeGo); }
            volumeGo = null; camData = null;
        }
        readonly List<Material> owned = new();
        public int preset = 2;
        void Awake()
        {
            var cache = new Dictionary<Material, Material>();
            foreach (var r in GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(m =>
            { if (m == null || !m.HasProperty("_FloorAO")) return m; if (!cache.TryGetValue(m, out var clone)) { clone = new Material(m) { name = m.name }; cache[m] = clone; owned.Add(clone); baseColors[clone.name] = clone.GetColor("_BaseColor"); } return clone; }).ToArray();
            var room = GetComponent<CryoSoftRoomData>();   // a kit room's own contact map and footprint (the original room: the material's 44 x 32 map)
            if (room != null) foreach (var m in owned) { m.SetVector("_AOSize", room.size); if (m.GetFloat("_FloorAO") > .5f && room.ao != null) m.SetTexture("_FloorAOMap", room.ao); }
            sceneGame = Object.FindAnyObjectByType<Game>();
            AcquireLighting(); SetPreset(3);
        }
        void AcquireLighting()
        {
            if (owner == null)
            {
                originalSun = RenderSettings.sun;
                if (originalSun != null) originalRotation = originalSun.transform.rotation;
                originalCameraData = Camera.main != null ? UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(Camera.main) : null;
                originalCameraPost = originalCameraData != null && originalCameraData.renderPostProcessing;
            }
            owner = this;
        }
        void ReleaseLighting()
        {
            if (owner != this) return;
            if (originalSun != null)
            {
                originalSun.transform.rotation = originalRotation;
                if (originalShadowStrength >= 0) originalSun.shadowStrength = originalShadowStrength;
            }
            if (originalCameraData != null) originalCameraData.renderPostProcessing = originalCameraPost;
            if (originalShadowDistance >= 0 && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset asset)
                asset.shadowDistance = originalShadowDistance;
            owner = null; originalSun = null; originalCameraData = null;
            originalShadowDistance = originalShadowStrength = -1;
        }
        void LateUpdate()
        {
            // The menu retains the level object. Its transient lighting must end when the run ends,
            // rather than waiting for object destruction. Replaced rooms never release a newer owner's state.
            bool wanted = CryoSoftEnvironment.Enabled && sceneGame != null && sceneGame.run && sceneGame.sector?.id == "cryo-station";
            if (!wanted && owner == this) { GlowOff(); ReleaseLighting(); }
            else if (wanted && owner == null) { AcquireLighting(); SetPreset(preset); }
        }
        // preset 3: the reference match (exact-user-reference.png, 2026-10-14). A warm high key from front-left, cool lavender
        // sky fill, blue-tinted soft shadows at near full strength, the reference palette, a plastic sheen and no early
        // highlight clamp. Presets 0-2 are the earlier study, unchanged.
        static readonly Dictionary<string, string> RefPalette = new()
        {
            { "Soft_White", "#d3dfe4" }, { "Soft_Ivory", "#c9d6e2" }, { "Soft_Blue", "#2c4f9c" }, { "Soft_Navy", "#1c3268" },
            { "Soft_Floor", "#93adc6" }, { "Soft_FloorLight", "#9ab4cc" }, { "Soft_FloorShade", "#8aa4c0" }, { "Soft_Seam", "#8aa4c0" },
            { "Soft_Ice", "#88c6f4" }, { "Soft_Dock", "#31558e" }, { "Soft_Cyan", "#5fdcef" }, { "Soft_Glass", "#5fe4f5" }, { "Soft_Amber", "#ffc04a" },
        };
        public static Vector3 RefKey = new Vector3(-30, 30, 12);
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        void ApplyReference()
        {
            if (originalSun != null) { originalSun.transform.rotation = Quaternion.LookRotation(-RefKey); if (originalShadowStrength < 0) originalShadowStrength = originalSun.shadowStrength; originalSun.shadowStrength = 1f; }   // full-strength contact shadows in this room   // from the left and a little behind: shadows fall right and toward the camera, as in the reference
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {   // a tighter shadow range while the room exists: sharper contact shadows (restored on exit)
                if (originalShadowDistance < 0) originalShadowDistance = urp.shadowDistance;
                urp.shadowDistance = 60f;
            }
            foreach (var m in owned)
            {
                if (RefPalette.TryGetValue(m.name, out var hx)) m.SetColor("_BaseColor", Hex(hx));
                m.SetFloat("_RefMode", 1); m.SetFloat("_Shoulder", .92f);
                m.SetColor("_SkyFill", new Color(.80f, .82f, .97f)); m.SetColor("_GroundFill", new Color(.47f, .51f, .67f));
                m.SetColor("_KeyColor", new Color(1f, .97f, .92f)); m.SetColor("_ShadowTint", new Color(.07f, .10f, .27f));
                m.SetFloat("_AmbientStrength", .50f); m.SetFloat("_KeyGain", 1.24f);
                m.SetFloat("_ShadowOpacity", .90f); m.SetFloat("_ShadowSpread", 2.2f); m.SetFloat("_ShadowTaps", 8);
                m.SetFloat("_WarmSpill", .22f); m.SetFloat("_AOIntensity", m.GetFloat("_FloorAO") > .5f ? .90f : .75f);   // deeper baked contact at wall feet and under machines
                bool plastic = m.name == "Soft_White" || m.name == "Soft_Ivory" || m.name == "Soft_Blue" || m.name == "Soft_Navy";
                m.SetFloat("_Sheen", !plastic ? 0f : m.name == "Soft_Blue" || m.name == "Soft_Navy" ? .35f : 1f);   // pass 10: the reference's navy pads read deep blue; full sheen lifted ours to lavender
                bool glow = Glow;
                m.SetFloat("_EmitAfter", glow && (m.name == "Soft_Amber" || m.name == "Soft_Cyan" || m.name == "Soft_Glass") ? 1f : 0f);
                if (m.name == "Soft_Amber") m.SetColor("_EmissionColor", Hex("#ffb238") * (glow ? 2.4f : 1.6f));
                if (m.name == "Soft_Cyan") m.SetColor("_EmissionColor", glow ? Hex("#10dcf5") * 2.0f : Hex("#4fe6fa") * 1.5f);   // saturated: the core stays cyan above 1
                if (m.name == "Soft_Glass") { m.SetColor("_EmissionColor", glow ? Hex("#10d4f8") * 2.1f : Hex("#4fe6fa") * .7f); m.SetFloat("_GlassAlpha", glow ? .55f : .21f); }   // a solid glowing tube
                if (m.name.StartsWith("Prop_"))   // the Meshy Cryo objects: plastic sheen, and their painted cyan / amber lights glow with the room
                { m.SetFloat("_Sheen", m.name == "Prop_Tyre" ? 0f : .55f); m.SetFloat("_TexGlow", glow ? 1.6f : .5f); m.SetFloat("_EmitAfter", glow ? 1f : 0f); m.SetFloat("_AOIntensity", 0f); }
            }
        }
        public void SetPreset(int choice)
        {
            preset = choice;
            if (choice == 3) { ApplyReference(); if (Glow) GlowOn(); else GlowOff(); return; }
            GlowOff();
            if (originalSun != null && originalShadowStrength >= 0) { originalSun.shadowStrength = originalShadowStrength; originalShadowStrength = -1; }
            foreach (var m in owned) { m.SetFloat("_EmitAfter", 0); m.SetFloat("_RefMode", 0); m.SetFloat("_Shoulder", .75f); m.SetFloat("_Sheen", 0); m.SetFloat("_ShadowTaps", 4); if (baseColors.TryGetValue(m.name, out var bc)) m.SetColor("_BaseColor", bc); }
            if (originalShadowDistance >= 0 && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset u0) u0.shadowDistance = originalShadowDistance;
            Vector3 key = choice == 0 ? new Vector3(-18,34,-16) : choice == 1 ? new Vector3(-24,28,-18) : new Vector3(-20,32,-19);
            if (originalSun != null) originalSun.transform.rotation = Quaternion.LookRotation(-key);
            float fill = choice == 0 ? .50f : choice == 1 ? .37f : .45f;
            float gain = choice == 0 ? 1.12f : choice == 1 ? 1.35f : 1.23f;
            float shadow = choice == 0 ? .68f : choice == 1 ? .76f : .72f;
            float spread = choice == 0 ? 2.6f : choice == 1 ? 2.0f : 2.4f;
            foreach (var m in owned)
            {
                m.SetFloat("_AmbientStrength", fill); m.SetFloat("_KeyGain", gain); m.SetFloat("_ShadowOpacity", shadow); m.SetFloat("_ShadowSpread", spread); m.SetFloat("_WarmSpill", choice == 0 ? .12f : choice == 1 ? .22f : .20f);
                if (m.name == "Soft_Amber") m.SetColor("_EmissionColor", m.GetColor("_BaseColor") * .80f);
            }
        }
        void OnDestroy()
        {
            GlowOff();
            ReleaseLighting();
            foreach (var m in owned) if (m != null) Destroy(m);
        }
    }
}
