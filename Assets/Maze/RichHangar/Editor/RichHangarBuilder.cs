#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    public static class RichHangarBuilder
    {
        const string Root = "Assets/Maze/RichHangar/Resources/RichHangar/Art";
        public static void Import()
        {
            foreach (var path in Directory.GetFiles(Root, "*.png"))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool scene = Path.GetFileName(path).StartsWith("scene-");
                importer.textureType = scene ? TextureImporterType.Default : TextureImporterType.Sprite;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = !scene;
                importer.mipmapEnabled = scene;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = scene ? FilterMode.Trilinear : FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                if (!scene)
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100;
                    importer.spriteBorder = name.StartsWith("button_") || name == "tray" || name == "pill" ? Vector4.one * 32 : Vector4.zero;
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                }
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }
        public static void ImportAndReview() { Import(); LivingEnvironmentBuilder.Review(); }
    }
}
#endif
