using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Maze
{
    // The live 3D Vanguard on the painted hangar (2026-10-16, Manuel via the coordinator: "prep the 3D Vanguard rig for that
    // spot on the current plate ... behind a toggle, off by default"). OFF unless On is set or PlayerPrefs "ms.hangarhero3d" = 1.
    // The approved Heavy (Resources/Models/hero_vanguard_meshy) and his minigun stand in an isolated rig far below the world
    // (layer 28, its own camera and lights), rendered into a transparent RenderTexture that HangarLife places on the painted
    // hero's spot (feet ~(.361,.712), head ~(.361,.188) in the extended plate's painting coordinates; see
    // Design/Briefs/hangar-heroless-plate-for-codex.md). Lit to the painting: warm sun key from the front-left above, a warm
    // rim from the bay behind, a cyan fill from the holo table at his right. He is alive with:
    //   breathing      chest rise and fall (~3.6 s)
    //   weight shift   the hips settle onto one leg then the other (~9.5 s), spine and head counter
    //   spin-up        every ~8 s he braces and the minigun's barrels spin up, hold, and wind down (no shots)
    // Gameplay's hero, model, clips and Player are not touched; the pose is the game's idle (the walk clip held at frame 0)
    // with both hands put on the hip-carried gun by the same two-bone IK as in Player.
    // When RichHangar/Art/scene-rich-extended-heroless.png exists (Codex brief), the plate is swapped to it while this is on.
    public class HangarHero3D : MonoBehaviour
    {
        public static bool On = false;
        public static bool Enabled => CohesiveZone.Enabled || On || PlayerPrefs.GetInt("ms.hangarhero3d", 0) == 1;
        public const string HerolessPlate = "RichHangar/Art/scene-rich-extended-heroless";
        // where he stands on the plate (painting coords, u right, v down) and the frame's size (painting-height units)
        public static Vector2 Feet => CohesiveZone.Enabled ? CohesiveMenuArt.HeroFeet : new Vector2(.361f, .712f);
        public static float PaintedHeight => CohesiveZone.Enabled ? CohesiveMenuArt.HeroVisibleHeight : .524f;
        public const float FrameFill = .8f, FrameFeet = .08f, Aspect = 1f;
        public static float FrameH => PaintedHeight / FrameFill;
        public static Vector2 FrameCenter => new(Feet.x, Feet.y + FrameH * FrameFeet - FrameH * .5f);
        public static Vector2 FrameSize => new(FrameH * Aspect, FrameH);
        public static float Yaw = 128f;   // three-quarter toward camera-right, looking at the holo table

        const int Layer = 28; const float GunLen = 1.15f;
        float H => selectedId == "engineer" ? CohesiveCharacterPolicy.EngineerHeight : 1.76f;
        string selectedId; bool shown = true;   // game metres: modelHeight 1.6 x the Vanguard's 1.1, gun as in Player
        static readonly Vector3 HipGrip = new(0.06f, 0.50f, 0.17f);
        Transform rig, body, model, gun, barrels, hips, spine, chest, head, hand, handL;
        Vector3 hipsBase, gunGrip, gunGuard; float gunScale, gunDir, spin, spinSpeed, t;
        Animation anim; AnimationState restPose; Camera cam; RenderTexture rt;
        public Texture Texture => rt;

        public static HangarHero3D Make()
        {
            var go = new GameObject("HangarHero3D"); var h = go.AddComponent<HangarHero3D>();
            if (!h.Build()) { Destroy(go); return null; }
            return h;
        }

        bool Build()
        {
            selectedId = Prefs.Hero;
            var prefab = Resources.Load<GameObject>(CohesiveCharacterPolicy.HeroResource); if (prefab == null) return false;
            rig = new GameObject("HangarHeroRig").transform; rig.SetParent(transform, false); rig.position = new Vector3(0, -8000, 0);
            body = new GameObject("Body").transform; body.SetParent(rig, false); body.localRotation = Quaternion.Euler(0, Yaw, 0);
            model = Instantiate(prefab, body).transform; model.name = selectedId == "engineer" ? "Engineer" : "Vanguard"; Mats.ConvertGltf(model.gameObject);
            if (CohesiveCharacterPolicy.PreserveAuthoredHero) CohesiveCharacterPolicy.ApplyHeroMaterials(model);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true; }
            foreach (var a in model.GetComponentsInChildren<Animator>()) a.enabled = false;
            anim = model.GetComponentInChildren<Animation>();
            if (anim != null)
            {
                AnimationState walk = null;
                foreach (AnimationState st in anim)
                    if (walk == null && st.name.ToLower().Contains(selectedId == "engineer" ? "idle" : "walk")) walk = st;
                if (walk != null) { restPose = walk; anim.Play(walk.name); walk.speed = 0; walk.normalizedTime = 0; anim.Sample(); }   // the game's idle: walk held at frame 0
            }
            // height and floor, as Player.SetupHero
            // (tight: the skinned renderers' own bounds are loose before the first render, which left him small and floating)
            var b = Tight(model);
            if (b.size.y > .01f) { float s = H / b.size.y; model.localScale *= s; b = Tight(model); model.position += Vector3.up * (body.position.y - b.min.y); }
            foreach (var tr in model.GetComponentsInChildren<Transform>())
            {
                string n = tr.name.ToLower();
                if (hips == null && n.Contains("hips")) hips = tr;
                if (hand == null && n.Contains("righthand")) hand = tr;
                if (handL == null && n.Contains("lefthand")) handL = tr;
                if (tr.name.EndsWith(":Spine1") || tr.name == "Spine1") spine = tr;
                if (tr.name.EndsWith(":Spine2") || tr.name == "Spine2") chest = tr;
                if (tr.name.EndsWith(":Head") || tr.name == "Head") head = tr;
            }
            if (hips != null) hipsBase = hips.localPosition;
            if (selectedId == "engineer") CohesiveCharacterPolicy.SetEngineerAnimatedBounds(model);
            Minigun();
            foreach (var tr in rig.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = Layer;

            rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32) { name = "HangarHero3D", antiAliasing = 4 }; rt.Create();
            cam = new GameObject("HangarHeroCamera").AddComponent<Camera>(); cam.transform.SetParent(rig, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear; cam.cullingMask = 1 << Layer;
            cam.orthographic = true; cam.nearClipPlane = .1f; cam.farClipPlane = 30; cam.targetTexture = rt; cam.depth = -50;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            // framed so his height fills FrameFill of the frame with the feet FrameFeet above its bottom; a slight look down
            float frame = H / FrameFill; cam.orthographicSize = frame * .5f;
            var aim = rig.position + Vector3.up * (frame * (.5f - FrameFeet));
            cam.transform.position = aim + Quaternion.Euler(9f, 0, 0) * Vector3.back * 12f; cam.transform.LookAt(aim);
            if (CohesiveZone.Enabled)
            {
                Light("Garage key", new Vector3(-3f, 3.5f, -3f), new Color(1f, .83f, .68f), 3.3f, 12);
                Light("Planet rim", new Vector3(-1.2f, 3f, 3f), new Color(.73f, .56f, 1f), 4.1f, 6);
                Light("Holo fill", new Vector3(3f, .9f, -.6f), new Color(.44f, .85f, 1f), 2.1f, 7);
            }
            else
            {
                Light("Sun key", new Vector3(-3f, 3.5f, -3f), new Color(1f, .85f, .63f), 5.5f, 12);
                Light("Bay rim", new Vector3(-1.2f, 3f, 3f), new Color(1f, .78f, .5f), 6f, 6);
                Light("Holo fill", new Vector3(3f, .9f, -.6f), new Color(.44f, .85f, 1f), 3.2f, 7);
            }
            return true;
        }

        static Bounds Tight(Transform root)
        {
            bool first = true; Bounds total = default; var mesh = new Mesh();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                Bounds w;
                if (r is SkinnedMeshRenderer sk)
                {
                    sk.BakeMesh(mesh, false); var v = mesh.vertices; if (v.Length == 0) continue;
                    w = new Bounds(sk.transform.TransformPoint(v[0]), Vector3.zero); foreach (var p in v) w.Encapsulate(sk.transform.TransformPoint(p));
                }
                else w = r.bounds;
                if (first) { total = w; first = false; } else total.Encapsulate(w);
            }
            Destroy(mesh); return total;
        }

        void Light(string name, Vector3 p, Color c, float k, float range)
        {
            var l = new GameObject(name).AddComponent<Light>(); l.transform.SetParent(rig, false); l.transform.localPosition = p;
            l.type = LightType.Point; l.color = c; l.intensity = k; l.range = range; l.cullingMask = 1 << Layer; l.shadows = LightShadows.None;
        }

        // the minigun measured as in Player.AttachMinigun: grip, support handle, muzzle direction; carried level at the right hip
        void Minigun()
        {
            var prefab = Resources.Load<GameObject>(CohesiveCharacterPolicy.GunResource); if (prefab == null || hand == null) return;
            var go = Instantiate(prefab, body); go.name = "Minigun"; Mats.ConvertGltf(go);
            if (CohesiveCharacterPolicy.PreserveAuthoredHero) CohesiveCharacterPolicy.ApplyHeroMaterials(go.transform);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            foreach (var tr in go.GetComponentsInChildren<Transform>()) if (tr.name == "Barrels") barrels = tr;
            Bounds all = default, bodyB = default, barB = default; bool a0 = true, b0 = true, c0 = true;
            var root = go.transform; var keep = root.localScale; root.localScale = Vector3.one; root.localRotation = Quaternion.identity;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mb = mf.sharedMesh.bounds; var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var bb = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
                for (int k = 0; k < 8; k++) bb.Encapsulate(m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) * 2 - 1, (k & 2) - 1, (k & 4) / 2f - 1))));
                bool isBar = barrels != null && mf.transform.IsChildOf(barrels);
                if (a0) { all = bb; a0 = false; } else all.Encapsulate(bb);
                if (isBar) { if (c0) { barB = bb; c0 = false; } else barB.Encapsulate(bb); } else { if (b0) { bodyB = bb; b0 = false; } else bodyB.Encapsulate(bb); }
            }
            root.localScale = keep;
            gunDir = c0 ? 1 : Mathf.Sign(barB.center.x - bodyB.center.x);
            float rear = gunDir > 0 ? all.min.x : all.max.x, axisY = c0 ? all.center.y : barB.center.y;
            gunGrip = new Vector3(rear + gunDir * .42f * bodyB.size.x, axisY - .12f, all.center.z);
            gunGuard = new Vector3(rear + gunDir * .40f * bodyB.size.x, bodyB.min.y + .10f, all.center.z);
            if (CohesiveCharacterPolicy.PreserveAuthoredHero) { gunGrip = CohesiveCharacterPolicy.ChibiGunGrip; gunGuard = CohesiveCharacterPolicy.ChibiGunSupport; }
            gunScale = (CohesiveCharacterPolicy.PreserveAuthoredHero ? CohesiveCharacterPolicy.GunLength : GunLen) / Mathf.Max(1e-4f, all.size.x);
            gun = root; gun.localScale = Vector3.one * gunScale;
        }

        void LateUpdate()
        {
            if (model == null || rig == null || !rig.gameObject.activeSelf) return;
            t += Time.deltaTime;
            if (restPose != null) { restPose.enabled = true; restPose.weight = 1; restPose.time = selectedId == "engineer" ? Mathf.Repeat(t, restPose.length) : 0; restPose.speed = 0; anim.Sample(); }
            // (the held walk frame has already been applied by the Animation this frame)
            if (hips != null) hips.localPosition = hipsBase;
            if (chest != null) chest.localScale = Vector3.one * (!CohesiveCharacterPolicy.PreserveAuthoredHero && ReadabilityFx.HeroLook ? ReadabilityFx.UpperMul : 1f);   // the game's HeroLook proportions
            if (head != null) head.localScale = Vector3.one * (!CohesiveCharacterPolicy.PreserveAuthoredHero && ReadabilityFx.HeroLook ? ReadabilityFx.HeadMul / ReadabilityFx.UpperMul : 1f);
            var right = body.right; var up = Vector3.up;
            // weight shift: hips slide and roll onto one leg, spine and head counter so the eyes stay level
            float ws = Mathf.Sin(t * Mathf.PI * 2f / 9.5f); ws = Mathf.Sign(ws) * Mathf.SmoothStep(0, 1, Mathf.Abs(ws));
            if (hips != null) { hips.position += right * (ws * .022f * H / 1.76f) + up * (-Mathf.Abs(ws) * .006f); hips.rotation = Quaternion.AngleAxis(ws * 2.2f, body.forward) * hips.rotation; }
            if (spine != null) spine.rotation = Quaternion.AngleAxis(-ws * 3f, body.forward) * spine.rotation;
            // breathing: the chest lifts and opens a little
            float br = Mathf.Sin(t * Mathf.PI * 2f / 3.6f);
            if (chest != null) chest.rotation = Quaternion.AngleAxis(-br * 1.1f, right) * chest.rotation;
            // spin-up every 8 s: brace (lean into the gun), barrels spin up, hold, wind down
            float c = t % 8f; float brace = c < 3.4f ? Mathf.SmoothStep(0, 1, Mathf.Min(1, c / .35f)) * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((c - 2.6f) / .8f))) : 0;
            if (spine != null && brace > 0) spine.rotation = Quaternion.AngleAxis(brace * 4f, right) * spine.rotation;
            if (head != null) head.rotation = Quaternion.AngleAxis(-brace * 3f + br * .6f, right) * Quaternion.AngleAxis(ws * 1.5f, body.forward) * head.rotation;
            bool spinning = c > .25f && c < 2.0f;
            spinSpeed = Mathf.MoveTowards(spinSpeed, spinning ? 1800f : 0f, Time.deltaTime * (spinning ? 2400f : 1100f));
            spin = (spin + spinSpeed * Time.deltaTime) % 360f;
            if (barrels != null) barrels.localRotation = Quaternion.AngleAxis(spin, Vector3.right);
            if (gun != null)
            {
                float shake = spinSpeed / 1800f;
                var jitter = new Vector3(Mathf.Sin(t * 83f), Mathf.Sin(t * 97f + 1f), 0) * (.004f * shake);
                gun.rotation = body.rotation * Quaternion.Euler(-brace * 2.5f, gunDir > 0 ? -90 : 90, 0);
                var hipBob = hips != null ? (hips.position.y - hips.parent.TransformPoint(hipsBase).y) : 0f;
                var hipGrip = CohesiveCharacterPolicy.PreserveAuthoredHero ? CohesiveCharacterPolicy.SelectedHipGrip : HipGrip;
                var gripWorld = body.TransformPoint(hipGrip * H + jitter) + right * (ws * .012f) + up * hipBob;
                gun.position = gripWorld - gun.rotation * (gunGrip * gunScale);
                ArmIk(hand, gun.TransformPoint(gunGrip));
                ArmIk(handL, gun.TransformPoint(gunGuard));
            }
        }

        // two-bone IK as in Player.ArmIk (palm onto a point, bend plane kept from the pose)
        void ArmIk(Transform h, Vector3 palmTarget)
        {
            if (h == null || h.parent == null || h.parent.parent == null) return;
            Transform c = h, b = c.parent, a = b.parent;
            var tgt = palmTarget - (Palm(h) - h.position);
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude, lcb = (pc - pb).magnitude; if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((tgt - pa).magnitude, Mathf.Abs(lab-lcb)+.0001f, lab+lcb-.0001f);
            float ac_ab0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pc - pa).normalized, (pb - pa).normalized), -1, 1));
            float ba_bc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pa - pb).normalized, (pc - pb).normalized), -1, 1));
            float ac_at0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pc - pa).normalized, (tgt - pa).normalized), -1, 1));
            float ac_ab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2 * lab * lat), -1, 1));
            float ba_bc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2 * lab * lcb), -1, 1));
            var axis0 = Vector3.Cross(pc - pa, pb - pa); if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(pc-pa, body.up); if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(pc-pa, body.forward); axis0.Normalize();
            var axis1 = Vector3.Cross(pc - pa, tgt - pa); if (axis1.sqrMagnitude < 1e-8f) axis1 = axis0; axis1.Normalize();
            var handRot = c.rotation;
            // Compute every local axis before changing either parent, then aim after bending.
            var r0=Quaternion.AngleAxis((ac_ab1-ac_ab0)*Mathf.Rad2Deg,Quaternion.Inverse(a.rotation)*axis0);
            var r1=Quaternion.AngleAxis((ba_bc1-ba_bc0)*Mathf.Rad2Deg,Quaternion.Inverse(b.rotation)*axis0);
            var r2=Quaternion.AngleAxis(ac_at0*Mathf.Rad2Deg,Quaternion.Inverse(a.rotation)*axis1);
            a.localRotation *= r2*r0; b.localRotation *= r1;
            c.rotation = handRot;
        }
        static Vector3 Palm(Transform h)
        {
            if (h.childCount == 0) return h.position;
            var child = h.GetChild(0);
            if (child.name == "RightPalm" || child.name == "LeftPalm") return child.position;
            if (CohesiveCharacterPolicy.UseRebuiltVanguard)
                return h.TransformPoint(new Vector3(0,h.name.EndsWith("RightHand") ? .05753228f : .05346125f,0));
            return (h.position+child.position)*.5f;
        }

        public void Refresh()
        {
            if (selectedId == Prefs.Hero && model != null) return;
            ClearOwnedRig();
            hips = spine = chest = head = hand = handL = gun = barrels = model = body = null;
            anim = null; restPose = null; t = spin = spinSpeed = 0;
            if (Build()) Show(shown);
        }
        public void Show(bool v) { shown = v; if (rig != null) rig.gameObject.SetActive(v); if (cam != null) cam.enabled = v; }
        void ClearOwnedRig()
        {
            if (cam != null) { cam.enabled = false; cam.targetTexture = null; }
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
            if (rig != null) { rig.gameObject.SetActive(false); Destroy(rig.gameObject); rig = null; }
            cam = null;
        }
        void OnDestroy() => ClearOwnedRig();
    }
}
