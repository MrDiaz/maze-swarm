#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    // Scoped to approved extracted results artwork; no existing shared UI texture settings are changed.
    public sealed class ResultsArtImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Maze/Resources/UI/Results/", StringComparison.Ordinal) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            Configure((TextureImporter)assetImporter);
        }
        static void Configure(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false; // Preserve original RGB at the deterministic keyed-alpha edges.
            importer.maxTextureSize = 2048; importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
        [MenuItem("Maze/Results/Validate Approved Artwork")]
        public static void Validate()
        {
            string[] paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Maze/Resources/UI/Results" });
            if (paths.Length != 83) throw new InvalidOperationException("Expected 71 extracted sprites plus 12 exact composition/substrate assets; found " + paths.Length);
            foreach (string guid in paths)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled || importer.isReadable || importer.wrapMode != TextureWrapMode.Clamp)
                    throw new InvalidOperationException("Incorrect results UI import settings: " + path);
                if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null) throw new InvalidOperationException("Missing results Sprite: " + path);
            }
            Debug.Log("ResultsArtImport: 83 approved/exact-source raster sprites validated.");
        }
        public static void Apply()
        {
            BuildFont();
            BuildMeasuredFont("Regular"); BuildMeasuredFont("Bold");
            foreach (string path in Directory.GetFiles("Assets/Maze/Resources/UI/Results", "*.png", SearchOption.AllDirectories))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { AssetDatabase.ImportAsset(path); importer = (TextureImporter)AssetImporter.GetAtPath(path); }
                Configure(importer); importer.SaveAndReimport();
            }
            Validate();
        }
        static void BuildMeasuredFont(string weight)
        {
            string path = "Assets/Maze/Resources/UI/ResultsFonts/RobotoCondensed" + weight + "SDF.asset";
            if (AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path) != null) return;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Maze/Resources/UI/ResultsFonts/RobotoCondensed-" + weight + ".ttf");
            var font = TMPro.TMP_FontAsset.CreateFontAsset(source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDF16, 2048, 2048, TMPro.AtlasPopulationMode.Dynamic, false);
            font.name = "Roboto Condensed Results " + weight;
            string chars = ""; for (int c = 32; c < 127; c++) chars += (char)c; chars += "·•×→★‹›";
            font.TryAddCharacters(chars, out string missing); font.atlasPopulationMode = TMPro.AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, path);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.AddObjectToAsset(font.material, font); EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            Debug.Log("Measured results font " + weight + "; missing glyphs: " + missing);
        }
        static void BuildFont()
        {
            // Existing UGUI package resources, imported non-interactively; no package installation.
            if (TMPro.TMP_Settings.instance == null)
                TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            const string folder = "Assets/Maze/Resources/UI/ResultsFonts";
            const string path = folder + "/RajdhaniResultsSDF.asset";
            if (AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path) != null) return;
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Maze/Resources/UI/Rajdhani-SemiBold.ttf");
            var font = TMPro.TMP_FontAsset.CreateFontAsset(source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDF16, 2048, 2048, TMPro.AtlasPopulationMode.Dynamic, false);
            font.name = "Rajdhani Results SDF";
            string characters = "";
            for (int c = 32; c < 256; c++) characters += (char)c;
            characters += "—–…→★";
            font.TryAddCharacters(characters, out string missing);
            font.atlasPopulationMode = TMPro.AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, path);
            foreach (var atlas in font.atlasTextures) { atlas.name = "Results Static Glyph Atlas"; AssetDatabase.AddObjectToAsset(atlas, font); }
            font.material.name = "Results Static SDF Material"; AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            Debug.Log("Results font: static SDF16 atlas from existing Rajdhani; missing requested glyphs: " + missing);
        }
    }
}
#endif
