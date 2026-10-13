using UnityEngine;
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

    // In-game UI in the holographic style of the main menu: HP / wave / score bar, the virtual move
    // stick, DASH / weapon / pause buttons, and the pause and game-over panels.
    // Left half of the screen = move stick; aiming and firing are automatic, so the right thumb
    // is free for the DASH and weapon buttons.
    public class Hud : MonoBehaviour
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
        Image hpFill, dashCool, reloadDim, reloadSweep;
        Text dashNum; bool dashWasReady = true;
        RawImage hurt;
        Text hpText, waveText, scoreText, weaponText, banner, statsText;
        Image hpBack; RectTransform hpRoot, wavePill;
        HoloButton dashBtn, weaponBtn, pauseBtn, skillBtn, flareBtn; Text flareLbl; Image skillCool; Text skillNum, skillLbl;
        public static bool SkillOn;   // a sector run: the hero's skill button shows
        bool weaponIconHeavy;
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

            // HP bar + wave/score (top center)
            var top = Holo.Panel(safe, "Top");
            top.anchorMin = top.anchorMax = top.pivot = new Vector2(0.5f, 1);
            top.anchoredPosition = new Vector2(0, -18); top.sizeDelta = new Vector2(880, 90);
            var hpl = Holo.Label(Holo.Rect(top, "HP", new Vector2(0, 0.5f), new Vector2(32, 0), new Vector2(60, 60)), "HP", 32, Holo.Acid, TextAnchor.MiddleLeft);
            hpl.fontStyle = FontStyle.Bold;
            var bar = Holo.Rect(top, "Bar", new Vector2(0, 0.5f), new Vector2(96, 0), new Vector2(300, 24));
            Holo.Img(bar, Holo.WhiteSprite, new Color(0, 0.02f, 0.05f, 0.75f));
            hpFill = Holo.Img(Holo.Fill(bar, "Fill", 3), Holo.WhiteSprite, Holo.Cyan);
            hpFill.type = Image.Type.Filled; hpFill.fillMethod = Image.FillMethod.Horizontal;
            for (int i = 1; i < 10; i++) Holo.Img(Holo.Rect(bar, "Tick", new Vector2(i / 10f, 0.5f), Vector2.zero, new Vector2(3, 24)), Holo.WhiteSprite, new Color(0, 0.02f, 0.05f, 0.85f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            hpText = Holo.Label(Holo.Rect(top, "HpText", new Vector2(0, 0.5f), new Vector2(410, 0), new Vector2(90, 60)), "100", 34, Holo.Ink, TextAnchor.MiddleLeft);
            waveText = Holo.Label(Holo.Rect(top, "Wave", new Vector2(0, 0.5f), new Vector2(510, 0), new Vector2(170, 60)), "WAVE 1", 34, Holo.Acid, TextAnchor.MiddleLeft);
            waveText.fontStyle = FontStyle.Bold;
            scoreText = Holo.Label(Holo.Rect(top, "Score", new Vector2(1, 0.5f), new Vector2(-34, 0), new Vector2(200, 60)), "0", 36, Holo.Ink, TextAnchor.MiddleRight);
            scoreText.fontStyle = FontStyle.Bold;
            // minimal HUD: the old top panel (HP, wave, score) is gone; HP sits bottom-left, the wave / room label in a
            // small pill at the top (outside sector runs, whose tracker lives in RunUi), FPS only via Settings
            top.gameObject.SetActive(false);
            var hpBlock = Holo.Plain(safe, "Hp", 0.78f);
            hpBlock.anchorMin = hpBlock.anchorMax = hpBlock.pivot = new Vector2(0, 0); hpBlock.anchoredPosition = new Vector2(26, 22); hpBlock.sizeDelta = new Vector2(400, 62);
            var hb = Holo.Rect(hpBlock, "Bar", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(368, 34));
            hpBack = Holo.Img(hb, Holo.WhiteSprite, new Color(0, 0.02f, 0.05f, 0.85f));
            hpFill = Holo.Img(Holo.Fill(hb, "Fill", 3), Holo.WhiteSprite, Holo.Cyan);
            hpFill.type = Image.Type.Filled; hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpText = Holo.Label(Holo.Fill(hb, "Num"), "100", 28, Holo.Ink); hpText.fontStyle = FontStyle.Bold;
            hpText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0.03f, 0.06f, 0.9f);
            hpRoot = hpBlock;
            var wp = Holo.Plain(safe, "WavePill", 0.7f); wp.anchorMin = wp.anchorMax = wp.pivot = new Vector2(0.5f, 1); wp.anchoredPosition = new Vector2(0, -16); wp.sizeDelta = new Vector2(300, 56);
            waveText = Holo.Label(Holo.Fill(wp, "T"), "WAVE 1", 30, Holo.Acid); waveText.fontStyle = FontStyle.Bold; wavePill = wp;
            statsText = Holo.Label(Holo.Rect(safe, "Stats", new Vector2(0, 1), new Vector2(26, -22), new Vector2(560, 40)), "", 21, Holo.Ink, TextAnchor.UpperLeft);
            var ssh = statsText.gameObject.AddComponent<Outline>(); ssh.effectColor = new Color(0, 0.03f, 0.06f, 0.7f); ssh.effectDistance = new Vector2(1.5f, -1.5f);

            testTag = Holo.Label(Holo.Rect(safe, "TestTag", new Vector2(1, 1), new Vector2(-200, -40), new Vector2(260, 40)), "TEST MODE", 28, new Color(1f, 0.72f, 0.15f), TextAnchor.MiddleRight);   // a test run (no progress saved)
            testTag.fontStyle = FontStyle.Bold; testTag.raycastTarget = false; testTag.rectTransform.pivot = new Vector2(1, 1); testTag.gameObject.SetActive(false);
            pauseBtn = Holo.Button(safe, "Pause", new Vector2(Holo.MinTouch, Holo.MinTouch), null, "pause");
            Place(pauseBtn, new Vector2(1, 1), new Vector2(-24, -18), new Vector2(1, 1));
            pauseBtn.onClick = () => game.TogglePause();

            // buttons (bottom right, above the aim stick zone); they read touches directly for zero latency
            weaponBtn = Corner("Weapon", "rifle", 164, new Vector2(-150, 330), out weaponText);
            // reload: the button darkens and a cyan wedge sweeps round it clockwise from 12 o'clock
            reloadDim = Holo.Img(Holo.Fill(weaponBtn.body, "ReloadDim", 6), Holo.FillSprite, new Color(0, 0.02f, 0.05f, 0.62f));
            reloadSweep = Holo.Img(Holo.Fill(weaponBtn.body, "ReloadSweep", 6), Holo.FillSprite, Holo.A(Holo.Cyan, 0.62f));
            reloadSweep.type = Image.Type.Filled; reloadSweep.fillMethod = Image.FillMethod.Radial360;
            reloadSweep.fillOrigin = (int)Image.Origin360.Top; reloadSweep.fillClockwise = true;
            reloadDim.raycastTarget = reloadSweep.raycastTarget = false;
            weaponText.transform.SetAsLastSibling();
            dashBtn = Corner("Dash", "bolt", 150, new Vector2(-340, 196), out var dashLbl);
            dashLbl.text = "DASH";
            dashCool = Holo.Img(Holo.Fill(dashBtn.body, "Cooldown", 6), Holo.FillSprite, new Color(0, 0.02f, 0.05f, 0.7f));
            dashCool.type = Image.Type.Filled; dashCool.fillMethod = Image.FillMethod.Vertical; dashCool.fillOrigin = (int)Image.OriginVertical.Top;
            skillBtn = Corner("Skill", "bolt", 132, new Vector2(-520, 120), out skillLbl);
            flareBtn = Corner("Flare", "bolt", 112, new Vector2(-520, 300), out flareLbl); flareBtn.gameObject.SetActive(false);
            skillCool = Holo.Img(Holo.Fill(skillBtn.body, "Cooldown", 6), Holo.FillSprite, new Color(0, 0.02f, 0.05f, 0.7f));
            skillCool.type = Image.Type.Filled; skillCool.fillMethod = Image.FillMethod.Vertical; skillCool.fillOrigin = (int)Image.OriginVertical.Top; skillCool.raycastTarget = false;
            skillNum = Holo.Label(Holo.Rect(skillBtn.body, "Seconds", new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(120, 70)), "", 54, Holo.Ink); skillNum.fontStyle = FontStyle.Bold; skillNum.raycastTarget = false;
            // seconds left, big in the middle of the button while it recharges
            dashNum = Holo.Label(Holo.Rect(dashBtn.body, "Seconds", new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(140, 80)), "", 64, Holo.Ink);
            dashNum.fontStyle = FontStyle.Bold; Holo.GlowText(dashNum, Holo.Cyan, 0.5f); dashNum.raycastTarget = false;

            left.baseImg = StickImg("MoveStick", 260, ringSprite, Holo.A(Holo.Cyan, 0.55f));
            left.knob = StickImg("MoveKnob", 110, circle, Holo.A(Holo.Acid, 0.7f));
            right.baseImg = StickImg("AimStick", 260, ringSprite, Holo.A(Holo.Cyan, 0.55f));
            right.knob = StickImg("AimKnob", 110, circle, Holo.A(Holo.Cyan, 0.75f));
            ShowStick(left, false); ShowStick(right, false);

            var br = Holo.Rect(safe, "Banner");
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.72f); br.pivot = new Vector2(0.5f, 0.5f); br.sizeDelta = new Vector2(1400, 150);
            banner = Holo.Label(br, "", 100, Holo.Ink);
            banner.fontStyle = FontStyle.Bold;
            Holo.GlowText(banner, Holo.Cyan, 0.8f);
            var hr = Holo.Rect(safe, "Hint"); hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 1f); hr.pivot = new Vector2(0.5f, 1f); hr.anchoredPosition = new Vector2(0, -120); hr.sizeDelta = new Vector2(1300, 60);
            hint = Holo.Label(hr, "", 38, Holo.Acid); hint.fontStyle = FontStyle.Bold; hint.raycastTarget = false; hint.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0.03f, 0.06f, 0.9f);   // onboarding / floor-script hints (top band, never the thumbs)

            BuildPause();
            BuildGameOver();
            SetMode(Mode.Hidden);
        }

        static void Place(HoloButton b, Vector2 anchor, Vector2 pos, Vector2 pivot)
        {
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos;
        }

        HoloButton Corner(string name, string icon, float size, Vector2 pos, out Text label)
        {
            var b = Holo.Button(safe, name, new Vector2(size, size), null, icon);
            Place(b, new Vector2(1, 0), pos, new Vector2(0.5f, 0.5f));
            b.manual = true; b.fill.raycastTarget = false;
            b.icon.rectTransform.anchoredPosition = new Vector2(0, size * 0.1f);
            label = Holo.Label(Holo.Rect(b.body, "Label", new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(size - 20, 36)), "", 28, Holo.Ink);
            label.rectTransform.pivot = new Vector2(0.5f, 0);
            label.fontStyle = FontStyle.Bold;
            return b;
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

        void BuildPause()
        {
            pauseRt = Dim(canvas.transform, "Pause", new Color(0, 0.01f, 0.03f, 0.86f), out pauseCg, out pauseScan);
            var ps = Holo.SafeRoot(pauseRt);
            pausePanel = Holo.Panel(ps, "Panel");
            pausePanel.anchorMin = pausePanel.anchorMax = pausePanel.pivot = new Vector2(0.5f, 0.5f);
            pausePanel.sizeDelta = new Vector2(860, 880);
            Holo.Label(Holo.Rect(pausePanel, "Caption", new Vector2(0, 1), new Vector2(46, -30), new Vector2(500, 34)), "TACTICAL HOLD", 26, Holo.Acid, TextAnchor.UpperLeft);
            var title = Holo.Label(Holo.Rect(pausePanel, "Title", new Vector2(0, 1), new Vector2(42, -58), new Vector2(520, 96)), "PAUSED", 86, Holo.Ink, TextAnchor.UpperLeft);
            title.fontStyle = FontStyle.Bold; Holo.GlowText(title, Holo.Cyan, 0.45f);
            pauseInfo = Holo.Label(Holo.Rect(pausePanel, "Info", new Vector2(0, 1), new Vector2(46, -160), new Vector2(760, 34)), "", 26, Holo.Muted, TextAnchor.UpperLeft);

            soundBtn = Holo.Button(pausePanel, "Sound", new Vector2(Holo.MinTouch, Holo.MinTouch), null, "sound");
            Place(soundBtn, new Vector2(1, 1), new Vector2(-170, -20), new Vector2(1, 1));
            soundBtn.onClick = () => { Prefs.Sound = !Prefs.Sound; Sfx.Toggle(); };
            hapticBtn = Holo.Button(pausePanel, "Haptics", new Vector2(Holo.MinTouch, Holo.MinTouch), null, "haptic");
            Place(hapticBtn, new Vector2(1, 1), new Vector2(-22, -20), new Vector2(1, 1));
            hapticBtn.onClick = () => { Prefs.HapticsOn = !Prefs.HapticsOn; Haptics.Medium(); };

            Stack(pausePanel, "Resume", "RESUME", "play", new Vector2(770, 166), 214, true, 66).onClick = () => game.TogglePause();
            Stack(pausePanel, "Restart", "RESTART RUN", "retry", new Vector2(770, Holo.MinTouch), 404, false, 48).onClick = () => game.Restart();
            Stack(pausePanel, "Menu", "MAIN MENU", "home", new Vector2(770, Holo.MinTouch), 556, false, 48).onClick = () => game.ToMenu();
            camBtn = Stack(pausePanel, "Camera", "CAMERA: FAR", "eye", new Vector2(770, Holo.MinTouch), 708, false, 48);
            camBtn.onClick = () => { game.ToggleCamera(); Sfx.Toggle(); };
            // FLOOR test toggle (Escape Test only): shares the camera row, each half width
            floorBtn = Holo.Button(pausePanel, "Floor", new Vector2(377, Holo.MinTouch), "FLOOR: NEW", "quality", 36);
            Place(floorBtn, new Vector2(0.5f, 1), new Vector2(197, -708), new Vector2(0.5f, 1));
            floorBtn.onClick = () => { game.ToggleFloor(); Sfx.Toggle(); };
        }

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
            var sector = Holo.Button(overPanel, "Sector", new Vector2(240, 150), "SECTOR", "deploy", 26);
            Place(sector, new Vector2(1, 0), new Vector2(-280, 54), new Vector2(1, 0));
            sector.onClick = () => game.ToSector();
        }

        bool Hit(RectTransform rt, Vector2 screen) => rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
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
                        if (Hit((RectTransform)dashBtn.transform, t.position)) { f.dash = true; dashBtn.Flash(false); break; }
                        if (SkillOn && Hit((RectTransform)skillBtn.transform, t.position)) { f.skill = true; skillBtn.Flash(false); break; }
                        if (flareBtn.gameObject.activeSelf && Hit((RectTransform)flareBtn.transform, t.position)) { f.flare = true; flareBtn.Flash(false); break; }
                        if (Hit((RectTransform)weaponBtn.transform, t.position))
                        {   // the Heavy can't swap: the same button is his manual RELOAD
                            if (Prefs.Vanguard && !Meta.WeaponOwned("THERMAL") && !Meta.WeaponOwned("RAIL")) { if (!weaponBtn.dimmed) { f.reload = true; weaponBtn.Flash(); } }   // the Heavy with only the minigun: RELOAD
                            else { f.nextWeapon = true; weaponBtn.Flash(); }
                            break;
                        }
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
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) { f.dash = true; if (!overlayOpen) dashBtn.Flash(false); }
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
        public void ShowBanner(string text) { banner.text = text; bannerT = 1.6f; }
        Text hint, testTag; float hintT;
        public bool HintBusy => hintT > 0.2f;
        public void ShowHint(string text) { if (hint == null) return; hint.text = text; hintT = 2.4f; }

        public void SetMode(Mode m)
        {
            mode = m;
            canvas.enabled = m != Mode.Hidden;
            pauseRt.gameObject.SetActive(m == Mode.Paused);
            overRt.gameObject.SetActive(m == Mode.Dead);
            if (m == Mode.Paused) pauseK = 0;
            if (m == Mode.Dead) { overK = 0; overDelay = 1.1f; overCg.alpha = 0; }
            pauseBtn.gameObject.SetActive(m == Mode.Playing);
            dashBtn.gameObject.SetActive(m == Mode.Playing || m == Mode.Paused);
            skillBtn.gameObject.SetActive(SkillOn && (m == Mode.Playing || m == Mode.Paused));
            weaponBtn.gameObject.SetActive(m == Mode.Playing || m == Mode.Paused);
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
            hpFill.color = pl.hurtT > 0 ? Color.Lerp(Holo.Cyan, Holo.Red, Mathf.Clamp01(pl.hurtT * 5)) // a red flash on each hit
                         : pct < 0.3f ? Color.Lerp(Holo.Red, Color.white, 0.25f * (0.5f + 0.5f * Mathf.Sin(t * 10))) : Holo.Cyan;
            hpRoot.localScale = Vector3.one * (1 + 0.06f * Mathf.Clamp01(pl.hurtT * 4));
            wavePill.gameObject.SetActive(!g.run && mode == Mode.Playing);
            hpText.text = Mathf.CeilToInt(pl.hp).ToString();
            waveText.text = g.WaveLabel;
            scoreText.text = g.score.ToString("N0");
            hurt.color = Holo.A(Holo.Red, mode == Mode.Playing && pct < 0.3f ? 0.35f + 0.2f * Mathf.Sin(t * 6) : Mathf.Clamp01(pl.hurtT * 2.4f));
            var w = cfg.weapons[pl.weapon];
            bool reloading = pl.ammo != null && pl.Reloading;
            bool heavy = Prefs.Vanguard;
            weaponText.text = reloading ? "RELOADING" : heavy ? "RELOAD" : w.name;
            weaponText.fontSize = reloading ? 22 : heavy ? 26 : w.name.Length > 5 ? 24 : 28;
            weaponText.color = reloading ? Color.Lerp(Holo.Ink, Holo.Acid, 0.5f + 0.5f * Mathf.Sin(t * 12)) : Holo.Ink;
            reloadDim.enabled = reloadSweep.enabled = reloading;
            if (reloading) reloadSweep.fillAmount = 1 - pl.reloadLeft[pl.weapon] / pl.reloadDur[pl.weapon];
            weaponBtn.tint = reloading ? Holo.Cyan : Color.Lerp(w.color, Holo.Cyan, 0.35f);
            if (weaponBtn.icon != null && heavy != weaponIconHeavy) { weaponBtn.icon.sprite = Holo.Icon(heavy ? "retry" : "rifle"); weaponIconHeavy = heavy; }
            // the Heavy's RELOAD is disabled with a full magazine or while a reload runs
            weaponBtn.dimmed = heavy && pl.ammo != null && (reloading || pl.ammo[pl.weapon] >= w.magSize);
            if (skillBtn.gameObject.activeSelf != (SkillOn && mode == Mode.Playing)) skillBtn.gameObject.SetActive(SkillOn && mode == Mode.Playing);
            bool dark = CellLight.I != null && CellLight.I.Dark && mode == Mode.Playing;
            if (flareBtn.gameObject.activeSelf != dark) flareBtn.gameObject.SetActive(dark);
            if (dark) { flareLbl.text = $"FLARE {CellLight.Flares}"; flareBtn.dimmed = CellLight.Flares <= 0; }
            if (SkillOn)
            {
                bool sr = Abilities.Cd <= 0; skillLbl.text = Abilities.Name;
                skillCool.fillAmount = sr ? 0 : Mathf.Clamp01(Abilities.Cd / Abilities.Cooldown);
                skillNum.text = sr ? "" : Mathf.CeilToInt(Abilities.Cd).ToString();
                skillBtn.tint = sr ? Holo.Acid : Holo.Cyan;
            }
            bool ready = pl.dashCd <= 0;
            dashCool.fillAmount = ready ? 0 : Mathf.Clamp01(pl.dashCd / cfg.player.dashCooldown);
            dashBtn.tint = ready ? Holo.Acid : Holo.Cyan;
            dashBtn.glowBoost = ready ? 0.12f + 0.12f * Mathf.Sin(t * 5) : 0;
            dashNum.text = ready ? "" : Mathf.CeilToInt(pl.dashCd).ToString();
            if (dashBtn.icon != null) dashBtn.icon.color = Holo.A(Holo.Ink, ready ? 1 : 0.25f);
            if (ready && !dashWasReady && mode == Mode.Playing) { dashBtn.Flash(false); Sfx.Click(); Haptics.Light(); } // it is back
            dashWasReady = ready;
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
                pauseInfo.text = $"WAVE {g.wave}  //  {g.kills} KILLS  //  SCORE {g.score:N0}";
                soundBtn.dimmed = !Prefs.Sound; soundBtn.selected = Prefs.Sound;
                hapticBtn.dimmed = !Prefs.HapticsOn; hapticBtn.selected = Prefs.HapticsOn;
                if (camBtn.label != null) camBtn.label.text = Prefs.CamCloseOn ? "CAMERA: CLOSE" : "CAMERA: FAR";
                bool esc = MazeMap.Escape;
                floorBtn.gameObject.SetActive(esc);
                var crt = (RectTransform)camBtn.transform;
                crt.sizeDelta = new Vector2(esc ? 377 : 770, crt.sizeDelta.y); crt.anchoredPosition = new Vector2(esc ? -197 : 0, -708);
                if (camBtn.label != null) camBtn.label.fontSize = esc ? 36 : 48;
                if (floorBtn.label != null) floorBtn.label.text = Game.FloorNew ? "FLOOR: NEW" : "FLOOR: OLD";
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
