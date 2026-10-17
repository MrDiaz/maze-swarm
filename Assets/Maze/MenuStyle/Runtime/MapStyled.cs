using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TMPro;

namespace Maze
{
    // Deployment and briefing use the approved airy blue presentation. Selection, unlocks, the pending
    // plan, difficulty and launch remain owned by the existing MapSelect/MapBrief flow.
    public partial class MapSelect
    {
        RectTransform styleSelect, styleBrief, styleDiffModal;
        TMP_Text stylePage, styleSelectedName, styleSelectedGate, styleName, styleLocation, styleRouteCount;
        TMP_Text styleNextFloor, styleNextObjective, stylePreviewCaption, styleRouteNote, styleMods, styleTest, styleDiffLabel;
        UnityEngine.UI.Image styleZone;
        UnityEngine.UI.RawImage styleZoneFallback;
        UnityEngine.UI.Button styleBriefButton, styleDeploy, stylePrevious, styleNext, styleDifficulty;
        readonly (UnityEngine.UI.Button button, UnityEngine.UI.Image art, UnityEngine.UI.RawImage fallback,
            UnityEngine.UI.Image selected, UnityEngine.UI.Image check, TMP_Text name, TMP_Text state)[] styleZones =
            new (UnityEngine.UI.Button, UnityEngine.UI.Image, UnityEngine.UI.RawImage, UnityEngine.UI.Image,
                UnityEngine.UI.Image, TMP_Text, TMP_Text)[3];
        readonly (RectTransform root, UnityEngine.UI.Image backing, UnityEngine.UI.Image icon,
            TMP_Text number, TMP_Text text)[] styleGoals =
            new (RectTransform, UnityEngine.UI.Image, UnityEngine.UI.Image, TMP_Text, TMP_Text)[5];
        readonly (UnityEngine.UI.Button button, TMP_Text text, TMP_Text gate)[] styleTiers =
            new (UnityEngine.UI.Button, TMP_Text, TMP_Text)[3];
        readonly List<int> styleZoneOrder = new();
        readonly List<UnityEngine.UI.Image> styleGoalLinks = new();
        int styleZonePage, styleSelected = -1;
        string styleSignature;

        public UnityEngine.UI.Button StyledBriefButton => styleBriefButton;
        public UnityEngine.UI.Button StyledDeployButton => styleDeploy;
        public UnityEngine.UI.Button StyledDifficultyButton => styleDifficulty;
        public int StyledZonePage => styleZonePage;
        public int StyledZonePageCount => Mathf.Max(1, (styleZoneOrder.Count + 2) / 3);
        public int StyledNextFloorNumber => Sectors[Selected].map == 1 ? 1 : Prefs.TestMode ? Mathf.Clamp(Prefs.TestFloor, 1,
            Sectors[Selected].def != null && Sectors[Selected].def.endless ? 30 : Game.Floors) : 1;
        public string StyledNextFloorText => styleNextFloor != null ? styleNextFloor.text : "";
        public string StyledNextObjectiveText => styleNextObjective != null ? styleNextObjective.text : "";
        public void StyledBack() => Back();
        public void StyledNextZonePage() => ChangeStyledPage(1);
        public void StyledPreviousZonePage() => ChangeStyledPage(-1);
        public int StyledVisibleZoneIndex(int card) => card >= 0 && card < 3 && styleZonePage * 3 + card < styleZoneOrder.Count
            ? styleZoneOrder[styleZonePage * 3 + card] : -1;

