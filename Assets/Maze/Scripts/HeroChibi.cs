using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Chibi pass on the Iron Vanguard (2026-10-16, Manuel: "maybe our hero has way too much detail compared to the chibi style
    // we're going for"). OFF unless On or PlayerPrefs "ms.herochibi" = 1 (Playground has a CHIBI switch). Visual only: the
    // collision, hit radius, camera, clips and saves are untouched. Reversible: switching off restores every material/bone.
    //   Materials  hero and minigun swap to Maze/ChibiToon (soft two-tone ramp, gentle rim, faint outline) on flat colour-block
    //              maps baked in mesh space from the current albedo (Resources/Models/*_chibi.png, for both Vanguard rigs: per-face k-means palette,
    //              majority-merged over neighbouring faces so plates/greebles become one colour; the painted face is kept;
    //              the suit's blue glow kept as an accent). No normal / metal / detail maps.
    //   Proportions head x1.42 (HeroLook: 1.2), upper body x1.14, legs 12% shorter and a touch wider (the model drops to keep
    //              the boots on the floor).
    public static class HeroChibi
    {
        public static bool On = false;
        public static bool Enabled => CohesiveCharacterPolicy.UseRebuiltVanguard || On || PlayerPrefs.GetInt("ms.herochibi", 0) == 1;
        // the chibi REBUILD (Characters/VanguardChibi, built in Blender on the game's Meshy rig + clips): Player loads
        // Resources/Models/hero_vanguard_chibi + minigun_chibi instead; already chibi, so only its materials go ChibiToon
        public static bool ModelOn { get => CohesiveCharacterPolicy.PreserveAuthoredHero || PlayerPrefs.GetInt("ms.herochibi.model", 0) == 1; set { PlayerPrefs.SetInt("ms.herochibi.model", value ? 1 : 0); PlayerPrefs.Save(); } }
        public const float GunLen = 0.82f;   // the rebuild's minigun length (game metres) and hip grip (fractions of his height)
        public static readonly Vector3 HipGrip = new(0.08f, 0.36f, 0.2f);
        public const float Head = 1.42f, Upper = 1.14f, LegLen = .88f, LegWide = 1.06f;

        class St { public bool on; public Renderer[] rs; public Material[][] orig; public Transform model, hips; public Transform[] upLegs = new Transform[0], feet = new Transform[0]; public Vector3 modelBase; public float drop; }
        static readonly Dictionary<Transform, St> cache = new();
        static Material heroMat, heroMatB, gunMat;

        static Material Make(string tex)
        {
            var sh = Resources.Load<Shader>("Shaders/ChibiToon") ?? Shader.Find("Maze/ChibiToon"); if (sh == null) return null;
            var m = new Material(sh) { name = "Chibi " + tex }; var t = Resources.Load<Texture2D>("Models/" + tex); if (t != null) m.SetTexture("_BaseMap", t);
            return m;
        }

        // called every frame from ReadabilityFx.HeroBones (before the IK); returns whether the chibi look is on for this hero
        public static bool Tick(Transform vis)
        {
            // The intentional delivery default uses authored proportions/materials; no second toon/bone pass.
            if (CohesiveCharacterPolicy.PreserveAuthoredHero) return true;
            bool model = ModelOn, want = Enabled || model;
            if (!cache.TryGetValue(vis, out var s) || s.rs == null || s.rs.Length == 0 || s.rs[0] == null)
            {
                if (!want) return false;
                s = new St(); cache[vis] = s;
                s.rs = vis.GetComponentsInChildren<Renderer>(true); s.orig = new Material[s.rs.Length][];
                for (int i = 0; i < s.rs.Length; i++) s.orig[i] = s.rs[i].sharedMaterials;
                var ups = new List<Transform>(); var fs = new List<Transform>();
                foreach (var t in vis.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.EndsWith("UpLeg")) ups.Add(t); else if (t.name.EndsWith("Foot")) fs.Add(t);
                    if (s.hips == null && t.name.ToLower().Contains("hips")) s.hips = t;
                }
                s.upLegs = ups.ToArray(); s.feet = fs.ToArray();
                foreach (var r in s.rs) if (r is SkinnedMeshRenderer) { var m = r.transform; while (m.parent != null && m.parent != vis) m = m.parent; s.model = m; break; }
                if (s.model != null) s.modelBase = s.model.localPosition;
                float len = 0; int n = 0;
                foreach (var u in s.upLegs) foreach (var f in s.feet) if (f.IsChildOf(u)) { len += u.position.y - f.position.y; n++; }
                s.drop = n > 0 ? len / n * (1 - LegLen) : 0;
            }
            if (want != s.on) { s.on = want; Swap(s, want); }
            if (!want) return false;
            if (model) return true;   // (the rebuild is chibi already: no bone changes)
            foreach (var u in s.upLegs) u.localScale = new Vector3(LegWide, LegLen, LegWide);
            foreach (var f in s.feet) f.localScale = new Vector3(1 / LegWide, 1 / LegLen, 1 / LegWide);
            if (s.model != null) s.model.localPosition = s.modelBase + Vector3.down * (s.drop / Mathf.Max(1e-4f, vis.lossyScale.y));
            return true;
        }

        static readonly Dictionary<Material, Material> toon = new();
        static Material Toon(Material src)   // the rebuild's own flat map on ChibiToon
        {
            if (toon.TryGetValue(src, out var m)) return m;
            var sh = Resources.Load<Shader>("Shaders/ChibiToon") ?? Shader.Find("Maze/ChibiToon"); if (sh == null) return src;
            m = new Material(sh) { name = "Chibi " + src.name }; if (src.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            return toon[src] = m;
        }
        static void Swap(St s, bool on)
        {
            if (on && heroMat == null) { heroMat = Make("VanguardMeshyMat_chibi"); heroMatB = Make("VanguardMat_chibi"); gunMat = Make("MinigunMat_chibi"); }
            for (int i = 0; i < s.rs.Length; i++)
            {
                var r = s.rs[i]; if (r == null) continue;
                if (!on) { r.sharedMaterials = s.orig[i]; continue; }
                var src = s.orig[i]; var dst = new Material[src.Length];
                for (int k = 0; k < src.Length; k++)
                {
                    string nm = src[k] != null ? src[k].name : "";
                    dst[k] = nm.Contains("Chibi") ? Toon(src[k]) : nm.Contains("VanguardMeshy") && heroMat != null ? heroMat : nm.StartsWith("VanguardMat") && heroMatB != null ? heroMatB : nm.Contains("Minigun") && gunMat != null ? gunMat : src[k];
                }
                r.sharedMaterials = dst;
            }
            if (!on)
            {
                foreach (var u in s.upLegs) if (u != null) u.localScale = Vector3.one;
                foreach (var f in s.feet) if (f != null) f.localScale = Vector3.one;
                if (s.model != null) s.model.localPosition = s.modelBase;
            }
        }
    }
}
