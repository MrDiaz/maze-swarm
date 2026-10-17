using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    // Uses the approved staged manifest; no hand-authored .meta or scene/prefab YAML.
    public static class RoundedHudImporter
    {
        const string Staged = "Design/ArtStaging/2026-10-14-rounded-chibi-hud-v1.1-drafts/stylized-hud-kit";
        const string Target = "Assets/Maze/Resources/UI/RoundedHud";
        [System.Serializable] class Manifest { public Entry[] assets; }
        [System.Serializable] class Entry { public string name, png; public int[] size_px, nine_slice_lbrt_px; }
        [MenuItem("Maze/HUD/Import approved rounded artwork")]
        public static void Import()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Staged+"/manifest.json"));
            Directory.CreateDirectory(Target); int count = 0;
            foreach (var a in manifest.assets)
            {
                if (a.name.Contains("rifle") || a.name.Contains("shield") || a.name.Contains("coin") || a.name.Contains("currency") || a.name.Contains("rectangle") || a.name == "button_selected_accent") continue;
                string dest = Target+"/"+a.name+".png";
                if (!File.Exists(dest)) File.Copy(Staged+"/"+a.png,dest);
                AssetDatabase.ImportAsset(dest,ImportAssetOptions.ForceSynchronousImport);
                var ti = (TextureImporter)AssetImporter.GetAtPath(dest);
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = 100;
                var settings = new TextureImporterSettings(); ti.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(settings);
                ti.spriteBorder = new Vector4(a.nine_slice_lbrt_px[0],a.nine_slice_lbrt_px[1],a.nine_slice_lbrt_px[2],a.nine_slice_lbrt_px[3]);
                ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
                ti.sRGBTexture = true; ti.mipmapEnabled = false; ti.isReadable = false;
                ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed; ti.maxTextureSize = 2048;
                ti.SaveAndReimport(); count++;
            }
            AssetDatabase.SaveAssets(); Debug.Log("RoundedHudImporter: imported "+count+" approved PNGs; no weapon/coin/shield controls.");
        }
    }
}