        void BuildStyledScreens()
        {
            // Preserve the legacy hierarchy and references used by the existing flow, while hiding its presentation.
            foreach (Transform child in selectRt) child.gameObject.SetActive(false);
            foreach (Transform child in briefRt) child.gameObject.SetActive(false);
            BuildStyledZoneOrder();

            styleSelect = Holo.Fill(selectRt, "StyledZones");
            if (CohesiveZone.Enabled) CohesiveMenuArt.Background(styleSelect); else BlueUi.Background(styleSelect, "scene_deployment");
            var frame = BlueUi.Frame(styleSelect, "ZonesLayout");
            var back = BlueUi.Button(frame, "Back", "", 40, 20, 100, 82, Back, false, "icon_back");
            back.transform.Find("Surface").GetComponent<UnityEngine.UI.Image>().sprite = BlueUi.Art("button_navy_blank");
            BlueUi.Text(frame, "Heading", "Deployment", 168, 17, 870, 88, 68, CohesiveZone.Enabled ? Color.white : BlueUi.Navy);
            BlueUi.Wallet(frame);
            BlueUi.Panel(frame, "ZoneSelectionPanel", 37, 116, 1601, 788);
            BlueUi.Text(frame, "ChooseZone", "Choose a zone", 74, 132, 1170, 71, 58);

            for (int i = 0; i < styleZones.Length; i++)
            {
                int card = i;
                float x = 62 + i * 523;
                var root = BlueUi.Rect(frame, "ZoneCard" + i, x, 203, 510, 570);
                var surface = StyledPlate(root, "Surface", "zone_card_blank", 0, 0, 510, 570);
                surface.raycastTarget = true;
                var button = root.gameObject.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = surface;
                button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                var colors = button.colors;
                colors.highlightedColor = Color.white;
                colors.pressedColor = new Color(.87f, .95f, 1f);
                colors.disabledColor = new Color(.75f, .82f, .91f);
                colors.fadeDuration = 0;
                button.colors = colors;
                button.onClick.AddListener(() =>
                {
                    int index = StyledVisibleZoneIndex(card);
                    if (index < 0) return;
                    PickSector(index);
                    RefreshStyledScreens();
                });
                var name = BlueUi.Text(root, "Name", "", 32, 16, 430, 63, 43);
                var chip = StyledPlate(root, "StateChip", "chip_blue", 34, 76, 247, 37, true);
                chip.color = Color.white;
                var state = BlueUi.Text(root, "State", "", 45, 76, 225, 37, 25);
                state.alignment = TextAlignmentOptions.Center;
                var artClip = BlueUi.Rect(root, "ArtClip", 10, 109, 490, 448);
                artClip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
                var art = BlueUi.Image(artClip, "ApprovedZoneArt", (Sprite)null, 0, 0, 490, 448);
                art.preserveAspect = true;
                var fallback = Holo.Raw(Holo.Fill(artClip, "AuthoredZoneArt"), null, Color.white);
                fallback.raycastTarget = false;
                fallback.gameObject.AddComponent<CoverUv>();
                var selected = BlueUi.Image(root, "Selection", BlueUi.Art("selected_zone_outline") ?? BlueUi.Art("selected_outline"), 0, 0, 510, 570, true);
                selected.raycastTarget = false;
                var check = BlueUi.Image(root, "SelectedCheck", "check_blue", 418, 20, 67, 67);
                check.preserveAspect = true;
                styleZones[i] = (button, art, fallback, selected, check, name, state);
            }
            var pin = BlueUi.Image(frame, "SelectedZonePin", "icon_pin", 74, 801, 65, 74);
            pin.preserveAspect = true;
            styleSelectedName = BlueUi.Text(frame, "SelectedZone", "", 155, 802, 664, 56, 36);
            styleSelectedGate = BlueUi.Text(frame, "SelectedGate", "", 156, 853, 684, 30, 23, BlueUi.Muted, true);
            stylePrevious = StyledPageArrow(frame, "Previous", 860, false, () => ChangeStyledPage(-1));
            styleNext = StyledPageArrow(frame, "Next", 1150, true, () => ChangeStyledPage(1));
            stylePage = BlueUi.Text(frame, "Page", "", 946, 811, 188, 56, 29, BlueUi.Muted, true);
            stylePage.alignment = TextAlignmentOptions.Center;
            styleBriefButton = BlueUi.Button(frame, "Briefing", "Deploy", 1298, 792, 316, 94, OpenBriefing, true, null, 50);

            styleBrief = Holo.Fill(briefRt, "StyledBriefing");
            if (CohesiveZone.Enabled) CohesiveMenuArt.Background(styleBrief); else BlueUi.Background(styleBrief, "scene_deployment");
            frame = BlueUi.Frame(styleBrief, "BriefingLayout");
            back = BlueUi.Button(frame, "Back", "", 40, 20, 100, 82, Back, false, "icon_back");
            back.transform.Find("Surface").GetComponent<UnityEngine.UI.Image>().sprite = BlueUi.Art("button_navy_blank");
            BlueUi.Text(frame, "Heading", "Mission Briefing", 168, 17, 900, 88, 68, CohesiveZone.Enabled ? Color.white : BlueUi.Navy);
            BlueUi.Wallet(frame);
            var arenaPanel = BlueUi.Panel(frame, "ArenaPanel", 39, 118, 875, 786).rectTransform;
            var arenaClip = BlueUi.Rect(arenaPanel, "ArenaClip", 10, 106, 855, 650);
            arenaClip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            styleZone = BlueUi.Image(arenaClip, "ApprovedArenaArt", (Sprite)null, 0, 0, 855, 650);
            styleZone.preserveAspect = true;
            styleZoneFallback = Holo.Raw(Holo.Fill(arenaClip, "AuthoredArenaArt"), null, Color.white);
            styleZoneFallback.raycastTarget = false;
            styleZoneFallback.gameObject.AddComponent<CoverUv>();
            styleName = BlueUi.Text(arenaPanel, "Name", "", 43, 12, 800, 87, 70);
            StyledPlate(arenaPanel, "LocationChip", "chip_blue", 45, 99, 296, 47, true);
            styleLocation = BlueUi.Text(arenaPanel, "Location", "", 55, 99, 276, 47, 34);
            styleLocation.alignment = TextAlignmentOptions.Center;
            styleDifficulty = BlueUi.Button(arenaPanel, "Difficulty", "Normal", 561, 99, 270, 47,
                () => styleDiffModal.gameObject.SetActive(true), false, null, 28);
            styleDiffLabel = styleDifficulty.GetComponentInChildren<TMP_Text>();
            stylePreviewCaption = BlueUi.Text(arenaPanel, "PreviewCaption", "Arena preview", 252, 726, 450, 48, 28, BlueUi.Muted, true);
            stylePreviewCaption.alignment = TextAlignmentOptions.Center;

            var routePanel = BlueUi.Panel(frame, "RunRoutePanel", 925, 118, 710, 786).rectTransform;
            BlueUi.Text(routePanel, "Heading", "Run route", 38, 18, 624, 82, 66);
            styleRouteCount = BlueUi.Text(routePanel, "FloorCount", "5 floors", 40, 91, 621, 44, 32, BlueUi.Muted, true);
            for (int i = 0; i < styleGoals.Length; i++)
            {
                float x = 22 + i * 136;
                var card = BlueUi.Rect(routePanel, "Floor" + (i + 1), x, 155, 128, 190);
                var backing = StyledPlate(card, "Surface", "route_inactive", 0, 0, 128, 190, true);
                // The source circle backing stays independent of the actual, live route index.
                BlueUi.Image(card, "NumberBadge", "route_number_badge", 42, 10, 44, 43);
                var number = BlueUi.Text(card, "Number", (i + 1).ToString(), 34, 9, 60, 45, 31);
                number.alignment = TextAlignmentOptions.Center;
                var icon = BlueUi.Image(card, "Icon", (Sprite)null, 27, 61, 74, 74);
                icon.preserveAspect = true;
                var label = BlueUi.Text(card, "Label", "", 4, 136, 120, 46, 31);
                label.alignment = TextAlignmentOptions.Center;
                styleGoals[i] = (card, backing, icon, number, label);
                if (i < styleGoals.Length - 1)
                    styleGoalLinks.Add(BlueUi.Image(routePanel, "Link" + i, "route_link", x + 124, 244, 24, 17, true));
            }
            styleRouteNote = BlueUi.Text(routePanel, "EndlessRouteNote", "Shuffled zone layouts\nNew objectives each floor", 182, 179, 480, 143, 34, BlueUi.Muted, true);
            styleRouteNote.textWrappingMode = TextWrappingModes.Normal;
            var next = BlueUi.Panel(routePanel, "NextFloorPanel", 22, 373, 664, 250).rectTransform;
            BlueUi.Text(next, "Eyebrow", "NEXT FLOOR", 32, 25, 602, 41, 30, BlueUi.Blue);
            styleNextFloor = BlueUi.Text(next, "FloorTitle", "", 32, 75, 602, 77, 57);
            styleNextObjective = BlueUi.Text(next, "Objective", "", 32, 156, 602, 77, 48);
            styleNextObjective.textWrappingMode = TextWrappingModes.Normal;
            styleDeploy = BlueUi.Button(routePanel, "Deploy", "Deploy", 22, 647, 664, 116, LaunchDrop, true, null, 68);
            styleTest = BlueUi.Text(frame, "TestMode", "", 62, 906, 1540, 28, 22, BlueUi.Blue, true);

            BuildStyledDifficulty(frame);
            RefreshStyledScreens();
        }

