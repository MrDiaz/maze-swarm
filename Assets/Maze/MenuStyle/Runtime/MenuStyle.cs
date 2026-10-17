using System;
using System.Linq;
using UnityEngine;
using TMPro;
namespace Maze
{
    public static class MenuStyle
    {
        public const float Width=1672,Height=941,Padding=36,Gap=20;
        public static readonly Color Paper=new Color32(246,248,255,255),Cyan=new Color32(62,230,255,255),Gold=new Color32(255,206,45,255),Navy=new Color32(5,18,44,255),Muted=new Color32(165,180,207,255);
        public static Sprite Art(string name)=>Resources.Load<Sprite>("MenuStyle/Kit/"+name);
        public static RectTransform Rect(Transform p,string name,float x,float y,float w,float h)
        {
            var r=Holo.Rect(p,name);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
        }
        public static RectTransform Frame(Transform parent,string name)
        {
            var safe=Holo.SafeRoot(parent);var r=Holo.Rect(safe,name,Vector2.one*.5f,Vector2.zero,new Vector2(Width,Height));r.gameObject.AddComponent<MenuFrameFit>().Init(safe);return r;
        }
        public static UnityEngine.UI.Image Image(Transform parent,string name,Sprite sprite,float x,float y,float w,float h,bool sliced=false)
        {
            var i=Holo.Img(Rect(parent,name,x,y,w,h),sprite,Color.white,sliced);return i;
        }
        public static UnityEngine.UI.Image Panel(Transform parent,string name,float x,float y,float w,float h,string art="panel_main")=>Image(parent,name,Art(art),x,y,w,h,true);
        public static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,float size=32,Color? color=null,bool bold=false)
        {
            var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
            t.font=Resources.Load<TMP_FontAsset>(bold?"UI/ResultsFonts/RobotoCondensedBoldSDF":"UI/ResultsFonts/RobotoCondensedRegularSDF");
            if(t.font==null)t.font=Resources.Load<TMP_FontAsset>("UI/ResultsFonts/RajdhaniResultsSDF");
            t.text=value;t.fontSize=t.fontSizeMax=size;t.fontSizeMin=Mathf.Max(18,size-6);t.enableAutoSizing=true;t.color=color??Paper;t.alignment=TextAlignmentOptions.MidlineLeft;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Truncate;t.raycastTarget=false;return t;
        }
        public static UnityEngine.UI.Button Button(Transform parent,string name,string label,float x,float y,float w,float h,Action action,bool primary=false,string icon=null)
        {
            float px=(Mathf.Max(124,w)-w)*.5f,py=(Mathf.Max(124,h)-h)*.5f;var rt=Rect(parent,name,x-px,y-py,Mathf.Max(124,w),Mathf.Max(124,h));var bg=Holo.Img(Holo.Fill(rt,"Surface"),Art(primary?"button_yellow_blank":"button_navy_blank"),Color.white,true,true);
            var b=rt.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=bg;b.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            var colours=b.colors;colours.normalColor=Color.white;colours.highlightedColor=new Color(.92f,.97f,1);colours.pressedColor=new Color(.78f,.83f,.92f);colours.disabledColor=new Color(.45f,.49f,.58f);colours.fadeDuration=0;b.colors=colours;b.onClick.AddListener(()=>action?.Invoke());
            float left=icon==null?20:82;if(icon!=null)Image(rt,"Icon",Art("icon_"+icon+"_"+(primary?"navy":"ivory")),24+px,(h-48)*.5f+py,48,48).preserveAspect=true;
            var tx=Text(rt,"Label",label,left+px,10+py,w-left-20,h-20,36,primary?Navy:Paper,true);tx.alignment=TextAlignmentOptions.Center;return b;
        }
        public static void Background(Transform parent,string name,string resource,float dim=0)
        {
            var raw=Holo.Raw(Holo.Fill(parent,name),Resources.Load<Texture2D>("MenuStyle/Backgrounds/"+resource),Color.white);raw.raycastTarget=true;
            if(raw.texture!=null)raw.gameObject.AddComponent<CoverUv>().aspect=(float)raw.texture.width/raw.texture.height;
            if(dim>0)Holo.Img(Holo.Fill(parent,name+"Shade"),Holo.WhiteSprite,new Color(Navy.r,Navy.g,Navy.b,dim));
        }
        public static UnityEngine.UI.RawImage Portrait(Transform parent,string name,float x,float y,float w,float h)
        {
            var raw=Holo.Raw(Rect(parent,name,x,y,w,h),null,Color.white);raw.raycastTarget=false;return raw;
        }
        public static Sprite Objective(Game.Goal goal)
        {
            var n=Game.GoalNames[(int)goal].ToLowerInvariant();return Art("icon_objective_"+n+"_cyan")??Holo.Art("goal_"+n+"_mono")??Holo.Icon(Game.GoalIcons[(int)goal]);
        }
        public static Texture2D Zone(MapSelect.Sector s)
        {
            return Resources.Load<Texture2D>("MenuStyle/ZoneArt/"+(s.def?.id??"evac-deck"))??Resources.Load<Texture2D>("UI/Sectors/"+s.art);
        }
        public static void SkinPanel(RectTransform panel){foreach(Transform child in panel){if(new[]{"Grid","Edge","Brackets","Accent","Glow"}.Contains(child.name))child.gameObject.SetActive(false);else if(child.name=="Fill"||child.name=="Glass"){var im=child.GetComponent<UnityEngine.UI.Image>();if(im!=null){im.sprite=Art("panel_main");im.color=Color.white;im.type=UnityEngine.UI.Image.Type.Sliced;}}}}
        public static Sprite WeaponIcon(string name)=>Holo.Art(name=="SHOTGUN"?"card_multishot":name=="SMG"||name=="TWIN NEEDLERS"||name=="NEEDLER"?"card_firerate":name=="FRAG"?"card_grenade":name=="LANCE"?"evo_railgun":name=="THERMAL"?"card_trail":name=="RAIL"?"evo_railgun":"card_heavy")??Holo.Icon("rifle");
        public static string HeroShort(string id)=>"VANGUARD";
    }
    public sealed class MenuFrameFit:MonoBehaviour
    {
        RectTransform safe,frame;Vector2 last;
        public void Init(RectTransform s){safe=s;frame=(RectTransform)transform;Fit();}
        void LateUpdate()=>Fit();
        void Fit(){if(safe==null)return;var size=safe.rect.size;if(size==last||size.x<=0||size.y<=0)return;last=size;frame.localScale=Vector3.one*Mathf.Min(size.x/MenuStyle.Width,size.y/MenuStyle.Height);}
    }
}
