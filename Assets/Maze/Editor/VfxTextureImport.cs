using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    // Import settings for the hazard VFX textures (Resources/Fx/Vfx): raw values (sRGB off, the shaders treat colours in gamma),
    // full 2048 size, mipmaps; the noise tile repeats and stays uncompressed (its four channels hold different noises).
    public class VfxTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Fx/Vfx/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = !assetPath.Contains("fire_8x8");   // fire is premultiplied on black
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            bool noise = assetPath.Contains("vfx_noise");
            ti.wrapMode = noise ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.textureCompression = noise ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            ti.filterMode = FilterMode.Bilinear;
        }
    }
}