        void BuildStyledZoneOrder()
        {
            styleZoneOrder.Clear();
            // The approved three themed previews lead the catalogue, without changing the selected sector index,
            // sector identity or progression order. Every other actual zone remains reachable on later pages.
            foreach (string id in new[] { "cryo-station", "kessler-refinery", "verdant-colony" })
                for (int i = 0; i < Sectors.Length; i++) if (Sectors[i].def?.id == id) styleZoneOrder.Add(i);
            for (int i = 0; i < Sectors.Length; i++) if (!styleZoneOrder.Contains(i)) styleZoneOrder.Add(i);
        }

        void BuildStyledDifficulty(Transform frame)
        {
            styleDiffModal = Holo.Fill(frame, "DifficultyOptions");
            var blocker = Holo.Img(Holo.Fill(styleDiffModal, "InputBlocker"), Holo.WhiteSprite, new Color(.1f, .24f, .4f, .22f));
            blocker.raycastTarget = true;
            var modal = BlueUi.Panel(styleDiffModal, "Panel", 366, 160, 940, 680).rectTransform;
            BlueUi.Text(modal, "Heading", "Difficulty", 36, 20, 690, 76, 58);
            BlueUi.Button(modal, "Close", "", 818, 24, 82, 72,
                () => styleDiffModal.gameObject.SetActive(false), false, "icon_close");
            for (int i = 0; i < styleTiers.Length; i++)
            {
                int difficulty = i;
                var button = BlueUi.Button(modal, "Tier" + i, "", 34, 123 + i * 133, 872, 108, () =>
                {
                    var def = Sectors[Selected].def;
                    if (def != null && Meta.TierGateFor(def, difficulty) == null)
                    {
                        PickTier(difficulty);
                        RefreshStyledScreens();
                        styleDiffModal.gameObject.SetActive(false);
                    }
                    else { Sfx.Deny(); Haptics.Warn(); menu.ShowToast("DIFFICULTY LOCKED"); }
                });
                var title = BlueUi.Text(button.transform, "TierName", "", 24, 12, 820, 55, 39, Color.white);
                var gate = BlueUi.Text(button.transform, "Requirement", "", 24, 68, 820, 35, 23, Color.white, true);
                title.alignment = gate.alignment = TextAlignmentOptions.Center;
                styleTiers[i] = (button, title, gate);
            }
            styleMods = BlueUi.Text(modal, "Modifiers", "", 40, 527, 860, 125, 23, BlueUi.Muted, true);
            styleMods.textWrappingMode = TextWrappingModes.Normal;
            styleDiffModal.gameObject.SetActive(false);
        }

