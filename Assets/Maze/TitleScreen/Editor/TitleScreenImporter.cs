#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Maze.EditorTools
{
    public static class TitleScreenImporter
    {
        // Versioned source leaves the former approved title and its original exports recoverable.
        const string Source="Design/ArtStaging/2026-10-15-blue-title";
        const string Target="Assets/Maze/BlueUI/TitleArt/Resources/BlueUI/TitleArt";
        [MenuItem("Maze/Title Screen/Import approved layers")]
        public static void Import()
        {
            var data=File.ReadAllText(Source+"/manifest.json");
            var m=JsonUtility.FromJson<TitleScreenManifest>(data);
            if(m.canvas==null || m.canvas.width!=1672 || m.canvas.height!=941 || m.glyphs==null || m.glyphs.Length!=9 || m.ui==null || m.ui.Length!=1)
                throw new InvalidDataException("Approved blue title requires its native1672x941 canvas,9 independent glyphs and1 independent Play layer.");
            Directory.CreateDirectory(Target);
            CopyIfChanged(Source+"/manifest.json",Target+"/manifest.json");
            if(!string.IsNullOrEmpty(m.background))ImportImage(m.background,false);
            foreach(var a in m.glyphs)ImportImage(a.file,true);
            foreach(var a in m.ui)ImportImage(a.file,true);
            AssetDatabase.Refresh();AssetDatabase.SaveAssets();
            Debug.Log("TitleScreenImporter: approved blue title9 independent native glyphs and independent Play imported; shared clean composition: "+m.background_resource);
        }
        static void CopyIfChanged(string source,string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            byte[] bytes=File.ReadAllBytes(source);
            if(File.Exists(target))
            {
                byte[] old=File.ReadAllBytes(target);bool same=old.Length==bytes.Length;
                for(int i=0;same && i<bytes.Length;i++)same=old[i]==bytes[i];
                if(same)return;
            }
            File.WriteAllBytes(target,bytes);
        }
        static void ImportImage(string name,bool sprite)
        {
            if(name.Contains("..") || Path.IsPathRooted(name))throw new InvalidDataException("Title asset must be a relative manifest path: "+name);
            string path=Target+"/"+name;CopyIfChanged(Source+"/"+name,path);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.textureType=sprite?TextureImporterType.Sprite:TextureImporterType.Default;
            ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.FromInput;ti.alphaIsTransparency=sprite;ti.mipmapEnabled=false;ti.isReadable=false;ti.filterMode=FilterMode.Bilinear;ti.wrapMode=TextureWrapMode.Clamp;ti.npotScale=TextureImporterNPOTScale.None;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=2048;
            if(sprite){ti.spriteImportMode=SpriteImportMode.Single;ti.spritePixelsPerUnit=100;var settings=new TextureImporterSettings();ti.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;settings.spritePivot=Vector2.one*.5f;ti.SetTextureSettings(settings);}
            ti.SaveAndReimport();
        }
    }
}
#endif
