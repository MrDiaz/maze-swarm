using System;
using UnityEngine;

namespace Maze
{
    [Serializable] public sealed class TitleScreenManifest
    {
        [Serializable] public sealed class CanvasSpec { public float width, height; }
        [Serializable] public sealed class Layer
        {
            public string id, file, letter, word;
            public float[] rect_top_left, center_on_canvas, pivot_normalized;
        }
        public CanvasSpec canvas;
        public string background, background_resource, resource_root;
        public Layer[] glyphs, ui;
    }

    // Static title composition. Every glyph and the Play control retains its own transform.
    // No entrance animation, camera movement, or gameplay/progression state belongs here.
    public sealed class TitleScreenView : MonoBehaviour
    {
        MainMenu menu; Game game; Action enter;
        Canvas canvas; RectTransform legacyRoot, safe, designFrame, logo;
        UnityEngine.UI.RawImage background;
        UnityEngine.UI.Button play;
        TitleScreenManifest manifest;
        bool showing,entered;
        Vector2 previousCanvasSize = new Vector2(-1,-1), playSafeOffset;
        Rect previousSafeBounds = new Rect(-1,-1,-1,-1);
        public bool Entered => entered;
        public void ReturnToTitle(){entered=false;RefreshVisibility();}
        public bool Visible => canvas != null && canvas.enabled;
        public RectTransform Logo => logo;
        public RectTransform PlayRect => play == null ? null : (RectTransform)play.transform;
        public UnityEngine.UI.Button PlayButton => play;
        public RectTransform DesignFrame => designFrame;
        public float CoverScale => designFrame == null ? 0 : designFrame.localScale.x;
        public Vector2 PlaySafeOffsetNative => playSafeOffset;

