using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Maze
{
    // DEPLOY -> SECTOR SELECT (a live holo planet with sector pins, a side panel and the sector card carousel)
    // -> MISSION BRIEFING -> LAUNCH -> the drop-bay countdown -> the main menu's blast-door drop into the level.
    // Built from Concepts/MapSelect (a holo planet, b sector cards, d briefing, e drop transition; the c route map
    // is a COMING SOON tab). Holo style and widgets from Holo/MainMenu. Only the first sector (the current maze,
    // Cryo Station) is playable; the others show as locked.
    public partial class MapSelect : MonoBehaviour
    {
        public static MapSelect I;

        public class Sector
        {
            public string name, art, mission, unlock; public int threat, stars, map; public float lat, lon; public bool lockedFixed; // map: MazeMap.MapIndex
            public int tier;          // a progression sector's tier (5-floor runs, Recommended Power); 0 = a test map
            public SectorDef def;     // the drop zone's data (SectorDefs.cs); null for the Escape Test map
            public bool locked => !Prefs.TestMode && (def != null ? !Progress.Unlocked(def) : lockedFixed);   // TEST MODE: every pin
            public int Rec => def != null ? Meta.RecommendedAt(def, Meta.Difficulty) : Progress.Recommended(tier);
        }
        // built from SectorDefs (every drop zone is a pin), plus the Escape Test map at the end
        static Sector[] sectors;
        public static Sector[] Sectors => sectors ??= BuildSectors();
        static Sector[] BuildSectors()
        {
            var l = new List<Sector>();
            foreach (var d in SectorDefs.All)
            {
                var after = d.unlockAfter != null ? SectorDefs.Get(d.unlockAfter) : null;
                int pips = Mathf.Clamp(Mathf.CeilToInt(d.tier * 5f / 8f), 1, 5);
                l.Add(new Sector { name = d.name, art = d.art, mission = "SURVIVE", threat = pips, stars = pips, lat = d.lat, lon = d.lon, tier = d.tier, def = d,
                                   unlock = after != null ? $"CLEAR {after.name} TO UNLOCK" : "" });
            }
            l.Add(new Sector { name = "ESCAPE TEST", art = "kessler", mission = "ESCAPE", threat = 3, stars = 3, lat = -2f, lon = -60f, map = 1 });
            return l.ToArray();
        }
        public static int Selected
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("ms.sector", 0), 0, Sectors.Length - 1);
            set { PlayerPrefs.SetInt("ms.sector", value); PlayerPrefs.Save(); }
        }

        enum Mode { Hidden, Select, Brief, Drop }
        Mode mode = Mode.Hidden;
        Game game; MainMenu menu;
        Canvas canvas; RectTransform canvasRt;
        RectTransform selectRt, briefRt, dropRt;
        CanvasGroup selectCg, briefCg, dropCg;
        float t, modeT, idleT;
        bool waitingLoadout;

        public bool Open => mode != Mode.Hidden;

        public void Init(Game g, MainMenu m)
        {
            I = this; game = g; menu = m;
            canvas = Holo.MakeCanvas(transform, "MapSelectCanvas", 12);
            canvasRt = (RectTransform)canvas.transform;
            var bgTex = Prefs.HeroBackdrop();
            selectRt = MakeScreen("Select", bgTex, 0.5f, out selectCg);
            briefRt = MakeScreen("Brief", bgTex, 0.84f, out briefCg);
            dropRt = Holo.Fill(canvas.transform, "Drop"); dropCg = dropRt.gameObject.AddComponent<CanvasGroup>();
            BuildSelect(); BuildBrief(); BuildDrop();
            wind = gameObject.AddComponent<AudioSource>(); wind.playOnAwake = false; wind.loop = false;
            wind.clip = Resources.Load<AudioClip>("Audio/drop_wind");
            SetMode(Mode.Hidden);
        }

        RectTransform MakeScreen(string name, Texture bg, float dim, out CanvasGroup cg)
        {
            var rt = Holo.Fill(canvas.transform, name);
            if (bg != null) { var r = Holo.Raw(Holo.Fill(rt, "Bg"), bg, Color.white); r.gameObject.AddComponent<CoverUv>().aspect = (float)bg.width / bg.height; bgRaws.Add(r); }
            Holo.Img(Holo.Fill(rt, "Dim"), Holo.WhiteSprite, new Color(0, 0.02f, 0.05f, dim), false, true);
            Holo.Raw(Holo.Fill(rt, "Scan"), Holo.ScanTex, new Color(0, 0, 0, 0.12f)).gameObject.AddComponent<ScanUv>();
            cg = rt.gameObject.AddComponent<CanvasGroup>();
            return rt;
        }

        void SetMode(Mode m)
        {
            mode = m; modeT = 0;
            canvas.enabled = m != Mode.Hidden;
            menu.SetCanvasVisible(m == Mode.Hidden); // the menu underneath isn't drawn while map select covers it
            selectRt.gameObject.SetActive(m == Mode.Select);
            briefRt.gameObject.SetActive(m == Mode.Brief);
            dropRt.gameObject.SetActive(m == Mode.Drop);
            if (planetRig != null) { planetRig.SetActive(m == Mode.Select); planetCam.enabled = m == Mode.Select; }
        }

        // ---------------------------------------------------------------- entry points
        // backdrops and the OPERATIVE frame follow the Squad pick
        readonly List<RawImage> bgRaws = new();
        public void RefreshHero()
        {
            var bd = Prefs.HeroBackdrop();
            foreach (var r in bgRaws) if (r != null && bd != null && r.texture != bd) { r.texture = bd; r.GetComponent<CoverUv>().aspect = (float)bd.width / bd.height; }
            var bay = Prefs.HeroDropBay();
            if (dropBg != null && bay != null && dropBg.texture != bay) { dropBg.texture = bay; var cu = dropBg.GetComponent<CoverUv>(); if (cu != null) cu.aspect = (float)bay.width / bay.height; }
            RefreshOperative();
        }

        public void Show()
        {
            hotId = Ops.HotZone?.id;
            RefreshTestBadge();
            seenCache.Clear(); foreach (var z in SectorDefs.All) if (Seen(z)) seenCache.Add(z.id);
            RefreshHero(); RefreshStarTrack();
            EnsurePlanet();
            SetMode(Mode.Select);
            Pick(Selected, false);
            Sfx.Open(); Haptics.Light();
            Debug.Log($"Maze map select: shown, sector {Sectors[Selected].name}, planet {(planetRt != null ? planetRt.width : 0)} px");
        }
        public void Hide() { SetMode(Mode.Hidden); }
        // (also driven by the editor screenshot tool)
        public void PickSector(int i) => Pick(i, true);
        public void OpenBriefing() => OpenBrief();
        public void LaunchDrop() => Launch();
        public RenderTexture PlanetRT => planetRt;
        void Back() { Sfx.Close(); if (mode == Mode.Brief) { SetMode(Mode.Select); Pick(Selected, false); } else Hide(); }

        void Launch()
        {
            game.LoadMap(Sectors[Selected].map); // rebuilds the level behind the countdown
            Game.PendingTier = Sectors[Selected].tier; // a progression sector: the drop starts a 5-floor run
            Game.PendingSector = Sectors[Selected].def;
            dropSector.text = Sectors[Selected].name;
            SetMode(Mode.Drop);
            lastCount = -1;
            if (wind.clip != null && Prefs.Sound) wind.Play();
            Haptics.Medium();
        }

        void FinishDrop()
        {
            if (mode != Mode.Drop) return;
            wind.Stop();
            Hide();                 // (shows the menu canvas again: the drop transition lives there)
            menu.Deploy(); // the blast doors close on the bridge and open on the level
        }

        // ---------------------------------------------------------------- planet (rendered into a texture)
        GameObject planetRig; Camera planetCam; RenderTexture planetRt;
        Transform planet; Material infestMat;
        Quaternion q = Quaternion.Euler(-15, 0, 0), qGoal; bool focusing;
        const int PlanetLayer = 30;
        static readonly Vector3 RigAt = new Vector3(0, 1000, 0);

        void EnsurePlanet()
        {
            if (planetRig != null) return;
            planetRig = new GameObject("MapPlanet"); planetRig.transform.position = RigAt;
            planetRt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "MapPlanet" };
            planetCam = new GameObject("PlanetCam").AddComponent<Camera>();
            planetCam.transform.SetParent(planetRig.transform, false);
            planetCam.transform.localPosition = new Vector3(0, 0, -3.4f);
            planetCam.fieldOfView = 38; planetCam.nearClipPlane = 0.5f; planetCam.farClipPlane = 10;
            planetCam.clearFlags = CameraClearFlags.SolidColor; planetCam.backgroundColor = new Color(0, 0, 0, 0);
            planetCam.cullingMask = 1 << PlanetLayer; planetCam.targetTexture = planetRt;
            planetRaw.texture = planetRt;

            var sphere = Sphere(96, 48);
            planet = new GameObject("Planet").transform; planet.SetParent(planetRig.transform, false);
            var tex = Resources.Load<Texture2D>("UI/planet");
            Part(planet, "Surface", sphere, 1f, Mats.Unlit(Color.white, tex));
            var inf = Resources.Load<Texture2D>("UI/planet_infest");
            if (inf != null) { infestMat = Mats.UnlitClip(inf, 0.3f); Part(planet, "Infestation", sphere, 1.003f, infestMat); }
            // fixed to the camera: night side and a cyan holo rim (vertex colours, particle shaders)
            var L = new Vector3(-0.55f, 0.45f, -0.7f).normalized;
            Part(planetRig.transform, "Shade", Shaded(sphere, n => new Color(0, 0.02f, 0.06f, 0.82f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.1f, -0.6f, Vector3.Dot(n, L))))), 1.006f, Mats.ParticlesFade(Texture2D.whiteTexture));
            Part(planetRig.transform, "Rim", Shaded(sphere, n => { float f = Mathf.Pow(1 - Mathf.Abs(n.z), 3.2f); return new Color(0.3f, 0.85f, 1f, 1) * f * 1.2f; }), 1.02f, Mats.Particles(true));
            foreach (var tr in planetRig.GetComponentsInChildren<Transform>()) tr.gameObject.layer = PlanetLayer;
        }

        static void Part(Transform parent, string name, Mesh m, float r, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * r;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        // equirectangular UV sphere; lat/lon -> (cos lat sin lon, sin lat, -cos lat cos lon), lon 0 faces the camera
        static Mesh Sphere(int seg, int rings)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float lat = Mathf.PI * (j / (float)rings - 0.5f);
                for (int i = 0; i <= seg; i++)
                {
                    float lon = 2 * Mathf.PI * (i / (float)seg - 0.5f);
                    v.Add(Dir(lat, lon)); uv.Add(new Vector2(i / (float)seg, j / (float)rings));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + seg + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
        static Vector3 Dir(float lat, float lon) => new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), -Mathf.Cos(lat) * Mathf.Cos(lon));
        static Vector3 DirDeg(float lat, float lon) => Dir(lat * Mathf.Deg2Rad, lon * Mathf.Deg2Rad);

        static Mesh Shaded(Mesh src, System.Func<Vector3, Color> f)
        {
            var m = Instantiate(src); var n = src.normals; var c = new Color[n.Length];
            for (int i = 0; i < n.Length; i++) c[i] = f(n[i]);
            m.colors = c; return m;
        }

        void Pick(int i, bool sound)
        {
            Selected = i;
            var pd = i >= 0 && i < Sectors.Length ? Sectors[i].def : null; if (pd != null && !Sectors[i].locked && seenCache.Add(pd.id)) { PlayerPrefs.SetInt("ms.seen." + pd.id, 1); PlayerPrefs.Save(); }   // NEW goes once opened
            var s = Sectors[i];
            if (cardStrip != null)
            {   // keep the picked card in view
                float vw = ((RectTransform)cardStrip.transform).rect.width, cw = cardStrip.content.rect.width, x = i * CardStep + 18;
                float want = Mathf.Clamp(x - vw / 2 + CardW / 2, 0, Mathf.Max(0, cw - vw));
                stripTarget = cw > vw ? want / (cw - vw) : 0; stripT = 0.5f;
            }
            // turn the planet so the pin faces the camera (keeping north roughly up)
            var d = q * DirDeg(s.lat, s.lon);
            qGoal = Quaternion.FromToRotation(d, Vector3.back) * q;
            focusing = true; idleT = 0;
            RefreshPanel();
            if (sound) { Sfx.Click(); Haptics.Light(); }
        }

        void PickTier(int t)
        {
            var def = Sectors[Selected].def; if (def == null) return;
            var gate = Meta.TierGateFor(def, t);
            if (gate != null) { Sfx.Deny(); Haptics.Warn(); return; }
            Meta.Difficulty = t; Sfx.Toggle(); RefreshTiers(); RefreshOperative();
        }
        void OpenLoadout()
        {
            canvas.enabled = false; waitingLoadout = true;
            menu.SetCanvasVisible(true);
            menu.OpenSheet("loadout");
        }

        // ---------------------------------------------------------------- DROP screen (concept e)
        RawImage dropBg; readonly List<(RectTransform rt, float speed, float y)> clouds = new();
        AudioSource wind; int lastCount;
        float routeT;
        const float DropBeat = 0.7f; // 3, 2, 1 -> the blast doors

        void BuildDrop()
        {
            var tex = Prefs.HeroDropBay(); // the drop-bay painting shows the hero too (a Vanguard version once it exists)
            dropBg = Holo.Raw(Holo.Fill(dropRt, "Bay"), tex, Color.white);
            if (tex != null) dropBg.gameObject.AddComponent<CoverUv>().aspect = (float)tex.width / tex.height;
            dropBg.raycastTarget = true;
            for (int i = 0; i < 7; i++)
            {
                var c = Holo.Rect(dropRt, "Cloud" + i, new Vector2(0, 0.5f), Vector2.zero, new Vector2(900, 300));
                Holo.Img(c, Holo.SoftSprite, new Color(1, 1, 1, 0.18f + 0.05f * (i % 3)));
                clouds.Add((c, 2400f + i * 420f, Random.Range(-420f, 420f)));
            }
            Holo.Img(Holo.Fill(dropRt, "Vignette"), Holo.WhiteSprite, new Color(0, 0, 0, 0)).raycastTarget = false;
            var safe = Holo.SafeRoot(dropRt);
            var panel = Holo.Panel(safe, "Count");
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f); panel.anchoredPosition = new Vector2(-120, 260); panel.sizeDelta = new Vector2(820, 150);
            dropCount = Holo.Label(Holo.Fill(panel, "T"), "", 88, Holo.Ink); dropCount.fontStyle = FontStyle.Bold; Holo.GlowText(dropCount, Holo.Cyan, 0.6f);
            var sub = Holo.Panel(safe, "Zone");
            sub.anchorMin = sub.anchorMax = sub.pivot = new Vector2(0.5f, 0.5f); sub.anchoredPosition = new Vector2(-120, 148); sub.sizeDelta = new Vector2(520, 70);
            dropSector = Holo.Label(Holo.Fill(sub, "T"), "", 36, Holo.Acid); dropSector.fontStyle = FontStyle.Bold;
            var skip = Holo.Label(Holo.Rect(safe, "Skip", new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(600, 34)), "TAP TO SKIP", 24, Holo.A(Holo.Ink, 0.7f));
            skip.raycastTarget = false;
            dropRt.gameObject.AddComponent<Tap>().onTap = () => FinishDrop();
        }

        // ---------------------------------------------------------------- frame
        void Update()
        {
            if (mode == Mode.Hidden)
            {
                if (waitingLoadout && !menu.SheetOpen) { waitingLoadout = false; canvas.enabled = true; RefreshLoadout(); }
                return;
            }
            if (waitingLoadout)
            {
                if (menu.SheetOpen) { canvas.enabled = false; return; }
                waitingLoadout = false; canvas.enabled = true; menu.SetCanvasVisible(false); RefreshLoadout(); Sfx.Close();
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            t += dt; modeT += dt; idleT += dt;
            if (mode == Mode.Select) TickSelect(dt);
            if (mode == Mode.Drop) TickDrop(dt);
            if (mode == Mode.Brief) FitHero();
            var cg = mode == Mode.Select ? selectCg : mode == Mode.Brief ? briefCg : dropCg;
            cg.alpha = Mathf.Clamp01(modeT * 5);
        }

        void TickDrop(float dt)
        {
            float k = modeT;
            int count = 3 - Mathf.FloorToInt(k / DropBeat);
            if (count != lastCount && count >= 1)
            {
                lastCount = count; dropCount.text = "DROPPING IN " + count;
                Sfx.Click(); Haptics.Light();
                dropCount.rectTransform.localScale = Vector3.one * 1.25f;
            }
            dropCount.rectTransform.localScale = Vector3.Lerp(dropCount.rectTransform.localScale, Vector3.one, dt * 12);
            dropBg.rectTransform.localScale = Vector3.one * (1 + 0.06f * k); // a slow push in
            var sh = Random.insideUnitCircle * (4 + 6 * k);
            dropBg.rectTransform.anchoredPosition = sh;
            float W = canvasRt.rect.width;
            for (int i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                float x = Mathf.Repeat(k * c.speed + i * 517f, W + 1800) - 900;
                c.rt.anchoredPosition = new Vector2(x, c.y);
            }
            if (k >= DropBeat * 3) FinishDrop();
        }

        // ---------------------------------------------------------------- helpers
        static Text Lbl(RectTransform parent, string name, Vector2 pos, string text, int size, Color c)
        {
            var l = Holo.Label(Holo.Rect(parent, name, new Vector2(0, 1), pos, new Vector2(560, 36)), text, size, c, TextAnchor.UpperLeft);
            l.rectTransform.pivot = new Vector2(0, 1); l.rectTransform.anchoredPosition = pos;
            return l;
        }

        static HoloButton Btn(RectTransform parent, string name, string label, string icon, Vector2 size, Vector2 anchor, Vector2 pos, int font, System.Action a, bool hero = false)
        {
            var b = Holo.Button(parent, name, size, label, icon, font, hero);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(anchor.x, anchor.y); rt.anchoredPosition = pos;
            b.onClick = () => { a(); };
            return b;
        }
    }

    // a RawImage that covers its rect like CSS background-size: cover
    public class CoverUv : MonoBehaviour
    {
        public float aspect = 1;
        RawImage img; RectTransform rt;
        void Awake() { img = GetComponent<RawImage>(); rt = (RectTransform)transform; }
        void LateUpdate()
        {
            var r = rt.rect; if (r.height < 1) return;
            float a = r.width / r.height;
            img.uvRect = a > aspect ? new Rect(0, 0.5f - aspect / a / 2, 1, aspect / a) : new Rect(0.5f - a / aspect / 2, 0, a / aspect, 1);
        }
    }

    public class ScanUv : MonoBehaviour
    {
        RawImage img; RectTransform rt;
        void Awake() { img = GetComponent<RawImage>(); rt = (RectTransform)transform; }
        void LateUpdate() { img.uvRect = new Rect(0, Time.unscaledTime * 0.5f, 1, rt.rect.height / 4f); }
    }

    // drag on a UI element (the planet)
    public class DragArea : MonoBehaviour, IDragHandler
    {
        public System.Action<Vector2> onDrag;
        public void OnDrag(PointerEventData e) => onDrag?.Invoke(e.delta);
    }
}
