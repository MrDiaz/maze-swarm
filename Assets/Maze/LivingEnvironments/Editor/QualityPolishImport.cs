#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    public static class QualityPolishImport
    {
        public static void FinalProof()
        {
            CryoSoftEnvironment.Enabled = true;
            LivingEnvironmentBuilder.ImportAndProof();
        }

        public static void MapRoomsAndProof()
        {
            CryoSoftBuilder.ImportPropRooms();
            LivingEnvironmentBuilder.ImportAndProof();
        }

        public static void RoomsAndProof()
        {
            RepairRoomData();
            LivingEnvironmentBuilder.ImportAndProof();
        }

        public static void PropsRoomsAndProof()
        {
            ImportBarrel();
            LivingEnvironmentBuilder.ImportAndProof();
        }

        public static void PropsAndProof()
        {
            ImportBarrel();
            LivingEnvironmentBuilder.Review();
        }

        static void ImportBarrel()
        {
            const string root = "Assets/Maze/LivingEnvironments";
            string path = root + "/Source/CryoExplosiveBarrel.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Authored barrel FBX missing");
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importAnimation = importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            var shader = Shader.Find("Maze/PolishPaint");
            if (shader == null) throw new InvalidOperationException("Painted prop shader missing");
            string materialPath = root + "/Materials/CryoExplosiveBarrel.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = UnityEngine.Object.Instantiate(model);
            try
            {
                go.name = "CryoExplosiveBarrel";
                go.transform.localScale = new Vector3(-1, 1, 1);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = material;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                if (go.GetComponentsInChildren<Collider>().Length != 0)
                    throw new InvalidOperationException("Barrel visual must add no collision");
                PrefabUtility.SaveAsPrefabAsset(go, root + "/Resources/LivingEnvironments/CryoExplosiveBarrel.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            AssetDatabase.SaveAssets();
        }

        static void RepairRoomData()
        {
            // Rebuild only the tiny serialized data component. All geometry, materials, transforms,
            // prop anchors and imported original assets stay intact.
            string folder = "Assets/Maze/Resources/CryoSoft";
            foreach (string path in Directory.GetFiles(folder, "*.prefab"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string id = name.StartsWith("CryoSoftRoomProps_") ? name.Substring(18) :
                    name.StartsWith("CryoSoftRoom_") ? name.Substring(13) : null;
                if (id == null) continue;
                string aoName = (name.StartsWith("CryoSoftRoomProps_") ? "SoftFloorAOProps_" : "SoftFloorAO_") + id;
                string aoPath = "Assets/Maze/CryoSoft/Source/" + aoName + ".png";
                Texture2D ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
                if (ao == null) throw new InvalidOperationException("Room contact texture missing: " + aoPath);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    var data = contents.GetComponent<CryoSoftRoomData>();
                    if (data == null) data = contents.AddComponent<CryoSoftRoomData>();
                    string layoutName = "cryo-station/" + id;
                    var layout = ZoneData.Layouts[layoutName];
                    data.ao = ao;
                    data.size = new Vector2(layout[0].Length * 2, layout.Length * 2);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    Debug.Log("Restored serializable room contact data: " + name + " " + data.size);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
