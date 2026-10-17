#if UNITY_EDITOR
using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using System.Security.Cryptography;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.EventSystems;using UnityEngine.Rendering.Universal;using Object=UnityEngine.Object;
namespace Maze.EditorTools {
[InitializeOnLoad]public static class StyledMenuProof {
const string Key="Maze.StyledMenuProof.Active";const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
static string Out=>Environment.GetEnvironmentVariable("MENU_PROOF_OUT")??"Logs/StyledMenuProof";static Game G=>Object.FindAnyObjectByType<Game>();static MenuPresentation P=>G.menu.Presentation;static TitleScreenView Title=>G.menu.GetComponent<TitleScreenView>();
[Serializable]class Report{public string utc,savedBefore,savedAfter;public List<string>tests=new(),errors=new(),warnings=new(),captures=new();}static Report report;static List<Action>steps;static int step,frame,width,height;static double start=-1,next;static bool prepared,done,oldSim;static string originalProgress,oldHeroOverride;static Camera ui;static RenderTexture target;static Rect safePixels;static Dictionary<string,(bool had,int integer)> ints=new();static Dictionary<string,(bool had,string value)>strings=new();
static StyledMenuProof(){if(SessionState.GetBool(Key,false))Hook();}public static void Run(){SessionState.SetBool(Key,true);Hook();EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");EditorApplication.EnterPlaymode();}
static void Hook(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Logged;Application.logMessageReceived+=Logged;}static void Logged(string msg,string stack,LogType t){if(done||!SessionState.GetBool(Key,false))return;report??=new Report();if(t==LogType.Exception||t==LogType.Error||t==LogType.Assert)report.errors.Add(msg+"\n"+stack);else if(t==LogType.Warning)report.warnings.Add(msg);}
static void Check(bool ok,string msg){report.tests.Add((ok?"PASS ":"FAIL ")+msg);if(!ok)report.errors.Add(msg);}static void Wait(double seconds=.6){next=EditorApplication.timeSinceStartup+seconds;frame=Time.frameCount+8;}static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Flags).GetValue(o);
static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}static string Saved()=>Hash(System.Text.Encoding.UTF8.GetBytes(PlayerPrefs.GetString("ms.progress","")));
static void Tick(){if(done)return;if(start<0)start=EditorApplication.timeSinceStartup;if(EditorApplication.timeSinceStartup-start>320){Finish("Timed out");return;}if(!EditorApplication.isPlaying||G==null||G.menu==null||P==null||Camera.main==null)return;try{if(!prepared){Prepare();return;}if(EditorApplication.timeSinceStartup<next||Time.frameCount<frame)return;if(step>=steps.Count){Finish();return;}steps[step++]();}catch(Exception e){Finish(e.ToString());}}
static void Prepare(){report??=new Report();report.utc=DateTime.UtcNow.ToString("O");report.savedBefore=Saved();oldSim=Progress.Sim;originalProgress=JsonUtility.ToJson(Progress.D);oldHeroOverride=Prefs.HeroOverride;
foreach(var key in new[]{"ms.sector","ms.difficulty","ms.tut.armory","ms.best","ms.test.floor","ms.test.mode"}.Concat(SectorDefs.All.Select(d=>"ms.seen."+d.id)))ints[key]=(PlayerPrefs.HasKey(key),PlayerPrefs.GetInt(key));foreach(var key in new[]{"ms.hero","ms.weapon"})strings[key]=(PlayerPrefs.HasKey(key),PlayerPrefs.GetString(key));
Progress.Sim=true;var temp=JsonUtility.FromJson<SaveData>(originalProgress);temp.runs=Mathf.Max(5,temp.runs);Progress.UseTemp(temp);Prefs.HeroOverride="sentinel";G.player.RebuildHero(G.cfg);AudioListener.pause=true;AudioListener.volume=0;Directory.CreateDirectory(Out);prepared=true;
steps=new List<Action>{
()=>{Resize(1672,941);G.menu.Show();Wait();},
()=>{Check(Title.Visible,"title initially visible");Shot("01_title");Title.PlayButton.onClick.Invoke();Wait(1);},
()=>{Check(P.Visible&&P.Current==MenuPresentation.Screen.Home&&!Title.Visible,"Play opens hangar");Inspect("hangar");Shot("02_hangar");P.HeroesButton.onClick.Invoke();Wait();},
()=>{Check(P.Visible&&P.Current==MenuPresentation.Screen.Heroes,"Heroes opens real hero overview");Inspect("heroes");Shot("03_heroes");P.Back();P.GearButton.onClick.Invoke();Wait();},
()=>{Check(P.Visible&&P.Current==MenuPresentation.Screen.Weapons,"Gear opens starting weapons");Inspect("gear");Shot("04_gear");P.Back();P.DeployButton.onClick.Invoke();Wait();},
()=>{Check(MapSelect.I.Open&&!P.Visible,"Deploy opens zone selection");Inspect("zones");Shot("05_zones");MapSelect.I.PickSector(0);MapSelect.I.StyledBriefButton.onClick.Invoke();Wait();},
()=>{Check(MapSelect.I.Open&&Game.PendingPlan!=null,"briefing binds real floor plan");Inspect("briefing");Shot("06_briefing");MapSelect.I.OpenStyledChange(true);Wait();},
()=>{Check(P.SubscreenOpen&&P.Current==MenuPresentation.Screen.Heroes,"briefing Change opens heroes");P.Back();Wait();},
()=>{Check(MapSelect.I.Open&&!P.Visible,"hero Back restores same briefing");MapSelect.I.OpenStyledChange(false);Wait();},
()=>{Check(P.SubscreenOpen&&P.Current==MenuPresentation.Screen.Weapons,"briefing Change opens gear");P.Back();Wait();},
()=>{Check(MapSelect.I.Open&&!P.Visible,"weapon Back restores briefing");MapSelect.I.StyledBack();MapSelect.I.Hide();Wait();},
()=>{Check(P.Visible&&P.Current==MenuPresentation.Screen.Home,"zone Back returns hangar");Resize(2556,1179);Wait();},
()=>{Inspect("hangar_pro");Shot("07_hangar_pro");P.OpenHeroes();Wait();},
()=>{Inspect("heroes_pro");Shot("08_heroes_pro");P.Back();P.OpenWeapons();Wait();},
()=>{Inspect("gear_pro");Shot("09_gear_pro");P.Back();P.Deploy();Wait();},
()=>{Inspect("zones_pro");Shot("10_zones_pro");MapSelect.I.OpenBriefing();Wait();},
()=>{Inspect("briefing_pro");Shot("11_briefing_pro");MapSelect.I.Hide();Wait();},
()=>{G.menu.OpenSheet("settings");Wait();},()=>{Check(G.menu.LegacySheetOpen&&!P.Visible,"settings works from hangar");Shot("12_settings");G.menu.CloseSheet();Wait();},
()=>{Check(P.Visible,"settings Back restores hangar");G.menu.OpenSheet("gear");Wait();},
()=>{Check(G.menu.Gear.Visible,"equipment inventory opens");Inspect("equipment_pro");Shot("14_equipment_pro");G.menu.Gear.Back();Wait();},
()=>{Check(P.Visible,"equipment Back restores hangar");P.Deploy();Wait();},
()=>{MapSelect.I.PickSector(0);MapSelect.I.OpenBriefing();Wait();},()=>{MapSelect.I.StyledDeployButton.onClick.Invoke();Wait(6);},
()=>{Check(G.run&&G.floor==1&&G.state==Game.State.Playing,"styled deploy reaches actual Cryo floor1");G.godMode=true;Shot("15_gameplay");G.TogglePause();Wait();},
()=>{Check(G.state==Game.State.Paused,"pause enters real paused state");Inspect("pause");Shot("16_pause");G.TogglePause();G.DebugLevelUp();Wait();},
()=>{Inspect("upgrade");Shot("17_upgrade");G.Pick(0);Wait();},()=>{Check(G.choosing==Game.Choice.None,"upgrade callback returns to gameplay");G.DebugCache();Wait();},
()=>{Inspect("cache");Shot("18_cache");G.Pick(0);Wait();},()=>{G.runUi.HideChoice();typeof(Game).GetMethod("RunEnd",Flags).Invoke(G,new object[]{false});Wait();},
()=>{Check(Field<RunResultsView>(G.runUi,"resultsView").Visible,"real RunEnd presents death results");Inspect("death");Shot("19_results_death");Field<RunResultsView>(G.runUi,"resultsView").MenuButton.onClick.Invoke();Wait();},
()=>{Check(G.state==Game.State.Menu&&P.Visible,"results Menu restores hangar");P.Back();Wait();},()=>{Check(Title.Visible&&!P.Visible,"hangar Back restores title");Shot("20_title_returned");}
};
foreach(var size in new[]{new Vector2Int(1334,750),new Vector2Int(2796,1290)}){var q=size;steps.Add(()=>{Title.PlayButton.onClick.Invoke();Resize(q.x,q.y);Wait();});steps.Add(()=>{Inspect("hangar_"+q.x);Shot("hangar_"+q.x);P.OpenHeroes();Wait();});steps.Add(()=>{Inspect("heroes_"+q.x);Shot("heroes_"+q.x);P.Back();P.OpenWeapons();Wait();});steps.Add(()=>{Inspect("gear_"+q.x);Shot("gear_"+q.x);P.Back();P.Deploy();Wait();});steps.Add(()=>{Inspect("zones_"+q.x);Shot("zones_"+q.x);MapSelect.I.OpenBriefing();Wait();});steps.Add(()=>{Inspect("briefing_"+q.x);Shot("briefing_"+q.x);MapSelect.I.Hide();Wait();});steps.Add(()=>{G.menu.OpenSheet("gear");Wait();});steps.Add(()=>{Inspect("equipment_"+q.x);Shot("equipment_"+q.x);G.menu.Gear.Back();Wait();});steps.Add(()=>{P.Back();Wait();});}

steps.Add(()=>{Prefs.HeroOverride=null;Title.PlayButton.onClick.Invoke();P.OpenHeroes();Wait();});
steps.Add(()=>{var b=P.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(x=>x.name=="Hero_vanguard");b.onClick.Invoke();Wait(1);});steps.Add(()=>{Check(Prefs.Vanguard,"actual Vanguard selection persists");Inspect("vanguard_hero");Shot("21_vanguard_hero");P.Back();P.OpenWeapons();Wait();});steps.Add(()=>{Check(P.Current==MenuPresentation.Screen.Weapons,"Vanguard weapons restricted by actual role");Shot("22_vanguard_gear");P.Back();P.OpenHeroes();Progress.D.heroStars.RemoveAll(x=>x.StartsWith("scout:"));Wait();});
steps.Add(()=>{P.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(x=>x.name=="Hero_scout").onClick.Invoke();Check(Prefs.Vanguard,"locked Scout remains gated");Progress.D.heroStars.Add("scout:1");Wait();});steps.Add(()=>{P.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(x=>x.name=="Hero_scout").onClick.Invoke();Wait(1);});steps.Add(()=>{Check(Prefs.Scout,"unlocked Scout selects actual hero");Shot("23_scout_hero");P.Back();P.OpenWeapons();Wait();});steps.Add(()=>{Shot("24_scout_gear");P.Back();P.OpenHeroes();P.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(x=>x.name=="Hero_sentinel").onClick.Invoke();Wait();});steps.Add(()=>{Check(Prefs.Hero=="sentinel","Sentinel restored via actual selection");P.Back();P.Deploy();Wait();});
foreach(int index in Enumerable.Range(0,MapSelect.Sectors.Length)){int i=index;steps.Add(()=>{MapSelect.I.PickSector(i);Wait(.2);});steps.Add(()=>{Check(MapSelect.Selected==i,"zone "+i+" selected");Check(MapSelect.I.StyledBriefButton.interactable==!MapSelect.Sectors[i].locked,"zone "+i+" lock matches progression");});}
steps.Add(()=>{MapSelect.I.Hide();Wait();});steps.Add(()=>{P.Back();Wait();});
}
static void Inspect(string label){Canvas.ForceUpdateCanvases();var canvases=Object.FindObjectsByType<Canvas>();Check(Object.FindObjectsByType<EventSystem>().Length==1,label+" one EventSystem");var b=canvases.Where(c=>c.enabled).SelectMany(c=>c.GetComponentsInChildren<UnityEngine.UI.Button>(false));foreach(var button in b){if(!button.IsActive())continue;var r=Bounds((RectTransform)button.transform);Check(r.xMin>=safePixels.xMin-2&&r.xMax<=safePixels.xMax+2&&r.yMin>=safePixels.yMin-2&&r.yMax<=safePixels.yMax+2,label+" "+button.name+" within safe area");Check(r.width/height*375>=44&&r.height/height*375>=44,label+" "+button.name+" target at least44pt");}}
        static void Resize(int w,int h,bool asymmetric=false)
        {
            width=w;height=h;if(target!=null){Camera.main.targetTexture=null;if(ui!=null)ui.targetTexture=null;target.Release();Object.DestroyImmediate(target);}
            target=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32){antiAliasing=1,name="StyledMenuProof"};target.Create();Camera.main.targetTexture=target;
            if(ui==null){ui=new GameObject("StyledMenuProofUiCamera").AddComponent<Camera>();ui.cullingMask=1<<5;ui.orthographic=true;ui.nearClipPlane=.1f;ui.farClipPlane=200;ui.transform.position=new Vector3(0,-2000,0);ui.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Overlay;Camera.main.GetUniversalAdditionalCameraData().cameraStack.Add(ui);}
            ui.targetTexture=target;float l=w/(float)h>2?177:0,r=w/(float)h>2?(asymmetric?132:177):0,b=w/(float)h>2?63:0;safePixels=new Rect(l,b,w-l-r,h-b);SafeArea.Override=new Rect(l/w,b/h,1-(l+r)/w,1-b/h);
            foreach(var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if(!c.isRootCanvas)continue;int order=c.sortingOrder;c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=ui;c.planeDistance=50;c.sortingOrder=order;
                var sc=c.GetComponent<UnityEngine.UI.CanvasScaler>();if(sc!=null&&c.name!="TitleScreenCanvas"){sc.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;sc.scaleFactor=h/1080f;}
            }
        }
        static Rect Bounds(RectTransform rt){var cs=new Vector3[4];rt.GetWorldCorners(cs);var a=RectTransformUtility.WorldToScreenPoint(ui,cs[0]);var b=RectTransformUtility.WorldToScreenPoint(ui,cs[2]);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);}
        static string Shot(string name)
        {
            var old=RenderTexture.active;RenderTexture.active=target;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();RenderTexture.active=old;var png=tex.EncodeToPNG();string file=Path.Combine(Out,name+".png");File.WriteAllBytes(file,png);Object.DestroyImmediate(tex);report.captures.Add(file);Debug.Log("StyledMenuProof capture "+name);return Hash(png);
        }
static void Finish(string error=null){if(done)return;done=true;report??=new Report();if(error!=null)report.errors.Add(error);try{if(prepared){report.savedAfter=Saved();Check(report.savedBefore==report.savedAfter,"saved progress unchanged");Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalProgress));Progress.Sim=oldSim;Prefs.HeroOverride=oldHeroOverride;foreach(var kv in ints){if(kv.Value.had)PlayerPrefs.SetInt(kv.Key,kv.Value.integer);else PlayerPrefs.DeleteKey(kv.Key);}foreach(var kv in strings){if(kv.Value.had)PlayerPrefs.SetString(kv.Key,kv.Value.value);else PlayerPrefs.DeleteKey(kv.Key);}PlayerPrefs.Save();SafeArea.Override=null;}Directory.CreateDirectory(Out);File.WriteAllText(Path.Combine(Out,"report.json"),JsonUtility.ToJson(report,true));}catch(Exception e){Debug.LogError(e);}SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Logged;Debug.Log("StyledMenuProof complete:"+report.tests.Count+"checks,"+report.errors.Count+"errors");EditorApplication.Exit(report.errors.Count==0?0:1);}
}}
#endif
