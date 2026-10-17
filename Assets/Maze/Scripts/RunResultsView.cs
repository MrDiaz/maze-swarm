using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Maze
{
    // RunEnd owns every reward grant. This view only presents the immutable awarded Loot
    // and invokes the existing navigation and optional meta-upgrade actions.
    public sealed class RunResultsView : MonoBehaviour
    {
        Game game;
        RectTransform board, deathLayout, victoryLayout, details;
        TMP_Text deathZone, victoryZone, victoryFloor, victoryCleared, deathTime, deathFloor, deathKills;
        TMP_Text victoryTime, victoryFloors, victoryKills;
        TMP_Text levelValue, killsValue, scoreValue, xp, upgradeName, upgradeDetail, upgradeLabel, grade, verdict, testLabel, itemName, itemStat, pageLabel;
        readonly TMP_Text[] deathRewards = new TMP_Text[3], victoryRewards = new TMP_Text[3];
        UnityEngine.UI.Image itemIcon;
        UnityEngine.UI.Button upgradeButton, retryButton, menuButton, deathRetry, deathMenu, victoryMenu, detailsRetry, detailsMenu, prevButton, nextButton;
        readonly List<Reward> rewards = new();
        List<Meta.Pick> picks = new();
        bool won, leaving, buying;
        float shownAt, lastUpgrade = -100;
        int rewardPage;
        struct Reward { public string name, stat; public Sprite icon; }

        public bool Visible => gameObject.activeInHierarchy;
        public string UpgradeMode { get; private set; }
        public int RenderedRewardRows => rewards.Count;
        public Progress.Loot DisplayLoot { get; private set; }
        public UnityEngine.UI.Button RetryButton => retryButton;
        public UnityEngine.UI.Button MenuButton => menuButton;
        public UnityEngine.UI.Button UpgradeButton => upgradeButton;
        public int RewardPage => rewardPage;

        public void Init(Game g)
        {
            game = g;
            BlueUi.Background(transform, "scene_cryo");
            board = BlueUi.Frame(transform, "ResultsLayout");
            deathLayout = BlueUi.Rect(board, "RunEnded", 0, 0, BlueUi.Width, BlueUi.Height);
            victoryLayout = BlueUi.Rect(board, "Victory", 0, 0, BlueUi.Width, BlueUi.Height);
            BuildDeath(); BuildVictory(); BuildDetails();
            details.gameObject.SetActive(false);
        }

        static TMP_Text Center(TMP_Text t)
        {
            t.alignment = TextAlignmentOptions.Center;
            return t;
        }

        static UnityEngine.UI.Image Icon(Transform parent, string name, string art, float x, float y, float w, float h)
        {
            var image = BlueUi.Image(parent, name, art, x, y, w, h);
            image.preserveAspect = true;
            return image;
        }

        void BuildDeath()
        {
            Icon(deathLayout, "HeavySeat", "death_bench", 0, 637, 773, 122);
            BlueUi.Avatar(deathLayout, "FallenHeavy", 97, 199, 582, 589, "dead");
            BlueUi.Panel(deathLayout, "ResultPanel", 815, 59, 806, 823, false, true);
            Center(BlueUi.Text(deathLayout, "Title", "Run ended", 848, 87, 740, 107, 96));
            deathZone = Center(BlueUi.Text(deathLayout, "Zone", "", 899, 187, 626, 54, 38, BlueUi.Muted));
            deathTime = DeathStat("Time", "icon_time", 848);
            deathFloor = DeathStat("Floor", "icon_floor", 1095);
            deathKills = DeathStat("Defeated", "icon_kills", 1350);
            Center(BlueUi.Text(deathLayout, "RewardsHeading", "Rewards", 853, 470, 731, 60, 52));
            for (int i = 0; i < 3; i++)
            {
                float x = 848 + i * 251;
                BlueUi.Panel(deathLayout, "Currency" + i, x, 537, 226, 189, true);
                if (i == 0) Icon(deathLayout, "CurrencyIcon" + i, CurrencyIcon(i), 906, 550, 104, 111);
                else if (i == 1) Icon(deathLayout, "CurrencyIcon" + i, CurrencyIcon(i), 1154, 555, 108, 110);
                else Icon(deathLayout, "CurrencyIcon" + i, CurrencyIcon(i), 1407, 558, 119, 106);
                deathRewards[i] = Center(BlueUi.Text(deathLayout, "CurrencyValue" + i, "", x + 7, 661, 212, 56, 50));
            }
            deathMenu = BlueUi.Button(deathLayout, "Menu", "Hangar", 838, 748, 354, 110, SubmitMenu, false, null, 54);
            deathRetry = BlueUi.Button(deathLayout, "Retry", "Retry", 1211, 748, 385, 110, SubmitRetry, true, null, 56);
            BlueUi.Button(deathLayout, "RunDetails", "Run details", 224, 836, 344, 70, OpenDetails, false, null, 30);
        }

        TMP_Text DeathStat(string label, string icon, float x)
        {
            BlueUi.Panel(deathLayout, label + "Card", x, 250, 236, 210, true);
            if (label == "Time") Icon(deathLayout, label + "Icon", icon, 929, 268, 69, 78);
            else if (label == "Defeated") Icon(deathLayout, label + "Icon", icon, 1429, 269, 86, 77);
            else Icon(deathLayout, label + "Icon", icon, x + 84, 269, 69, 78);
            Center(BlueUi.Text(deathLayout, label + "Label", label, x + 8, 350, 220, 43, 32));
            return Center(BlueUi.Text(deathLayout, label + "Value", "", x + 6, 389, 224, 64, 58));
        }

        void BuildVictory()
        {
            Icon(victoryLayout, "VictoryPlatform", "victory_platform", 49, 777, 751, 157);
            BlueUi.Avatar(victoryLayout, "VictoriousHeavy", 162, 102, 568, 730, "victory");
            Icon(victoryLayout, "VictoryTrophy", "icon_trophy", 865, 25, 167, 161);
            BlueUi.Text(victoryLayout, "Title", "Victory!", 1033, 22, 584, 158, 127);
            BlueUi.Panel(victoryLayout, "ZoneBacking", 954, 178, 518, 59, true);
            victoryZone = Center(BlueUi.Text(victoryLayout, "Zone", "", 956, 181, 514, 54, 41));
            BlueUi.Panel(victoryLayout, "ResultPanel", 815, 250, 829, 655, false, true);
            var badge = BlueUi.Panel(victoryLayout, "CompletedBadge", 854, 270, 377, 58, true);
            badge.color = BlueUi.Blue;
            Icon(victoryLayout, "BossCheck", "check_blue", 871, 278, 52, 42);
            BlueUi.Text(victoryLayout, "CompletedLabel", "Boss defeated", 945, 275, 277, 49, 33, Color.white);
            // Fredoka's full line is 1.21× point size. A 76/68pt auto range in
            // an82px box truncates even the minimum line (82.28px) before drawing.
            victoryFloor = BlueUi.Text(victoryLayout, "CompletedFloor", "", 851, 320, 754, 96, 76);
            victoryFloor.fontSizeMin = 40;
            victoryFloor.textWrappingMode = TextWrappingModes.NoWrap;
            victoryCleared = BlueUi.Text(victoryLayout, "ClearedCaption", "", 854, 406, 742, 45, 39, BlueUi.Muted);
            victoryFloors = VictoryStat("Floors", "icon_floor", 850, 247);
            victoryTime = VictoryStat("Time", "victory_time", 1105, 249);
            victoryKills = VictoryStat("Enemies", "victory_enemies", 1366, 232);
            BlueUi.Text(victoryLayout, "RewardsHeading", "Run rewards", 852, 602, 720, 48, 38);
            for (int i = 0; i < 3; i++)
            {
                float x = 850 + i * 258;
                BlueUi.Panel(victoryLayout, "Currency" + i, x, 651, 244, 104, true);
                Icon(victoryLayout, "CurrencyIcon" + i, CurrencyIcon(i), x + 14, 654, 101, 96);
                victoryRewards[i] = BlueUi.Text(victoryLayout, "CurrencyValue" + i, "", x + 113, 672, 122, 67, 45);
            }
            victoryMenu = BlueUi.Button(victoryLayout, "Menu", "Return to Hangar", 841, 776, 776, 105, SubmitMenu, true, null, 56);
            BlueUi.Button(victoryLayout, "RunDetails", "Run details", 426, 50, 344, 70, OpenDetails, false, null, 30);
        }

        TMP_Text VictoryStat(string label, string icon, float x, float width)
        {
            BlueUi.Panel(victoryLayout, label + "Card", x, 465, width, 123, true);
            if (label == "Time") Icon(victoryLayout, label + "Icon", icon, 1126, 483, 72, 84);
            else if (label == "Enemies") Icon(victoryLayout, label + "Icon", icon, 1391, 487, 82, 81);
            else Icon(victoryLayout, label + "Icon", icon, 875, 493, 59, 67);
            BlueUi.Text(victoryLayout, label + "Label", label, x + 113, 482, width - 121, 32, 26);
            return BlueUi.Text(victoryLayout, label + "Value", "", x + 109, 517, width - 116, 58, 49);
        }

        static string CurrencyIcon(int index) => index == 0 ? "currency_coin" : index == 1 ? "currency_parts" : "currency_alloy";

        void BuildDetails()
        {
            details = BlueUi.Rect(board, "RunDetailsOverlay", 0, 0, BlueUi.Width, BlueUi.Height);
            var dim = BlueUi.Scrim(details, "Scrim", new Color(.02f, .08f, .24f, .64f));
            dim.raycastTarget = true;
            BlueUi.Panel(details, "DetailsPanel", 256, 65, 1160, 815);
            BlueUi.Text(details, "Heading", "Run details", 300, 92, 900, 74, 62);
            BlueUi.Button(details, "Close", "", 1287, 84, 83, 83, () => details.gameObject.SetActive(false), false, "icon_close");
            levelValue = DetailStat("LEVEL", "Level", 300);
            killsValue = DetailStat("KILLS", "Defeated", 572);
            scoreValue = DetailStat("SCORE", "Score", 844);
            grade = Center(BlueUi.Text(details, "Grade", "", 1130, 168, 218, 126, 92, BlueUi.Blue));
            xp = BlueUi.Text(details, "CommanderXP", "", 302, 297, 1028, 44, 28, BlueUi.Muted);
            BlueUi.Panel(details, "Loot", 300, 359, 1070, 154, true);
            itemIcon = BlueUi.Image(details, "ItemIcon", (Sprite)null, 319, 381, 112, 108);
            itemIcon.preserveAspect = true;
            itemName = BlueUi.Text(details, "ItemName", "", 454, 371, 635, 57, 37);
            itemStat = BlueUi.Text(details, "ItemStats", "", 454, 430, 635, 61, 26, BlueUi.Muted, true);
            itemStat.textWrappingMode = TextWrappingModes.Normal;
            prevButton = BlueUi.Button(details, "PreviousReward", "‹", 1110, 367, 104, 66, () => SetRewardPage(rewardPage - 1), false, null, 44);
            nextButton = BlueUi.Button(details, "NextReward", "›", 1254, 367, 104, 66, () => SetRewardPage(rewardPage + 1), false, null, 44);
            pageLabel = Center(BlueUi.Text(details, "RewardPage", "", 1110, 450, 248, 41, 24, BlueUi.Muted));
            upgradeName = BlueUi.Text(details, "UpgradeName", "", 302, 545, 709, 49, 34);
            upgradeDetail = BlueUi.Text(details, "UpgradeDetail", "", 302, 598, 709, 43, 25, BlueUi.Muted);
            upgradeButton = BlueUi.Button(details, "Upgrade", "Upgrade", 1070, 548, 299, 101, SubmitUpgrade, true, null, 35);
            upgradeLabel = upgradeButton.transform.Find("Label")?.GetComponent<TMP_Text>();
            verdict = BlueUi.Text(details, "Verdict", "", 302, 667, 1066, 47, 27, BlueUi.Muted);
            detailsMenu = BlueUi.Button(details, "Return", "Hangar", 300, 738, 516, 99, SubmitMenu, false, null, 44);
            detailsRetry = BlueUi.Button(details, "Retry", "Retry", 844, 738, 525, 99, SubmitRetry, true, null, 44);
            testLabel = Center(BlueUi.Text(details, "TestRun", "", 300, 841, 1069, 30, 21, BlueUi.Muted));
        }

        TMP_Text DetailStat(string name, string label, float x)
        {
            BlueUi.Panel(details, name + "Card", x, 179, 250, 112, true);
            BlueUi.Text(details, name + "Label", label, x + 18, 188, 214, 35, 27, BlueUi.Muted);
            return BlueUi.Text(details, name + "Value", "", x + 18, 222, 214, 60, 50);
        }

        public void Show(bool success, int reached, Progress.Loot loot)
        {
            won = success; DisplayLoot = loot; leaving = buying = false; shownAt = Time.unscaledTime;
            deathLayout.gameObject.SetActive(!won); victoryLayout.gameObject.SetActive(won); details.gameObject.SetActive(false);
            retryButton = won ? detailsRetry : deathRetry; menuButton = won ? victoryMenu : deathMenu;
            string zone = CohesiveMenuArt.SectorName(game.sector, "Extraction run");
            deathZone.text = zone; victoryZone.text = zone + " complete";
            deathTime.text = Clock(game.RunTime); deathFloor.text = reached.ToString("N0"); deathKills.text = game.kills.ToString("N0");
            victoryFloor.text = BlueRunPresentation.FloorName(game.sector, reached);
            victoryCleared.text = $"All {reached} floors cleared";
            victoryFloors.text = $"{reached} / {Game.Floors}"; victoryTime.text = Clock(game.RunTime); victoryKills.text = game.kills.ToString("N0");
            int[] values = { loot.credits, loot.parts, loot.alloy };
            for (int i = 0; i < values.Length; i++) deathRewards[i].text = victoryRewards[i].text = "+" + values[i].ToString("N0");
            levelValue.text = game.level.ToString("N0"); killsValue.text = game.kills.ToString("N0"); scoreValue.text = game.RunScore.ToString("N0");
            xp.text = $"+{game.XpGained:N0} XP · Commander rank {Meta.Rank:N0}";
            grade.text = verdict.text = "";
            testLabel.text = game.LastRunTest ? "Test run · Progress not saved" : "";
            rewards.Clear();
            foreach (var gear in loot.gear ?? Enumerable.Empty<GearItem>())
                rewards.Add(new Reward { name = BlueRunPresentation.TitleCase(Progress.Rarities[gear.rarity] + " " + Progress.Slots[gear.slot]), stat = Progress.StatText(gear), icon = Holo.Art("gear_" + Progress.Slots[gear.slot].ToLowerInvariant()) });
            if (loot.blueprint != null) AddReward("Blueprint", loot.blueprint, "rifle");
            if (loot.relic != null) AddReward("Relic", loot.relic + (Meta.RelicSlotOpen ? "" : " · Slot at 60 stars"), "quality");
            if (game.sector != null) foreach (int i in game.newStars) AddReward("New star", game.sector.stars[i], "quality");
            if (game.ShardsGained > 0) AddReward("Heavy shards", "+" + game.ShardsGained, "squad");
            foreach (var x in game.StarTrack) AddReward("Star track", x, "quality");
            foreach (var x in game.OpsToasts) AddReward("Operations", x, "quality");
            foreach (var x in game.RankUps) AddReward("Rank up", x, "squad");
            if (rewards.Count == 0) rewards.Add(new Reward { name = "No equipment recovered", stat = "" });
            SetRewardPage(0); SetNavigationEnabled(true); RefreshPicks();
        }

        void AddReward(string name, string text, string icon) => rewards.Add(new Reward { name = name, stat = text, icon = Holo.Icon(icon) });

        static string Clock(float seconds) => $"{Mathf.FloorToInt(seconds / 60):00}:{Mathf.FloorToInt(seconds % 60):00}";

        public void SetRewardPage(int page)
        {
            rewardPage = Mathf.Clamp(page, 0, Mathf.Max(0, rewards.Count - 1));
            if (rewards.Count == 0) return;
            var reward = rewards[rewardPage]; itemName.text = reward.name; itemStat.text = reward.stat;
            itemIcon.sprite = reward.icon; itemIcon.gameObject.SetActive(reward.icon != null);
            prevButton.gameObject.SetActive(rewards.Count > 1); nextButton.gameObject.SetActive(rewards.Count > 1);
            pageLabel.text = rewards.Count > 1 ? $"{rewardPage + 1} / {rewards.Count}" : "";
            prevButton.interactable = rewardPage > 0; nextButton.interactable = rewardPage < rewards.Count - 1;
        }

        public void OpenDetails()
        {
            if (!CanAct()) return;
            details.gameObject.SetActive(true); details.SetAsLastSibling(); RefreshPicks();
        }

        void RefreshPicks()
        {
            picks = Meta.BestUpgrades(3);
            bool available = picks.Count > 0 && picks[0].credits <= Progress.D.credits && (picks[0].canBuy?.Invoke() ?? true);
            UpgradeMode = available ? "upgrade" : "armory";
            if (upgradeLabel != null) upgradeLabel.text = available ? "Upgrade" : "Equipment";
            upgradeName.text = picks.Count > 0 ? picks[0].name + (picks.Count > 1 ? $" +{picks.Count - 1} more" : "") : "No upgrade available";
            upgradeDetail.text = picks.Count > 0 ? $"{(picks.Count > 1 ? "Up to " : "")}+{picks.Sum(p => p.gain):N0} power ({picks.Sum(p => p.credits):N0} credits)" : "View your loadout";
            if (!available && picks.Count > 0) upgradeDetail.text = "Requirements not met";
        }

        bool CanAct() => !leaving && Visible && (game.state == Game.State.Dead || game.state == Game.State.Won);

        void SetNavigationEnabled(bool enabled)
        {
            foreach (var b in new[] { deathMenu, deathRetry, victoryMenu, detailsMenu, detailsRetry, upgradeButton }) b.interactable = enabled;
        }

        public void SubmitRetry()
        {
            if (!CanAct()) return;
            leaving = true; SetNavigationEnabled(false); Sfx.Click(); Haptics.Light(); gameObject.SetActive(false); game.Restart();
        }

        public void SubmitMenu()
        {
            if (!CanAct()) return;
            leaving = true; SetNavigationEnabled(false); Sfx.Click(); Haptics.Light(); gameObject.SetActive(false); game.ToMenu();
        }

        public void SubmitUpgrade()
        {
            if (!CanAct() || buying || Time.unscaledTime - lastUpgrade < .35f) return;
            lastUpgrade = Time.unscaledTime; buying = true;
            try
            {
                RefreshPicks();
                if (UpgradeMode == "armory")
                {
                    leaving = true; gameObject.SetActive(false); game.ToMenu(); game.menu.OpenSheet("loadout"); return;
                }
                int before = Progress.Power(), count = 0;
                foreach (var pick in picks) if (pick.buy()) count++;
                if (count > 0) { Haptics.Medium(); Sfx.Toggle(); } else { Sfx.Deny(); Haptics.Warn(); }
                RefreshPicks();
                verdict.text = count > 0 ? $"Power {before:N0} → {Progress.Power():N0} · {count} upgrade{(count == 1 ? "" : "s")}" : "Upgrade unavailable";
            }
            finally { buying = false; }
        }

        void LateUpdate()
        {
            if (upgradeButton == null) return;
            upgradeButton.interactable = !leaving && !buying && Time.unscaledTime - lastUpgrade >= .35f;
            if (!won || game.Grade == null || Time.unscaledTime - shownAt < 2.6f || grade.text.Length > 0) return;
            grade.text = game.Grade;
            verdict.text = game.GradeVerdict + (game.GradeBonus > 0 ? $" +{game.GradeBonus:N0} credits" : "");
            Haptics.Heavy();
        }
    }
}
