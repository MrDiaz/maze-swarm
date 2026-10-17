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
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // Capture-only harness. Runtime layout, inputs, saves, and combat are not replaced by this proof.
    [InitializeOnLoad]
    public static class RoundedHudProof
    {
        const string Key = "Maze.RoundedHudProof.Active";
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly string Out = Environment.GetEnvironmentVariable("HUD_PROOF_OUT") ?? "Logs/RoundedHudProof";
        [Serializable] class Report
        {
            public string utc, unityVersion, savedBefore, savedAfter;
            public List<string> tests = new(), errors = new(), warnings = new(), knownEditorDiagnostics = new(), captures = new();
            public List<BoundsRow> bounds = new();
        }
        [Serializable] class BoundsRow { public string screen, name; public float x,y,width,height; }
        static Report report;
        static Game G => Object.FindAnyObjectByType<Game>();
        static List<Action> steps; static int step, frame, width, height; static double started = -1, next;
        static bool prepared, done, originalSim, originalPreview, originalAudioPause; static float originalVolume, originalTime;
        static string originalProgress, originalHero; static int originalTier; static SectorDef originalSector; static Game.Goal[] originalPlan;
        static RenderTexture target; static Camera ui; static Rect safePixels; static int liveKills; static Vector2 liveStart; static int liveAmmo; static float liveTime;
        static CellLight testLight;
        static RoundedHudProof() { if (SessionState.GetBool(Key,false)) Hook(); }
        public static void Run()
        {
            SessionState.SetBool(Key,true); Hook(); EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
        }
        static void Hook() { EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived -= Log; Application.logMessageReceived += Log; }
        static void Log(string message,string stack,LogType type)
        {
            if (done || !SessionState.GetBool(Key,false)) return; report ??= new Report();
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                var method = typeof(CryoKitProof).GetMethod("KnownEditorSearchStartup",BindingFlags.Static|BindingFlags.NonPublic);
                if (method != null && (bool)method.Invoke(null,new object[]{message,stack,type})) report.knownEditorDiagnostics.Add(message);
                else report.errors.Add(message+"\n"+stack);
            }
            else if (type == LogType.Warning) report.warnings.Add(message);
        }
        static void Tick()
        {
            if (done) return;
            if (started < 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup-started > 240) { Finish("Timed out"); return; }
            if (!EditorApplication.isPlaying || G == null || G.player == null || G.runUi == null || Camera.main == null) return;
            try
            {
                if (!prepared) { Prepare(); return; }
                G.hud.Refresh(1f/60,G);
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < frame) return;
                if (step >= steps.Count) { Finish(); return; }
                steps[step++]();
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Check(bool ok,string message) { report.tests.Add((ok?"PASS ":"FAIL ")+message); if (!ok) report.errors.Add(message); }
        static void Wait(double sec=.2) { next=EditorApplication.timeSinceStartup+sec; frame=Time.frameCount+4; }
        static T Field<T>(object obj,string name) => (T)obj.GetType().GetField(name,Instance).GetValue(obj);
        static void Set(object obj,string name,object value) => obj.GetType().GetField(name,Instance).SetValue(obj,value);
        static string Hash(string s) { using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant(); }
        static void Prepare()
        {
            report ??= new Report(); report.utc=DateTime.UtcNow.ToString("O");report.unityVersion=Application.unityVersion;report.savedBefore=Hash(PlayerPrefs.GetString("ms.progress",""));
            originalProgress=JsonUtility.ToJson(Progress.D);originalSim=Progress.Sim;originalHero=Prefs.HeroOverride;originalPreview=CryoSoftEnvironment.Enabled;
            originalVolume=AudioListener.volume;originalAudioPause=AudioListener.pause;originalTime=Time.timeScale;
            originalTier=Game.PendingTier;originalSector=Game.PendingSector;originalPlan=Game.PendingPlan;
            Progress.Sim=true;AudioListener.volume=0;AudioListener.pause=true;Time.timeScale=1;
            Directory.CreateDirectory(Out);prepared=true;
            steps=new List<Action>{
                ()=>{ Resize(2556,1179);StartHero("sentinel");Wait(2.8); },
                ()=>{ Ready();Inspect("pro_ready");Shot("01_pro_sentinel_ready");HitTests();Wait(); },
                ()=>{ Set(G,"botDash",true);G.demo=true;G.Tick(.001f);G.demo=false;Check(G.player.dashT>0&&G.player.dashCd>0,"actual dash action starts active and cooldown state");Wait(); },
                ()=>{ Check(Field<RoundedHudButton>(G.hud,"dashVisual").Active,"dash active presentation uses real dash lifetime");Shot("01a_pro_dash_active");G.Tick(G.player.dashT+.01f);Wait(); },
                ()=>{ Check(!Field<RoundedHudButton>(G.hud,"dashVisual").Active&&G.player.dashCd>0,"dash active ends into cooldown");Shot("01b_pro_dash_cooldown");ActivateSkill();G.player.dashCd=G.cfg.player.dashCooldown*.5f;Wait(); },
                ()=>{ Check(Field<RoundedHudButton>(G.hud,"skillVisual").Counter!="","freeze cooldown timer is visible");Shot("02_pro_sentinel_cooldown");PauseTest();Wait(.65); },
                ()=>{ Check(G.state==Game.State.Paused && G.hud.OverlayOpen,"pause opened through real pointer callback");Shot("03_pause");G.TogglePause();Ready();ShowStick();Wait(); },
                ()=>{ Shot("04_pro_joystick");HideStick();G.player.hp=G.player.maxHp*.22f;G.player.hurtT=.16f;G.level=Game.MaxLevel;G.xp=0;Wait(); },
                ()=>{ Check(Field<UnityEngine.UI.Text>(G.runUi,"lvText").text=="MAX 10","maximum level shows MAX 10");Check(Mathf.Abs(Field<UnityEngine.UI.Image>(G.runUi,"xpFill").fillAmount)<.01f,"XP remains empty at cap");Inspect("pro_lowhp_max");Shot("05_pro_lowhp_max");G.player.hp=G.player.maxHp;G.player.hurtT=0;G.level=3;G.xp=G.XpNeed/2;Resize(1334,750);Wait(); },
                ()=>{ Inspect("se_ready");Shot("06_se_sentinel_ready");G.player.dashCd=G.cfg.player.dashCooldown*.5f;Abilities.Cd=Abilities.Cooldown*.5f;Wait(); },
                ()=>{ Inspect("se_cooldown");Shot("06b_se_cooldown");Ready();Resize(2796,1290);Wait(); },
                ()=>{ Inspect("max_ready");Shot("07_max_sentinel_ready");G.player.dashCd=G.cfg.player.dashCooldown*.5f;Abilities.Cd=Abilities.Cooldown*.5f;Wait(); },
                ()=>{ Inspect("max_cooldown");Shot("07b_max_cooldown");Ready();Resize(2556,1179,true);Wait(); },
                ()=>{ Inspect("asymmetric_safe");Shot("08_asymmetric_safe");Resize(2556,1179);StartHero("vanguard");Wait(2.8); },
                ()=>{ Ready();Inspect("vanguard_ready");Check(Field<RoundedHudButton>(G.hud,"skillVisual").IconName=="barrage","Vanguard uses barrage icon");Shot("09_vanguard_ready");ActivateSkill();Wait(); },
                ()=>{ Check(Abilities.BarrageT>0 && Field<RoundedHudButton>(G.hud,"skillVisual").Active,"real Barrage active state reaches HUD");Shot("10_vanguard_active");Abilities.Tick(G,Abilities.BarrageT+.1f,false);Wait(); },
                ()=>{ Check(!Field<RoundedHudButton>(G.hud,"skillVisual").Active && Field<RoundedHudButton>(G.hud,"skillVisual").Counter!="","Barrage ends into cooldown");Shot("11_vanguard_cooldown");StartHero("scout");Wait(2.8); },
                ()=>{ Ready();Inspect("scout_ready");Check(Field<RoundedHudButton>(G.hud,"skillVisual").IconName=="decoy","Scout uses decoy icon");Shot("12_scout_ready");ActivateSkill();Wait(); },
                ()=>{ Check(Abilities.DecoyT>0 && Field<RoundedHudButton>(G.hud,"skillVisual").Active,"real Decoy active state reaches HUD");Shot("13_scout_active");Abilities.Tick(G,Abilities.DecoyT+.1f,false);Wait(); },
                ()=>{ Check(!Field<RoundedHudButton>(G.hud,"skillVisual").Active,"Decoy ends into cooldown");Shot("14_scout_cooldown");PowerUps.EndRun();Ready();testLight=CellLight.I;if(testLight==null){testLight=new CellLight();testLight.Begin(G);}testLight.Level=0;CellLight.Flares=3;Wait(); },
                ()=>{ Inspect("flare_ready");Shot("15_flare_ready");var f=new InputFrame();var b=Field<HoloButton>(G.hud,"flareBtn");Check(TryAction(Centre((RectTransform)b.transform),ref f)&&f.flare,"flare hit produces flare input");testLight.ThrowFlare();Check(CellLight.Flares==2,"real flare consumes one charge");Wait(); },
                ()=>{ Check(Field<RoundedHudButton>(G.hud,"flareVisual").Counter=="2","flare counter reflects remaining charges");Shot("16_flare_used");CellLight.Flares=0;Wait(); },
                ()=>{ Shot("17_flare_empty");testLight.ThrowFlare();Check(CellLight.Flares==0,"empty flare cannot consume below zero");testLight.Level=1;Wait(); },
                ()=>{ Check(!Field<HoloButton>(G.hud,"flareBtn").gameObject.activeSelf,"flare hides in a lit map");StartHero("sentinel");Wait(2.8); },
                ()=>{ Ready();liveStart=G.player.p;liveKills=G.kills;liveAmmo=G.player.ammo[G.player.weapon];liveTime=G.GameTime;G.demo=true;G.holdFire=false;G.enabled=true;Wait(9); },
                ()=>{ G.enabled=false;Check(G.GameTime>liveTime+.5f,"real gameplay advanced");Check((G.player.p-liveStart).sqrMagnitude>.2f,"real gameplay movement advanced");Check(G.kills>liveKills || G.player.ammo[G.player.weapon]!=liveAmmo,"real gameplay fires and/or kills");Shot("18_live_gameplay");G.demo=false;G.holdFire=true;G.state=Game.State.Playing;G.DebugFloor(5);G.enabled=false;G.hud.SetMode(Hud.Mode.Playing);Wait(2.8); },
                ()=>{ Inspect("boss");Shot("19_boss_header");G.DebugFloor(1);G.enabled=false;G.hud.SetMode(Hud.Mode.Playing);Ready();Wait(.3); },
                ()=>{ Check(Object.FindObjectsByType<EventSystem>().Length==1,"one active EventSystem");Check(Field<Canvas>(G.hud,"canvas").GetComponent<UnityEngine.UI.GraphicRaycaster>()!=null,"HUD canvas has GraphicRaycaster");Shot("20_returned_first_floor"); }
            };
        }
        static void StartHero(string hero)
        {
            G.enabled=true;G.demo=false;G.holdFire=true;G.godMode=true;Prefs.HeroOverride=hero;CryoSoftEnvironment.Enabled=true;
            Progress.Sim=true;Progress.UseTemp(new SaveData{runs=5});UnityEngine.Random.InitState(7);G.LoadMap(0);
            var def=SectorDefs.Get("cryo-station");Game.PendingTier=def.tier;Game.PendingSector=def;Game.PendingPlan=null;G.menu.Hide();G.Restart();if(G.floor!=1)G.DebugFloor(1);G.events.Tick(0);
            G.player.RebuildHero(G.cfg);G.state=Game.State.Playing;G.hud.SetMode(Hud.Mode.Playing);G.Tick(0);G.enabled=false;Ready();
            Check(G.run&&G.sector?.id=="cryo-station","started actual Cryo for "+hero);
        }
        static void Ready() { EnemyAI.Tele.Clear();Abilities.Cd=0;Abilities.BarrageT=0;Abilities.DecoyT=0;G.player.dashCd=G.player.dashT=0;G.hud.Refresh(.1f,G); }
        static void ActivateSkill()
        {
            var b=Field<HoloButton>(G.hud,"skillBtn");var f=new InputFrame();Check(TryAction(Centre((RectTransform)b.transform),ref f)&&f.skill,"skill hit region routes only skill");Abilities.Cd=0;Abilities.Tick(G,0,f.skill);Check(Abilities.Cd>0,"actual ability activation starts cooldown");
            G.hud.Refresh(.1f,G);
            Check(!Field<UnityEngine.UI.Text>(G.hud,"banner").text.Contains(Abilities.Name),"no ability-name banner");
        }
        static bool TryAction(Vector2 screen,ref InputFrame input)
        {
            var args=new object[]{screen,input};var hit=(bool)typeof(Hud).GetMethod("TryHudAction",Instance).Invoke(G.hud,args);input=(InputFrame)args[1];return hit;
        }
        static void HitTests()
        {
            foreach(var name in new[]{"dashBtn","skillBtn"})
            {
                var b=Field<HoloButton>(G.hud,name);var f=new InputFrame();Check(TryAction(Centre((RectTransform)b.transform),ref f),name+" centre hit");Check(name=="dashBtn"?f.dash&&!f.skill&&!f.flare:f.skill&&!f.dash&&!f.flare,name+" distinct input");
                var r=ScreenRect((RectTransform)b.transform);f=new InputFrame();Check(!TryAction(new Vector2(r.xMin-6,r.yMax+6),ref f),name+" outside hit rejected");
            }
            var actions=G.hud.GetComponentsInChildren<HoloButton>(true).Where(b=>b.transform.parent==Field<RectTransform>(G.hud,"safe")).Select(b=>b.name).ToArray();
            Check(!actions.Contains("Weapon")&&actions.OrderBy(s=>s).SequenceEqual(new[]{"Dash","Flare","Pause","Skill"}),"only intended in-map actions, no weapon/shield/coin picker");
            Check(G.hud.GetComponentsInChildren<UnityEngine.UI.Text>(false).All(x=>x.name!="Label"),"in-map action labels removed");
        }
        static void PauseTest()
        {
            var pause=Field<HoloButton>(G.hud,"pauseBtn");ExecuteEvents.Execute(pause.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);
            var f=new InputFrame();Check(!TryAction(Centre((RectTransform)Field<HoloButton>(G.hud,"dashBtn").transform),ref f),"pause blocks action input");
        }
        static void ShowStick()
        {
            var stick=Field<object>(G.hud,"left");Set(stick,"finger",99);Set(stick,"origin",new Vector2(width*.18f,height*.23f));Set(stick,"pos",new Vector2(width*.18f+110*height/1080f,height*.23f+30*height/1080f));
            var baseRt=Field<RectTransform>(stick,"baseImg");var knob=Field<RectTransform>(stick,"knob");baseRt.gameObject.SetActive(true);knob.gameObject.SetActive(true);baseRt.anchoredPosition=Field<Vector2>(stick,"origin")/(height/1080f);knob.anchoredPosition=Field<Vector2>(stick,"pos")/(height/1080f);
            var v=(Vector2)stick.GetType().GetMethod("Value").Invoke(stick,new object[]{110*height/1080f});Check(v.magnitude<=1.001f&&v.x>.5f,"existing joystick clamps and returns movement");
        }
        static void HideStick()
        {
            var stick=Field<object>(G.hud,"left");Set(stick,"finger",-1);Field<RectTransform>(stick,"baseImg").gameObject.SetActive(false);Field<RectTransform>(stick,"knob").gameObject.SetActive(false);
        }
        static void Resize(int w,int h,bool asymmetric=false)
        {
            width=w;height=h;
            if(target!=null){Camera.main.targetTexture=null;if(ui!=null)ui.targetTexture=null;target.Release();Object.DestroyImmediate(target);}
            target=new RenderTexture(w,h,24,RenderTextureFormat.ARGBHalf){antiAliasing=4,name="RoundedHudProof"};target.Create();Camera.main.targetTexture=target;
            if(ui==null){ui=new GameObject("RoundedHudProofUiCamera").AddComponent<Camera>();ui.cullingMask=1<<5;ui.orthographic=true;ui.nearClipPlane=.1f;ui.farClipPlane=200;ui.transform.position=new Vector3(0,-2000,0);ui.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Overlay;Camera.main.GetUniversalAdditionalCameraData().cameraStack.Add(ui);}
            ui.targetTexture=target;
            float l=w/(float)h>2?177:0,r=w/(float)h>2?(asymmetric?132:177):0,b=w/(float)h>2?63:0;
            safePixels=new Rect(l,b,w-l-r,h-b);SafeArea.Override=new Rect(l/w,b/h,1-(l+r)/w,1-b/h);
            foreach(var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if(!canvas.isRootCanvas)continue;int order=canvas.sortingOrder;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=ui;canvas.planeDistance=50;canvas.sortingOrder=order;
                var sc=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();if(sc!=null){sc.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;sc.scaleFactor=h/1080f;}
            }
        }
        static Rect ScreenRect(RectTransform rt)
        {
            var corners=new Vector3[4];rt.GetWorldCorners(corners);var a=RectTransformUtility.WorldToScreenPoint(ui,corners[0]);var b=RectTransformUtility.WorldToScreenPoint(ui,corners[2]);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        static Vector2 Centre(RectTransform rt)=>ScreenRect(rt).center;
        static void Inspect(string label)
        {
            Canvas.ForceUpdateCanvases();var roots=new List<(string,RectTransform)>{("status",Field<RectTransform>(G.hud,"roundedStatus")),("objective",Field<RectTransform>(G.runUi,"trackPanel")),("pause",(RectTransform)Field<HoloButton>(G.hud,"pauseBtn").transform),("dash",(RectTransform)Field<HoloButton>(G.hud,"dashBtn").transform),("skill",(RectTransform)Field<HoloButton>(G.hud,"skillBtn").transform)};
            var flare=Field<HoloButton>(G.hud,"flareBtn");if(flare.gameObject.activeSelf)roots.Add(("flare",(RectTransform)flare.transform));
            foreach(var (name,rt) in roots)
            {
                var r=ScreenRect(rt);report.bounds.Add(new BoundsRow{screen=label+" "+width+"x"+height,name=name,x=r.x,y=r.y,width=r.width,height=r.height});
                Check(r.xMin>=safePixels.xMin-1&&r.xMax<=safePixels.xMax+1&&r.yMin>=safePixels.yMin-1&&r.yMax<=safePixels.yMax+1,label+" "+name+" within safe area");
                if(name=="pause"||name=="dash"||name=="skill"||name=="flare")Check(r.width/height*375>=44&&r.height/height*375>=44,label+" "+name+" at least44pt target at375pt landscape height");
            }
            for(int i=0;i<roots.Count;i++)for(int j=i+1;j<roots.Count;j++)Check(!ScreenRect(roots[i].Item2).Overlaps(ScreenRect(roots[j].Item2)),label+" "+roots[i].Item1+" / "+roots[j].Item1+" do not overlap");
            foreach(var tx in new[]{Field<UnityEngine.UI.Text>(G.hud,"hpText"),Field<UnityEngine.UI.Text>(G.runUi,"lvText"),Field<UnityEngine.UI.Text>(G.runUi,"goalText"),Field<UnityEngine.UI.Text>(G.runUi,"roundedGoalValue")})
            {
                var settings=tx.GetGenerationSettings(tx.rectTransform.rect.size);var gen=new TextGenerator();gen.Populate(tx.text,settings);Check(gen.characterCountVisible>=tx.text.Length-1,label+" "+tx.name+" text fits");
            }
            foreach(var b in G.hud.GetComponentsInChildren<RoundedHudButton>(false))Check(Field<UnityEngine.UI.Image>(b,"glyph").sprite!=null,label+" "+b.name+" has actual icon sprite");
        }
        static void Shot(string name)
        {
            var old=RenderTexture.active;RenderTexture.active=target;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();RenderTexture.active=old;var file=Path.Combine(Out,name+".png");File.WriteAllBytes(file,tex.EncodeToPNG());Object.DestroyImmediate(tex);report.captures.Add(file);Debug.Log("RoundedHudProof capture "+file);
        }
        static void Finish(string error=null)
        {
            if(done)return;done=true;if(error!=null){report??=new Report();report.errors.Add(error);}
            try
            {
                if(prepared)
                {
                    if(G!=null){G.enabled=false;G.ToMenu();}
                    report.savedAfter=Hash(PlayerPrefs.GetString("ms.progress",""));Check(report.savedBefore==report.savedAfter,"saved progress key unchanged");
                    Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalProgress));Progress.Sim=originalSim;Prefs.HeroOverride=originalHero;CryoSoftEnvironment.Enabled=originalPreview;
                    Game.PendingTier=originalTier;Game.PendingSector=originalSector;Game.PendingPlan=originalPlan;SafeArea.Override=null;AudioListener.pause=originalAudioPause;AudioListener.volume=originalVolume;Time.timeScale=originalTime;
                }
                Directory.CreateDirectory(Out);File.WriteAllText(Path.Combine(Out,"report.json"),JsonUtility.ToJson(report,true));
            }
            catch(Exception e){Debug.LogError(e);}
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Debug.Log("RoundedHudProof complete: "+report.errors.Count+" errors, "+report.tests.Count+" checks");EditorApplication.Exit(report.errors.Count==0?0:1);
        }
    }
}
#endif
