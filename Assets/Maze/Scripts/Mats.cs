using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Materials are cloned from template assets in Resources/Materials (created by
    // Maze > Setup Project) so URP keeps the shader variants they need in player builds.
    // In the editor it falls back to Shader.Find if a template is missing.
    public static class Mats
    {
        static Material Template(string name, string shader)
        {
            var t = Resources.Load<Material>("Materials/" + name);
            if (t != null) return new Material(t);
            var s = Shader.Find(shader);
            if (s == null) { Debug.LogError($"Maze: shader '{shader}' not found. Run Maze > Setup Project."); s = Shader.Find("Hidden/InternalErrorShader"); }
            return new Material(s);
        }

        public static Material Lit(Color color, Texture tex = null, Vector2? tile = null, float smoothness = 0.15f, float metallic = 0f)
        {
            var m = Template("MazeLit", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                if (tile.HasValue) m.SetTextureScale("_BaseMap", new Vector2(1f / tile.Value.x, 1f / tile.Value.y));
            }
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        // unlit, alpha-blended (decals, rings)
        public static Material Fade(Color color, Texture tex = null)
        {
            var m = Template("MazeFade", "Universal Render Pipeline/Unlit");
            MakeTransparent(m, false);
            m.SetColor("_BaseColor", color);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        // unlit, additive (fire, muzzle flash, tracers)
        public static Material Glow(Color color)
        {
            var m = Template("MazeGlow", "Universal Render Pipeline/Unlit");
            MakeTransparent(m, true);
            m.SetColor("_BaseColor", color);
            return m;
        }

        public static Material Unlit(Color color, Texture tex = null)
        {
            var m = Template("MazeUnlit", "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        // lit with an emission map (Cryo Station's glowing pads)
        public static Material LitEmit(Texture tex, Vector2 tile, Texture emit, Color emitColor, float smoothness)
        {
            var m = Template("MazeLitEmit", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white); m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", new Vector2(1f / tile.x, 1f / tile.y));
            m.SetTexture("_EmissionMap", emit); m.SetColor("_EmissionColor", emitColor); m.EnableKeyword("_EMISSION");
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0f);
            return m;
        }

        // full PBR (Cryo Station, Tools/cryo_pbr.mjs): colour, OpenGL normal, metallic+smoothness pack (R metal,
        // A smoothness), optional emission, optional alpha clip; tile = metres per texture repeat on world-space UVs
        public static Material PBR(Texture color, Texture normal, Texture ms, Vector2 tile, float normalScale = 1f,
                                   Texture emit = null, Color? emitColor = null, float? cutoff = null)
        {
            string t = cutoff.HasValue ? "MazeLitPBRClipEmit" : emit != null ? "MazeLitPBREmit" : "MazeLitNormalMS";
            var m = Template(t, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white); m.SetTexture("_BaseMap", color);
            m.SetTextureScale("_BaseMap", new Vector2(1f / tile.x, 1f / tile.y));
            m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f);
            if (emit != null) { m.SetTexture("_EmissionMap", emit); m.SetColor("_EmissionColor", emitColor ?? Color.white); m.EnableKeyword("_EMISSION"); }
            if (cutoff.HasValue) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", cutoff.Value); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; }
            return m;
        }

        // the painted map floor with a PBR plate pattern as detail: x2 grey albedo + detail normal (Cryo Station)
        public static Material LitDetailPBR(Texture tex, Texture detail, Texture detailNormal, Vector2 detailTiles, float smoothness, float detailNormalScale)
        {
            var m = Template("MazeLitDetailN", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white); m.SetTexture("_BaseMap", tex);
            m.SetTexture("_DetailAlbedoMap", detail); m.SetTextureScale("_DetailAlbedoMap", detailTiles);
            m.SetFloat("_DetailAlbedoMapScale", 1f); m.EnableKeyword("_DETAIL_MULX2");
            m.SetTexture("_DetailNormalMap", detailNormal); m.SetFloat("_DetailNormalMapScale", detailNormalScale); m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0f);
            return m;
        }

        // lit with a x2 detail albedo map tiled on top (Cryo Station floor: painted map + deck-plate detail)
        public static Material LitDetail(Texture tex, Texture detail, Vector2 detailTiles, float smoothness)
        {
            var m = Template("MazeLitDetail", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white); m.SetTexture("_BaseMap", tex);
            m.SetTexture("_DetailAlbedoMap", detail); m.SetTextureScale("_DetailAlbedoMap", detailTiles);
            m.SetFloat("_DetailAlbedoMapScale", 1f); m.EnableKeyword("_DETAIL_MULX2");
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0f);
            return m;
        }

        // unlit, alpha-tested (the sector-select planet's spreading infestation: animate _Cutoff)
        public static Material UnlitClip(Texture tex, float cutoff)
        {
            var m = Template("MazeUnlitClip", "Universal Render Pipeline/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", cutoff); m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            return m;
        }

        public static Material Particles(bool additive)
        {
            var m = Template(additive ? "MazeParticlesAdd" : "MazeParticlesLit",
                             additive ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Particles/Simple Lit");
            if (additive) MakeTransparent(m, true);
            return m;
        }

        // lit + specular highlight (wet goo); smoothness comes from _SpecColor.a
        public static Material ParticlesGloss()
        {
            var m = Template("MazeParticlesGloss", "Universal Render Pipeline/Particles/Simple Lit");
            m.EnableKeyword("_SPECULAR_COLOR");
            m.SetColor("_SpecColor", new Color(0.8f, 0.72f, 0.85f, 0.85f));
            return m;
        }

        // unlit, alpha-blended particles (floor splats)
        public static Material ParticlesFade(Texture tex)
        {
            var m = Template("MazeParticlesFade", "Universal Render Pipeline/Particles/Unlit");
            MakeTransparent(m, false);
            m.SetTexture("_BaseMap", tex);
            return m;
        }

        // glTFast's shader graphs draw shadows but no color in iOS player builds, so imported
        // models are switched to URP/Lit (cloned from templates that keep the variants).
        static readonly System.Collections.Generic.Dictionary<Material, Material> gltfCache = new();

        public static void ConvertGltf(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                for (int i = 0; i < src.Length; i++) src[i] = FromGltf(src[i]);
                r.sharedMaterials = src;
            }
        }

        static Material FromGltf(Material g)
        {
            if (g == null || !g.shader.name.Contains("glTF")) return g; // "glTF/..." or "Shader Graphs/glTF-..."
            if (gltfCache.TryGetValue(g, out var cached)) return cached;
            var emit = g.HasProperty("emissiveTexture") ? g.GetTexture("emissiveTexture") : null;
            // a model's metallic-roughness converted to URP's layout (R metal, A smoothness) by its Tools script
            var ms = Resources.Load<Texture2D>("Models/" + g.name + "_ms");
            var m = Template(emit != null ? (ms != null ? "MazeLitPBREmit" : "MazeLitNormalEmit") : ms != null ? "MazeLitNormalMS" : "MazeLitNormal", "Universal Render Pipeline/Lit");
            m.name = g.name + " (URP)";
            if (g.HasProperty("baseColorFactor")) m.SetColor("_BaseColor", g.GetColor("baseColorFactor"));
            if (g.HasProperty("baseColorTexture")) m.SetTexture("_BaseMap", g.GetTexture("baseColorTexture"));
            var nrm = g.HasProperty("normalTexture") ? g.GetTexture("normalTexture") : null;
            // full-resolution maps shipped next to the model (Tools/sentinel.mjs): Unity-imported, ASTC on iOS
            var baseFull = Resources.Load<Texture2D>("Models/" + g.name + "_base");
            if (baseFull != null) m.SetTexture("_BaseMap", baseFull);
            var nrmFull = Resources.Load<Texture2D>("Models/" + g.name + "_normal");
            if (nrmFull != null) nrm = nrmFull;
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", g.GetFloat("normalTexture_scale")); m.EnableKeyword("_NORMALMAP"); }
            else m.DisableKeyword("_NORMALMAP");
            // glTF packs roughness/metal in G/B, URP wants metal in R and smoothness in A: use the converted map
            // when the model ships one (<material>_ms), else flat values
            if (ms != null)
            {
                m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f);
            }
            else
            {
                m.DisableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Metallic", 0.1f);
                m.SetFloat("_Smoothness", 0.35f);
            }
            if (emit != null)
            {
                var emitFull = Resources.Load<Texture2D>("Models/" + g.name + "_emit"); // full-res emission next to the model
                m.SetTexture("_EmissionMap", emitFull != null ? emitFull : emit);
                m.SetColor("_EmissionColor", g.HasProperty("emissiveFactor") ? g.GetColor("emissiveFactor") : Color.white);
                m.EnableKeyword("_EMISSION");
            }
            gltfCache[g] = m;
            return m;
        }

        // the keyword/blend setup URP's material inspector does for "Surface Type: Transparent"
        public static void MakeTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f); // double-sided: decals and rings are flat
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
