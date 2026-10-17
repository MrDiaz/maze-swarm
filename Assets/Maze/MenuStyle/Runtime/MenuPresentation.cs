using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
namespace Maze
{
    // Presentation only: existing MainMenu, Meta, Progress and Game APIs remain authoritative.
    public sealed class MenuPresentation:MonoBehaviour
    {
        public enum Screen { Home,Heroes,Weapons }
        MainMenu owner;Game game;RectTransform legacyRoot;Canvas canvas;Action deploy;
        RectTransform home,heroes,weapons;MenuModelPreview model;
        readonly List<UnityEngine.UI.RawImage> avatars=new();readonly List<UnityEngine.UI.Image> heroSelections=new();
        Canvas noticeCanvas;TMP_Text notice;float noticeUntil;
        TMP_Text homeHero,heroName,heroLevel,abilityName,abilityBody,heroCost,heroStarCost,weaponName,weaponBody,weaponLevel,equipText;
        UnityEngine.UI.Button heroLevelButton,heroStarButton,heroSelectButton,equipButton;
        readonly List<(int index,UnityEngine.UI.Button button,UnityEngine.UI.Image selected,TMP_Text label)> weaponCards=new();
        readonly List<(int slot,TMP_Text state)> slotLabels=new();Screen screen;int previewWeapon=-1,weaponPage;string shownHero;bool visible;
        public Texture HeroTexture=>model?.Texture;public Screen Current=>screen;public bool Visible=>canvas!=null&&canvas.enabled;
        public bool SubscreenOpen=>Visible&&screen!=Screen.Home;
        public UnityEngine.UI.Button DeployButton{get;private set;}
        public UnityEngine.UI.Button HeroesButton{get;private set;}
        public UnityEngine.UI.Button SettingsButton{get;private set;}
        public UnityEngine.UI.Button GearButton{get;private set;}
        public UnityEngine.UI.Button BackButton{get;private set;}
        public void Init(MainMenu menu,Game g,RectTransform original,Action onDeploy)
        {
            owner=menu;game=g;legacyRoot=original;deploy=onDeploy;canvas=Holo.MakeCanvas(transform,"StyledMenuCanvas",11);
            noticeCanvas=Holo.MakeCanvas(transform,"StyledMenuNoticeCanvas",30);var nf=MenuStyle.Frame(noticeCanvas.transform,"NoticeLayout");MenuStyle.Panel(nf,"NoticePanel",370,750,932,116);notice=MenuStyle.Text(nf,"Notice","",398,770,876,75,30,MenuStyle.Paper,true);notice.alignment=TextAlignmentOptions.Center;noticeCanvas.enabled=false;
            model=gameObject.AddComponent<MenuModelPreview>();model.Init(g);home=BuildHome();heroes=BuildHeroes();weapons=BuildWeapons();ShowHome();RefreshVisibility();
        }
        RectTransform Root(string name,string backdrop,bool dim=false)
        {
            var root=Holo.Fill(canvas.transform,name);MenuStyle.Background(root,"Environment",backdrop,dim ? .15f : 0);return root;
        }
        UnityEngine.UI.RawImage Avatar(Transform parent,float x,float y,float w,float h)
        {
            float fittedWidth=Mathf.Min(w,h*640f/896),fittedHeight=fittedWidth*896f/640;var a=MenuStyle.Portrait(parent,"SelectedHero",x+(w-fittedWidth)*.5f,y+(h-fittedHeight)*.5f,fittedWidth,fittedHeight);a.texture=model.Texture;avatars.Add(a);return a;
        }
        static Texture2D contactShadow;
        static void ContactShadow(Transform f,float x,float y,float w,float h){
            if(contactShadow==null){contactShadow=new Texture2D(128,64,TextureFormat.RGBA32,false){name="MenuContactShadow",wrapMode=TextureWrapMode.Clamp};var colors=new Color[128*64];for(int yy=0;yy<64;yy++)for(int xx=0;xx<128;xx++){float dx=(xx-63.5f)/63.5f,dy=(yy-31.5f)/31.5f;colors[yy*128+xx]=new Color(.02f,.03f,.06f,.48f*Mathf.Pow(Mathf.Max(0,1-dx*dx-dy*dy),2));}contactShadow.SetPixels(colors);contactShadow.Apply();}
            var raw=Holo.Raw(MenuStyle.Rect(f,"CharacterContactShadow",x,y,w,h),contactShadow,Color.white);raw.raycastTarget=false;
        }
        RectTransform BuildHome()
        {
            var root=Holo.Fill(canvas.transform,"Hangar");var f=BlueUi.Frame(root,"HangarLayout");RichHangarStyle.Background(root,f);
            // Independent live UI above the rich scene plate; the approved Heavy is the only depicted/selected hero.
            RichHangarStyle.Image(f,"HangarHeadingPlate","pill",54,174,266,45,true);RichHangarStyle.Text(f,"HangarHeading","HANGAR",65,174,244,43,28);
            float heroTagX=CohesiveZone.Enabled?760:456,heroTagY=CohesiveZone.Enabled?292:112;
            RichHangarStyle.Image(f,"SelectedHeroPlate","pill",heroTagX,heroTagY,219,43,true);RichHangarStyle.Text(f,"SelectedHeroState","SELECTED HERO",heroTagX+9,heroTagY,201,42,22);
            RichHangarStyle.Image(f,"SelectedHeroPointer","pointer",heroTagX+98,heroTagY+42,24,15);
            SettingsButton=RichHangarStyle.Button(f,"Settings","","icon_settings",1549,40,84,74,()=>owner.OpenSheet("settings"));
            RichHangarStyle.Image(f,"NavigationTray","tray",356,774,964,120,true);
            DeployButton=RichHangarStyle.Button(f,"Deploy","DEPLOY","icon_deploy",377,786,314,92,Deploy,true);
            HeroesButton=RichHangarStyle.Button(f,"Heroes","HEROES","icon_heroes",703,786,297,92,OpenHeroes);
            GearButton=RichHangarStyle.Button(f,"Equipment","GEAR","icon_gear",1013,786,286,92,()=>OpenEquipment(-1));
            homeHero=BlueUi.Text(f,"HeroIdentity","",0,0,1,1);homeHero.gameObject.SetActive(false);return root;
        }
        RectTransform BuildHeroes()
        {
            var root=Root("Heroes","showcase");var f=MenuStyle.Frame(root,"HeroesLayout");
            MenuStyle.Button(f,"Back","",36,25,116,116,Back,false,"back");MenuStyle.Text(f,"Heading","HEROES",180,36,600,94,64,MenuStyle.Paper,true);
            var avatar=Avatar(f,250,155,530,505);
            avatar.gameObject.AddComponent<MenuHeroPlatformAnchor>().Init(root.Find("Environment").GetComponent<UnityEngine.UI.RawImage>(),avatar.rectTransform,model);
            var right=MenuStyle.Panel(f,"Overview",870,90,766,804);
            MenuStyle.Image(right.transform,"RoleIcon",MenuStyle.Art("icon_objective_hold_cyan"),42,46,112,112).preserveAspect=true;
            heroName=MenuStyle.Text(right.transform,"Name","",178,52,538,82,57,MenuStyle.Paper,true);heroLevel=MenuStyle.Text(right.transform,"Level","",180,134,530,46,29,MenuStyle.Muted);
            var ability=MenuStyle.Button(right.transform,"Ability","",38,232,690,156,()=>owner.OpenSheet("talents"));
            abilityName=MenuStyle.Text(ability.transform,"Name","",124,15,492,48,37,MenuStyle.Paper,true);abilityBody=MenuStyle.Text(ability.transform,"Description","",124,61,492,66,25,MenuStyle.Muted);
            var glyph=MenuStyle.Image(ability.transform,"SkillIcon",RoundedHudStyle.Art("icon_freeze_cyan"),28,26,80,80);glyph.preserveAspect=true;
            MenuStyle.Button(right.transform,"Equipment","EQUIPMENT",38,382,690,112,OpenWeapons,false,"gear");
            heroLevelButton=MenuStyle.Button(right.transform,"LevelUp","LEVEL UP",38,522,330,112,()=>HeroLevel());heroStarButton=MenuStyle.Button(right.transform,"StarUp","STAR UP",386,522,342,112,()=>HeroStar());
            heroCost=MenuStyle.Text(right.transform,"LevelCost","",46,640,316,38,24,MenuStyle.Muted);heroStarCost=MenuStyle.Text(right.transform,"StarCost","",398,640,330,38,24,MenuStyle.Muted);
            heroSelectButton=MenuStyle.Button(right.transform,"Selected","SELECTED",38,678,690,112,()=>{},true);
            string[] ids={"vanguard","engineer"};for(int i=0;i<ids.Length;i++)
            {
                var id=ids[i];var b=MenuStyle.Button(f,"Hero_"+id,"",336+i*218,686,206,212,()=>ChooseHero(id));
                var card=MenuStyle.Image(b.transform,"Selection",MenuStyle.Art("selected_outline"),0,0,206,212,true);heroSelections.Add(card);
                float portraitWidth=136;var tex=Portrait(id);var raw=Holo.Raw(MenuStyle.Rect(b.transform,"Portrait",14+(178-portraitWidth)*.5f,14,portraitWidth,136),tex,Color.white);raw.raycastTarget=false;
                var tx=MenuStyle.Text(b.transform,"Name",HeroLabel(id),10,151,186,46,27,MenuStyle.Paper,true);tx.alignment=TextAlignmentOptions.Center;
            }
            return root;
        }
        UnityEngine.UI.Image fidelityWeaponProfile,fidelityHint;TMP_Text fidelityHeroCaption;
        RectTransform BuildWeapons()
        {
            var root=Holo.Fill(canvas.transform,"Loadout");LivingEnvironmentView.Attach(root,true);var f=BlueUi.Frame(root,"LoadoutLayout");
            BackButton=BlueUi.Button(f,"Back","",42,20,98,82,Back,false,"icon_back");BackButton.transform.Find("Surface").GetComponent<UnityEngine.UI.Image>().sprite=BlueUi.Art("button_navy_blank");BlueUi.Image(f,"LoadoutHeadingPlate","wallet_pill",153,24,378,82,true);BlueUi.Text(f,"Heading","Loadout",175,20,334,84,57,Color.white);BlueUi.Wallet(f);
            
            var p=BlueUi.Panel(f,"WeaponsPanel",813,109,830,800).rectTransform;BlueUi.Text(p,"Heading","Starting weapon",38,18,650,70,51);BlueUi.Text(p,"Hint","Choose your weapon before deployment",40,90,748,43,28,BlueUi.Muted,true);
            for(int k=0;k<3;k++){int card=k;var b=BlueUi.Button(p,"WeaponCard"+k,"",37+k*252,156,240,191,()=>PreviewWeaponCard(card));b.transform.Find("Surface").GetComponent<UnityEngine.UI.Image>().sprite=BlueUi.Art("card_white");var sel=BlueUi.Image(b.transform,"Selection","selected_outline",-4,-5,248,201,true);BlueUi.Image(b.transform,"EquippedCheck","check_green",183,130,47,47);var im=BlueUi.Image(b.transform,"Icon",FidelityWeapon("MINIGUN"),12,22,214,117);var label=BlueUi.Text(b.transform,"Name","",8,143,222,39,27);label.alignment=TextAlignmentOptions.Center;weaponCards.Add((-1,b,sel,label));}
            BlueUi.Panel(p,"WeaponDetails",37,377,756,254,true);weaponName=BlueUi.Text(p,"WeaponName","",62,384,610,56,42);weaponLevel=BlueUi.Text(p,"WeaponLevel","",648,389,118,46,26,BlueUi.Muted);weaponLevel.alignment=TextAlignmentOptions.Right;
            fidelityWeaponProfile=BlueUi.Image(p,"WeaponProfile",FidelityWeapon("MINIGUN"),84,441,648,139);weaponBody=BlueUi.Text(p,"BlueprintStatus","",61,581,706,45,27,BlueUi.Muted,true);weaponBody.alignment=TextAlignmentOptions.Center;
            fidelityHint=BlueUi.Image(p,"HiddenHint",(Sprite)null,0,0,1,1);fidelityHint.gameObject.SetActive(false);
            equipButton=BlueUi.Button(p,"Equip","Equip",37,663,756,110,Equip,true,null,52);equipText=equipButton.transform.Find("Label").GetComponent<TMP_Text>();return root;
        }
        static Texture2D Portrait(string h)=>Prefs.HeroPortrait(h);
        static string HeroLabel(string h)=>h=="engineer"?"ENGINEER":"HEAVY";
        public void EnterHangar(){ShowHome();RefreshVisibility();}
        public void ShowHome(){screen=Screen.Home;Refresh();}
        public void OpenHeroes(){if(!CanOpen())return;screen=Screen.Heroes;RefreshVisibility();Refresh();}
        public void OpenWeapons(){if(!CanOpen())return;screen=Screen.Weapons;weaponPage=0;previewWeapon=Prefs.Vanguard?0:Prefs.Scout?System.Array.FindIndex(game.cfg.weapons,w=>w.name=="NEEDLER"):Prefs.WeaponIndex(game.cfg.weapons);RefreshVisibility();Refresh();}
        bool CanOpen(){if(Progress.D.runs==0){Sfx.Deny();owner.ShowToast("UNLOCKS AFTER YOUR FIRST DROP");return false;}return true;}
        public void Back(){if(screen==Screen.Home){owner.ReturnToTitle();}else{screen=Screen.Home;RefreshVisibility();Refresh();}Sfx.Close();}
        public void Deploy(){if(Visible&&screen==Screen.Home&&!owner.Busy)deploy?.Invoke();}
        void ChooseHero(string h){owner.PickHero(h);model.Refresh();Refresh();}
        void HeroLevel(){if(Meta.LevelHero(Meta.HeroId)){Sfx.Toggle();owner.RefreshStyledLabels();Refresh();}else{Sfx.Deny();owner.ShowToast("LEVEL REQUIREMENTS NOT MET");}}
        void HeroStar(){if(Meta.StarHero(Meta.HeroId)){Sfx.Toggle();owner.RefreshStyledLabels();Refresh();}else{Sfx.Deny();owner.ShowToast("STAR REQUIREMENTS NOT MET");}}
        void OpenEquipment(int slot){owner.OpenSheet("gear");owner.Gear.ChooseFilter(slot);}
        bool RoleAllowed(int i){var n=game.cfg.weapons[i].name;return Prefs.Vanguard?i==0||n=="THERMAL"||n=="RAIL":Prefs.Scout?n=="NEEDLER":n!="THERMAL"&&n!="RAIL"&&n!="NEEDLER";}
        int[] WeaponIndices()=>Enumerable.Range(0,game.cfg.weapons.Length).Where(RoleAllowed).ToArray();
        void PreviewWeaponCard(int card){if(card>=weaponCards.Count||weaponCards[card].index<0)return;previewWeapon=weaponCards[card].index;RefreshWeapons();}
        string WeaponName(int i)=>Prefs.Vanguard&&i==0?"MINIGUN":game.cfg.weapons[i].name=="NEEDLER"?"TWIN NEEDLERS":game.cfg.weapons[i].name;
        void Equip(){if(previewWeapon<0||!game.WeaponAllowed(previewWeapon)){Sfx.Deny();return;}owner.ChooseStyledWeapon(previewWeapon);RefreshWeapons();}
        static Sprite FidelityWeapon(string name)=>MenuFidelityArt.Art(name=="MINIGUN"?"Derived/minigun_art_alpha":name=="THERMAL"?"Derived/thermal_profile_v1":"Derived/rail_profile_v1");
        void RefreshWeapons()
        {
            var ids=WeaponIndices();foreach(var n in new[]{"Previous","Next"}){var r=weapons.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==n);if(r!=null)r.gameObject.SetActive(ids.Length>6);}weaponPage=Mathf.Clamp(weaponPage,0,Mathf.Max(0,(ids.Length-1)/3));if(!ids.Contains(previewWeapon))previewWeapon=ids.Length>0?ids[0]:-1;
            for(int k=0;k<weaponCards.Count;k++){var row=weaponCards[k];int n=weaponPage*3+k,index=n<ids.Length?ids[n]:-1;row.index=index;row.button.gameObject.SetActive(index>=0);if(index>=0){row.label.text=WeaponName(index);((RectTransform)row.label.transform).sizeDelta=new Vector2(index==Prefs.WeaponIndex(game.cfg.weapons)?180:222,39);var icon=row.button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>();icon.sprite=FidelityWeapon(WeaponName(index));icon.color=Color.white;row.selected.gameObject.SetActive(index==previewWeapon);row.button.transform.Find("EquippedCheck").gameObject.SetActive(index==Prefs.WeaponIndex(game.cfg.weapons));row.button.targetGraphic.color=Meta.WeaponOwned(game.cfg.weapons[index].name)?Color.white:new Color(.5f,.58f,.7f);}weaponCards[k]=row;}
            if(previewWeapon<0)return;var w=game.cfg.weapons[previewWeapon];bool owned=game.WeaponAllowed(previewWeapon);int progressIndex=Prefs.Vanguard&&previewWeapon==0?3:Progress.WeaponIndexFor(w.name);
            weaponName.text=WeaponName(previewWeapon);fidelityWeaponProfile.sprite=FidelityWeapon(WeaponName(previewWeapon));fidelityHint.gameObject.SetActive(false);weaponBody.gameObject.SetActive(true);weaponLevel.text=owned?"LV "+Progress.Level(progressIndex):"LOCKED";weaponBody.text=owned?(Prefs.WeaponIndex(game.cfg.weapons)==previewWeapon?"Current starting weapon":"Ready to equip"): $"Blueprint {Meta.Fragments(w.name)}/10 - {Meta.BlueprintZones(w.name)}";equipButton.interactable=owned;equipText.text=owned?"Equip":"Locked";
        }
        void Refresh()
        {
            if(homeHero==null)return;home.gameObject.SetActive(screen==Screen.Home);heroes.gameObject.SetActive(screen==Screen.Heroes);weapons.gameObject.SetActive(screen==Screen.Weapons);
            homeHero.text=Prefs.HeroName;heroName.text=HeroLabel(Prefs.Hero);heroLevel.text=$"LV {Meta.HeroLevel(Meta.HeroId)}  ·  {Meta.HeroStars(Meta.HeroId)} STAR{(Meta.HeroStars(Meta.HeroId)==1?"":"S")}";
            abilityName.text=Abilities.Name;abilityBody.text=$"{Abilities.BaseCooldown:0.#} S COOLDOWN · TALENTS";var skill=heroes.GetComponentsInChildren<UnityEngine.UI.Image>(true).FirstOrDefault(t=>t.name=="SkillIcon");if(skill!=null)skill.sprite=RoundedHudStyle.Art("icon_"+RoundedHudStyle.SkillIcon+"_cyan");
            int level=Meta.HeroLevel(Meta.HeroId),stars=Meta.HeroStars(Meta.HeroId);heroCost.text=level>=Meta.HeroCap(Meta.HeroId)?"LEVEL CAP":$"{Meta.HeroLevelCost(level):N0} CR";heroStarCost.text=stars>=5?"MAX STARS":$"{Meta.Shards(Meta.HeroId)} / {Meta.StarCost(Meta.HeroId)} SHARDS";
            heroLevelButton.interactable=stars>0&&level<Meta.HeroCap(Meta.HeroId)&&Progress.D.credits>=Meta.HeroLevelCost(level);heroStarButton.interactable=Meta.StarCost(Meta.HeroId)>0&&Meta.Shards(Meta.HeroId)>=Meta.StarCost(Meta.HeroId);heroSelectButton.interactable=true;
            string[] hs={"vanguard","engineer"};for(int i=0;i<hs.Length;i++)heroSelections[i].gameObject.SetActive(hs[i]==Prefs.Hero);
            foreach(var (s,t) in slotLabels){var eq=Progress.Equipped(s);t.text=Progress.Slots[s].ToUpperInvariant()+(eq==null?"":$" {eq.level}");}
            RefreshWeapons();
        }
        void RefreshVisibility()
        {
            bool show=owner.MenuTitleEntered&&owner.Visible&&game.state==Game.State.Menu&&!owner.LegacySheetOpen&&!owner.Busy&&(MapSelect.I==null||!MapSelect.I.Open||MapSelect.I.WaitingForMenu);
            bool was=visible;visible=show;canvas.enabled=show;if(show){if(legacyRoot.gameObject.activeSelf)legacyRoot.gameObject.SetActive(false);}else if(was&&owner.Visible&&!owner.TitleVisible)legacyRoot.gameObject.SetActive(true);
        }
        public void Notice(string value){notice.text=value.ToUpperInvariant();noticeUntil=Time.unscaledTime+2.8f;}
        void LateUpdate(){if(owner==null)return;noticeCanvas.enabled=Time.unscaledTime<noticeUntil&&game.state==Game.State.Menu;RefreshVisibility();if(shownHero!=Prefs.Hero){shownHero=Prefs.Hero;model.Refresh();Refresh();}if(!visible)return;if(Input.GetKeyDown(KeyCode.Escape)&&!owner.LegacySheetOpen)Back();}
    }
    // Match the real preview soles to the painted platform through the background's cover UVs.
    // The safe-area UI frame and full-screen backdrop scale differently on wider phones.
    [DefaultExecutionOrder(100)]
    public sealed class MenuHeroPlatformAnchor:MonoBehaviour
    {
        UnityEngine.UI.RawImage background;RectTransform avatar;MenuModelPreview preview;
        public void Init(UnityEngine.UI.RawImage image,RectTransform target,MenuModelPreview model){background=image;avatar=target;preview=model;}
        void LateUpdate()
        {
            if(background==null||avatar==null||preview==null)return;
            var bg=background.rectTransform;var uv=background.uvRect;
            if(uv.width<=0||uv.height<=0)return;
            var point=new Vector2((515f/1672-uv.x)/uv.width,((1-610f/941)-uv.y)/uv.height);
            var onPlatform=bg.TransformPoint(new Vector3((point.x-bg.pivot.x)*bg.rect.width,(point.y-bg.pivot.y)*bg.rect.height,0));
            var sole=avatar.TransformPoint(new Vector3((.5f-avatar.pivot.x)*avatar.rect.width,(preview.FootV-avatar.pivot.y)*avatar.rect.height,0));
            avatar.position+=onPlatform-sole;
        }
    }

}
