using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Maze
{
    // The run's screens on top of the HUD: the biomass (XP) bar under the HP bar, the level-up / cache card pick, the
    // evolution flash, and the run's end (extracted or swarmed) with the loot and the upgrade that closes the gap.
    // Icons: the painted set in Resources/UI/Icons (Holo.Art), full colour and untinted; holo glyphs where none exists yet.
    public partial class RunUi : MonoBehaviour
    {
        Game game;
        Canvas canvas; RectTransform safe;
        RectTransform xpRoot; Image xpFill; TMP_Text lvText, floorText;
        RectTransform choiceRt; CanvasGroup choiceCg; TMP_Text choiceTitle, choiceSub; readonly List<HoloButton> cards = new();
        RectTransform trackPanel; Image introIcon; CanvasGroup trackCg; int trackKey = -1; float trackT;
        RectTransform bossRt, bossBar; TMP_Text bossName; Image bossFill; Enemy bossShown; readonly List<GameObject> bossNotches = new(); float bossShowT;
        TMP_Text puText, chainText; Image flashImg, vigImg, chainBg, chainFill; RectTransform chainRt; float lastHp = -1;
        readonly (Image bg, Image ic, Text tx)[] pips = new (Image, Image, Text)[5]; RectTransform pipsRoot, goalBar, introRt; TMP_Text goalText, introFloor, introTitle, introLine; Image goalFill, introBand; float introT = -1;
        RectTransform evoRt; TMP_Text evoText, evoSub; float evoT = -1;
        RectTransform endRt; RunResultsView resultsView; Image evoIcon;
        float t;

        public void Init(Game g)
        {
            game = g;
            canvas = Holo.MakeCanvas(transform, "RunUi", 5);
            // feel overlays (under everything else on this canvas): the screen flash and the edge vignette
            flashImg = Holo.Img(Holo.Fill(canvas.transform, "FeelFlash"), Holo.WhiteSprite, Color.clear); flashImg.raycastTarget = false;
            vigImg = Holo.Img(Holo.Fill(canvas.transform, "FeelVignette"), Sprite.Create(Holo.VignetteTex, new Rect(0, 0, 128, 64), new Vector2(0.5f, 0.5f)), Color.clear); vigImg.raycastTarget = false;
            safe = Holo.SafeRoot(Holo.Fill(canvas.transform, "Root"));
            BuildBlueBossAndChain();

            BuildRoundedRunHeader();
            BuildBlueFloorIntro();

            // The approved light-blue card presentation retains the existing offer/Pick contract.
            BuildBlueChoices();

            BuildBlueEvolution();

            // The approved asset-backed results screen; gameplay rewards are already applied by RunEnd.
            endRt = Holo.Fill(canvas.transform, "End");
            resultsView = endRt.gameObject.AddComponent<RunResultsView>();
            resultsView.Init(game);
            endRt.gameObject.SetActive(false);
        }

        static void Place(HoloButton b, Vector2 anchor, Vector2 pos)
        {
            var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos;
        }

        public void RunStarted() { endRt.gameObject.SetActive(false); choiceRt.gameObject.SetActive(false); ResetBlueFloorClear(); }

        public void FloorIntro(int f, Game.Goal g, string line)
        {
            introRt.gameObject.SetActive(true); introT = 0;
            introFloor.text = f == Game.Floors ? $"Floor {f} of {Game.Floors} · Final floor" : $"Floor {f} of {Game.Floors}";
            introTitle.text = DisplayCase(Game.GoalNames[(int)g]); introLine.text = DisplayCase(line.Replace("//","·"));
            var ia = BlueUi.Art("objective_" + Game.GoalNames[(int)g].ToLowerInvariant()) ?? Holo.Art("goal_" + Game.GoalNames[(int)g].ToLowerInvariant()); introIcon.gameObject.SetActive(ia != null); if (ia != null) introIcon.sprite = ia;
            ArrangeBlueFloorIntro(ia != null);
            introTitle.color = g == Game.Goal.Boss ? BlueUi.Red : BlueUi.Navy;
            if (!Game.SimRun) { if (!FirstCryoAudio.Reveal(game)) Sfx.Open(); Haptics.Medium(); }
        }

        static string Icon(Card k) => k.evo ? "quality" : k.id.StartsWith("cache") ? (k.id == "cache_heal" ? "squad" : k.id == "cache_card" ? "quality" : "deploy")
            : k.group == 0 ? "rifle" : k.group == 1 ? "squad" : k.group == 2 ? "bolt" : "person";
        public static string ArtName(Card k) => k.evo ? "evo_" + k.icon : k.id == "cache_heal" ? "cache_med" : k.id == "cache_card" ? "cache_tech" : k.id == "cache_credits" ? "cache_supply" : "card_" + k.icon;

        public void ShowChoice(Game.Choice c, List<Card> offer) => ShowBlueChoice(c,offer);
        public void HideChoice() { choiceRt.gameObject.SetActive(false); HideBlueFloorClear(); }

        public void ShowEvolution(Card k)
        {
            evoRt.gameObject.SetActive(true); evoT = 0;
            evoText.text = DisplayCase(k.name); evoSub.text = k.desc;
            var a = Holo.Art(ArtName(k)); evoIcon.gameObject.SetActive(a != null); if (a != null) evoIcon.sprite = a;
        }

        public void ShowEnd(bool won, int reached, Progress.Loot loot)
        {
            HideBlueFloorClear();
            choiceRt.gameObject.SetActive(false);
            endRt.gameObject.SetActive(true);
            resultsView.Show(won, reached, loot);
        }

        void Update()
        {
            RefreshBlueFeedbackVisibility();
            t += Time.unscaledDeltaTime;
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
                    bossName.text = DisplayCase(be.brain != null ? be.brain.Name : (game.sector?.boss ?? "Boss"));
                    if (be.brain != null) foreach (var m in be.brain.PhaseMarks)
                    {
                        var nr = Holo.Rect(bossBar, "Notch", new Vector2(m, 0.5f), Vector2.zero, new Vector2(4, 20)); Holo.Img(nr, Holo.WhiteSprite, new Color(1, 1, 1, 0.8f)); bossNotches.Add(nr.gameObject);
                    }
                }
                bossShowT += Time.unscaledDeltaTime;
                float frac = Mathf.Clamp01(be.hp / Mathf.Max(1, be.maxHp));
                bossFill.fillAmount = Mathf.Min(frac, bossShowT / 0.4f);
                bossFill.color = be.brain != null && be.brain.Invulnerable ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
            }
            bool chainOn = game != null && game.run && game.state == Game.State.Playing && (Chain.Tier > 0 || Chain.Pool > 0.5f);
            chainRt.gameObject.SetActive(chainOn);
            if (chainOn)
            {
                var cc = Chain.Colors[Chain.Tier]; chainText.text = $"x{Chain.ScoreMult:0.##}"; chainText.color = BlueUi.Navy; chainFill.color = cc; chainFill.fillAmount = Chain.NextFrac;
                float a = Chain.Bump > 0 ? 1f : 0.6f; chainBg.color = new Color(1,1,1,a); chainText.color = Holo.A(BlueUi.Navy,a);
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
                if (FirstCryoFx.Active && EnemyAI.Latched > 0) puText.text = $"<color=#ffb1a3>LATCHED {EnemyAI.Latched} // DASH TO SHAKE</color>";
                // the pill stays readable at 85% after 3 s; it brightens when the goal moves on (every 10%, a nest, a door) or
                // in the last 10 s of SURVIVE
                int key = Mathf.FloorToInt(Mathf.Clamp01(game.GoalProgress) * 10) + 100 * (int)game.goal + 1000 * game.floor;
                if (key != trackKey) { trackKey = key; trackT = 0; }
                trackT += Time.unscaledDeltaTime;
                bool urgent = game.goal == Game.Goal.Survive && game.GoalProgress > 0.78f;
                float ta = urgent || trackT < 3f ? 1f : Mathf.MoveTowards(trackCg.alpha, 0.85f, Time.unscaledDeltaTime * 1.5f);
                trackCg.alpha = Mathf.MoveTowards(trackCg.alpha, ta, Time.unscaledDeltaTime * 4f);
                goalText.color = Holo.A(Holo.Ink, trackCg.alpha); goalFill.color = Holo.A(Holo.Acid, trackCg.alpha);
                goalFill.fillAmount = Mathf.MoveTowards(goalFill.fillAmount, Mathf.Clamp01(game.GoalProgress), Time.unscaledDeltaTime * 3f);
            }
            if (introT >= 0 && game != null && game.state == Game.State.Choosing) { introT = -1; introRt.gameObject.SetActive(false); } // a card pick covers it
            if (introT >= 0)
            {
                introT += Time.unscaledDeltaTime;
                float a = introT < 0.2f ? introT / 0.2f : Mathf.Clamp01((2.4f - introT) / 0.5f);
                introTitle.color = Holo.A(introTitle.color, a); introIcon.color = Holo.A(Color.white, a); introFloor.color = Holo.A(introFloor.color, a); introLine.color = Holo.A(introLine.color, a); introBand.color = Holo.A(Color.white,a);
                introRt.localScale = Vector3.one * (1 + 0.12f * Mathf.Clamp01(1 - introT * 4));
                if (introT > 2.4f) { introT = -1; introRt.gameObject.SetActive(false); }
            }
            if (show)
            {
                xpFill.fillAmount = Mathf.MoveTowards(xpFill.fillAmount, game.xp / (float)game.XpNeed, Time.unscaledDeltaTime * 2.5f);
                lvText.text = game.level >= Game.MaxLevel ? "MAX " + Game.MaxLevel : "LV " + game.level;
                floorText.text = $"FLOOR {game.floor}/{Game.Floors}";
            }
            if (choiceRt.gameObject.activeSelf) choiceCg.alpha = Mathf.MoveTowards(choiceCg.alpha, 1, Time.unscaledDeltaTime * 6);
            if (evoT >= 0)
            {
                evoT += Time.unscaledDeltaTime;
                float a = evoT < 0.15f ? evoT / 0.15f : Mathf.Clamp01((1.8f - evoT) / 0.5f);
                evoRt.localScale = Vector3.one * (1 + 0.15f * Mathf.Clamp01(1 - evoT * 3));
                foreach (var g in evoRt.GetComponentsInChildren<Graphic>()) { var c = g.color; c.a = g is Image && g.gameObject == evoRt.gameObject ? .14f * a : a; g.color = c; }
                if (evoT > 1.8f) { evoT = -1; evoRt.gameObject.SetActive(false); }
            }
            if (game != null) RefreshRoundedRunHeader();
        }
    }
}
