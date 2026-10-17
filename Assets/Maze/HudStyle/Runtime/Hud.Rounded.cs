using System;
using TMPro;
using UnityEngine;

namespace Maze
{
    public partial class Hud
    {
        RectTransform roundedStatus, blueHudFrame;
        UnityEngine.UI.Image cargoHintBack;
        RoundedHudButton dashVisual, skillVisual, flareVisual;
        internal RectTransform RoundedStatus => roundedStatus;
        internal RectTransform BlueHudFrame => blueHudFrame;

        void BuildRoundedHud()
        {
            blueHudFrame = BlueUi.Frame(hudRoot,"BlueHudLayout");
            roundedStatus = BlueUi.Panel(blueHudFrame,"Status",31,28,451,117).rectTransform;
            BlueUi.Image(roundedStatus,"HealthIcon","stat_health",18,11,54,48).preserveAspect = true;
            hpRoot = BlueUi.Rect(roundedStatus,"Hp",88,18,350,40);
            hpBack = HudImage(hpRoot,"Bar","hud_health_track","health_track",0,0,350,40);
            hpFill = Holo.Img(Holo.Fill(hpRoot,"Fill",4),BlueUi.Art("hud_health_fill") ?? RoundedHudStyle.Art("health_fill"),Color.white);
            hpFill.type = UnityEngine.UI.Image.Type.Filled; hpFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            hpText = BlueUi.Text(hpRoot,"Num","",4,0,342,40,30,Color.white);
            hpText.alignment = TextAlignmentOptions.Center;

            wavePill = BlueUi.Panel(blueHudFrame,"WavePill",688,23,296,94).rectTransform;
            waveText = BlueUi.Text(wavePill,"T","",12,12,272,70,36);
            waveText.alignment = TextAlignmentOptions.Center;
            statsText = BlueUi.Text(blueHudFrame,"Stats","",32,153,330,34,24,BlueUi.Navy,true);
            testTag = BlueUi.Text(blueHudFrame,"TestTag","Test mode",1390,124,250,34,24,BlueUi.Gold,true);
            testTag.alignment = TextAlignmentOptions.Right; testTag.gameObject.SetActive(false);

            pauseBtn = Holo.Button(blueHudFrame,"Pause",Vector2.one * 124,null,null);
            var pr = (RectTransform)pauseBtn.transform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0,1);
            pr.anchoredPosition = new Vector2(1537,-11);
            RoundedHudButton.Attach(pauseBtn,"pause",true);
            pauseBtn.onClick = ()=>game.TogglePause();
            dashBtn = RoundedButton("Dash","dash",0,out dashVisual);
            skillBtn = RoundedButton("Skill","barrage",1,out skillVisual);
            flareBtn = RoundedButton("Flare","flare",2,out flareVisual);
            flareBtn.gameObject.SetActive(false);

            left.baseImg = StickImg("MoveStick",260,BlueUi.Art("hud_joystick_base") ?? RoundedHudStyle.Art("joystick_base"),new Color(1,1,1,.7f));
            left.knob = StickImg("MoveKnob",110,BlueUi.Art("hud_joystick_thumb") ?? RoundedHudStyle.Art("joystick_thumb"),new Color(1,1,1,.92f));
            right.baseImg = StickImg("AimStick",260,ringSprite,Holo.A(Holo.Cyan,.55f));
            right.knob = StickImg("AimKnob",110,circle,Holo.A(Holo.Cyan,.75f));
            ShowStick(left,false); ShowStick(right,false);

            banner = BlueUi.Text(blueHudFrame,"Banner","",276,270,1120,104,72,BlueUi.Navy);
            banner.alignment = TextAlignmentOptions.Center;
            hint = BlueUi.Text(blueHudFrame,"Hint","",276,162,1120,54,30,BlueUi.Navy,true);
            hint.alignment = TextAlignmentOptions.Center;
            cargoHintBack = BlueUi.Panel(blueHudFrame,"CargoHintBack",260,154,1152,70);
            cargoHintBack.transform.SetSiblingIndex(hint.transform.GetSiblingIndex());
            cargoHintBack.raycastTarget = false; cargoHintBack.enabled = false;
        }

        static UnityEngine.UI.Image HudImage(Transform parent,string name,string blueArt,string oldArt,float x,float y,float w,float h)
            => BlueUi.Image(parent,name,BlueUi.Art(blueArt) ?? RoundedHudStyle.Art(oldArt),x,y,w,h);

