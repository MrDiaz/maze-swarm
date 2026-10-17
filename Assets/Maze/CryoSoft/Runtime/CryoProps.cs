using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // The Meshy Cryo objects (Assets/Maze/CryoProps/Resources/CryoProps/<slug>.glb + _base / _normal maps) placed on a
    // CryoSoft kit room's anchors (Resources/CryoSoft/CryoSoftRoom_<id>_props.json, written by the Blender kit). The anchors
    // sit on island / wall cells that are already solid: nothing here touches walkable cells or collision. Each prop is
    // scaled uniformly to fill its anchor's footprint (capped in height). Materials use the room's own shader
    // (Maze/CryoSoftSurface + the prop's maps), so CryoSoftSceneInfo's preset-3 lighting and glow apply to them too.
    // Motion: the hero's turrets (CryoHeroTurret), the blast door opens when the floor's goal completes, the cart wheels /
    // pump rotor / valve idle.
    public static class CryoProps
    {
        [System.Serializable] class Anchor { public string prop, name; public float x, y, z, yaw, fw, fd, maxh; }
        [System.Serializable] class Layout { public string layout; public float door_x; public Anchor[] anchors; }
        static readonly Dictionary<string, Material> mats = new();
        public const float RealCap = 1.3f;   // (2026-10-15 polish) props were fit to their islands (2.6-3.4 m tall); now at most 1.3 x real size

        public static int Spawn(GameObject room, string id)
        {
            if (FloorEditing.FloorRuntime.TryCurrent(out _)) return FloorEditing.FloorRuntime.Spawn(room.transform);
            var ta = Resources.Load<TextAsset>("CryoSoft/CryoSoftRoom_" + id + "_props"); if (ta == null) return 0;
            var lay = JsonUtility.FromJson<Layout>(ta.text); if (lay?.anchors == null) return 0;
            // the room root is mirrored (-1, 1, 1: the FBX handedness fix); this container cancels it so anchors are world metres
            var box = new GameObject("Cryo objects").transform; box.SetParent(room.transform, false); box.localScale = new Vector3(-1, 1, 1);
            int n = 0; var placed = new List<(string, Transform)>();
            CryoDressing.NewFloor();
            foreach (var a in lay.anchors)
            {
                // (2026-10-16, Manuel: "the turrets can sit by themselves, not tied to an island") ReadabilityFx.StandaloneTurrets:
                // the turret moves to an open-floor emplacement near its old post; that one cell becomes solid (before the flow fields)
                if ((a.prop == "cryo-gun-turret" || a.prop == "cryo-flamethrower-turret") && ReadabilityFx.StandaloneTurrets && CryoDressing.TurretSpot(a.x, a.z, out var tx, out var tz)) { a.x = tx; a.z = tz; a.y = 0f; }
                var src = Resources.Load<GameObject>("CryoProps/" + a.prop); if (src == null) { Debug.LogWarning("CryoProps: missing " + a.prop); continue; }
                var go = Object.Instantiate(src, box, false); go.name = a.name;
                go.transform.localPosition = new Vector3(a.x, a.y, a.z); go.transform.localRotation = Quaternion.Euler(0, a.yaw, 0);
                var rs = go.GetComponentsInChildren<Renderer>();
                if (a.fw > 0 && a.fd > 0 && rs.Length > 0)
                {   // fill the footprint (world-axis bounds at the anchor's yaw), no taller than maxh
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float s = Mathf.Min(a.fw / Mathf.Max(.01f, b.size.x), a.fd / Mathf.Max(.01f, b.size.z), (a.maxh > 0 ? a.maxh : 3.4f) / Mathf.Max(.01f, b.size.y));
                    if (a.prop != "cryo-blast-door") s = Mathf.Min(s, RealCap);   // the GLBs are in real metres: no bigger than RealCap x life size beside the ~1.8 m hero
                    if (Application.isEditor) Debug.Log($"CryoProps {a.name}: scale {s:0.00}, height {b.size.y * s:0.00} m");
                    go.transform.localScale *= s;
                }
                foreach (var r in rs)
                {
                    var sm = r.sharedMaterials; for (int i = 0; i < sm.Length; i++) sm[i] = Mat(a.prop, sm[i] != null ? sm[i].name : ""); r.sharedMaterials = sm;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                }
                if (a.prop == "cryo-gun-turret" || a.prop == "cryo-flamethrower-turret") go.AddComponent<CryoHeroTurret>().Init(a.prop == "cryo-flamethrower-turret");
                else go.AddComponent<CryoPropMotion>().Init(a.prop);
                placed.Add((a.prop, go.transform)); n++;
            }
            CryoDressing.Dress(box, placed, id, lay.door_x);   // (2026-10-16: islands given a purpose, the coolant motif, grounding; ReadabilityFx.CryoDressing)
            return n;
        }

        // the prop's own textured material, or a room palette material for the cut caps and the modelled wheels
        internal static Material Mat(string prop, string gltfName)
        {
            string key = gltfName.StartsWith("Cap_Navy") ? "Soft_Navy" : gltfName.StartsWith("Wheel_Rim") ? "Soft_Navy" : gltfName.StartsWith("Wheel_Hub") ? "Soft_Cyan"
                       : gltfName.StartsWith("Wheel_Tyre") ? "Prop_Tyre" : "Prop_" + prop;
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Maze/CryoSoftSurface")) { name = key };
            if (key == "Prop_Tyre") m.SetColor("_BaseColor", new Color(.10f, .12f, .18f));
            else if (key.StartsWith("Prop_"))
            {
                m.SetTexture("_BaseMap", Resources.Load<Texture2D>("CryoProps/" + prop + "_base"));
                var nrm = Resources.Load<Texture2D>("CryoProps/" + prop + "_normal");
                if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_NormalStrength", .65f); }
                m.SetColor("_BaseColor", PropTint);
            }
            m.SetFloat("_UseVertexAO", 0); m.SetFloat("_Gloss", .35f);
            mats[key] = m; return m;
        }
        // Meshy's whites are brighter / warmer than the room's #d3dfe4 plastic: a slight cool tint so they sit in the palette
        public static Color PropTint = new Color(.86f, .90f, .95f);

        // a model's part by name
        public static Transform Part(Transform root, string name) { foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
    }

    // the blast door, cart wheels, pump rotor and valve wheel
    public class CryoPropMotion : MonoBehaviour
    {
        Transform leafL, leafR, rotor, valve; readonly List<Transform> wheels = new(); readonly List<Quaternion> wheel0 = new();
        Quaternion rotor0, valve0; Vector3 leafL0, leafR0, rotorAxis = Vector3.forward; float open, idle; static Game game;
        public static bool DebugOpen;   // (captures)
        public static Vector3? DebugTarget;   // (checks) the turrets aim and spin at this world point, in or out of a run
        public float Open => open;
        public string Prop { get; private set; }
        public void Init(string prop)
        {
            Prop = prop;
            leafL = CryoProps.Part(transform, "leaf_left"); leafR = CryoProps.Part(transform, "leaf_right");
            rotor = CryoProps.Part(transform, "pump_rotor"); valve = CryoProps.Part(transform, "valve_wheel");
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("wheel_")) { wheels.Add(t); wheel0.Add(t.localRotation); }
            if (rotor) { rotor0 = rotor.localRotation; var mf = rotor.GetComponent<MeshFilter>(); if (mf) { var e = mf.sharedMesh.bounds.extents; rotorAxis = e.x < e.y && e.x < e.z ? Vector3.right : e.y < e.z ? Vector3.up : Vector3.forward; } }   // a disc: it spins about its thin axis
            if (valve) valve0 = valve.localRotation;
            if (leafL) leafL0 = leafL.localPosition; if (leafR) leafR0 = leafR.localPosition;
            idle = Random.value * 10f;
        }
        void Update()
        {
            float dt = Time.deltaTime; idle += dt;
            if (game == null) game = FindAnyObjectByType<Game>();
            if (leafL != null && leafR != null)
            {   // the floor's goal done: the leaves slide 0.391 (Meshy units) into the pillars, outward along the frame's X
                bool goal = DebugOpen || (game != null && game.run && game.GoalProgress >= 0.999f);
                open = Mathf.MoveTowards(open, goal ? 1f : 0f, dt / 1.2f); float u = open * open * (3 - 2 * open);
                leafL.localPosition = leafL0 + Vector3.right * (Mathf.Sign(leafL0.x) * .391f * u);
                leafR.localPosition = leafR0 + Vector3.right * (Mathf.Sign(leafR0.x) * .391f * u);
            }
            for (int i = 0; i < wheels.Count; i++) wheels[i].localRotation = wheel0[i] * Quaternion.AngleAxis(Mathf.Sin(idle * .6f + i) * 25f, Vector3.right);   // a parked cart: a gentle rock
            if (rotor) rotor.localRotation = rotor0 * Quaternion.AngleAxis(idle * 90f, rotorAxis);
            if (valve) valve.localRotation = valve0 * Quaternion.AngleAxis(Mathf.Sin(idle * .35f) * 40f, Vector3.up);
        }
    }

    // The hero's turrets on the Cryo floors: the escape floors' CellTurrets rules (stand within 1.5 m + the hero's radius for
    // 2.5 s to bring it online; 25 s online, 200 HP the swarm chews, then 15 s cooling; grey / cyan / red ring;
    // "TURRET ONLINE"). The gun: 12 m, 6 shots/s, 1 RH a shot at the nearest enemy (CellTurrets' numbers), the gunfire
    // kit's rifle flash and tracers at its barrel tips, the barrels spinning while it fires. The flamethrower: a 5 m, +-20
    // degree cone at the nearest enemy, 4 RH/s to every enemy in it (10 ticks/s), VfxKit's fire flipbook as the jet.
    // Idle: a slow scan sweep.
    public class CryoHeroTurret : MonoBehaviour
    {
        public static readonly List<CryoHeroTurret> Live = new();
        public static int Shots, FlameTicks, Activations, Kills;   // (checks)
        public bool flame; public float cap, owned, cool, hp; float shot, idle, spin;
        Transform yaw, pitch, barrels; Quaternion yaw0, pitch0; Vector3 restFwd, barrelAxis = Vector3.forward, muzzleLocal; float pitchSign = 1;
        Vector2 at; Transform ring; Material ringM; Vector3 aimW; static Game game;
        GunfireKit kit; Transform kitAnchor;   // the gun: its own copy of the hero rifle's gunfire kit (flash star, flame cone, glow), at the barrel tips
        const float CapTime = 2.5f, Online = 20f, Cooling = 15f, GunRange = 12f, GunRate = 6f, FlameRange = 5f, FlameCone = 20f, FlameRH = 4f;
        public bool Idle => owned <= 0 && cool <= 0;
        public Vector2 At => at;

        public void Init(bool isFlame)
        {
            flame = isFlame; Live.Add(this);
            yaw = CryoProps.Part(transform, "yaw_head"); pitch = CryoProps.Part(transform, "pitch_assembly"); barrels = CryoProps.Part(transform, "barrel_cluster");
            yaw0 = yaw.localRotation; pitch0 = pitch.localRotation;
            // the muzzle: the far end of the barrels (gun) or of the head along its forward (flamethrower), found from the meshes
            var tip = barrels != null ? barrels : pitch; var mf = tip.GetComponent<MeshFilter>(); var bb = mf.sharedMesh.bounds;
            var cW = tip.TransformPoint(bb.center); var fw = (cW - yaw.position); fw.y = 0;
            if (barrels != null) { var c = bb.center; barrelAxis = Mathf.Abs(c.z) >= Mathf.Abs(c.x) ? Vector3.forward * Mathf.Sign(c.z == 0 ? 1 : c.z) : Vector3.right * Mathf.Sign(c.x); muzzleLocal = c + Vector3.Scale(barrelAxis, bb.extents); }
            else { var fl = tip.InverseTransformDirection(fw.normalized); fl = new Vector3(Mathf.Round(fl.x), 0, Mathf.Round(fl.z)); if (fl == Vector3.zero) fl = Vector3.forward; muzzleLocal = bb.center + Vector3.Scale(fl, bb.extents); }
            restFwd = yaw.parent.InverseTransformDirection(fw.normalized);
            // which way a positive local-X pitch turns the muzzle (import axes): measured, not assumed
            var m0 = MuzzleW(); pitch.localRotation = pitch0 * Quaternion.AngleAxis(5f, Vector3.right); var m1 = MuzzleW(); pitch.localRotation = pitch0;
            pitchSign = m1.y > m0.y ? 1f : -1f;
            // the hero stands at the island edge: the activation point is the turret's own cell, the ring on the walkable side
            var w = transform.position; at = new Vector2(w.x, -w.z);
            ring = new GameObject("TurretRing").transform; ring.SetParent(transform.parent, true);
            ring.gameObject.AddComponent<MeshFilter>().sharedMesh = Fx.Ring; var mr = ring.gameObject.AddComponent<MeshRenderer>(); ringM = Mats.Glow(new Color(.6f, .6f, .6f, .5f)); mr.sharedMaterial = ringM;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            ring.position = new Vector3(w.x, 0.06f, w.z); ring.localScale = Vector3.one * 2.4f;
            idle = Random.value * 10f;
            if (Application.isEditor) Debug.Log($"CryoHeroTurret {name}: pivot {transform.position}, muzzle {MuzzleW()}, pitch sign {pitchSign}, rest fwd {restFwd}");
        }
        void OnDestroy() { Live.Remove(this); CryoTurretFx.Tick(); if (kitAnchor != null) Destroy(kitAnchor.gameObject); if (ringM != null) Destroy(ringM); }
        Vector3 MuzzleW() => (barrels != null ? barrels : pitch).TransformPoint(muzzleLocal);

        // the rules run on the game's step (Game.TickRun -> StepAll), so the fixed-step sims see them; the look runs per frame
        public static bool Off;   // (sims) SIM_NOTURRETS: the turrets stay inert, the bot ignores them
        public static void StepAll(Game g, float dt) { if (Off) return; for (int i = Live.Count - 1; i >= 0; i--) if (Live[i] != null) Live[i].Step(g, dt); }
        Enemy tgt; Color ringC = new Color(.6f, .6f, .6f, .5f);
        void Step(Game g, float dt)
        {
            game = g; tgt = null; if (g.player == null) return;
            float dist = (g.player.p - at).magnitude, range = flame ? FlameRange : GunRange;
            if (cool > 0) { cool -= dt; ringC = new Color(1f, .25f, .2f, .6f); }
            else if (owned > 0)
            {
                owned -= dt; ringC = new Color(.3f, .85f, 1f, .8f);
                float bd = range * range;
                foreach (var e in g.swarm.enemies)
                {
                    if (e.dead || e.spawnT < .5f) continue; float dd = (e.p - at).sqrMagnitude;
                    if (dd < bd) { bd = dd; tgt = e; }
                    if (dd < (e.r + 1.1f) * (e.r + 1.1f)) hp -= e.damage * dt * 1.2f;   // the swarm chews it
                }
                if (tgt != null) Fire(g, tgt, dt);
                if (owned <= 0 || hp <= 0) { owned = 0; cool = Cooling; tgt = null; }
            }
            else
            {
                bool near = dist < 1.5f + g.cfg.player.radius + 1.0f;   // (+1 m: the turret stands on its island's edge cell, the hero one step off it)
                cap = near ? cap + dt : Mathf.Max(0, cap - dt);
                float k = cap / CapTime;
                ringC = Color.Lerp(new Color(.6f, .6f, .6f, .5f), new Color(.3f, .85f, 1f, .9f), near ? Mathf.PingPong(Time.time * 4, 1) * k + k * .5f : 0);
                if (cap >= CapTime) { cap = 0; owned = Online; Activations++; hp = ZoneCells.Hp(g, 200); if (!Game.SimRun) { g.hud.ShowBanner("TURRET ONLINE"); Haptics.Light(); } }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime; idle += dt; CryoTurretFx.Tick();
            if (CryoPropMotion.DebugTarget != null) { Aim(CryoPropMotion.DebugTarget, dt, true); return; }
            if (game == null || !game.run || game.player == null) { Aim(null, dt, false); return; }
            ringM.SetColor("_BaseColor", ringC);
            var t = tgt != null && !tgt.dead ? tgt : null;
            if (t != null && flame && !Game.SimRun) CryoTurretFx.Flame(MuzzleW(), aimW.sqrMagnitude > 0 ? aimW : yaw.forward, dt);
            Aim(t != null ? MazeMap.ToWorld(t.p, .8f) : (Vector3?)null, dt, t != null);
        }

        void Fire(Game g, Enemy best, float dt)
        {
            if (!flame)
            {
                if ((shot -= dt) > 0) return;
                shot = 1f / GunRate; best.hp -= ZoneCells.RH(g); best.flash = 1; Shots++;
                if (!Game.SimRun) { var m = MuzzleW(); if (kitAnchor == null) { kitAnchor = new GameObject(name + " muzzle fx").transform; kit = GunfireKit.Load(kitAnchor, kitAnchor); }   // unparented: the room's mirror scale stays off the particles
                    kitAnchor.position = m; CryoTurretFx.GunShot(m, MazeMap.ToWorld(best.p, .8f), kit); HazardAudio.Shot("sfx_hit_sparks", MazeMap.ToWorld(best.p, .8f), .25f, 3, this, .08f); }
                if (best.hp <= 0) { g.KillEnemy(best); Kills++; }
                return;
            }
            // flamethrower: every enemy in the cone takes FlameRH RH/s (10 ticks/s); the jet runs while anything is in reach
            if ((shot -= dt) > 0) return; shot = .1f; FlameTicks++;
            var dir = (best.p - at).normalized;   // the cone points at the target (the head's swing is only the look)
            foreach (var e in g.swarm.enemies.ToArray())
            {
                if (e.dead) continue; var rel = e.p - at; float L = rel.magnitude;
                if (L > FlameRange + e.r || Vector2.Angle(dir, rel) > FlameCone) continue;
                e.hp -= ZoneCells.RH(g) * FlameRH * .1f; e.flash = 1; if (e.hp <= 0) { g.KillEnemy(e); Kills++; }
            }
        }

        // yaw / pitch toward the target (smoothed); no target: a slow scan sweep; the gun's barrels spin while it fires
        void Aim(Vector3? target, float dt, bool firing)
        {
            float yawDeg, pitchDeg;
            if (target != null)
            {
                var d = yaw.parent.InverseTransformDirection(target.Value - yaw.position); d.y = 0;
                yawDeg = Vector3.SignedAngle(restFwd, d, Vector3.up);
                var m = MuzzleW(); var to = target.Value - pitch.position; float horiz = new Vector2(to.x, to.z).magnitude;
                pitchDeg = Mathf.Clamp(Mathf.Atan2(to.y - (m.y - pitch.position.y), Mathf.Max(.5f, horiz)) * Mathf.Rad2Deg, -18f, 30f);
            }
            else { yawDeg = Mathf.Sin(idle * .35f) * 55f; pitchDeg = 3f + Mathf.Sin(idle * .5f) * 3f; }
            var wantYaw = yaw0 * Quaternion.Euler(0, yawDeg, 0); var wantPitch = pitch0 * Quaternion.AngleAxis(pitchDeg * pitchSign, Vector3.right);
            yaw.localRotation = Quaternion.RotateTowards(yaw.localRotation, wantYaw, (target != null ? 220f : 35f) * dt);
            pitch.localRotation = Quaternion.RotateTowards(pitch.localRotation, wantPitch, 90f * dt);
            aimW = MuzzleW() - pitch.position; aimW.y = 0;
            if (barrels != null) { spin = Mathf.MoveTowards(spin, firing ? 900f : 0f, 1500f * dt); barrels.localRotation = barrels.localRotation * Quaternion.AngleAxis(spin * dt, barrelAxis); }
        }

        // the demo bot (only): an idle turret within 9 m, with 3+ enemies near the hero: go and stand by it
        public static bool BotWants(Game g, out Vector2 dir)
        {
            dir = default; if (Off) return false; int near = 0; foreach (var e in g.swarm.enemies) if (!e.dead && (e.p - g.player.p).sqrMagnitude < 100f) near++;
            if (near < 3) return false;
            CryoHeroTurret best = null; float bd = 81f;
            foreach (var t in Live) { if (!t.Idle) continue; float d = (t.at - g.player.p).sqrMagnitude; if (d < bd) { bd = d; best = t; } }
            if (best == null) return false;
            var to = best.at - g.player.p; if (to.magnitude < 1.9f) { dir = Vector2.zero; return true; }   // in the ring: hold still
            dir = to.normalized; return true;
        }
    }

    // pooled turret effects: the gunfire kit's flash + a tracer quad for the gun, VfxKit's fire flipbook for the flamethrower
    public static class CryoTurretFx
    {
        public static void GunShot(Vector3 muzzle, Vector3 hit, GunfireKit kit)
        {
            var dir = hit - muzzle; dir.y = 0; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            if (kit != null) { kit.Shot(false, false, dir); kit.Impact(hit, -dir, true, 2); }
            else if (VfxKit.I != null) VfxKit.I.Flash(muzzle, 1.6f, new Color(1f, .8f, .4f, 1f), .08f);
            if (VfxKit.I != null) VfxKit.I.Flash(muzzle + dir * .3f, 1.2f, new Color(1f, .72f, .3f, .9f), .09f);   // a warm bloom so the flash reads on the pale floor
            if (Fx.I != null)
            {
                var t = Fx.I.GetTracer(0); var d = hit - muzzle; float L = Mathf.Min(d.magnitude, 4f);
                t.SetPositionAndRotation(muzzle + d.normalized * (L * .5f), Quaternion.LookRotation(d.normalized)); var s = t.localScale; s.z = L; t.localScale = s;
                tracers.Add((t, Time.time + .08f));
            }
            Tick();
        }
        static readonly List<(Transform t, float until)> tracers = new();
        public static void Tick() { for (int i = tracers.Count - 1; i >= 0; i--) if (Time.time >= tracers[i].until) { if (tracers[i].t != null) Fx.I.ReleaseTracer(tracers[i].t); tracers.RemoveAt(i); } }
        static float flameAcc;
        public static void Flame(Vector3 nozzle, Vector3 fwd, float dt)
        {
            Tick(); if (VfxKit.I == null || VfxKit.I.fire == null) return;
            fwd.y = 0; fwd.Normalize(); flameAcc += dt * 40f;   // 40 flames/s, short-lived, warm, no smoke
            while (flameAcc >= 1f)
            {
                flameAcc -= 1f;
                var v = (fwd + new Vector3(Random.Range(-.25f, .25f), Random.Range(.02f, .12f), Random.Range(-.25f, .25f))).normalized * Random.Range(9f, 12f);
                var ep = new ParticleSystem.EmitParams { position = nozzle + fwd * .2f, velocity = v, startSize = Random.Range(.7f, 1.1f), startLifetime = Random.Range(.38f, .46f), startColor = new Color(1f, .78f, .45f, .95f), rotation = Random.value * 360f };
                VfxKit.I.fire.Emit(ep, 1);
            }
        }
    }
}
