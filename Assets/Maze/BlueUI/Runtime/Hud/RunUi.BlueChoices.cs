using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Maze
{
    public partial class RunUi
    {
        RectTransform blueChoiceFrame, blueChoicePanel;
        readonly List<BlueUpgradeCard> blueChoiceCards = new();
        int blueChoicePickFrame = -1;

        void BuildBlueChoices()
        {
            choiceRt = Holo.Fill(canvas.transform,"Choice");
            Holo.Img(choiceRt,Holo.WhiteSprite,new Color(.12f,.27f,.53f,.28f),false,true);
            choiceCg = choiceRt.gameObject.AddComponent<CanvasGroup>();
            blueChoiceFrame = BlueUi.Frame(choiceRt,"ChoiceLayout");
            blueChoicePanel = BlueUi.Panel(blueChoiceFrame,"Panel",280,138,1112,690,false,true).rectTransform;
            choiceTitle = BlueUi.Text(blueChoicePanel,"Title","Level up",24,6,1064,93,84);
            choiceTitle.alignment = TextAlignmentOptions.Center;
            choiceSub = BlueUi.Text(blueChoicePanel,"Sub","Choose an upgrade",24,87,1064,48,38,BlueUi.Muted,true);
            choiceSub.alignment = TextAlignmentOptions.Center;
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var view = new BlueUpgradeCard(blueChoiceFrame,index,()=>PickBlueChoice(index));
                blueChoiceCards.Add(view); cards.Add(view.contract);
            }
            choiceRt.gameObject.SetActive(false);
        }

        void PickBlueChoice(int index)
        {
            if (game.state != Game.State.Choosing || !choiceRt.gameObject.activeInHierarchy || blueChoicePickFrame == Time.frameCount) return;
            blueChoicePickFrame = Time.frameCount;
            game.Pick(index);
        }

        void ShowBlueChoice(Game.Choice c,List<Card> offer)
        {
            choiceRt.gameObject.SetActive(true); choiceCg.alpha = 0;
            FirstCryoAudio.Reveal(game);
            choiceTitle.text = c == Game.Choice.Cache ? "Supply cache" : c == Game.Choice.Rare ? "Rare upgrade" : "Level up";
            choiceSub.text = c == Game.Choice.Cache ? $"Floor {game.floor} cleared · Choose one crate" : "Choose an upgrade";
            int count = Mathf.Min(offer.Count,blueChoiceCards.Count);
            float width = count >= 4 ? 1504 : 1112;
            float leftEdge = (BlueUi.Width-width)*.5f;
            blueChoicePanel.anchoredPosition = new Vector2(leftEdge,-138);
            blueChoicePanel.sizeDelta = new Vector2(width,690);
            choiceTitle.rectTransform.sizeDelta = new Vector2(width-48,93);
            choiceSub.rectTransform.sizeDelta = new Vector2(width-48,48);
            const float cardWidth = 352;
            const float gap = 10;
            float first = (BlueUi.Width-(cardWidth*count+gap*(count-1)))*.5f;
            bool expandedDescription = count >= 4 || offer.Exists(card=>card.desc.Length > 65);
            for (int i = 0; i < blueChoiceCards.Count; i++)
            {
                var view = blueChoiceCards[i];
                view.root.gameObject.SetActive(i<count);
                if (i >= count) continue;
                view.root.anchoredPosition = new Vector2(first+i*(cardWidth+gap),-285);
                view.SetLayout(expandedDescription);
                view.Bind(offer[i],game.mods[offer[i].id],game.ObRecommend && i==0);
            }
            InterceptBlueFloorClear(c);
        }

        sealed class BlueUpgradeCard
        {
            public readonly RectTransform root;
            public readonly HoloButton contract;
            readonly UnityEngine.UI.Image picture, surface, recommendation, descriptionBacking;
            readonly RectTransform chooseTarget;
            readonly TMP_Text title, description, rank;

            public BlueUpgradeCard(Transform parent,int index,System.Action action)
            {
                root = BlueUi.Rect(parent,"Card"+index,0,285,352,514);
                surface = BlueUi.Panel(root,"Surface",0,0,352,514);
                surface.raycastTarget = true;
                var cardButton = root.gameObject.AddComponent<UnityEngine.UI.Button>();
                cardButton.targetGraphic = surface;
                cardButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                var colors = cardButton.colors;
                colors.normalColor = Color.white; colors.highlightedColor = Color.white;
                colors.pressedColor = new Color(.82f,.92f,1); colors.disabledColor = new Color(.7f,.78f,.87f);
                colors.fadeDuration = 0; cardButton.colors = colors; cardButton.onClick.AddListener(()=>action());
                contract = root.gameObject.AddComponent<HoloButton>();
                contract.enabled = false; contract.manual = true; contract.body = root;
                contract.onClick = action; contract.fill = surface;
                BlueUi.Panel(root,"PictureBacking",19,20,314,244,true);
                picture = BlueUi.Image(root,"Icon",(Sprite)null,19,20,314,244);
                picture.preserveAspect = true;
                recommendation = BlueUi.Image(root,"Recommended","check_green",286,27,39,39);
                title = BlueUi.Text(root,"Name","",12,270,328,54,40);
                title.alignment = TextAlignmentOptions.Center;
                descriptionBacking = BlueUi.Panel(root,"DescriptionBacking",19,325,314,78,true);
                description = BlueUi.Text(root,"Desc","",29,329,294,70,30,BlueUi.Muted,true);
                description.textWrappingMode = TextWrappingModes.Normal;
                description.alignment = TextAlignmentOptions.Center;
                rank = BlueUi.Text(root,"Picks","",28,26,150,31,23,BlueUi.Navy,true);
                chooseTarget = (RectTransform)BlueUi.Button(root,"Choose","Choose",19,416,314,85,action,false,null,44).transform;
            }

            public void SetLayout(bool expanded)
            {
                root.sizeDelta = new Vector2(352,expanded ? 528 : 514);
                surface.rectTransform.sizeDelta = root.sizeDelta;
                descriptionBacking.rectTransform.sizeDelta = new Vector2(314,expanded ? 92 : 78);
                description.rectTransform.sizeDelta = new Vector2(294,expanded ? 84 : 70);
                // Shared button geometry includes 19.5 px transparent padding above its 85 px face.
                chooseTarget.anchoredPosition = new Vector2(19,-(expanded ? 410.5f : 396.5f));
            }

            public void Bind(Card card,int have,bool recommended)
            {
                title.text = DisplayCase(card.name);
                title.fontSize = card.name.Length > 18 ? 32 : card.name.Length > 14 ? 36 : 40;
                description.text = card.desc;
                description.fontSizeMax = card.desc.Length > 65 ? 22 : card.desc.Length > 46 ? 24 : card.desc.Length > 34 ? 27 : 30;
                description.fontSizeMin = 18; description.fontSize = description.fontSizeMax;
                // The artwork changes only where the approved image actually represents this upgrade.
                string source = card.id == "rate" ? "upgrade_rapid" : card.id == "hp" ? "upgrade_vitality" : card.id == "rico" ? "upgrade_ricochet" : null;
                picture.sprite = source != null ? BlueUi.Art(source) : Holo.Art(ArtName(card));
                picture.color = Color.white;
                picture.rectTransform.anchoredPosition = new Vector2(source != null ? 19 : 60,source != null ? -20 : -38);
                picture.rectTransform.sizeDelta = source != null ? new Vector2(314,244) : new Vector2(232,208);
                picture.preserveAspect = true;
                rank.text = card.evo ? "Evolution" : card.id.StartsWith("cache") ? "" : $"{have+1} / {card.max}";
                recommendation.gameObject.SetActive(recommended);
                contract.selected = card.evo || recommended;
                contract.silent = true;
                // The selected/recommended perimeter is independent of the painted illustration.
                surface.color = recommended || card.evo ? new Color(.94f,1,1,1) : Color.white;
            }
        }
    }
}
