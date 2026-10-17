#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
namespace Maze.EditorTools
{
    [InitializeOnLoad]
    public static class CryoSoftBuilder
    {
        const string Root = "Assets/Maze/CryoSoft";
        const string Choice = "Maze.CryoSoft.ArtReviewChoice";
        static readonly string[] Names = {"Ivory","White","Blue","Navy","Floor","FloorLight","FloorShade","Seam","Ice","Dock","Cyan","Glass","Amber"};
        static readonly string[] Hex = {"#cbdce9","#e7eef4","#355a97","#162c4f","#8393aa","#899bb1","#7b8da5","#6e819f","#9dd1e7","#4063b0","#45d7ef","#4bdaef","#ffc266"};
        static readonly float[] Rough = {.68f,.62f,.62f,.72f,.78f,.78f,.78f,.84f,.66f,.70f,.32f,.32f,.48f};
        static CryoSoftBuilder() { CryoSoftEnvironment.Enabled = EditorPrefs.GetBool(Choice, false); }
        [MenuItem("Maze/Cryo Environment Review/Original")]
        public static void Original() { Choose(false); }
        [MenuItem("Maze/Cryo Environment Review/Rounded Blender variant")]
        public static void Rounded() { Choose(true); }
        static void Choose(bool rounded)
        {
            EditorPrefs.SetBool(Choice, rounded); CryoSoftEnvironment.Enabled = rounded;
            // Rejected trials remain off. Selection applies on the next normal room build.
            CryoBoundaryPreview.Enabled = false; CryoMachineryPreview.Enabled = false;
            Debug.Log("Cryo environment selected: " + (rounded ? "rounded Blender variant" : "original") + ". Re-enter the first Cryo floor to preview.");
        }
        [MenuItem("Maze/Cryo Environment Review/Original", true)] static bool ValidateOriginal() { Menu.SetChecked("Maze/Cryo Environment Review/Original", !CryoSoftEnvironment.Enabled); return true; }
        [MenuItem("Maze/Cryo Environment Review/Rounded Blender variant", true)] static bool ValidateRounded() { Menu.SetChecked("Maze/Cryo Environment Review/Rounded Blender variant", CryoSoftEnvironment.Enabled); return true; }
        [MenuItem("Maze/Cryo Environment Review/Import authored Blender room")]
        // the kit rooms for the other Cryo Station layouts (Tools/CryoSoft/exports: CryoSoftRoom_<id>.fbx + SoftFloorAO_<id>.png,
        // copied into Source/): same import, the shared Soft_* materials, a CryoSoftRoomData with the room's contact map
        //   Unity -batchmode -quit -projectPath . -executeMethod Maze.EditorTools.CryoSoftBuilder.ImportKitRooms
        public static void ImportKitRooms() => ImportRooms(new[] { "cryo-b", "cryo-c", "cryo-boss" }, "CryoSoftRoom_", "SoftFloorAO_", "CryoSoftRoom_");
        // the kit rooms with prop anchors (2026-10-15): CryoSoftRoomProps_<id>.fbx + SoftFloorAOProps_<id>.png, all four layouts;
        // their props json sits in Resources/CryoSoft (CryoProps spawns the Meshy objects on it)
        //   Unity -batchmode -quit -projectPath . -executeMethod Maze.EditorTools.CryoSoftBuilder.ImportPropRooms
        public static void ImportPropRooms() => ImportRooms(new[] { "cryo-a", "cryo-b", "cryo-c", "cryo-boss" }, "CryoSoftRoomProps_", "SoftFloorAOProps_", "CryoSoftRoomProps_");
        static void ImportRooms(string[] ids, string fbxPre, string aoPre, string prefabPre)
        {
            AssetDatabase.Refresh();
            var mats = Names.ToDictionary(n => "Soft_" + n, n => AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Soft_" + n + ".mat"));
            if (mats.Values.Any(m => m == null)) throw new Exception("Run Import first: the Soft_* materials are missing");
            foreach (var id in ids)
            {
                string fbx = Root + "/Source/" + fbxPre + id + ".fbx", aoPath = Root + "/Source/" + aoPre + id + ".png";
                var importer = (ModelImporter)AssetImporter.GetAtPath(fbx); if (importer == null) { Debug.LogWarning("CryoSoftBuilder: no " + fbx); continue; }
                importer.globalScale = 1; importer.useFileScale = true; importer.isReadable = true; importer.addCollider = false; importer.importAnimation = false; importer.importCameras = false; importer.importLights = false; importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None; importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.SaveAndReimport();
                var aoImporter = (TextureImporter)AssetImporter.GetAtPath(aoPath);
                aoImporter.sRGBTexture = false; aoImporter.mipmapEnabled = true; aoImporter.maxTextureSize = 1024; aoImporter.textureCompression = TextureImporterCompression.Uncompressed; aoImporter.filterMode = FilterMode.Bilinear; aoImporter.wrapMode = TextureWrapMode.Clamp; aoImporter.SaveAndReimport();
                var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model); go.name = prefabPre + id;
                go.transform.localScale = new Vector3(-1, 1, 1);   // as the original room: undo the FBX handedness mirror
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterials = r.sharedMaterials.Select(m => mats[m.name.Split('.')[0]]).ToArray(); r.shadowCastingMode = r.name.Contains("SC_marking") || r.name.Contains("SC_floor") || r.name.Contains("Soft_Glass") ? ShadowCastingMode.Off : ShadowCastingMode.On; r.receiveShadows = true;
                }
                if (go.GetComponentsInChildren<Collider>().Length > 0) throw new Exception("Unexpected imported collision");
                var layout = Maze.ZoneData.Layouts["cryo-station/" + id];
                var data = go.AddComponent<CryoSoftRoomData>(); data.ao = ao; data.size = new Vector2(layout[0].Length * 2, layout.Length * 2);
                int tris = 0; foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) tris += mf.sharedMesh.triangles.Length / 3;
                PrefabUtility.SaveAsPrefabAsset(go, "Assets/Maze/Resources/CryoSoft/" + prefabPre + id + ".prefab"); Object.DestroyImmediate(go);
                Debug.Log($"CryoSoftBuilder: kit room {id}: {tris} tris, {data.size.x}x{data.size.y} m");
            }
            AssetDatabase.SaveAssets();
            if (prefabPre.Contains("Props")) ImportPropModels();
        }
        // the Meshy objects: texture import (2048, the canister 1024; normal maps as normal maps)
        static void ImportPropModels()
        {
            foreach (var path in Directory.GetFiles("Assets/Maze/CryoProps/Resources/CryoProps", "*.png"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path); if (ti == null) continue;
                bool nrm = path.EndsWith("_normal.png");
                ti.textureType = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = !nrm; ti.mipmapEnabled = true;
                ti.maxTextureSize = path.Contains("freeze-canister") ? 1024 : 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            Debug.Log("CryoSoftBuilder: prop textures imported");
        }

        public static void Import()
        {
            AssetDatabase.Refresh(); Directory.CreateDirectory(Root + "/Materials"); Directory.CreateDirectory("Assets/Maze/Resources/CryoSoft"); AssetDatabase.Refresh();
            string fbx = Root + "/Source/CryoSoftRoom.fbx"; var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            importer.globalScale = 1; importer.useFileScale = true; importer.isReadable = true; importer.addCollider = false; importer.importAnimation = false; importer.importCameras = false; importer.importLights = false; importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None; importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.SaveAndReimport();
            string aoPath = Root + "/Source/SoftFloorAO.png"; var aoImporter = (TextureImporter)AssetImporter.GetAtPath(aoPath);
            aoImporter.sRGBTexture = false; aoImporter.mipmapEnabled = true; aoImporter.maxTextureSize = 512; aoImporter.textureCompression = TextureImporterCompression.Uncompressed; aoImporter.filterMode = FilterMode.Bilinear; aoImporter.wrapMode = TextureWrapMode.Clamp; aoImporter.SaveAndReimport();
            var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath); var shader = Shader.Find("Maze/CryoSoftSurface"); if (shader == null) throw new Exception("Missing scoped Cryo environment shader");
            var mats = Names.Select((n, i) =>
            {
                string p = Root + "/Materials/Soft_" + n + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, p); } else m.shader = shader;
                ColorUtility.TryParseHtmlString(Hex[i], out var c); m.name = "Soft_" + n; var pigment = c;
                m.SetColor("_BaseColor", pigment); m.SetFloat("_Gloss", 1-Rough[i]);
                bool floor = n.StartsWith("Floor") || n == "Seam", glass = n == "Glass";
                m.SetFloat("_FloorAO", floor ? 1 : 0); m.SetTexture("_FloorAOMap", floor ? ao : null); m.SetFloat("_AOIntensity", floor ? .65f : .60f);
                m.SetFloat("_Glass", glass ? 1 : 0); m.SetFloat("_ShadowSpread", 2.8f); m.SetFloat("_ShadowOpacity", .52f);
                m.SetColor("_EmissionColor", pigment * (n == "Amber" ? .38f : n == "Cyan" ? .24f : glass ? .09f : 0));
                m.SetFloat("_SrcBlend", glass ? (float)BlendMode.SrcAlpha : (float)BlendMode.One); m.SetFloat("_DstBlend", glass ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero); m.SetFloat("_ZWrite", glass ? 0 : 1);
                m.renderQueue = glass ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry; m.SetOverrideTag("RenderType", glass ? "Transparent" : "Opaque");
                EditorUtility.SetDirty(m); return m;
            }).ToDictionary(m => m.name);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx); if (model == null) throw new Exception("Missing Blender room model");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model); go.name = "CryoSoftRoom";
            // FBX's handedness conversion mirrors this authored fixed world across X in Unity.
            // Flip the model root back once; nested mesh transforms and imported smooth normals remain intact.
            go.transform.localScale = new Vector3(-1, 1, 1);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => mats[m.name.Split('.')[0]]).ToArray(); r.shadowCastingMode = r.name.Contains("SC_marking") || r.name.Contains("SC_floor") || r.name.Contains("Soft_Glass") ? ShadowCastingMode.Off : ShadowCastingMode.On; r.receiveShadows = true;
            }
            if (go.GetComponentsInChildren<Collider>().Length > 0) throw new Exception("Unexpected imported collision");
            PrefabUtility.SaveAsPrefabAsset(go, "Assets/Maze/Resources/CryoSoft/CryoSoftRoom.prefab"); Object.DestroyImmediate(go); AssetDatabase.SaveAssets();
            Debug.Log("CryoSoftBuilder: imported original Blender geometry, 13 scoped matte/optical materials with baked contacts; original kit assets untouched.");
        }
    }
}
#endif