        static UnityEngine.UI.Image StyledPlate(Transform parent, string name, string art,
            float x, float y, float width, float height, bool soft = false)
        {
            var source = BlueUi.Art(art);
            return source != null ? BlueUi.Image(parent, name, source, x, y, width, height, true)
                : BlueUi.Panel(parent, name, x, y, width, height, soft);
        }

        static UnityEngine.UI.Button StyledPageArrow(Transform parent, string name, float x, bool forward, Action action)
        {
            var button = BlueUi.Button(parent, name, "", x, 804, 76, 70, action);
            var arrow = BlueUi.Image(button.transform, "Arrow", "icon_back", 47, 47, 30, 30);
            arrow.preserveAspect = true;
            arrow.rectTransform.pivot = new Vector2(.5f, .5f);
            arrow.rectTransform.anchoredPosition = new Vector2(62, -62);
            if (forward) arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            return button;
        }

        void ChangeStyledPage(int direction)
        {
            styleZonePage = Mathf.Clamp(styleZonePage + direction, 0, StyledZonePageCount - 1);
            RefreshStyledScreens();
            Sfx.Click(); Haptics.Light();
        }

        void TickStyledScreens()
        {
            if (styleSelect == null) return;
            if (planetCam != null && (mode == Mode.Select || mode == Mode.Brief)) planetCam.enabled = false;
            var signature = Selected + "|" + Meta.Difficulty + "|" + Prefs.Weapon + "|" + Prefs.TestMode + "|" +
                Prefs.TestFloor + "|" + waitingLoadout + "|" + mode + "|" + Progress.D.stars.Count + "|" + Progress.D.cleared.Count;
            if (signature == styleSignature) return;
            styleSignature = signature;
            RefreshStyledScreens();
        }

