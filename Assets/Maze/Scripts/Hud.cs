using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Maze
{
    // What the player asked for this frame, from touch or keyboard/mouse.
    public struct InputFrame
    {
        public Vector2 move;     // screen-space stick (x right, y up), length 0..1
        public Vector2 aim;      // screen-space aim direction (touch), or zero
        public bool hasMouseAim; public Vector2 mouseScreen;
        public bool fire, dash, skill, flare, nextWeapon, prevWeapon, reload, tap, pause, restart, demo;
        public int selectWeapon; // 0 = none, 1..3
        public float zoom;       // +1 out, -1 in
    }

    // In-map status and icon controls, plus the existing pause and game-over panels.
    // The left half retains the floating move stick; action buttons read touches directly.
    public partial class Hud : MonoBehaviour
    {
        class Stick
        {
            public int finger = -1;
            public Vector2 origin, pos;
            public RectTransform baseImg, knob;
            public Vector2 Value(float radius)
            {
                if (finger < 0) return Vector2.zero;
                var d = (pos - origin) / radius;
                return d.sqrMagnitude > 1 ? d.normalized : d;
            }
        }

        public enum Mode { Hidden, Playing, Paused, Dead }

        Game game;
        Canvas canvas;
        RectTransform hudRoot, safe;
        Sprite circle, ringSprite;
        Image hpFill;
        bool dashWasReady = true;
        RawImage hurt;
        TMP_Text hpText, waveText, banner, statsText;
        Image hpBack; RectTransform hpRoot, wavePill;
        HoloButton dashBtn, pauseBtn, skillBtn, flareBtn;
        public static bool SkillOn;   // a sector run: the hero's skill button shows
        readonly Stick left = new(), right = new();
        float bannerT, stickRadiusPx, t;
        float fpsAvg = 16f;
        Mode mode = Mode.Hidden;

        // overlays
        RectTransform pauseRt, overRt, pausePanel, overPanel;
        CanvasGroup pauseCg, overCg;
        RawImage pauseScan, overScan;
        Text pauseInfo, overTitle, newBest, overCaption;
        readonly Text[] tileNames = new Text[4];
        bool won;
        readonly Text[] tiles = new Text[4];
        HoloButton soundBtn, hapticBtn, camBtn, floorBtn;
        float pauseK, overK, overDelay;

        public bool OverlayOpen => mode != Mode.Playing;

        public void Init(Game g)
        {
            game = g;
            Input.simulateMouseWithTouches = false;
            Input.multiTouchEnabled = true;
            circle = ProcTex.Circle(128);
            ringSprite = ProcTex.Circle(128, 7);

            canvas = Holo.MakeCanvas(transform, "HUD", 0);
            hudRoot = Holo.Fill(canvas.transform, "Hud");
            hurt = Holo.Raw(Holo.Fill(hudRoot, "Hurt"), Holo.VignetteTex, Holo.A(Holo.Red, 0));
            safe = Holo.SafeRoot(hudRoot);

            BuildRoundedHud();

            BuildPause();
            BuildGameOver();
            SetMode(Mode.Hidden);
        }

        static void Place(HoloButton b, Vector2 anchor, Vector2 pos, Vector2 pivot)
        {
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos;
        }

        RectTransform StickImg(string name, float size, Sprite s, Color c)
        {
            var rt = Holo.Rect(hudRoot, name, Vector2.zero, Vector2.zero, new Vector2(size, size));
            rt.pivot = new Vector2(0.5f, 0.5f);
            Holo.Img(rt, s, c);
            return rt;
        }
        void ShowStick(Stick s, bool on) { s.baseImg.gameObject.SetActive(on); s.knob.gameObject.SetActive(on); }

        // ---- overlays
        static RectTransform Dim(Transform parent, string name, Color c, out CanvasGroup cg, out RawImage scan)
        {
            var rt = Holo.Fill(parent, name);
            Holo.Img(rt, Holo.WhiteSprite, c, false, true);
            scan = Holo.Raw(Holo.Fill(rt, "Scan"), Holo.ScanTex, new Color(0, 0, 0, 0.18f));
            cg = rt.gameObject.AddComponent<CanvasGroup>();
            return rt;
        }

        static HoloButton Stack(RectTransform panel, string name, string label, string icon, Vector2 size, float y, bool hero, int font)
        {
            var b = Holo.Button(panel, name, size, label, icon, font, hero);
            Place(b, new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(0.5f, 1));
            return b;
        }

        void BuildPause() => BuildBluePause();

        void BuildGameOver()
        {
            overRt = Dim(canvas.transform, "GameOver", new Color(0.12f, 0f, 0.03f, 0.84f), out overCg, out overScan);
            var os = Holo.SafeRoot(overRt);
            overPanel = Holo.Panel(os, "Panel", Holo.Red, 0.9f);
            overPanel.anchorMin = overPanel.anchorMax = overPanel.pivot = new Vector2(0.5f, 0.5f);
            overPanel.sizeDelta = new Vector2(1030, 660);
            overCaption = Holo.Label(Holo.Rect(overPanel, "Caption", new Vector2(0, 1), new Vector2(48, -30), new Vector2(900, 34)), "SIGNAL LOST  //  OPERATIVE DOWN", 26, Holo.Red, TextAnchor.UpperLeft);
            overTitle = Holo.Label(Holo.Rect(overPanel, "Title", new Vector2(0, 1), new Vector2(42, -54), new Vector2(940, 130)), "SWARMED", 120, Holo.Ink, TextAnchor.UpperLeft);
            overTitle.fontStyle = FontStyle.Bold; Holo.GlowText(overTitle, Holo.Red, 0.9f);
            newBest = Holo.Label(Holo.Rect(overPanel, "NewBest", new Vector2(1, 1), new Vector2(-48, -96), new Vector2(320, 50)), "NEW BEST", 36, Holo.Acid, TextAnchor.MiddleRight);
            newBest.fontStyle = FontStyle.Bold; Holo.GlowText(newBest, Holo.Acid, 0.4f);
            string[] names = { "WAVE", "KILLS", "SCORE", "BEST" };
            for (int i = 0; i < 4; i++)
            {
                var tile = Holo.Panel(overPanel, "Tile" + i, Holo.Cyan, 0.7f);
                tile.anchorMin = tile.anchorMax = tile.pivot = new Vector2(0, 1);
                tile.anchoredPosition = new Vector2(48 + i * 240, -206); tile.sizeDelta = new Vector2(214, 150);
                tiles[i] = Holo.Label(Holo.Rect(tile, "Value", new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(200, 70)), "0", 58, Holo.Ink);
                tiles[i].fontStyle = FontStyle.Bold;
                tileNames[i] = Holo.Label(Holo.Rect(tile, "Name", new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(200, 30)), names[i], 24, Holo.Muted);
            }
            var retry = Holo.Button(overPanel, "Retry", new Vector2(440, 170), "RETRY", "retry", 64, true);
            Place(retry, new Vector2(0, 0), new Vector2(48, 44), new Vector2(0, 0));
            retry.onClick = () => game.Restart();
            var menu = Holo.Button(overPanel, "Menu", new Vector2(220, 150), "MENU", "home", 30);
            Place(menu, new Vector2(1, 0), new Vector2(-48, 54), new Vector2(1, 0));
            menu.onClick = () => game.ToMenu();

        }

        bool Hit(RectTransform rt, Vector2 screen) => rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera);
        Vector2 ToCanvas(Vector2 screen) => screen / canvas.scaleFactor;

        // ---- input
        public InputFrame ReadInput(bool overlayOpen)
        {
            var f = new InputFrame();
            stickRadiusPx = 110f * canvas.scaleFactor;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        if (overlayOpen) break; // menus and panels take taps through their own buttons
                        if (TryHudAction(t.position, ref f)) break;
                        if (t.position.x >= Screen.width * 0.5f) break; // right half: buttons only
                        var s = left;
                        if (s.finger < 0) { s.finger = t.fingerId; s.origin = s.pos = t.position; ShowStick(s, true); }
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (left.finger == t.fingerId) left.pos = t.position;
                        if (right.finger == t.fingerId) right.pos = t.position;
                        break;
                    default:
                        if (left.finger == t.fingerId) { left.finger = -1; ShowStick(left, false); }
                        if (right.finger == t.fingerId) { right.finger = -1; ShowStick(right, false); }
                        break;
                }
            }
            if (Input.touchCount == 0 || overlayOpen)
            {
                if (left.finger >= 0) { left.finger = -1; ShowStick(left, false); }
                if (right.finger >= 0) { right.finger = -1; ShowStick(right, false); }
            }
            f.move = left.Value(stickRadiusPx);
            var a = right.Value(stickRadiusPx);
            if (a.magnitude > 0.25f) { f.aim = a.normalized; f.fire = true; }
            foreach (var s in new[] { left, right })
                if (s.finger >= 0) { s.baseImg.anchoredPosition = ToCanvas(s.origin); s.knob.anchoredPosition = ToCanvas(s.origin + s.Value(stickRadiusPx) * stickRadiusPx); }

            // keyboard + mouse (editor / desktop / tablets with keyboards)
            float kx = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            float ky = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            if (kx != 0 || ky != 0) f.move = new Vector2(kx, ky).normalized;
            if (Input.touchCount == 0 && Input.mousePresent && !overlayOpen)
            {
                f.hasMouseAim = true; f.mouseScreen = Input.mousePosition;
                if (Input.GetMouseButton(0)) f.fire = true;
            }
            if (Input.GetKey(KeyCode.Space)) f.fire = true;
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) { f.dash = true; if (!overlayOpen) FlashRounded(dashBtn); }
            if (Input.GetKeyDown(KeyCode.E)) f.nextWeapon = true;
            if (Input.GetKeyDown(KeyCode.R)) f.reload = true;
            if (Input.GetKeyDown(KeyCode.F)) f.skill = true;
            if (Input.GetKeyDown(KeyCode.G)) f.flare = true;
            if (Input.GetKeyDown(KeyCode.Q)) f.prevWeapon = true;
            if (Input.GetKeyDown(KeyCode.Alpha1)) f.selectWeapon = 1;
            if (Input.GetKeyDown(KeyCode.Alpha2)) f.selectWeapon = 2;
            if (Input.GetKeyDown(KeyCode.Alpha3)) f.selectWeapon = 3;
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape)) f.pause = true;
            if (Input.GetKeyDown(KeyCode.R)) f.restart = true;
            if (Input.GetKeyDown(KeyCode.G)) f.demo = true;
            if (Input.GetKeyDown(KeyCode.Return)) f.tap = true;
            f.zoom = -Input.mouseScrollDelta.y;
            return f;
        }

        // ---- display
        public void ShowBanner(string text)
        {
            // Ability feedback lives on its icon; keep the central lane clear of ability names.
            if (text == "FLASH FREEZE" || text == "BARRAGE" || text == "DECOY") { if (skillBtn != null) FlashRounded(skillBtn); return; }
            banner.text = text; bannerT = 1.6f;
        }
        TMP_Text hint, testTag; float hintT;
        public bool HintBusy => hintT > 0.2f;
        public void ShowHint(string text) { if (hint == null) return; hint.text = text; hintT = 2.4f; }

        public void SetMode(Mode m)
        {
            canvas.sortingOrder=m==Mode.Paused?6:0;
            mode = m;
            canvas.enabled = m != Mode.Hidden;
            pauseRt.gameObject.SetActive(m == Mode.Paused);
            overRt.gameObject.SetActive(m == Mode.Dead);
            if (m == Mode.Paused) { pauseK = 0; ResetBluePauseConfirmation(); }
            if (m == Mode.Dead) { overK = 0; overDelay = 1.1f; overCg.alpha = 0; }
            pauseBtn.gameObject.SetActive(m == Mode.Playing);
            dashBtn.gameObject.SetActive(m == Mode.Playing);
            skillBtn.gameObject.SetActive(SkillOn && (m == Mode.Playing || m == Mode.Paused));
            if (m != Mode.Playing) { ShowStick(left, false); ShowStick(right, false); left.finger = right.finger = -1; }
            if (m == Mode.Hidden) { bannerT = 0; hintT = 0; }
        }

        public void ShowGameOver(int wave, int kills, int score, int best, bool isNewBest, string firstName = "WAVE")
        {
            SetMode(Mode.Dead);
            OverLook(false);
            overTitle.text = "SWARMED"; overCaption.text = "SIGNAL LOST  //  OPERATIVE DOWN";
            tileNames[0].text = firstName; tileNames[3].text = "BEST";
            tiles[0].text = wave.ToString();
            tiles[1].text = kills.ToString("N0");
            tiles[2].text = score.ToString("N0");
            tiles[3].text = best.ToString("N0");
            newBest.gameObject.SetActive(isNewBest);
        }

        // escape maps: the floor escaped (same panel, green, with the run time)
        public void ShowEscaped(float time, int kills, int score, float bestTime, bool isNewBest)
        {
            SetMode(Mode.Dead);
            OverLook(true);
            overTitle.text = "FLOOR ESCAPED"; overCaption.text = "EXTRACTION COMPLETE  //  OPERATIVE SAFE";
            tileNames[0].text = "TIME"; tileNames[3].text = "BEST TIME";
            tiles[0].text = Clock(time); tiles[1].text = kills.ToString("N0"); tiles[2].text = score.ToString("N0"); tiles[3].text = Clock(bestTime);
            newBest.gameObject.SetActive(isNewBest);
        }
        public static string Clock(float t) => $"{Mathf.FloorToInt(t / 60)}:{Mathf.FloorToInt(t % 60):00}";

        void OverLook(bool win)
        {
            won = win;
            var dim = overRt.GetComponent<Image>(); if (dim != null) dim.color = win ? new Color(0f, 0.08f, 0.06f, 0.84f) : new Color(0.12f, 0f, 0.03f, 0.84f);
            overCaption.color = win ? Holo.Acid : Holo.Red;
            foreach (var sh in overTitle.GetComponents<Shadow>()) sh.effectColor = Holo.A(win ? Holo.Acid : Holo.Red, sh.effectColor.a);
            var tint = win ? Holo.Acid : Holo.Red;
            foreach (var (n, a) in new[] { ("Edge", 0.8f), ("Glow", 0.22f), ("Brackets", 0.9f) })
            { var im = overPanel.Find(n)?.GetComponent<Image>(); if (im != null) im.color = Holo.A(tint, a); }
            overTitle.fontSize = win ? 96 : 120; // "FLOOR ESCAPED" leaves room for NEW BEST
        }

        public void Refresh(float dt, Game g)
        {
            t += dt;
            var pl = g.player; var cfg = g.cfg;
            float pct = pl.hp / Mathf.Max(1, pl.maxHp);
            hpFill.fillAmount = Mathf.Lerp(hpFill.fillAmount, pct, 1 - Mathf.Exp(-dt * 12));
            hpFill.color = Color.Lerp(Color.white, new Color(1,.65f,.65f), Mathf.Clamp01(pl.hurtT * 4));
            hpText.color = pct < .3f ? Color.Lerp(new Color(1,.7f,.65f),Color.white,.5f+.5f*Mathf.Sin(t*6)) : RoundedHudStyle.Ink;
            hpRoot.localScale = Vector3.one; // fixed containment while taking damage
            wavePill.gameObject.SetActive(!g.run && mode == Mode.Playing);
            hpText.text = $"{Mathf.CeilToInt(pl.hp):N0} / {Mathf.CeilToInt(pl.maxHp):N0}";
            waveText.text = g.WaveLabel;
            hurt.color = Holo.A(Holo.Red, mode == Mode.Playing && pct < 0.3f ? 0.35f + 0.2f * Mathf.Sin(t * 6) : Mathf.Clamp01(pl.hurtT * 2.4f));
            RefreshRoundedActions();
            bannerT -= dt;
            if (testTag != null && testTag.gameObject.activeSelf != Game.TestRun) testTag.gameObject.SetActive(Game.TestRun);
            if (hint != null) { hintT -= dt; var hc = hint.color; hc.a = Mathf.Clamp01(hintT * 2.5f); hint.color = hc; }
            var bc = banner.color; bc.a = Mathf.Clamp01(bannerT * 2); banner.color = bc;
            banner.rectTransform.localScale = Vector3.one * (1 + Mathf.Max(0, bannerT - 1.3f) * 0.6f);
            fpsAvg = Mathf.Lerp(fpsAvg, Time.unscaledDeltaTime * 1000f, 0.05f);
            statsText.gameObject.SetActive(Prefs.ShowFps);
            if (Prefs.ShowFps) statsText.text = $"{Mathf.RoundToInt(1000f / fpsAvg)} FPS";

            if (mode == Mode.Paused)
            {
                pauseK = Mathf.MoveTowards(pauseK, 1, dt * 5);
                float e = 1 - Mathf.Pow(1 - pauseK, 3);
                pauseCg.alpha = e;
                pausePanel.localScale = Vector3.one * (0.94f + 0.06f * e);
                pauseScan.uvRect = new Rect(0, t * 0.5f, 1, ((RectTransform)canvas.transform).rect.height / 4f);
                RefreshBluePause();
            }
            if (mode == Mode.Dead)
            {
                overDelay -= dt;
                if (overDelay <= 0)
                {
                    overK = Mathf.MoveTowards(overK, 1, dt * 3.5f);
                    float e = 1 - Mathf.Pow(1 - overK, 3);
                    overCg.alpha = e;
                    overCg.blocksRaycasts = overK > 0.5f;
                    overPanel.localScale = Vector3.one * (0.9f + 0.1f * e);
                }
                else overCg.blocksRaycasts = false;
                overScan.uvRect = new Rect(0, t * 0.5f, 1, ((RectTransform)canvas.transform).rect.height / 4f);
                bool glitch = !won && Random.value < 0.06f;
                overTitle.rectTransform.anchoredPosition = new Vector2(42 + (glitch ? Random.Range(-10f, 10f) : 0), -54);
                overTitle.color = Holo.A(Holo.Ink, glitch ? 0.6f : 1);
                newBest.color = Holo.A(Holo.Acid, 0.6f + 0.4f * Mathf.Sin(t * 6));
            }
        }
    }
}
