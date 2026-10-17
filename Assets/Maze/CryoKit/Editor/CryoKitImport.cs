#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    /// <summary>
    /// Offline native-asset build for the approved twenty-object Cryo kit.
    /// Source staging: Assets/Maze/CryoKit/Source/*.gltf, *.bin, sharedtextures/*.png,
    /// manifest-final.json. Uses the project's installed glTFast ScriptedImporter;
    /// no runtime glTF loading, package installation or pipeline/global-settings writes.
    /// </summary>
    public static class CryoKitImport
    {
        public const string Source = "Assets/Maze/CryoKit/Source";
        public const string Materials = "Assets/Maze/CryoKit/Materials";
        public const string Meshes = "Assets/Maze/CryoKit/Meshes";
        public const string Prefabs = "Assets/Maze/Resources/CryoKit/Prefabs";
        public const string ShaderName = "Maze/CryoKit PBR";
        static string Project => Directory.GetParent(Application.dataPath).FullName;
        static string Full(string assetPath) => Path.GetFullPath(Path.Combine(Project, assetPath));

        [MenuItem("Maze/Cryo Kit/Build Native Art Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the Cryo art kit outside Play mode.");
            var sourceFiles = Directory.GetFiles(Full(Source), "*.gltf").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (sourceFiles.Length != 20) throw new InvalidDataException($"Expected 20 staged glTF files; found {sourceFiles.Length}.");
            string manifestPath = Full(Source + "/manifest-final.json");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Stage the authoritative manifest-final.json next to the glTF files.", manifestPath);
            var manifest = Json.ReadObject(File.ReadAllText(manifestPath));
            var records = List(manifest, "assets").Select(AsObject).ToDictionary(a => Text(a, "id") + "_" + Text(a, "slug"));
            var documents = sourceFiles.Select(p => new SourceDocument(ToAssetPath(p), Json.ReadObject(File.ReadAllText(p)))).ToArray();
            EnsureFolder(Materials); EnsureFolder(Meshes); EnsureFolder(Prefabs);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Configure each canonical PNG once by its actual semantic usage, before
            // glTF reimports. Raw RGB normals deliberately remain Default/linear: the
            // native shader reads XYZ directly and avoids platform-dependent swizzles.
            var textureRoles = GatherTextureRoles(documents);
            foreach (var entry in textureRoles.OrderBy(x => x.Key, StringComparer.Ordinal)) ConfigureTexture(entry.Key, entry.Value);
            foreach (var document in documents) ConfigureGltf(document.AssetPath);

            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("CryoKit.shader must be imported before calling Build.");
            RejectShaderErrors(shader);
            var report = new ImportReport { source = Source, shader = shader.name, generatedUtc = DateTime.UtcNow.ToString("O") };
            var catalog = new Catalog();
            foreach (var document in documents)
            {
                if (!records.TryGetValue(document.Key, out var record)) throw new InvalidDataException("No manifest record for " + document.Key);
                report.assets.Add(BuildAsset(document, record, shader));
                catalog.assets.Add(new CatalogEntry
                {
                    id = Text(record, "id"), slug = Text(record, "slug"), name = Text(record, "name"),
                    resource = "CryoKit/Prefabs/" + document.Key, dimensions = Floats(List(record, "dimensions")),
                    pivot = Text(GetObject(record, "pivot"), "convention"), triangles = (int)Number(record, "triangles", 0)
                });
            }
            report.textureCount = textureRoles.Count;
            report.totalTriangles = report.assets.Sum(a => a.triangles);
            report.estimatedAstc6x6MipBytes = textureRoles.Keys.Sum(EstimateAstcBytes);
            report.textureProfiles = textureRoles.OrderBy(x => x.Key).Select(x => TextureProfile(x.Key, x.Value)).ToList();
            string catalogPath = "Assets/Maze/Resources/CryoKit/Catalog.json";
            File.WriteAllText(Full(catalogPath), JsonUtility.ToJson(catalog, true));
            AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            Validate();
            foreach (var item in report.assets) item.savedAndReloaded = true;
            Directory.CreateDirectory(Path.Combine(Project, "Logs/CryoKit"));
            File.WriteAllText(Path.Combine(Project, "Logs/CryoKit/import-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log($"CryoKit: {report.assets.Count}/20 native prefabs saved and reloaded; {report.totalTriangles:N0} triangles; {report.textureCount} canonical shared PNGs. ASTC estimate incl. mips {report.estimatedAstc6x6MipBytes / 1048576.0:F2} MiB (not a device measurement). No colliders or gameplay were changed.");
        }

        [MenuItem("Maze/Cryo Kit/Validate Native Art Assets")]
        public static void Validate()
        {
            var manifest = Json.ReadObject(File.ReadAllText(Full(Source + "/manifest-final.json")));
            var records = List(manifest, "assets").Select(AsObject).ToArray();
            if (records.Length != 20) throw new InvalidDataException("Manifest does not describe exactly twenty assets.");
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Cryo native shader missing.");
            RejectShaderErrors(shader);
            foreach (var record in records)
            {
                string key = Text(record, "id") + "_" + Text(record, "slug");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + key + ".prefab");
                if (prefab == null) throw new InvalidDataException("Missing native prefab " + key);
                ValidateInstance(prefab, record);
                foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader != shader) throw new InvalidDataException(key + " has missing/non-native material.");
                        if (material.GetFloat("_UseAOMap") > 0.5f && material.GetFloat("_AOMap_UV") != 1)
                            throw new InvalidDataException(key + " lost authored AO UV1.");
                        if (material.GetFloat("_UseNormalMap") > 0.5f && material.GetTexture("_NormalMap") == null)
                            throw new InvalidDataException(key + " lost normal texture.");
                    }
            }
            Debug.Log("CryoKit native reload validation passed: 20 prefabs, manifest dimensions/triangles, both UV sets, source-derived normal/tangent frames, native materials, UV1 AO and no colliders.");
        }

        static AssetReport BuildAsset(SourceDocument document, Dictionary<string, object> record, Shader shader)
        {
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(document.AssetPath);
            if (imported == null) throw new InvalidDataException("glTFast did not produce a GameObject for " + document.AssetPath);
            var builtMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
            EnsureFolder(Materials + "/" + document.Key);
            EnsureFolder(Meshes + "/" + document.Key);
            foreach (var sourceMaterial in List(document.Root, "materials").Select(AsObject))
            {
                string name = Text(sourceMaterial, "name");
                if (string.IsNullOrEmpty(name)) throw new InvalidDataException(document.Key + " has an unnamed material.");
                var material = NewMaterial(shader, document, sourceMaterial);
                string path = Materials + "/" + document.Key + "/" + SafeName(name) + ".mat";
                builtMaterials.Add(name, SaveNative(material, path));
            }
            var root = new GameObject(document.Key);
            try
            {
                var visual = Object.Instantiate(imported, root.transform, false);
                visual.name = "Visual";
                // Preserve the imported root's own transform and every authored node,
                // including the two independently addressable service-door leaves.
                int meshIndex = 0;
                int importedBadTangents = 0;
                foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) throw new InvalidDataException(document.Key + " has an empty mesh filter.");
                    var clone = Object.Instantiate(filter.sharedMesh);
                    clone.name = document.Key + "_" + SafeName(filter.name);
                    importedBadTangents += CountBadTangents(clone);
                    RestoreSourceTangents(document, filter.name, clone);
                    filter.sharedMesh = SaveNative(clone, Meshes + "/" + document.Key + "/" + meshIndex.ToString("D2") + "_" + SafeName(filter.name) + ".asset");
                    meshIndex++;
                }
                foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materialArray = renderer.sharedMaterials;
                    for (int i = 0; i < materialArray.Length; i++)
                    {
                        if (materialArray[i] == null || !builtMaterials.TryGetValue(materialArray[i].name, out var replacement))
                            throw new InvalidDataException(document.Key + " could not map imported material " + (materialArray[i] == null ? "null" : materialArray[i].name));
                        materialArray[i] = replacement;
                    }
                    renderer.sharedMaterials = materialArray;
                    bool transparent = materialArray.Any(m => m.GetFloat("_IsGlass") > 0.5f);
                    renderer.shadowCastingMode = transparent ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                }
                ValidateInstance(root, record);
                string prefabPath = Prefabs + "/" + document.Key + ".prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success || saved == null) throw new IOException("Saving prefab failed: " + prefabPath);
                return new AssetReport
                {
                    key = document.Key, prefab = prefabPath, sourceGltf = document.AssetPath,
                    triangles = (int)Number(record, "triangles", 0), renderers = root.GetComponentsInChildren<MeshRenderer>(true).Length,
                    nativeMeshes = meshIndex, nativeMaterials = builtMaterials.Count,
                    correctedImportedNonorthogonalTangentVertices = importedBadTangents,
                    dimensions = Floats(List(record, "dimensions")), sourceFront = "+Z", unityFront = "+Z",
                    collision = "Visual assets only; no runtime colliders. Existing game routes remain authoritative."
                };
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Material NewMaterial(Shader shader, SourceDocument document, Dictionary<string, object> source)
        {
            var material = new Material(shader) { name = Text(source, "name"), enableInstancing = true };
            var pbr = GetObject(source, "pbrMetallicRoughness");
            var extensions = GetObject(source, "extensions");
            material.SetVector("_BaseColor", Vector(pbr, "baseColorFactor", Vector4.one));
            material.SetFloat("_Metallic", (float)Number(pbr, "metallicFactor", 1));
            material.SetFloat("_Roughness", (float)Number(pbr, "roughnessFactor", 1));
            SetMap(material, "_BaseMap", "_UseBaseMap", document, GetObject(pbr, "baseColorTexture"));
            SetMap(material, "_RMMap", "_UseRMMap", document, GetObject(pbr, "metallicRoughnessTexture"));
            var normal = GetObject(source, "normalTexture");
            SetMap(material, "_NormalMap", "_UseNormalMap", document, normal);
            material.SetFloat("_NormalScale", (float)Number(normal, "scale", 1));
            var ao = GetObject(source, "occlusionTexture");
            SetMap(material, "_AOMap", "_UseAOMap", document, ao);
            material.SetFloat("_AOStrength", (float)Number(ao, "strength", 1));
            Vector4 emission = Vector(source, "emissiveFactor", Vector4.zero);
            emission *= (float)Number(GetObject(extensions, "KHR_materials_emissive_strength"), "emissiveStrength", 1);
            material.SetVector("_EmissionColor", emission);
            SetMap(material, "_EmissionMap", "_UseEmissionMap", document, GetObject(source, "emissiveTexture"));
            var clearcoat = GetObject(extensions, "KHR_materials_clearcoat");
            float coat = (float)Number(clearcoat, "clearcoatFactor", 0);
            material.SetFloat("_Clearcoat", coat);
            material.SetFloat("_ClearcoatRoughness", (float)Number(clearcoat, "clearcoatRoughnessFactor", 0));
            if (coat > 0) material.EnableKeyword("_CLEARCOAT");
            var transmission = GetObject(extensions, "KHR_materials_transmission");
            float transmissionFactor = (float)Number(transmission, "transmissionFactor", 0);
            material.SetFloat("_Transmission", transmissionFactor);
            SetMap(material, "_TransmissionMap", "_UseTransmissionMap", document, GetObject(transmission, "transmissionTexture"));
            bool glass = transmissionFactor > 0;
            material.SetFloat("_IsGlass", glass ? 1 : 0);
            material.SetFloat("_Cull", Boolean(source, "doubleSided") ? (float)CullMode.Off : (float)CullMode.Back);
            material.SetFloat("_ZWrite", glass ? 0 : 1);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", glass ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            if (glass)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.SetShaderPassEnabled("DepthOnly", false);
            }
            else material.renderQueue = (int)RenderQueue.Geometry;
            return material;
        }

        static void SetMap(Material material, string property, string enabled, SourceDocument document, Dictionary<string, object> info)
        {
            material.SetFloat(enabled, info.Count > 0 ? 1 : 0);
            var transform = GetObject(GetObject(info, "extensions"), "KHR_texture_transform");
            float sx = Element(transform, "scale", 0, 1), sy = Element(transform, "scale", 1, 1);
            float ox = Element(transform, "offset", 0, 0), oy = Element(transform, "offset", 1, 0);
            float angle = (float)Number(transform, "rotation", 0);
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            // conjugate KHR transform by V'=1-V, matching glTFast's imported UVs
            var st = new Vector4(sx * c, sy * c, ox - sy * s, 1 - oy - sy * c);
            material.SetTextureScale(property, new Vector2(st.x, st.y));
            material.SetTextureOffset(property, new Vector2(st.z, st.w));
            material.SetVector(property + "_ST", st);
            material.SetVector(property + "_Rotation", new Vector4(sy * s, -sx * s, 0, 0));
            int uv = (int)Number(transform, "texCoord", Number(info, "texCoord", 0));
            if (uv < 0 || uv > 1) throw new InvalidDataException("Only authored UV0 and UV1 are supported.");
            material.SetFloat(property + "_UV", uv);
            if (info.Count == 0) return;
            string path = document.TexturePath((int)Number(info, "index", -1));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidDataException("Texture failed to import: " + path);
            material.SetTexture(property, texture);
        }

        static Dictionary<string, HashSet<string>> GatherTextureRoles(IEnumerable<SourceDocument> documents)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var document in documents)
            {
                foreach (var material in List(document.Root, "materials").Select(AsObject))
                {
                    var pbr = GetObject(material, "pbrMetallicRoughness");
                    AddRole(document, GetObject(pbr, "baseColorTexture"), "sRGB", result);
                    AddRole(document, GetObject(pbr, "metallicRoughnessTexture"), "linear", result);
                    AddRole(document, GetObject(material, "normalTexture"), "normalRGB", result);
                    AddRole(document, GetObject(material, "occlusionTexture"), "linear", result);
                    AddRole(document, GetObject(material, "emissiveTexture"), "sRGB", result);
                    AddRole(document, GetObject(GetObject(GetObject(material, "extensions"), "KHR_materials_transmission"), "transmissionTexture"), "linear", result);
                }
                var textures = List(document.Root, "textures");
                var samplers = List(document.Root, "samplers");
                for (int i = 0; i < textures.Count; i++)
                {
                    string path = document.TexturePath(i);
                    if (!result.TryGetValue(path, out var roles)) continue;
                    var texture = AsObject(textures[i]);
                    int samplerIndex = (int)Number(texture, "sampler", -1);
                    var sampler = samplerIndex >= 0 ? AsObject(samplers[samplerIndex]) : new Dictionary<string, object>();
                    int wrapS = (int)Number(sampler, "wrapS", 10497), wrapT = (int)Number(sampler, "wrapT", 10497);
                    if (wrapS != wrapT || (wrapS != 10497 && wrapS != 33071)) throw new InvalidDataException("Unexpected Cryo source sampler " + path);
                    roles.Add(wrapS == 33071 ? "wrap:clamp" : "wrap:repeat");
                    if (roles.Contains("wrap:clamp") && roles.Contains("wrap:repeat")) throw new InvalidDataException("A shared PNG requires conflicting samplers: " + path);
                }
            }
            return result;
        }
        static void AddRole(SourceDocument document, Dictionary<string, object> info, string role, Dictionary<string, HashSet<string>> result)
        {
            if (info.Count == 0) return;
            string path = document.TexturePath((int)Number(info, "index", -1));
            if (!result.TryGetValue(path, out var roles)) result[path] = roles = new HashSet<string>();
            roles.Add(role);
            if (roles.Contains("sRGB") && (roles.Contains("linear") || roles.Contains("normalRGB"))) throw new InvalidDataException("A texture mixes sRGB color and linear data: " + path);
        }
        static void ConfigureTexture(string path, HashSet<string> roles)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidDataException("Missing PNG TextureImporter " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = roles.Contains("sRGB");
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.maxTextureSize = 1024;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = roles.Contains("wrap:clamp") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.anisoLevel = 2;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            foreach (string platform in new[] { "iPhone", "Android" })
            {
                var profile = importer.GetPlatformTextureSettings(platform);
                profile.name = platform;
                profile.overridden = true;
                profile.maxTextureSize = 1024;
                profile.format = TextureImporterFormat.ASTC_6x6;
                profile.textureCompression = TextureImporterCompression.CompressedHQ;
                profile.crunchedCompression = false;
                importer.SetPlatformTextureSettings(profile);
            }
            importer.SaveAndReimport();
        }
        static void ConfigureGltf(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || !importer.GetType().FullName.Contains("GLTFast"))
                throw new InvalidOperationException("Expected installed glTFast importer for " + path);
            var serialized = new SerializedObject(importer);
            SetSerialized(serialized, "importSettings.nodeNameMethod", 0);
            SetSerialized(serialized, "importSettings.animationMethod", 0);
            SetSerialized(serialized, "importSettings.generateMipMaps", true);
            SetSerialized(serialized, "importSettings.texturesReadable", false);
            SetSerialized(serialized, "editorImportSettings.generateSecondaryUVSet", false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            var reportItems = new SerializedObject(importer).FindProperty("reportItems");
            if (reportItems != null && reportItems.isArray)
                for (int i = 0; i < reportItems.arraySize; i++)
                {
                    var type = reportItems.GetArrayElementAtIndex(i).FindPropertyRelative("type");
                    if (type != null && (type.intValue == (int)LogType.Error || type.intValue == (int)LogType.Exception))
                        throw new InvalidDataException("glTFast import errors for " + path + "; inspect its import report.");
                }
        }
        static void SetSerialized(SerializedObject target, string path, object value)
        {
            var property = target.FindProperty(path);
            if (property == null) throw new InvalidOperationException("Installed importer API lacks " + path);
            if (value is bool flag) property.boolValue = flag;
            else property.intValue = (int)value;
        }

        static void RestoreSourceTangents(SourceDocument document, string nodeName, Mesh mesh)
        {
            var node = List(document.Root, "nodes").Select(AsObject).SingleOrDefault(n => n.ContainsKey("mesh") && Text(n, "name") == nodeName);
            if (node == null) throw new InvalidDataException("Cannot identify source mesh node " + nodeName);
            var sourceMesh = AsObject(List(document.Root, "meshes")[(int)Number(node, "mesh", -1)]);
            var primitives = List(sourceMesh, "primitives");
            if (primitives.Count != 1) throw new InvalidDataException("Cryo mesh must have one authored material primitive: " + nodeName);
            var attributes = GetObject(AsObject(primitives[0]), "attributes");
            int tangentAccessor = (int)Number(attributes, "TANGENT", -1);
            var source = document.ReadFloat4Accessor(tangentAccessor);
            if (source.Length != mesh.vertexCount) throw new InvalidDataException("Source/native vertex order mismatch: " + nodeName);
            var normals = mesh.normals;
            for (int i = 0; i < source.Length; i++)
            {
                // Imported positions/normals mirror X. Preserve the source normal-map
                // basis through that reflection: reflected T, opposite handedness.
                // The V flip changes addressing; no source normal-map pixels are flipped.
                source[i] = new Vector4(-source[i].x, source[i].y, source[i].z, -source[i].w);
                var t = new Vector3(source[i].x, source[i].y, source[i].z);
                if (!Finite(t) || Mathf.Abs(Vector3.Dot(normals[i], t)) > 0.002f || Mathf.Abs(t.magnitude - 1) > 0.002f)
                    throw new InvalidDataException("Source-derived tangent frame invalid at " + nodeName + ":" + i);
            }
            mesh.tangents = source;
        }
        static int CountBadTangents(Mesh mesh)
        {
            var normals = mesh.normals; var tangents = mesh.tangents; int count = 0;
            if (tangents.Length != normals.Length) return normals.Length;
            for (int i = 0; i < tangents.Length; i++)
                if (Mathf.Abs(Vector3.Dot(normals[i], new Vector3(tangents[i].x, tangents[i].y, tangents[i].z))) > 0.002f) count++;
            return count;
        }
        static void ValidateInstance(GameObject root, Dictionary<string, object> record)
        {
            int triangles = 0;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) throw new InvalidDataException(root.name + " contains no meshes.");
            if (root.GetComponentsInChildren<Collider>(true).Length > 0) throw new InvalidDataException(root.name + " unexpectedly contains colliders.");
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) throw new InvalidDataException(root.name + " lost a mesh.");
                if (mesh.uv.Length != mesh.vertexCount || mesh.uv2.Length != mesh.vertexCount)
                    throw new InvalidDataException(root.name + " lost UV0 or authored AO UV1.");
                if (mesh.normals.Length != mesh.vertexCount || mesh.tangents.Length != mesh.vertexCount || CountBadTangents(mesh) != 0)
                    throw new InvalidDataException(root.name + " has invalid normal/tangent frames.");
                foreach (var p in mesh.vertices) if (!Finite(p)) throw new InvalidDataException(root.name + " has a nonfinite position.");
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) triangles += (int)mesh.GetIndexCount(submesh) / 3;
            }
            if (triangles != (int)Number(record, "triangles", 0)) throw new InvalidDataException(root.name + " triangle count changed.");
            var actual = LocalBounds(root);
            var sourceBounds = GetObject(record, "bounds");
            var minimum = Floats(List(sourceBounds, "min")); var maximum = Floats(List(sourceBounds, "max"));
            var expectedMin = new Vector3(-maximum[0], minimum[1], minimum[2]);
            var expectedMax = new Vector3(-minimum[0], maximum[1], maximum[2]);
            if (Vector3.Distance(actual.min, expectedMin) > 0.004f || Vector3.Distance(actual.max, expectedMax) > 0.004f)
                throw new InvalidDataException(root.name + " authored metric bounds/pivot changed: " + actual);
            if (Text(record, "id") == "06")
            {
                var names = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                if (!names.Contains("Door_Left_Leaf") || !names.Contains("Door_Right_Leaf")) throw new InvalidDataException("Service-door leaf hierarchy was lost.");
            }
        }
        static Bounds LocalBounds(GameObject root)
        {
            bool initialized = false; Bounds result = default;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Matrix4x4 matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var p in filter.sharedMesh.vertices)
                {
                    Vector3 q = matrix.MultiplyPoint3x4(p);
                    if (!initialized) { result = new Bounds(q, Vector3.zero); initialized = true; }
                    else result.Encapsulate(q);
                }
            }
            return result;
        }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        static void RejectShaderErrors(Shader shader)
        {
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error") throw new InvalidOperationException("CryoKit shader error: " + message.message);
        }
        static T SaveNative<T>(T transient, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(transient, path); return transient; }
            EditorUtility.CopySerialized(transient, existing);
            Object.DestroyImmediate(transient);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
        static string ToAssetPath(string fullPath) => fullPath.Substring(Project.Length + 1).Replace('\\', '/');
        static string SafeName(string value) => new string(value.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
        static long EstimateAstcBytes(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int w = texture.width, h = texture.height; long bytes = 0;
            do { bytes += (long)((w + 5) / 6) * ((h + 5) / 6) * 16; if (w == 1 && h == 1) break; w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); } while (true);
            return bytes;
        }
        static TextureReport TextureProfile(string path, HashSet<string> roles)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            return new TextureReport { asset = path, roles = roles.OrderBy(x => x).ToArray(), sRGB = importer.sRGBTexture, readable = importer.isReadable, maxSize = importer.maxTextureSize, wrap = importer.wrapMode.ToString(),
                ios = importer.GetPlatformTextureSettings("iPhone").format.ToString(), android = importer.GetPlatformTextureSettings("Android").format.ToString() };
        }

        sealed class SourceDocument
        {
            public readonly string AssetPath, Key;
            public readonly Dictionary<string, object> Root;
            readonly Dictionary<int, byte[]> buffers = new Dictionary<int, byte[]>();
            public SourceDocument(string path, Dictionary<string, object> data) { AssetPath = path; Key = Path.GetFileNameWithoutExtension(path); Root = data; }
            public string TexturePath(int index)
            {
                var texture = AsObject(List(Root, "textures")[index]);
                var image = AsObject(List(Root, "images")[(int)Number(texture, "source", -1)]);
                return RelativePath(Text(image, "uri"));
            }
            string RelativePath(string uri)
            {
                string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Full(AssetPath)), Uri.UnescapeDataString(uri)));
                if (!full.StartsWith(Full(Source) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
                    throw new InvalidDataException("Source dependency missing/outside the isolated Cryo staging directory: " + uri);
                return ToAssetPath(full);
            }
            public Vector4[] ReadFloat4Accessor(int index)
            {
                var accessor = AsObject(List(Root, "accessors")[index]);
                if (Text(accessor, "type") != "VEC4" || Number(accessor, "componentType", 0) != 5126 || accessor.ContainsKey("sparse"))
                    throw new InvalidDataException("Expected ordinary float4 source tangent accessor.");
                var view = AsObject(List(Root, "bufferViews")[(int)Number(accessor, "bufferView", -1)]);
                int buffer = (int)Number(view, "buffer", 0);
                if (!buffers.TryGetValue(buffer, out var bytes))
                {
                    string uri = Text(AsObject(List(Root, "buffers")[buffer]), "uri");
                    bytes = File.ReadAllBytes(Full(RelativePath(uri))); buffers.Add(buffer, bytes);
                }
                int offset = (int)(Number(view, "byteOffset", 0) + Number(accessor, "byteOffset", 0));
                int stride = (int)Number(view, "byteStride", 16), count = (int)Number(accessor, "count", 0);
                if (count < 1 || offset < 0 || offset + (long)(count - 1) * stride + 16 > bytes.Length) throw new InvalidDataException("Source tangent accessor exceeds buffer.");
                var result = new Vector4[count];
                for (int i = 0; i < count; i++)
                {
                    int p = offset + i * stride;
                    result[i] = new Vector4(BitConverter.ToSingle(bytes, p), BitConverter.ToSingle(bytes, p + 4), BitConverter.ToSingle(bytes, p + 8), BitConverter.ToSingle(bytes, p + 12));
                }
                return result;
            }
        }

        [Serializable] sealed class ImportReport
        {
            public string generatedUtc, source, shader;
            public int textureCount, totalTriangles;
            public long estimatedAstc6x6MipBytes;
            public bool engineRenderingVerified = false, phonePerformanceMeasured = false;
            public string glass = "Premultiplied URP PBR with frost-dependent opacity and grazing reflection; no volumetric refraction. Source thickness, IOR and attenuation retained in staged glTF for future parity work.";
            public string anisotropy = "Directional source normal detail is used; BRDF anisotropy is approximated by isotropic URP metallic reflection.";
            public string geometry = "Original source vertices, normals, UV0/UV1, indices and node pivots. Native source-derived tangents account for X handedness reflection; no collider/LOD generation. Meshes currently retain CPU copies for import verification.";
            public List<AssetReport> assets = new List<AssetReport>();
            public List<TextureReport> textureProfiles = new List<TextureReport>();
        }
        [Serializable] sealed class AssetReport { public string key, sourceGltf, prefab, sourceFront, unityFront, collision; public int triangles, renderers, nativeMeshes, nativeMaterials, correctedImportedNonorthogonalTangentVertices; public float[] dimensions; public bool savedAndReloaded; }
        [Serializable] sealed class TextureReport { public string asset, ios, android, wrap; public string[] roles; public bool sRGB, readable; public int maxSize; }
        [Serializable] sealed class Catalog { public string units = "metres", front = "+Z", up = "+Y"; public List<CatalogEntry> assets = new List<CatalogEntry>(); }
        [Serializable] sealed class CatalogEntry { public string id, slug, name, resource, pivot; public float[] dimensions; public int triangles; }

        // Tiny data-only JSON reader avoids adding a serializer/package dependency.
        // Explicit presence checks preserve glTF's defaults for omitted scalar fields.
        static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object> ?? new Dictionary<string, object>();
        static Dictionary<string, object> GetObject(Dictionary<string, object> value, string key) => value != null && value.TryGetValue(key, out var item) ? AsObject(item) : new Dictionary<string, object>();
        static List<object> List(Dictionary<string, object> value, string key) => value != null && value.TryGetValue(key, out var item) && item is List<object> list ? list : new List<object>();
        static string Text(Dictionary<string, object> value, string key) => value != null && value.TryGetValue(key, out var item) ? item as string ?? "" : "";
        static double Number(Dictionary<string, object> value, string key, double fallback) => value != null && value.TryGetValue(key, out var item) && item is double n ? n : fallback;
        static bool Boolean(Dictionary<string, object> value, string key) => value != null && value.TryGetValue(key, out var item) && item is bool b && b;
        static float[] Floats(List<object> values) => values.Select(v => Convert.ToSingle(v, CultureInfo.InvariantCulture)).ToArray();
        static float Element(Dictionary<string, object> value, string key, int index, float fallback) { var values = List(value, key); return values.Count > index ? Convert.ToSingle(values[index], CultureInfo.InvariantCulture) : fallback; }
        static Vector4 Vector(Dictionary<string, object> value, string key, Vector4 fallback)
        {
            var numbers = List(value, key);
            if (numbers.Count == 0) return fallback;
            for (int i = 0; i < Math.Min(4, numbers.Count); i++) fallback[i] = Convert.ToSingle(numbers[i], CultureInfo.InvariantCulture);
            return fallback;
        }
        sealed class Json
        {
            readonly string text; int index;
            Json(string value) { text = value; }
            public static Dictionary<string, object> ReadObject(string value)
            {
                var reader = new Json(value); var result = reader.Value() as Dictionary<string, object>;
                reader.White(); if (result == null || reader.index != value.Length) throw new InvalidDataException("Malformed glTF/manifest JSON.");
                return result;
            }
            void White() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
            object Value()
            {
                White(); if (index >= text.Length) throw new InvalidDataException("Unexpected JSON end.");
                char c = text[index];
                if (c == '{')
                {
                    index++; var result = new Dictionary<string, object>(StringComparer.Ordinal); White();
                    if (Take('}')) return result;
                    do { White(); string key = String(); White(); Require(':'); result.Add(key, Value()); White(); if (Take('}')) return result; Require(','); } while (true);
                }
                if (c == '[')
                {
                    index++; var result = new List<object>(); White(); if (Take(']')) return result;
                    do { result.Add(Value()); White(); if (Take(']')) return result; Require(','); } while (true);
                }
                if (c == '"') return String();
                if (c == 't') { Literal("true"); return true; }
                if (c == 'f') { Literal("false"); return false; }
                if (c == 'n') { Literal("null"); return null; }
                int start = index;
                while (index < text.Length && "-+0123456789.eE".IndexOf(text[index]) >= 0) index++;
                if (start == index || !double.TryParse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) throw new InvalidDataException("Invalid JSON value.");
                return number;
            }
            string String()
            {
                Require('"'); var result = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++]; if (c == '"') return result.ToString();
                    if (c != '\\') { result.Append(c); continue; }
                    if (index >= text.Length) break;
                    char e = text[index++];
                    switch (e)
                    {
                        case '"': case '\\': case '/': result.Append(e); break;
                        case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                        case 'u': if (index + 4 > text.Length) throw new InvalidDataException("Invalid Unicode escape."); result.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); index += 4; break;
                        default: throw new InvalidDataException("Invalid JSON escape.");
                    }
                }
                throw new InvalidDataException("Unterminated JSON string.");
            }
            bool Take(char c) { if (index < text.Length && text[index] == c) { index++; return true; } return false; }
            void Require(char c) { if (!Take(c)) throw new InvalidDataException("Expected JSON '" + c + "'."); }
            void Literal(string value) { if (index + value.Length > text.Length || text.Substring(index, value.Length) != value) throw new InvalidDataException("Invalid JSON literal."); index += value.Length; }
        }
    }
}
#endif
