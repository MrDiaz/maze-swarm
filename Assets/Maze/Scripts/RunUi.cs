using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Maze
{
    // The run's screens on top of the HUD: the biomass (XP) bar under the HP bar, the level-up / cache card pick, the
    // evolution flash, and the run's end (extracted or swarmed) with the loot and the upgrade that closes the gap.
    // Icons: the painted set in Resources/UI/Icons (Holo.Art), full colour and untinted; holo glyphs where none exists yet.
    public class RunUi : MonoBehaviour
    {
        Game game;
        Canvas canvas; RectTransform safe;
        RectTransform xpRoot; Image xpFill; Text lvText, floorText;
        RectTransform choiceRt; CanvasGroup choiceCg; Text choiceTitle, choiceSub; readonly List<HoloButton> cards = new();
        RectTransform trackPanel; Image introIcon; CanvasGroup trackCg; int trackKey = -1; float trackT;
        Text endGrade, endVerdict, endScore; float endT = -1;
        RectTransform bossRt, bossBar; Text bossName; Image bossFill; Enemy bossShown; readonly List<GameObject> bossNotches = new(); float bossShowT;
        Text puText, chainText; Image flashImg, vigImg, chainBg, chainFill; RectTransform chainRt; float lastHp = -1;
        readonly (Image bg, Image ic, Text tx)[] pips = new (Image, Image, Text)[5]; RectTransform pipsRoot, goalBar, introRt; Text goalText, introFloor, introTitle, introLine; Image goalFill, introBand; float introT = -1;
        RectTransform evoRt; Text evoText, evoSub; float evoT = -1;
        RectTransform endRt; Text endTitle, endCaption, endFloor, endPower, endLoot, endSuggest; HoloButton upgradeBtn; Image evoIcon;
        readonly List<(Image ic, Text tx)> lootRows = new();
        float t;

        public void Init(Game g)
        {
            game = g;
            canvas = Holo.MakeCanvas(transform, "RunUi", 5);
            // feel overlays (under everything else on this canvas): the screen flash and the edge vignette
            flashImg = Holo.Img(Holo.Fill(canvas.transform, "FeelFlash"), Holo.WhiteSprite, Color.clear); flashImg.raycastTarget = false;
            vigImg = Holo.Img(Holo.Fill(canvas.transform, "FeelVignette"), Sprite.Create(Holo.VignetteTex, new Rect(0, 0, 128, 64), new Vector2(0.5f, 0.5f)), Color.clear); vigImg.raycastTarget = false;
            safe = Holo.SafeRoot(Holo.Fill(canvas.transform, "Root"));
            // the boss bar: top centre, 50% wide, notches where the phases change, fills over 0.4 s at the intro
            bossRt = Holo.Rect(safe, "BossBar", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(1100, 54)); bossRt.pivot = new Vector2(0.5f, 1);
            bossName = Holo.Label(Holo.Rect(bossRt, "Name", new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(1100, 30)), "", 24, new Color(1f, 0.55f, 0.45f)); bossName.fontStyle = FontStyle.Bold;
            var bb = Holo.Rect(bossRt, "Bar", new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(1100, 14)); bb.pivot = new Vector2(0.5f, 0);
            Holo.Img(bb, Holo.WhiteSprite, new Color(0, 0.02f, 0.05f, 0.75f));
            bossFill = Holo.Img(Holo.Fill(bb, "Fill", 1), Holo.WhiteSprite, new Color(0.95f, 0.25f, 0.2f)); bossFill.type = Image.Type.Filled; bossFill.fillMethod = Image.FillMethod.Horizontal;
            bossBar = bb;
            // the Swarm Chain chip: left edge, 30% down
            chainRt = Holo.Rect(safe, "Chain", new Vector2(0, 0.7f), new Vector2(24, 0), new Vector2(150, 64)); chainRt.pivot = new Vector2(0, 0.5f);
            chainBg = Holo.Img(chainRt, Holo.WhiteSprite, new Color(0, 0.02f, 0.05f, 0.6f));
            chainText = Holo.Label(Holo.Rect(chainRt, "T", new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(150, 44)), "", 34, Color.white); chainText.fontStyle = FontStyle.Bold;
            var cb = Holo.Rect(chainRt, "Bar", new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(130, 6)); cb.pivot = new Vector2(0.5f, 0);
            Holo.Img(cb, Holo.WhiteSprite, new Color(1, 1, 1, 0.15f));
            chainFill = Holo.Img(Holo.Fill(cb, "Fill", 1), Holo.WhiteSprite, Color.white); chainFill.type = Image.Type.Filled; chainFill.fillMethod = Image.FillMethod.Horizontal;

            // biomass bar: right under the HP panel
            // biomass (XP): a thin full-width bar along the top edge, the level in a small badge at its left end
            xpRoot = Holo.Rect(safe, "Xp", new Vector2(0, 1), Vector2.zero, new Vector2(0, 40)); xpRoot.anchorMax = new Vector2(1, 1); xpRoot.pivot = new Vector2(0.5f, 1);
            xpRoot.offsetMin = new Vector2(20, -40); xpRoot.offsetMax = new Vector2(-180, -6);
            var bar = Holo.Rect(xpRoot, "Bar", new Vector2(0, 0.5f), new Vector2(150, 0), new Vector2(0, 10)); bar.anchorMax = new Vector2(1, 0.5f); bar.pivot = new Vector2(0, 0.5f); bar.offsetMin = new Vector2(150, -5); bar.offsetMax = new Vector2(0, 5);
            Holo.Img(bar, Holo.WhiteSprite, new Color(0.04f, 0.02f, 0.08f, 0.8f));
            xpFill = Holo.Img(Holo.Fill(bar, "Fill", 2), Holo.WhiteSprite, new Color(0.75f, 0.38f, 1f));
            xpFill.type = Image.Type.Filled; xpFill.fillMethod = Image.FillMethod.Horizontal;
            var badge = Holo.Plain(xpRoot, "Badge", 0.85f); badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0, 0.5f); badge.anchoredPosition = Vector2.zero; badge.sizeDelta = new Vector2(138, 40);
            lvText = Holo.Label(Holo.Rect(badge, "Lv", new Vector2(0, 0.5f), new Vector2(46, 0), new Vector2(90, 34)), "LV 1", 24, new Color(0.85f, 0.6f, 1f), TextAnchor.MiddleLeft);
            lvText.rectTransform.pivot = new Vector2(0, 0.5f);
            var bio = Holo.Art("cur_biomass");
            if (bio != null) { var bi = Holo.Img(Holo.Rect(badge, "Bio", new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(34, 34)), bio, Color.white); bi.preserveAspect = true; bi.rectTransform.pivot = new Vector2(0, 0.5f); }
            lvText.fontStyle = FontStyle.Bold;
            floorText = Holo.Label(Holo.Rect(xpRoot, "Floor", new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(100, 30)), "", 22, Holo.Muted, TextAnchor.MiddleRight);
            floorText.gameObject.SetActive(false);
            // the run's shape: 5 pips (each floor's objective), the current one lit; the objective tracker under them
            trackPanel = Holo.Plain(safe, "Tracker", 0.75f); trackPanel.anchorMin = trackPanel.anchorMax = trackPanel.pivot = new Vector2(0.5f, 1);
            trackPanel.anchoredPosition = new Vector2(0, -52); trackPanel.sizeDelta = new Vector2(560, 64); // a small pill under the XP bar
            trackCg = trackPanel.gameObject.AddComponent<CanvasGroup>();
            var pr = Holo.Rect(safe, "Pips", new Vector2(0.5f, 1), new Vector2(0, -152), new Vector2(880, 58)); pr.pivot = new Vector2(0.5f, 1);
            for (int i = 0; i < 5; i++)
            {
                var pip = Holo.Rect(pr, "Pip" + i, new Vector2(0.5f, 0.5f), new Vector2((i - 2) * 112, 0), new Vector2(102, 54));
                var bg = Holo.Img(pip, Holo.FillSprite, Holo.A(Holo.Glass, 0.9f), true);
                var ic = Holo.Img(Holo.Rect(pip, "Ic", new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(30, 30)), Holo.Icon("rifle"), Holo.Muted); ic.rectTransform.pivot = new Vector2(0, 0.5f);
                var tx = Holo.Label(Holo.Rect(pip, "T", new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(64, 40)), "", 17, Holo.Muted, TextAnchor.MiddleLeft); tx.rectTransform.pivot = new Vector2(0, 0.5f); tx.fontStyle = FontStyle.Bold;
                pips[i] = (bg, ic, tx);
            }
            pipsRoot = pr;
            goalText = Holo.Label(Holo.Rect(safe, "Goal", new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(880, 40)), "", 26, Holo.Ink); goalText.fontStyle = FontStyle.Bold;
            goalText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0.03f, 0.06f, 0.8f);
            var gb = Holo.Rect(safe, "GoalBar", new Vector2(0.5f, 1), new Vector2(0, -96), new Vector2(460, 8)); gb.pivot = new Vector2(0.5f, 1);
            Holo.Img(gb, Holo.WhiteSprite, new Color(0, 0.02f, 0.05f, 0.7f));
            goalFill = Holo.Img(Holo.Fill(gb, "Fill", 1), Holo.WhiteSprite, Holo.Acid); goalFill.type = Image.Type.Filled; goalFill.fillMethod = Image.FillMethod.Horizontal;
            goalBar = gb;
            puText = Holo.Label(Holo.Rect(safe, "PowerUps", new Vector2(0.5f, 1), new Vector2(0, -122), new Vector2(1400, 34)), "", 22, Holo.Ink); puText.fontStyle = FontStyle.Bold; puText.supportRichText = true;
            puText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0.03f, 0.06f, 0.8f);
            // the floor intro: the objective name, big, and its one-line goal
            introRt = Holo.Fill(canvas.transform, "Intro");
            introBand = Holo.Img(Holo.Rect(introRt, "Band", new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(3000, 230)), Holo.WhiteSprite, new Color(0.01f, 0.03f, 0.06f, 0.6f));
            introFloor = Holo.Label(Holo.Rect(introRt, "F", new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(1200, 40)), "", 30, Holo.Acid);
            introTitle = Holo.Label(Holo.Rect(introRt, "T", new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(1600, 130)), "", 110, Holo.Ink); introTitle.fontStyle = FontStyle.Bold;
            Holo.GlowText(introTitle, Holo.Cyan, 0.5f);
            introIcon = Holo.Img(Holo.Rect(introRt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(140, 140)), Holo.WhiteSprite, Color.white); introIcon.preserveAspect = true;
            introLine = Holo.Label(Holo.Rect(introRt, "L", new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(1400, 44)), "", 34, Holo.Ink);
            introRt.gameObject.SetActive(false);

            // the card pick
            choiceRt = Holo.Fill(canvas.transform, "Choice");
            Holo.Img(choiceRt, Holo.WhiteSprite, new Color(0.01f, 0.02f, 0.06f, 0.78f), false, true);
            choiceCg = choiceRt.gameObject.AddComponent<CanvasGroup>();
            var cs = Holo.SafeRoot(choiceRt);
            choiceTitle = Holo.Label(Holo.Rect(cs, "Title", new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1200, 100)), "LEVEL UP", 86, Holo.Ink); choiceTitle.fontStyle = FontStyle.Bold;
            Holo.GlowText(choiceTitle, new Color(0.75f, 0.38f, 1f), 0.5f);
            choiceSub = Holo.Label(Holo.Rect(cs, "Sub", new Vector2(0.5f, 1), new Vector2(0, -158), new Vector2(1200, 40)), "CHOOSE ONE", 28, Holo.Acid);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = Holo.Button(cs, "Card" + i, new Vector2(380, 470), null, null);
                var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2((i - 1) * 420, -60);
                // a soft dark disc behind the icon: thin painted icons (the evolved guns) stay readable on the glass card
                var back = Holo.Img(Holo.Rect(b.body, "Back", new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(210, 210)), Holo.SoftSprite, new Color(0.02f, 0.04f, 0.08f, 0.85f)); back.rectTransform.pivot = new Vector2(0.5f, 1);
                Holo.Img(Holo.Rect(b.body, "Icon", new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(170, 170)), Holo.Icon("bolt"), Holo.Ink).rectTransform.pivot = new Vector2(0.5f, 1);
                var nm = Holo.Label(Holo.Rect(b.body, "Name", new Vector2(0.5f, 1), new Vector2(0, -228), new Vector2(350, 56)), "", 38, Holo.Ink); nm.fontStyle = FontStyle.Bold;
                Holo.Label(Holo.Rect(b.body, "Desc", new Vector2(0.5f, 1), new Vector2(0, -290), new Vector2(330, 110)), "", 26, Holo.Muted, TextAnchor.UpperCenter);
                Holo.Label(Holo.Rect(b.body, "Picks", new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(330, 36)), "", 26, Holo.Acid).fontStyle = FontStyle.Bold;
                b.onClick = () => game.Pick(idx);
                cards.Add(b);
            }
            choiceRt.gameObject.SetActive(false);

            // the evolution flash
            evoRt = Holo.Fill(canvas.transform, "Evolve");
            Holo.Img(evoRt, Holo.WhiteSprite, new Color(1f, 0.8f, 0.3f, 0.18f));
            Holo.Img(Holo.Rect(evoRt, "Band", new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(3000, 300)), Holo.WhiteSprite, new Color(0.05f, 0.02f, 0f, 0.6f));
            evoText = Holo.Label(Holo.Rect(evoRt, "T", new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1600, 160)), "", 120, new Color(1f, 0.85f, 0.35f)); evoText.fontStyle = FontStyle.Bold;
            Holo.GlowText(evoText, new Color(1f, 0.6f, 0.1f), 0.7f);
            evoSub = Holo.Label(Holo.Rect(evoRt, "S", new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(1400, 50)), "", 34, Holo.Ink);
            evoIcon = Holo.Img(Holo.Rect(evoRt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(210, 210)), Holo.WhiteSprite, Color.white); evoIcon.preserveAspect = true;
            evoRt.gameObject.SetActive(false);

            // the run's end
            endRt = Holo.Fill(canvas.transform, "End");
            Holo.Img(endRt, Holo.WhiteSprite, new Color(0.02f, 0.02f, 0.05f, 0.86f), false, true);
            var es = Holo.SafeRoot(endRt);
            var panel = Holo.Panel(es, "Panel");
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f); panel.sizeDelta = new Vector2(1400, 860);
            endCaption = Holo.Label(Holo.Rect(panel, "Cap", new Vector2(0, 1), new Vector2(50, -36), new Vector2(1000, 34)), "", 26, Holo.Acid, TextAnchor.UpperLeft); endCaption.supportRichText = true;
            endTitle = Holo.Label(Holo.Rect(panel, "Title", new Vector2(0, 1), new Vector2(46, -70), new Vector2(1100, 120)), "", 104, Holo.Ink, TextAnchor.UpperLeft); endTitle.fontStyle = FontStyle.Bold;
            endGrade = Holo.Label(Holo.Rect(panel, "Grade", new Vector2(1, 1), new Vector2(-120, -110), new Vector2(200, 200)), "", 180, Holo.Ink); endGrade.fontStyle = FontStyle.Bold;
            endVerdict = Holo.Label(Holo.Rect(panel, "Verdict", new Vector2(0, 1), new Vector2(50, -350), new Vector2(620, 36)), "", 26, Holo.Muted, TextAnchor.UpperLeft);
            endScore = Holo.Label(Holo.Rect(panel, "Score", new Vector2(0, 1), new Vector2(50, -300), new Vector2(620, 40)), "", 32, Holo.Ink, TextAnchor.UpperLeft); endScore.fontStyle = FontStyle.Bold;
            endFloor = Holo.Label(Holo.Rect(panel, "Floor", new Vector2(0, 1), new Vector2(50, -200), new Vector2(620, 50)), "", 36, Holo.Ink, TextAnchor.UpperLeft); endFloor.fontStyle = FontStyle.Bold;
            endPower = Holo.Label(Holo.Rect(panel, "Power", new Vector2(0, 1), new Vector2(50, -252), new Vector2(620, 44)), "", 30, Holo.Muted, TextAnchor.UpperLeft); endPower.supportRichText = true;
            Holo.Label(Holo.Rect(panel, "LootT", new Vector2(0, 1), new Vector2(700, -200), new Vector2(640, 40)), "REWARDS", 30, Holo.Muted, TextAnchor.UpperLeft);
            endLoot = Holo.Label(Holo.Rect(panel, "Loot", new Vector2(0, 1), new Vector2(700, -246), new Vector2(660, 300)), "", 32, Holo.Ink, TextAnchor.UpperLeft);
            endLoot.supportRichText = true; endLoot.gameObject.SetActive(false);
            for (int r = 0; r < 9; r++)
            {   // a loot row: the painted icon + the line
                var row = Holo.Rect(panel, "Loot" + r, new Vector2(0, 1), new Vector2(700, -248 - r * 34), new Vector2(660, 32)); row.pivot = new Vector2(0, 1);
                var ic = Holo.Img(Holo.Rect(row, "Ic", new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(32, 32)), Holo.WhiteSprite, Color.white); ic.rectTransform.pivot = new Vector2(0, 0.5f); ic.preserveAspect = true;
                var tx = Holo.Label(Holo.Rect(row, "T", new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(620, 32)), "", 24, Holo.Ink, TextAnchor.MiddleLeft); tx.rectTransform.pivot = new Vector2(0, 0.5f); tx.supportRichText = true;
                lootRows.Add((ic, tx));
            }
            endSuggest = Holo.Label(Holo.Rect(panel, "Suggest", new Vector2(0, 1), new Vector2(50, -560), new Vector2(1300, 80)), "", 26, Holo.Acid, TextAnchor.UpperLeft);
            upgradeBtn = Holo.Button(panel, "Upgrade", new Vector2(420, 110), "UPGRADE", "rifle", 40, true);
            Place(upgradeBtn, new Vector2(0, 0), new Vector2(50, 40));
            upgradeBtn.onClick = () =>
            {   // UPGRADE ALL 3: buy the three best affordable upgrades (by CP per Credit); nothing affordable: the Armory
                if (picks.Count > 0 && picks[0].credits <= Progress.D.credits)
                {
                    int before = Progress.Power(); foreach (var pk in picks) pk.buy();
                    Haptics.Medium(); Sfx.Toggle(); RefreshPicks(); endPower.text += $"   <color=#a0ff6a>{before:N0} → {Progress.Power():N0}</color>"; return;
                }
                endRt.gameObject.SetActive(false); game.ToMenu(); game.menu.OpenSheet("loadout");
            };
            var retry = Holo.Button(panel, "Retry", new Vector2(280, 110), "RETRY", "retry", 40);
            Place(retry, new Vector2(1, 0), new Vector2(-640, 40));
            retry.onClick = () => { endRt.gameObject.SetActive(false); game.Restart(); };
            var sector = Holo.Button(panel, "Sector", new Vector2(280, 110), "SECTOR", "deploy", 36);
            Place(sector, new Vector2(1, 0), new Vector2(-350, 40));
            sector.onClick = () => { endRt.gameObject.SetActive(false); game.ToSector(); };
            var menuB = Holo.Button(panel, "Menu", new Vector2(280, 110), "MENU", "home", 36);
            Place(menuB, new Vector2(1, 0), new Vector2(-50, 40));
            menuB.onClick = () => { endRt.gameObject.SetActive(false); game.ToMenu(); };
            endRt.gameObject.SetActive(false);
        }

        static void Place(HoloButton b, Vector2 anchor, Vector2 pos)
        {
            var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos;
        }

        public void RunStarted() { endRt.gameObject.SetActive(false); choiceRt.gameObject.SetActive(false); }

        public void FloorIntro(int f, Game.Goal g, string line)
        {
            introRt.gameObject.SetActive(true); introT = 0;
            introFloor.text = f == Game.Floors ? $"FLOOR {f} OF {Game.Floors}  //  FINAL" : $"FLOOR {f} OF {Game.Floors}";
            introTitle.text = Game.GoalNames[(int)g]; introLine.text = line;
            var ia = Holo.Art("goal_" + Game.GoalNames[(int)g].ToLower()); introIcon.gameObject.SetActive(ia != null); if (ia != null) introIcon.sprite = ia;
            introIcon.rectTransform.anchoredPosition = new Vector2(-introTitle.preferredWidth / 2 - 90, 100); // left of the title
            introTitle.color = g == Game.Goal.Boss ? Holo.Red : Holo.Ink;
            if (!Game.SimRun) { Sfx.Open(); Haptics.Medium(); }
        }

        static string Icon(Card k) => k.evo ? "quality" : k.id.StartsWith("cache") ? (k.id == "cache_heal" ? "squad" : k.id == "cache_card" ? "quality" : "deploy")
            : k.group == 0 ? "rifle" : k.group == 1 ? "squad" : k.group == 2 ? "bolt" : "person";
        public static string ArtName(Card k) => k.evo ? "evo_" + k.icon : k.id == "cache_heal" ? "cache_med" : k.id == "cache_card" ? "cache_tech" : k.id == "cache_credits" ? "cache_supply" : "card_" + k.icon;

        public void ShowChoice(Game.Choice c, List<Card> offer)
        {
            choiceRt.gameObject.SetActive(true); choiceCg.alpha = 0;
            choiceTitle.text = c == Game.Choice.Cache ? "SUPPLY CACHE" : c == Game.Choice.Rare ? "RARE UPGRADE" : $"LEVEL {game.level}";
            choiceSub.text = c == Game.Choice.Cache ? $"FLOOR {game.floor} CLEARED  //  TAKE ONE CRATE" : game.ObRecommend ? "PICK ONE. YOUR BUILD GROWS EVERY LEVEL." : "CHOOSE ONE UPGRADE";
            for (int i = 0; i < cards.Count; i++)
            {
                var b = cards[i]; bool on = i < offer.Count; b.gameObject.SetActive(on); if (!on) continue;
                float sp = offer.Count >= 4 ? 400f : 420f; var crt = (RectTransform)b.transform;   // 2, 3 or 4 cards, centred
                crt.anchoredPosition = new Vector2((i - (offer.Count - 1) * 0.5f) * sp, -60); crt.localScale = Vector3.one * (offer.Count >= 4 ? 0.94f : 1f);
                var k = offer[i];
                b.tint = k.Tint; b.selected = k.evo;
                var ic = b.body.Find("Icon").GetComponent<Image>();
                Holo.SetIcon(ic, ArtName(k), Icon(k), k.Tint);
                ic.rectTransform.localScale = Vector3.one * (k.evo ? 1.12f : 1f);
                b.body.Find("Name").GetComponent<Text>().text = k.name;
                b.body.Find("Desc").GetComponent<Text>().text = k.desc;
                int have = game.mods[k.id];
                b.body.Find("Picks").GetComponent<Text>().text = game.ObRecommend && i == 0 ? "RECOMMENDED" : k.evo ? "EVOLUTION" : k.id.StartsWith("cache") ? "" : $"{have + 1}/{k.max}";
                if (game.ObRecommend && i == 0) b.selected = true;
            }
        }
        public void HideChoice() => choiceRt.gameObject.SetActive(false);

        public void ShowEvolution(Card k)
        {
            evoRt.gameObject.SetActive(true); evoT = 0;
            evoText.text = k.name; evoSub.text = k.desc;
            var a = Holo.Art(ArtName(k)); evoIcon.gameObject.SetActive(a != null); if (a != null) evoIcon.sprite = a;
        }

        bool testNote;
        public void ShowEnd(bool won, int reached, Progress.Loot loot)
        {
            testNote = game.LastRunTest;
            choiceRt.gameObject.SetActive(false);
            endRt.gameObject.SetActive(true);
            endTitle.text = won ? "SECTOR CLEARED" : "SWARMED";
            endTitle.color = won ? Holo.Acid : Holo.Red;
            var zone = game.sector; endT = 0;
            endGrade.text = ""; endVerdict.text = ""; endScore.text = $"SCORE {game.RunScore:N0}";
            endCaption.text = won ? (zone != null ? $"{zone.name}  //  STARS {Progress.StarCount(zone)}/3" : "EXTRACTION COMPLETE  //  LOOT SECURED") : "SIGNAL LOST  //  OPERATIVE DOWN";
            if (testNote) endCaption.text = "<color=#ffb826>TEST RUN: PROGRESS NOT SAVED</color>  //  " + endCaption.text;   // TEST MODE
            endCaption.color = won ? Holo.Acid : Holo.Red;
            endFloor.text = game.Endless ? $"GATE FLOOR {reached}  //  BEST {game.GateBest}  //  {game.kills} KILLS" : $"FLOOR {reached} OF {Game.Floors}  //  LV {game.level}  //  {game.kills} KILLS";
            int p = Progress.Power(), rec = game.sector != null ? Meta.RecommendedAt(game.sector, Meta.Difficulty) : Progress.Recommended(game.tier);
            endPower.text = $"POWER {p:N0}  /  RECOMMENDED {rec:N0}"; endPower.color = Progress.PowerColor(p, rec);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<color=#f5c400>{loot.credits:N0}</color> CREDITS");
            sb.AppendLine($"<color=#7fd0ff>{loot.parts}</color> WEAPON PARTS");
            sb.AppendLine($"<color=#c0c8d0>{loot.alloy}</color> ALLOY");
            foreach (var gi in loot.gear) sb.AppendLine($"<color=#{ColorUtility.ToHtmlStringRGB(Progress.RarityColors[gi.rarity])}>{Progress.Rarities[gi.rarity]} {Progress.Slots[gi.slot]}</color>  {Progress.StatText(gi)}");
            if (!won) sb.AppendLine($"<size=24><color=#8a96a8>(a death pays {Mathf.RoundToInt(20 * Mathf.Clamp(reached, 1, 4))}% of a win)</color></size>");
            endLoot.text = sb.ToString();
            var rows = new List<(string art, string glyph, string text)>
            {
                ("cur_credits", "deploy", $"<color=#f5c400>{loot.credits:N0}</color> CR   <color=#7fd0ff>{loot.parts}</color> PARTS   <color=#c0c8d0>{loot.alloy}</color> ALLOY" + (Ops.R.hot ? $"   <color=#ff9a4a>HOT x2 {Ops.MaterialNames[Ops.HotMaterial]}</color>" : "")),
            };
            if (zone != null) foreach (int si in game.newStars) rows.Add((null, "quality", $"<color=#ffcc33>NEW STAR</color>  {zone.stars[si]}"));
            rows.Add((null, "squad", $"<color=#7fd0ff>+{game.XpGained} XP</color>  COMMANDER RANK {Meta.Rank}" + (game.RankUps.Count > 0 ? $"  <color=#a0ff6a>{game.RankUps[^1]}</color>" : "")));
            if (game.ShardsGained > 0) rows.Add((null, "squad", $"<color=#ff9a4a>+{game.ShardsGained}</color>  {game.ShardHero.ToUpperInvariant()} SHARDS"));
            foreach (var st in game.StarTrack) rows.Add((null, "quality", $"<color=#ffcc33>STAR TRACK</color>  {st}"));
            foreach (var ot in game.OpsToasts) rows.Add((null, "quality", $"<color=#ff9a4a>{ot}</color>"));
            if (loot.blueprint != null) rows.Add((null, "rifle", $"<color=#7ae8ff>BLUEPRINT</color>  {loot.blueprint}"));
            if (loot.relic != null) rows.Add((null, "quality", $"<color=#ff7ad9>RELIC</color>  {loot.relic}" + (Meta.RelicSlotOpen ? "" : "  <color=#8a96a8>(slot at 60★)</color>")));
            foreach (var gi in loot.gear) rows.Add(("gear_" + Progress.Slots[gi.slot].ToLower(), "squad", $"<color=#{ColorUtility.ToHtmlStringRGB(Progress.RarityColors[gi.rarity])}>{Progress.Rarities[gi.rarity]} {Progress.Slots[gi.slot]}</color>  {Progress.StatText(gi)}"));
            if (!won) rows.Add((null, null, $"<size=22><color=#8a96a8>a death pays {Mathf.RoundToInt(20 * Mathf.Clamp(reached, 1, 4))}% of a win</color></size>"));
            for (int r = 0; r < lootRows.Count; r++)
            {
                bool on = r < rows.Count; lootRows[r].ic.transform.parent.gameObject.SetActive(on); if (!on) continue;
                lootRows[r].tx.text = rows[r].text;
                bool icon = rows[r].art != null || rows[r].glyph != null;
                lootRows[r].ic.gameObject.SetActive(icon);
                if (icon) Holo.SetIcon(lootRows[r].ic, rows[r].art, rows[r].glyph, rows[r].art == null ? new Color(1f, 0.8f, 0.2f) : Holo.Ink);
            }
            RefreshPicks();
            upgradeBtn.gameObject.SetActive(true);
        }

        List<Meta.Pick> picks = new();
        void RefreshPicks()
        {
            picks = Meta.BestUpgrades(3);
            int p = Progress.Power(), rec = game.sector != null ? Meta.RecommendedAt(game.sector, Meta.Difficulty) : Progress.Recommended(game.tier);
            var tip = Meta.BranchTip(game.sector);
            if (picks.Count == 0) { endSuggest.text = "NOTHING TO UPGRADE YET  //  " + (tip ?? "REPLAY A CLEARED ZONE FOR CREDITS"); upgradeBtn.label.text = "ARMORY"; upgradeBtn.dimmed = false; return; }
            int gain = 0, cost = 0; var parts = new List<string>();
            foreach (var pk in picks) { gain += pk.gain; cost += pk.credits; parts.Add($"{pk.name} +{pk.gain} CP ({pk.credits:N0} CR)"); }
            bool can = cost <= Progress.D.credits;
            endSuggest.text = string.Join("   ", parts) + $"\nPOWER {p:N0} / {rec:N0}  →  {p + gain:N0} / {rec:N0}" + (tip != null ? "   //   " + tip : "");
            upgradeBtn.label.text = can ? "UPGRADE ALL 3" : "ARMORY"; upgradeBtn.dimmed = false;
        }
        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (endT >= 0 && game != null && endRt.gameObject.activeSelf)
            {   // the grade slams in at 2.6 s (wins only)
                float before = endT; endT += Time.unscaledDeltaTime;
                if (game.Grade != null && before < 2.6f && endT >= 2.6f)
                {
                    endGrade.text = game.Grade; endVerdict.text = game.GradeVerdict + (game.GradeBonus > 0 ? $"   +{game.GradeBonus:N0} CR" : "");
                    endGrade.color = game.Grade == "S" ? new Color(1f, 0.8f, 0.2f) : game.Grade == "A" ? new Color(1f, 0.95f, 0.75f) : game.Grade == "B" ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                    Haptics.Heavy(); if (game.Grade == "S") Feel.Flash(new Color(1f, 0.8f, 0.2f), 0.3f, 0.2f);
                }
                if (endGrade.text.Length > 0) { float k = Mathf.Clamp01((endT - 2.6f) / 0.18f); endGrade.rectTransform.localScale = Vector3.one * Mathf.Lerp(2f, 1f, k); }
            }
            flashImg.color = new Color(Feel.FlashColor.r, Feel.FlashColor.g, Feel.FlashColor.b, Feel.FlashAlpha);
            vigImg.color = new Color(Feel.VignetteColor.r, Feel.VignetteColor.g, Feel.VignetteColor.b, Feel.VignetteAlpha);
            var be = game != null && game.run && game.state == Game.State.Playing ? game.Boss : null;
            bossRt.gameObject.SetActive(be != null);
            if (be != null)
            {
                if (be != bossShown)
                {   // a new boss: name, notches, fill-in
                    bossShown = be; bossShowT = 0;
                    foreach (var n in bossNotches) Destroy(n); bossNotches.Clear();
                    bossName.text = be.brain != null ? be.brain.Name.ToUpperInvariant() : (game.sector?.boss ?? "BOSS").ToUpperInvariant();
                    if (be.brain != null) foreach (var m in be.brain.PhaseMarks)
                    {
                        var nr = Holo.Rect(bossBar, "Notch", new Vector2(m, 0.5f), Vector2.zero, new Vector2(4, 20)); Holo.Img(nr, Holo.WhiteSprite, new Color(1, 1, 1, 0.8f)); bossNotches.Add(nr.gameObject);
                    }
                }
                bossShowT += Time.unscaledDeltaTime;
                float frac = Mathf.Clamp01(be.hp / Mathf.Max(1, be.maxHp));
                bossFill.fillAmount = Mathf.Min(frac, bossShowT / 0.4f);
                bossFill.color = be.brain != null && be.brain.Invulnerable ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.95f, 0.25f, 0.2f);
            }
            bool chainOn = game != null && game.run && game.state == Game.State.Playing && (Chain.Tier > 0 || Chain.Pool > 0.5f);
            chainRt.gameObject.SetActive(chainOn);
            if (chainOn)
            {
                var cc = Chain.Colors[Chain.Tier]; chainText.text = $"x{Chain.ScoreMult:0.##}"; chainText.color = cc; chainFill.color = cc; chainFill.fillAmount = Chain.NextFrac;
                float a = Chain.Bump > 0 ? 1f : 0.6f; chainBg.color = new Color(0, 0.02f, 0.05f, 0.6f * a); chainText.color = new Color(cc.r, cc.g, cc.b, a);
                chainRt.localScale = Vector3.one * (Chain.Bump > 0.25f ? 1f + (Chain.Bump - 0.25f) * 2.3f : 1f);
            }
            // heals: a green edge flash
            if (game != null && game.run && game.player != null)
            {
                if (lastHp >= 0 && game.player.hp > lastHp + 0.5f && game.state == Game.State.Playing) Feel.Vignette(new Color(0.3f, 1f, 0.4f), 0.3f, 0.3f);
                lastHp = game.player.hp;
            }
            bool show = game != null && game.run && (game.state == Game.State.Playing || game.state == Game.State.Choosing || game.state == Game.State.Paused);
            xpRoot.gameObject.SetActive(show && game.state != Game.State.Choosing);
            bool trk = show && game.state != Game.State.Choosing;
            pipsRoot.gameObject.SetActive(false); goalText.gameObject.SetActive(trk); goalBar.gameObject.SetActive(trk); trackPanel.gameObject.SetActive(trk);
            if (trk)
            {
                for (int i = 0; i < 5; i++)
                {
                    var gg = game.plan[i]; bool cur = i == game.floor - 1, done = i < game.floor - 1;
                    var col = cur ? (gg == Game.Goal.Boss ? Holo.Red : Holo.Acid) : done ? Holo.A(Holo.Acid, 0.45f) : Holo.Muted;
                    pips[i].bg.color = cur ? Holo.A(col, 0.28f) : Holo.A(Holo.Glass, 0.85f);
                    // painted goal icons when they exist (goal_purge.png ...; the boss uses ui_boss), else the glyphs
                    string artN = done ? null : "goal_" + Game.GoalNames[(int)gg].ToLower();
                    var pa = artN != null ? Holo.Art(artN) : null;
                    if (pa != null) { pips[i].ic.sprite = pa; pips[i].ic.color = cur ? Color.white : new Color(1, 1, 1, 0.5f); pips[i].ic.preserveAspect = true; }
                    else { pips[i].ic.sprite = Holo.Icon(done ? "check" : Game.GoalIcons[(int)gg]); pips[i].ic.color = col; }
                    pips[i].tx.text = Game.GoalNames[(int)gg]; pips[i].tx.color = col;
                }
                goalText.text = game.GoalLine;
                puText.text = PowerUps.Line() + (CellFloors.Rad > 0.02f ? $"<color=#c8ff3a>RAD {Mathf.RoundToInt(CellFloors.Rad * 100)}%</color>" : "");
                // the pill fades to ~40% after 3 s; it brightens when the goal moves on (every 10%, a nest, a door) or
                // in the last 10 s of SURVIVE
                int key = Mathf.FloorToInt(Mathf.Clamp01(game.GoalProgress) * 10) + 100 * (int)game.goal + 1000 * game.floor;
                if (key != trackKey) { trackKey = key; trackT = 0; }
                trackT += Time.unscaledDeltaTime;
                bool urgent = game.goal == Game.Goal.Survive && game.GoalProgress > 0.78f;
                float ta = urgent || trackT < 3f ? 1f : Mathf.MoveTowards(trackCg.alpha, 0.4f, Time.unscaledDeltaTime * 1.5f);
                trackCg.alpha = Mathf.MoveTowards(trackCg.alpha, ta, Time.unscaledDeltaTime * 4f);
                goalText.color = Holo.A(Holo.Ink, trackCg.alpha); goalFill.color = Holo.A(Holo.Acid, trackCg.alpha);
                goalFill.fillAmount = Mathf.MoveTowards(goalFill.fillAmount, Mathf.Clamp01(game.GoalProgress), Time.unscaledDeltaTime * 3f);
            }
            if (introT >= 0 && game != null && game.state == Game.State.Choosing) { introT = -1; introRt.gameObject.SetActive(false); } // a card pick covers it
            if (introT >= 0)
            {
                introT += Time.unscaledDeltaTime;
                float a = introT < 0.2f ? introT / 0.2f : Mathf.Clamp01((2.4f - introT) / 0.5f);
                introTitle.color = Holo.A(introTitle.color, a); introIcon.color = Holo.A(Color.white, a); introFloor.color = Holo.A(introFloor.color, a); introLine.color = Holo.A(introLine.color, a); introBand.color = Holo.A(introBand.color, 0.6f * a);
                introRt.localScale = Vector3.one * (1 + 0.12f * Mathf.Clamp01(1 - introT * 4));
                if (introT > 2.4f) { introT = -1; introRt.gameObject.SetActive(false); }
            }
            if (show)
            {
                xpFill.fillAmount = Mathf.MoveTowards(xpFill.fillAmount, game.xp / (float)game.XpNeed, Time.unscaledDeltaTime * 2.5f);
                lvText.text = "LV " + game.level;
                floorText.text = $"FLOOR {game.floor}/{Game.Floors}";
            }
            if (choiceRt.gameObject.activeSelf) choiceCg.alpha = Mathf.MoveTowards(choiceCg.alpha, 1, Time.unscaledDeltaTime * 6);
            if (evoT >= 0)
            {
                evoT += Time.unscaledDeltaTime;
                float a = evoT < 0.15f ? evoT / 0.15f : Mathf.Clamp01((1.8f - evoT) / 0.5f);
                evoRt.localScale = Vector3.one * (1 + 0.15f * Mathf.Clamp01(1 - evoT * 3));
                foreach (var g in evoRt.GetComponentsInChildren<Graphic>()) { var c = g.color; c.a = g is Image ? (g.name == "Icon" ? 1f : g.name == "Band" ? 0.6f : 0.18f) * a : a; g.color = c; }
                if (evoT > 1.8f) { evoT = -1; evoRt.gameObject.SetActive(false); }
            }
        }
    }
}
