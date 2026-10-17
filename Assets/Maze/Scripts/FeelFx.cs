using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace Maze
{
    // The feel batch (2026-10-16, closer to Archero 2), next to ReadabilityFx. One static toggle per change (true = on):
    //   AttackPose   while the minigun fires: a slight forward lean and a small recoil kick on the upper body (before the arm IK,
    //                so the hands stay on the gun; movement and aim unchanged)
    //   HeroHpBar    a chunky world-space HP bar over the hero's head: segment ticks every 20 HP and the number
    //   HitStop      a 30-50 ms time dip on elite hits (at most every 0.3 s) and on big / elite kills (never during telegraphs)
    //   Squash       enemies squash for a moment when hit (visual; the hit flash drives it)
    //   KillGems     a few small cyan gems scatter from each kill and fly into the hero (visual, on top of the XP orbs)
    //   Hearts       a rare heart (3% of kills in a run) that heals 8% of max HP when walked over; lives 12 s. Gameplay: the
    //                sims see it too (Game.KillEnemy -> OnKill, Game.TickRun -> Tick)
    //   FrameLog     frame times per floor (p50 / p95 / p99 / max ms) appended to <app data>/Documents/frametimes.csv; on whenever
    //                Settings > SHOW FPS is on, or PlayerPrefs "ms.framelog" = 1
    public class FeelFx : MonoBehaviour
    {
        public static bool AttackPose = true, HeroHpBar = true, HitStop = true, Squash = true, KillGems = true, Hearts = true, FrameLog = true;
        public const float HeartChance = .03f, HeartHeal = .08f, HeartLife = 12f, HeartReach = 1.25f;
        public static FeelFx I;
        public static int HeartsDropped, HeartsTaken;   // (checks)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var go = new GameObject("FeelFx"); DontDestroyOnLoad(go); I = go.AddComponent<FeelFx>(); }

        // ---------------------------------------------------------------- attack pose (called from Player.LateUpdate, before the IK)
        static readonly Dictionary<Transform, (Transform spine, Transform spine2, Transform lsh, Transform rsh)> bones = new();
        static float poseK;
        public static void HeroPose(Player p, float recoil, bool firing)
        {
            var vis = p.VisualRoot; if (vis == null) return;
            if (!bones.TryGetValue(vis, out var b) || b.spine == null)
            {
                Transform s = null, s2 = null, l = null, r = null;
                foreach (var t in vis.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.EndsWith(":Spine") || t.name == "Spine") s = t; else if (t.name.EndsWith(":Spine2") || t.name == "Spine2") s2 = t;
                    else if (t.name.EndsWith(":LeftShoulder")) l = t; else if (t.name.EndsWith(":RightShoulder")) r = t;
                }
                b = (s, s2, l, r); bones[vis] = b;
            }
            poseK = Mathf.MoveTowards(poseK, AttackPose && firing ? 1f : 0f, Time.deltaTime / .12f);
            if (b.spine == null || poseK <= 0) return;
            var right = vis.right; float t2 = Time.time * 42f;
            b.spine.rotation = Quaternion.AngleAxis(7f * poseK, right) * b.spine.rotation;                       // lean into it
            float kick = recoil * poseK;
            if (b.spine2 != null) b.spine2.rotation = Quaternion.AngleAxis(-3.2f * kick + .6f * Mathf.Sin(t2) * poseK, right) * b.spine2.rotation;   // the kick back, a buzz while it spins
            if (b.lsh != null) b.lsh.rotation = Quaternion.AngleAxis(-2f * kick, right) * b.lsh.rotation;
            if (b.rsh != null) b.rsh.rotation = Quaternion.AngleAxis(-2f * kick, right) * b.rsh.rotation;
        }

        // ---------------------------------------------------------------- hearts (gameplay: runs in the sims too)
        class Heart { public Vector2 p; public float t; public Transform view; }
        static readonly List<Heart> hearts = new();
        public static void OnKill(Game g, Enemy e)
        {
            if (!Hearts || g == null || !g.run || e == null || e.type.nest || e.type.mini) return;
            if (Random.value >= HeartChance) return;
            var h = new Heart { p = e.p }; hearts.Add(h); HeartsDropped++;
            if (!Game.SimRun && I != null) h.view = I.HeartView(MazeMap.ToWorld(e.p, .5f));
        }
        public static void Tick(Game g, float dt)
        {
            for (int i = hearts.Count - 1; i >= 0; i--)
            {
                var h = hearts[i]; h.t += dt; bool take = g.player != null && (g.player.p - h.p).magnitude < HeartReach + g.cfg.player.radius;
                if (take) { g.player.hp = Mathf.Min(g.player.maxHp, g.player.hp + g.player.maxHp * HeartHeal); HeartsTaken++; if (!Game.SimRun && VfxKit.I != null) { var w = MazeMap.ToWorld(h.p, .8f); VfxKit.I.Flash(w, 1.6f, new Color(1f, .35f, .45f, .8f), .2f); VfxKit.I.FxSparks(w, 10, new Color(1f, .45f, .55f), 4f, .5f, .09f, 4f); } }
                if (take || h.t > HeartLife || !g.run) { if (h.view != null) Destroy(h.view.gameObject); hearts.RemoveAt(i); continue; }
                if (h.view != null) { h.view.position = MazeMap.ToWorld(h.p, .55f + .12f * Mathf.Sin(h.t * 4f)); h.view.rotation = Quaternion.Euler(0, h.t * 120f, 0); if (h.t > HeartLife - 2f) h.view.gameObject.SetActive(Mathf.Repeat(h.t * 6f, 1f) > .3f); }
            }
        }
        Material heartMat;
        Transform HeartView(Vector3 at)
        {
            heartMat ??= Mats.Glow(new Color(1f, .25f, .38f, 1f));
            var root = new GameObject("Heart").transform; root.position = at;
            foreach (var (pos, scl, rot) in new[] { (new Vector3(-.13f, .1f, 0), Vector3.one * .3f, 0f), (new Vector3(.13f, .1f, 0), Vector3.one * .3f, 0f), (new Vector3(0, -.07f, 0), new Vector3(.3f, .3f, .3f), 45f) })
            {
                var g = GameObject.CreatePrimitive(rot == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(root, false); g.transform.localPosition = pos; g.transform.localScale = scl; g.transform.localRotation = Quaternion.Euler(0, 0, rot);
                var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = heartMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        // ---------------------------------------------------------------- per frame: HP bar, hit-stop, gems, frame log
        Game game; Camera cam;
        readonly Dictionary<Enemy, float> seen = new(); readonly List<Enemy> gone = new();
        float lastStop = -9;
        Transform bar, barFill; TextMeshPro barText; readonly List<Transform> ticks = new(); float shownMax;

        void LateUpdate()
        {
            if (game == null) { game = FindAnyObjectByType<Game>(); if (game == null) return; }
            cam = Camera.main; float dt = Time.deltaTime;
            bool live = game.run && !Game.SimRun && (game.state == Game.State.Playing || game.state == Game.State.Paused || game.state == Game.State.Choosing);
            FrameLogTick(live && game.state == Game.State.Playing);
            HpBar(live);
            if (!live) { seen.Clear(); TickGems(dt); return; }
            foreach (var e in game.swarm.enemies)
            {
                if (e.type.nest) continue;
                if (seen.TryGetValue(e, out float hp0) && e.hp < hp0 - .01f && e.elite && HitStop && Time.unscaledTime - lastStop > .3f) { Feel.Dip(.08f, .03f); lastStop = Time.unscaledTime; }
                seen[e] = e.hp;
            }
            gone.Clear(); foreach (var kv in seen) if (kv.Key.dead || !game.swarm.enemies.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var e in gone)
            {
                seen.Remove(e); if (!e.dead) continue;
                if (HitStop && (e.elite || e.boss || e.scale >= 1.5f) && Time.unscaledTime - lastStop > .2f) { Feel.Dip(.05f, .05f); lastStop = Time.unscaledTime; }
                if (KillGems) Gems(MazeMap.ToWorld(e.p, .4f), e.elite ? 6 : 3);
            }
            TickGems(dt);
        }

        void HpBar(bool live)
        {
            bool show = live && HeroHpBar && game.player != null;
            if (bar == null)
            {
                if (!show) return;
                bar = new GameObject("HeroHpBar").transform; bar.SetParent(transform, false);
                var back = Quad("Back", bar, Mats.Fade(new Color(.05f, .06f, .12f, .85f))); back.localScale = new Vector3(1.12f, .2f, 1);
                barFill = Quad("Fill", bar, Mats.Glow(new Color(.35f, .95f, .35f, 1f)));
                var tgo = new GameObject("Hp"); tgo.transform.SetParent(bar, false); tgo.transform.localPosition = new Vector3(0, .26f, -.02f);
                barText = tgo.AddComponent<TextMeshPro>(); barText.font = Resources.Load<TMP_FontAsset>("BlueUI/Fonts/Display") ?? TMP_Settings.defaultFontAsset; barText.fontSize = 2.6f;
                barText.alignment = TextAlignmentOptions.Center; barText.textWrappingMode = TextWrappingModes.NoWrap; barText.outlineWidth = .25f; barText.outlineColor = new Color32(14, 18, 40, 255);
                barText.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            bar.gameObject.SetActive(show); if (!show) return;
            var p = game.player; float k = Mathf.Clamp01(p.hp / Mathf.Max(1, p.maxHp));
            bar.position = MazeMap.ToWorld(p.p, 2.75f); if (cam != null) bar.rotation = cam.transform.rotation;
            barFill.localPosition = new Vector3(-.53f + .53f * k, 0, -.01f); barFill.localScale = new Vector3(1.06f * k, .14f, 1);
            barFill.GetComponent<MeshRenderer>().sharedMaterial.SetColor("_BaseColor", k > .5f ? new Color(.35f, .95f, .35f) : k > .25f ? new Color(1f, .8f, .25f) : new Color(1f, .3f, .25f));
            barText.text = Mathf.CeilToInt(p.hp).ToString();
            if (!Mathf.Approximately(shownMax, p.maxHp))
            {   // segment ticks every 20 HP
                shownMax = p.maxHp; foreach (var t in ticks) Destroy(t.gameObject); ticks.Clear();
                var tm = Mats.Fade(new Color(.05f, .06f, .12f, .9f));
                for (float v = 20; v < p.maxHp - 1; v += 20) { var t = Quad("Tick", bar, tm); t.localPosition = new Vector3(-.53f + 1.06f * v / p.maxHp, 0, -.02f); t.localScale = new Vector3(.018f, .14f, 1); ticks.Add(t); }
            }
        }
        static Mesh quad;
        static Transform Quad(string n, Transform parent, Material m)
        {
            if (quad == null) { var q = GameObject.CreatePrimitive(PrimitiveType.Quad); quad = q.GetComponent<MeshFilter>().sharedMesh; Destroy(q); }
            var go = new GameObject(n); go.transform.SetParent(parent, false); go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; return go.transform;
        }

        // kill gems: scatter on a short arc, then fly into the hero
        class Gem { public Transform t; public Vector3 a, b; public float age; }
        readonly List<Gem> gems = new(); readonly Stack<Gem> gemPool = new(); Material gemMat; static Mesh gemMesh;
        void Gems(Vector3 at, int n)
        {
            if (gems.Count > 90) return;
            gemMat ??= Mats.Glow(new Color(.45f, .95f, 1f, 1f));
            if (gemMesh == null) { gemMesh = new Mesh { name = "Gem" }; gemMesh.vertices = new[] { new Vector3(0, .5f, 0), new Vector3(.3f, 0, 0), new Vector3(0, 0, .3f), new Vector3(-.3f, 0, 0), new Vector3(0, 0, -.3f), new Vector3(0, -.5f, 0) }; gemMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1 }; gemMesh.RecalculateNormals(); }
            for (int i = 0; i < n; i++)
            {
                var g = gemPool.Count > 0 ? gemPool.Pop() : new Gem { t = new GameObject("Gem").transform };
                if (g.t.GetComponent<MeshFilter>() == null) { g.t.SetParent(transform, false); g.t.gameObject.AddComponent<MeshFilter>().sharedMesh = gemMesh; var r = g.t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = gemMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                float a = Random.value * Mathf.PI * 2, d = Random.Range(.6f, 1.4f);
                g.a = at; g.b = at + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); g.b.y = .25f; g.age = 0; g.t.localScale = Vector3.one * .32f; g.t.gameObject.SetActive(true); gems.Add(g);
            }
        }
        void TickGems(float dt)
        {
            Vector3 hero = game != null && game.player != null ? MazeMap.ToWorld(game.player.p, .9f) : Vector3.zero;
            for (int i = gems.Count - 1; i >= 0; i--)
            {
                var g = gems[i]; g.age += dt;
                if (g.age < .35f) { float u = g.age / .35f; var p = Vector3.Lerp(g.a, g.b, u); p.y += 1.6f * u * (1 - u) * 2f; g.t.position = p; }
                else if (g.age < .45f) g.t.position = g.b;
                else { float u = Mathf.Clamp01((g.age - .45f) / .3f); g.t.position = Vector3.Lerp(g.b, hero, u * u); g.t.localScale = Vector3.one * .32f * (1 - .5f * u); }
                g.t.rotation = Quaternion.Euler(0, g.age * 360f, 0);
                if (g.age >= .75f || game == null || !game.run) { g.t.gameObject.SetActive(false); gems.RemoveAt(i); gemPool.Push(g); }
            }
        }

        // frame times per floor
        readonly List<float> frames = new(); int logFloor = -1; string logLayout;
        public static bool FrameLogOn => FrameLog && (Prefs.ShowFps || PlayerPrefs.GetInt("ms.framelog", 0) == 1);
        void FrameLogTick(bool playing)
        {
            if (!FrameLogOn) { frames.Clear(); logFloor = -1; return; }
            if (game.floor != logFloor || !game.run) { Flush(); logFloor = game.run ? game.floor : -1; logLayout = MazeMap.LayoutName(MazeMap.MapIndex); }
            if (playing) frames.Add(Time.unscaledDeltaTime * 1000f);
        }
        void OnApplicationPause(bool paused) { if (paused) Flush(); }
        void OnApplicationQuit() => Flush();
        void Flush()
        {
            if (frames.Count < 30 || game == null) { frames.Clear(); return; }
            frames.Sort(); float P(float q) => frames[Mathf.Clamp(Mathf.RoundToInt(q * (frames.Count - 1)), 0, frames.Count - 1)];
            int over17 = 0, over33 = 0; foreach (var f in frames) { if (f > 17.5f) over17++; if (f > 34f) over33++; }
            var path = Path.Combine(Application.persistentDataPath, "frametimes.csv"); bool head = !File.Exists(path);
            try
            {
                using var w = new StreamWriter(path, true);
                if (head) w.WriteLine("utc,build,sector,layout,floor,frames,p50_ms,p95_ms,p99_ms,max_ms,over_17ms_pct,over_34ms_pct,target_fps");
                w.WriteLine($"{System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ},{Application.version},{game.sector?.id},{logLayout},{logFloor},{frames.Count},{P(.5f):0.0},{P(.95f):0.0},{P(.99f):0.0},{frames[^1]:0.0},{100f * over17 / frames.Count:0.0},{100f * over33 / frames.Count:0.0},{Application.targetFrameRate}");
            }
            catch (System.Exception ex) { Debug.LogWarning("FeelFx frame log: " + ex.Message); }
            frames.Clear();
        }
    }
}