        void RefreshStyledScreens()
        {
            if (styleSelect == null) return;
            if (styleSelected != Selected)
            {
                styleSelected = Selected;
                int at = styleZoneOrder.IndexOf(Selected);
                styleZonePage = Mathf.Max(0, at) / 3;
            }
            for (int i = 0; i < styleZones.Length; i++)
            {
                int index = StyledVisibleZoneIndex(i);
                var card = styleZones[i];
                card.button.gameObject.SetActive(index >= 0);
                if (index < 0) continue;
                var sector = Sectors[index];
                card.name.text = CohesiveMenuArt.SectorName(sector.def, StyledTitle(sector.name));
                card.name.fontSize = card.name.text.Length > 20 ? 33 : card.name.text.Length > 15 ? 38 : 43;
                card.selected.gameObject.SetActive(index == Selected);
                card.check.gameObject.SetActive(index == Selected);
                card.state.text = sector.locked ? "Locked" : sector.map == 1 ? "Escape test" :
                    sector.def != null && sector.def.endless ? "Endless floors" : StyledFloorName(sector.def, 1);
                card.state.fontSize = card.state.text.Length > 19 ? 20 : 25;
                SetStyledArt(card.art, card.fallback, StyledZoneArt(sector), MenuStyle.Zone(sector));
            }
            var current = Sectors[Selected];
            stylePage.text = (styleZonePage + 1) + " / " + StyledZonePageCount;
            stylePrevious.interactable = styleZonePage > 0;
            styleNext.interactable = styleZonePage < StyledZonePageCount - 1;
            styleSelectedName.text = CohesiveMenuArt.SectorName(current.def, StyledTitle(current.name)) + (current.locked ? " locked" : " selected");
            styleSelectedName.fontSize = styleSelectedName.text.Length > 30 ? 30 : 36;
            styleSelectedGate.text = current.locked ? CohesiveMenuArt.Text(StyledTitle(current.unlock)) : current.def != null && !current.def.endless
                ? "Recommended power " + current.Rec.ToString("N0") : "";
            styleBriefButton.interactable = !current.locked;

            styleName.text = CohesiveMenuArt.SectorName(current.def, StyledTitle(current.name));
            styleName.fontSize = styleName.text.Length > 22 ? 46 : styleName.text.Length > 16 ? 57 : 70;
            int floor = StyledNextFloorNumber;
            string actualFloorName = StyledFloorName(current.def, floor);
            styleLocation.text = actualFloorName.Length > 24 ? "Floor " + floor : actualFloorName;
            styleLocation.fontSize = styleLocation.text.Length > 17 ? 28 : 34;
            var arena = CohesiveZone.For(current.def) ? CohesiveMenuArt.CargoSprite : current.def?.id == "cryo-station" ? BlueUi.Art("arena_cryo") : null;
            var arenaFallback = current.map == 1 ? Resources.Load<Texture2D>("UI/arena_escape") : current.def != null
                ? Resources.Load<Texture2D>("UI/Arena/" + current.def.id) : null;
            SetStyledArt(styleZone, styleZoneFallback, arena, arenaFallback != null ? arenaFallback : MenuStyle.Zone(current));
            if (arena == null) FitStyledArenaFallback(styleZoneFallback);
            stylePreviewCaption.text = CohesiveZone.For(current.def) ? "Cargo Depot preview" : arena != null ? "Arena preview" : arenaFallback != null ? "Floor 1 layout" : "Zone preview";
            bool endless = current.def != null && current.def.endless;
            styleRouteCount.text = current.map == 1 ? "1 floor" : endless ? "Endless floors" :
                (Game.PendingPlan != null ? Game.PendingPlan.Length : Game.Floors) + " floors";
            // Swarm Gate determines later floors only on entry. Its opening is authored; do not promise
            // that the pending seed's five placeholders are the actual future route.
            var displayPlan = current.map == 1 ? new[] { Game.Goal.Escape } : endless
                ? floor > 1 ? new Game.Goal[0] : new[] { current.def.floors != null && current.def.floors.Length > 0 ? current.def.floors[0].goal : Game.Goal.Purge }
                : Game.PendingPlan;
            styleRouteNote.gameObject.SetActive(endless);
            int nextSlot = endless ? (floor - 1) % Game.Floors : Mathf.Clamp(floor - 1, 0, Game.Floors - 1);
            for (int i = 0; i < styleGoals.Length; i++)
            {
                var card = styleGoals[i];
                bool shown = displayPlan != null && i < displayPlan.Length;
                card.root.gameObject.SetActive(shown);
                if (!shown) continue;
                Game.Goal goal = displayPlan[i];
                bool active = i == nextSlot;
                card.backing.sprite = BlueUi.Art(active ? "route_active" : "route_inactive") ?? BlueUi.Art(active ? "tab_active" : "panel_soft");
                card.backing.color = Color.white;
                card.icon.sprite = BlueUi.Art(BlueRunPresentation.GoalArt(goal) ?? "objective_" + Game.GoalNames[(int)goal].ToLowerInvariant()) ?? MenuStyle.Objective(goal);
                card.icon.color = active ? Color.white : BlueUi.Navy;
                card.number.color = BlueUi.Navy;
                card.text.text = StyledTitle(Game.GoalNames[(int)goal]);
                card.text.fontSize = card.text.text.Length > 8 ? 22 : 31;
                card.text.color = active ? Color.white : BlueUi.Navy;
            }
            for (int i = 0; i < styleGoalLinks.Count; i++)
                styleGoalLinks[i].gameObject.SetActive(displayPlan != null && i + 1 < displayPlan.Length);
            Game.Goal nextGoal = displayPlan != null && nextSlot < displayPlan.Length ? displayPlan[nextSlot] : Game.Goal.Escape;
            string floorName = StyledFloorName(current.def, floor);
            styleNextFloor.text = current.map == 1 ? "Escape test" : "Floor " + floor + " · " + floorName;
            bool longFloor = styleNextFloor.text.Length > 28;
            if (longFloor) styleNextFloor.text = "Floor " + floor + "\n" + floorName;
            styleNextFloor.rectTransform.anchoredPosition = new Vector2(32, longFloor ? -62 : -75);
            styleNextFloor.rectTransform.sizeDelta = new Vector2(602, longFloor ? 111 : 77);
            styleNextFloor.fontSize = longFloor ? 42 : styleNextFloor.text.Length > 22 ? 47 : 57;
            styleNextObjective.text = current.map == 1 ? "Reach the exit" : BlueRunPresentation.ObjectiveText(current.def, floor, nextGoal);
            if (endless && floor > 1)
            {
                styleLocation.text = "Endless run";
                styleNextFloor.text = "Floor " + floor;
                styleNextObjective.text = "Objective revealed on entry";
            }
            styleNextObjective.fontSize = styleNextObjective.text.Length > 35 ? 34 : styleNextObjective.text.Length > 26 ? 40 : 48;
            styleNextObjective.rectTransform.anchoredPosition = new Vector2(32, longFloor ? -179 : -156);
            styleNextObjective.rectTransform.sizeDelta = new Vector2(602, longFloor ? 60 : 77);
            if (longFloor) styleNextObjective.fontSize = Mathf.Min(styleNextObjective.fontSize, 32);
            styleDifficulty.gameObject.SetActive(current.def != null && !endless);
            styleDiffLabel.text = StyledDifficultyName(Meta.Difficulty);
            for (int i = 0; i < styleTiers.Length; i++)
            {
                var row = styleTiers[i];
                var gate = current.def != null ? Meta.TierGateFor(current.def, i) : null;
                row.button.interactable = current.def != null && !endless && gate == null;
                row.text.text = StyledDifficultyName(i) + (Meta.Difficulty == i ? " - Active" : "");
                row.gate.text = gate != null ? StyledTitle(gate) : "Recommended power " +
                    (current.def != null ? Meta.RecommendedAt(current.def, i).ToString("N0") : "0");
            }
            var modifiers = BriefMods(current.def, Meta.Difficulty, current.def != null && current.def.id == hotId);
            var descriptions = new List<string>();
            for (int i = 0; i < modifiers.names.Count; i++) descriptions.Add(StyledTitle(modifiers.names[i]) +
                (i < modifiers.descs.Count && !string.IsNullOrEmpty(modifiers.descs[i]) ? ": " + modifiers.descs[i] : ""));
            styleMods.text = string.Join("\n", descriptions);
            styleTest.text = Prefs.TestMode ? "TEST MODE · START FLOOR " + floor + " · SAVED PROGRESS IS PROTECTED" : "";
            styleDeploy.interactable = !current.locked;
        }

