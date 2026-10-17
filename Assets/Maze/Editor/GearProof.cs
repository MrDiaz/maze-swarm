#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;
namespace Maze.EditorTools
{
    [InitializeOnLoad]
    public static class GearProof
    {
        const string Key="Maze.GearProof.Active";
        static readonly BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
        static Game G=>Object.FindAnyObjectByType<Game>();static GearView V=>G.menu.Gear;
        static Report report;static List<Action> steps;static int step,frame;static double started=-1,next;
        static bool prepared,done,originalSim,originalPause;static string originalSave,originalHero;static float originalVolume;
        static string Root=>Directory.GetParent(Application.dataPath).FullName;
        static string Out=>Path.Combine(Root,"Logs/GearProof",Environment.GetEnvironmentVariable("UISHOT_SIZE")??"1849x851");
        static string snapshot;static int pendingLocked;static int uid,creditBefore,alloyBefore,costCredit,costAlloy;static GearItem pick;
        static GearProof(){if(SessionState.GetBool(Key,false))Hook();}
        public static void Run(){Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Start gear proof outside Play mode.");GearArtImport.Apply();SessionState.SetBool(Key,true);Hook();EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");EditorApplication.EnterPlaymode();}
        static void Hook(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Logged;Application.logMessageReceived+=Logged;}
        static void Logged(string message,string stack,LogType type){if(done||!SessionState.GetBool(Key,false))return;report??=new Report();if(type is LogType.Error or LogType.Exception or LogType.Assert){bool known=(bool)typeof(CryoKitProof).GetMethod("KnownEditorSearchStartup",Flags).Invoke(null,new object[]{message,stack,type});if(known)report.editorDiagnostics.Add(type+": "+message+"\n"+stack);else report.errors.Add(message+"\n"+stack);}else if(type==LogType.Warning)report.warnings.Add(message);}
        static void Tick(){if(done)return;if(started<0)started=EditorApplication.timeSinceStartup;if(EditorApplication.timeSinceStartup-started>360){Finish(false,"Gear proof timed out.");return;}if(!EditorApplication.isPlaying||G==null||G.menu==null||G.menu.Gear==null||G.player==null)return;try{if(!prepared){Prepare();return;}Require(report.errors.Count==0,"Unity error during gear proof.");if(EditorApplication.timeSinceStartup<next||Time.frameCount<frame)return;if(step>=steps.Count){Finish(true);return;}steps[step++]();}catch(Exception e){Finish(false,e.ToString());}}
        static void Wait(double seconds=.5){next=EditorApplication.timeSinceStartup+seconds;frame=Time.frameCount+3;}
        static string Snapshot()=>JsonUtility.ToJson(Progress.D);
        static SaveData Data(int c=444,int a=26)=>new(){credits=c,parts=45,alloy=a,runs=5};
        static void Seed(int count=16,int credits=444,int alloy=26)
        {
            Progress.Sim=true;Progress.UseTemp(Data(credits,alloy));Prefs.HeroOverride="vanguard";
            for(int n=0;n<count;n++){int slot=count==16?n/4:n%5;int rarity=count==16?n%4:n%5;var item=Progress.AddGear(slot,rarity,slot==0&&rarity==1?3:-1);item.locked=slot==0&&rarity==3;}
            G.menu.Show();G.menu.OpenSheet("loadout");G.menu.OpenSheet("gear");if(count>0)V.SelectItem(Progress.D.gear.FirstOrDefault(g=>g.slot==0&&g.rarity==1)?.uid??Progress.D.gear[0].uid,true);
        }
        static void Prepare()
        {
            report??=new Report();report.generatedUtc=DateTime.UtcNow.ToString("O");report.unityVersion=Application.unityVersion;report.resolution=Environment.GetEnvironmentVariable("UISHOT_SIZE")??"1849x851";
            originalSave=Snapshot();originalSim=Progress.Sim;originalHero=Prefs.HeroOverride;originalPause=AudioListener.pause;originalVolume=AudioListener.volume;report.savedBefore=Hash(PlayerPrefs.GetString("ms.progress",""));prepared=true;
            UiShot.Prepare();if(report.resolution=="1849x851")SafeArea.Override=new Rect(0,0,1,1);AudioListener.pause=true;AudioListener.volume=0;
            steps=new(){()=>{Seed();Wait(1.1);},()=>{CheckBinding();CheckTargets();Shot("01_fullscreen_gear");Wait();}};
            if(Environment.GetEnvironmentVariable("GEAR_VISUAL_ONLY")!="1")AddBehavior();Wait();
        }
        static void AddBehavior()
        {
            steps.Add(()=>{snapshot=Snapshot();int power=Progress.Power();foreach(var g in Progress.D.gear)V.SelectItem(g.uid);Require(Snapshot()==snapshot&&Progress.Power()==power,"Selection changed equipped data/power.");for(int f=-1;f<5;f++){V.ChooseFilter(f);Require(V.VisibleUids.Distinct().Count()==V.VisibleCount,"Duplicate cells.");Require(V.VisibleUids.All(id=>f<0||Progress.Item(id).slot==f),"Filter displayed wrong item type.");}report.selectionAndFilters=true;V.ChooseFilter(-1);Wait();});
            steps.Add(()=>{Seed(65);Wait();});
            steps.Add(()=>{Require(V.FilteredCount==65,"Large inventory truncated.");var all=new HashSet<int>();for(int p=0;p<5;p++){V.SetPage(p);foreach(int id in V.VisibleUids)Require(all.Add(id),"Repeated UID across pages.");}Require(all.Count==65,"Pagination lost inventory.");report.largeInventory=true;Wait();});
            steps.Add(()=>{Require(V.Page==4&&V.VisibleCount==1,"Last page changed before capture.");Shot("02_large_inventory_last_page");Seed(0);Wait();});
            steps.Add(()=>{Require(V.VisibleCount==0&&V.SelectedUid==0,"Empty inventory retained sample items.");Shot("03_empty_inventory");report.emptyInventory=true;Seed();Wait();});
            steps.Add(()=>{pick=Progress.Item(V.SelectedUid);uid=pick.uid;snapshot=Snapshot();Require(Progress.D.equipped[pick.slot]!=uid&&V.EquippedText=="EMPTY","Prospective item implied equipped state.");V.Equip();Require(Progress.D.equipped[pick.slot]==uid,"Equip did not use actual UID.");string after=Snapshot();V.Equip();Require(Snapshot()==after,"Repeated equip changed data.");report.equip=true;Wait();});
            steps.Add(()=>{Require(V.EquippedText=="THIS ITEM","Actual equipped state not shown.");pick=Progress.Item(uid);creditBefore=Progress.D.credits;alloyBefore=Progress.D.alloy;costCredit=Meta.GearCredits(pick);costAlloy=Meta.GearAlloy(pick);int lv=pick.level;V.Upgrade();Require(pick.level==lv+1&&Progress.D.credits==creditBefore-costCredit&&Progress.D.alloy==alloyBefore-costAlloy,"Upgrade costs/level changed.");snapshot=Snapshot();V.Upgrade();Require(Snapshot()==snapshot,"Repeated upgrade bought twice.");report.upgradeAndDebounce=true;Wait();});
            steps.Add(()=>{Progress.D.credits=0;Progress.D.alloy=0;V.Refresh();snapshot=Snapshot();V.Upgrade();Require(Snapshot()==snapshot&&!V.CanUpgrade,"Unaffordable upgrade changed data.");pick.level=Progress.MaxLevel(pick.rarity);Progress.D.credits=99999;Progress.D.alloy=99999;V.Refresh();Require(!V.CanUpgrade,"Max-level upgrade enabled.");report.affordabilityAndMax=true;Seed();Wait();});
            steps.Add(()=>{pick=Progress.Item(V.SelectedUid);V.ToggleLock();Require(pick.locked,"Lock failed.");V.ToggleLock();Require(pick.locked,"Repeated lock toggled twice.");snapshot=Snapshot();V.RequestSalvage();V.RequestMerge();Require(!V.ModalOpen&&Snapshot()==snapshot,"Locked item allowed destructive action.");report.locking=true;Wait();});
            steps.Add(()=>{V.ToggleLock();Require(!pick.locked,"Unlock failed.");V.RequestSalvage();Require(V.ModalOpen,"Salvage omitted confirmation.");snapshot=Snapshot();Wait();});
            steps.Add(()=>{Shot("05_salvage_confirmation");V.Cancel();Require(Snapshot()==snapshot,"Cancel salvaged item.");report.destructiveCancel=true;Wait();});
            steps.Add(()=>{uid=pick.uid;int reward=Meta.Salvage(pick);alloyBefore=Progress.D.alloy;int count=Progress.D.gear.Count;V.RequestSalvage();V.Confirm();Require(Progress.Item(uid)==null&&Progress.D.gear.Count==count-1&&Progress.D.alloy==alloyBefore+reward,"Salvage reward/removal incorrect.");snapshot=Snapshot();V.Confirm();Require(Snapshot()==snapshot,"Repeated confirmation salvaged twice.");report.salvage=true;Wait();});
            steps.Add(()=>{pick=Progress.D.gear.First(g=>!g.locked&&Progress.D.equipped[g.slot]!=g.uid);V.SelectItem(pick.uid,true);V.RequestSalvage();Require(V.ModalOpen,"Missing pending salvage.");pick.locked=true;snapshot=Snapshot();V.Confirm();Require(Snapshot()==snapshot&&!V.ModalOpen,"Stale confirmation consumed changed/locked item.");report.staleConfirmation=true;Progress.UseTemp(Data());for(int n=0;n<3;n++)Progress.AddGear(0,0,3);V.Open();V.ChooseFilter(-1);V.SelectItem(Progress.D.gear[0].uid,true);Wait();});
            steps.Add(()=>{int count=Progress.D.gear.Count;V.RequestMerge();Require(V.ModalOpen,"Merge omitted confirmation.");V.Confirm();var result=Progress.Item(V.SelectedUid);Require(result!=null&&result.rarity==1&&result.set==3&&Progress.D.gear.Count==count-2,"Merge outcome changed.");Require(Progress.D.equipped[0]==result.uid,"Existing empty-slot merge auto-equip changed.");snapshot=Snapshot();V.Confirm();Require(Snapshot()==snapshot,"Repeated merge confirmation consumed more.");report.merge=true;Wait();});
            steps.Add(()=>{Progress.UseTemp(Data());var protectedItem=Progress.AddGear(0,0);Progress.Equip(protectedItem);var lockedItem=Progress.AddGear(0,0);lockedItem.locked=true;for(int n=0;n<3;n++)Progress.AddGear(0,0);V.Open();V.ChooseFilter(-1);uid=protectedItem.uid;pendingLocked=lockedItem.uid;Wait();});
            steps.Add(()=>{V.RequestMergeAll();Require(V.ModalOpen,"Merge all omitted confirmation.");V.Confirm();Require(Progress.Item(uid)!=null&&Progress.D.equipped[0]==uid&&Progress.Item(pendingLocked)!=null&&Progress.D.gear.Count==3,"Merge all consumed equipped/locked pieces or changed ratios.");report.mergeAll=true;Wait();});
            steps.Add(()=>{Progress.UseTemp(Data(1000,2000));Progress.D.starTrackClaimed=60;Progress.D.relics.Add("0:1");G.menu.Show();G.menu.OpenSheet("gear");V.OpenRelics();Require(V.ModalOpen,"Unlocked relic view missing.");V.Confirm();Require(Meta.EquippedRelic==0,"Owned relic cycle changed.");Wait();});
            steps.Add(()=>{V.OpenRelics();alloyBefore=Progress.D.alloy;int c=Meta.RelicCost(0);var b=V.transform.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(x=>x.name=="RelicUpgrade");b.onClick.Invoke();Require(Meta.RelicLv(0)==2&&Progress.D.alloy==alloyBefore-c,"Relic upgrade cost/level changed.");report.relic=true;Wait();});
            steps.Add(()=>{Seed();uid=V.SelectedUid;V.Back();Require(!G.menu.GearOpen&&G.menu.SheetOpen,"Back did not return to Armory.");G.menu.OpenSheet("gear");Require(V.SelectedUid==uid,"Selection did not persist on return.");report.navigation=true;Wait();});
            steps.Add(()=>{CheckBinding();Shot("04_final_fullscreen_gear");});
            steps.Add(()=>{Prefs.HeroOverride="sentinel";V.Refresh();Wait();});
            steps.Add(()=>{Shot("06_other_operative");Prefs.HeroOverride="vanguard";V.Refresh();});
        }
        static void CheckBinding(){Require(V.Visible&&G.menu.GearOpen,"Gear did not replace modal with active screen.");var text=V.GetComponentsInChildren<TMPro.TMP_Text>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First().GetParsedText());Require(text["Credits"]==Progress.D.credits.ToString("N0")&&text["Parts"]==Progress.D.parts.ToString("N0")&&text["Alloy"]==Progress.D.alloy.ToString("N0")&&text["Power"]==Progress.Power().ToString("N0"),"Wallet/power are not live.");Require(!CryoMachineryPreview.Enabled,"Machinery preview unexpectedly enabled.");report.liveBindings=true;}
        static void CheckTargets(){Require(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"Expected one EventSystem.");var canvas=V.GetComponentInParent<Canvas>();Require(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>()!=null,"Missing raycaster.");foreach(var b in new[]{V.EquipButton,V.UpgradeButton,V.LockButton,V.SalvageButton,V.MergeButton,V.MergeAllButton,V.BackButton})Require(b.targetGraphic!=null&&b.targetGraphic.raycastTarget,"Missing button hit target: "+b.name);var raycaster=canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();foreach(var b in new[]{V.EquipButton,V.UpgradeButton,V.LockButton,V.SalvageButton,V.BackButton}){if(!b.interactable)continue;Vector3 point=b.transform.TransformPoint(((RectTransform)b.transform).rect.center);var data=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,point)};var hits=new List<RaycastResult>();raycaster.Raycast(data,hits);Require(hits.Any(h=>h.gameObject.transform.IsChildOf(b.transform)),"Button blocked from raycast: "+b.name);}report.interactionReady=true;report.raycastTargets=true;}
        static void Shot(string name){UiShot.Shot(name,new[]{Out});var path=Path.Combine(Out,name+".png");var bytes=File.ReadAllBytes(path);Require(bytes.Length>20000,"Empty gear capture.");report.captures.Add(new Capture{file=path,sha256=Hash(bytes),bytes=bytes.Length});Directory.CreateDirectory(Out);File.WriteAllText(Path.Combine(Out,"proof-report.json"),JsonUtility.ToJson(report,true));Debug.Log("GearProof capture: "+path);}
        static string Hash(string s)=>Hash(Encoding.UTF8.GetBytes(s));static string Hash(byte[] b){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
        static void Require(bool p,string s){if(!p)throw new InvalidOperationException(s);}
        static void Finish(bool passed,string error=null){if(done)return;done=true;report??=new Report();if(error!=null)report.errors.Add(error);if(prepared)try{V.Close();G.menu.CloseSheet();Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalSave));Progress.Sim=originalSim;Prefs.HeroOverride=originalHero;AudioListener.pause=originalPause;AudioListener.volume=originalVolume;SafeArea.Override=null;report.savedAfter=Hash(PlayerPrefs.GetString("ms.progress",""));Require(report.savedBefore==report.savedAfter,"Saved progression changed.");}catch(Exception e){report.errors.Add("Cleanup: "+e);passed=false;}report.passed=passed&&report.errors.Count==0;report.completedUtc=DateTime.UtcNow.ToString("O");Directory.CreateDirectory(Out);File.WriteAllText(Path.Combine(Out,"proof-report.json"),JsonUtility.ToJson(report,true));SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Logged;Debug.Log("GearProof: "+(report.passed?"PASS":"FAIL")+"; "+Out);if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);else EditorApplication.ExitPlaymode();}
        [Serializable]class Capture{public string file,sha256;public int bytes;}
        [Serializable]class Report{public string generatedUtc,completedUtc,unityVersion,resolution,savedBefore,savedAfter;public bool passed,liveBindings,interactionReady,raycastTargets,selectionAndFilters,largeInventory,emptyInventory,equip,upgradeAndDebounce,affordabilityAndMax,locking,destructiveCancel,salvage,staleConfirmation,merge,mergeAll,relic,navigation;public List<string> errors=new(),warnings=new(),editorDiagnostics=new();public List<Capture> captures=new();public string[] limits={"Actual Unity Editor rendering with temporary save; no phone installation or device performance measurement.","Artwork is approved source pixels; reference has no editable font/clean hidden backing layers.","Displayed test values come from existing Progress/Meta APIs; no screenshot-specific production stats.","The taller machinery preview remains disabled; this proof makes no scene or occlusion-system changes."};}
    }
}
#endif
