using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Maze
{
    // Life on the painted hangar (2026-10-16, Manuel: "the start screen looks very plain"). The hangar home is one painted
    // plate (RichHangar/Art/scene-rich-extended.png, another thread's) under live buttons; nothing there is touched. These
    // layers ride on top of that plate's RawImage ("RichHangarScene"), in its own image coordinates (u right, v down), so they
    // follow its fit on every screen. One toggle per layer (true = on):
    //   Lights   the painted fixtures glow: blue panel lights breathe, warm warning lights blink in a slow rhythm
    //   Holo     the holo map table shimmers, with a scan line sweeping up through it every few seconds
    //   Snow     snow drifting in through the bay opening, a cold haze breathing at its foot
    //   Drones   three small drones with blinking lights flying slow loops through the bay
    //   Ships    two far ships drifting across the sky (off: against the painted sky full of ships they read as grey blocks)
    //   Breath   the plate breathes (a ~0.6% slow scale) and, on the phone, tilts a touch with the gyro (clamped; Gyro, and
    //            PlayerPrefs "ms.hangargyro" = 0 turns it off)
    //   Hum      a quiet low hangar hum under the menu music (procedural, ~5% volume, only with sound on)
    public class HangarLife : MonoBehaviour
    {
        public static bool On = true, Lights = true, Holo = true, Snow = true, Drones = true, Ships = false, Breath = true, Gyro = true, Hum = true;
        public static bool GyroPref { get => PlayerPrefs.GetInt("ms.hangargyro", 1) == 1; set { PlayerPrefs.SetInt("ms.hangargyro", value ? 1 : 0); PlayerPrefs.Save(); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var go = new GameObject("HangarLife"); DontDestroyOnLoad(go); go.AddComponent<HangarLife>(); }

        RawImage plate; RectTransform layer; AudioSource hum; float t;
        static Sprite dot, glow, drone, ship;
        // the painting's fixtures (found from its bright saturated spots, outside the bay, hero and table)
        static readonly Vector2[] BlueLights = { new(.700f, .798f), new(.517f, .826f), new(.582f, .602f), new(.674f, .602f), new(.765f, .602f), new(.700f, .574f), new(.896f, .882f), new(.634f, .602f), new(.608f, .630f), new(.085f, .742f), new(.281f, .406f), new(.909f, .042f), new(.804f, .574f) };
        static readonly Vector2[] WarmLights = { new(.961f, .742f), new(.268f, .126f), new(.163f, .154f), new(.229f, .126f), new(.190f, .126f), new(.033f, .882f), new(.150f, .686f), new(.190f, .266f), new(.033f, .770f), new(.255f, .630f), new(.072f, .518f), new(.961f, .602f), new(.281f, .294f) };
        static Vector2 TableC => CohesiveZone.Enabled ? new Vector2(.868f, .565f) : new Vector2(.71f, .64f);
        static Vector2 TableSize => CohesiveZone.Enabled ? new Vector2(.13f, .11f) : new Vector2(.44f, .19f);
        static readonly Vector2[] PurpleBlueLights = { new(.246f,.038f), new(.954f,.128f), new(.963f,.501f), new(.855f,.614f) };
        static readonly Vector2[] PurpleWarmLights = { new(.189f,.226f), new(.153f,.326f), new(.670f,.590f), new(.412f,.589f), new(.948f,.329f) };
        static readonly Rect Bay = new(.34f, 0f, .53f, .54f);

        class Fx { public RectTransform rt; public Image img; public Vector2 uv, v, sz; public float phase, size, kind; }
        readonly List<Fx> lights = new(), snow = new(), drones = new(), ships = new(); Fx tableGlow, scan; readonly List<Fx> haze = new();

        void LateUpdate()
        {
            t += Time.unscaledDeltaTime;
            if (plate == null) { if ((Time.frameCount & 15) == 0) Find(); return; }
            bool shown = On && plate.isActiveAndEnabled && plate.canvas != null && plate.canvas.enabled;
            if (layer != null) layer.gameObject.SetActive(shown);
            HumTick(shown);
            HeroTick(shown);
            if (!shown) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            var size = new Vector2(2.14f, 1f);   // (painting units: width / height of the plate)
            // lights
            foreach (var f in lights)
            {
                f.rt.gameObject.SetActive(Lights); if (!Lights) continue;
                float a = f.kind == 0 ? .35f + .4f * (.5f + .5f * Mathf.Sin(t * 1.3f + f.phase))                                  // blue: breathe
                                      : (Mathf.Repeat(t * .55f + f.phase, 1f) < .18f ? 1f : .15f);                             // warm: a slow warning blink
                var c = f.img.color; c.a = a; f.img.color = c;
            }
            // the holo table: a shimmer and a scan line sweeping up through it every ~4 s
            if (tableGlow != null)
            {
                tableGlow.rt.gameObject.SetActive(Holo); scan.rt.gameObject.SetActive(Holo);
                var c = tableGlow.img.color; c.a = .16f + .08f * Mathf.Sin(t * 2.1f) + .04f * Mathf.Sin(t * 7.3f); tableGlow.img.color = c;
                float u = Mathf.Repeat(t / 4f, 1f), y = Mathf.Lerp(TableC.y + TableSize.y * .35f, TableC.y - TableSize.y * .9f, u);
                Place(scan.rt, new Vector2(TableC.x, y), new Vector2(TableSize.x * 2.14f * (1 - Mathf.Abs(u - .5f) * .9f), .006f));
                var sc = scan.img.color; sc.a = Mathf.Sin(u * Mathf.PI) * .55f; scan.img.color = sc;
            }
            // snow through the bay, the haze at its foot
            foreach (var f in snow)
            {
                f.rt.gameObject.SetActive(Snow); if (!Snow) continue;
                f.uv += f.v * dt; if (f.uv.y > .78f || f.uv.x < -.02f) { f.uv = new Vector2(Random.Range(.05f, 1.02f), -.02f); }
                var k = Mathf.Clamp01((.78f - f.uv.y) / .12f); var c = f.img.color; c.a = .9f * k; f.img.color = c;
                Place(f.rt, f.uv + new Vector2(Mathf.Sin(t * .9f + f.phase) * .004f, 0), Vector2.one * f.size * size.y);
            }
            foreach (var f in haze) { f.rt.gameObject.SetActive(Snow); var c = f.img.color; c.a = .07f + .05f * Mathf.Sin(t * .35f + f.phase); f.img.color = c; }
            // drones: slow loops through the bay with blinking lights
            foreach (var f in drones)
            {
                f.rt.gameObject.SetActive(Drones); if (!Drones) continue;
                float a = t * .06f * f.size + f.phase;
                var p = new Vector2(Bay.center.x + Mathf.Cos(a) * Bay.width * .42f, Bay.yMin + Bay.height * (.35f + .18f * Mathf.Sin(a * 2f)));
                Place(f.rt, p, new Vector2(.03f, .016f) * size.y * 1.6f * (.8f + .2f * f.size)); f.rt.localScale = new Vector3(Mathf.Sin(a) > 0 ? -1 : 1, 1, 1);
                var c = f.img.color; c.a = .85f; f.img.color = c;
                var blink = f.rt.GetChild(0).GetComponent<Image>(); var bc = blink.color; bc.a = Mathf.Repeat(t * 1.4f + f.phase, 1f) < .15f ? 1f : .15f; blink.color = bc;
            }
            // far ships drifting across the sky
            foreach (var f in ships)
            {
                f.rt.gameObject.SetActive(Ships); if (!Ships) continue;
                f.uv.x += f.v.x * dt; if (f.uv.x > Bay.xMax + .03f) f.uv.x = Bay.xMin - .03f;
                float edge = Mathf.Clamp01(Mathf.Min(f.uv.x - Bay.xMin, Bay.xMax - f.uv.x) / .05f); var c = f.img.color; c.a = .5f * edge; f.img.color = c;
                Place(f.rt, f.uv + new Vector2(0, Mathf.Sin(t * .4f + f.phase) * .003f), new Vector2(.06f, .018f) * size.y * f.size);
            }
            // the plate breathes; the gyro tilts it a touch (clamped)
            breath = Breath && !CohesiveZone.Enabled ? 1.012f + .006f * Mathf.Sin(t * .5f) : 1f;
            float tilt = 0;
            if (Breath && !CohesiveZone.Enabled && Gyro && GyroPref && SystemInfo.supportsGyroscope) { if (!Input.gyro.enabled) Input.gyro.enabled = true; tilt = Mathf.Clamp(Input.gyro.gravity.x, -.6f, .6f); }
            tiltNow = Mathf.Lerp(tiltNow, tilt, dt * 3f);
        }

        void Find()
        {
            var go = GameObject.Find("RichHangarScene"); if (go == null) return;
            plate = go.GetComponent<RawImage>(); if (plate == null) return;
            if (hero != null) Destroy(hero.gameObject);
            hero = null; heroImg = heroShadow = null; heroTried = herolessTried = false; paintedTex = herolessTex = null;
            lights.Clear(); snow.Clear(); drones.Clear(); ships.Clear(); haze.Clear(); placed.Clear(); tableGlow = scan = null;
            Sprites();
            layer = new GameObject("HangarLife Layers", typeof(RectTransform)).GetComponent<RectTransform>(); layer.SetParent(plate.transform, false);
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one; layer.offsetMin = layer.offsetMax = Vector2.zero; layer.gameObject.layer = plate.gameObject.layer;
            var size = Vector2.one;   // (sizes in painting-height units)
            if (CohesiveZone.Enabled)
            {
                for (int i = 0; i < PurpleBlueLights.Length; i++) lights.Add(Make(glow, PurpleBlueLights[i], Vector2.one * .038f, new Color(.45f,.87f,1f,.16f), i*.7f, 0));
                for (int i = 0; i < PurpleWarmLights.Length; i++) lights.Add(Make(glow, PurpleWarmLights[i], Vector2.one * .042f, new Color(1f,.63f,.28f,.14f), i*.6f, 0));
                tableGlow = Make(glow, TableC, new Vector2(.29f,.12f), new Color(.3f,.85f,1f,.12f),0,0);
                scan = Make(dot, TableC, Vector2.one, new Color(.6f,.95f,1f,.22f),0,0);
                foreach (var tr in layer.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = plate.gameObject.layer;
                return;
            }
            for (int i = 0; i < BlueLights.Length; i++) lights.Add(Make(glow, BlueLights[i], Vector2.one * size.y * .075f, new Color(.55f, .9f, 1f, .3f), i * .7f, 0));
            for (int i = 0; i < WarmLights.Length; i++) lights.Add(Make(glow, WarmLights[i], Vector2.one * size.y * .07f, new Color(1f, .62f, .2f, .2f), i * .37f, 1));
            for (int i = 0; i < 3; i++) haze.Add(Make(glow, new Vector2(Bay.xMin + Bay.width * (.2f + .3f * i), Bay.yMax - .06f), new Vector2(size.y * .55f, size.y * .16f), new Color(.85f, .93f, 1f, .1f), i * 2f, 0));
            tableGlow = Make(glow, TableC, new Vector2(TableSize.x * 2.14f * 1.05f, TableSize.y * 1.2f), new Color(.3f, .85f, 1f, .2f), 0, 0);
            scan = Make(dot, TableC, Vector2.one, new Color(.6f, .95f, 1f, .5f), 0, 0);
            for (int i = 0; i < 90; i++) { var f = Make(dot, new Vector2(Random.Range(.05f, 1f), Random.Range(0f, .78f)), Vector2.one, new Color(.92f, .97f, 1f, .8f), Random.value * 6, 0); f.size = Random.Range(.007f, .015f); f.v = new Vector2(-Random.Range(.004f, .012f), Random.Range(.02f, .045f)); snow.Add(f); }
            for (int i = 0; i < 3; i++)
            {
                var f = Make(drone, Bay.center, Vector2.one, new Color(.18f, .22f, .3f, .85f), i * 2.1f, 0); f.size = 1f + .25f * i;
                var b = new GameObject("Blink", typeof(RectTransform), typeof(Image)); b.transform.SetParent(f.rt, false); var bi = b.GetComponent<Image>(); bi.sprite = glow; bi.color = i == 1 ? new Color(1f, .3f, .25f, 1f) : new Color(.4f, .9f, 1f, 1f); bi.raycastTarget = false;
                var brt = (RectTransform)b.transform; brt.anchorMin = new Vector2(.42f, 0f); brt.anchorMax = new Vector2(.58f, .35f); brt.offsetMin = brt.offsetMax = Vector2.zero; drones.Add(f);
            }
            for (int i = 0; i < 2; i++) { var f = Make(ship, new Vector2(Bay.xMin + .1f + .35f * i, .08f + .1f * i), Vector2.one, new Color(.55f, .62f, .75f, .5f), i * 3f, 0); f.size = 1f - .3f * i; f.v = new Vector2(.0035f - .0012f * i, 0); ships.Add(f); }
            foreach (var tr in layer.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = plate.gameObject.layer;
        }
        // the live 3D Vanguard (HangarHero3D, off by default) on the painted hero's spot, with a soft contact shadow; while he
        // is on, the plate becomes the hero-less one if Codex's re-export has landed
        HangarHero3D hero; RectTransform heroImg, heroShadow; Texture paintedTex, herolessTex; bool herolessTried, heroTried; string shownHero;
        void Hero()
        {
            hero = HangarHero3D.Make(); if (hero == null) return;
            shownHero = Prefs.Hero;
            hero.transform.SetParent(transform, false);
            heroShadow = Make(glow, HangarHero3D.Feet + new Vector2(.01f, .004f), new Vector2(.3f, .05f), new Color(0f, 0f, 0f, CohesiveZone.Enabled ? .65f : .4f), 0, 0).rt;
            var go = new GameObject("Hero3D", typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(layer, false);
            var ri = go.GetComponent<RawImage>(); ri.texture = hero.Texture; ri.raycastTarget = false; heroImg = ri.rectTransform;
            Place(heroImg, HangarHero3D.FrameCenter, HangarHero3D.FrameSize);
            heroShadow.SetAsLastSibling(); heroImg.SetAsLastSibling(); foreach (var f in snow) f.rt.SetAsLastSibling();   // over the painted lights and the drones (they fly behind him), under the snow
            foreach (var tr in new[] { heroImg, heroShadow }) tr.gameObject.layer = plate.gameObject.layer;
            paintedTex = plate.texture;
        }
        void HeroTick(bool shown)
        {
            if (hero == null) { if (!heroTried && shown && HangarHero3D.Enabled) { heroTried = true; Hero(); } if (hero == null) return; }
            if (shownHero != Prefs.Hero)
            {
                hero.Refresh();
                shownHero = Prefs.Hero;
                heroImg.GetComponent<UnityEngine.UI.RawImage>().texture = hero.Texture;
            }
            bool on = shown && HangarHero3D.Enabled;
            hero.Show(on); heroImg.gameObject.SetActive(on); heroShadow.gameObject.SetActive(on);
            if (on && !herolessTried) { herolessTried = true; herolessTex = CohesiveZone.Enabled ? null : Resources.Load<Texture>(HangarHero3D.HerolessPlate); }
            var want = on && herolessTex != null ? herolessTex : paintedTex;
            if (want != null && plate.texture != want) plate.texture = want;
        }
        Fx Make(Sprite s, Vector2 uv, Vector2 px, Color c, float phase, float kind)
        {
            var go = new GameObject("Fx", typeof(RectTransform), typeof(Image)); go.transform.SetParent(layer, false);
            var img = go.GetComponent<Image>(); img.sprite = s; img.color = c; img.raycastTarget = false;
            var f = new Fx { rt = (RectTransform)go.transform, img = img, uv = uv, phase = phase, kind = kind }; Place(f.rt, uv, px); return f;
        }
        readonly Dictionary<RectTransform, (Vector2 uv, Vector2 sz)> placed = new();
        // uv: painting coordinates (u right, v down); sz: size in units of the painting's height
        void Place(RectTransform rt, Vector2 uv, Vector2 sz) { placed[rt] = (uv, sz); }
        float breath = 1, tiltNow;
        // after the art fit (LateUpdate) and before the canvas draws: the breath / tilt as a small zoom of the plate's uvRect,
        // then every layer mapped from painting coordinates through the final uvRect
        void Post()
        {
            if (plate == null || layer == null || !layer.gameObject.activeInHierarchy) return;
            var r = plate.uvRect;
            if (Breath && !CohesiveZone.Enabled) { var c = r.center; float k = 1f / breath; r = new Rect(c.x - r.width * k * .5f + tiltNow * r.width * .01f, c.y - r.height * k * .5f, r.width * k, r.height * k); plate.uvRect = r; }
            var rect = ((RectTransform)plate.transform).rect;
            foreach (var kv in placed)
            {
                if (kv.Key == null || !kv.Key.gameObject.activeSelf) continue;
                var (uv, sz) = kv.Value; kv.Key.anchorMin = kv.Key.anchorMax = new Vector2((uv.x - r.x) / r.width, ((1 - uv.y) - r.y) / r.height);
                kv.Key.anchoredPosition = Vector2.zero; kv.Key.sizeDelta = sz * rect.height / r.height;
            }
            if (CohesiveZone.Enabled)
            {
                CohesiveMenuArt.PlaceHero(heroImg, plate, HangarHero3D.FrameCenter, HangarHero3D.FrameSize);
                CohesiveMenuArt.PlaceHero(heroShadow, plate, HangarHero3D.Feet + new Vector2(0,-.014f), new Vector2(.22f,.033f));
            }
        }
        void OnEnable() => Canvas.willRenderCanvases += Post;
        void OnDisable() => Canvas.willRenderCanvases -= Post;

        static void Sprites()
        {
            if (dot != null) return;
            Sprite Tex(int w, int h, System.Func<float, float, float> a)
            {
                var tx = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[w * h]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float u = (x + .5f) / w * 2 - 1, v = (y + .5f) / h * 2 - 1; px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a(u, v)) * 255)); }
                tx.SetPixels32(px); tx.Apply(); return Sprite.Create(tx, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
            }
            dot = Tex(16, 16, (u, v) => 1 - Mathf.SmoothStep(.55f, 1f, Mathf.Sqrt(u * u + v * v)));
            glow = Tex(64, 64, (u, v) => Mathf.Pow(Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v)), 2.2f));
            // a small drone: a rounded body with two side rotors (silhouette)
            drone = Tex(64, 32, (u, v) => Mathf.Max(1 - Mathf.SmoothStep(.4f, .6f, Mathf.Sqrt(u * u * 2.2f + v * v * 3f)), Mathf.Max(1 - Mathf.SmoothStep(.15f, .3f, Mathf.Sqrt((u - .7f) * (u - .7f) + (v - .35f) * (v - .35f) * 6f)), 1 - Mathf.SmoothStep(.15f, .3f, Mathf.Sqrt((u + .7f) * (u + .7f) + (v - .35f) * (v - .35f) * 6f)))));
            // a far ship: a long wedge
            ship = Tex(96, 32, (u, v) => (Mathf.Abs(v) < .55f * (1 - (u + 1) * .35f) + .1f && u > -.95f && u < .95f) ? 1f - Mathf.SmoothStep(.85f, 1f, Mathf.Abs(u)) : 0f);
        }

        // ---------------------------------------------------------------- the hum: a 4 s procedural loop, ~5% volume
        void HumTick(bool shown)
        {
            bool want = shown && Hum && !CohesiveZone.Enabled && Prefs.Sound;
            if (want && hum == null)
            {
                hum = gameObject.AddComponent<AudioSource>(); hum.loop = true; hum.playOnAwake = false; hum.spatialBlend = 0; hum.volume = 0; hum.ignoreListenerPause = true;
                int sr = 22050, n = sr * 4; var clip = AudioClip.Create("hangar_hum", n, 1, sr, false); var d = new float[n]; float lp = 0; var rnd = new System.Random(3);
                for (int i = 0; i < n; i++) { float s = i / (float)sr; lp += ((float)rnd.NextDouble() * 2 - 1 - lp) * .02f; d[i] = .35f * Mathf.Sin(2 * Mathf.PI * 55 * s) + .18f * Mathf.Sin(2 * Mathf.PI * 110 * s + .5f) + .9f * lp; }
                clip.SetData(d, 0); hum.clip = clip;
            }
            if (hum == null) return;
            hum.volume = Mathf.MoveTowards(hum.volume, want ? .05f : 0f, Time.unscaledDeltaTime * .05f);
            if (hum.volume > 0 && !hum.isPlaying) hum.Play(); else if (hum.volume <= 0 && hum.isPlaying) hum.Stop();
        }
    }
}
