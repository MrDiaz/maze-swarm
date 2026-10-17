#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    public static class CryoMachineryImport
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string Source = "Assets/Maze/CryoKit/Source/MachineryPreviewV2/";
        const string Key = "21_structural_thermal_island_6x4";
        [MenuItem("Maze/Cryo Kit/Import One Machinery Preview")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Import outside Play mode.");
            // Reuse the verified native source-frame/PBR converter. Its twenty-asset manifest is not changed.
            Type importer = typeof(CryoKitImport), json = importer.GetNestedType("Json", Flags), document = importer.GetNestedType("SourceDocument", Flags);
            object Parse(string path) => json.GetMethod("ReadObject", Flags).Invoke(null, new object[] { File.ReadAllText(path) });
            object Call(string method, params object[] args) => importer.GetMethod(method, Flags).Invoke(null, args);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string p in Directory.GetFiles(Source + "textures", "*.png"))
            {
                var t = (TextureImporter)AssetImporter.GetAtPath(p);
                t.textureType = TextureImporterType.Default; t.sRGBTexture = p.Contains("BaseColor") || p.Contains("Emission");
                t.mipmapEnabled = true; t.isReadable = false; t.alphaIsTransparency = false; t.wrapMode = TextureWrapMode.Repeat;
                t.maxTextureSize = p.Contains("BaseColor") ? 2048 : 1024;
                foreach (string platform in new[] { "iPhone", "Android" })
                    t.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = platform, overridden = true, maxTextureSize = t.maxTextureSize, format = TextureImporterFormat.ASTC_6x6 });
                t.SaveAndReimport();
            }
            string path = Source + Key + ".gltf";
            Call("ConfigureGltf", path);
            object sourceDoc = Activator.CreateInstance(document, Flags, null, new[] { (object)path, Parse(path) }, null);
            object record = Parse(Source + "native-record.json");
            object result = Call("BuildAsset", sourceDoc, record, Shader.Find(CryoKitImport.ShaderName));
            AssetDatabase.SaveAssets();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Maze/Resources/" + CryoMachineryPreview.Resource + ".prefab");
            Call("ValidateInstance", prefab, record);
            Directory.CreateDirectory("Logs/MachineryPreview");
            File.WriteAllText("Logs/MachineryPreview/import-report.json", JsonUtility.ToJson(result, true));
            Debug.Log("Machinery v2: unchanged 6 x 3.808 x 4 m native prefab, 17,202 triangles, one material; no Colliders.");
        }
        [MenuItem("Maze/Cryo Kit/Enable One Machinery Preview")]
        static void Enable() { CryoMachineryPreview.Enabled = true; Debug.Log("One-block Cryo preview enabled for the next Cryo A rebuild; visual acceptance pending."); }
        [MenuItem("Maze/Cryo Kit/Disable One Machinery Preview")]
        static void Disable() { CryoMachineryPreview.Enabled = false; Debug.Log("One-block preview disabled for the next Cryo A rebuild."); }
    }
}
#endif