        static string StyledDifficultyName(int tier) => tier == 0 ? "Normal" : tier == 1 ? "Hard" : "Nightmare";
        static string StyledTitle(string value) => string.IsNullOrEmpty(value) ? "" :
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
        static string StyledFloorName(SectorDef sector, int floor) => sector != null ? BlueRunPresentation.FloorName(sector, floor) : "Escape test";
        static Sprite StyledZoneArt(Sector sector) => CohesiveZone.For(sector.def) ? CohesiveMenuArt.CargoSprite : BlueUi.Art(sector.def?.id == "cryo-station" ? "zone_cryo" :
            sector.def?.id == "kessler-refinery" ? "zone_lava" : sector.def?.id == "verdant-colony" ? "zone_overgrown" : "__none");
        static void SetStyledArt(UnityEngine.UI.Image approved, UnityEngine.UI.RawImage fallback, Sprite art, Texture texture)
        {
            approved.gameObject.SetActive(art != null);
            fallback.gameObject.SetActive(art == null);
            if (art != null) { approved.sprite = art; approved.color = Color.white; }
            else { fallback.texture = texture; if (texture != null) fallback.GetComponent<CoverUv>().aspect = (float)texture.width / texture.height; }
        }
        static void FitStyledArenaFallback(UnityEngine.UI.RawImage image)
        {
            if (image.texture == null) return;
            float fit = Mathf.Min(815f / image.texture.width, 570f / image.texture.height);
            float width = image.texture.width * fit, height = image.texture.height * fit;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2((855 - width) * .5f, -24 - (570 - height) * .5f);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
