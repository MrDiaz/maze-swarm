using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Escape maps (MazeMap.Escape): a chain of rooms behind blast doors. The start room's door opens straight away;
    // stepping into the next room seals the door behind you and the room's swarm pours out of its vents; clear it
    // and the door ahead slides open (green light, chime, "ROOM CLEAR", a guide arrow to the next room). The last
    // room holds the extraction pad: stand on it for 3 s to escape the floor.
    public class EscapeRun : MonoBehaviour
    {
        Game g;
        // a blast door: the Meshy frame on both wall faces, two mirrored sliding panels, an emissive status lens, a
        // row of bolt lights along the seam and a light pool on the floor each side (see DoorAnim)
        class DoorView
        {
            public MazeMap.Door d; public Transform root, panelL, panelR; public Material lens, bolt, pool; public Transform[] pools;
            public float halfW, gap = 0, t = -1; public int state; public float alarmAt = -1; public bool isSealed;
            public Transform slab => root;
        }
        const int Idle = 0, Slam = 1, Unlock = 2, Gentle = 3;
        readonly List<DoorView> views = new();
        Transform padRing, arrow; Material padMat, arrowMat; Light padLight;
        short[] field; Vector2 target; bool guide;

        public int Cur { get; private set; }      // the room the player is in (0 = start)
        public int Combat => Mathf.Max(0, MazeMap.RoomCount - 2);
        public int Last => MazeMap.RoomCount - 1;
        public bool Cleared { get; private set; }
        public bool Escaped { get; private set; }
        public float RunTime { get; private set; }
        int toSpawn, bloatersLeft, bloaterStride, nextBloaterAt, pendingClose = -1, lastCount;
        float spawnT, extractT;

        // per combat room 1..4: swarm size, Bloaters, the "wave" used for HP / speed scaling and the enemy mix
        static readonly int[] Count = { 0, 12, 15, 18, 20 };
        static readonly int[] Bloat = { 0, 0, 0, 1, 1 };
        static readonly int[] Leap = { 0, 0, 1, 2, 2 };   // Leapers per room (rooms 2-4)
        int leapersLeft, leaperStride, nextLeaperAt;
        static readonly int[] Scale = { 1, 1, 2, 3, 3 };
        const float SpawnEvery = 0.45f; // a room's swarm trickles out of the vents instead of arriving at once
        const float ClearHeal = 35f; // a med station refill as each door opens
        const float ExtractTime = 3f;

        public string Label => Cur == 0 ? "START" : Cur >= Last ? "EXTRACT" : $"ROOM {Cur}/{Combat}";

        public void Build(Game game)
        {
            g = game;
            foreach (var v in views) Destroy(v.slab.gameObject);
            views.Clear();
            if (padRing != null) Destroy(padRing.gameObject);
            if (arrow != null) Destroy(arrow.gameObject);
            if (padLight != null) Destroy(padLight.gameObject);
            if (!MazeMap.Facility) return; // doors on every facility map (a run's floors too); the pad and arrow on the Escape Test
            field = new short[MazeMap.W * MazeMap.H];
            DoorAudioInit();

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cube = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            var tq = GameObject.CreatePrimitive(PrimitiveType.Quad); var quad = tq.GetComponent<MeshFilter>().sharedMesh; Destroy(tq);
            var framePf = Resources.Load<GameObject>("Models/door_frame"); var panelPf = Resources.Load<GameObject>("Models/door_panel");
            float C = MazeMap.C;
            foreach (var d in MazeMap.doors)
            {
                var root = new GameObject("Door").transform; root.SetParent(transform, false);
                bool alongX = MazeMap.DoorAlongX(d);                       // the opening spans world X (passage along Z): from the wall it sits in
                float open = (alongX ? d.r.width : d.r.height) * C, thick = (alongX ? d.r.height : d.r.width) * C;
                root.SetPositionAndRotation(MazeMap.ToWorld(d.center), Quaternion.Euler(0, alongX ? 0 : 90, 0));
                var v = new DoorView { d = d, root = root };
                // frame (model 1.9 x 1.57 x 0.36, inner opening ~77% x 83%): stretched to the opening, one per wall face
                var fs = new Vector3(open / (1.9f * 0.77f), DoorH / (1.57f * 0.83f), 2.2f);
                v.lens = Mats.Unlit(Holo.Acid);
                foreach (var side in new[] { -1f, 1f })
                {
                    if (framePf == null) break;
                    var f = Instantiate(framePf, root); Mats.ConvertGltf(f);
                    f.transform.localPosition = new Vector3(0, 0, side * (thick / 2 - 0.36f * fs.z / 2 + 0.02f));
                    f.transform.localRotation = Quaternion.Euler(0, side > 0 ? 0 : 180, 0);
                    f.transform.localScale = fs;
                    PlaceOnFloor(f.transform);
                    foreach (var r in f.GetComponentsInChildren<Renderer>())
                    {
                        r.shadowCastingMode = ShadowCastingMode.On;
                        if (r.name.Contains("Lens")) { r.sharedMaterial = v.lens; r.shadowCastingMode = ShadowCastingMode.Off; }
                    }
                }
                // panels: half the opening each, mirrored, meeting at the centre; they slide into the wall when open
                float innerW = 1.9f * 0.77f * fs.x;
                v.halfW = innerW / 2f;
                var ps = new Vector3(v.halfW / 1.05f, (DoorH - 0.05f) / 1.9f, 2f);
                for (int k = 0; k < 2; k++)
                {
                    Transform pt;
                    if (panelPf != null) { var pg = Instantiate(panelPf, root); Mats.ConvertGltf(pg); pt = pg.transform; }
                    else { pt = new GameObject("panel").transform; pt.SetParent(root, false); Part(pt, cube, new Vector3(0, 0.5f, 0), new Vector3(1.05f, 1.9f, 0.15f), Mats.Lit(new Color(0.85f, 0.87f, 0.9f)), true); }
                    // the model's inner edge (seal, handle slots) is at its -X in Unity: the right panel as is, the left mirrored
                    pt.localScale = new Vector3(k == 0 ? -ps.x : ps.x, ps.y, ps.z);
                    foreach (var r in pt.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                    if (k == 0) v.panelL = pt; else v.panelR = pt;
                }
                // a solid header between the two frames above the panels, so the doorway reads as one thick door from above
                var header = Mats.Lit(new Color32(0x3a, 0x40, 0x4c, 255), null, null, 0.45f, 0.4f);
                Part(root, cube, new Vector3(0, DoorH + 0.22f, 0), new Vector3(innerW + 0.3f, 0.5f, thick - 0.36f * fs.z * 1.6f), header, true);
                // bolt lights: a short row along the seam on both faces (lit while locked)
                v.bolt = Mats.Unlit(Holo.Red);
                foreach (var side in new[] { -1f, 1f })
                    for (int b = 0; b < BoltCount; b++)
                        Part(root, cube, new Vector3(0, 0.55f + b * (DoorH - 1.1f) / (BoltCount - 1), side * 0.2f), new Vector3(0.08f, 0.16f, 0.06f), v.bolt, false).name = "Bolt" + b;
                // light pools on the floor, both sides (additive, pulse with the alarm)
                v.pool = Mats.Glow(new Color(1, 0.1f, 0.08f, 0)); v.pool.SetTexture("_BaseMap", Holo.SoftSprite.texture);
                v.pools = new Transform[2];
                for (int k = 0; k < 2; k++)
                {
                    var q = Part(root, quad, new Vector3(0, 0.03f, (k == 0 ? -1 : 1) * (thick / 2 + 1.6f)), new Vector3(open * 0.9f, 3.4f, 1), v.pool, false);
                    q.localRotation = Quaternion.Euler(90, 0, 0); q.name = "Pool"; v.pools[k] = q;
                }
                Pose(v, 0);
                views.Add(v);
            }

            // the extraction pad: a ring that's dim red until the last room, then pulses acid green, and a light
            if (MazeMap.Escape && MazeMap.exitCells.Count > 0)
            {
                Vector2 c = Vector2.zero; foreach (var e in MazeMap.exitCells) c += e; c /= MazeMap.exitCells.Count;
                padRing = new GameObject("ExtractRing").transform; padRing.SetParent(transform, false);
                padRing.position = MazeMap.ToWorld(c, 0.06f);
                padRing.localScale = Vector3.one * (Mathf.Sqrt(MazeMap.exitCells.Count) * C * 0.62f);
                padRing.gameObject.AddComponent<MeshFilter>().sharedMesh = Fx.Ring;
                var mr = padRing.gameObject.AddComponent<MeshRenderer>(); padMat = Mats.Glow(Holo.Red); mr.sharedMaterial = padMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                padLight = new GameObject("ExtractLight").AddComponent<Light>(); padLight.transform.SetParent(transform, false);
                padLight.type = LightType.Point; padLight.range = 9; padLight.shadows = LightShadows.None; padLight.transform.position = MazeMap.ToWorld(c, 2.2f);
            }

            if (!MazeMap.Escape) return;
            // the guide arrow: a flat chevron on the floor ahead of the hero, pointing along the path
            arrow = new GameObject("GuideArrow").transform; arrow.SetParent(transform, false);
            var am = new Mesh();
            am.vertices = new[] { new Vector3(0, 0, 0.9f), new Vector3(0.75f, 0, -0.35f), new Vector3(0, 0, 0f), new Vector3(-0.75f, 0, -0.35f) };
            am.triangles = new[] { 0, 1, 2, 0, 2, 3 }; am.RecalculateNormals(); am.RecalculateBounds();
            arrow.gameObject.AddComponent<MeshFilter>().sharedMesh = am;
            var ar = arrow.gameObject.AddComponent<MeshRenderer>(); arrowMat = Mats.Glow(Holo.Acid); arrowMat.SetFloat("_Cull", 0);
            ar.sharedMaterial = arrowMat; ar.shadowCastingMode = ShadowCastingMode.Off;
            arrow.gameObject.SetActive(false);
        }

        static Transform Part(Transform parent, Mesh m, Vector3 pos, Vector3 size, Material mat, bool shadow)
        {
            var go = new GameObject("part"); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        // stand a model's lowest point on the floor (y = 0 in the door's space)
        static void PlaceOnFloor(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            t.position += Vector3.up * (t.parent.position.y - b.min.y + 0.005f);
        }

        public void ResetRun()
        {
            if (!MazeMap.Escape) return;
            MazeMap.ResetDoors();
            foreach (var v in views) { v.state = Idle; v.t = -1; v.isSealed = false; v.alarmAt = -1; }
            quietSlam = false;
            Cur = 0; Cleared = true; Escaped = false; RunTime = 0; toSpawn = 0; pendingClose = -1; extractT = 0; lastCount = -1;
            DoorAudioReset();
            g.wave = 1;
            Open(0, false, true); // the start room is safe
        }

        // fanfare: a cleared combat room (bolts, then the door slides; it becomes passable mid-slide); else a soft open
        void Open(int door, bool fanfare, bool instant = false)
        {
            if (door < 0 || door >= MazeMap.doors.Count) return;
            var dv = View(MazeMap.doors[door]);
            if (fanfare && dv != null) StartUnlock(dv);
            else { MazeMap.SetDoor(MazeMap.doors[door], true); if (dv != null && !instant) StartGentle(dv, true); }
            Guide(door + 1 <= Last ? MazeMap.roomCenter[door + 1] : target);
            if (!fanfare) return;
            g.hud.ShowBanner("ROOM CLEAR");
            g.player.hp = Mathf.Min(g.player.maxHp, g.player.hp + ClearHeal);
            Fx.I.Sparks(MazeMap.ToWorld(g.player.p, 0.6f), 12, new Color(0.28f, 0.7f, 1f), 3, 0.6f, 0.1f);
            Haptics.Medium(); // (the door sounds: DoorsUnsealed)
        }

        // ---- door sounds (ElevenLabs, Sounds/Doors): a combat room seals with a slam, the bolts lock (+0.35 s) and an
        // alarm pulses (+0.8 s, looping) until it's clear; then the alarm fades, the bolts release and the door slides.
        // Half-3D at the door (gentle linear rolloff, still heard at the FAR camera); the Settings toggle (AudioListener
        // volume) and pause (the alarm pauses) apply.
        AudioSource doorSrc, alarmSrc;
        AudioClip slam, lockClip, alarm, unlock, open;
        readonly List<(float t, System.Action a)> doorQueue = new();
        float alarmFade = -1; bool alarmOn, alarmPaused; // own state: isPlaying reads false while the listener is paused
        AudioSource DoorSource(string n)
        {
            var s = new GameObject(n).AddComponent<AudioSource>(); s.transform.SetParent(transform, false);
            s.playOnAwake = false; s.spatialBlend = 0.45f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 30; s.maxDistance = 140; s.dopplerLevel = 0;
            return s;
        }
        void DoorAudioInit()
        {
            if (doorSrc != null) return;
            doorSrc = DoorSource("DoorSfx"); alarmSrc = DoorSource("DoorAlarm"); alarmSrc.loop = true;
            AudioClip L(string n) => Resources.Load<AudioClip>("Audio/" + n);
            slam = L("door_slam"); lockClip = L("door_lock"); alarm = L("door_alarm_loop"); unlock = L("door_unlock"); open = L("door_open");
        }
        void DoorAt(MazeMap.Door d) { var w = MazeMap.ToWorld(d.center, 1.5f); doorSrc.transform.position = w; alarmSrc.transform.position = w; }
        void Say(string what) => Debug.Log($"Maze door audio: {what} t={RunTime:F2}");
        DoorView View(MazeMap.Door d) { foreach (var v in views) if (v.d == d) return v; return null; }
        void PlayAt(DoorView v, AudioClip c, float vol, string what)
        {
            DoorAudioInit(); if (c == null) return;
            doorSrc.transform.position = v.root.position + Vector3.up * 1.5f; doorSrc.PlayOneShot(c, vol); Say(what);
        }
        void DoorAudioTick(float dt)
        {
            if (alarmFade >= 0)
            {
                alarmFade -= dt; alarmSrc.volume = AlarmVol * Mathf.Clamp01(alarmFade / 0.25f);
                if (alarmFade <= 0) { alarmSrc.Stop(); alarmOn = false; alarmFade = -1; Say("alarm off"); }
            }
        }

        // ---- the door's choreography (tuned to the clips: the slam's transient sits at 0.0-0.05 s, door_unlock's
        // bolt clacks in its first 0.35 s, door_open's main movement at 0.6-1.2 s)
        const float DoorH = 3.0f, SlamDur = 0.25f, LockAt = SlamDur + 0.35f, AlarmAt = SlamDur + 0.8f, AlarmVol = 0.42f;
        const float OpenAt = 1.5f, CrackDur = 0.1f, SlideAt = OpenAt + 0.45f, SlideDur = 1.1f, Crack = 0.03f;
        const int BoltCount = 4;
        static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

        // combat room seals: panels slam shut (strong ease-in), bounce, then lock and alarm
        void StartSlam(DoorView v) { v.state = Slam; v.t = 0; v.isSealed = true; v.alarmAt = -1; Say("seal (slam starts)"); }
        // room cleared: amber + bolts off with door_unlock, then green, crack, beat, slide (door_open)
        void StartUnlock(DoorView v) { v.state = Unlock; v.t = 0; Say("unlock sequence"); }
        // start / lift rooms and debug: soft open or close
        void StartGentle(DoorView v, bool open) { v.state = Gentle; v.t = 0; v.isSealed = !open; }

        void DoorTick(DoorView v, float dt, float time)
        {
            float t0 = v.t; if (v.t >= 0) v.t += dt; float t = v.t;
            bool Cross(float at) => t0 < at && t >= at;
            Color lens = Holo.Acid; float bolts = 0, pool = 0;
            switch (v.state)
            {
                case Slam:
                {
                    float x = Mathf.Clamp01(t / SlamDur);
                    v.gap = x < 1 ? v.halfW * (1 - x * x * x) : -0.05f * Mathf.Exp(-(t - SlamDur) * 18f) * Mathf.Sin((t - SlamDur) * 40f); // overshoot bounce
                    lens = t < 0.12f ? (Mathf.FloorToInt(t * 50) % 2 == 0 ? Holo.Red : Holo.Acid) : Holo.Red; // 2-3 frame flicker
                    if (Cross(SlamDur)) Impact(v);
                    if (Cross(LockAt)) PlayAt(v, lockClip, 0.8f, "lock");
                    bolts = t >= LockAt ? 1 : 0;
                    if (Cross(AlarmAt) && !Cleared && !quietSlam) { DoorAudioInit(); alarmSrc.transform.position = v.root.position + Vector3.up * 1.5f; alarmSrc.clip = alarm; alarmSrc.volume = AlarmVol; alarmSrc.Play(); alarmOn = true; v.alarmAt = time; Say("alarm on"); }
                    if (v.alarmAt >= 0 && alarmOn) { float ph = ((time - v.alarmAt) % 0.5f) / 0.5f; float pulse = Mathf.Exp(-ph * 5f); lens = Color.Lerp(new Color(0.45f, 0.03f, 0.03f), Holo.Red * 1.6f, pulse); pool = 0.1f + 0.35f * pulse; }
                    else if (t >= SlamDur) lens = Holo.Red;
                    break;
                }
                case Unlock:
                {
                    if (Cross(0.0001f) || t0 == 0) { if (alarmOn) { alarmFade = 0.25f; Say("alarm fade"); } PlayAt(v, unlock, 0.9f, "unlock"); }
                    bool amber = t < OpenAt;
                    lens = amber ? (Mathf.FloorToInt(t * 8) % 2 == 0 ? new Color(1f, 0.62f, 0.1f) : new Color(0.3f, 0.17f, 0.02f)) : Holo.Acid;
                    // the bolts click off one by one with the clacks (0.06-0.30 s)
                    int lit = BoltCount - Mathf.Clamp(Mathf.FloorToInt((t - 0.06f) / 0.08f) + 1, 0, BoltCount);
                    bolts = lit / (float)BoltCount;
                    if (Cross(0.15f)) foreach (var sx in new[] { -1f, 1f }) Fx.I.Smoke(v.root.TransformPoint(new Vector3(sx * (v.halfW + 0.3f), DoorH - 0.1f, 0)), 5, new Color(0.9f, 0.93f, 0.97f), 1.6f, 0.7f, 0.22f, 1.5f); // steam hiss
                    if (Cross(OpenAt)) { PlayAt(v, open, 0.9f, "open"); Haptics.Light(); }
                    if (t < OpenAt) v.gap = 0;
                    else if (t < SlideAt) v.gap = Crack * EaseInOut((t - OpenAt) / CrackDur);
                    else v.gap = Crack + (v.halfW + 0.15f - Crack) * EaseInOut((t - SlideAt) / SlideDur);
                    // the way is free once the gap fits the hero
                    if (!v.d.open && v.gap * 2 > 1.3f) { MazeMap.SetDoor(v.d, true); v.isSealed = false; Say("passable"); }
                    if (t > SlideAt + SlideDur) { v.state = Idle; v.t = -1; }
                    break;
                }
                case Gentle:
                {
                    float x = EaseInOut(t / 0.7f);
                    v.gap = v.isSealed ? (v.halfW + 0.15f) * (1 - x) : (v.halfW + 0.15f) * x;
                    lens = v.isSealed ? Holo.Red : Holo.Acid;
                    if (v.isSealed && Cross(0.7f)) { Sfx.Close(); Haptics.Light(); }
                    if (t > 0.7f) { v.state = Idle; v.t = -1; }
                    break;
                }
                default:
                    v.gap = v.d.open ? v.halfW + 0.15f : 0;
                    lens = v.d.open ? Holo.Acid : (v.isSealed ? Holo.Red : new Color(0.7f, 0.08f, 0.06f));
                    bolts = v.isSealed && !v.d.open ? 1 : 0;
                    break;
            }
            v.lens.SetColor("_BaseColor", lens);
            v.bolt.SetColor("_BaseColor", Holo.Red * 1.4f);
            for (int i = 0; i < v.root.childCount; i++)
            {
                var c = v.root.GetChild(i);
                if (c.name.StartsWith("Bolt")) { int b = c.name[4] - '0'; c.gameObject.SetActive(v.gap < 0.02f && b < Mathf.RoundToInt(bolts * BoltCount)); }
            }
            v.pool.SetColor("_BaseColor", new Color(1f, 0.1f, 0.06f, pool));
            foreach (var q in v.pools) q.gameObject.SetActive(pool > 0.01f);
            Pose(v, v.gap);
        }

        // panels at this half-gap (each slides v.gap from the centre into the wall)
        static void Pose(DoorView v, float gap)
        {
            float w = v.halfW;
            const float y = (DoorH - 0.05f) / 2 + 0.03f; // the panel model is centred
            v.panelL.localPosition = new Vector3(-w / 2 - gap, y, 0);
            v.panelR.localPosition = new Vector3(w / 2 + gap, y, 0);
        }

        // the slam lands: dust at the floor seam, sparks at the meeting edge, distance-scaled shake, haptic
        void Impact(DoorView v)
        {
            PlayAt(v, slam, 1f, "slam (impact)");
            var c = v.root.position;
            Fx.I.Smoke(c + Vector3.up * 0.1f, 8, new Color(0.82f, 0.84f, 0.88f), 2.2f, 0.6f, 0.3f, 0.6f);
            Fx.I.Sparks(c + Vector3.up * 1.4f, 10, new Color(1f, 0.8f, 0.35f), 4f, 0.35f, 0.05f, 2f);
            float dist = (g.player.p - v.d.center).magnitude;
            g.AddShake(Mathf.Lerp(0.3f, 0.08f, Mathf.InverseLerp(2f, 14f, dist)));
            Haptics.Medium();
        }
        void DoorAudioReset() { alarmFade = -1; alarmOn = false; if (alarmSrc != null) alarmSrc.Stop(); }
        // pause holds the alarm (and any door sound mid-play); called from Update
        void DoorAudioPause()
        {
            if (alarmSrc == null) return;
            bool paused = g != null && (g.state == Game.State.Paused || g.state == Game.State.Menu);
            if (paused != alarmPaused)
            {
                alarmPaused = paused;
                if (paused) { alarmSrc.Pause(); doorSrc.Pause(); } else { alarmSrc.UnPause(); doorSrc.UnPause(); }
                Say(paused ? "paused (alarm held)" : "resumed");
            }
            if (g != null && alarmOn && (g.state == Game.State.Dead || g.state == Game.State.Won || !MazeMap.Escape)) { alarmSrc.Stop(); alarmOn = false; Say("alarm off (run over)"); }
        }

        void Guide(Vector2 to) { target = to; MazeMap.FlowTo(to, field); guide = true; }

        // the demo bot's way on: along the guide path when the room is quiet
        public Vector2 BotDir(Vector2 p) => guide ? MazeMap.StepAlong(field, p, target) : Vector2.zero;
        float refreshT;

        public void Tick(float dt)
        {
            if (!MazeMap.Escape || Escaped) return;
            RunTime += dt;
            if (guide && (refreshT -= dt) <= 0) { refreshT = 1f; MazeMap.FlowTo(target, field); }
            var p = g.player.p;
            int room = MazeMap.RoomAt(p);

            // stepped into the next room: seal the door behind once clear of it, then the room wakes up
            if (room == Cur + 1 && Cleared)
            {
                Cur = room; pendingClose = room - 1; guide = false;
                if (Cur < Last)
                {
                    Cleared = false; toSpawn = Count[Mathf.Min(Cur, Count.Length - 1)]; spawnT = 1.0f;
                    bloatersLeft = g.swarm.BloaterType >= 0 ? Bloat[Mathf.Min(Cur, Bloat.Length - 1)] : 0;
                    bloaterStride = toSpawn / (bloatersLeft + 1); nextBloaterAt = toSpawn - bloaterStride;
                    leapersLeft = g.swarm.LeaperType >= 0 ? Leap[Mathf.Min(Cur, Leap.Length - 1)] : 0;
                    leaperStride = toSpawn / (leapersLeft + 1); nextLeaperAt = toSpawn - leaperStride / 2;
                    g.wave = Scale[Mathf.Min(Cur, Scale.Length - 1)];
                    g.hud.ShowBanner(Label);
                }
                else { Guide(PadCenter()); g.hud.ShowBanner("REACH THE LIFT"); }
            }
            if (pendingClose >= 0)
            {
                var d = MazeMap.doors[pendingClose];
                var rect = new Rect(d.r.x * MazeMap.C, d.r.y * MazeMap.C, d.r.width * MazeMap.C, d.r.height * MazeMap.C);
                var cp = new Vector2(Mathf.Clamp(p.x, rect.xMin, rect.xMax), Mathf.Clamp(p.y, rect.yMin, rect.yMax));
                if ((cp - p).magnitude > 1.1f)
                {   // blocks at once; the panels slam (combat room) or close softly (the lift room)
                    MazeMap.SetDoor(d, false); pendingClose = -1;
                    var dv = View(d); if (dv != null) { if (!Cleared) StartSlam(dv); else StartGentle(dv, false); }
                }
            }

            // the room's swarm from its vents (not right next to the hero when there's a choice)
            if (!Cleared && toSpawn > 0)
            {
                spawnT -= dt;
                if (spawnT <= 0 && g.swarm.enemies.Count < g.cfg.waves.maxAlive)
                {
                    var vents = MazeMap.roomVents[Cur];
                    Vector2 at = vents.Count > 0 ? vents[Random.Range(0, vents.Count)] : MazeMap.roomCenter[Cur];
                    for (int k = 0; k < 4 && vents.Count > 1 && (at - p).magnitude < 7f; k++) at = vents[Random.Range(0, vents.Count)];
                    if (bloatersLeft > 0 && toSpawn <= nextBloaterAt) { g.swarm.Spawn(at, g.wave, g.swarm.BloaterType); bloatersLeft--; nextBloaterAt -= bloaterStride; }
                    else if (leapersLeft > 0 && toSpawn <= nextLeaperAt) { g.swarm.Spawn(at, g.wave, g.swarm.LeaperType); leapersLeft--; nextLeaperAt -= leaperStride; }
                    else g.swarm.Spawn(at, g.wave);
                    toSpawn--; spawnT = SpawnEvery;
                }
            }
            if (!Cleared && toSpawn == 0 && g.swarm.enemies.Count == 0 && !g.swarm.AnyDying) { Cleared = true; Open(Cur, true); }
            DoorAudioTick(dt);
            foreach (var v in views) DoorTick(v, dt, RunTime); // game time: in step with the sounds, held by pause

            // the lift: 3 s on the pad
            if (Cur >= Last && Cleared)
            {
                if (MazeMap.OnExit(p))
                {
                    extractT += dt;
                    int n = Mathf.CeilToInt(ExtractTime - extractT);
                    if (n != lastCount && n > 0) { lastCount = n; g.hud.ShowBanner("EXTRACTING " + n); Sfx.Click(); Haptics.Light(); }
                    if (extractT >= ExtractTime) { Escaped = true; arrow.gameObject.SetActive(false); g.Escape(RunTime); return; }
                }
                else if (extractT > 0) { extractT = 0; lastCount = -1; }
            }
        }

        // ---- a sector run's ESCAPE floor drives the doors itself: all open, slam the one behind the hero, no alarm
        bool quietSlam; float doorClock;
        public void OpenAllDoors() { foreach (var d in MazeMap.doors) MazeMap.SetDoor(d, true); foreach (var v in views) { v.state = Idle; v.t = -1; v.isSealed = false; } }
        public void SlamDoor(int i) { if (i < 0 || i >= MazeMap.doors.Count || !MazeMap.doors[i].open) return; quietSlam = true; MazeMap.SetDoor(MazeMap.doors[i], false); var dv = View(MazeMap.doors[i]); if (dv != null) StartSlam(dv); }
        public void UnlockDoor(int i) { if (i < 0 || i >= MazeMap.doors.Count) return; quietSlam = true; var dv = View(MazeMap.doors[i]); if (dv != null) StartUnlock(dv); else MazeMap.SetDoor(MazeMap.doors[i], true); }
        public void CloseAllDoors() { foreach (var d in MazeMap.doors) MazeMap.SetDoor(d, false); foreach (var v in views) { v.state = Idle; v.t = -1; v.isSealed = true; } }
        public void TickDoors(float dt) { doorClock += dt; DoorAudioTick(dt); foreach (var v in views) DoorTick(v, dt, doorClock); }

        // editor captures: jump straight to a room / clear it / stand on the lift
        public void DebugEnter(int room) { if (room <= 0 || room > Last) return; Cur = room - 1; Cleared = true; Open(Cur, false); g.player.p = MazeMap.roomCenter[room]; }
        public void DebugClear() { toSpawn = 0; foreach (var e in g.swarm.enemies.ToArray()) g.KillEnemy(e); }
        public void DebugToLift() { DebugEnter(Last); }
        public Vector2 Pad => PadCenter();

        Vector2 PadCenter() { Vector2 c = Vector2.zero; foreach (var e in MazeMap.exitCells) c += e; return MazeMap.exitCells.Count > 0 ? c / MazeMap.exitCells.Count : MazeMap.roomCenter[Last]; }

        // door slabs slide, lights and the guide arrow follow the state (scaled time: frozen when paused)
        void Update()
        {
            DoorAudioPause();
            if (!MazeMap.Escape || g == null) return;
            float dt = Time.deltaTime, t = Time.time;
            if (padMat != null)
            {
                bool live = Cur >= Last && Cleared;
                float pulse = 0.6f + 0.4f * Mathf.Sin(t * (extractT > 0 ? 12 : 4));
                padMat.SetColor("_BaseColor", live ? Holo.A(Holo.Acid, pulse) : Holo.A(Holo.Red, 0.35f));
                padRing.localRotation = Quaternion.Euler(0, t * 30, 0);
                padLight.color = live ? Holo.Acid : Holo.Red; padLight.intensity = live ? 1.6f + 1.2f * pulse : 0.5f;
            }
            bool show = guide && Cleared && g.state == Game.State.Playing && !Escaped && !(Cur >= Last && MazeMap.OnExit(g.player.p));
            arrow.gameObject.SetActive(show);
            if (show)
            {
                var dir = MazeMap.StepAlong(field, g.player.p, target);
                if (dir.sqrMagnitude > 0.01f)
                {
                    var w = MazeMap.ToWorld(dir);
                    arrow.SetPositionAndRotation(MazeMap.ToWorld(g.player.p + dir * 1.9f, 0.07f), Quaternion.LookRotation(w));
                    arrow.localScale = Vector3.one * (0.9f + 0.12f * Mathf.Sin(t * 6));
                    arrowMat.SetColor("_BaseColor", Holo.A(Holo.Acid, 0.6f + 0.35f * Mathf.Sin(t * 6)));
                }
            }
        }
    }
}
