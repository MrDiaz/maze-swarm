using System;using UnityEngine;using TMPro;
namespace Maze {
 public static class MenuFidelityArt {
  public static Sprite Art(string path)=>Resources.Load<Sprite>("MenuFidelity/"+path);
  public static UnityEngine.UI.Image Image(Transform p,string n,string path,float x,float y,float w,float h){var i=Holo.Img(MenuStyle.Rect(p,n,x,y,w,h),Art(path),Color.white);i.raycastTarget=false;return i;}
  public static UnityEngine.UI.Button Button(Transform p,string n,string path,float x,float y,float w,float h,Action action){float tw=Mathf.Max(124,w),th=Mathf.Max(124,h),px=(tw-w)*.5f,py=(th-h)*.5f;var r=MenuStyle.Rect(p,n,x-px,y-py,tw,th);Holo.Img(Holo.Fill(r,"TouchTarget"),Holo.WhiteSprite,Color.clear,false,true);var surface=Image(r,"OriginalArtwork",path,px,py,w,h);surface.raycastTarget=true;var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=surface;b.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};var c=b.colors;c.normalColor=Color.white;c.highlightedColor=Color.white;c.pressedColor=new Color(.85f,.9f,.95f);c.fadeDuration=0;b.colors=c;b.onClick.AddListener(()=>action?.Invoke());return b;}
  public static TMP_Text Text(Transform p,string n,string value,float x,float y,float w,float h,float size,bool bold=true){var t=MenuStyle.Text(p,n,value,x,y,w,h,size,MenuStyle.Paper,bold);t.font=Resources.Load<TMP_FontAsset>("MenuFidelity/Fonts/"+(bold?"Display":"Body"));t.fontSharedMaterial=t.font.material;t.enableAutoSizing=false;t.fontSize=size;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Overflow;return t;}
 }
}
