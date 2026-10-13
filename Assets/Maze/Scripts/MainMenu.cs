using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Maze
{
    // The "Orbital drop" start screen: the dropship bridge over an infested planet (a painted plate
    // with live parallax, pulsing hive glow, dust motes and scanlines), holographic panel buttons,
    // the LOADOUT / SQUAD / RESEARCH / settings sheets, and the DEPLOY drop-hatch transition.
    public class MainMenu : MonoBehaviour
    {
        Game game;
        Canvas canvas;
        RectTransform canvasRt, shakeRoot, bgRt, dustRoot, safe, sheetRoot, logoRt, column, transitionRt;
        RawImage scan;
        CanvasGroup logoCg, dimCg;
        Image logoGlow, acidGlow, purpleGlow, dim;
        readonly List<(Image img, float phase, float speed)> hives = new();
        class Mote { public RectTransform rt; public Image img; public Vector2 p, v; public float depth, tw, a; }
        readonly List<Mote> motes = new();
        readonly List<(HoloButton b, CanvasGroup cg, float delay)> mainButtons = new();
        HoloButton deployBtn, loadoutBtn, squadBtn, researchBtn, gearBtn, opsBtn;
        Text telemetry;
        Vector2 tilt, accBase, drift;
        float t, introT, shake, teleT;
        const float BgAspect = 2340f / 1080f;
        static readonly Color Metal = new Color(0.045f, 0.07f, 0.095f, 1);

        class Sheet { public RectTransform rt; public CanvasGroup cg; public float k, width; public bool open; public Action onOpen, onClose; }
        readonly Dictionary<string, Sheet> sheets = new();
        Sheet current;

        // drop transition
        RectTransform doorTop, doorBot;
        RawImage[] hazards;
        Image flash;
        CanvasGroup dropTextCg;
        float dropT = -1; bool impact, landed;

        public bool Busy => dropT >= 0;
        public bool SheetOpen => current != null;
        public void SetCanvasVisible(bool on) { if (canvas != null) canvas.enabled = on; } // map select covers the menu
        public bool Visible => canvas != null && canvas.enabled;

        public void Init(Game g)
        {
            game = g;
            canvas = Holo.MakeCanvas(transform, "MenuCanvas", 10);
            canvasRt = (RectTransform)canvas.transform;
            shakeRoot = Holo.Fill(canvas.transform, "Root");
            BuildBackground();
            safe = Holo.SafeRoot(shakeRoot);
            BuildMain();

            var dimRt = Holo.Fill(shakeRoot, "Dim");
            dim = Holo.Img(dimRt, Holo.WhiteSprite, new Color(0, 0.01f, 0.03f, 0.8f), false, true);
            dimRt.gameObject.AddComponent<Tap>().onTap = CloseSheet;
            dimCg = dimRt.gameObject.AddComponent<CanvasGroup>();
            dimRt.gameObject.SetActive(false);
            sheetRoot = Holo.SafeRoot(shakeRoot);
            toast = Holo.Label(Holo.Rect(shakeRoot, "Toast", new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(1400, 60)), "", 34, Holo.Acid); toast.fontStyle = FontStyle.Bold;
            toast.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0.03f, 0.06f, 0.9f); toast.raycastTarget = false;
            BuildLoadout();
            BuildSquad();
            BuildGear();
            BuildResearch();
            BuildOps();
            BuildTalents();
            BuildSettings();
            BuildTransition();
            Show();
            var ms = new GameObject("MapSelect"); ms.transform.SetParent(transform, false);
            ms.AddComponent<MapSelect>().Init(g, this);
        }

        // ------------------------------------------------------------ background
        static RectTransform At(RectTransform parent, string name, float u, float v, Vector2 size)
        {
            var rt = Holo.Rect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(u, v); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }
        static RectTransform Box(RectTransform parent, string name, float u0, float v0, float u1, float v1)
        {
            var rt = Holo.Rect(parent, name);
            rt.anchorMin = new Vector2(u0, v0); rt.anchorMax = new Vector2(u1, v1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        void BuildBackground()
        {
            var tex = Prefs.HeroBackdrop();
            bgRt = Holo.Rect(shakeRoot, "Backdrop", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2340, 1080));
            bgRaw = Holo.Raw(bgRt, tex, tex != null ? Color.white : new Color(0.03f, 0.05f, 0.1f));
            // the infestation breathes: big soft glows over the planet plus pulsing hive spots
            purpleGlow = Holo.Img(Box(bgRt, "HivePurple", 0.50f, 0.04f, 0.98f, 0.96f), Holo.SoftSprite, Holo.A(Holo.Purple, 0.1f));
            acidGlow = Holo.Img(Box(bgRt, "HiveAcid", 0.48f, 0.16f, 0.86f, 0.86f), Holo.SoftSprite, Holo.A(Holo.Acid, 0.12f));
            var spots = new[] { (1255f, 335f), (1232f, 445f), (1432f, 410f), (1663f, 505f), (1300f, 560f), (1092f, 232f), (1550f, 300f), (1420f, 600f), (1180f, 380f) };
            int i = 0;
            foreach (var (x, y) in spots)
            {
                var img = Holo.Img(At(bgRt, "Hive" + i, x / 1846f, 1 - y / 852f, Vector2.one * UnityEngine.Random.Range(46f, 84f)), Holo.SoftSprite, Holo.A(Holo.Acid, 0));
                hives.Add((img, UnityEngine.Random.value * 6.28f, UnityEngine.Random.Range(0.8f, 1.9f)));
                i++;
            }
            // readability shade behind the panels, vignette, dust, scanlines and a rolling interference band
            Holo.Raw(Box(shakeRoot, "LeftShade", 0, 0, 0.62f, 1), Holo.LeftShadeTex, new Color(1, 1, 1, 0.72f));
            Holo.Raw(Holo.Fill(shakeRoot, "Vignette"), Holo.VignetteTex, Color.black);
            dustRoot = Holo.Fill(shakeRoot, "Dust");
            for (int m = 0; m < 42; m++)
            {
                float depth = UnityEngine.Random.Range(0.3f, 1.3f);
                var rt = Holo.Rect(dustRoot, "Mote", Vector2.zero, Vector2.zero, Vector2.one * Mathf.Lerp(3, 11, depth / 1.3f));
                rt.pivot = new Vector2(0.5f, 0.5f);
                var mo = new Mote
                {
                    rt = rt, depth = depth,
                    img = Holo.Img(rt, Holo.SoftSprite, Holo.A(m % 3 == 0 ? Holo.Acid : Holo.Ink, 0)),
                    p = new Vector2(UnityEngine.Random.Range(0f, 2400f), UnityEngine.Random.Range(0f, 1080f)),
                    v = new Vector2(UnityEngine.Random.Range(4f, 16f), UnityEngine.Random.Range(2f, 9f)) * depth,
                    tw = UnityEngine.Random.Range(0.6f, 2.2f), a = UnityEngine.Random.Range(0.25f, 0.7f),
                };
                motes.Add(mo);
            }
            scan = Holo.Raw(Holo.Fill(shakeRoot, "Scanlines"), Holo.ScanTex, new Color(0, 0, 0, 0.17f));
        }

        // ------------------------------------------------------------ main panel
        void BuildMain()
        {
            logoRt = Holo.Rect(safe, "Logo", new Vector2(0, 1), new Vector2(6, -2), new Vector2(690, 230));
            logoGlow = Holo.Img(At(logoRt, "Glow", 0.5f, 0.5f, new Vector2(980, 420)), Holo.SoftSprite, Holo.A(Holo.Cyan, 0));
            var logoTex = Resources.Load<Texture2D>("UI/menu_logo");
            if (logoTex != null) Holo.Raw(Holo.Fill(logoRt, "Img"), logoTex, Color.white);
            else { var l = Holo.Label(Holo.Fill(logoRt, "Text"), "MAZE SWARM", 110, Holo.Ink); l.fontStyle = FontStyle.Bold; Holo.GlowText(l, Holo.Cyan, 0.8f); }
            logoCg = logoRt.gameObject.AddComponent<CanvasGroup>();
            logoCg.blocksRaycasts = false;

            column = Holo.Rect(safe, "Buttons", new Vector2(0, 1), new Vector2(44, -240), new Vector2(660, 760));
            deployBtn = AddMain("Deploy", "DEPLOY", "deploy", 650, 176, 0, 78, true, () => { if (!Busy) MapSelect.I.Show(); }, 0.55f); // sector select -> briefing -> drop
            deployBtn.SetSub("SECTOR 7  //  DROP WINDOW OPEN", Holo.Acid, 26);
            loadoutBtn = AddMain("Loadout", "ARMORY", "rifle", 580, Holo.MinTouch, 194, 50, false, () => OpenSheet("loadout"), 0.67f);
            squadBtn = AddMain("Squad", "SQUAD", "squad", 580, Holo.MinTouch, 342, 50, false, () => OpenSheet("squad"), 0.77f);
            researchBtn = AddMain("Research", "RESEARCH", "flask", 580, Holo.MinTouch, 490, 50, false, () => OpenSheet("research"), 0.87f);
            researchBtn.SetSub("CLASSIFIED", Holo.Muted, 24);
            opsBtn = AddMain("Ops", "OPS", "quality", 580, 108, 638, 44, false, () => OpenSheet("ops"), 0.95f);   // Daily Ops, the Hot Zone, achievements
            var lockIc = Holo.Img(Holo.Rect(researchBtn.body, "Lock", new Vector2(1, 0.5f), new Vector2(-34, 0), new Vector2(46, 46)), Holo.Icon("lock"), Holo.A(Holo.Muted, 0.9f));
            lockIc.rectTransform.pivot = new Vector2(1, 0.5f);

            gearBtn = Holo.Button(safe, "Settings", new Vector2(Holo.MinTouch, Holo.MinTouch), null, "gear");
            var grt = (RectTransform)gearBtn.transform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(1, 1); grt.anchoredPosition = new Vector2(-26, -22);
            gearBtn.spin = 14;
            gearBtn.onClick = () => OpenSheet("settings");
            var gcg = gearBtn.gameObject.AddComponent<CanvasGroup>();
            mainButtons.Add((gearBtn, gcg, 1.0f));

            // the model test arena (built-in game models on the phone)
            var sbBtn = Holo.Button(safe, "Sandbox", new Vector2(290, 96), "SANDBOX", "flask", 30);
            var sbrt = (RectTransform)sbBtn.transform;
            sbrt.anchorMin = sbrt.anchorMax = sbrt.pivot = new Vector2(1, 0); sbrt.anchoredPosition = new Vector2(-30, 26);
            sbBtn.onClick = () => { Sfx.Open(); UnityEngine.SceneManagement.SceneManager.LoadScene("Sandbox"); };
            sbBtn.gameObject.SetActive(Application.CanStreamedLevelBeLoaded("Sandbox"));
            mainButtons.Add((sbBtn, sbBtn.gameObject.AddComponent<CanvasGroup>(), 1.05f));

            telemetry = Holo.Label(Holo.Rect(safe, "Telemetry", new Vector2(1, 1), new Vector2(-196, -30), new Vector2(560, 130)), "", 25, Holo.Ink, TextAnchor.UpperRight);
            var tsh = telemetry.gameObject.AddComponent<Shadow>(); tsh.effectColor = new Color(0, 0, 0, 0.85f); tsh.effectDistance = new Vector2(2, -2);
            var ver = Holo.Label(Holo.Rect(safe, "Version", new Vector2(0, 0), new Vector2(740, 18), new Vector2(760, 40)), $"ORBITAL DROP  //  BUILD {Application.version}", 22, Holo.A(Holo.Muted, 0.75f), TextAnchor.LowerLeft);
            ver.raycastTarget = false;
        }

        HoloButton AddMain(string name, string label, string icon, float w, float h, float y, int font, bool hero, Action a, float delay)
        {
            var b = Holo.Button(column, name, new Vector2(w, h), label, icon, font, hero);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(0, -y);
            b.onClick = a;
            mainButtons.Add((b, b.gameObject.AddComponent<CanvasGroup>(), delay));
            return b;
        }

        void RefreshLabels()
        {
            var ws = game.cfg.weapons;
            loadoutBtn.SetSub((Prefs.Vanguard ? "MINIGUN" : ws[Prefs.WeaponIndex(ws)].name) + $" LV {Progress.Level(Progress.HeroWeapon)}  //  POWER {Progress.Power():N0}", Holo.Acid, 24);
            squadBtn.SetSub("OPERATIVE: " + Prefs.HeroName, Holo.Acid, 24);
            var bd = Prefs.HeroBackdrop(); if (bgRaw != null && bd != null && bgRaw.texture != bd) bgRaw.texture = bd;
            MapSelect.I?.RefreshHero();
            bool firstDrop = Progress.D.runs == 0;   // onboarding: only DEPLOY is bright until the first drop
            loadoutBtn.dimmed = squadBtn.dimmed = researchBtn.dimmed = firstDrop; if (opsBtn != null) opsBtn.dimmed = firstDrop;
            var od = Ops.TakeJustDone(); if (od.Count > 0) ShowToast(od[^1]);   // a mission finished by an upgrade or merge
            if (opsBtn != null) opsBtn.SetSub(!Ops.DailyOpen ? "RANK 2" : Ops.ChestReady ? "CHEST READY" : $"DAILY {Ops.Today().FindAll(m => m.done).Count}/3" + (Ops.HotOpen && Ops.HotZone != null ? "  //  HOT ZONE" : ""), Ops.ChestReady ? new Color(1f, 0.6f, 0.25f) : Holo.Acid, 24);
        }

        // ------------------------------------------------------------ sheets
        Sheet MakeSheet(string key, string caption, string title, float width, out RectTransform content)
        {
            var rt = Holo.Panel(sheetRoot, "Sheet_" + key);
            rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(width, -48); rt.anchoredPosition = new Vector2(-26, 0);
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            Holo.Label(Holo.Rect(rt, "Caption", new Vector2(0, 1), new Vector2(46, -30), new Vector2(width - 260, 34)), caption, 26, Holo.Acid, TextAnchor.UpperLeft);
            var tl = Holo.Label(Holo.Rect(rt, "Title", new Vector2(0, 1), new Vector2(44, -60), new Vector2(width - 260, 84)), title, 70, Holo.Ink, TextAnchor.UpperLeft);
            tl.fontStyle = FontStyle.Bold; Holo.GlowText(tl, Holo.Cyan, 0.4f);
            var div = Holo.Rect(rt, "Divider");
            div.anchorMin = new Vector2(0, 1); div.anchorMax = new Vector2(1, 1); div.pivot = new Vector2(0.5f, 1);
            div.offsetMin = new Vector2(44, -158); div.offsetMax = new Vector2(-44, -156);
            Holo.Img(div, Holo.WhiteSprite, Holo.A(Holo.Cyan, 0.35f));
            var close = Holo.Button(rt, "Close", new Vector2(Holo.MinTouch, Holo.MinTouch), null, "close");
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1, 1); crt.anchoredPosition = new Vector2(-20, -18);
            close.onClick = CloseSheet;
            content = Holo.Stretch(Holo.Rect(rt, "Content"), 44, 34, 44, 184);
            var s = new Sheet { rt = rt, cg = cg, width = width };
            sheets[key] = s;
            rt.gameObject.SetActive(false);
            return s;
        }

        public void OpenSheet(string key)
        {
            if (Busy || !sheets.TryGetValue(key, out var s)) return;
            if (Progress.D.runs == 0 && key != "settings") { Sfx.Deny(); ShowToast("UNLOCKS AFTER YOUR FIRST DROP"); return; }
            if (current != null && current != s) { current.open = false; current.k = 0; current.rt.gameObject.SetActive(false); current.onClose?.Invoke(); }
            current = s; s.open = true;
            s.rt.gameObject.SetActive(true);
            dim.gameObject.SetActive(true);
            Sfx.Open();
            s.onOpen?.Invoke();
        }

        public void CloseSheet()
        {
            if (current == null || !current.open) return;
            current.open = false;
            Sfx.Close();
            current.onClose?.Invoke();
        }

        void TickSheets(float dt)
        {
            float dimK = 0;
            foreach (var s in sheets.Values)
            {
                if (!s.rt.gameObject.activeSelf) continue;
                s.k = Mathf.MoveTowards(s.k, s.open ? 1 : 0, dt * (s.open ? 3.6f : 5f));
                float e = s.open ? 1 - Mathf.Pow(1 - s.k, 3) : s.k * s.k;
                s.rt.anchoredPosition = new Vector2(-26 + (1 - e) * (s.width + 260), 0);
                s.cg.alpha = Mathf.Clamp01(s.k * 1.6f);
                s.cg.interactable = s.cg.blocksRaycasts = s.open;
                dimK = Mathf.Max(dimK, e);
                if (!s.open && s.k <= 0) { s.rt.gameObject.SetActive(false); if (current == s) current = null; }
            }
            dimCg.alpha = dimK;
            if (dimK <= 0 && current == null) dim.gameObject.SetActive(false);
        }

        // ---- LOADOUT
        class Bar { public Image[] seg; public Text value; public float shown, target; }
        readonly List<(HoloButton b, Image check, int index)> weaponCards = new();
        HoloButton minigunCard, needlerCard; Text loadoutHint; RawImage bgRaw;
        Text armoryWallet, armoryLevel; HoloButton upgradeBtn; int armorySel;
        // the Armory tutorial after the first clear: rifle LEVEL UP, then EQUIP the gloves (0 -> 1 -> 2)
        static int Tut { get => PlayerPrefs.GetInt("ms.tut.armory", 0); set { PlayerPrefs.SetInt("ms.tut.armory", value); PlayerPrefs.Save(); } }
        static bool TutReady => Progress.D.runs >= 1 && Progress.D.runs <= 4 && Progress.D.cleared.Count > 0;
        int ArmoryWeapon => Prefs.Vanguard ? 3 : Prefs.Scout ? 8 : armorySel;
        static string Wallet() => $"<color=#f5c400>{Progress.D.credits:N0}</color> CR   <color=#7fd0ff>{Progress.D.parts}</color> PARTS   <color=#c0c8d0>{Progress.D.alloy}</color> ALLOY   <color=#a0ff6a>POWER {Progress.Power():N0}</color>";
        // the wallet on the Armory / Gear sheets: painted currency icons + amounts, then Combat Power
        readonly List<Text[]> wallets = new();
        void MakeWallet(RectTransform sheet)
        {
            var w = new Text[4]; string[] art = { "cur_credits", "cur_parts", "cur_alloy", "ui_power" }; Color[] col = { new Color(0.96f, 0.77f, 0f), new Color(0.5f, 0.82f, 1f), new Color(0.75f, 0.78f, 0.82f), Holo.Acid };
            float x = -180;
            for (int i = 3; i >= 0; i--)
            {   // right to left
                float tw = i == 3 ? 150 : 110;
                var tx = Holo.Label(Holo.Rect(sheet, "W" + i, new Vector2(1, 1), new Vector2(x, -104), new Vector2(tw, 44)), "", 30, col[i], TextAnchor.MiddleLeft); tx.rectTransform.pivot = new Vector2(1, 0.5f); tx.fontStyle = FontStyle.Bold;
                var ic = Holo.Img(Holo.Rect(sheet, "Wi" + i, new Vector2(1, 1), new Vector2(x - tw - 4, -104), new Vector2(46, 46)), Holo.WhiteSprite, Color.white); ic.rectTransform.pivot = new Vector2(1, 0.5f);
                Holo.SetIcon(ic, art[i], "bolt", col[i]);
                w[i] = tx; x -= tw + 56;
            }
            wallets.Add(w);
        }
        void RefreshWallets()
        {
            foreach (var w in wallets) { w[0].text = Progress.D.credits.ToString("N0"); w[1].text = Progress.D.parts.ToString(); w[2].text = Progress.D.alloy.ToString(); w[3].text = Progress.Power().ToString("N0"); }
        }
        void RefreshArmory()
        {
            int w = ArmoryWeapon, lv = Progress.Level(w);
            RefreshWallets();
            if (armoryLevel != null)
            {
                string perk = lv < 10 ? "Lv 10: PIERCE +1" : lv < 20 ? "Lv 20: CRIT +10%" : "ALL PERKS";
                int gain = 0; if (lv < Meta.WeaponCap(w)) { int cp0 = Meta.CP(); Progress.D.weaponLv[w]++; gain = Meta.CP() - cp0; Progress.D.weaponLv[w]--; }
                armoryLevel.text = $"<color=#{ColorUtility.ToHtmlStringRGB(Meta.MkColor[Meta.WeaponTier(w)])}>{Meta.Mk[Meta.WeaponTier(w)]}</color>  {Progress.WeaponModels[w]}  LV {lv}/{Meta.WeaponCap(w)}  <color=#a0ff6a>+{gain} CP</color>  <size=22><color=#8a96a8>PERKS {Meta.Perks(w)}</color></size>";
            }
            if (upgradeBtn != null)
            {
                bool atCap = lv >= Meta.WeaponCap(w); int nt = Meta.WeaponTier(w) + 1;
                bool can = atCap ? nt < Meta.Mk.Length && Meta.TierGate(w) == null && Progress.D.credits >= Meta.MkCredits[nt] && Progress.D.parts >= Meta.MkParts[nt] : Progress.CanUpgrade(w);
                upgradeBtn.dimmed = !can; if (upgradeBtn.label != null) upgradeBtn.label.text = atCap ? "TIER UP" : "UPGRADE";
                upgradeBtn.SetSub(atCap ? (nt >= Meta.Mk.Length ? "MAX" : Meta.TierGate(w) ?? $"{Meta.Mk[nt]}  {Meta.MkCredits[nt]:N0} CR + {Meta.MkParts[nt]} P") : $"{Meta.LevelCredits(w):N0} CR + {Meta.LevelParts(w)} PARTS", can ? Holo.Acid : Holo.Muted, 22);
            }
        }
        // the Heavy carries the minigun only: hide the picker (stats are the rifle's for now, a placeholder)
        void HeavyLoadout()
        {
            bool heavy = Prefs.Vanguard, scout = Prefs.Scout;
            foreach (var (b, _, _) in weaponCards) b.gameObject.SetActive(!heavy && !scout);
            if (minigunCard != null) minigunCard.gameObject.SetActive(heavy);
            if (needlerCard != null) needlerCard.gameObject.SetActive(scout);
            if (arPivot != null) arPivot.gameObject.SetActive(!heavy || mgPivot == null);
            if (mgPivot != null) { mgPivot.gameObject.SetActive(heavy); }
            gunPivot = heavy && mgPivot != null ? mgPivot : arPivot;
            if (loadoutHint != null) loadoutHint.text = heavy ? "Iron Vanguard's minigun. Tap RELOAD during a run to reload early." : "Your starting weapon. Tap the weapon button during a run to swap.";
        }
        readonly Bar[] bars = new Bar[4];
        Text previewName, dpsText;
        RectTransform previewScan;
        Transform previewRoot, gunPivot;
        Camera previewCam;
        RenderTexture previewRt;

        void BuildLoadout()
        {
            var s = MakeSheet("loadout", "WEAPONS  //  LEVEL UP WITH CREDITS + PARTS", "ARMORY", 1250, out var c);
            armoryWallet = Holo.Label(Holo.Rect(s.rt, "Wallet", new Vector2(1, 1), new Vector2(-180, -64), new Vector2(760, 44)), "", 28, Holo.Ink, TextAnchor.MiddleRight);
            armoryWallet.rectTransform.pivot = new Vector2(1, 0.5f); armoryWallet.supportRichText = true; armoryWallet.gameObject.SetActive(false);
            MakeWallet(s.rt);
            s.onOpen = () => { if (Tut == 0 && TutReady && !Prefs.Vanguard) { ShowToast("TAP UPGRADE: LEVEL UP YOUR RIFLE"); if (upgradeBtn != null) upgradeBtn.selected = true; } HeavyLoadout(); SelectWeapon(Prefs.Vanguard ? 0 : Prefs.Scout ? Mathf.Max(0, System.Array.FindIndex(game.cfg.weapons, w => w.name == "NEEDLER")) : Prefs.WeaponIndex(game.cfg.weapons), false); if (Prefs.Vanguard) previewName.text = "MINIGUN"; if (previewRoot != null) previewRoot.gameObject.SetActive(true); if (previewCam != null) previewCam.enabled = true; };
            s.onClose = () => { if (previewRoot != null) previewRoot.gameObject.SetActive(false); if (previewCam != null) previewCam.enabled = false; };
            var ws = game.cfg.weapons;
            for (int i = 0; i < ws.Length; i++)
            {
                int idx = i; if (ws[i].name == "THERMAL" || ws[i].name == "RAIL" || ws[i].name == "NEEDLER") continue;   // the Heavy's (swapped in a run)
                var b = Holo.Button(c, "W_" + ws[i].name, new Vector2(470, 140), ws[i].name, "rifle", 48);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(0, -i * 158);
                b.SetSub(Tagline(ws[i].name), Holo.Muted, 24);
                if (ws[i].kind != null) { var wn = ws[i].name; b.locked = !Meta.WeaponOwned(wn); if (b.locked) b.SetSub($"BLUEPRINT {Meta.Fragments(wn)}/10  //  {Meta.BlueprintZones(wn)}", Holo.Muted, 22); }
                var chk = Holo.Img(Holo.Rect(b.body, "Check", new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(46, 46)), Holo.Icon("check"), Holo.Acid);
                chk.rectTransform.pivot = new Vector2(1, 0.5f);
                b.onClick = () => SelectWeapon(idx, true);
                weaponCards.Add((b, chk, i));
            }
            loadoutHint = Holo.Label(Holo.Rect(c, "Hint", new Vector2(0, 1), new Vector2(4, -486), new Vector2(460, 80)),
                "Your starting weapon. Tap the weapon button during a run to swap.", 24, Holo.Muted, TextAnchor.UpperLeft);
            loadoutHint.gameObject.SetActive(ws.Length <= 3);   // the blueprint weapons take its place
            // Iron Vanguard: his one weapon, no picker
            minigunCard = Holo.Button(c, "W_MINIGUN", new Vector2(470, 140), "MINIGUN", "rifle", 48);
            var mrt = (RectTransform)minigunCard.transform;
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0, 1); mrt.anchoredPosition = Vector2.zero;
            minigunCard.SetSub("HEAVY  //  IRON VANGUARD ONLY", Holo.Muted, 24); minigunCard.selected = true;
            needlerCard = Holo.Button(c, "W_NEEDLER", new Vector2(470, 140), "TWIN NEEDLERS", "rifle", 40);   // the Scout's (Twin Needlers)
            var nrt = (RectTransform)needlerCard.transform; nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(0, 1); nrt.anchoredPosition = Vector2.zero;
            needlerCard.SetSub("HOMING  //  SCOUT ONLY", Holo.Muted, 24); needlerCard.selected = true; needlerCard.gameObject.SetActive(false);
            var dep = Holo.Button(c, "Deploy", new Vector2(470, 150), "DEPLOY", "deploy", 58, true);
            var drt = (RectTransform)dep.transform;
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0, 0); drt.anchoredPosition = new Vector2(0, 6);
            dep.onClick = Deploy;

            // 3D preview
            var pf = Holo.Rect(c, "Preview", new Vector2(1, 1), Vector2.zero, new Vector2(640, 300));
            Holo.Img(pf, Holo.FillSprite, new Color(0.012f, 0.04f, 0.07f, 1), true);
            previewRt = new RenderTexture(1024, 480, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "GunPreview" };
            Holo.Raw(Holo.Fill(pf, "View", 8), previewRt, Color.white);
            Holo.Img(At(pf, "Pedestal", 0.5f, 0.12f, new Vector2(470, 70)), Holo.SoftSprite, Holo.A(Holo.Cyan, 0.3f));
            var pg = Holo.Raw(Holo.Fill(pf, "Grid", 10), Holo.GridTex, Holo.A(Holo.Cyan, 0.05f));
            pg.gameObject.AddComponent<GridUv>();
            previewScan = Holo.Rect(pf, "Scan");
            previewScan.anchorMin = new Vector2(0, 0.5f); previewScan.anchorMax = new Vector2(1, 0.5f); previewScan.sizeDelta = new Vector2(-24, 2);
            Holo.Img(previewScan, Holo.WhiteSprite, Holo.A(Holo.Cyan, 0.35f));
            Holo.Img(Holo.Fill(pf, "Edge"), Holo.EdgeSprite, Holo.A(Holo.Cyan, 0.6f), true);
            Holo.Img(Holo.Stretch(Holo.Rect(pf, "Brackets"), -10, -10, -10, -10), Holo.BracketSprite, Holo.A(Holo.Acid, 0.8f), true);
            previewName = Holo.Label(Holo.Rect(pf, "Name", new Vector2(0, 1), new Vector2(30, -18), new Vector2(400, 60)), "", 50, Holo.Ink, TextAnchor.UpperLeft);
            previewName.fontStyle = FontStyle.Bold; Holo.GlowText(previewName, Holo.Cyan, 0.4f);
            BuildPreviewRig();

            string[] names = { "RANGE", "DAMAGE", "FIRE RATE", "MOVE SPEED" };
            for (int i = 0; i < 4; i++)
            {
                var row = Holo.Rect(c, "Stat" + i, new Vector2(1, 1), new Vector2(0, -318 - i * 58), new Vector2(640, 52));
                Holo.Label(Holo.Rect(row, "Name", new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(200, 56)), names[i], 28, Holo.Muted, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                var bar = new Bar { seg = new Image[12] };
                for (int k = 0; k < 12; k++)
                    bar.seg[k] = Holo.Img(Holo.Rect(row, "Seg", new Vector2(0, 0.5f), new Vector2(206 + k * 26, 0), new Vector2(21, 24)), Holo.WhiteSprite, Holo.A(Holo.Cyan, 0.12f));
                bar.value = Holo.Label(Holo.Rect(row, "Value", new Vector2(1, 0.5f), Vector2.zero, new Vector2(110, 56)), "", 30, Holo.Ink, TextAnchor.MiddleRight);
                bars[i] = bar;
            }
            dpsText = Holo.Label(Holo.Rect(c, "Dps", new Vector2(1, 1), new Vector2(0, -604), new Vector2(640, 40)), "", 26, Holo.Acid, TextAnchor.MiddleRight);
            // weapon level: +8% damage a level, a perk every 10; UPGRADE costs Credits + Parts; GEAR opens the gear sheet
            dpsText.rectTransform.anchoredPosition = new Vector2(0, -560);
            armoryLevel = Holo.Label(Holo.Rect(c, "Level", new Vector2(1, 1), new Vector2(0, -604), new Vector2(640, 36)), "", 24, Holo.Ink, TextAnchor.MiddleLeft);
            armoryLevel.rectTransform.pivot = new Vector2(1, 1); armoryLevel.fontStyle = FontStyle.Bold; armoryLevel.supportRichText = true;
            upgradeBtn = Holo.Button(c, "Upgrade", new Vector2(390, 110), "UPGRADE", "bolt", 34, true);
            if (upgradeBtn.icon != null) Holo.SetIcon(upgradeBtn.icon, "ui_levelup", "bolt", Holo.Ink);
            var urt = (RectTransform)upgradeBtn.transform; urt.anchorMin = urt.anchorMax = urt.pivot = new Vector2(1, 0); urt.anchoredPosition = new Vector2(-250, 6);
            upgradeBtn.onClick = () =>
            {   // LEVEL UP below the tier cap, TIER UP (Mk) at it
                int w = ArmoryWeapon; bool ok = Progress.Level(w) >= Meta.WeaponCap(w) ? Meta.TierUp(w) : Progress.Upgrade(w);
                if (ok) { Sfx.Toggle(); Haptics.Medium(); RefreshArmory(); RefreshLabels(); if (Tut == 0 && TutReady) { Tut = 1; upgradeBtn.selected = false; ShowToast("NOW EQUIP YOUR RARE GLOVES: OPEN GEAR"); } } else { Sfx.Deny(); Haptics.Warn(); if (Progress.Level(w) >= Meta.WeaponCap(w)) ShowToast(Meta.TierGate(w) ?? $"{Meta.MkCredits[Meta.WeaponTier(w) + 1]:N0} CR + {Meta.MkParts[Meta.WeaponTier(w) + 1]} PARTS"); }
            };
            var gearBtn = Holo.Button(c, "Gear", new Vector2(236, 110), "GEAR", "squad", 34);
            var grt = (RectTransform)gearBtn.transform; grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(1, 0); grt.anchoredPosition = new Vector2(0, 6);
            gearBtn.onClick = () => OpenSheet("gear");
        }

        static string Tagline(string w) => w switch
        {
            "RIFLE" => "LONG REACH  //  STEADY",
            "SHOTGUN" => "CLOSE BLAST  //  KNOCKBACK",
            "SMG" => "BULLET HOSE  //  AGILE",
            "FRAG" => "ARCING BLAST  //  CROWDS",
            "LANCE" => "RAMPING BEAM  //  SLOWS",
            _ => "",
        };

        void BuildPreviewRig()
        {
            previewRoot = new GameObject("GunPreview").transform;
            previewRoot.SetParent(transform, false);
            previewRoot.position = new Vector3(0, -600, 0);
            arPivot = PreviewGun("Models/ar"); mgPivot = PreviewGun("Models/minigun");
            gunPivot = arPivot; if (mgPivot != null) mgPivot.gameObject.SetActive(false);
            var cgo = new GameObject("PreviewCam");
            cgo.transform.SetParent(previewRoot, false);
            cgo.transform.localPosition = new Vector3(0, 0.28f, -1.95f);
            cgo.transform.LookAt(previewRoot.position + Vector3.up * 0.02f);
            previewCam = cgo.AddComponent<Camera>();
            previewCam.fieldOfView = 34; previewCam.nearClipPlane = 0.1f; previewCam.farClipPlane = 8;
            previewCam.clearFlags = CameraClearFlags.SolidColor; previewCam.backgroundColor = new Color(0.012f, 0.04f, 0.07f, 1);
            previewCam.targetTexture = previewRt;
            previewCam.enabled = false;
            Light(new Vector3(1.3f, 0.9f, 1.4f), Holo.Cyan, 5f);
            Light(new Vector3(-1.6f, 1.1f, -1.4f), new Color(1f, 0.93f, 0.85f), 3f);
            Light(new Vector3(0.2f, -1.2f, -0.6f), Holo.Acid, 1.5f);
            previewRoot.gameObject.SetActive(false);
        }

        // a gun model for the Loadout's turntable (the AK, or the Heavy's minigun), fitted to the same size
        Transform arPivot, mgPivot;
        Transform PreviewGun(string path)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return null;
            var gunPivot = new GameObject("Pivot").transform;
            gunPivot.SetParent(previewRoot, false);
            {
                var go = Instantiate(prefab, gunPivot);
                Mats.ConvertGltf(go);
                var rs = go.GetComponentsInChildren<Renderer>();
                foreach (var r in rs) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float s = 1.75f / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    go.transform.localScale *= s;
                    b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    go.transform.position -= b.center - gunPivot.position;
                }
            }
            return gunPivot;
        }

        void Light(Vector3 at, Color c, float intensity)
        {
            var go = new GameObject("PreviewLight");
            go.transform.SetParent(previewRoot, false);
            go.transform.localPosition = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = 6; l.shadows = LightShadows.None;
        }

        void SelectWeapon(int i, bool save)
        {
            var ws = game.cfg.weapons;
            var w = ws[i];
            if (!Meta.WeaponOwned(w.name)) { if (save) { Sfx.Deny(); ShowToast($"{Progress.WeaponModels[Progress.WeaponIndexFor(w.name)]}: BLUEPRINT {Meta.Fragments(w.name)}/10 FROM {Meta.BlueprintZones(w.name)}"); } return; }
            if (save) { Prefs.Weapon = w.name; RefreshLabels(); }
            armorySel = Progress.WeaponIndexFor(w.name); RefreshArmory();
            foreach (var (b, chk, idx) in weaponCards) { b.selected = idx == i; chk.gameObject.SetActive(idx == i); }
            float maxReach = 0, maxDmg = 0, maxRate = 0;
            foreach (var x in ws) { maxReach = Mathf.Max(maxReach, x.reach); maxDmg = Mathf.Max(maxDmg, x.damage * x.pellets); maxRate = Mathf.Max(maxRate, x.rate); }
            float dmg = w.damage * w.pellets, speed = game.cfg.player.speed * w.moveMul;
            float reach = Prefs.Vanguard ? w.reach * Game.MinigunRangeMul : w.reach; // the minigun: half the rifle's reach
            Set(0, reach / maxReach, $"{reach:0.#} m");
            Set(1, Mathf.Sqrt(dmg / maxDmg), $"{dmg:0}");
            Set(2, w.rate / maxRate, $"{w.rate:0.#}/s");
            Set(3, Mathf.InverseLerp(0.6f, 1.15f, w.moveMul), $"{speed:0.0} m/s");
            previewName.text = w.name;
            dpsText.text = $"SUSTAINED DPS  {dmg * w.rate:0}   //   {(w.pellets > 1 ? w.pellets + " PELLETS" : "SINGLE SHOT")}";
            void Set(int k, float v, string txt) { bars[k].target = Mathf.Clamp01(v); bars[k].value.text = txt; if (!save) bars[k].shown = 0; }
        }

        void TickLoadout(float dt)
        {
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                bar.shown = Mathf.MoveTowards(bar.shown, bar.target, dt * 2.2f);
                float lit = bar.shown * bar.seg.Length;
                for (int k = 0; k < bar.seg.Length; k++)
                {
                    float a = Mathf.Clamp01(lit - k);
                    var on = Color.Lerp(Holo.Cyan, Holo.Acid, k / (bar.seg.Length - 1f));
                    bar.seg[k].color = Color.Lerp(Holo.A(Holo.Cyan, 0.12f), on, a);
                }
            }
            if (gunPivot != null) gunPivot.localRotation = Quaternion.Euler(6 * Mathf.Sin(t * 0.7f), t * 42, 0);
            if (previewScan != null) previewScan.anchoredPosition = new Vector2(0, Mathf.Lerp(-130, 130, Mathf.Repeat(t * 0.35f, 1)));
        }

        // ---- GEAR (Armory): 5 slots, the inventory (by slot, best first), the selected piece: EQUIP / LEVEL UP (Alloy) /
        // MERGE (3 of the same slot + rarity into the next rarity)
        RectTransform gearSlots, gearGrid; Text gearWallet, gearSelName, gearSelStat; HoloButton gearEquip, gearLevel, gearMerge, gearLock, gearSalvage, relicBtn, relicLv; int gearSelUid;
        void BuildGear()
        {
            var s = MakeSheet("gear", "ARMORY  //  HELMET  VEST  GLOVES  BOOTS  IMPLANT", "GEAR", 1250, out var c);
            gearWallet = Holo.Label(Holo.Rect(s.rt, "Wallet", new Vector2(1, 1), new Vector2(-180, -64), new Vector2(760, 44)), "", 28, Holo.Ink, TextAnchor.MiddleRight);
            gearWallet.rectTransform.pivot = new Vector2(1, 0.5f); gearWallet.supportRichText = true; gearWallet.gameObject.SetActive(false);
            MakeWallet(s.rt);
            s.onOpen = () => { if (Tut == 1) { int best = 0, br = -1; foreach (var g in Progress.D.gear) if (g.slot == 2 && g.rarity > br) { br = g.rarity; best = g.uid; } if (best != 0) { gearSelUid = best; ShowToast("TAP EQUIP ON THE GLOVES"); } } RefreshGear(); if (Tut == 1 && gearEquip != null) gearEquip.selected = true; };
            gearSlots = Holo.Rect(c, "Slots", new Vector2(0, 1), Vector2.zero, new Vector2(470, 700)); gearSlots.pivot = new Vector2(0, 1);
            gearGrid = Holo.Rect(c, "Grid", new Vector2(1, 1), Vector2.zero, new Vector2(640, 470)); gearGrid.pivot = new Vector2(1, 1);
            var sel = Holo.Rect(c, "Sel", new Vector2(1, 0), Vector2.zero, new Vector2(640, 300)); sel.pivot = new Vector2(1, 0);
            Holo.Img(sel, Holo.FillSprite, Holo.A(Holo.Glass, 1), true);
            Holo.Img(Holo.Fill(sel, "Edge"), Holo.EdgeSprite, Holo.A(Holo.Cyan, 0.5f), true);
            gearSelName = Holo.Label(Holo.Rect(sel, "N", new Vector2(0, 1), new Vector2(24, -18), new Vector2(600, 46)), "SELECT A PIECE", 34, Holo.Ink, TextAnchor.UpperLeft); gearSelName.fontStyle = FontStyle.Bold;
            gearSelStat = Holo.Label(Holo.Rect(sel, "S", new Vector2(0, 1), new Vector2(24, -66), new Vector2(600, 60)), "", 24, Holo.Muted, TextAnchor.UpperLeft);
            HoloButton B(string n, string label, string icon, float x) { var b = Holo.Button(sel, n, new Vector2(118, 96), label, null, 22); var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0); rt.anchoredPosition = new Vector2(x, 16); return b; }
            gearEquip = B("Equip", "EQUIP", "check", 12); gearLevel = B("Level", "LEVEL", "bolt", 136); gearMerge = B("Merge", "MERGE", "quality", 260);
            gearLock = B("Lock", "LOCK", "lock", 384); gearSalvage = B("Salvage", "SCRAP", "close", 508);
            gearLock.onClick = () => { var g = Progress.Item(gearSelUid); if (g != null) { g.locked = !g.locked; Progress.Save(); Sfx.Toggle(); RefreshGear(); } };
            gearSalvage.onClick = () => { var g = Progress.Item(gearSelUid); int a = g != null ? Meta.SalvageItem(g) : 0; if (a > 0) { gearSelUid = 0; Sfx.Close(); ShowToast($"+{a} ALLOY"); RefreshGear(); RefreshLabels(); } else { Sfx.Deny(); ShowToast("EQUIPPED OR LOCKED"); } };
            var ma = Holo.Button(c, "MergeAll", new Vector2(470, 84), "MERGE ALL", "quality", 28);
            var mart = (RectTransform)ma.transform; mart.anchorMin = mart.anchorMax = mart.pivot = new Vector2(0, 1); mart.anchoredPosition = new Vector2(0, -560);
            relicBtn = Holo.Button(c, "Relic", new Vector2(340, 110), "RELIC", null, 22);
            var rrt = (RectTransform)relicBtn.transform; rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0, 1); rrt.anchoredPosition = new Vector2(0, -656);
            relicBtn.onClick = () =>
            {   // cycle the owned relics (and none)
                if (!Meta.RelicSlotOpen) { Sfx.Deny(); ShowToast("RELIC SLOT OPENS AT 60 STARS"); return; }
                int cur = Progress.D.equippedRelic, n = Meta.Relics.Length;
                for (int k = 1; k <= n + 1; k++) { int i = (cur + 1 + k) % (n + 1) - 1; if (i < 0 || Meta.RelicLv(i) > 0) { Progress.D.equippedRelic = i; break; } }
                Progress.Save(); Sfx.Toggle(); RefreshGear(); RefreshLabels();
            };
            relicLv = Holo.Button(c, "RelicLv", new Vector2(120, 110), "LV UP", null, 24);
            var rlt = (RectTransform)relicLv.transform; rlt.anchorMin = rlt.anchorMax = rlt.pivot = new Vector2(0, 1); rlt.anchoredPosition = new Vector2(350, -656);
            relicLv.onClick = () => { if (Meta.EquippedRelic >= 0 && Meta.LevelRelic(Meta.EquippedRelic)) { Sfx.Open(); Haptics.Medium(); RefreshGear(); RefreshLabels(); } else { Sfx.Deny(); ShowToast(Meta.EquippedRelic < 0 ? "NO RELIC EQUIPPED" : "MAX LEVEL OR NOT ENOUGH ALLOY"); } };
            ma.onClick = () => { int n = Meta.MergeAll(); if (n > 0) { Sfx.Open(); Haptics.Heavy(); ShowToast($"{n} MERGED"); RefreshGear(); RefreshLabels(); } else { Sfx.Deny(); ShowToast("NO FULL STACKS (3 / 3 / 4 / 5)"); } };
            gearEquip.onClick = () => { var g = Progress.Item(gearSelUid); if (g != null) { Progress.Equip(g); Sfx.Toggle(); RefreshGear(); RefreshLabels(); if (Tut == 1) { Tut = 2; gearEquip.selected = false; ShowToast($"UPGRADES RAISE YOUR POWER: {Progress.Power():N0}"); } } };
            gearLevel.onClick = () => { var g = Progress.Item(gearSelUid); if (g != null && Progress.LevelGear(g)) { Sfx.Toggle(); Haptics.Light(); RefreshGear(); RefreshLabels(); } else { Sfx.Deny(); Haptics.Warn(); } };
            gearMerge.onClick = () => { var g = Progress.Item(gearSelUid); var n = g != null ? Progress.Merge(g) : null; if (n != null) { gearSelUid = n.uid; Sfx.Open(); Haptics.Heavy(); RefreshGear(); RefreshLabels(); } else { Sfx.Deny(); Haptics.Warn(); } };
        }

        void RefreshGear()
        {
            if (gearSlots == null) return;
            RefreshWallets();
            if (relicBtn != null)
            {   // the 6th slot: the relic
                int ri = Meta.EquippedRelic;
                if (relicBtn.label != null) relicBtn.label.text = !Meta.RelicSlotOpen ? "RELIC  ★ 60" : ri < 0 ? $"RELIC  ({Meta.RelicsOwned} OWNED)" : Meta.Relics[ri];
                relicBtn.SetSub(!Meta.RelicSlotOpen ? "LOCKED: 60 STARS" : ri < 0 ? (Meta.RelicsOwned > 0 ? "TAP TO EQUIP" : "HARD / NIGHTMARE BOSS DROPS") : $"LV {Meta.RelicLv(ri)}  //  +{Meta.RelicLv(ri) * 2}% CP  //  TAP: NEXT", ri < 0 ? Holo.Muted : new Color(1f, 0.48f, 0.85f), 20);
                relicBtn.dimmed = !Meta.RelicSlotOpen;
                relicLv.dimmed = ri < 0 || Meta.RelicCost(ri) == 0 || Progress.D.alloy < Meta.RelicCost(ri);
                relicLv.SetSub(ri < 0 || Meta.RelicCost(ri) == 0 ? "" : $"{Meta.RelicCost(ri)} AL", Holo.Muted, 18);
            }
            for (int i = gearSlots.childCount - 1; i >= 0; i--) Destroy(gearSlots.GetChild(i).gameObject);
            for (int i = gearGrid.childCount - 1; i >= 0; i--) Destroy(gearGrid.GetChild(i).gameObject);
            for (int sl = 0; sl < 5; sl++)
            {
                var g = Progress.Equipped(sl);
                var b = Holo.Button(gearSlots, "Slot" + sl, new Vector2(470, 100), Progress.Slots[sl], "squad", 32);
                if (b.icon != null) { Holo.SetIcon(b.icon, "gear_" + Progress.Slots[sl].ToLower(), "squad", Holo.Ink); if (g == null) b.icon.color = new Color(1, 1, 1, 0.35f); }
                var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(0, -sl * 112);
                b.SetSub(g == null ? "EMPTY" : $"{Progress.Rarities[g.rarity]} LV {g.level}  {Progress.StatText(g)}", g == null ? Holo.Muted : Progress.RarityColors[g.rarity], 22);
                if (g != null) { b.tint = Progress.RarityColors[g.rarity]; int uid = g.uid; b.onClick = () => { gearSelUid = uid; RefreshGear(); }; }
            }
            var list = new List<GearItem>(Progress.D.gear);
            list.Sort((x, y) => x.slot != y.slot ? x.slot.CompareTo(y.slot) : y.rarity != x.rarity ? y.rarity.CompareTo(x.rarity) : y.level.CompareTo(x.level));
            int n = 0;
            foreach (var g in list)
            {
                if (n >= 24) break;
                bool eq = Progress.D.equipped[g.slot] == g.uid;
                var b = Holo.Button(gearGrid, "G" + g.uid, new Vector2(98, 110), null, "squad");
                if (b.icon != null) { Holo.SetIcon(b.icon, "gear_" + Progress.Slots[g.slot].ToLower(), "squad", Holo.Ink); b.icon.rectTransform.sizeDelta = new Vector2(62, 62); }
                var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2((n % 6) * 106, -(n / 6) * 118);
                b.tint = Progress.RarityColors[g.rarity]; b.selected = g.uid == gearSelUid;
                Holo.Label(Holo.Rect(b.body, "Sl", new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(96, 26)), Progress.Slots[g.slot].Substring(0, 4) + (eq ? " ✓" : ""), 18, Progress.RarityColors[g.rarity]).fontStyle = FontStyle.Bold;
                Holo.Label(Holo.Rect(b.body, "Lv", new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(96, 24)), "LV " + g.level, 18, Holo.Ink);
                int uid = g.uid; b.onClick = () => { gearSelUid = uid; RefreshGear(); };
                n++;
            }
            if (list.Count == 0) Holo.Label(Holo.Rect(gearGrid, "Empty", new Vector2(0, 1), new Vector2(0, -10), new Vector2(620, 80)), "No gear yet: clear floors and beat bosses to find some.", 26, Holo.Muted, TextAnchor.UpperLeft);
            var s = Progress.Item(gearSelUid);
            gearEquip.dimmed = gearLevel.dimmed = gearMerge.dimmed = gearLock.dimmed = gearSalvage.dimmed = s == null;
            // active set bonuses, always shown
            var bonus = new List<string>(); if (Meta.EquippedRelic >= 0) bonus.Add($"{Meta.Relics[Meta.EquippedRelic]}: {Meta.RelicDesc(Meta.EquippedRelic)}"); for (int k = 0; k < Meta.Sets.Length; k++) { int pc = Meta.SetPieces(k); if (pc >= 2) bonus.Add($"{Meta.Sets[k]} {pc}/4: {Meta.Set2[k]}" + (pc >= 4 ? $" + {Meta.Set4[k]}" : "")); }
            if (s == null) { gearSelName.text = "SELECT A PIECE"; gearSelStat.text = (bonus.Count > 0 ? string.Join("\n", bonus) : "Merge 3 / 3 / 4 / 5 of the same slot and rarity into the next rarity.\n2 or 4 pieces of one set turn on its bonus."); return; }
            gearSelName.text = $"{Progress.Rarities[s.rarity]} {Progress.Slots[s.slot]}{(s.set >= 0 ? "  //  " + Meta.Sets[s.set] : "")}{(s.locked ? "  [LOCKED]" : "")}"; gearSelName.color = Progress.RarityColors[s.rarity];
            var set = Progress.MergeSet(s); int need = Meta.MergeNeed[s.rarity];
            gearSelStat.text = $"LV {s.level}/{Progress.MaxLevel(s.rarity)}  {Progress.StatText(s)}  {Meta.Affixes(s)}\nLEVEL {Meta.GearCredits(s):N0} CR + {Meta.GearAlloy(s)} ALLOY  //  MERGE {(set != null ? "READY" : $"{CountSame(s)}/{need}")}  //  SALVAGE {Meta.Salvage(s)} ALLOY" + (bonus.Count > 0 ? "\n" + bonus[0] : "");
            if (gearLock.label != null) gearLock.label.text = s.locked ? "UNLOCK" : "LOCK";
            gearEquip.dimmed = Progress.D.equipped[s.slot] == s.uid;
            gearLevel.dimmed = s.level >= Progress.MaxLevel(s.rarity) || Progress.D.alloy < Meta.GearAlloy(s) || Progress.D.credits < Meta.GearCredits(s);
            gearSalvage.dimmed = s.locked || Progress.D.equipped[s.slot] == s.uid;
            gearMerge.dimmed = set == null;
        }
        static int CountSame(GearItem g) { int c = 0; foreach (var o in Progress.D.gear) if (o.slot == g.slot && o.rarity == g.rarity) c++; return c; }

        // ---- SQUAD
        void BuildSquad()
        {
            var sq = MakeSheet("squad", "OPERATIVES  //  DROP TEAM", "SQUAD", 1250, out var c);
            var sqPower = Holo.Label(Holo.Rect(sq.rt, "Power", new Vector2(1, 1), new Vector2(-500, -64), new Vector2(320, 44)), "", 30, Holo.Acid, TextAnchor.MiddleLeft);
            sqPower.rectTransform.pivot = new Vector2(0, 0.5f); sqPower.fontStyle = FontStyle.Bold;
            sq.onOpen = () => sqPower.text = $"COMBAT POWER  {Progress.Power():N0}   COHESION +{Meta.Cohesion(Meta.HeroId) * 100:0}% HP";
            var sqi = Holo.Img(Holo.Rect(sq.rt, "PowerIc", new Vector2(1, 1), new Vector2(-508, -64), new Vector2(44, 44)), Holo.WhiteSprite, Color.white); sqi.rectTransform.pivot = new Vector2(1, 0.5f);
            Holo.SetIcon(sqi, "ui_power", "bolt", Holo.Acid);
            var bgTex = Resources.Load<Texture2D>("UI/menu_bg");
            heroCards.Clear();
            heroCards.Add(("sentinel", Card(c, 0, "ARCTIC SENTINEL", "RIFLEMAN  //  BALANCED", "HP 100  //  DASH 6 S\nAuto-aims anything inside the range circle.", false, Resources.Load<Texture2D>("UI/hero_portrait") ?? Resources.Load<Texture2D>("UI/Portraits/hero") ?? bgTex)));
            // Iron Vanguard (the Heavy): rigged in Blender on the Sentinel's skeleton (Tools/vanguard_rig.py), carries the minigun
            heroCards.Add(("vanguard", Card(c, 1, "IRON VANGUARD", "HEAVY  //  MINIGUN", "Giant in an exo-frame.\nShreds crowds with the minigun.", false, Resources.Load<Texture2D>("UI/vanguard_portrait") ?? Resources.Load<Texture2D>("UI/Portraits/vanguard"))));
            foreach (var (key, card) in heroCards) { var k = key; card.onClick = () => PickHero(k); }
            // compare the Heavy's two rigs (Meshy's own / the Blender fit): a small toggle under his card
            rigBtn = Holo.Button(c, "VanguardRig", new Vector2(354, 96), "RIG: MESHY", "squad", 30);
            var rrt = (RectTransform)rigBtn.transform; rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0, 1); rrt.anchoredPosition = new Vector2(382, -744);
            rigBtn.onClick = () => { Prefs.VanguardMeshy = !Prefs.VanguardMeshy; if (Prefs.Vanguard) game.player.RebuildHero(game.cfg); RigLabel(); Sfx.Toggle(); };
            RigLabel();
            var tb = Holo.Button(c, "Talents", new Vector2(354, 96), "TALENTS", "quality", 30);
            var tbt = (RectTransform)tb.transform; tbt.anchorMin = tbt.anchorMax = tbt.pivot = new Vector2(0, 1); tbt.anchoredPosition = new Vector2(0, -744);
            tb.onClick = () => OpenSheet("talents");
            PickHero(Prefs.Hero, false);
            scoutCard = Card(c, 2, "SCOUT", "TWIN NEEDLERS  //  FAST", "Decoy hologram, double dash.\nSHARDS DROP FROM DROWNED CITY", true, null);
            scoutCard.onClick = () => { if (Meta.HeroStars("scout") > 0) PickHero("scout"); else { Sfx.Deny(); ShowToast($"SCOUT: {Meta.Shards("scout")} / 30 SHARDS"); } };
            heroCards.Add(("scout", scoutCard));
            // hero levels (+2% damage and HP a level; the cap is 10 x stars) and stars (shards from the zone bosses)
            string[] ids = { "sentinel", "vanguard", "scout" };
            for (int i = 0; i < 3; i++)
            {
                string h = ids[i]; int col = i;
                var lv = Holo.Button(c, "HeroLv_" + h, new Vector2(172, 100), "LEVEL", null, 26);
                var lrt = (RectTransform)lv.transform; lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0, 1); lrt.anchoredPosition = new Vector2(col * 382, -628);
                lv.onClick = () => { if (Meta.LevelHero(h)) { Sfx.Toggle(); Haptics.Light(); RefreshHeroes(); RefreshLabels(); } else { Sfx.Deny(); ShowToast(Meta.HeroLevel(h) >= Meta.HeroCap(h) ? "STAR UP TO RAISE THE CAP" : $"{Meta.HeroLevelCost(Meta.HeroLevel(h)):N0} CREDITS"); } };
                var st = Holo.Button(c, "HeroStar_" + h, new Vector2(172, 100), "STAR UP", null, 26);
                var srt = (RectTransform)st.transform; srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0, 1); srt.anchoredPosition = new Vector2(col * 382 + 182, -628);
                st.onClick = () => { if (Meta.StarHero(h)) { Sfx.Toggle(); Haptics.Medium(); RefreshHeroes(); RefreshLabels(); } else { Sfx.Deny(); ShowToast($"{Meta.Shards(h)} / {Meta.StarCost(h)} SHARDS"); } };
                heroBtns.Add((h, lv, st));
            }
            sq.onOpen += RefreshHeroes;
        }
        HoloButton scoutCard; readonly List<(string h, HoloButton lv, HoloButton st)> heroBtns = new();
        void RefreshHeroes()
        {
            foreach (var (h, lv, st) in heroBtns)
            {
                int L = Meta.HeroLevel(h), S = Meta.HeroStars(h);
                lv.SetSub(S == 0 ? "LOCKED" : L >= Meta.HeroCap(h) ? $"LV {L} CAP" : $"LV {L}  {Meta.HeroLevelCost(L):N0} CR", Holo.Acid, 16);
                lv.dimmed = S == 0 || L >= Meta.HeroCap(h) || Progress.D.credits < Meta.HeroLevelCost(L);
                st.SetSub(S >= 5 ? "★★★★★" : $"{new string('★', S)}  {Meta.Shards(h)}/{Meta.StarCost(h)}", Holo.Acid, 16);
                st.dimmed = Meta.StarCost(h) == 0 || Meta.Shards(h) < Meta.StarCost(h);
            }
            if (scoutCard != null)
            {
                bool owned = Meta.HeroStars("scout") > 0; scoutCard.locked = !owned;
                var tag = scoutCard.body.Find("Tag")?.GetComponent<Text>(); if (tag != null) tag.text = owned ? (Prefs.Scout ? "SELECTED" : "TAP TO SELECT") : $"SHARDS {Meta.Shards("scout")} / 30";
            }
        }

        // the remembered Squad pick: highlights its card and swaps the hero model in place
        readonly List<(string key, HoloButton card)> heroCards = new();
        HoloButton rigBtn;
        void RigLabel() { if (rigBtn != null && rigBtn.label != null) rigBtn.label.text = Prefs.VanguardMeshy ? "RIG: MESHY" : "RIG: BLENDER"; }
        public void PickHero(string key, bool save = true)
        {
            if (save && key != Prefs.Hero) { Prefs.Hero = key; game.player.RebuildHero(game.cfg); RefreshLabels(); Sfx.Toggle(); }
            foreach (var (k, card) in heroCards)
            {
                bool on = k == Prefs.Hero; card.selected = on;
                var tag = card.body.Find("Tag")?.GetComponent<Text>();
                if (tag != null) { tag.text = on ? "SELECTED" : "TAP TO SELECT"; tag.color = on ? Holo.Acid : Holo.Muted; }
            }
        }

        HoloButton Card(RectTransform parent, int i, string name, string role, string info, bool locked, Texture2D portrait, string lockTag = "LOCKED")
        {
            var b = Holo.Button(parent, "Card_" + name, new Vector2(354, 610), null, null);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(i * 382, 0);
            b.locked = locked; b.selected = !locked;
            var pr = Holo.Rect(b.body, "Portrait", new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(310, 330));
            if (portrait != null)
            {
                var raw = Holo.Raw(pr, portrait, Color.white);
                if (portrait.name != "menu_bg") raw.gameObject.AddComponent<CoverUv>().aspect = (float)portrait.width / portrait.height; // a rendered portrait
                else raw.uvRect = new Rect(0.697f, 0.108f, 0.241f, 0.575f); // the hero in the backdrop painting
            }
            else
            {
                Holo.Img(pr, Holo.FillSprite, new Color(0.02f, 0.05f, 0.08f, 1), true);
                Holo.Img(Holo.Fill(pr, "Silhouette", 30), Holo.Icon("person"), new Color(0.09f, 0.18f, 0.24f, 1));
                Holo.Img(At(pr, "Lock", 0.5f, 0.45f, new Vector2(96, 96)), Holo.Icon("lock"), Holo.A(Holo.Muted, 0.95f));
            }
            Holo.Img(Holo.Fill(pr, "Frame"), Holo.EdgeSprite, Holo.A(locked ? Holo.Muted : Holo.Cyan, 0.6f), true);
            var nm = Holo.Label(Holo.Rect(b.body, "Name", new Vector2(0.5f, 1), new Vector2(0, -366), new Vector2(330, 60)), name, name.Length > 10 ? 34 : 48, locked ? Holo.Muted : Holo.Ink); // long names stay on one line
            nm.fontStyle = FontStyle.Bold;
            Holo.Label(Holo.Rect(b.body, "Role", new Vector2(0.5f, 1), new Vector2(0, -422), new Vector2(310, 34)), role, 25, locked ? Holo.Muted : Holo.Acid);
            Holo.Label(Holo.Rect(b.body, "Info", new Vector2(0.5f, 1), new Vector2(0, -462), new Vector2(300, 80)), info, 23, Holo.Muted, TextAnchor.UpperCenter);
            var tag = Holo.Label(Holo.Rect(b.body, "Tag", new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(300, 40)), locked ? lockTag : "SELECTED", 28, locked ? Holo.A(Holo.Muted, 0.8f) : Holo.Acid);
            tag.fontStyle = FontStyle.Bold;
            return b;
        }

        // ---- RESEARCH (teaser)
        // RESEARCH (upgrade brief 5): 4 branches x 4 tiers, 4 levels a node; tiers open at rank 3 / 7 / 11 / 15, a node
        // needs the one above at level 2+. Credits only, no randomness. Firepower / Armor show "+N CP".
        readonly List<(HoloButton b, Meta.Node n)> researchNodes = new(); Text researchHead;
        void BuildResearch()
        {
            var s = MakeSheet("research", "BIO-LAB  //  4 BRANCHES  //  CREDITS ONLY", "RESEARCH", 1250, out var c);
            MakeWallet(s.rt);
            researchHead = Holo.Label(Holo.Rect(c, "Head", new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(1150, 40)), "", 26, Holo.Acid); researchHead.supportRichText = true;
            for (int b = 0; b < 4; b++)
            {
                var hl = Holo.Label(Holo.Rect(c, "Branch" + b, new Vector2(0.5f, 1), new Vector2((b - 1.5f) * 288, -52), new Vector2(276, 34)), Meta.Branches[b], 24, Holo.Ink); hl.fontStyle = FontStyle.Bold;
                foreach (var n in Meta.Nodes)
                {
                    if (n.branch != b) continue;
                    var nb = Holo.Button(c, "Node_" + n.id, new Vector2(272, 112), n.name, null, 20);
                    var rt = (RectTransform)nb.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1);
                    rt.anchoredPosition = new Vector2((b - 1.5f) * 288, -96 - n.tier * 124);
                    var node = n;
                    nb.onClick = () => { if (Meta.BuyResearch(node)) { Sfx.Toggle(); Haptics.Light(); RefreshResearch(); RefreshLabels(); } else { Sfx.Deny(); Haptics.Warn(); ShowToast(Meta.ResearchGate(node) ?? $"{Meta.ResearchCost(node):N0} CREDITS"); } };
                    researchNodes.Add((nb, n));
                }
            }
            s.onOpen = RefreshResearch;
        }
        void RefreshResearch()
        {
            researchHead.text = $"COMMANDER RANK {Meta.Rank}  //  XP {Progress.D.cxp:N0} / {Meta.XpToNext(Meta.Rank):N0}  //  <color=#a0ff6a>POWER {Progress.Power():N0}</color>";
            int cp0 = Meta.CP();
            foreach (var (b, n) in researchNodes)
            {
                int lv = Meta.ResearchLv(n.id); var gate = Meta.ResearchGate(n);
                string gainTxt = "";
                if (n.branch <= 1 && lv < 4) { Progress.D.research.Add(n.id + "~"); int L = lv; SetLv(n.id, L + 1); gainTxt = $"+{Meta.CP() - cp0} CP  "; SetLv(n.id, L); Progress.D.research.Remove(n.id + "~"); }
                else if (n.branch >= 2) gainTxt = "NO CP CHANGE  ";
                b.dimmed = gate != null || Progress.D.credits < Meta.ResearchCost(n);
                b.SetSub(lv >= 4 ? "MAX  ●●●●" : gate ?? $"{new string('●', lv)}{new string('○', 4 - lv)}  {gainTxt}{Meta.ResearchCost(n):N0} CR", gate != null ? Holo.Muted : Holo.Acid, 15);
            }
        }
        static void SetLv(string id, int lv)
        {
            var l = Progress.D.research; for (int i = 0; i < l.Count; i++) if (l[i].StartsWith(id + ":")) { l[i] = id + ":" + lv; return; }
            l.Add(id + ":" + lv);
        }

        // ---- TALENTS (the selected hero): a row every 5 levels, two choices, free reset, +1% CP a pick
        Text talHead; readonly List<(int row, int c, HoloButton b)> talBtns = new();
        void BuildTalents()
        {
            var s = MakeSheet("talents", "HERO TALENTS  //  A ROW EVERY 5 LEVELS  //  FREE RESET", "TALENTS", 1250, out var c);
            talHead = Holo.Label(Holo.Rect(c, "Head", new Vector2(0, 1), new Vector2(0, 0), new Vector2(900, 40)), "", 26, Holo.Acid, TextAnchor.UpperLeft); talHead.supportRichText = true;
            var rs = Holo.Button(s.rt, "Reset", new Vector2(220, 96), "RESET", "retry", 28);
            var rst = (RectTransform)rs.transform; rst.anchorMin = rst.anchorMax = rst.pivot = new Vector2(1, 1); rst.anchoredPosition = new Vector2(-170, -40);
            rs.onClick = () => { Meta.ResetTalents(Meta.HeroId); Sfx.Close(); RefreshTalents(); RefreshLabels(); };
            for (int r = 0; r < 10; r++)
            {
                Holo.Label(Holo.Rect(c, "Lv" + r, new Vector2(0, 1), new Vector2(0, -48 - r * 72), new Vector2(110, 66)), $"LV {5 * (r + 1)}", 26, Holo.Muted, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                for (int k = 1; k <= 2; k++)
                {
                    var b = Holo.Button(c, $"T{r}_{k}", new Vector2(520, 66), "", null, 20);
                    var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(120 + (k - 1) * 532, -48 - r * 72);
                    int row = r, ch = k;
                    b.onClick = () => { if (!Meta.RowOpen(Meta.HeroId, row)) { Sfx.Deny(); ShowToast($"HERO LEVEL {5 * (row + 1)}"); return; } Meta.PickTalent(Meta.HeroId, row, ch); Sfx.Toggle(); RefreshTalents(); RefreshLabels(); };
                    talBtns.Add((r, k, b));
                }
            }
            s.onOpen = RefreshTalents;
        }
        void RefreshTalents()
        {
            string h = Meta.HeroId; int hi = Meta.HeroIdx(h);
            talHead.text = $"{Prefs.HeroName.ToUpperInvariant()}  //  LV {Meta.HeroLevel(h)}  //  {Meta.TalentsPicked(h)} PICKED  <color=#a0ff6a>+{Meta.TalentsPicked(h)}% CP</color>";
            foreach (var (r, k, b) in talBtns)
            {
                bool open = Meta.RowOpen(h, r), on = Meta.Talent(h, r) == k;
                if (b.label != null) { b.label.text = Meta.Talents[hi * 10 + r, k - 1]; b.label.fontSize = 20; }
                b.selected = on; b.dimmed = !open; b.tint = on ? Holo.Acid : Holo.Cyan; if (b.label != null) b.label.color = open ? Holo.Ink : Holo.A(Holo.Muted, 0.6f);
            }
        }

        // ---- OPS: Daily Ops (rank 2), the Hot Zone (rank 4), achievements (upgrade brief 9-10)
        Text opsHead, opsHot, opsAch; readonly List<(HoloButton row, HoloButton reroll)> opsRows = new(); HoloButton opsChest;
        void BuildOps()
        {
            var s = MakeSheet("ops", "DAILY OPS  //  HOT ZONE  //  ACHIEVEMENTS", "OPS", 1250, out var c);
            MakeWallet(s.rt);
            opsHead = Holo.Label(Holo.Rect(c, "Head", new Vector2(0, 1), new Vector2(0, 0), new Vector2(1160, 40)), "", 26, Holo.Acid, TextAnchor.UpperLeft); opsHead.supportRichText = true;
            for (int i = 0; i < 3; i++)
            {
                var row = Holo.Button(c, "Mission" + i, new Vector2(900, 108), "", null, 30);
                var rt = (RectTransform)row.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(0, -52 - i * 122);
                var rr = Holo.Button(c, "Reroll" + i, new Vector2(240, 108), "REROLL", "retry", 26);
                var rrt = (RectTransform)rr.transform; rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0, 1); rrt.anchoredPosition = new Vector2(920, -52 - i * 122);
                int slot = i; rr.onClick = () => { if (Ops.Reroll(slot)) { Sfx.Toggle(); RefreshOps(); RefreshWallets(); } else { Sfx.Deny(); ShowToast(Progress.D.cores < Ops.RerollCost ? $"{Ops.RerollCost} CORES" : "CAN'T REROLL THAT"); } };
                opsRows.Add((row, rr));
            }
            opsChest = Holo.Button(c, "Chest", new Vector2(1160, 100), "DAILY CHEST", "quality", 34, true);
            var crt = (RectTransform)opsChest.transform; crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0, 1); crt.anchoredPosition = new Vector2(0, -420);
            opsChest.onClick = () => { var r = Ops.OpenChest(); if (r != null) { Sfx.Open(); Haptics.Heavy(); ShowToast(r); RefreshOps(); RefreshWallets(); } else { Sfx.Deny(); ShowToast("FINISH ALL 3 OPS"); } };
            opsHot = Holo.Label(Holo.Rect(c, "Hot", new Vector2(0, 1), new Vector2(0, -540), new Vector2(1160, 80)), "", 26, new Color(1f, 0.6f, 0.25f), TextAnchor.UpperLeft); opsHot.supportRichText = true;
            opsAch = Holo.Label(Holo.Rect(c, "Ach", new Vector2(0, 1), new Vector2(0, -630), new Vector2(1160, 190)), "", 24, Holo.Ink, TextAnchor.UpperLeft); opsAch.supportRichText = true;
            s.onOpen = RefreshOps;
        }
        void RefreshOps()
        {
            if (opsHead == null) return;
            var left = Ops.OpsNow.Date.AddDays(1) - Ops.OpsNow;
            opsHead.text = $"COMMANDER RANK {Meta.Rank}  //  STREAK {Progress.D.dailyStreak}  //  RESETS IN {left.Hours}H {left.Minutes:00}M" + (string.IsNullOrEmpty(Progress.D.title) ? "" : $"  //  <color=#ffcc33>{Progress.D.title}</color>");
            bool open = Ops.DailyOpen; var ms = open ? Ops.Today() : new List<Ops.Mission>();
            for (int i = 0; i < opsRows.Count; i++)
            {
                var (row, rr) = opsRows[i]; bool has = i < ms.Count;
                if (row.label != null) row.label.text = !open ? (i == 0 ? "DAILY OPS OPEN AT COMMANDER RANK 2" : "") : has ? ms[i].def.text : "";
                row.SetSub(has ? (ms[i].done ? "DONE  //  " : $"{ms[i].progress} / {ms[i].def.goal}  //  ") + Ops.RewardText(ms[i].def) : "", has && ms[i].done ? Holo.Acid : Holo.Muted, 22);
                row.dimmed = !has || ms[i].done;
                rr.gameObject.SetActive(has && !ms[i].done);
                rr.SetSub(Ops.RerollCost == 0 ? "FREE" : $"{Ops.RerollCost} CORES", Holo.Muted, 18);
            }
            opsChest.dimmed = !Ops.ChestReady;
            opsChest.SetSub(Progress.D.dailyChest ? "OPENED  //  BACK TOMORROW" : "20 CORES + 3 RUNS OF CREDITS + 5 SHARDS", Holo.Muted, 22);
            opsHot.text = !Ops.HotOpen ? "HOT ZONE OPENS AT COMMANDER RANK 4" : Ops.HotLine ?? "HOT ZONE: CLEAR A FEW ZONES FIRST";
            var sb = new System.Text.StringBuilder($"<b>ACHIEVEMENTS {Ops.AchDone} / {Ops.All.Length}</b>\n");
            int n = 0; foreach (var a in Ops.All) { if (Ops.Has(a) || n >= 4) continue; sb.AppendLine($"<color=#8a96a8>○</color> {a.name}  <color=#8a96a8>{a.desc}  +{a.cores} CORES</color>"); n++; }
            opsAch.text = sb.ToString();
        }

        // ---- SETTINGS
        readonly List<(RectTransform knob, Image track, Text value, Func<bool> get, string on, string off)> switches = new();

        void BuildSettings()
        {
            MakeSheet("settings", "SHIP SYSTEMS", "SETTINGS", 880, out var c);
            Row(c, 0, "SOUND", "sound", () => Prefs.Sound, () => Prefs.Sound = !Prefs.Sound, "ON", "OFF");
            Row(c, 1, "HAPTICS", "haptic", () => Prefs.HapticsOn, () => { Prefs.HapticsOn = !Prefs.HapticsOn; Haptics.Medium(); }, "ON", "OFF");
            Row(c, 2, "QUALITY", "quality", () => Prefs.HighQuality, () => { Prefs.HighQuality = !Prefs.HighQuality; game.ApplyQuality(); }, "HIGH", "LOW");
            Row(c, 3, "CAMERA", "eye", () => Prefs.CamCloseOn, game.ToggleCamera, "CLOSE", "FAR");
            int nr = 4;
            if (Prefs.Supports120) Row(c, nr++, "FRAME RATE", "bolt", () => Prefs.Fps120, () => Prefs.Fps120 = !Prefs.Fps120, "120", "60"); // ProMotion screens only
            Row(c, nr, "SHOW FPS", "quality", () => Prefs.ShowFps, () => Prefs.ShowFps = !Prefs.ShowFps, "ON", "OFF");
            // TEST MODE: every zone open, runs save nothing; a second tap adds TEST POWER (the zone scaled to your CP), a third turns both off
            var trow = Row(c, nr + 1, "TEST MODE", "flask", () => Prefs.TestMode, () => { if (!Prefs.TestMode) Prefs.TestMode = true; else if (!Prefs.TestPower) Prefs.TestPower = true; else { Prefs.TestMode = false; Prefs.TestPower = false; } }, "ON", "OFF");
            trow.SetSub("ALL ZONES OPEN, NO PROGRESS SAVED", new Color(1f, 0.72f, 0.15f), 20);
            System.Action trefresh = () => trow.SetSub(Prefs.TestMode ? (Prefs.TestPower ? "ON + TEST POWER (ZONE SCALED TO YOUR CP)" : "ON: ALL ZONES OPEN, NO PROGRESS SAVED") : "OFF  //  TAP: ALL ZONES OPEN, NO PROGRESS SAVED", new Color(1f, 0.72f, 0.15f), 20);
            trefresh(); var tprev = trow.onClick; trow.onClick = () => { tprev?.Invoke(); trefresh(); };
            var dev = Holo.Label(Holo.Rect(c, "DevReset", new Vector2(1, 0), new Vector2(0, 0), new Vector2(200, 50)), "v0.9", 20, Holo.A(Holo.Muted, 0.4f), TextAnchor.LowerRight);
            dev.rectTransform.pivot = new Vector2(1, 0); dev.raycastTarget = true;
            int devTaps = 0; float devT = 0;
            dev.gameObject.AddComponent<Tap>().onTap = () =>
            {   // hidden dev reset: 5 quick taps on the version tag wipes progress (credits, levels, gear, sectors)
                if (Time.unscaledTime - devT > 2f) devTaps = 0; devT = Time.unscaledTime;
                if (++devTaps >= 5) { devTaps = 0; Progress.ResetAll(); RefreshLabels(); dev.text = "PROGRESS RESET"; Sfx.Deny(); Haptics.Heavy(); }
            };
            // (the note under the rows made way for SHOW FPS; the font's OFL text ships in Resources/UI)
        }

        HoloButton Row(RectTransform c, int i, string label, string icon, Func<bool> get, Action toggle, string on, string off)
        {
            var b = Holo.Button(c, "Row_" + label, new Vector2(792, 108), label, icon, 42);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(0, -i * 116); // 7 rows fit the sheet
            var track = Holo.Rect(b.body, "Track", new Vector2(1, 0.5f), new Vector2(-36, 0), new Vector2(150, 62));
            track.pivot = new Vector2(1, 0.5f);
            var ti = Holo.Img(track, Holo.FillSprite, Holo.A(Holo.Glass, 1), true);
            Holo.Img(Holo.Fill(track, "Edge"), Holo.EdgeSprite, Holo.A(Holo.Cyan, 0.7f), true);
            var knob = Holo.Rect(track, "Knob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 48));
            Holo.Img(knob, Holo.FillSprite, Holo.Ink, true);
            var val = Holo.Label(Holo.Rect(b.body, "Value", new Vector2(1, 0.5f), new Vector2(-206, 0), new Vector2(160, 60)), "", 32, Holo.Acid, TextAnchor.MiddleRight);
            val.rectTransform.pivot = new Vector2(1, 0.5f);
            val.fontStyle = FontStyle.Bold;
            b.onClick = () => { toggle(); Sfx.Toggle(); };
            switches.Add((knob, ti, val, get, on, off));
            return b;
        }

        void TickSettings(float dt)
        {
            foreach (var (knob, track, value, get, on, off) in switches)
            {
                bool v = get();
                var p = knob.anchoredPosition;
                p.x = Mathf.MoveTowards(p.x, v ? 38 : -38, dt * 600);
                knob.anchoredPosition = p;
                track.color = Color.Lerp(track.color, v ? Holo.A(Holo.Acid, 0.55f) : Holo.A(Holo.Glass, 1), dt * 12);
                value.text = v ? on : off;
                value.color = v ? Holo.Acid : Holo.Muted;
            }
        }

        // ------------------------------------------------------------ drop transition
        void BuildTransition()
        {
            transitionRt = Holo.Fill(canvas.transform, "Drop");
            doorTop = Door("DoorTop", true);
            doorBot = Door("DoorBottom", false);
            flash = Holo.Img(Holo.Stretch(Holo.Rect(transitionRt, "Flash"), -120, -120, -120, -120), Holo.WhiteSprite, new Color(1, 1, 1, 0), false, true); // also swallows taps mid-drop
            var tr = Holo.Rect(transitionRt, "Text", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 440));
            var dt = Holo.Label(Holo.Rect(tr, "Title", new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(1400, 120)), "DROP POD AWAY", 96, Holo.Ink);
            dt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            dt.fontStyle = FontStyle.Bold; Holo.GlowText(dt, Holo.Cyan, 0.8f);
            var sub = Holo.Label(Holo.Rect(tr, "Sub", new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(1400, 60)), "SECTOR 7  //  BRACE FOR IMPACT", 38, Holo.Acid);
            sub.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            sub.fontStyle = FontStyle.Bold;
            dropTextCg = tr.gameObject.AddComponent<CanvasGroup>();
            dropTextCg.blocksRaycasts = false;
            transitionRt.gameObject.SetActive(false);
        }

        RectTransform Door(string name, bool top)
        {
            var d = Holo.Rect(transitionRt, name);
            d.anchorMin = new Vector2(0, top ? 0.5f : 0); d.anchorMax = new Vector2(1, top ? 1 : 0.5f);
            d.offsetMin = new Vector2(-80, top ? 0 : -80); d.offsetMax = new Vector2(80, top ? 80 : 0);
            Holo.Img(d, Holo.WhiteSprite, Metal);
            for (int i = 1; i < 5; i++) // panel seams
            {
                var s = Holo.Rect(d, "Seam");
                s.anchorMin = new Vector2(0, i / 5f); s.anchorMax = new Vector2(1, i / 5f); s.sizeDelta = new Vector2(0, 3);
                Holo.Img(s, Holo.WhiteSprite, new Color(0.1f, 0.15f, 0.2f, 1));
            }
            float edgeY = top ? 0 : 1;
            var glowR = Holo.Rect(d, "SeamGlow");
            glowR.anchorMin = new Vector2(0, edgeY); glowR.anchorMax = new Vector2(1, edgeY); glowR.pivot = new Vector2(0.5f, edgeY);
            glowR.sizeDelta = new Vector2(0, 90); glowR.anchoredPosition = Vector2.zero;
            Holo.Raw(glowR, Holo.BandTex, Holo.A(Holo.Cyan, 0.5f));
            var hz = Holo.Rect(d, "Hazard");
            hz.anchorMin = new Vector2(0, edgeY); hz.anchorMax = new Vector2(1, edgeY); hz.pivot = new Vector2(0.5f, edgeY);
            hz.sizeDelta = new Vector2(0, 30); hz.anchoredPosition = new Vector2(0, top ? 14 : -14);
            var hr = Holo.Raw(hz, Holo.HazardTex, Color.white);
            hazards = hazards == null ? new[] { hr } : new[] { hazards[0], hr };
            var line = Holo.Rect(d, "Line");
            line.anchorMin = new Vector2(0, edgeY); line.anchorMax = new Vector2(1, edgeY); line.pivot = new Vector2(0.5f, edgeY);
            line.sizeDelta = new Vector2(0, 6); line.anchoredPosition = Vector2.zero;
            Holo.Img(line, Holo.WhiteSprite, Holo.Cyan);
            return d;
        }

        public void Deploy()
        {
            if (Busy || !Visible) return;
            if (current != null) { current.open = false; current.onClose?.Invoke(); }
            dropT = 0; impact = landed = false;
            transitionRt.gameObject.SetActive(true);
            Sfx.Drop(); Haptics.Medium();
        }

        void TickDrop(float dt, Vector2 S)
        {
            dropT += dt;
            const float CloseAt = 0.42f, OpenAt = 1.05f;
            float closeK = Mathf.Clamp01(dropT / CloseAt);
            float openK = Mathf.Clamp01((dropT - OpenAt) / 0.5f);
            float door = dropT < OpenAt ? closeK * closeK * closeK : 1 - (1 - Mathf.Pow(1 - openK, 3));
            float off = (1 - door) * (S.y * 0.5f + 200);
            doorTop.anchoredPosition = new Vector2(0, off);
            doorBot.anchoredPosition = new Vector2(0, -off);
            foreach (var h in hazards) h.uvRect = new Rect(t * 0.6f, 0, (S.x + 160) / 30f, 1);
            if (!impact) shake = Mathf.Max(shake, closeK * 0.3f);
            if (!impact && dropT >= CloseAt) { impact = true; shake = 1; Haptics.Heavy(); }
            if (!landed && dropT >= 0.75f)
            {   // behind the closed hatch: swap the menu for the level
                landed = true;
                shakeRoot.gameObject.SetActive(false);
                if (previewRoot != null) previewRoot.gameObject.SetActive(false);
                if (previewCam != null) previewCam.enabled = false;
                game.Deploy();
            }
            float fl = impact ? Mathf.Clamp01(1 - (dropT - CloseAt) / 0.32f) : 0;
            flash.color = Color.Lerp(Holo.A(Holo.Cyan, 0), new Color(0.88f, 1f, 1f, 0.92f), fl);
            dropTextCg.alpha = impact ? Mathf.Clamp01((dropT - 0.46f) / 0.1f) * Mathf.Clamp01((OpenAt - dropT) / 0.12f) : 0;
            // buttons blast off to the left as the hatch drops
            for (int i = 0; i < mainButtons.Count; i++)
            {
                var (b, cg, _) = mainButtons[i];
                float k = Mathf.Clamp01((dropT - i * 0.04f) / 0.35f);
                if (b != gearBtn) ((RectTransform)b.transform).anchoredPosition = new Vector2(-k * k * 900, ((RectTransform)b.transform).anchoredPosition.y);
                cg.alpha = 1 - k;
            }
            logoCg.alpha = 1 - closeK;
            if (dropT >= 1.65f) { dropT = -1; Hide(); }
        }

        // ------------------------------------------------------------ show / hide / frame
        public void Show()
        {
            canvas.enabled = true;
            shakeRoot.gameObject.SetActive(true);
            transitionRt.gameObject.SetActive(false);
            foreach (var s in sheets.Values) { s.open = false; s.k = 0; s.rt.gameObject.SetActive(false); }
            current = null; dim.gameObject.SetActive(false);
            introT = 0; dropT = -1; shake = 0;
            RefreshLabels();
            Prefs.ApplySound();
            Update();
        }

        public void Hide()
        {
            canvas.enabled = false;
            shakeRoot.gameObject.SetActive(false);
            transitionRt.gameObject.SetActive(false);
            if (previewRoot != null) previewRoot.gameObject.SetActive(false);
            if (previewCam != null) previewCam.enabled = false;
        }

        static float OutBack(float k) { const float c1 = 1.70158f, c3 = c1 + 1; float x = k - 1; return 1 + c3 * x * x * x + c1 * x * x; }

        Vector2 AccelScreen()
        {
            var a = Input.acceleration;
            return Screen.orientation == ScreenOrientation.LandscapeRight ? new Vector2(a.y, -a.x) : new Vector2(-a.y, a.x);
        }

        Text toast; float toastT;
        public void ShowToast(string s) { if (toast == null) return; toast.text = s.ToUpperInvariant(); toastT = 2.2f; toast.transform.SetAsLastSibling(); }
        void Update()
        {
            if (canvas == null || !canvas.enabled) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (toast != null) { toastT -= dt; toast.color = Holo.A(Holo.Acid, Mathf.Clamp01(toastT * 2)); }
            t += dt; introT += dt;
            var S = canvasRt.rect.size;

            if (dropT >= 0) TickDrop(dt, S);
            shake = Mathf.Max(0, shake - dt * 2.4f);
            var sh = UnityEngine.Random.insideUnitCircle * shake * shake * 28;
            shakeRoot.anchoredPosition = sh;
            transitionRt.anchoredPosition = sh;
            if (!shakeRoot.gameObject.activeSelf) return;

            // parallax: slow drift plus device tilt (mouse in the editor)
            Vector2 want = Vector2.zero;
            if (Application.isMobilePlatform)
            {
                var a = AccelScreen();
                accBase = Vector2.Lerp(accBase == Vector2.zero ? a : accBase, a, dt * 0.4f);
                want = Vector2.ClampMagnitude(a - accBase, 0.35f) * 2.2f;
            }
            else if (Input.mousePresent && Screen.width > 0)
                want = new Vector2(Input.mousePosition.x / Screen.width - 0.5f, Input.mousePosition.y / Screen.height - 0.5f) * 0.8f;
            tilt = Vector2.Lerp(tilt, want, dt * 3);
            drift = new Vector2(Mathf.Sin(t * 0.09f) * 0.6f + Mathf.Sin(t * 0.031f) * 0.4f, Mathf.Sin(t * 0.07f + 1.3f) * 0.6f);
            float h = Mathf.Max(S.x / BgAspect, S.y);
            bgRt.sizeDelta = new Vector2(h * BgAspect, h) * 1.07f;
            bgRt.anchoredPosition = drift * 16 - tilt * 34;
            bgRt.localScale = Vector3.one * (1 + 0.012f * Mathf.Sin(t * 0.12f));

            acidGlow.color = Holo.A(Holo.Acid, 0.07f + 0.11f * (0.5f + 0.5f * Mathf.Sin(t * 1.25f)));
            purpleGlow.color = Holo.A(Holo.Purple, 0.05f + 0.07f * (0.5f + 0.5f * Mathf.Sin(t * 0.85f + 2.1f)));
            foreach (var (img, ph, sp) in hives)
            {
                float k = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * sp + ph), 3);
                img.color = Holo.A(Holo.Acid, 0.06f + 0.4f * k);
                img.rectTransform.localScale = Vector3.one * (0.8f + 0.35f * k);
            }
            foreach (var m in motes)
            {
                m.p += m.v * dt;
                if (m.p.x > S.x + 40) m.p.x -= S.x + 80;
                if (m.p.y > S.y + 40) m.p.y -= S.y + 80;
                m.rt.anchoredPosition = m.p - tilt * 80 * m.depth + drift * 22 * m.depth;
                m.img.color = Holo.A(m.img.color, m.a * (0.55f + 0.45f * Mathf.Sin(t * m.tw + m.depth * 9)));
            }
            scan.uvRect = new Rect(0, t * 0.5f, 1, S.y / 4f);

            // logo fades in with a little glitch, then floats
            float lk = Mathf.Clamp01((introT - 0.1f) / 0.9f);
            float glitch = introT < 1.3f && UnityEngine.Random.value < 0.25f ? 0.35f : (UnityEngine.Random.value < dt * 0.12f ? 0.7f : 1f);
            if (dropT < 0) logoCg.alpha = lk * lk * (3 - 2 * lk) * glitch;
            logoRt.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, 1 - Mathf.Pow(1 - lk, 3));
            logoRt.anchoredPosition = new Vector2(6, -2 + Mathf.Sin(t * 0.8f) * 3);
            logoGlow.color = Holo.A(Holo.Cyan, lk * (0.1f + 0.06f * Mathf.Sin(t * 1.7f)));

            if (dropT < 0)
            {
                foreach (var (b, cg, delay) in mainButtons)
                {
                    float k = Mathf.Clamp01((introT - delay) / 0.42f);
                    cg.alpha = k;
                    cg.interactable = cg.blocksRaycasts = k > 0.6f;
                    var rt = (RectTransform)b.transform;
                    if (b != gearBtn) rt.anchoredPosition = new Vector2(-(1 - OutBack(k)) * 160, rt.anchoredPosition.y);
                }
            }

            teleT -= dt;
            if (teleT <= 0)
            {
                teleT = 0.25f;
                float spread = 63.4f + t * 0.006f;
                telemetry.text = $"ORBIT ALT  {412.6f + Mathf.Sin(t * 0.3f) * 0.4f:0.0} KM\nHIVE SPREAD  <color=#b4ff3d>{spread:0.00}%</color>\nDROP WINDOW  <color=#66e0ff>OPEN</color>";
            }

            TickSheets(dt);
            TickLoadout(dt);
            TickSettings(dt);
            if (current != null && current.open && Input.GetKeyDown(KeyCode.Escape)) CloseSheet();
        }
    }
}
