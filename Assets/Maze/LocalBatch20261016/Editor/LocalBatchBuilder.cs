using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Maze.FloorEditing.Editor
{
    public static class LocalBatchBuilder
    {
        const string Root = "Assets/Maze/LocalBatch20261016";
        const string PreviewRoot = "Assets/Maze/FloorEditor/Editor/previews/";
        [Serializable] public sealed class Plan { public int version; public string assetRoot; public Asset[] assets; }
        [Serializable] public sealed class Asset
        {
            public string id, slug, label, category, selectedVersion;
            public bool unrigged;
            public int mobileFirstLevel, textureLimit;
            public Lod[] lods;
            public float[] oldProxyOverflow;
        }
        [Serializable] public sealed class Lod
        {
            public int level, triangles;
            public string modelPath, baseTexture, normalTexture, maskTexture, mrTexture, sourceSha256;
            public float[] sourceFirstUV;
        }
        [Serializable] public sealed class Receipt
        {
            public string utc, unityVersion;
            public int assets, lods, materials, verifiedEmbeddedSlots;
            public List<string> checks = new(), errors = new();
            public List<AssetReceipt> imported = new();
        }
        [Serializable] public sealed class AssetReceipt
        {
            public string id, slug, version, prefab;
            public bool unrigged;
            public int mobileFirstLevel;
            public Bounds visualBounds, correctedPlacementBounds;
            public Vector3 pivotCorrection;
            public int[] allTriangles, activeTriangles;
            public float[] sourceUV, unityUV;
        }
        static Receipt receipt;
        static string Output => Environment.GetEnvironmentVariable("MAZE_BATCH_OUTPUT") ?? "Library/MazeFloorEditor/LocalBatch";
        static void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); receipt.checks.Add(text); }

        public static void Build()
        {
            Directory.CreateDirectory(Output);
            receipt = new Receipt { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            try
            {
                var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Root + "/Editor/import-plan.json"));
                Check(plan.version == 1 && plan.assets.Length == 20, "Exact twenty-asset import plan");
                var catalog = new List<FloorModelDefinition>();
                foreach (var asset in plan.assets)
                {
                    ImportAsset(asset, catalog);
                    receipt.assets++;
                    File.WriteAllText(Path.Combine(Output, "import-receipt.json"), JsonUtility.ToJson(receipt, true));
                }
                Check(receipt.lods == 51 && receipt.verifiedEmbeddedSlots == 153, "All 51 selected LODs and 153 source texture slots covered");
                string catalogPath = Root + "/Resources/FloorModels/catalog.json";
                File.WriteAllText(catalogPath, JsonUtility.ToJson(new FloorModelCatalog.Catalog { entries = catalog.ToArray() }, true));
                AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceSynchronousImport);
                FloorModelCatalog.Reload();
                Check(FloorModelCatalog.Entries.Length == 20, "Runtime catalog resolves twenty stable IDs");
                AssetDatabase.SaveAssets();
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); Debug.LogException(error); }
            finally
            {
                File.WriteAllText(Path.Combine(Output, "import-receipt.json"), JsonUtility.ToJson(receipt, true));
                EditorApplication.Exit(receipt.errors.Count == 0 ? 0 : 1);
            }
        }

        static void ImportAsset(Asset asset, List<FloorModelDefinition> catalog)
        {
            var meshes = new List<Mesh>(); var materials = new List<Material>(); var allBounds = new Bounds(); bool first = true;
            Vector2 firstUV = Vector2.zero;
            foreach (var lod in asset.lods)
            {
                ConfigureTexture(lod.baseTexture, false, true, asset.textureLimit, true);
                ConfigureTexture(lod.normalTexture, true, false, asset.textureLimit, true);
                ConfigureTexture(lod.maskTexture, false, false, asset.textureLimit, true);
                // Source glTF channel data stays linear; it is archived, not referenced by runtime materials.
                ConfigureTexture(lod.mrTexture, false, false, asset.textureLimit, false);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(lod.modelPath);
                Check(source != null, asset.slug + " LOD" + lod.level + " glTF imported");
                var filters = source.GetComponentsInChildren<MeshFilter>(true);
                Check(filters.Length == 1, asset.slug + " LOD" + lod.level + " has one visual mesh");
                var filter = filters[0]; var mesh = Object.Instantiate(filter.sharedMesh);
                mesh.name = asset.slug + "_LOD" + lod.level;
                Matrix4x4 matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var vertices = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                for (int i = 0; i < normals.Length; i++) normals[i] = matrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
                for (int i = 0; i < tangents.Length; i++) { var t = matrix.MultiplyVector(tangents[i]); tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w * Mathf.Sign(matrix.determinant)); }
                mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = tangents;
                if (matrix.determinant < 0) { var indices = mesh.triangles; for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]); mesh.triangles = indices; }
                mesh.RecalculateBounds();
                Check(mesh.triangles.Length / 3 == lod.triangles && mesh.uv.Length == mesh.vertexCount && mesh.tangents.Length == mesh.vertexCount, asset.slug + " LOD" + lod.level + " triangles/UV/tangents match selected source");
                if (first) { allBounds = mesh.bounds; firstUV = mesh.uv[0]; first = false; } else allBounds.Encapsulate(mesh.bounds);
                string meshPath = Root + "/Generated/Meshes/" + mesh.name + ".asset";
                Directory.CreateDirectory(Path.GetDirectoryName(meshPath));
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath); meshes.Add(mesh);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = mesh.name, enableInstancing = true };
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(lod.baseTexture));
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(lod.normalTexture)); material.SetFloat("_BumpScale", 1);
                material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(lod.maskTexture));
                material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1); material.SetFloat("_SmoothnessTextureChannel", 0);
                material.SetFloat("_Cull", 2); material.SetFloat("_Surface", 0); material.SetOverrideTag("RenderType", "Opaque");
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                string matPath = Root + "/Generated/Materials/" + mesh.name + ".mat";
                Directory.CreateDirectory(Path.GetDirectoryName(matPath));
                if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.CreateAsset(material, matPath); materials.Add(material);
                receipt.lods++; receipt.materials++; receipt.verifiedEmbeddedSlots += 3;
            }
            // One common pivot correction for every LOD; no LOD-specific sinking or pivot jump.
            Vector3 correction = new Vector3(-allBounds.center.x, -allBounds.min.y + .005f, -allBounds.center.z);
            var placement = new Bounds(new Vector3(0, (allBounds.size.y + .01f) / 2, 0), allBounds.size + Vector3.one * .01f);
            var root = new GameObject(asset.label + (asset.unrigged ? " [UNRIGGED STATIC PREVIEW]" : ""));
            var lods = new List<LOD>();
            try
            {
                var active = asset.lods.Where(l => l.level >= asset.mobileFirstLevel).ToArray();
                Check(active.Length > 0, asset.slug + " has a mobile default");
                for (int i = 0; i < asset.lods.Length; i++)
                {
                    if (asset.lods[i].level < asset.mobileFirstLevel) continue;
                    var child = new GameObject("Source LOD" + asset.lods[i].level); child.transform.SetParent(root.transform, false); child.transform.localPosition = correction;
                    child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = materials[i]; renderer.shadowCastingMode = ShadowCastingMode.On;
                    float threshold = active.Length == 1 ? .008f : lods.Count == active.Length - 1 ? .008f : active.Length == 2 ? .18f : lods.Count == 0 ? .28f : .12f;
                    lods.Add(new LOD(threshold, new Renderer[] { renderer }));
                }
                var group = root.AddComponent<LODGroup>(); group.SetLODs(lods.ToArray()); group.fadeMode = LODFadeMode.None; group.RecalculateBounds();
                var bounds = root.AddComponent<BoxCollider>(); bounds.center = placement.center; bounds.size = placement.size; bounds.enabled = false;
                foreach (var mesh in meshes) foreach (var vertex in mesh.vertices) CheckInside(placement, vertex + correction, asset.slug);
                string prefabPath = Root + "/Resources/FloorModels/" + asset.slug + ".prefab"; Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Check(prefab != null && prefab.GetComponentsInChildren<Animator>(true).Length == 0 && prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, asset.slug + " static prefab has no animation/gameplay scripts");
                RenderThumbnail(root, PreviewRoot + asset.id + ".png");
                catalog.Add(new FloorModelDefinition { id = asset.id, slug = asset.slug, label = asset.label, category = asset.category, resourcePath = "FloorModels/" + asset.slug, selectedVersion = asset.selectedVersion, unrigged = asset.unrigged, sizeX = placement.size.x, sizeY = placement.size.y, sizeZ = placement.size.z, centerY = placement.center.y, mobileFirstLevel = asset.mobileFirstLevel, sourceLevels = asset.lods.Select(l => l.level).ToArray(), triangles = asset.lods.Select(l => l.triangles).ToArray() });
                receipt.imported.Add(new AssetReceipt { id = asset.id, slug = asset.slug, version = asset.selectedVersion, prefab = prefabPath, unrigged = asset.unrigged, mobileFirstLevel = asset.mobileFirstLevel, visualBounds = allBounds, correctedPlacementBounds = placement, pivotCorrection = correction, allTriangles = asset.lods.Select(l => l.triangles).ToArray(), activeTriangles = active.Select(l => l.triangles).ToArray(), sourceUV = asset.lods[0].sourceFirstUV, unityUV = new[] { firstUV.x, firstUV.y } });
                foreach (var mesh in meshes)
                {
                    var serialized = new SerializedObject(mesh); var readable = serialized.FindProperty("m_IsReadable");
                    if (readable != null) { readable.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                }
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(root); }
        }
        static void CheckInside(Bounds bounds, Vector3 point, string slug)
        { if (!bounds.Contains(point)) throw new InvalidOperationException(slug + " visual vertex escapes corrected placement bounds"); }

        static void ConfigureTexture(string path, bool normal, bool srgb, int limit, bool mobile)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); importer = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (importer == null) throw new InvalidOperationException("Missing texture importer " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb; importer.mipmapEnabled = true; importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 2;
            importer.maxTextureSize = limit; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (normal) importer.flipGreenChannel = false;
            if (mobile) foreach (string platform in new[] { "iPhone", "Android" }) importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = platform, overridden = true, maxTextureSize = limit, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 75 });
            importer.SaveAndReimport();
            Check(importer.sRGBTexture == srgb && importer.mipmapEnabled && !importer.isReadable, path + " color space/mips/readability verified");
        }
        static void RenderThumbnail(GameObject source, string path)
        {
            var preview = new PreviewRenderUtility(); RenderTexture temporary = null; Texture2D pixels = null; var old = RenderTexture.active;
            try
            {
                var clone = Object.Instantiate(source); preview.AddSingleGO(clone); clone.GetComponent<LODGroup>().ForceLOD(0);
                var rs = clone.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = Color.clear; preview.camera.fieldOfView = 32;
                var q = Quaternion.Euler(22, -28, 0); float distance = b.extents.magnitude * 1.15f / Mathf.Sin(16 * Mathf.Deg2Rad);
                preview.camera.transform.SetPositionAndRotation(b.center + q * Vector3.back * distance, q); preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
                preview.ambientColor = new Color(.45f, .48f, .54f); preview.lights[0].intensity = 1.4f; preview.lights[0].transform.rotation = Quaternion.Euler(40, -40, 0); preview.lights[1].intensity = .7f;
                preview.BeginPreview(new Rect(0, 0, 320, 320), GUIStyle.none); preview.Render(true, true); var rendered = preview.EndPreview();
                temporary = RenderTexture.GetTemporary(320, 320, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rendered, temporary); RenderTexture.active = temporary;
                pixels = new Texture2D(320, 320, TextureFormat.RGBA32, false); pixels.ReadPixels(new Rect(0, 0, 320, 320), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG()); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            finally { RenderTexture.active = old; if (pixels) Object.DestroyImmediate(pixels); if (temporary) RenderTexture.ReleaseTemporary(temporary); preview.Cleanup(); }
        }
    }
}
