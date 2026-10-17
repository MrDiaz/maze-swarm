using TMPro;
using UnityEngine;

namespace Maze
{
    public partial class RunUi
    {
        void BuildBlueBossAndChain()
        {
            bossRt = BlueUi.Rect(game.hud.BlueHudFrame,"BossBar",436,134,800,80);
            BlueUi.Panel(bossRt,"Background",0,0,800,80);
            bossName = BlueUi.Text(bossRt,"Name","",24,7,752,36,30);
            bossName.alignment = TextAlignmentOptions.Center;
            bossBar = BlueUi.Rect(bossRt,"Bar",24,54,752,18);
            Holo.Img(Holo.Fill(bossBar,"Track"),BlueUi.Art("hud_health_track") ?? RoundedHudStyle.Art("health_track"),Color.white);
            bossFill = Holo.Img(Holo.Fill(bossBar,"Fill",2),BlueUi.Art("hud_health_fill") ?? RoundedHudStyle.Art("health_fill"),Color.white);
            bossFill.type = UnityEngine.UI.Image.Type.Filled; bossFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;

            chainRt = BlueUi.Rect(game.hud.BlueHudFrame,"Chain",32,166,150,64);
            chainBg = BlueUi.Panel(chainRt,"Background",0,0,150,64,true);
            chainText = BlueUi.Text(chainRt,"T","",8,2,134,44,34);
            chainText.alignment = TextAlignmentOptions.Center;
            var track = BlueUi.Rect(chainRt,"Bar",12,50,126,6);
            Holo.Img(Holo.Fill(track,"Track"),Holo.WhiteSprite,new Color(.51f,.75f,.94f,.35f));
            chainFill = Holo.Img(Holo.Fill(track,"Fill"),Holo.WhiteSprite,BlueUi.Blue);
            chainFill.type = UnityEngine.UI.Image.Type.Filled; chainFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        }

        void BuildBlueFloorIntro()
        {
            introRt = Holo.Fill(canvas.transform,"Intro");
            var frame = BlueUi.Frame(introRt,"IntroLayout");
            introBand = BlueUi.Panel(frame,"Band",411,285,850,240);
            introFloor = BlueUi.Text(introBand.transform,"F","",36,16,778,38,28,BlueUi.Muted,true);
            introFloor.alignment = TextAlignmentOptions.Center;
            introTitle = BlueUi.Text(introBand.transform,"T","",180,64,630,92,78);
            introIcon = BlueUi.Image(introBand.transform,"Icon",(Sprite)null,64,66,104,104);
            introLine = BlueUi.Text(introBand.transform,"L","",40,170,770,50,30,BlueUi.Muted,true);
            introLine.alignment = TextAlignmentOptions.Center;
            introRt.gameObject.SetActive(false);
        }

        void ArrangeBlueFloorIntro(bool hasIcon)
        {
            introTitle.rectTransform.anchoredPosition = new Vector2(hasIcon ? 180 : 40,-64);
            introTitle.rectTransform.sizeDelta = new Vector2(hasIcon ? 630 : 770,92);
            introTitle.alignment = hasIcon ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
        }

        void BuildBlueEvolution()
        {
            evoRt = Holo.Fill(canvas.transform,"Evolve");
            Holo.Img(evoRt,Holo.WhiteSprite,new Color(.17f,.48f,1,.14f));
            var frame = BlueUi.Frame(evoRt,"EvolutionLayout");
            var panel = BlueUi.Panel(frame,"Band",336,285,1000,350).rectTransform;
            BlueUi.Text(panel,"Caption","Weapon evolution",280,24,670,42,30,BlueUi.Muted,true);
            evoText = BlueUi.Text(panel,"T","",280,80,670,88,68);
            evoSub = BlueUi.Text(panel,"S","",280,180,670,120,32,BlueUi.Muted,true);
            evoSub.textWrappingMode = TextWrappingModes.Normal;
            evoIcon = BlueUi.Image(panel,"Icon",(Sprite)null,40,80,210,210);
            evoRt.gameObject.SetActive(false);
        }
        void RefreshBlueFeedbackVisibility()
        {
            if (game == null || game.state != Game.State.Menu && game.state != Game.State.Dead && game.state != Game.State.Won) return;
            introT = evoT = -1;
            introRt.gameObject.SetActive(false);
            evoRt.gameObject.SetActive(false);
        }
    }
}
