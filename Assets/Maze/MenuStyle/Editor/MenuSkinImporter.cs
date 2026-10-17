#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Maze.EditorTools
{
    public static class MenuSkinImporter
    {
        const string Source="Design/ArtStaging/2026-10-14-layered-menus";
        const string Target="Assets/Maze/MenuStyle/Resources/MenuStyle";
        [System.Serializable] sealed class Manifest{public Layer[] assets;}
        [System.Serializable] sealed class Layer{public string name,png;public int[] size,border_lbrt;}
        [MenuItem("Maze/Menus/Import approved layered artwork")]
        public static void Import()
        {
            var m=JsonUtility.FromJson<Manifest>(File.ReadAllText(Source+"/menu-ui-kit/manifest.json"));
            foreach(var a in m.assets)Image(Source+"/menu-ui-kit/"+a.png,Target+"/Kit/"+a.name+".png",true,a.border_lbrt);
            Image(Source+"/backgrounds/hangar-environment.png",Target+"/Backgrounds/hangar.png",false);
            Image(Source+"/backgrounds/hero-gear-showcase.png",Target+"/Backgrounds/showcase.png",false);
            Image(Source+"/zone-art/cryo-station-art.png",Target+"/ZoneArt/cryo-station.png",false);
            Image(Source+"/zone-art/evac-deck-art.png",Target+"/ZoneArt/evac-deck.png",false);
            AssetDatabase.SaveAssets();Debug.Log("MenuSkinImporter:40 reusable sprites,2 clean environments and2 image-only zone crops imported.");
        }
        static void Image(string from,string to,bool sprite,int[] border=null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));if(!File.Exists(to))File.Copy(from,to);
            AssetDatabase.ImportAsset(to,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(to);
            ti.textureType=sprite?TextureImporterType.Sprite:TextureImporterType.Default;ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.FromInput;ti.alphaIsTransparency=sprite;ti.mipmapEnabled=false;ti.isReadable=false;ti.filterMode=FilterMode.Bilinear;ti.wrapMode=TextureWrapMode.Clamp;ti.npotScale=TextureImporterNPOTScale.None;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=2048;
            if(sprite){ti.spriteImportMode=SpriteImportMode.Single;ti.spritePixelsPerUnit=100;ti.spriteBorder=border==null?Vector4.zero:new Vector4(border[0],border[1],border[2],border[3]);var st=new TextureImporterSettings();ti.ReadTextureSettings(st);st.spriteMeshType=SpriteMeshType.FullRect;st.spriteAlignment=(int)SpriteAlignment.Center;ti.SetTextureSettings(st);}ti.SaveAndReimport();
        }
    }
}
#endif
