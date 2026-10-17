using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Maze
{
    // A presentation pause before the existing cache selection. It never grants loot,
    // changes the prepared offer, or starts a floor itself.
    public sealed class BlueFloorClearView : MonoBehaviour
    {
        Game game;
        RectTransform board;
        TMP_Text location, completed, nextFloor, nextGoal, nextObjective;
        readonly TMP_Text[] routeLabels = new TMP_Text[Game.Floors], routeStates = new TMP_Text[Game.Floors];
        readonly UnityEngine.UI.Image[] routeCards = new UnityEngine.UI.Image[Game.Floors], routeIcons = new UnityEngine.UI.Image[Game.Floors], routeChecks = new UnityEngine.UI.Image[Game.Floors];
        TMP_Text creditChoice;
        UnityEngine.UI.Image nextArt, arenaPreview, nextGoalIcon, nextModeBadge;
        UnityEngine.UI.RawImage authoredArenaPreview;
        UnityEngine.UI.Button continueButton;
        Action proceed;
        int shownFloor;
        bool submitted;
        public UnityEngine.UI.Button ContinueButton => continueButton;
        public bool Visible => gameObject.activeInHierarchy;

        static TMP_Text Center(TMP_Text text) { text.alignment = TextAlignmentOptions.Center; return text; }

        static UnityEngine.UI.Image Icon(Transform parent, string name, string art, float x, float y, float w, float h)
        {
            var image = BlueUi.Image(parent, name, art, x, y, w, h); image.preserveAspect = true; return image;
        }

        public void Init(Game g)
        {
            game = g;
            Holo.Img(Holo.Fill(transform, "InputShield"), Holo.WhiteSprite, Color.clear, false, true);
            BlueUi.Background(transform, "scene_cryo");
            board = BlueUi.Frame(transform, "FloorClearedLayout");
            Icon(board, "ClearedCheck", "check_green", 435, 24, 130, 130);
            BlueUi.Text(board, "Title", "Floor cleared!", 589, 16, 853, 112, 100);
            BlueUi.Panel(board, "LocationPill", 594, 121, 627, 57, true);
            location = Center(BlueUi.Text(board, "Location", "", 605, 124, 605, 52, 35));
            BlueUi.Panel(board, "CompletedPanel", 53, 197, 796, 714, false, true);
            completed = Center(BlueUi.Text(board, "CompletedObjective", "", 84, 213, 734, 79, 62));
            arenaPreview = Icon(board, "ArenaPreview", "arena_cryo", 69, 283, 760, 466);
            authoredArenaPreview = Holo.Raw(BlueUi.Rect(board, "AuthoredArenaPreview", 69, 283, 760, 466), null, Color.white);
            authoredArenaPreview.raycastTarget = false;
            BlueUi.Panel(board, "CachePanel", 73, 739, 759, 160);
            BlueUi.Text(board, "CacheHeading", "Supply cache", 96, 746, 470, 42, 35);
            BlueUi.Text(board, "CacheHint", "Choose one next", 526, 753, 287, 34, 24, BlueUi.Muted);
            CacheOption("Heal", "stat_health", 93, 228);
            CacheOption("Rare upgrade", "stat_power", 340, 228);
            creditChoice = CacheOption("", "currency_coin", 588, 228);
            BlueUi.Panel(board, "RoutePanel", 862, 197, 761, 714, false, true);
            BlueUi.Text(board, "RouteHeading", "Run route", 899, 213, 674, 74, 61);
            for (int i = 0; i < Game.Floors; i++)
            {
                float x = 889 + i * 144;
                routeCards[i] = BlueUi.Panel(board, "FloorCard" + (i + 1), x, 297, 128, 178, true);
                routeIcons[i] = BlueUi.Image(board, "FloorIcon" + (i + 1), (Sprite)null, x + 24, 323, 80, 81);
                routeIcons[i].preserveAspect = true;
                routeLabels[i] = Center(BlueUi.Text(board, "FloorLabel" + (i + 1), "", x + 4, 403, 120, 38, 29));
                routeLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
                routeLabels[i].fontSizeMin = 17;
                routeStates[i] = Center(BlueUi.Text(board, "FloorState" + (i + 1), "", x + 5, 439, 118, 29, 22));
                routeChecks[i] = Icon(board, "CompletedCheck" + (i + 1), "check_green", x + 84, 286, 46, 46);
                if (i < Game.Floors - 1)
                {
                    var connector = BlueUi.Panel(board, "RouteConnector" + i, x + 122, 361, 29, 13, true);
                    connector.color = new Color(.44f, .77f, 1f);
                }
            }
            BlueUi.Panel(board, "NextFloorPanel", 879, 498, 727, 269);
            BlueUi.Text(board, "NextFloorCaption", "NEXT FLOOR", 902, 522, 620, 43, 28, BlueUi.Blue);
            nextFloor = BlueUi.Text(board, "NextFloorName", "", 900, 561, 680, 66, 52);
            nextModeBadge = BlueUi.Panel(board, "NextModeBadge", 902, 630, 230, 59, true); nextModeBadge.color = BlueUi.Blue;
            nextGoalIcon = BlueUi.Image(board, "NextGoalIcon", (Sprite)null, 936, 639, 43, 43);
            nextGoalIcon.preserveAspect = true;
            nextGoal = Center(BlueUi.Text(board, "NextGoal", "", 993, 634, 127, 51, 32, Color.white));
            nextGoal.textWrappingMode = TextWrappingModes.NoWrap;
            nextGoal.fontSizeMin = 16;
            nextObjective = BlueUi.Text(board, "NextObjective", "", 901, 696, 466, 56, 33);
            nextObjective.textWrappingMode = TextWrappingModes.Normal;
            nextArt = Icon(board, "NextObjectiveArt", "nest_cryo", 1295, 605, 298, 160);
            continueButton = BlueUi.Button(board, "Continue", "Continue ›", 887, 770, 710, 116, SubmitContinue, true, null, 66);
        }

        TMP_Text CacheOption(string label, string art, float x, float width)
        {
            BlueUi.Panel(board, "Cache" + x, x, 792, width, 94, true);
            Icon(board, "CacheIcon" + x, art, x + 13, 799, 76, 78);
            return BlueUi.Text(board, "CacheValue" + x, label, x + 96, 810, width - 104, 65, label == "Rare upgrade" ? 24 : 31);
        }

        public void Show(Action callback)
        {
            proceed = callback; submitted = false; shownFloor = game.floor;
            continueButton.interactable = true;
            gameObject.SetActive(true); transform.SetAsLastSibling();
            string zone = CohesiveMenuArt.SectorName(game.sector);
            var currentGoal = BlueRunPresentation.RouteGoal(game, game.floor);
            location.text = $"{zone} · Floor {game.floor} · {BlueRunPresentation.GoalName(currentGoal)}";
            completed.text = CompletedCaption(currentGoal);
            int next = game.floor + 1;
            var nextMode = BlueRunPresentation.RouteGoal(game, next);
            for (int i = 0; i < Game.Floors; i++)
            {
                int absoluteFloor = game.Endless ? (Mathf.Max(0, next - 1) / Game.Floors) * Game.Floors + i + 1 : i + 1;
                var goal = BlueRunPresentation.RouteGoal(game, absoluteFloor);
                bool done = absoluteFloor <= game.floor, active = absoluteFloor == next;
                routeCards[i].color = done ? BlueUi.Green : active ? BlueUi.Blue : BlueUi.PanelColor;
                bool unknown = game.Endless && absoluteFloor > game.floor;
                routeLabels[i].text = unknown ? "?" : BlueRunPresentation.GoalName(goal);
                routeLabels[i].color = routeStates[i].color = done || active ? Color.white : BlueUi.Navy;
                routeStates[i].text = done ? "DONE" : active ? "NEXT" : "";
                string art = BlueRunPresentation.GoalArt(goal);
                routeIcons[i].sprite = art != null ? (done || active ? BlueUi.Art(art + "_white") ?? BlueUi.Art(art) : BlueUi.Art(art)) : MenuStyle.Objective(goal);
                routeIcons[i].gameObject.SetActive(!unknown);
                routeIcons[i].color = done || active ? Color.white : BlueUi.Navy;
                routeChecks[i].gameObject.SetActive(done);
            }
            nextFloor.text = $"Floor {next} · {BlueRunPresentation.FloorName(game.sector, next)}";
            nextGoal.text = BlueRunPresentation.GoalName(nextMode);
            bool longMode = nextGoal.text.Length > 8;
            nextModeBadge.rectTransform.sizeDelta = new Vector2(longMode ? 315 : 230, 59);
            var nextModeArt = BlueRunPresentation.GoalArt(nextMode);
            nextGoalIcon.sprite = nextModeArt != null ? BlueUi.Art(nextModeArt + "_white") ?? BlueUi.Art(nextModeArt) : MenuStyle.Objective(nextMode);
            nextGoalIcon.gameObject.SetActive(!game.Endless);
            nextObjective.text = BlueRunPresentation.ObjectiveText(game.sector, next, nextMode);
            // The approved egg illustration denotes Hunt only; other objectives use their actual route icon.
            nextArt.sprite = nextMode == Game.Goal.Hunt ? BlueUi.Art("nest_cryo") : BlueUi.Art(BlueRunPresentation.GoalArt(nextMode) ?? "icon_floor");
            nextArt.rectTransform.anchoredPosition = new Vector2(nextMode == Game.Goal.Hunt ? 1295 : 1440, -605);
            nextArt.rectTransform.sizeDelta = nextMode == Game.Goal.Hunt ? new Vector2(298, 160) : new Vector2(138, 138);
            if (game.Endless)
            {
                nextFloor.text = "Floor " + next;
                nextGoal.text = "Next floor";
                nextGoal.rectTransform.anchoredPosition = new Vector2(913, -634);
                nextGoal.rectTransform.sizeDelta = new Vector2(209, 51);
                nextObjective.text = "Objective revealed on entry";
                nextArt.gameObject.SetActive(false);
            }
            else
            {
                nextArt.gameObject.SetActive(true);
                nextGoal.rectTransform.anchoredPosition = new Vector2(993, -634);
                nextGoal.rectTransform.sizeDelta = new Vector2(longMode ? 212 : 127, 51);
            }
            ShowArenaPreview();
            var credit = game.offer.FirstOrDefault(card => card.id == "cache_credits");
            creditChoice.text = credit != null ? "+" + credit.max.ToString("N0") : "Credits";
        }

        void ShowArenaPreview()
        {
            bool cryo = game.sector?.id == "cryo-station";
            arenaPreview.gameObject.SetActive(cryo);
            Texture2D texture = cryo ? null : Resources.Load<Texture2D>("UI/Arena/" + game.sector?.id)
                ?? Resources.Load<Texture2D>("MenuStyle/ZoneArt/" + game.sector?.id)
                ?? Resources.Load<Texture2D>("UI/Sectors/" + game.sector?.art);
            authoredArenaPreview.gameObject.SetActive(texture != null);
            if (texture == null) return;
            authoredArenaPreview.texture = texture;
            float fit = Mathf.Min(760f / texture.width, 466f / texture.height);
            float width = texture.width * fit, height = texture.height * fit;
            authoredArenaPreview.rectTransform.anchoredPosition = new Vector2(69 + (760 - width) / 2, -283 - (466 - height) / 2);
            authoredArenaPreview.rectTransform.sizeDelta = new Vector2(width, height);
        }

        string CompletedCaption(Game.Goal goal)
        {
            return goal switch
            {
                Game.Goal.Purge => BlueRunPresentation.FloorDefinition(game.sector, game.floor)?.param == "3 waves" ? "Three waves survived" : "All waves survived",
                Game.Goal.Hunt => "Nests destroyed",
                Game.Goal.Hold => "Zone secured",
                Game.Goal.Survive => "Swarm survived",
                Game.Goal.Boss => "Boss defeated",
                _ => BlueRunPresentation.GoalName(goal) + " complete"
            };
        }

        public void SubmitContinue()
        {
            if (submitted || game.floor != shownFloor || game.state != Game.State.Choosing || game.choosing != Game.Choice.Cache) return;
            submitted = true; continueButton.interactable = false;
            var action = proceed; proceed = null; gameObject.SetActive(false);
            Haptics.Light(); Sfx.Click(); action?.Invoke();
        }
    }
}
