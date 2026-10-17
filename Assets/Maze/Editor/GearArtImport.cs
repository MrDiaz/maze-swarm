#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    public sealed class GearArtImport : AssetPostprocessor
    {
        const string Folder="Assets/Maze/Resources/UI/Gear";
        void OnPreprocessTexture(){if(assetPath.StartsWith(Folder+"/",StringComparison.Ordinal))Configure((TextureImporter)assetImporter);}
        static void Configure(TextureImporter importer)
        {
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=100;
            importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
            importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=false;
            importer.maxTextureSize=2048;importer.isReadable=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
        }
        public static void Apply()
        {
            foreach(var path in Directory.GetFiles(Folder,"*.png",SearchOption.AllDirectories))
            {
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null){AssetDatabase.ImportAsset(path);importer=(TextureImporter)AssetImporter.GetAtPath(path);}
                Configure(importer);importer.SaveAndReimport();
                if(AssetDatabase.LoadAssetAtPath<Sprite>(path)==null)throw new InvalidOperationException("Missing gear sprite: "+path);
            }
            Debug.Log("GearArtImport: original source sprites imported without compression or resizing.");
        }
    }
}
#endif
