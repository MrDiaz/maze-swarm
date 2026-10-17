#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Maze
{
    // STAGED: no static initializer, scene mutation, play/build/install or broad Setup Project call.
    // Explicit import methods change only the named new model/art allowlist; retained hero/gun are read-only.
    public static class CohesiveAssetImport
    {
        const string ModelRoot = "Assets/Maze/CohesiveZone/Resources/CohesiveZone/Models/";
        const string ArtRoot = "Assets/Maze/CohesiveZone/Resources/CohesiveZone/Art/";
        const string Spitter = "Assets/Maze/Resources/Models/cohesive_lavender_spitter.glb";
        const string Hero = "Assets/Maze/Resources/Models/hero_vanguard_chibi.glb";
        const string Gun = "Assets/Maze/Resources/Models/minigun_chibi.glb";
        const string Engineer = "Assets/Maze/Resources/Models/hero_engineer_chibi.glb";
        const string HeavyPortrait = "Assets/Maze/Resources/UI/Portraits/heavy-chibi.png";
        const string EngineerPortrait = "Assets/Maze/Resources/UI/Portraits/engineer.png";
        const string EngineerExpectedSha = "23715596db9e3530b440c21267af23829b538cf16d3ab82eaebebe522dc76034";
        const int EngineerExpectedJoints = 26;
        static readonly string[] Cargo = { "crate", "battery", "tank", "barrier", "conveyor" };
        static readonly string[] Art = { "hangar-purple", "cargo-preview" };
        static readonly string[] Family = { "grunt", "runner", "tick", "carrier", "carapace" };
        static readonly string[] FamilyBaseJoints = { "Root", "Hips", "Spine", "Head", "UpperArm.L", "Forearm.L", "UpperArm.R", "Forearm.R", "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R" };
        static readonly string[] FamilyExpectedSha = { "b4c335e6fde7614e13b12d70d5d55f68813a17e17829677927fa17fd259895ae", "d60b7d1523fbe65167440efc5b22369fd80330aa07119c8e0b4b9fbde024f717", "61ec8474954bb0ef1390cd2eed65a8f90687ff08346b4c5fb010cb2ae5ff03f1", "2b22e2583d0a1ca7f71098658d8968117d860d80d4bfb0be269e2e57d8da5480", "5074f8ae09d54f31a9a7159d24843c079256c7ac462e17c8a70ba8c1cc301ffb" };
        const string SelectorArt = "cargo-arena";
        static string FamilyPath(string role) => "Assets/Maze/Resources/Models/cargo-family-" + role + ".glb";
        static bool IsFamily(string role) => role != null && role.StartsWith("family-", StringComparison.Ordinal) && Family.Contains(role.Substring(7));
        static bool HasPods(string role) => role == "family-carrier" || role == "family-carapace";
        static string[] FamilyJoints(string role) => HasPods(role) ? FamilyBaseJoints.Concat(new[] { "CarrierBelly", "CarrierPod.L", "CarrierPod.R", "CarrierPod.Back" }).ToArray() : FamilyBaseJoints;
        static (string name, float seconds)[] FamilyClips(string role)
        {
            var common = new[] { ("Idle", 2f), ("Walk", role == "family-runner" ? 2f / 3f : HasPods(role) ? 1f : .8f), ("MeleeWindup", .15f), ("Melee", .25f) };
            if (role == "family-runner") return common.Concat(new[] { ("Crouch", .6f), ("Leap", 28f / 60f), ("Land", .8f), ("Sidestep", .35f) }).ToArray();
            return HasPods(role) ? common.Concat(new[] { ("Bloat", .7f), ("Burst", .35f) }).ToArray() : common;
        }


        [Serializable] public sealed class Receipt
        {
            public string utc, mode, unityVersion, projectPath;
            public bool success, sceneChanged, playStarted, buildStarted, installStarted;
            public bool selectorCaptureRequired, selectorCapturePresent;
            public List<PolicyRecord> familyPolicy = new List<PolicyRecord>();
            public int checks;
            public List<string> errors = new List<string>(), warnings = new List<string>(), importerChanges = new List<string>();
            public List<ModelRecord> models = new List<ModelRecord>();
            public List<ArtRecord> art = new List<ArtRecord>();
            public string normalConvention = "glTF +Y preserved; importer UV/tangent conversion; no green-channel flip/external map extraction";
        }
        [Serializable] public sealed class ModelRecord
        {
            public string path, resource, sha256, importer;
            public string role, safeBelly, sourceAlbedoSha256, expectedSha256;
            public string[] requiredRoleClips, sourceJointNames;
            public bool sourceMaterialsOpaque;
            public int rendererCount, skinnedRendererCount, triangles, vertices, sourceJoints, maximumSourceInfluences;
            public float maxSourceWeightError;
            public Vector3 nativeBoundsCenter, nativeBoundsSize;
            public bool legacyImporter;
            public List<ClipRecord> clips = new List<ClipRecord>();
            public List<MaterialRecord> materials = new List<MaterialRecord>();
        }
        [Serializable] public sealed class ClipRecord
        {
            public string name; public float length; public bool legacy; public int bindings, movingResolvedBindings;
        }
        [Serializable] public sealed class MaterialRecord
        {
            public string name, shader, urpShader;
            public string albedo, normal, roughnessMetal;
            public string albedoFormat, normalFormat;
            public bool albedoSrgb, normalLinear, urpKeepsAlbedo, urpKeepsNormal, retainedHeroAlbedoOverride;
            public string resolvedAlbedoPath;
            public int width, height, mips;
            public float sourceNormalScale, urpNormalScale;
            public bool opaque;
        }
        [Serializable] public sealed class PolicyRecord { public string type, ai, expectedResource, resolvedPath; public bool exactResource; }
        [Serializable] public sealed class ArtRecord
        {
            public string path, resource, sha256, format;
            public int sourceWidth, sourceHeight, importedWidth, importedHeight;
            public bool srgb, npotPreserved, readable, iosOverride;
            public bool capturedSelectorSlot;
        }

        // Import this delivered character independently while the remaining prop pipeline continues.
        // This receipt is explicitly partial and cannot satisfy the full iOS build gate.
        public static void ImportCharacterForReview()
        {
            string output = Environment.GetEnvironmentVariable("COHESIVE_OUT");
            if (string.IsNullOrWhiteSpace(output)) throw new BuildFailedException("Set COHESIVE_OUT for the partial character review receipt.");
            Directory.CreateDirectory(output);
            var receipt = new Receipt { utc = DateTime.UtcNow.ToString("O"), mode = "partial-character-review-only", unityVersion = Application.unityVersion, projectPath = Directory.GetParent(Application.dataPath).FullName };
            try
            {
                AssetDatabase.ImportAsset(Spitter, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                SetLegacy(receipt); ConfigureEmbeddedTextures(Spitter, receipt);
                ValidateModel(Spitter, "Models/cohesive_lavender_spitter", "spitter", receipt);
                ValidateModel(Hero, "Models/hero_vanguard_chibi", "hero", receipt);
                ValidateModel(Gun, "Models/minigun_chibi", "gun", receipt);
                AssetDatabase.SaveAssets();
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            receipt.success = receipt.errors.Count == 0;
            File.WriteAllText(Path.Combine(output, "partial-character-validation.json"), JsonUtility.ToJson(receipt, true));
            if (!receipt.success) throw new BuildFailedException("Partial character validation failed: " + string.Join(" | ", receipt.errors));
        }
        // Explicit partial critique import. Cannot satisfy full delivery/build validation.
        public static void ImportEnemyFamilyForReview()
        {
            string output = Environment.GetEnvironmentVariable("COHESIVE_OUT");
            if (string.IsNullOrWhiteSpace(output)) throw new BuildFailedException("Set COHESIVE_OUT for partial family validation.");
            Directory.CreateDirectory(output);
            var receipt = new Receipt { utc = DateTime.UtcNow.ToString("O"), mode = "partial-enemy-family-review-only", unityVersion = Application.unityVersion, projectPath = Directory.GetParent(Application.dataPath).FullName };
            try
            {
                foreach (var role in Family) Check(receipt, File.Exists(Absolute(FamilyPath(role))), "Required family resource missing: " + FamilyPath(role));
                if (receipt.errors.Count == 0)
                {
                    foreach (var role in Family)
                    {
                        string path = FamilyPath(role);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        SetLegacy(receipt, path); ConfigureEmbeddedTextures(path, receipt);
                        ValidateModel(path, "Models/cargo-family-" + role, "family-" + role, receipt);
                    }
                    if (File.Exists(Absolute(ArtRoot + SelectorArt + ".png")))
                    { ConfigureArt(ArtRoot + SelectorArt + ".png", receipt); ValidateArt(ArtRoot + SelectorArt + ".png", "CohesiveZone/Art/" + SelectorArt, receipt); }
                    ValidateFamilyPaletteDistinct(receipt);
                }
                AssetDatabase.SaveAssets();
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            receipt.success = receipt.errors.Count == 0;
            File.WriteAllText(Path.Combine(output, "partial-enemy-family-validation.json"), JsonUtility.ToJson(receipt, true));
            if (!receipt.success) throw new BuildFailedException("Partial enemy family validation failed: " + string.Join(" | ", receipt.errors));
        }

        static void ValidateFamilyRig(GameObject prefab, string role, ModelRecord rec, Receipt receipt)
        {
            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            Check(receipt, !transforms.Any(t => t.name == "Belly"), "Unsafe legacy Belly would receive Swarm's old overwrite: " + rec.path);
            Check(receipt, !transforms.Any(t => t.name == "Muzzle"), "Family closed-mouth role unexpectedly retains spitter Muzzle: " + rec.path);
            var expected = FamilyJoints(role);
            foreach (var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var names = skin.bones.Where(b => b != null).Select(b => b.name).ToArray();
                Check(receipt, names.Length == expected.Length && names.Distinct().Count() == expected.Length && names.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)), "Imported family joint set differs from authored contract: " + rec.path);
            }
            rec.safeBelly = HasPods(role) ? "CarrierBelly" : "none";
            if (HasPods(role))
                foreach (string name in new[] { "CarrierBelly", "CarrierPod.L", "CarrierPod.R", "CarrierPod.Back" })
                {
                    var found = transforms.Where(t => t.name == name).ToArray();
                    Check(receipt, found.Length == 1 && found[0].parent != null && found[0].parent.name == "Hips", "Safe family belly/pod must be a unique direct child of Hips: " + rec.path + "/" + name);
                }
            else Check(receipt, !transforms.Any(t => t.name == "CarrierBelly" || t.name.StartsWith("CarrierPod.", StringComparison.Ordinal)), "Non-carrier family role has unexpected swelling/pod bones: " + rec.path);
            Check(receipt, rec.nativeBoundsSize.x >= .6f && rec.nativeBoundsSize.x <= 1.5f && rec.nativeBoundsSize.y >= .55f && rec.nativeBoundsSize.y <= 1.4f && rec.nativeBoundsSize.z >= .3f && rec.nativeBoundsSize.z <= 1.2f, "Family native bounds are implausible for approved source scale: " + rec.path);
        }

        static void ValidateFamilyClipBindings(Animation player, AnimationClip clip, string role, Receipt receipt, string path)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var transform = string.IsNullOrEmpty(binding.path) ? player.transform : player.transform.Find(binding.path);
                Check(receipt, transform != null, "Family clip has an unresolved binding: " + path + "/" + clip.name + "/" + binding.path);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (transform == null || curve == null || curve.length == 0) continue;
                float min = curve.keys.Min(k => k.value), max = curve.keys.Max(k => k.value);
                if (binding.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal) && max - min > .0001f)
                    Check(receipt, HasPods(role) && transform.name == "CarrierBelly" && min >= .99f && max <= 1.095f, "Family animation scales a legacy body/root/limb or exceeds safe belly breathing/swelling: " + path + "/" + clip.name + "/" + binding.path);
                if (transform.name == "Root" && binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal))
                    Check(receipt, max - min < .0001f, "Family source has root translation instead of in-place authored motion: " + path + "/" + clip.name);
            }
        }

        static void ValidateFamilySource(GlbDoc doc, byte[] bytes, int bin, string role, ModelRecord rec, Receipt receipt)
        {
            var expected = FamilyJoints(role);
            rec.sourceJointNames = doc.skins[0].joints.Select(i => doc.nodes[i].name).ToArray();
            Check(receipt, rec.sourceJointNames.Distinct().Count() == expected.Length && rec.sourceJointNames.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)), "Source family joint names differ from exact authored role: " + rec.path);
            Check(receipt, !doc.nodes.Any(n => n.name == "Belly" || n.name == "Muzzle"), "Unsafe Belly or old spit Muzzle in closed-mouth family source: " + rec.path);
            var required = FamilyClips(role).Select(x => x.name).OrderBy(x => x).ToArray();
            var actual = (doc.animations ?? Array.Empty<GlbAnimation>()).Select(a => a.name).OrderBy(x => x).ToArray();
            Check(receipt, required.SequenceEqual(actual), "Source family role clips differ from authored contract: " + rec.path);
            rec.sourceMaterialsOpaque = doc.materials != null && doc.materials.Length > 0 && doc.materials.All(m =>
                (string.IsNullOrEmpty(m.alphaMode) || m.alphaMode == "OPAQUE") && (m.pbrMetallicRoughness.baseColorFactor == null || m.pbrMetallicRoughness.baseColorFactor.Length == 0 || m.pbrMetallicRoughness.baseColorFactor.Length == 4 && m.pbrMetallicRoughness.baseColorFactor[3] >= .999f));
            Check(receipt, rec.sourceMaterialsOpaque && doc.materials.Length == 1, "Family source must retain its single opaque authored material: " + rec.path);
            foreach (var material in doc.materials ?? Array.Empty<GlbMaterial>())
            {
                int texture = material.pbrMetallicRoughness.baseColorTexture != null ? material.pbrMetallicRoughness.baseColorTexture.index : -1;
                Check(receipt, texture >= 0 && doc.textures != null && texture < doc.textures.Length, "Family source lost authored albedo texture: " + rec.path);
                if (texture < 0 || doc.textures == null || texture >= doc.textures.Length) continue;
                int image = doc.textures[texture].source;
                Check(receipt, doc.images != null && image >= 0 && image < doc.images.Length, "Family source albedo image missing: " + rec.path);
                if (doc.images == null || image < 0 || image >= doc.images.Length) continue;
                var view = doc.bufferViews[doc.images[image].bufferView];
                var imageBytes = new byte[view.byteLength]; Array.Copy(bytes, bin + view.byteOffset, imageBytes, 0, imageBytes.Length);
                using (var sha = SHA256.Create()) rec.sourceAlbedoSha256 = BitConverter.ToString(sha.ComputeHash(imageBytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        static void ValidateFamilyPolicy(Receipt receipt)
        {
            var policy = typeof(CohesiveCharacterPolicy);
            var roleMethod = policy.GetMethod("RoleFor", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(EnemyType) }, null);
            var resourceMethod = policy.GetMethod("ResourceFor", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(EnemyType) }, null);
            var prefabMethod = policy.GetMethod("AuthoredEnemyPrefab", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(EnemyType) }, null);
            var identityMethod = policy.GetMethod("IsAuthoredFamilyPrefab", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
            var routeMethod = policy.GetMethod("EnemyPrefab", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(EnemyType), typeof(string) }, null);
            Check(receipt, roleMethod != null && roleMethod.ReturnType == typeof(string) && resourceMethod != null && resourceMethod.ReturnType == typeof(string) &&
                prefabMethod != null && prefabMethod.ReturnType == typeof(GameObject) && identityMethod != null && identityMethod.ReturnType == typeof(bool) &&
                routeMethod != null && routeMethod.ReturnType == typeof(GameObject), "Production EnemyType-aware family policy API is absent or has a different contract.");
            if (roleMethod == null || resourceMethod == null || prefabMethod == null || identityMethod == null || routeMethod == null) return;
            var expected = new Dictionary<string, string> { { "grunt", "grunt" }, { "mini", "grunt" }, { "leaper", "runner" }, { "bloater", "carrier" }, { "boss_bloater", "carrier" }, { "carapace", "carapace" }, { "tick", "tick" }, { "spitter", "spitter" } };
            var productionTypes = new MazeConfig().enemyTypes;
            foreach (var pair in expected)
            {
                var type = productionTypes.SingleOrDefault(t => t.name == pair.Key);
                Check(receipt, type != null, "Required production family EnemyType missing: " + pair.Key); if (type == null) continue;
                string expectedResource = pair.Value == "spitter" ? "Models/cohesive_lavender_spitter" : "Models/cargo-family-" + pair.Value;
                string actualRole = roleMethod.Invoke(null, new object[] { type }) as string;
                string actualResource = resourceMethod.Invoke(null, new object[] { type }) as string;
                var resolved = prefabMethod.Invoke(null, new object[] { type }) as GameObject;
                string expectedPath = pair.Value == "spitter" ? Spitter : FamilyPath(pair.Value);
                var record = new PolicyRecord { type = type.name, ai = type.ai, expectedResource = expectedResource, resolvedPath = AssetDatabase.GetAssetPath(resolved),
                    exactResource = actualRole == pair.Value && actualResource == expectedResource && resolved != null && resolved == Resources.Load<GameObject>(expectedResource) && AssetDatabase.GetAssetPath(resolved) == expectedPath };
                receipt.familyPolicy.Add(record);
                Check(receipt, record.exactResource, "Production family policy falls back or selects a mismatched role/resource: " + type.name);
                if (resolved != null)
                    Check(receipt, pair.Value == "spitter" ? CohesiveCharacterPolicy.IsAuthoredSpitterPrefab(resolved) : (bool)identityMethod.Invoke(null, new object[] { resolved }), "Authored family/spitter identity helper rejects its required model: " + type.name);
            }
            receipt.warnings.Add("Policy gate verifies actual production EnemyTypes through route-independent RoleFor/ResourceFor/AuthoredEnemyPrefab and requires the EnemyType-aware route overload. Active-Cargo spawn/pool selection is verified by rendered runtime QA, not forced inside this Editor-only asset gate.");
        }

        static void ValidateFamilyPaletteDistinct(Receipt receipt)
        {
            var carrier = receipt.models.FirstOrDefault(m => m.role == "family-carrier");
            var carapace = receipt.models.FirstOrDefault(m => m.role == "family-carapace");
            if (carrier == null || carapace == null) return;
            Check(receipt, !string.IsNullOrEmpty(carrier.sourceAlbedoSha256) && !string.IsNullOrEmpty(carapace.sourceAlbedoSha256) && carrier.sourceAlbedoSha256 != carapace.sourceAlbedoSha256,
                "Carrier/carapace aliases have identical/missing authored albedo bytes; separate shell palette contract missing.");
            receipt.warnings.Add("Carrier/carapace have distinct source atlas bytes; actual silhouette/palette readability still requires rendered review. Import validation is not visual approval.");
        }

        public static void ImportAndValidate() => Run(true);
        public static void ValidateForBuild() => Run(false);

        static void Run(bool configure)
        {
            string output = Environment.GetEnvironmentVariable("COHESIVE_OUT");
            if (string.IsNullOrWhiteSpace(output)) throw new BuildFailedException("Set COHESIVE_OUT to a review evidence directory before cohesive import/build validation.");
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            var receipt = new Receipt { utc = DateTime.UtcNow.ToString("O"), mode = configure ? "import-and-validate" : "validate-only", unityVersion = Application.unityVersion, projectPath = Directory.GetParent(Application.dataPath).FullName };
            try
            {
                var newModels = Cargo.Select(s => ModelRoot + "cargo-" + s + ".glb").Concat(new[] { Spitter, Engineer }).Concat(Family.Select(FamilyPath)).ToArray();
                receipt.selectorCaptureRequired = !configure;
                receipt.selectorCapturePresent = File.Exists(Absolute(ArtRoot + SelectorArt + ".png"));
                var artNames = Art.Concat(receipt.selectorCapturePresent || !configure ? new[] { SelectorArt } : Array.Empty<string>()).ToArray();
                var newArt = artNames.Select(s => ArtRoot + s + ".png").ToArray();
                if (configure && !receipt.selectorCapturePresent) receipt.warnings.Add("Actual captured cargo-arena selector art absent: critique import only; ValidateForBuild will require it.");
                foreach (var path in newModels.Concat(newArt).Concat(new[] { Hero, Gun, EngineerPortrait, HeavyPortrait })) Check(receipt, File.Exists(Absolute(path)), "Required asset missing: " + path);
                if (receipt.errors.Count == 0 && configure)
                {
                    foreach (var path in newModels) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    SetLegacy(receipt); SetLegacy(receipt, Engineer);
                    foreach (var role in Family) SetLegacy(receipt, FamilyPath(role));
                    foreach (var path in newModels) ConfigureEmbeddedTextures(path, receipt);
                    foreach (var path in newArt) ConfigureArt(path, receipt);
                    ConfigurePortrait(EngineerPortrait, receipt); ConfigurePortrait(HeavyPortrait, receipt);
                }
                if (receipt.errors.Count == 0)
                {
                    foreach (var slug in Cargo) ValidateModel(ModelRoot + "cargo-" + slug + ".glb", "CohesiveZone/Models/cargo-" + slug, "cargo", receipt);
                    ValidateModel(Spitter, "Models/cohesive_lavender_spitter", "spitter", receipt);
                    ValidateModel(Hero, "Models/hero_vanguard_chibi", "hero", receipt);
                    ValidateModel(Engineer, "Models/hero_engineer_chibi", "engineer", receipt);
                    ValidateArt(EngineerPortrait, "UI/Portraits/engineer", receipt); ValidateArt(HeavyPortrait, "UI/Portraits/heavy-chibi", receipt);
                    ValidateModel(Gun, "Models/minigun_chibi", "gun", receipt);
                    foreach (var role in Family) ValidateModel(FamilyPath(role), "Models/cargo-family-" + role, "family-" + role, receipt);
                    ValidateFamilyPaletteDistinct(receipt);
                    ValidateFamilyPolicy(receipt);
                    foreach (var name in artNames) ValidateArt(ArtRoot + name + ".png", "CohesiveZone/Art/" + name, receipt);
                    Check(receipt, CohesiveZone.Enabled, "CohesiveZone.Enabled must be the intentional delivery default.");
                    Check(receipt, CohesiveCharacterPolicy.UseRebuiltVanguard, "The actual hero/gun resource policy falls back to an old character.");
                    Check(receipt, CohesiveCharacterPolicy.HasAuthoredSpitter, "The actual enemy policy cannot resolve the authored spitter.");
                }
            }
            catch (Exception e) { receipt.errors.Add(e.ToString()); }
            receipt.success = receipt.errors.Count == 0;
            string file = Path.Combine(output, configure ? "cohesive-import-validation.json" : "cohesive-build-validation.json");
            File.WriteAllText(file, JsonUtility.ToJson(receipt, true));
            if (!receipt.success) throw new BuildFailedException("Cohesive asset validation failed: " + string.Join(" | ", receipt.errors) + "\nReceipt: " + file);
            Debug.Log("Cohesive asset validation passed " + receipt.checks + " checks. Receipt: " + file);
        }
        static void Check(Receipt r, bool ok, string error) { r.checks++; if (!ok) r.errors.Add(error); }

        static void SetLegacy(Receipt r, string path = Spitter)
        {
            var importer = AssetImporter.GetAtPath(path);
            Check(r, importer != null, "Character importer missing: " + path); if (importer == null) return;
            var so = new SerializedObject(importer); bool changed = false; int matches = 0;
            // Exactly the iteration/property/int convention used by MazeSetup.SetupModels.
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.Enum && it.propertyType != SerializedPropertyType.Integer) continue;
                if (!it.name.ToLowerInvariant().Contains("animationmethod")) continue;
                matches++; if (it.intValue != 1) { it.intValue = 1; changed = true; }
            }
            Check(r, matches == 1, "Character Legacy animation importer setting not found uniquely: " + path);
            if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport(); r.importerChanges.Add(path + ": animationMethod=1 Legacy"); }
        }
        static bool Legacy(string path)
        {
            var importer = AssetImporter.GetAtPath(path); if (importer == null) return false;
            var so = new SerializedObject(importer); var it = so.GetIterator();
            while (it.Next(true)) if ((it.propertyType == SerializedPropertyType.Enum || it.propertyType == SerializedPropertyType.Integer) && it.name.ToLowerInvariant().Contains("animationmethod")) return it.intValue == 1;
            return false;
        }
        static void ConfigureEmbeddedTextures(string path, Receipt r)
        {
            var importer = AssetImporter.GetAtPath(path); if (importer == null) return;
            var so = new SerializedObject(importer); bool changed = false;
            foreach (var field in new[] { ("importSettings.generateMipMaps", true), ("importSettings.texturesReadable", false) })
            {
                var p = so.FindProperty(field.Item1);
                if (p != null && p.propertyType == SerializedPropertyType.Boolean && p.boolValue != field.Item2) { p.boolValue = field.Item2; changed = true; }
            }
            if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport(); r.importerChanges.Add(path + ": embedded mips=true/readable=false; color/normal usage untouched"); }
        }
        static void ConfigureArt(string path, Receipt r)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(r, ti != null, "Art TextureImporter missing: " + path); if (ti == null) return;
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None; ti.isReadable = false; ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear; ti.alphaSource = TextureImporterAlphaSource.None; ti.alphaIsTransparency = false;
            ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 80 });
            ti.SaveAndReimport(); r.importerChanges.Add(path + ": sRGB, NPOT none, opaque, ASTC6x6 iPhone max2048");
        }

        static void ConfigurePortrait(string path, Receipt receipt)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(receipt, ti != null, "Engineer portrait importer missing"); if (ti == null) return;
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None; ti.isReadable = false; ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear; ti.maxTextureSize = 512;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 512, format = TextureImporterFormat.ASTC_4x4, compressionQuality = 80 });
            ti.SaveAndReimport(); receipt.importerChanges.Add(path + ": alpha-preserving sRGB512 portrait ASTC4x4");
        }
        static void ValidateEngineerRig(GameObject prefab, ModelRecord rec, Receipt receipt)
        {
            rec.expectedSha256 = EngineerExpectedSha;
            Check(receipt, rec.sha256 == EngineerExpectedSha, "Engineer bytes differ from the approved rig handoff");
            var bones = prefab.GetComponentsInChildren<Transform>(true);
            foreach (string name in new[] { "Hips", "Spine", "RightHand", "LeftHand" })
                Check(receipt, bones.Count(b => b.name == name || b.name.EndsWith(":" + name, StringComparison.Ordinal)) == 1, "Engineer required unique bone missing: " + name);
            foreach (string side in new[] { "Right", "Left" })
            {
                var hand = bones.SingleOrDefault(b => b.name == side + "Hand" || b.name.EndsWith(":" + side + "Hand", StringComparison.Ordinal));
                Check(receipt, hand != null && hand.parent != null && hand.parent.parent != null && hand.childCount > 0 && hand.GetChild(0).name == side + "Palm", "Engineer conventional arm chain/explicit first-child palm absent: " + side);
            }
            Check(receipt, rec.triangles > 10000 && rec.triangles < 30000 && rec.nativeBoundsSize.y > .8f && rec.nativeBoundsSize.y < 1.2f, "Engineer native geometry/author scale differs from handoff");
        }

        static void ValidateModel(string path, string resource, string role, Receipt receipt)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(receipt, prefab != null, "No imported model GameObject: " + path); if (prefab == null) return;
            Check(receipt, AssetDatabase.GetAssetPath(Resources.Load<GameObject>(resource)) == path, "Resources lookup missing or ambiguous: " + resource);
            var importer = AssetImporter.GetAtPath(path);
            var rec = new ModelRecord { path = path, resource = resource, role = role, sha256 = Hash(path), importer = importer != null ? importer.GetType().FullName : "missing", legacyImporter = Legacy(path) };
            receipt.models.Add(rec);
            if (IsFamily(role)) { rec.expectedSha256 = FamilyExpectedSha[Array.IndexOf(Family, role.Substring(7))]; Check(receipt, rec.sha256 == rec.expectedSha256, "Family resource bytes differ from verified final GLB metadata: " + path); }
            var rs = prefab.GetComponentsInChildren<Renderer>(true); rec.rendererCount = rs.Length;
            var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true); rec.skinnedRendererCount = skins.Length;
            Check(receipt, rs.Length > 0, "No renderers: " + path);
            var meshes = new HashSet<Mesh>(); var mats = new HashSet<Material>();
            foreach (var renderer in rs)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer sk ? sk.sharedMesh : renderer.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                Check(receipt, mesh != null && mesh.vertexCount > 0, "Empty renderer mesh: " + path + "/" + renderer.name);
                if (mesh != null && meshes.Add(mesh))
                {
                    rec.vertices += mesh.vertexCount;
                    for (int i = 0; i < mesh.subMeshCount; i++) rec.triangles += (int)(mesh.GetIndexCount(i) / 3);
                }
                foreach (var material in renderer.sharedMaterials)
                {
                    Check(receipt, material != null && material.shader != null && !material.shader.name.Contains("InternalError"), "Missing/error material: " + path);
                    if (material != null && mats.Add(material)) ValidateMaterial(material, role, rec, receipt);
                }
            }
            Check(receipt, rec.triangles > 0, "No triangles: " + path);
            if (role == "cargo" && rec.triangles > 12000) receipt.warnings.Add(path + ": dense cargo prototype (" + rec.triangles + " triangles)");
            Check(receipt, CohesiveCharacterPolicy.TryMeshBounds(prefab.transform, out var bounds), "Cannot measure combined mesh bounds: " + path);
            rec.nativeBoundsCenter = bounds.center; rec.nativeBoundsSize = bounds.size;
            Check(receipt, Finite(bounds.size) && bounds.size.x > .001f && bounds.size.y > .001f && bounds.size.z > .001f, "Invalid native dimensions: " + path);
            if (role == "gun") Check(receipt, prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Barrels"), "Chibi gun missing retained Barrels transform.");
            if (role == "spitter" || role == "hero" || role == "engineer" || IsFamily(role))
            {
                Check(receipt, rec.legacyImporter, "Character importer is not Legacy: " + path);
                Check(receipt, skins.Length > 0, "Character is static, no SkinnedMeshRenderer: " + path);
                Check(receipt, prefab.GetComponentsInChildren<Animator>(true).Length == 0, "Character contains a competing Animator: " + path);
                foreach (var skin in skins)
                {
                    Check(receipt, skin.bones.Length > 0 && skin.bones.All(b => b != null), "Missing skin bones: " + path);
                    Check(receipt, skin.sharedMesh != null && skin.sharedMesh.bindposes.Length == skin.bones.Length, "Bindpose/bone count mismatch: " + path);
                }
                ValidateClips(prefab, role, rec, receipt);
                ValidateSourceSkin(path, role, rec, receipt);
                if (IsFamily(role)) ValidateFamilyRig(prefab, role, rec, receipt);
                if (role == "engineer") ValidateEngineerRig(prefab, rec, receipt);
            }
        }
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));
        static void ValidateClips(GameObject prefab, string role, ModelRecord rec, Receipt receipt)
        {
            var player = prefab.GetComponentInChildren<Animation>(true);
            Check(receipt, player != null, "No legacy Animation component: " + rec.path); if (player == null) return;
            foreach (AnimationState state in player)
            {
                var clip = state.clip; if (clip == null) continue;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var c = new ClipRecord { name = state.name, length = clip.length, legacy = clip.legacy, bindings = bindings.Length };
                foreach (var binding in bindings)
                {
                    bool resolves = string.IsNullOrEmpty(binding.path) || player.transform.Find(binding.path) != null;
                    if (role == "engineer") Check(receipt, resolves, "Engineer clip has unresolved binding: " + binding.path);
                    if (!resolves) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.length > 1 && curve.keys.Max(k => k.value) - curve.keys.Min(k => k.value) > .00001f) c.movingResolvedBindings++;
                }
                if (role == "engineer" && state.name == "Dash")
                    foreach (string joint in new[] { "Hips", "Spine", "RightFoot", "LeftFoot", "RightHand", "LeftHand" })
                        Check(receipt, bindings.Any(b => b.path == joint || b.path.EndsWith("/" + joint, StringComparison.Ordinal)), "Held Dash must bind " + joint);
                rec.clips.Add(c);
                if (IsFamily(role)) ValidateFamilyClipBindings(player, clip, role, receipt, rec.path);
            }
            rec.requiredRoleClips = IsFamily(role) ? FamilyClips(role).Select(x => x.name).ToArray() : role == "spitter" ? new[] { "Idle", "Walk", "Spit" } : role == "engineer" ? new[] { "Idle", "Walking", "Jogging", "Running", "Dash", "Attack", "Death" } : new[] { "Walking", "Running", "Dead" };
            foreach (string name in rec.requiredRoleClips)
            {
                var clip = rec.clips.FirstOrDefault(c => c.name.Equals(name, StringComparison.OrdinalIgnoreCase) || c.name.EndsWith("|" + name, StringComparison.OrdinalIgnoreCase));
                // Dash is the approved held tuck/glide pose, not a locomotion loop.
                bool heldDash = role == "engineer" && name == "Dash" && clip != null && Mathf.Abs(clip.length - .16f) < .01f;
                Check(receipt, clip != null && clip.legacy && clip.length > .05f && clip.bindings > 0 && (clip.movingResolvedBindings > 0 || heldDash), "Missing/static/unbound Legacy " + name + ": " + rec.path);
                if (role == "engineer" && name == "Dash") Check(receipt, heldDash, "Engineer Dash must retain the approved.16s held pose");
                if (role == "spitter" && clip != null) Check(receipt, Mathf.Abs(clip.length - (name == "Idle" ? 2f : .8f)) < .05f, "Unexpected spitter " + name + " duration " + clip.length);
                if (IsFamily(role) && clip != null) Check(receipt, Mathf.Abs(clip.length - FamilyClips(role).Single(x => x.name == name).seconds) <= 1f / 60f + .001f, "Family role clip duration differs from authored60Hz contract: " + rec.path + "/" + name);
            }
            if (role == "spitter") Check(receipt, prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Muzzle"), "Spitter Muzzle bone missing.");
        }
        static Texture2D Texture(Material m, string a, string b) => (m.HasProperty(a) ? m.GetTexture(a) : m.HasProperty(b) ? m.GetTexture(b) : null) as Texture2D;
        static void ValidateMaterial(Material material, string role, ModelRecord rec, Receipt receipt)
        {
            var albedo = Texture(material, "baseColorTexture", "_BaseMap");
            var normal = Texture(material, "normalTexture", "_BumpMap");
            var mr = Texture(material, "metallicRoughnessTexture", "_MetallicGlossMap");
            var r = new MaterialRecord { name = material.name, shader = material.shader.name, albedo = albedo != null ? albedo.name : null, normal = normal != null ? normal.name : null, roughnessMetal = mr != null ? mr.name : null,
                albedoSrgb = albedo != null && GraphicsFormatUtility.IsSRGBFormat(albedo.graphicsFormat), normalLinear = normal == null || !GraphicsFormatUtility.IsSRGBFormat(normal.graphicsFormat),
                albedoFormat = albedo != null ? albedo.graphicsFormat.ToString() : null, normalFormat = normal != null ? normal.graphicsFormat.ToString() : null,
                width = albedo != null ? albedo.width : 0, height = albedo != null ? albedo.height : 0, mips = albedo != null ? albedo.mipmapCount : 0 };
            rec.materials.Add(r);
            if (albedo != null) Check(receipt, r.albedoSrgb, "Albedo is not sRGB: " + rec.path + "/" + material.name);
            if (normal != null) Check(receipt, r.normalLinear, "Embedded normal is incorrectly sRGB: " + rec.path + "/" + material.name);
            if (mr != null) Check(receipt, !GraphicsFormatUtility.IsSRGBFormat(mr.graphicsFormat), "Embedded roughness/metal map is incorrectly sRGB: " + rec.path);
            // Invoke the actual production conversion on a material only. It creates a cached
            // runtime material clone; no prefab asset or scene object is instantiated/modified.
            var method = typeof(Mats).GetMethod("FromGltf", BindingFlags.Static | BindingFlags.NonPublic);
            Check(receipt, method != null, "Production Mats.FromGltf converter not found."); if (method == null) return;
            var converted = method.Invoke(null, new object[] { material }) as Material;
            Check(receipt, converted != null && converted.shader != null && converted.shader.name == "Universal Render Pipeline/Lit", "Production model material does not resolve to URP/Lit: " + rec.path);
            if (converted == null) return;
            r.urpShader = converted.shader.name;
            r.urpKeepsAlbedo = albedo == null || converted.GetTexture("_BaseMap") == albedo;
            r.urpKeepsNormal = normal == null || converted.GetTexture("_BumpMap") == normal;
            // Existing Mats.FromGltf deliberately selects the retained full-resolution rebuild atlas.
            // New pipeline props/spitter must keep their embedded maps exactly; only this known hero path may override.
            var retainedHeroAtlas = role == "hero" && material.name == "VanguardChibiMat" ? Resources.Load<Texture2D>("Models/VanguardChibiMat_base") : null;
            r.retainedHeroAlbedoOverride = retainedHeroAtlas != null && converted.GetTexture("_BaseMap") == retainedHeroAtlas;
            r.resolvedAlbedoPath = AssetDatabase.GetAssetPath(converted.GetTexture("_BaseMap"));
            Check(receipt, (r.urpKeepsAlbedo || r.retainedHeroAlbedoOverride) && r.urpKeepsNormal, "Production converter replaced expected maps unexpectedly: " + rec.path + "/" + material.name);
            if (IsFamily(role))
            {
                r.opaque = (!converted.HasProperty("_Surface") || converted.GetFloat("_Surface") < .5f) && converted.renderQueue < 2450 && converted.GetColor("_BaseColor").a >= .999f;
                Check(receipt, albedo != null && r.albedoSrgb && r.urpKeepsAlbedo && r.opaque, "Family material lost authored opaque sRGB albedo: " + rec.path);
            }
            if (normal != null)
            {
                r.sourceNormalScale = material.HasProperty("normalTexture_scale") ? material.GetFloat("normalTexture_scale") : material.HasProperty("_BumpScale") ? material.GetFloat("_BumpScale") : 1;
                r.urpNormalScale = converted.GetFloat("_BumpScale");
                Check(receipt, Mathf.Abs(r.sourceNormalScale - r.urpNormalScale) < .001f && r.urpNormalScale >= 0 && r.urpNormalScale <= 1.25f, "Unexpected/overdriven normal strength: " + rec.path);
            }
        }
        static void ValidateArt(string path, string resource, Receipt receipt)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter; var texture = Resources.Load<Texture2D>(resource);
            Check(receipt, ti != null && texture != null, "Art resource/importer missing: " + resource); if (ti == null || texture == null) return;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h); var ios = ti.GetPlatformTextureSettings("iPhone");
            var rec = new ArtRecord { path = path, resource = resource, sha256 = Hash(path), sourceWidth = w, sourceHeight = h, importedWidth = texture.width, importedHeight = texture.height,
                srgb = ti.sRGBTexture, npotPreserved = ti.npotScale == TextureImporterNPOTScale.None, readable = ti.isReadable, iosOverride = ios.overridden, format = ios.format.ToString() };
            rec.capturedSelectorSlot = resource == "CohesiveZone/Art/" + SelectorArt;
            receipt.art.Add(rec);
            if (resource.StartsWith("UI/Portraits/")) Check(receipt, w == 512 && h == 512 && ti.alphaSource == TextureImporterAlphaSource.FromInput && ti.alphaIsTransparency, "Actual-model portrait must retain512 alpha framing");
            if (rec.capturedSelectorSlot) Check(receipt, w >= 1334 && h >= 750 && (float)w / h > 1.5f, "Actual selector capture must retain landscape review resolution: " + path);
            Check(receipt, AssetDatabase.GetAssetPath(texture) == path && rec.srgb && rec.npotPreserved && !rec.readable && ios.overridden, "Art dimensions/color/mobile import policy incorrect: " + path);
            Check(receipt, w > 0 && h > 0 && Mathf.Abs((float)w / h - (float)texture.width / texture.height) < .01f, "Art framing aspect changed: " + path);
        }
        static string Absolute(string path) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Absolute(path)))).Replace("-", "").ToLowerInvariant(); }

        [Serializable] sealed class GlbDoc { public Skin[] skins; public Node[] nodes; public MeshDef[] meshes; public Accessor[] accessors; public View[] bufferViews; public GlbAnimation[] animations; public GlbMaterial[] materials; public GlbTexture[] textures; public GlbImage[] images; }
        [Serializable] sealed class GlbAnimation { public string name; }
        [Serializable] sealed class GlbMaterial { public string alphaMode; public GlbPbr pbrMetallicRoughness = new GlbPbr(); }
        [Serializable] sealed class GlbPbr { public float[] baseColorFactor; public GlbIndex baseColorTexture; }
        [Serializable] sealed class GlbIndex { public int index = -1; }
        [Serializable] sealed class GlbTexture { public int source = -1; }
        [Serializable] sealed class GlbImage { public int bufferView = -1; }
        [Serializable] sealed class Skin { public int[] joints; }
        [Serializable] sealed class Node { public string name; }
        [Serializable] sealed class MeshDef { public Primitive[] primitives; }
        [Serializable] sealed class Primitive { public Attributes attributes; }
        [Serializable] sealed class Attributes { public int WEIGHTS_0 = -1, JOINTS_0 = -1; }
        [Serializable] sealed class Accessor { public int bufferView, byteOffset, componentType, count; public string type; public bool normalized; }
        [Serializable] sealed class View { public int byteOffset, byteStride, byteLength; }
        static void ValidateSourceSkin(string path, string role, ModelRecord rec, Receipt receipt)
        {
            byte[] bytes = File.ReadAllBytes(Absolute(path)); Check(receipt, bytes.Length > 20 && BitConverter.ToUInt32(bytes, 0) == 0x46546c67, "Invalid GLB header: " + path);
            int jsonSize = BitConverter.ToInt32(bytes, 12); string json = Encoding.UTF8.GetString(bytes, 20, jsonSize);
            var doc = JsonUtility.FromJson<GlbDoc>(json); int bin = 0;
            for (int offset = 12; offset + 8 <= bytes.Length;)
            {
                int size = BitConverter.ToInt32(bytes, offset); uint type = BitConverter.ToUInt32(bytes, offset + 4);
                if (type == 0x004e4942) { bin = offset + 8; break; } offset += 8 + size;
            }
            Check(receipt, bin > 0 && doc.skins != null && doc.skins.Length > 0, "Source GLB has no embedded skin: " + path);
            if (bin == 0 || doc.skins == null || doc.skins.Length == 0) return;
            rec.sourceJoints = doc.skins[0].joints.Length;
            Check(receipt, rec.sourceJoints == (IsFamily(role) ? FamilyJoints(role).Length : role == "spitter" ? 15 : role == "engineer" ? EngineerExpectedJoints : 58), "Unexpected source rig joint count: " + path);
            if (IsFamily(role)) { Check(receipt, doc.skins.Length == 1, "Family role must have exactly one authored skin: " + path); ValidateFamilySource(doc, bytes, bin, role, rec, receipt); }
            if (role == "spitter") Check(receipt, doc.nodes.Any(n => n.name == "Muzzle"), "Source spitter Muzzle absent.");
            Check(receipt, !json.Contains("\"WEIGHTS_1\""), "More than four source skin influences: " + path);
            foreach (var mesh in doc.meshes) foreach (var primitive in mesh.primitives)
            {
                int index = primitive.attributes.WEIGHTS_0;
                Check(receipt, index >= 0 && index < doc.accessors.Length, "Missing source WEIGHTS_0: " + path); if (index < 0 || index >= doc.accessors.Length) continue;
                var a = doc.accessors[index]; var view = doc.bufferViews[a.bufferView];
                Check(receipt, a.type == "VEC4", "Source weights are not four-component vectors: " + path);
                int component = a.componentType == 5126 ? 4 : a.componentType == 5123 ? 2 : a.componentType == 5121 ? 1 : 0;
                Check(receipt, component > 0, "Unsupported source weight encoding: " + path); if (component == 0) continue;
                if (IsFamily(role) || role == "engineer")
                {
                    Check(receipt, component == 4 || a.normalized, "Integer family weights must use normalized encoding: " + path);
                    int jointIndex = primitive.attributes.JOINTS_0;
                    Check(receipt, jointIndex >= 0 && jointIndex < doc.accessors.Length, "Family source JOINTS_0 absent: " + path);
                    if (jointIndex >= 0 && jointIndex < doc.accessors.Length)
                    {
                        var joints = doc.accessors[jointIndex]; var jointView = doc.bufferViews[joints.bufferView];
                        int jointBytes = joints.componentType == 5121 ? 1 : joints.componentType == 5123 ? 2 : 0;
                        Check(receipt, joints.type == "VEC4" && joints.count == a.count && jointBytes > 0, "Family joint indices mismatch four-weight vertices: " + path);
                        if (jointBytes > 0)
                        {
                            int jointStride = jointView.byteStride > 0 ? jointView.byteStride : 4 * jointBytes;
                            bool inRange = true;
                            for (int v = 0; v < joints.count; v++) for (int k = 0; k < 4; k++)
                            {
                                int off = bin + jointView.byteOffset + joints.byteOffset + v * jointStride + k * jointBytes;
                                int indexValue = jointBytes == 1 ? bytes[off] : BitConverter.ToUInt16(bytes, off);
                                if (indexValue >= rec.sourceJoints) inRange = false;
                            }
                            Check(receipt, inRange, "Family source references a joint outside its exact rig: " + path);
                        }
                    }
                }
                int stride = view.byteStride > 0 ? view.byteStride : 4 * component;
                for (int v = 0; v < a.count; v++)
                {
                    float sum = 0; int influences = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int off = bin + view.byteOffset + a.byteOffset + v * stride + k * component;
                        float w = component == 4 ? BitConverter.ToSingle(bytes, off) : component == 2 ? BitConverter.ToUInt16(bytes, off) / 65535f : bytes[off] / 255f;
                        if (float.IsNaN(w) || float.IsInfinity(w) || w < 0) { receipt.errors.Add("Invalid source bone weight: " + path); break; }
                        sum += w; if (w > .000001f) influences++;
                    }
                    rec.maxSourceWeightError = Mathf.Max(rec.maxSourceWeightError, Mathf.Abs(1 - sum)); rec.maximumSourceInfluences = Mathf.Max(rec.maximumSourceInfluences, influences);
                }
            }
            Check(receipt, rec.maximumSourceInfluences > 0 && rec.maximumSourceInfluences <= 4 && rec.maxSourceWeightError < .002f, "Unnormalized/unweighted source vertices: " + path);
        }
    }

    // Read-only build gate. Never invokes import/reimport, Setup Project, scenes or install.
    public sealed class CohesiveAssetBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.iOS && CohesiveZone.Enabled) CohesiveAssetImport.ValidateForBuild();
        }
    }
}
#endif
