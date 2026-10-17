#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace Maze.EditorTools
{
    // Bounded to the recovered Cryo explosion atlas; preserve the source alpha.
    public sealed class FirstCryoArtImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (assetPath != "Assets/Maze/Resources/Fx/FirstCryo/explosion_4x2.png") return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
#endif