        HoloButton RoundedButton(string name,string icon,int slot,out RoundedHudButton visual)
        {
            const float size = 140, gap = 9;
            var b = Holo.Button(blueHudFrame,name,Vector2.one * size,null,null);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0,1);
            rt.anchoredPosition = new Vector2(1484-slot*(size+gap),-738);
            b.manual = true; visual = RoundedHudButton.Attach(b,icon); return b;
        }

        static void FlashRounded(HoloButton b)
        {
            if (b == null) return;
            b.Flash(false); b.GetComponent<RoundedHudButton>()?.Pulse();
        }

        internal bool TryHudAction(Vector2 screen,ref InputFrame f)
        {
            if (mode != Mode.Playing) return false;
            if (Hit((RectTransform)dashBtn.transform,screen)) { f.dash = true; FlashRounded(dashBtn); return true; }
            if (SkillOn && Hit((RectTransform)skillBtn.transform,screen)) { f.skill = true; FlashRounded(skillBtn); return true; }
            if (flareBtn.gameObject.activeSelf && Hit((RectTransform)flareBtn.transform,screen)) { f.flare = true; FlashRounded(flareBtn); return true; }
            return false;
        }

        void RefreshRoundedActions()
        {
            var pl = game.player;
            bool ready = pl.dashCd <= 0;
            dashVisual.SetState(pl.dashCd,game.cfg.player.dashCooldown,pl.dashT > 0,false);
            if (ready && !dashWasReady && mode == Mode.Playing) { FlashRounded(dashBtn); if (!FirstCryoAudio.Ready(game)) Sfx.Click(); Haptics.Light(); }
            dashWasReady = ready;
            skillBtn.gameObject.SetActive(SkillOn && mode == Mode.Playing);
            skillVisual.SetIcon("barrage");
            skillVisual.SetState(Abilities.Cd,Abilities.Cooldown,Abilities.BarrageT > 0,false);
            bool dark = CellLight.I != null && CellLight.I.Dark && mode == Mode.Playing;
            flareBtn.gameObject.SetActive(dark); flareBtn.dimmed = CellLight.Flares <= 0;
            flareVisual.SetState(0,1,false,CellLight.Flares <= 0,CellLight.Flares);
            hint.rectTransform.anchoredPosition = new Vector2(276,game.run && game.Boss != null ? -250 : -162);
            if (cargoHintBack != null)
            {
                cargoHintBack.enabled = CohesiveZone.For(game) && mode == Mode.Playing && hintT > 0 && !string.IsNullOrEmpty(hint.text);
                cargoHintBack.rectTransform.anchoredPosition = hint.rectTransform.anchoredPosition + new Vector2(-16,8);
                cargoHintBack.color = new Color(1,1,1,Mathf.Clamp01(hintT * 2.5f));
            }
        }
    }

    public partial class RunUi
    {
        TMP_Text roundedGoalValue;
        UnityEngine.UI.Image blueLevelBadge;
        RectTransform blueXpBar;
        void BuildRoundedRunHeader()
        {
            xpRoot = BlueUi.Rect(game.hud.RoundedStatus,"Xp",20,62,400,47);
            var badge = blueLevelBadge = BlueUi.Image(xpRoot,"Badge",BlueUi.Art("hud_level_badge") ?? RoundedHudStyle.Art("panel_level"),0,0,52,47);
            lvText = BlueUi.Text(badge.transform,"Lv","",2,0,48,45,30);
            lvText.alignment = TextAlignmentOptions.Center;
            var xb = BlueUi.Image(xpRoot,"Bar",BlueUi.Art("hud_xp_track") ?? RoundedHudStyle.Art("xp_track"),44,14,350,24);
            blueXpBar = xb.rectTransform; xb.preserveAspect = false;
            xpFill = Holo.Img(Holo.Fill(xb.transform,"Fill",3),BlueUi.Art("hud_xp_fill") ?? RoundedHudStyle.Art("xp_fill"),Color.white);
            xpFill.type = UnityEngine.UI.Image.Type.Filled; xpFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            floorText = BlueUi.Text(xpRoot,"Floor","",0,0,1,1,18); floorText.gameObject.SetActive(false);
            badge.transform.SetAsLastSibling();

            trackPanel = BlueUi.Panel(game.hud.BlueHudFrame,"Tracker",688,23,296,94).rectTransform;
            trackCg = trackPanel.gameObject.AddComponent<CanvasGroup>();
            goalText = BlueUi.Text(trackPanel,"Goal","",10,5,276,49,35);
            goalText.alignment = TextAlignmentOptions.Center;
            roundedGoalValue = BlueUi.Text(trackPanel,"Value","",12,49,272,34,27,BlueUi.Muted,true);
            roundedGoalValue.alignment = TextAlignmentOptions.Center;
            goalBar = BlueUi.Rect(trackPanel,"GoalBar",20,85,256,4);
            Holo.Img(Holo.Fill(goalBar,"Track"),Holo.WhiteSprite,new Color(.55f,.78f,.97f,.35f));
            goalFill = Holo.Img(Holo.Fill(goalBar,"Fill"),Holo.WhiteSprite,BlueUi.Blue);
            goalFill.type = UnityEngine.UI.Image.Type.Filled; goalFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            puText = BlueUi.Text(game.hud.BlueHudFrame,"PowerUps","",276,166,1120,58,27,BlueUi.Navy,true);
            puText.alignment = TextAlignmentOptions.Center;
            puText.textWrappingMode = TextWrappingModes.Normal;

            // The hidden route data remains available to existing run checks; it is not an extra HUD row.
            pipsRoot = Holo.Rect(safe,"Pips");
            for (int i = 0; i < 5; i++)
            {
                var pr = Holo.Rect(pipsRoot,"Pip"+i);
                var bg = Holo.Img(pr,Holo.FillSprite,Color.clear);
                var ic = Holo.Img(Holo.Fill(pr,"Ic"),Holo.Icon("rifle"),Holo.Muted);
                var tx = Holo.Label(Holo.Fill(pr,"T"),"",17,Holo.Muted); pips[i] = (bg,ic,tx);
            }
            pipsRoot.gameObject.SetActive(false);
            // Boss and chain already share this native frame and source panel backing.
            bossRt.anchoredPosition = new Vector2(436,-134); bossRt.sizeDelta = new Vector2(800,80);
            chainRt.anchoredPosition = new Vector2(32,-166);
        }

        string RoundedPowerUps()
        {
            var line = new System.Text.StringBuilder(); int shown = 0, extra = 0;
            foreach (var kv in PowerUps.Active)
            {
                if (kv.Value <= 0) continue;
                if (shown >= 3) { extra++; continue; }
                var def = PowerUps.Get(kv.Key);
                if (shown++ > 0) line.Append("   ");
                line.Append($"{def.name} {Mathf.CeilToInt(kv.Value)}");
            }
            if (extra > 0) line.Append($"   +{extra}");
            if (CellFloors.Rad > .02f) line.Append($"   RAD {Mathf.RoundToInt(CellFloors.Rad*100)}%");
            return line.ToString();
        }

        void RefreshRoundedRunHeader()
        {
            var parts = game.GoalLine.Split(new[] { "//" },StringSplitOptions.None);
            goalText.text = $"Floor {game.floor} · {DisplayCase(Game.GoalNames[(int)game.shownGoal])}";
            string detail = parts.Length > 1 ? (game.goal == Game.Goal.Purge ? parts[1].Trim() : string.Join(" · ",parts,1,parts.Length-1).Trim()) : "";
            if (game.goal == Game.Goal.Purge) detail = detail.Replace("WAVE ","Wave ").Replace("/"," of ");
            else detail = DisplayCase(detail.Replace("GENERATOR ","Generator ").Replace("REACH THE LIFT","Reach the lift").Replace("HACK DOOR ","Door "));
            roundedGoalValue.text = detail;
            float headerWidth = goalText.text.Length > 18 || detail.Length > 25 ? 380 : 296;
            trackPanel.anchoredPosition = new Vector2((BlueUi.Width-headerWidth)*.5f,-23);
            trackPanel.sizeDelta = new Vector2(headerWidth,94);
            goalText.rectTransform.sizeDelta = new Vector2(headerWidth-20,49);
            roundedGoalValue.rectTransform.sizeDelta = new Vector2(headerWidth-24,34);
            roundedGoalValue.fontSize = detail.Length > 32 ? 20 : detail.Length > 24 ? 23 : 27;
            goalBar.sizeDelta = new Vector2(headerWidth-40,4);
            goalText.color = BlueUi.Navy; goalFill.color = BlueUi.Blue; chainBg.color = Color.white;
            bool maxLevel = game.level >= Game.MaxLevel;
            lvText.text = maxLevel ? "MAX " + Game.MaxLevel : game.level.ToString();
            lvText.fontSizeMax = maxLevel ? 22 : 30; lvText.fontSizeMin = maxLevel ? 18 : 22;
            lvText.fontSize = lvText.fontSizeMax;
            lvText.rectTransform.sizeDelta = new Vector2(maxLevel ? 84 : 48,45);
            blueLevelBadge.sprite = maxLevel ? BlueUi.Art("button_yellow_blank") : BlueUi.Art("hud_level_badge") ?? RoundedHudStyle.Art("panel_level");
            blueLevelBadge.type = maxLevel ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            blueLevelBadge.preserveAspect = !maxLevel;
            blueLevelBadge.rectTransform.sizeDelta = new Vector2(maxLevel ? 88 : 52,47);
            blueXpBar.anchoredPosition = new Vector2(maxLevel ? 84 : 44,-14);
            blueXpBar.sizeDelta = new Vector2(maxLevel ? 310 : 350,24);
            if (EnemyAI.Latched <= 0 || !FirstCryoFx.Active) puText.text = RoundedPowerUps();
            puText.rectTransform.anchoredPosition = new Vector2(276,bossRt.gameObject.activeSelf ? -250 : -166);
            puText.gameObject.SetActive(trackPanel.gameObject.activeSelf && !game.hud.HintBusy && !string.IsNullOrEmpty(puText.text));
            chainRt.anchoredPosition = new Vector2(32,Prefs.ShowFps ? -206 : -166);
        }

        static string DisplayCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()).Replace("Hp","HP").Replace("Smg","SMG");
        }
    }
}
