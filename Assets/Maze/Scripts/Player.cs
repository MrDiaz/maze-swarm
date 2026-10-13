using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Player state (plane coordinates, like the web build) and its visuals: the animated
    // Wanderer model from Resources/Models/hero.glb, or a blocky stand-in if it's missing.
    public class Player : MonoBehaviour
    {
        public Vector2 p, vel, aim = new Vector2(0, -1), dashDir;
        public float hp, maxHp = 100, facing, dashT, dashCd, fireCd, hurtT, shootT;
        public bool shieldUp; // a run's SHIELD card is charged
        public float frozenT;  // frozen in place (cryo vent)
        public float ringReach; readonly Dictionary<float, Mesh> extraRings = new();
        public int weapon;
        // magazines, one per weapon (kept across switches): rounds left, reload time left / total, rounds
        // at reload start (the shotgun's shells go in one by one), and time with nothing in reach
        public int[] ammo, reloadFrom;
        public float[] reloadLeft, reloadDur;
        public float idleT;
        public bool Reloading => reloadLeft[weapon] > 0;

        Transform visual;
        Material ringMat;
        Transform ring, rangeRing;
        Material rangeMat;
        MeshFilter rangeMf;
        Mesh[] rangeMeshes; // one circle per weapon, sized to its reach
        // ammo arc: a segmented curve on the floor around the hero, on the camera side (one cell per shell,
        // or per 5 rounds); dim cells behind, the rounds left (or the reload sweep) in front
        Mesh[] ammoBack; Mesh ammoFront;
        Material ammoBackMat, ammoFrontMat;
        MeshFilter ammoBackMf, ammoFrontMf;
        int ammoKey = -1;
        float ammoCenter;
        const float AmmoR0 = 1.3f, AmmoR1 = 1.72f, AmmoSweep = 140f * Mathf.Deg2Rad, AmmoGap = 3.2f * Mathf.Deg2Rad;
        public bool hasTarget; // auto-fire has something locked inside the range circle

        // hero model
        Animation anim;
        string clipWalk, clipRun, clipShoot, currentClip;
        Transform hips, hand, handL;
        Vector3 hipsBaseLocal;
        float heroYaw;
        bool hasHero;

        // assault rifle from Resources/Models/ar.glb, held in the right hand
        public const float GunLength = 0.78f;       // world length in metres: ~48% of the 1.6 m hero, like an AK on a person (was 1.0)
        const float GunModelLength = 1.905f;        // model length along X (model units)
        static readonly Vector3 GunGrip = new Vector3(-0.43f, -0.13f, 0f);   // pistol grip, Unity model space
        static readonly Vector3 GunMuzzleAr = new Vector3(0.95f, 0.06f, 0f); // muzzle tip, barrel points down +X
        static readonly Vector3 GunHandguardAr = new Vector3(0.3f, -0.04f, 0f); // model space, between grip and muzzle
        Vector3 GunMuzzle = GunMuzzleAr, GunHandguard = GunHandguardAr;     // per gun (the Heavy's minigun measures its own)
        // Iron Vanguard (the Heavy): taller, carries Resources/Models/minigun.glb (barrel cluster in a "Barrels" child)
        const float VanguardHeightMul = 1.1f, MinigunLength = 1.15f; // ~1.25 m gun on a ~1.9 m giant, in game metres
        Transform barrels; float barrelSpin, barrelSpeed;
        // hip carry: the minigun hangs level at the right hip (never raised), both hands are put on it by IK
        bool hipCarry; Vector3 gunGrip; float gunScale;
        public static bool IkDebug;
        static readonly Vector3 HipGrip = new Vector3(0.06f, 0.55f, 0.17f); // rear grip, visual space as a fraction of hero height
        public Transform gun;

        public void Init(Game g) => Init(g.cfg);

        // (the editor/phone Sandbox spawns a hero without a Game)
        public void Init(MazeConfig cfg)
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);

            ring = new GameObject("Ring").transform;
            ring.SetParent(transform, false);
            ring.localPosition = Vector3.up * 0.04f;
            ring.localScale = Vector3.one * 0.85f;
            ring.gameObject.AddComponent<MeshFilter>().sharedMesh = Fx.Ring;
            ringMat = Mats.Fade(new Color(0.28f, 0.7f, 1f, 0.85f));
            var rmr = ring.gameObject.AddComponent<MeshRenderer>(); rmr.sharedMaterial = ringMat; rmr.shadowCastingMode = ShadowCastingMode.Off;

            // auto-fire range circle (world-sized, doesn't turn with the hero)
            rangeRing = new GameObject("RangeRing").transform;
            rangeRing.SetParent(transform, false);
            rangeRing.localPosition = Vector3.up * 0.03f;
            rangeMeshes = new Mesh[cfg.weapons.Length];
            for (int i = 0; i < rangeMeshes.Length; i++) { float R = cfg.weapons[i].reach; rangeMeshes[i] = Fx.RingMesh(R - 0.09f, R, 96); }
            rangeMf = rangeRing.gameObject.AddComponent<MeshFilter>(); rangeMf.sharedMesh = rangeMeshes[0];
            rangeMat = Mats.Fade(new Color(1f, 1f, 1f, 0.22f));
            var rrm = rangeRing.gameObject.AddComponent<MeshRenderer>(); rrm.sharedMaterial = rangeMat; rrm.shadowCastingMode = ShadowCastingMode.Off;

            // the arc faces the camera: plane offset (sin yaw, cos yaw) is world (sin yaw, 0, -cos yaw)
            float yaw = cfg.camera.yaw * Mathf.Deg2Rad;
            ammoCenter = Mathf.Atan2(-Mathf.Cos(yaw), Mathf.Sin(yaw));
            ammoBack = new Mesh[cfg.weapons.Length];
            for (int i = 0; i < ammoBack.Length; i++) { ammoBack[i] = new Mesh(); AmmoArc(ammoBack[i], Cells(cfg.weapons[i]), 1); }
            ammoFront = new Mesh(); ammoFront.MarkDynamic();
            ammoBackMat = Mats.Fade(new Color(0.02f, 0.05f, 0.1f, 0.6f));
            ammoFrontMat = Mats.Fade(Color.white);
            ammoBackMf = AmmoLayer("AmmoBack", ammoBack[0], ammoBackMat, 0.035f);
            ammoFrontMf = AmmoLayer("AmmoFront", ammoFront, ammoFrontMat, 0.045f);

            LoadHero(cfg);
        }

        // the Squad pick (Prefs.Hero): Arctic Sentinel with the AK, or Iron Vanguard with the minigun
        void LoadHero(MazeConfig cfg)
        {
            bool vg = Prefs.Vanguard && Resources.Load<GameObject>("Models/hero_vanguard") != null;
            // the Heavy: Meshy's rig (hero_vanguard_meshy: Walking / Running / run_fast) or the Blender one (Squad toggle)
            string vgModel = Prefs.VanguardMeshy && Resources.Load<GameObject>("Models/hero_vanguard_meshy") != null ? "Models/hero_vanguard_meshy" : "Models/hero_vanguard";
            bool sc = !vg && Prefs.Scout && Resources.Load<GameObject>("Models/hero_scout") != null;   // the Scout: Meshy's rig (Run_and_Shoot / Walking / Running)
            var prefab = Resources.Load<GameObject>(vg ? vgModel : sc ? "Models/hero_scout" : "Models/hero");
            if (prefab != null)
            {
                var hero = Instantiate(prefab); Mats.ConvertGltf(hero);
                SetupHero(hero, cfg.player.modelHeight * (vg ? VanguardHeightMul : 1));
                if (vg) AttachMinigun(); else AttachGun();
            }
            else { Debug.LogWarning("Maze: Resources/Models/hero not found, using the stand-in player."); BuildStandIn(); }
        }

        // swap the hero after a Squad pick (keeps the ring, range circle and ammo arc)
        public void RebuildHero(MazeConfig cfg)
        {
            for (int i = visual.childCount - 1; i >= 0; i--) Destroy(visual.GetChild(i).gameObject);
            visual.DetachChildren();
            anim = null; clipWalk = clipRun = clipShoot = currentClip = null; hips = hand = handL = null; gun = null; barrels = null; hasHero = false; hipCarry = false;
            GunMuzzle = GunMuzzleAr; GunHandguard = GunHandguardAr;
            LoadHero(cfg);
        }

        MeshFilter AmmoLayer(string name, Mesh m, Material mat, float y)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * y;
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return mf;
        }

        static int Cells(Weapon w) => w.magSize <= 0 ? 1 : w.magSize <= 10 ? w.magSize : Mathf.CeilToInt(w.magSize / 5f);

        // `cells` annular sectors with small gaps across AmmoSweep, filled left to right up to `fill` (0..1)
        void AmmoArc(Mesh m, int cells, float fill)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            float cell = AmmoSweep / cells, start = ammoCenter + AmmoSweep * 0.5f; // first round on the left
            float filled = Mathf.Clamp01(fill) * cells;
            for (int c = 0; c < cells && c < filled; c++)
            {
                float a0 = start - c * cell - AmmoGap * 0.5f;
                float a1 = start - (c + Mathf.Min(1, filled - c)) * cell + AmmoGap * 0.5f;
                if (a1 >= a0) continue;
                int seg = 4;
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)seg);
                    v.Add(new Vector3(Mathf.Cos(a) * AmmoR0, 0, Mathf.Sin(a) * AmmoR0));
                    v.Add(new Vector3(Mathf.Cos(a) * AmmoR1, 0, Mathf.Sin(a) * AmmoR1));
                    if (i == seg) continue;
                    int k = v.Count - 2; // angle decreases along the cell: RingMesh's winding with the ends swapped
                    t.Add(k + 2); t.Add(k); t.Add(k + 3);
                    t.Add(k + 3); t.Add(k); t.Add(k + 1);
                }
            }
            m.Clear(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        }

        void UpdateAmmo(MazeConfig cfg)
        {
            var w = cfg.weapons[weapon];
            ammoBackMf.gameObject.SetActive(w.magSize > 0);
            ammoFrontMf.gameObject.SetActive(w.magSize > 0);
            if (w.magSize <= 0) return;
            ammoBackMf.sharedMesh = ammoBack[weapon];
            bool reloading = reloadLeft[weapon] > 0;
            // the shotgun shows its shells going in; magazines show the reload sweeping round
            float fill = reloading && !w.perShell ? 1 - reloadLeft[weapon] / reloadDur[weapon] : ammo[weapon] / (float)w.magSize;
            int key = weapon * 100000 + (reloading ? 50000 : 0) + Mathf.RoundToInt(fill * 1000);
            if (key != ammoKey) { ammoKey = key; AmmoArc(ammoFront, Cells(w), fill); }
            Color c = Color.Lerp(w.color, Color.white, 0.2f);
            if (reloading) c = new Color(0.45f, 0.88f, 1f, 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 12));
            else if (fill <= 0.25f) c = Color.Lerp(c, new Color(1f, 0.25f, 0.2f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10));
            c.a = reloading ? c.a : 0.92f;
            ammoFrontMat.SetColor("_BaseColor", c);
        }

        void BuildStandIn()
        {
            var white = Mats.Lit(new Color(0.97f, 0.97f, 0.98f), null, null, 0.4f);
            var dark = Mats.Lit(new Color(0.11f, 0.11f, 0.14f), null, null, 0.5f);
            Cube(new Vector3(0, 0.75f, 0), new Vector3(0.62f, 0.6f, 0.42f), white);
            Cube(new Vector3(0, 1.28f, 0), new Vector3(0.46f, 0.42f, 0.42f), white);
            Cube(new Vector3(-0.15f, 0.22f, 0), new Vector3(0.2f, 0.45f, 0.24f), white);
            Cube(new Vector3(0.15f, 0.22f, 0), new Vector3(0.2f, 0.45f, 0.24f), white);
            Cube(new Vector3(0.24f, 0.92f, 0.38f), new Vector3(0.13f, 0.13f, 0.75f), dark);
        }
        void Cube(Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(visual, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        float heroHeight = 1.6f;
        void SetupHero(GameObject model, float height)
        {
            heroHeight = height;
            model.transform.SetParent(visual, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            // normalize height and stand on the floor
            var b = new Bounds(); bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            if (!first && b.size.y > 0.01f)
            {
                float s = height / b.size.y;
                model.transform.localScale *= s;
                model.transform.localPosition = new Vector3(0, -(b.min.y - visual.position.y) * s, 0);
            }
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                if (hips == null && t.name.ToLower().Contains("hips")) hips = t;
                string n = t.name.ToLower();
                if (hand == null && n.Contains("righthand")) hand = t;
                if (handL == null && n.Contains("lefthand")) handL = t;
            }
            if (hips != null) hipsBaseLocal = hips.localPosition;

            anim = model.GetComponentInChildren<Animation>();
            if (anim != null)
            {
                foreach (AnimationState st in anim)
                {
                    st.wrapMode = WrapMode.Loop;
                    string n = st.name.ToLower();
                    if (n.Contains("shoot")) clipShoot = st.name;
                    else if (n.Contains("run") && (clipRun == null || n.EndsWith("running"))) clipRun = st.name; // "Running" over the alternate "Run_02"
                    else if (n.Contains("walk")) clipWalk = st.name;
                }
            }
            else Debug.LogWarning("Maze: hero has no legacy Animation component. Run Maze > Setup Project (sets the glTF importer to Legacy animation).");
            hasHero = true;
        }

        // Fit the rifle once against the shoot pose: grip in the palm, muzzle along the hero's forward.
        void AttachGun()
        {
            if (hand == null) return;
            var prefab = Resources.Load<GameObject>("Models/ar");
            if (prefab == null) { Debug.LogWarning("Maze: Resources/Models/ar not found, the hero is unarmed."); return; }
            var go = Instantiate(prefab); go.name = "Gun";
            Mats.ConvertGltf(go);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

            AnimationState st = anim != null && clipShoot != null ? anim[clipShoot] : null;
            if (st != null) { st.enabled = true; st.weight = 1; st.normalizedTime = 0.3f; anim.Sample(); }
            var palm = Palm(hand);
            float s = GunLength / GunModelLength;
            // barrel level along the hero's forward (the left hand is put on the handguard by IK, see LeftHandIk)
            var rot = Quaternion.LookRotation(visual.forward, Vector3.up) * Quaternion.Euler(0, -90, 0); // model +X (muzzle) onto forward
            gun = go.transform;
            gun.SetParent(hand, false);
            gun.rotation = rot;
            gun.localScale = Vector3.one * (s / hand.lossyScale.x);
            gun.position = palm - rot * (GunGrip * s);
            if (st != null) { st.enabled = false; anim.Stop(); }
            currentClip = null;
            gunBasePos = gun.localPosition; gunBaseRot = gun.localRotation;
        }

        // The Heavy's minigun: grip, front handle and muzzle are measured from the model (body + "Barrels" child),
        // then it's fitted like the AK (grip in the right palm, muzzle along the hero's forward).
        void AttachMinigun()
        {
            if (hand == null) return;
            var prefab = Resources.Load<GameObject>("Models/minigun");
            if (prefab == null) { AttachGun(); return; }
            var go = Instantiate(prefab); go.name = "Gun";
            Mats.ConvertGltf(go);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            barrels = null; foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name == "Barrels") barrels = t;
            Bounds all = new Bounds(), bodyB = new Bounds(), barB = new Bounds(); bool a0 = true, b0 = true, c0 = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mb = mf.sharedMesh.bounds; var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var bb = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
                for (int k = 0; k < 8; k++) bb.Encapsulate(m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) * 2 - 1, (k & 2) - 1, (k & 4) / 2f - 1))));
                bool isBar = barrels != null && mf.transform.IsChildOf(barrels);
                if (a0) { all = bb; a0 = false; } else all.Encapsulate(bb);
                if (isBar) { if (c0) { barB = bb; c0 = false; } else barB.Encapsulate(bb); }
                else { if (b0) { bodyB = bb; b0 = false; } else bodyB.Encapsulate(bb); }
            }
            float dir = c0 ? 1 : Mathf.Sign(barB.center.x - bodyB.center.x);   // muzzle end along model X
            float rear = dir > 0 ? all.min.x : all.max.x, axisY = c0 ? all.center.y : barB.center.y;
            var grip = new Vector3(rear + dir * 0.42f * bodyB.size.x, axisY - 0.12f, all.center.z);
            GunHandguard = new Vector3(rear + dir * 0.52f * bodyB.size.x, bodyB.max.y - 0.04f, all.center.z);
            GunMuzzle = new Vector3(dir > 0 ? all.max.x : all.min.x, axisY, all.center.z);
            gunScale = MinigunLength / all.size.x; gunGrip = grip;
            gun = go.transform;
            gun.SetParent(visual, false);
            gun.localRotation = Quaternion.Euler(0, dir > 0 ? -90 : 90, 0); // muzzle onto the hero's forward, level
            gun.localScale = Vector3.one * (gunScale / Mathf.Max(1e-4f, visual.lossyScale.x));
            hipCarry = true;
            clipShoot = null; // the Sentinel's Run_and_Shoot shoulders the gun: the Heavy keeps walking/running clips while firing
            PlaceHipGun(0);
        }

        // the minigun level at the right hip, riding the hips' bob, with a small recoil shake (no raise)
        void PlaceHipGun(float bob)
        {
            float h = heroHeight;
            var rot = gun.rotation;
            float tt = Time.time * 90f; var shake = recoil * 0.025f * new Vector3(Mathf.Sin(tt), Mathf.Sin(tt * 1.37f + 1), -1.5f); // no Random: keeps the sims deterministic
            var gripWorld = visual.TransformPoint(new Vector3(HipGrip.x * h, HipGrip.y * h, HipGrip.z * h) + shake) + Vector3.up * bob;
            gun.position = gripWorld - rot * (gunGrip * gunScale);
        }

        // recoil: the gun kicks back along its barrel with a muzzle rise, the hand follows a bit and the
        // shotgun rocks the whole upper body back (per flash style: AK, SMG, shotgun). The shotgun then
        // pumps: the gun racks back and forward in time with the pump sound (0.34 s after the shot; the
        // model has no separate fore-end). Scaled time, so pause / slow motion freeze it.
        static readonly float[] KickBack = { 0.045f, 0.025f, 0.08f }, KickRise = { 3f, 1.5f, 11f }, KickRock = { 1.2f, 0.6f, 7f };
        Vector3 gunBasePos; Quaternion gunBaseRot;
        float recoil, pumpT = -1; int kickStyle;
        const float PumpStart = 0.34f;
        public void Kick(int style, bool pump = true) { recoil = 1; kickStyle = style; if (style == 2 && pump) pumpT = 0; }
        public void Rack() => pumpT = PumpStart; // the reload's closing rack: straight into the stroke

        // pump stroke offset (m, back along the barrel): back 0.36-0.46 s, held, forward 0.58-0.68 s
        static float PumpStroke(float t)
        {
            if (t < 0.36f || t > 0.68f) return 0;
            if (t < 0.46f) return 0.09f * Mathf.SmoothStep(0, 1, (t - 0.36f) / 0.1f);
            if (t < 0.58f) return 0.09f;
            return 0.09f * (1 - Mathf.SmoothStep(0, 1, (t - 0.58f) / 0.1f));
        }

        // Two-bone IK on the left arm: the support hand onto the AK's handguard (or the minigun's front handle), after
        // the clip and the recoil (the Arctic Sentinel's clips hold the hands ~17 cm apart, i.e. on the magazine well)
        void LeftHandIk() { if (gun != null) ArmIk(handL, gun.TransformPoint(GunHandguard)); }

        // two-bone IK: put this hand's palm on a point (shoulder-elbow-wrist chain, bend plane kept from the pose)
        void ArmIk(Transform hand, Vector3 palmTarget)
        {
            if (hand == null || hand.parent == null || hand.parent.parent == null) return;
            Transform c = hand, b = c.parent, a = b.parent;
            var palmOff = Palm(hand) - hand.position;
            var t = palmTarget - palmOff;
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude, lcb = (pc - pb).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((t - pa).magnitude, 0.01f, lab + lcb - 0.001f);
            float ac_ab0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pc - pa).normalized, (pb - pa).normalized), -1, 1));
            float ba_bc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pa - pb).normalized, (pc - pb).normalized), -1, 1));
            float ac_at0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pc - pa).normalized, (t - pa).normalized), -1, 1));
            float ac_ab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2 * lab * lat), -1, 1));
            float ba_bc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2 * lab * lcb), -1, 1));
            var axis0 = Vector3.Cross(pc - pa, pb - pa); if (axis0.sqrMagnitude < 1e-8f) axis0 = visual.right; axis0.Normalize();
            var axis1 = Vector3.Cross(pc - pa, t - pa); if (axis1.sqrMagnitude < 1e-8f) axis1 = axis0; axis1.Normalize();
            var handRot = c.rotation;
            var r0 = Quaternion.AngleAxis((ac_ab1 - ac_ab0) * Mathf.Rad2Deg, Quaternion.Inverse(a.rotation) * axis0);
            var r1 = Quaternion.AngleAxis((ba_bc1 - ba_bc0) * Mathf.Rad2Deg, Quaternion.Inverse(b.rotation) * axis0);
            var r2 = Quaternion.AngleAxis(ac_at0 * Mathf.Rad2Deg, Quaternion.Inverse(a.rotation) * axis1);
            a.localRotation = a.localRotation * (r0 * r2);
            b.localRotation = b.localRotation * r1;
            c.rotation = handRot; // keep the hand's own orientation
        }

        static Vector3 Palm(Transform h) => h.childCount > 0 ? (h.position + h.GetChild(0).position) * 0.5f : h.position;
        public Transform HandL => handL;

        public Vector3 MuzzleWorld => gun != null ? gun.TransformPoint(GunMuzzle) : Vector3.zero;
        public Transform Hand => hand;
        public Transform VisualRoot => visual;
        public void PoseShoot() { if (anim != null && clipShoot != null) { var st = anim[clipShoot]; st.enabled = true; st.weight = 1; st.normalizedTime = 0.3f; anim.Sample(); } }

        void Play(string clip, float speed, bool hold = false)
        {
            if (anim == null || clip == null) return;
            if (currentClip != clip) { anim.CrossFade(clip, 0.15f); currentClip = clip; }
            var st = anim[clip];
            st.speed = speed;
            if (hold) st.normalizedTime = 0;
        }

        public void Visuals(float dt, MazeConfig cfg, Vector2 moveVel, bool dead)
        {
            transform.position = MazeMap.ToWorld(p);
            float sp = moveVel.magnitude;
            bool shooting = shootT > 0, moving = sp > 0.6f;
            float moveYaw = Mathf.Atan2(moveVel.x, moveVel.y);
            float want = (shooting || !moving || !hasHero) ? facing : moveYaw;
            heroYaw += Mathf.DeltaAngle(heroYaw * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad * Mathf.Min(1, dt * 14);
            visual.rotation = Quaternion.Euler(0, MazeMap.YawDeg(heroYaw), 0);

            if (hasHero)
            {
                float rate = sp / cfg.player.speed;
                float back = shooting && moving && Mathf.Cos(moveYaw - facing) < -0.3f ? -1 : 1;
                if (shooting && clipShoot != null) Play(clipShoot, moving ? back * Mathf.Max(0.6f, rate) : 0f);
                else if (sp > cfg.player.speed * 0.6f) Play(clipRun ?? clipWalk, Mathf.Max(0.7f, rate) * 1.05f);
                else if (moving) Play(clipWalk ?? clipRun, Mathf.Max(0.6f, sp / (cfg.player.speed * 0.45f)));
                else Play(clipWalk ?? clipRun, 0f, true); // no idle clip: hold a frame
            }

            var rc = dashCd <= 0 ? new Color(0.28f, 0.7f, 1f, 0.85f) : new Color(0.62f, 0.7f, 0.84f, 0.85f);
            if (hurtT > 0) rc = Color.Lerp(rc, new Color(1f, 0.2f, 0.2f, 0.95f), Mathf.Clamp01(hurtT * 4));
            ringMat.SetColor("_BaseColor", rc);
            if (ringReach > 0 && Mathf.Abs(ringReach - cfg.weapons[weapon].reach) > 0.05f)
            {   // a reach that isn't the weapon's own: its ring, built once per radius
                if (!extraRings.TryGetValue(ringReach, out var m)) extraRings[ringReach] = m = Fx.RingMesh(ringReach - 0.09f, ringReach, 96);
                rangeMf.sharedMesh = m;
            }
            else rangeMf.sharedMesh = rangeMeshes[weapon];
            rangeMat.SetColor("_BaseColor", hasTarget ? new Color(1f, 0.55f, 0.35f, 0.35f) : new Color(1f, 1f, 1f, 0.2f));
            ring.localScale = Vector3.one * (dashT > 0 ? 1.1f : 0.85f);
            if (ammo != null) UpdateAmmo(cfg);
        }

        void LateUpdate()
        {
            // strip root motion: the game moves the player, so pin the hips horizontally
            float bob = 0;
            if (hips != null)
            {
                var basePos = hips.parent != null ? hips.parent.TransformPoint(hipsBaseLocal) : hipsBaseLocal;
                var d = hips.position - basePos;
                hips.position -= new Vector3(d.x, 0, d.z);
                bob = d.y;
            }
            if (hipCarry && gun != null)
            {
                recoil *= Mathf.Exp(-Time.deltaTime * 24f);
                PlaceHipGun(bob);
                ArmIk(hand, gun.TransformPoint(gunGrip));   // right hand on the rear grip / trigger
                if (IkDebug) Debug.Log($"Maze: hip IK right palm {(Palm(hand) - gun.TransformPoint(gunGrip)).magnitude:F2} m off, left {(Palm(handL) - gun.TransformPoint(GunHandguard)).magnitude:F2} m off (before left IK)");
            }
            else if (gun != null)
            {
                float dt = Time.deltaTime;
                recoil *= Mathf.Exp(-dt * (kickStyle == 2 ? 14f : 24f)); // the shotgun's kick settles slower
                if (pumpT >= 0) { pumpT += dt; if (pumpT > 0.7f) pumpT = -1; }
                float pumpBack = pumpT >= 0 ? PumpStroke(pumpT) : 0;
                float back = KickBack[kickStyle] * recoil + pumpBack;
                // upper body rocks back (rotate the visual about the hero's right axis, pivot near the hips)
                float rock = KickRock[kickStyle] * recoil;
                if (rock > 0.01f) visual.rotation = Quaternion.AngleAxis(-rock, visual.right) * visual.rotation;
                hand.position -= visual.forward * (back * 0.4f); // the arm gives a little
                gun.localPosition = gunBasePos + gunBaseRot * Vector3.left * (back / hand.lossyScale.x);
                gun.localRotation = gunBaseRot * Quaternion.Euler(0, 0, KickRise[kickStyle] * recoil + pumpBack * 30f);
            }
            LeftHandIk();
            if (IkDebug && hipCarry && handL != null) Debug.Log($"Maze: hip IK left palm after IK {(Palm(handL) - gun.TransformPoint(GunHandguard)).magnitude:F2} m off, right {(Palm(hand) - gun.TransformPoint(gunGrip)).magnitude:F2}");
            if (barrels != null) // the minigun's barrels spin up while firing and wind down after
            {
                barrelSpeed = Mathf.MoveTowards(barrelSpeed, recoil > 0.05f ? 1800f : 0f, Time.deltaTime * (recoil > 0.05f ? 5400f : 1200f));
                barrelSpin = (barrelSpin + barrelSpeed * Time.deltaTime) % 360f;
                barrels.localRotation = Quaternion.AngleAxis(barrelSpin, Vector3.right);
            }
            // the muzzle flash rides on the gun
            if (GunFx.I != null)
                GunFx.I.FollowMuzzle(gun != null ? gun.TransformPoint(GunMuzzle) : MazeMap.ToWorld(p + new Vector2(Mathf.Sin(facing), Mathf.Cos(facing)) * 0.9f, 0.95f));
        }

        public void SetVisible(bool v) => gameObject.SetActive(v);

        // readability pass (rim light + contrast + faint outline) for comparison renders; off in the game for now
        public static bool ReadabilityPass;
        public void ApplyReadability()
        {
            if (!ReadabilityPass || !hasHero || visual == null) return;
            var sh = Shader.Find("Maze/HeroRim");
            if (sh == null) { Debug.LogWarning("Maze: Maze/HeroRim shader missing"); return; }
            var rim = new Material(sh) { name = "HeroRim" };
            var line = new Material(sh) { name = "HeroOutline" }; line.SetFloat("_Outline", 1);
            foreach (var r in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = r.sharedMaterials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    // slightly stronger material contrast: a touch darker albedo, deeper normal detail
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * new Color(0.86f, 0.88f, 0.92f, 1));
                    if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1.4f);
                }
                var list = new System.Collections.Generic.List<Material>(mats) { rim, line };
                r.sharedMaterials = list.ToArray();
            }
        }
    }
}
