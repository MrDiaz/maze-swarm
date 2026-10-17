#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Maze.EditorTools
{
    [InitializeOnLoad] public static class TitleScreenProof
    {
        const string Key="Maze.TitleScreenProof.Active";
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Out=>Environment.GetEnvironmentVariable("TITLE_PROOF_OUT")??"Logs/TitleScreenProof";
        static Game G=>Object.FindAnyObjectByType<Game>();
        static TitleScreenView View=>G.menu.GetComponent<TitleScreenView>();
        [Serializable] class Report {public string utc,unityVersion,savedBefore,savedAfter,staticImageHash;public List<string> tests=new(),errors=new(),warnings=new(),captures=new();public List<BoundsRow> bounds=new();}
        [Serializable] class BoundsRow {public string screen,objectName;public float x,y,width,height;}
        static Report report;static List<Action> steps;static int step,frame,width,height;static double start=-1,next;
        static bool prepared,done,originalSim,originalPause;static float originalVolume,originalTime;static string originalProgress;static int originalTier;static SectorDef originalSector;static Game.Goal[] originalPlan;
        static Camera ui;static RenderTexture target;static Rect safePixels;static Vector2 playPosition,glyphPosition;static string staticTransforms;
        static TitleScreenProof(){if(SessionState.GetBool(Key,false))Hook();}
        public static void Run(){SessionState.SetBool(Key,true);Hook();EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");EditorApplication.EnterPlaymode();}
        static void Hook(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Logged;Application.logMessageReceived+=Logged;}
        static void Logged(string msg,string stack,LogType type){if(done||!SessionState.GetBool(Key,false))return;report??=new Report();if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.errors.Add(msg+"\n"+stack);else if(type==LogType.Warning)report.warnings.Add(msg);}
        static void Check(bool ok,string text){report.tests.Add((ok?"PASS ":"FAIL ")+text);if(!ok)report.errors.Add(text);}
        static void Wait(double s=.3){next=EditorApplication.timeSinceStartup+s;frame=Time.frameCount+5;}
        static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Flags).GetValue(o);
        static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
        static string Saved()=>Hash(System.Text.Encoding.UTF8.GetBytes(PlayerPrefs.GetString("ms.progress","")));
        static void Tick()
        {
            if(done)return;if(start<0)start=EditorApplication.timeSinceStartup;if(EditorApplication.timeSinceStartup-start>150){Finish("Timed out");return;}
            if(!EditorApplication.isPlaying||G==null||G.menu==null||G.player==null||View==null||Camera.main==null)return;
            try{if(!prepared){Prepare();return;}if(EditorApplication.timeSinceStartup<next||Time.frameCount<frame)return;if(step>=steps.Count){Finish();return;}steps[step++]();}catch(Exception e){Finish(e.ToString());}
        }
        static void Prepare()
        {
            report??=new Report();report.utc=DateTime.UtcNow.ToString("O");report.unityVersion=Application.unityVersion;report.savedBefore=Saved();originalProgress=JsonUtility.ToJson(Progress.D);originalSim=Progress.Sim;originalPause=AudioListener.pause;originalVolume=AudioListener.volume;originalTime=Time.timeScale;originalTier=Game.PendingTier;originalSector=Game.PendingSector;originalPlan=Game.PendingPlan;
            Progress.Sim=true;Progress.UseTemp(new SaveData{runs=5});AudioListener.volume=0;AudioListener.pause=true;Time.timeScale=1;Directory.CreateDirectory(Out);prepared=true;
            steps=new List<Action>{
                ()=>{Resize(1672,941);G.menu.Show();Wait(.6);},
                ()=>{Inspect("reference");report.staticImageHash=Shot("01_title_reference");staticTransforms=Transforms();Wait(2);},
                ()=>{Check(Transforms()==staticTransforms,"logo, letters and Play transforms stay static");Check(Shot("02_title_static_after_wait")==report.staticImageHash,"title pixels stay static across frames");Resize(1334,750);Wait();},
                ()=>{Inspect("se");Shot("03_title_se");Resize(2556,1179);Wait();},
                ()=>{Inspect("pro");Shot("04_title_pro");Resize(2796,1290);Wait();},
                ()=>{Inspect("max");Shot("05_title_max");Resize(2556,1179,true);Wait();},
                ()=>{Inspect("asymmetric");Shot("06_title_asymmetric");playPosition=View.PlayRect.anchoredPosition;View.PlayRect.anchoredPosition+=new Vector2(60,-36);glyphPosition=((RectTransform)View.Logo.GetChild(0)).anchoredPosition;((RectTransform)View.Logo.GetChild(0)).anchoredPosition+=new Vector2(0,18);Wait();},
                ()=>{Check(View.PlayRect.anchoredPosition==playPosition+new Vector2(60,-36),"Play has independently adjustable placement");Check(((RectTransform)View.Logo.GetChild(0)).anchoredPosition==glyphPosition+new Vector2(0,18),"single glyph placement remains independent");View.PlayRect.anchoredPosition=playPosition;((RectTransform)View.Logo.GetChild(0)).anchoredPosition=glyphPosition;Resize(2556,1179);Wait();},
                ()=>{Raycasts();ExecuteEvents.Execute(View.PlayButton.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerDownHandler);Wait(.15);},
                ()=>{var color=View.PlayButton.targetGraphic.canvasRenderer.GetColor();Check(color.r<.95f&&color.r>.8f,"Play pressed tint uses button feedback");Shot("07_title_play_pressed");ExecuteEvents.Execute(View.PlayButton.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerUpHandler);ExecuteEvents.Execute(View.PlayButton.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);Wait(.8);},
                ()=>{Check(MapSelect.I.Open&&!View.Visible&&G.state==Game.State.Menu,"real Play click opens existing sector select");Shot("08_play_destination_sector_select");MapSelect.I.Hide();Wait();},
                ()=>{Check(View.Visible,"sector-select Back returns to title");Shot("09_return_to_title");G.menu.OpenSheet("settings");Wait(.8);},
                ()=>{Check(G.menu.SheetOpen&&!View.Visible,"existing settings sheet remains functional off-title");Shot("10_existing_settings_sheet");G.menu.CloseSheet();Wait(.8);},
                ()=>{Check(View.Visible&&!G.menu.SheetOpen,"closing settings restores title");G.menu.Deploy();Wait(.8);},
                ()=>{Check(MapSelect.I.Open&&!View.Visible&&!G.menu.Dropping,"menu submit uses the same Play destination");MapSelect.I.OpenBriefing();Wait(.8);},
                ()=>{Check(MapSelect.I.Open&&!View.Visible,"existing briefing remains reachable");Shot("11_existing_briefing");MapSelect.I.LaunchDrop();Wait(6);},
                ()=>{Check(G.state==Game.State.Playing&&G.run&&G.floor==1&&!View.Visible,"existing drop reaches actual first-floor gameplay");G.godMode=true;Shot("12_gameplay_after_play");G.ToMenu();Wait(.6);},
                ()=>{Check(View.Visible&&G.state==Game.State.Menu,"return from gameplay restores title");Inspect("returned");Shot("13_title_after_gameplay");}
            };
        }
        static void Resize(int w,int h,bool asymmetric=false)
        {
            width=w;height=h;if(target!=null){Camera.main.targetTexture=null;if(ui!=null)ui.targetTexture=null;target.Release();Object.DestroyImmediate(target);}
            target=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32){antiAliasing=1,name="TitleScreenProof"};target.Create();Camera.main.targetTexture=target;
            if(ui==null){ui=new GameObject("TitleScreenProofUiCamera").AddComponent<Camera>();ui.cullingMask=1<<5;ui.orthographic=true;ui.nearClipPlane=.1f;ui.farClipPlane=200;ui.transform.position=new Vector3(0,-2000,0);ui.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Overlay;Camera.main.GetUniversalAdditionalCameraData().cameraStack.Add(ui);}
            ui.targetTexture=target;float l=w/(float)h>2?177:0,r=w/(float)h>2?(asymmetric?132:177):0,b=w/(float)h>2?63:0;safePixels=new Rect(l,b,w-l-r,h-b);SafeArea.Override=new Rect(l/w,b/h,1-(l+r)/w,1-b/h);
            foreach(var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if(!c.isRootCanvas)continue;int order=c.sortingOrder;c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=ui;c.planeDistance=50;c.sortingOrder=order;
                var sc=c.GetComponent<UnityEngine.UI.CanvasScaler>();if(sc!=null&&c.name!="TitleScreenCanvas"){sc.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;sc.scaleFactor=h/1080f;}
            }
        }
        static Rect Bounds(RectTransform rt){var cs=new Vector3[4];rt.GetWorldCorners(cs);var a=RectTransformUtility.WorldToScreenPoint(ui,cs[0]);var b=RectTransformUtility.WorldToScreenPoint(ui,cs[2]);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);}
        static string Transforms()=>string.Join(";",View.Logo.Cast<Transform>().Select(t=>t.name+":"+((RectTransform)t).anchoredPosition))+"|"+View.Logo.anchoredPosition+"|"+View.PlayRect.anchoredPosition;
        static void Inspect(string name)
        {
            Canvas.ForceUpdateCanvases();Check(View.Visible,name+" title visible");Check(View.Logo.childCount==9,name+" nine independent letters");Check(string.Join("",View.Logo.Cast<Transform>().Select(t=>t.name.Split('_').Last()))=="MAZESWARM",name+" exact MAZE SWARM spelling");
            var scaler=Field<Canvas>(View,"canvas").GetComponent<UnityEngine.UI.CanvasScaler>();Check(scaler.uiScaleMode==UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize&&scaler.referenceResolution==new Vector2(1672,941),name+" production title scaler preserved");
            Check(!Field<RectTransform>(G.menu,"shakeRoot").gameObject.activeSelf,name+" legacy menu hidden");Check(!Field<HoloButton>(G.menu,"gearBtn").gameObject.activeInHierarchy,name+" no active settings icon");
            Check(View.GetComponentsInChildren<UnityEngine.UI.Text>(false).Length==0,name+" no duplicate baked-label text objects");Check(View.GetComponentsInChildren<Animator>(true).Length==0&&View.GetComponentsInChildren<Animation>(true).Length==0,name+" no entrance animation components");
            var roots=View.Logo.Cast<Transform>().Select(t=>(RectTransform)t).Concat(new[]{View.PlayRect}).ToArray();
            foreach(var rt in roots){var r=Bounds(rt);report.bounds.Add(new BoundsRow{screen=name+" "+width+"x"+height,objectName=rt.name,x=r.x,y=r.y,width=r.width,height=r.height});Check(r.xMin>=safePixels.xMin-1&&r.xMax<=safePixels.xMax+1&&r.yMin>=safePixels.yMin-1&&r.yMax<=safePixels.yMax+1,name+" "+rt.name+" within safe area");}
            Check(!Bounds(View.Logo).Overlaps(Bounds(View.PlayRect)),name+" logo and Play do not overlap");Check(Bounds(View.PlayRect).height/height*375>=44,name+" Play target at least44pt at375pt landscape height");
            var m=Field<TitleScreenManifest>(View,"manifest");foreach(var a in m.glyphs){var image=View.Logo.Find(a.id).GetComponent<UnityEngine.UI.Image>();Check(image.sprite.texture.width==(int)a.rect_top_left[2]&&image.sprite.texture.height==(int)a.rect_top_left[3],name+" "+a.id+" original import dimensions");Check(!image.raycastTarget,name+" "+a.id+" does not block input");}
            Check(Object.FindObjectsByType<EventSystem>().Length==1,name+" one EventSystem");
        }
        static void Raycasts()
        {
            var ev=new PointerEventData(EventSystem.current){position=Bounds(View.PlayRect).center};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(ev,hits);Check(hits.Count>0&&hits[0].gameObject==View.PlayButton.gameObject,"actual raycaster selects Play at its centre");hits.Clear();ev.position=new Vector2(30,30);EventSystem.current.RaycastAll(ev,hits);Check(hits.Count>0&&hits[0].gameObject.name=="Background","background blocks underlying menu outside Play");
        }
        static string Shot(string name)
        {
            var old=RenderTexture.active;RenderTexture.active=target;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();RenderTexture.active=old;var png=tex.EncodeToPNG();string file=Path.Combine(Out,name+".png");File.WriteAllBytes(file,png);Object.DestroyImmediate(tex);report.captures.Add(file);Debug.Log("TitleScreenProof capture "+name);return Hash(png);
        }
        static void Finish(string error=null)
        {
            if(done)return;done=true;report??=new Report();if(error!=null)report.errors.Add(error);
            try{if(prepared){report.savedAfter=Saved();Check(report.savedBefore==report.savedAfter,"saved progress key unchanged");Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalProgress));Progress.Sim=originalSim;AudioListener.pause=originalPause;AudioListener.volume=originalVolume;Time.timeScale=originalTime;Game.PendingTier=originalTier;Game.PendingSector=originalSector;Game.PendingPlan=originalPlan;SafeArea.Override=null;}Directory.CreateDirectory(Out);File.WriteAllText(Path.Combine(Out,"report.json"),JsonUtility.ToJson(report,true));}catch(Exception e){Debug.LogError(e);}
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Logged;Debug.Log("TitleScreenProof complete: "+report.tests.Count+" checks, "+report.errors.Count+" errors");EditorApplication.Exit(report.errors.Count==0?0:1);
        }
    }
}
#endif