        public void Init(MainMenu owner,Game g,RectTransform originalRoot,Action onPlay)
        {
            menu=owner;game=g;legacyRoot=originalRoot;enter=onPlay;
            var data=Resources.Load<TextAsset>("BlueUI/TitleArt/manifest");
            if(data==null) throw new InvalidOperationException("Title screen manifest is missing.");
            manifest=JsonUtility.FromJson<TitleScreenManifest>(data.text);
            if(manifest.canvas==null || manifest.canvas.width<=0 || manifest.canvas.height<=0 || manifest.glyphs==null || manifest.glyphs.Length!=9 || manifest.ui==null || manifest.ui.Length!=1) throw new InvalidOperationException("Title screen needs nine glyphs and one Play layer.");
            canvas=Holo.MakeCanvas(transform,"TitleScreenCanvas",11);
            var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.referenceResolution=new Vector2(manifest.canvas.width,manifest.canvas.height);scaler.matchWidthOrHeight=.5f;
            var backgroundResource=string.IsNullOrEmpty(manifest.background_resource)?"TitleScreen/"+WithoutExtension(manifest.background):manifest.background_resource;
            var scene=CohesiveZone.Enabled?CohesiveMenuArt.Hangar:Resources.Load<Texture2D>(backgroundResource);
            background=Holo.Raw(Holo.Fill(canvas.transform,"Background"),scene,Color.white);
            if(background.texture==null) throw new InvalidOperationException("Title screen background is missing: "+backgroundResource);
            background.raycastTarget=true; // an opaque input shield over the old menu
            background.gameObject.AddComponent<CoverUv>().aspect=(float)background.texture.width/background.texture.height;
            safe=Holo.SafeRoot(canvas.transform);
            // The foreground uses the same centered cover coordinates as the sole background.
            // Safe-area fitting here would rescale the Heavy independently and duplicate scenery.
            designFrame=Holo.Rect(canvas.transform,"DesignFrame",Vector2.one*.5f,Vector2.zero,new Vector2(manifest.canvas.width,manifest.canvas.height));
            float left=float.MaxValue,top=float.MaxValue,right=float.MinValue,bottom=float.MinValue;
            foreach(var a in manifest.glyphs){var r=a.rect_top_left;left=Mathf.Min(left,r[0]);top=Mathf.Min(top,r[1]);right=Mathf.Max(right,r[0]+r[2]);bottom=Mathf.Max(bottom,r[1]+r[3]);}
            var logoCentre=new Vector2((left+right)*.5f,(top+bottom)*.5f);
            logo=Holo.Rect(designFrame,"Logo",Vector2.one*.5f,ToCanvas(logoCentre),new Vector2(right-left,bottom-top));
            foreach(var a in manifest.glyphs)
            {
                var rt=Holo.Rect(logo,a.id,Vector2.one*.5f,new Vector2(a.center_on_canvas[0]-logoCentre.x,logoCentre.y-a.center_on_canvas[1]),new Vector2(a.rect_top_left[2],a.rect_top_left[3]));
                rt.pivot=Pivot(a);var img=Holo.Img(rt,Art(a.file),Color.white);img.preserveAspect=true;
            }
            var button=manifest.ui[0];
            // Transparent hit padding makes the artwork a comfortable phone target without rescaling its pixels.
            var pr=Holo.Rect(designFrame,"Play",Vector2.one*.5f,ToCanvas(new Vector2(button.center_on_canvas[0],button.center_on_canvas[1])),new Vector2(button.rect_top_left[2]+20,button.rect_top_left[3]+20));
            pr.pivot=Pivot(button);Holo.Img(pr,Holo.WhiteSprite,Color.clear,false,true);
            var artwork=Holo.Img(Holo.Rect(pr,"Artwork",Vector2.one*.5f,Vector2.zero,new Vector2(button.rect_top_left[2],button.rect_top_left[3])),Art(button.file),Color.white);
            artwork.preserveAspect=true;
            play=pr.gameObject.AddComponent<UnityEngine.UI.Button>();play.targetGraphic=artwork;play.transition=UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors=play.colors;colors.normalColor=Color.white;colors.highlightedColor=Color.white;colors.selectedColor=Color.white;colors.pressedColor=new Color32(224,224,224,255);colors.disabledColor=new Color32(153,153,153,255);colors.fadeDuration=0;play.colors=colors;
            play.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};play.onClick.AddListener(Play);
            RefreshVisibility();Fit();
        }
        static string WithoutExtension(string path)=>path.Substring(0,path.LastIndexOf('.'));
        static Vector2 Pivot(TitleScreenManifest.Layer a)=>new Vector2(a.pivot_normalized[0],a.pivot_normalized[1]);
        Sprite Art(string path)
        {
            var sprite=Resources.Load<Sprite>((string.IsNullOrEmpty(manifest.resource_root)?"TitleScreen":manifest.resource_root)+"/"+WithoutExtension(path));
            if(sprite==null) throw new InvalidOperationException("Title layer is missing: "+path);return sprite;
        }
        Vector2 ToCanvas(Vector2 topLeftPoint)=>new Vector2(topLeftPoint.x-manifest.canvas.width*.5f,manifest.canvas.height*.5f-topLeftPoint.y);
        public void Play()
        {
            if(!Visible || menu.Busy || MapSelect.I==null || MapSelect.I.Open) return;
            entered=true;SetShowing(false);enter?.Invoke();
        }
        void LateUpdate(){RefreshVisibility();if(showing)Fit();}
        void RefreshVisibility()
        {
            if(canvas==null)return;
            SetShowing(!entered && menu.Visible && game.state==Game.State.Menu && !menu.SheetOpen && !menu.Busy && (MapSelect.I==null || !MapSelect.I.Open));
        }
        void SetShowing(bool on)
        {
            bool was=showing;showing=on;canvas.enabled=on;
            if(on){if(legacyRoot.gameObject.activeSelf)legacyRoot.gameObject.SetActive(false);}
            else if(was && menu.Visible && (menu.Presentation==null || !menu.Presentation.Visible))legacyRoot.gameObject.SetActive(true);
        }
        void Fit()
        {
            var canvasRect=(RectTransform)canvas.transform;
            var size=canvasRect.rect.size;
            var safeBounds=RelativeBounds(safe,canvasRect);
            if(size.x<=0 || size.y<=0 || (size==previousCanvasSize && safeBounds==previousSafeBounds))return;
            previousCanvasSize=size;previousSafeBounds=safeBounds;
            float scale=Mathf.Max(size.x/manifest.canvas.width,size.y/manifest.canvas.height);
            designFrame.localScale=Vector3.one*scale;
            // Only Play may depart from cover coordinates. Clamp its padded hit target, not
            // merely the visible yellow backing, and remove the former offset before refitting.
            // This keeps the native full-safe1672x941 composition unchanged and avoids drift.
            var pr=PlayRect;
            pr.anchoredPosition-=playSafeOffset;
            playSafeOffset=Vector2.zero;
            var allowed=RelativeBounds(safe,designFrame);
            var current=RelativeBounds(pr,designFrame);
            float dx=current.width>allowed.width?allowed.center.x-current.center.x:
                Mathf.Clamp(current.center.x,allowed.xMin+current.width*.5f,allowed.xMax-current.width*.5f)-current.center.x;
            float dy=current.height>allowed.height?allowed.center.y-current.center.y:
                Mathf.Clamp(current.center.y,allowed.yMin+current.height*.5f,allowed.yMax-current.height*.5f)-current.center.y;
            playSafeOffset=new Vector2(dx,dy);
            pr.anchoredPosition+=playSafeOffset;
        }
        static Rect RelativeBounds(RectTransform target,RectTransform relativeTo)
        {
            var corners=new Vector3[4];target.GetWorldCorners(corners);
            var min=new Vector2(float.MaxValue,float.MaxValue);var max=new Vector2(float.MinValue,float.MinValue);
            foreach(var corner in corners)
            {
                var point=(Vector2)relativeTo.InverseTransformPoint(corner);
                min=Vector2.Min(min,point);max=Vector2.Max(max,point);
            }
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        void OnDestroy(){if(legacyRoot!=null && menu!=null && menu.Visible)legacyRoot.gameObject.SetActive(true);}
    }
}
