using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Maze.EditorTools
{
    // One-click project setup and mobile builds.
    //   Maze > Setup Project   : URP asset, material templates, glTF import settings, scene, player settings
    //   Maze > Build Android   : Builds/Android/MazeSwarm.apk
    //   Maze > Build iOS       : Builds/iOS (Xcode project; open it, set your team, run)
    // Batch mode: Unity -batchmode -quit -projectPath . -executeMethod Maze.EditorTools.MazeSetup.BuildAndroid
    public static class MazeSetup
    {
        const string Root = "Assets/Maze";
        const string ScenePath = Root + "/Scenes/Main.unity";
        const string SandboxPath = Root + "/Scenes/Sandbox.unity"; // the model test arena (Sandbox.cs)
        const string BundleId = "com.mazeswarm.game"; // change before publishing to a store

        [MenuItem("Maze/Setup Project", priority = 0)]
        public static void Setup()
        {
            EnsureFolder(Root + "/Settings");
            EnsureFolder(Root + "/Resources/Materials");
            EnsureFolder(Root + "/Scenes");
            SetupUrp();
            SetupMaterials();
            SetupModels();
            SetupUiAndAudio();
            SetupScene();
            SetupPlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("Maze: setup done. Press Play, or use Maze > Build Android / Build iOS.");
        }

        // ---------------------------------------------------------------- URP
        static void SetupUrp()
        {
            var asset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                string path = Root + "/Settings/MazeURP.asset";
                asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                {
                    var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rd, Root + "/Settings/MazeURP_Renderer.asset");
                    asset = UniversalRenderPipelineAsset.Create(rd);
                    AssetDatabase.CreateAsset(asset, path);
                }
                GraphicsSettings.defaultRenderPipeline = asset;
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = asset;
                }
            }
            // mobile-friendly settings, sized for the follow camera
            var so = new SerializedObject(asset);
            Set(so, "m_ShadowDistance", 90f);
            Set(so, "m_ShadowCascadeCount", 1);
            Set(so, "m_MainLightShadowmapResolution", 2048);
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_AdditionalLightsRenderingMode", 1); // per pixel, for the explosion flash
            Set(so, "m_MSAA", 4);
            Set(so, "m_SupportsHDR", false);
            Set(so, "m_RenderScale", 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void Set(SerializedObject so, string name, object v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.Log($"Maze: URP setting {name} not found (skipped)"); return; }
            switch (v)
            {
                case float f: p.floatValue = f; break;
                case int i: p.intValue = i; break; // enums take their underlying value
                case bool b: p.boolValue = b; break;
            }
        }

        // ---------------------------------------------------------------- materials
        // Runtime materials are cloned from these, so the build keeps their shader variants.
        static void SetupMaterials()
        {
            Mat("MazeLit", "Universal Render Pipeline/Lit", null);
            Mat("MazeLitNormal", "Universal Render Pipeline/Lit", null, "_NORMALMAP");
            Mat("MazeLitNormalEmit", "Universal Render Pipeline/Lit", null, "_NORMALMAP", "_EMISSION");
            Mat("MazeLitNormalMS", "Universal Render Pipeline/Lit", null, "_NORMALMAP", "_METALLICSPECGLOSSMAP"); // the Bloater HQ
            Mat("MazeUnlit", "Universal Render Pipeline/Unlit", null);
            Mat("MazeUnlitClip", "Universal Render Pipeline/Unlit", null, "_ALPHATEST_ON"); // the map-select planet's infestation, Cryo creep
            Mat("MazeLitEmit", "Universal Render Pipeline/Lit", null, "_EMISSION");           // Cryo pads
            Mat("MazeLitDetail", "Universal Render Pipeline/Lit", null, "_DETAIL_MULX2");     // Cryo floor (painted tiles)
            Mat("MazeLitDetailN", "Universal Render Pipeline/Lit", null, "_DETAIL_MULX2", "_NORMALMAP");                         // Cryo PBR floor
            Mat("MazeLitPBREmit", "Universal Render Pipeline/Lit", null, "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_EMISSION");      // Cryo ice pads
            Mat("MazeLitPBRClipEmit", "Universal Render Pipeline/Lit", null, "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_EMISSION", "_ALPHATEST_ON"); // creep
            Mat("MazeCryoFloor", "Universal Render Pipeline/Lit", null, "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP", "_DETAIL_MULX2");   // Cryo Station deck (CryoFloor)
            Mat("MazeLitFade", "Universal Render Pipeline/Lit", false);                                          // lit decals (Cryo deck markings, frost, trim)
            Mat("MazeLitFadeEmit", "Universal Render Pipeline/Lit", false, "_EMISSION");                         // light strips
            Mat("MazeLitFadeNormalEmit", "Universal Render Pipeline/Lit", false, "_NORMALMAP", "_EMISSION");     // vent grates
            Mat("MazeFade", "Universal Render Pipeline/Unlit", false);
            Mat("MazeGlow", "Universal Render Pipeline/Unlit", true);
            Mat("MazeParticlesLit", "Universal Render Pipeline/Particles/Simple Lit", null);
            Mat("MazeParticlesAdd", "Universal Render Pipeline/Particles/Unlit", true);
            Mat("MazeParticlesGloss", "Universal Render Pipeline/Particles/Simple Lit", null, "_SPECULAR_COLOR");
            Mat("MazeParticlesFade", "Universal Render Pipeline/Particles/Unlit", false);
        }

        static void Mat(string name, string shader, bool? transparentAdditive, params string[] keywords)
        {
            string path = $"{Root}/Resources/Materials/{name}.mat";
            var s = Shader.Find(shader);
            if (s == null) { Debug.LogError($"Maze: shader {shader} missing (is URP installed?)"); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
            m.shader = s;
            if (shader.EndsWith("/Lit")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (transparentAdditive.HasValue) Mats.MakeTransparent(m, transparentAdditive.Value);
            foreach (var k in keywords) m.EnableKeyword(k); // keeps these variants through build stripping
            EditorUtility.SetDirty(m);
        }

        // ---------------------------------------------------------------- models
        // glTFast: import the hero/barrel/enemies with Legacy animation (the game drives an Animation component).
        static void SetupModels()
        {
            foreach (var name in new[] { "hero", "hero_vanguard", "hero_vanguard_meshy", "barrel", "drone", "bloater", "leaper" })
            {
                string path = $"{Root}/Resources/Models/{name}.glb";
                var imp = AssetImporter.GetAtPath(path);
                if (imp == null) { Debug.LogWarning($"Maze: {path} has no importer (is glTFast installed?)"); continue; }
                var so = new SerializedObject(imp);
                bool changed = false;
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.Enum && it.propertyType != SerializedPropertyType.Integer) continue;
                    if (!it.name.ToLower().Contains("animationmethod")) continue;
                    if (it.intValue != 1) { it.intValue = 1; changed = true; } // 1 = Legacy
                }
                if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); imp.SaveAndReimport(); Debug.Log($"Maze: {name}.glb set to Legacy animation"); }
            }
        }

        // ---------------------------------------------------------------- menu art, sounds, haptics plugin
        static void SetupUiAndAudio()
        {
            foreach (var (name, max) in new[] { ("menu_bg", 4096), ("menu_logo", 2048) })
            {
                string path = $"{Root}/Resources/UI/{name}.png";
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning($"Maze: {path} missing"); continue; }
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None; // the 2340x1080 plate must not be squashed to a power of two
                ti.maxTextureSize = max;
                ti.alphaIsTransparency = name != "menu_bg";
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            // models' URP metallic-smoothness maps (Tools/bloater-hq.mjs): linear data, alpha = smoothness
            foreach (var guid in AssetDatabase.FindAssets("_ms t:Texture2D", new[] { Root + "/Resources/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti) || (System.IO.Path.GetFileName(path).StartsWith("Sentinel") || System.IO.Path.GetFileName(path).StartsWith("Vanguard") || System.IO.Path.GetFileName(path).StartsWith("Minigun") || System.IO.Path.GetFileName(path).StartsWith("Door") || System.IO.Path.GetFileName(path).StartsWith("Leaper") || System.IO.Path.GetFileName(path).StartsWith("CargoCrate"))) continue;
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.mipmapEnabled = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
                ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            // the hero's full-resolution maps (Tools/sentinel.mjs): 2048, ASTC 4x4 on iOS, trilinear + aniso 4, a slight
            // negative mip bias so the small on-screen hero stays crisp
            // Escape Test start-room floor (Tools/escape_floor.mjs): x2 detail albedo (linear) + normal map, ASTC 4x4 on iOS
            foreach (var (file, normal) in new[] { ("floor_worn_detail", false), ("floor_worn_normal", true) })
            {
                if (!(AssetImporter.GetAtPath($"{Root}/Resources/Textures/EscapeFloor/{file}.png") is TextureImporter ti)) continue;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = false; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4; // detail: linear, 0.5 = neutral
                ti.wrapMode = TextureWrapMode.Repeat; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.ASTC_4x4, compressionQuality = 100 });
                ti.SaveAndReimport();
            }
            // the escape floor's grime blotches (Tools/floor_dirt.mjs): alpha atlas, clamped, ASTC on iOS
            if (AssetImporter.GetAtPath($"{Root}/Resources/Textures/EscapeFloor/floor_dirt.png") is TextureImporter fdi)
            {
                fdi.textureType = TextureImporterType.Default; fdi.sRGBTexture = true; fdi.mipmapEnabled = true; fdi.wrapMode = TextureWrapMode.Clamp;
                fdi.alphaSource = TextureImporterAlphaSource.FromInput; fdi.alphaIsTransparency = true; fdi.maxTextureSize = 1024;
                fdi.textureCompression = TextureImporterCompression.CompressedHQ;
                fdi.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 100 });
                fdi.SaveAndReimport();
            }
            // (also the Iron Vanguard preview, Tools/vanguard.mjs)
            foreach (var mat in new[] { "SentinelMat", "VanguardMat", "MinigunMat", "DoorFrameMat", "DoorPanelMat", "LeaperMat", "CargoCrateMat", "VanguardMeshyMat", "ScoutMat" })
            foreach (var (suffix, normal, linear) in new[] { ("_base", false, false), ("_normal", true, true), ("_ms", false, true), ("_emit", false, false) })
            {
                if (!(AssetImporter.GetAtPath($"{Root}/Resources/Models/{mat}{suffix}.png") is TextureImporter ti)) continue;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !linear; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4; ti.mipMapBias = -0.3f;
                ti.alphaSource = suffix == "_ms" ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; ti.alphaIsTransparency = false;
                ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.ASTC_4x4, compressionQuality = 100 });
                ti.SaveAndReimport();
            }
            // map select art: sector cards, enemy portraits (alpha), the arena render, the planet (wraps in longitude)
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Resources/UI/Sectors", Root + "/Resources/UI/Portraits" }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = true; ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 512; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            foreach (var (name, max, wrapU) in new[] { ("arena_cryo.png", 1024, false), ("arena_escape.png", 1024, false), ("drop_bay.jpg", 2048, false), ("planet.jpg", 2048, true), ("planet_infest.png", 1024, true) })
            {
                if (!(AssetImporter.GetAtPath($"{Root}/Resources/UI/{name}") is TextureImporter ti)) { Debug.LogWarning($"Maze: Resources/UI/{name} missing"); continue; }
                ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = true; ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = max; ti.alphaIsTransparency = name.EndsWith(".png");
                ti.wrapModeU = wrapU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.wrapModeV = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            // Cryo Station tiles (Tools/cryo_tiles.py): tile in both directions, except the drift strip (fades across v)
            // and the creep decal (clamped)
            foreach (var guid in !AssetDatabase.IsValidFolder(Root + "/Resources/Cryo") ? new string[0] : AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Resources/Cryo" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string n = System.IO.Path.GetFileNameWithoutExtension(path);
                ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
                ti.sRGBTexture = n != "floor_detail"; ti.alphaIsTransparency = n == "drift" || n == "creep_mask";
                ti.wrapModeU = n == "creep_mask" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.wrapModeV = n == "creep_mask" || n == "drift" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.maxTextureSize = 512; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            // the painted operative portrait (briefing + SQUAD card): a UI sprite, max 1024, compressed
            foreach (var pf in new[] { "hero_portrait", "vanguard_portrait" }) // (Iron Vanguard's: Concepts/Heavy/portrait_vanguard.png)
            if (AssetImporter.GetAtPath($"{Root}/Resources/UI/{pf}.png") is TextureImporter hpi)
            {
                hpi.textureType = TextureImporterType.Sprite; hpi.spriteImportMode = SpriteImportMode.Single;
                hpi.mipmapEnabled = false; hpi.alphaIsTransparency = false; hpi.wrapMode = TextureWrapMode.Clamp;
                hpi.maxTextureSize = 1024; hpi.textureCompression = TextureImporterCompression.CompressedHQ;
                hpi.SaveAndReimport();
            }
            // the approved briefing's original extracted pixels (Design/Concepts/2026-10-13-original-briefing-assets-4aewixjw):
            // sprites at native size, no mipmaps, uncompressed (tiny crops; ASTC would smear them), bilinear
            foreach (var guid in !AssetDatabase.IsValidFolder(Root + "/Resources/UI/BriefOriginal") ? new string[0] : AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Resources/UI/BriefOriginal" }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.npotScale = TextureImporterNPOTScale.None;
                ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            // the painted progression icons (Concepts/Progression/Icons, 256 px): UI sprites with mipmaps (crisp at ~64 px)
            foreach (var guid in !AssetDatabase.IsValidFolder(Root + "/Resources/UI/Icons") ? new string[0] : AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Resources/UI/Icons" }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear; ti.maxTextureSize = 256; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 256, format = TextureImporterFormat.ASTC_4x4, compressionQuality = 100 });
                ti.SaveAndReimport();
            }
            // Cryo Station deck (Design/Concepts/2026-10-13-cryo-floor): the 8 m tile at 2048 with mipmaps and aniso 8 (a grazing
            // camera), ASTC 4x4 on iOS so it stays sharp; normals as normal maps; metal/smoothness, AO and the macro linear;
            // decals clamp across (V) and repeat along their run (U)
            foreach (var guid in !AssetDatabase.IsValidFolder(Root + "/Resources/Cryo/floor2") ? new string[0] : AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Resources/Cryo/floor2" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string n = System.IO.Path.GetFileNameWithoutExtension(path);
                bool nrm = n.EndsWith("_normal"), lin = n.EndsWith("_ms") || n.EndsWith("_ao") || n.EndsWith("_macro"), deck = n.StartsWith("cryo_floor");
                bool alpha = n == "frost_drift" || n == "hazard_albedo" || n == "strip_albedo" || n == "grate_albedo" || n == "goo_splat" || n.EndsWith("_ms");
                ti.textureType = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !nrm && !lin; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = deck ? 8 : 4;
                ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = alpha && !n.EndsWith("_ms");
                bool runs = n == "frost_drift" || n == "hazard_albedo" || n.StartsWith("strip");
                ti.wrapModeU = deck || runs ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.wrapModeV = deck ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.ASTC_4x4, compressionQuality = 100 });
                ti.SaveAndReimport();
            }
            // Cryo Station PBR maps (Tools/cryo_pbr.mjs): 2K sources, 1K ASTC on iOS. Normals as normal maps,
            // metallic-smoothness and the floor detail linear, colour/emission sRGB; the creep decal clamps.
            foreach (var guid in !AssetDatabase.IsValidFolder(Root + "/Resources/Cryo/pbr") ? new string[0] : AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Resources/Cryo/pbr" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string n = System.IO.Path.GetFileNameWithoutExtension(path);
                bool nrm = n.EndsWith("_normal"), lin = n.EndsWith("_ms") || n == "floor_detail";
                ti.textureType = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !nrm && !lin; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
                ti.alphaSource = n.EndsWith("_ms") || n == "creep_color" ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = n == "creep_color";
                ti.wrapMode = n.StartsWith("creep") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50 });
                ti.SaveAndReimport();
            }
            // gunfire textures (Tools/fx_textures.py): soft additive sprites, so mipmaps + trilinear, never point
            // filtered, clamped so flipbook cells don't wrap
            foreach (var name in new[] { "flash_star", "flash_cone", "streaks", "puff", "ring" })
            {
                string path = $"{Root}/Resources/Fx/{name}.png";
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning($"Maze: {path} missing (run Tools/fx_textures.py)"); continue; }
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 2;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.npotScale = TextureImporterNPOTScale.None; // the 3x3 flipbooks are 1536 wide
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Resources/Audio" }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is AudioImporter ai)) continue;
                string an = System.IO.Path.GetFileNameWithoutExtension(ai.assetPath);
                if (an == "mus_menu" || an == "amb_bridge" || an == "mus_run_cryo" || an == "amb_cryo_station")
                {   // the title music and the bridge bed: stereo seamless loops, Vorbis kept compressed in memory
                    ai.forceToMono = false; ai.loadInBackground = true;
                    var ls = ai.defaultSampleSettings; ls.loadType = AudioClipLoadType.CompressedInMemory; ls.compressionFormat = AudioCompressionFormat.Vorbis; ls.quality = 0.7f;
                    ai.defaultSampleSettings = ls; ai.SaveAndReimport(); continue;
                }
                ai.forceToMono = true;
                // the clips are already peak-normalized to -1 dBFS by Tools/prep_sfx.py; keep that headroom
                var so = new SerializedObject(ai);
                var norm = so.FindProperty("m_Normalize");
                if (norm != null) { norm.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Debug.LogWarning("Maze: AudioImporter m_Normalize not found");
                var st = ai.defaultSampleSettings;
                st.loadType = AudioClipLoadType.DecompressOnLoad; // short one-shots: no decode latency
                st.compressionFormat = AudioCompressionFormat.ADPCM;
                ai.defaultSampleSettings = st;
                ai.SaveAndReimport();
            }
            if (AssetImporter.GetAtPath("Assets/Plugins/iOS/MazeHaptics.mm") is PluginImporter pi)
            {
                pi.SetCompatibleWithAnyPlatform(false);
                pi.SetCompatibleWithEditor(false);
                pi.SetCompatibleWithPlatform(BuildTarget.iOS, true);
                pi.SaveAndReimport();
            }
            else Debug.LogWarning("Maze: Assets/Plugins/iOS/MazeHaptics.mm missing (no haptics on iOS)");
        }

        // ---------------------------------------------------------------- scene
        static void SetupScene()
        {
            SetupSandboxScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Game").AddComponent<Game>();
            // saved in the scene so the build keeps the fog/ambient shader variants
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color32(0xe3, 0xe6, 0xeb, 255);
            RenderSettings.fogStartDistance = 90; RenderSettings.fogEndDistance = 170;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.skybox = null;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true), new EditorBuildSettingsScene(SandboxPath, true) };
        }

        // the Sandbox scene is just a "Sandbox" object; Sandbox.cs builds the arena, camera and panel on Play
        static void SetupSandboxScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Sandbox").AddComponent<Sandbox>();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.skybox = null; RenderSettings.fog = false;
            EditorSceneManager.SaveScene(scene, SandboxPath);
            Directory.CreateDirectory(Path.GetFullPath("Sandbox/Drop")); // GLBs dropped here show up in the Sandbox list
        }

        [MenuItem("Maze/Open Sandbox", priority = 2)]
        public static void OpenSandbox()
        {
            if (!File.Exists(SandboxPath)) { SetupSandboxScene(); EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true), new EditorBuildSettingsScene(SandboxPath, true) }; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(SandboxPath);
        }

        // ---------------------------------------------------------------- player settings
        static void SetupPlayer()
        {
            PlayerSettings.companyName = "Maze";
            PlayerSettings.productName = "Maze Swarm";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)24;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Low);
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.gpuSkinning = true; // up to ~90 skinned Drones on screen: skin them in compute, not on the CPU
        }

        // ---------------------------------------------------------------- builds
        [MenuItem("Maze/Build Android (APK)", priority = 20)]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/MazeSwarm.apk");

        [MenuItem("Maze/Build iOS (Xcode project)", priority = 21)]
        public static void BuildIOS() => Build(BuildTarget.iOS, "Builds/iOS");

        public static void SetupBatch() => Setup();

        // Batch sanity check for the hero's rifle: prints hand/muzzle in the player's local frame
        // in the shoot pose (expect the muzzle ~0.6-0.7 m ahead of the hand along +Z, roughly level).
        public static void GunCheck()
        {
            var ar = Resources.Load<GameObject>("Models/ar");
            if (ar != null)
            {
                Vector3 low = Vector3.positiveInfinity;
                foreach (var mf in ar.GetComponentsInChildren<MeshFilter>())
                    foreach (var v in mf.sharedMesh.vertices) { var w = ar.transform.InverseTransformPoint(mf.transform.TransformPoint(v)); if (w.y < low.y) low = w; }
                Debug.Log($"Maze GunCheck: lowest vertex (magazine) in model space {low:F3} (glTF x=+0.166, so mirrored should be x<0)");
            }
            var g = new GameObject("Game").AddComponent<Game>();
            new GameObject("Fx").AddComponent<Fx>().Init(g.cfg.weapons);
            var p = new GameObject("Player").AddComponent<Player>();
            p.Init(g);
            if (p.gun == null) { Debug.LogError("Maze GunCheck: no gun attached"); return; }
            p.PoseShoot();
            var t = p.transform;
            Debug.Log($"Maze GunCheck: hand {t.InverseTransformPoint(p.Hand.position):F2} muzzle {t.InverseTransformPoint(p.MuzzleWorld):F2} gun lossyScale {p.gun.lossyScale.x:F4}");
        }

        static void Build(BuildTarget target, string output)
        {
            if (!File.Exists(ScenePath)) Setup();
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath, SandboxPath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"Maze: {target} build {s.result}, {s.totalErrors} errors, {s.totalSize / (1024 * 1024)} MB -> {output}");
            // iPhones only run apps above 60 Hz with this Info.plist key (ProMotion)
            if (target == BuildTarget.iOS && s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                string plist = Path.Combine(output, "Info.plist");
                foreach (var cmd in new[] { "Delete :CADisableMinimumFrameDurationOnPhone", "Add :CADisableMinimumFrameDurationOnPhone bool true" })
                {
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/libexec/PlistBuddy", $"-c \"{cmd}\" \"{plist}\"") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                    p.WaitForExit();
                }
                Debug.Log("Maze: Info.plist CADisableMinimumFrameDurationOnPhone = true");
            }
            if (Application.isBatchMode && s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
            else if (!Application.isBatchMode && s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorUtility.RevealInFinder(output);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
