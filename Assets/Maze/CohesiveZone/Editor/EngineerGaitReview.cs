#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
namespace Maze.EditorTools
{
    public static partial class CohesiveEditorReview
    {
        [Serializable] sealed class GaitFrame
        {
            public int frame; public float enemySlow, terrainMoveMul, friction; public Vector3 leftToe, rightToe, leftHeel, rightHeel; public float time, stick, requestedSpeed, actualSpeed, rate, residual, leftHeight, rightHeight, leftSlip, rightSlip, rightGrip, leftGrip, facingDot;
            public float gaitPhase, leftPlantError, rightPlantError, maximumCorrection, leftKneeDegrees, rightKneeDegrees;
            public bool footPlantTelemetry, leftContact, rightContact, leftPlanted, rightPlanted;
            public string phase, clip; public Vector2 plane; public Vector3 leftSole, rightSole;
        }
        [Serializable] sealed class GaitReport
        {
            public string conditions = "Continuous actual Unity renders. Deterministic 60Hz production UpdatePlayer inputs, real collision/acceleration, sampled Player.Visuals and Player.LateUpdate; fixed simulation ticks per encoded frame, 1x playback. Owned Game/Player updates paused; forced Cargo floor5, initial placement in clear arena, enemies cleared, god mode/held fire. Main production camera unchanged; second close camera diagnostic only. Not natural phone play or FPS evidence. Optional long review appends separate repositioned steady fixtures: full, existing1.3boost multiplier, .8/.65footing, .25ice friction stop, existing wall hold; modifiers are QA-only and removed. No gameplay setting/balance changes.";
            public float baseSpeed, weaponMoveMul, terrainMoveMul, enemySlow, friction, dashSpeed, dashDuration;
            public int encodedFrames; public List<GaitFrame> frames = new();
        }
        static readonly MethodInfo GaitMovement = typeof(Game).GetMethod("UpdatePlayer", BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly MethodInfo GaitCamera = typeof(Game).GetMethod("UpdateCamera", BindingFlags.Instance|BindingFlags.NonPublic);
        static GaitReport gaitReport; static EngineerGaitFixtureDriver gaitDriver;
        static MediaEncoder gaitMainEncoder, gaitDetailEncoder;
        static Camera gaitDetail; static RenderTexture gaitDetailTarget;
        static Texture2D gaitMainPixels, gaitDetailPixels;
        static bool gaitActive, gaitDone, gaitGameEnabled, gaitPlayerEnabled;
        static int gaitFrame, gaitRenderedFrame = -1;
        static Vector2 gaitCentre; static Transform gaitLeft, gaitRight;
        static Vector3 gaitLeftLocal, gaitRightLocal, gaitPreviousLeft, gaitPreviousRight;
        static float gaitFloor, gaitOldSlow; static int gaitLongSegment=-1;
        static bool GaitLong => Environment.GetEnvironmentVariable("COHESIVE_REVIEW_GAIT_LONG") == "1";
        static Vector2 gaitStraightStart, gaitWallStart; static GaitReviewFooting gaitFooting;
        sealed class GaitReviewFooting : Hazard { public float movement=1,friction=1; public override float MoveMul=>movement;public override float Friction=>friction; }
        static List<Hazard> GaitHazards => (List<Hazard>)typeof(Hazards).GetField("live",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        static void GaitLongSetup()
        {
            int best=0;
            for(int z=1;z<MazeMap.H-1;z++)for(int x=1;x<MazeMap.W-1;x++)if(MazeMap.Walkable(x,z)&&!MazeMap.Walkable(x-1,z))
            {int end=x;while(end<MazeMap.W-1&&MazeMap.Walkable(end,z))end++;if(end-x>best){best=end-x;gaitStraightStart=new Vector2((x+1.5f)*MazeMap.C,(z+.5f)*MazeMap.C);gaitWallStart=new Vector2((end-1.5f)*MazeMap.C,(z+.5f)*MazeMap.C);}}
            Check(best>=8,"Existing clear line supports controlled steady movement fixtures");
            gaitOldSlow=G.EnemySlow;gaitFooting=new GaitReviewFooting();GaitHazards.Add(gaitFooting);gaitLongSegment=-1;
        }
        static void GaitSegment(int segment)
        {
            if(gaitLongSegment==segment)return;gaitLongSegment=segment;
            G.player.p=segment==5?gaitWallStart:gaitStraightStart;G.player.vel=Vector2.zero;G.player.dashT=0;G.player.ResetEngineerMotion();
            G.player.facing=Mathf.PI*.5f;typeof(Player).GetField("heroYaw",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(G.player,Mathf.PI*.5f);
            typeof(Game).GetField("camTarget",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(G,G.player.p);
        }
        static void AddGaitReviewSteps()
        {
            report.conditions = "Engineer gait continuous video fixture; see gait-metrics.json for exact conditions. Temporary protected save restored independently.";
            Add(()=>{G.ToMenu(); Progress.Sim=true; G.menu.ReturnToTitle();Resize(1280,720);Delay(.7);});
            Add(()=>{Click(Title.PlayButton);Delay(.7);});
            Add(()=>{Click(P.HeroesButton);Delay(.3);});
            Add(()=>{Click(Button(P,"Hero_engineer"));Delay(.4);});
            Add(()=>{Click(Button(P,"Back"));Delay(.4);});
            Add(()=>{Click(P.DeployButton);WaitFor(()=>MapSelect.I.Open&&MapSelect.I.StyledBriefButton.IsActive(),12);});
            Add(()=>{SelectActualZone();Delay(.4);});
            Add(()=>{Click(MapSelect.I.StyledBriefButton);Delay(.5);});
            Add(()=>{Click(MapSelect.I.StyledDeployButton);WaitFor(()=>G.state==Game.State.Playing&&G.run&&!G.menu.Busy&&!MapSelect.I.Open,25);});
            Add(()=>{G.DebugFloor(5);G.demo=false;G.godMode=true;G.holdFire=true;Delay(3.2);});
            Add(()=>{G.swarm.Clear();BeginGaitReview();WaitFor(()=>gaitDone,240);});
            Add(()=>{EndGaitReview();Check(gaitReport.encodedFrames>=840,"Continuous 60Hz gait video contains full controlled path");Check(gaitReport.frames.Any(f=>f.actualSpeed>5),"Actual full stick production motion exceeds5m/s");Shot("gait-end","End of continuous controlled gait fixture");});
        }
        static void BeginGaitReview()
        {
            Check(Prefs.Engineer,"Engineer selected through production roster");
            gaitGameEnabled=G.enabled;gaitPlayerEnabled=G.player.enabled;G.enabled=false;G.player.enabled=false;
            gaitCentre = new Vector2(MazeMap.W*MazeMap.C*.5f,MazeMap.H*MazeMap.C*.5f);
            // Choose the most open existing cell. Never edit the collision grid or camera settings.
            float best=-1;Vector2 bestAt=G.player.p;
            for(int z=2;z<MazeMap.H-2;z++)for(int x=2;x<MazeMap.W-2;x++)if(MazeMap.Walkable(x,z))
            {float score=0;for(int dz=-4;dz<=4;dz++)for(int dx=-4;dx<=4;dx++)if(MazeMap.Walkable(x+dx,z+dz))score+=1;score-=Vector2.Distance(new Vector2(x*MazeMap.C,z*MazeMap.C),gaitCentre)*.02f;if(score>best){best=score;bestAt=new Vector2((x+.5f)*MazeMap.C,(z+.5f)*MazeMap.C);}}
            gaitCentre=bestAt;G.player.p=bestAt;G.player.vel=Vector2.zero;G.player.dashT=0;G.player.facing=0;
            G.player.Visuals(0,G.cfg,Vector2.zero,false);HeroLateUpdate.Invoke(G.player,null);
            gaitLeft=G.player.VisualRoot.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="LeftFoot");
            gaitRight=G.player.VisualRoot.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="RightFoot");
            gaitLeftLocal=GaitSole(gaitLeft,"Left");gaitRightLocal=GaitSole(gaitRight,"Right");
            gaitPreviousLeft=gaitLeft.TransformPoint(gaitLeftLocal);gaitPreviousRight=gaitRight.TransformPoint(gaitRightLocal);
            gaitFloor=Mathf.Min(gaitPreviousLeft.y,gaitPreviousRight.y);
            gaitReport=new GaitReport{baseSpeed=G.cfg.player.speed,weaponMoveMul=G.cfg.weapons[G.player.weapon].moveMul,terrainMoveMul=Hazards.MoveMul,enemySlow=G.EnemySlow,friction=Hazards.Friction,dashSpeed=G.cfg.player.dashSpeed,dashDuration=G.cfg.player.dashTime};
            var attrs=new VideoTrackAttributes{frameRate=new MediaRational(60),width=1280,height=720,includeAlpha=false,bitRateMode=VideoBitrateMode.High};
            gaitMainEncoder=new MediaEncoder(Path.Combine(Out,"gait-production-60fps.mp4"),attrs);
            attrs.width=960;gaitDetailEncoder=new MediaEncoder(Path.Combine(Out,"gait-close-60fps.mp4"),attrs);
            gaitMainPixels=new Texture2D(1280,720,TextureFormat.RGBA32,false);gaitDetailPixels=new Texture2D(960,720,TextureFormat.RGBA32,false);
            gaitDetail=new GameObject("OwnedGaitDiagnosticCamera").AddComponent<Camera>();gaitDetail.CopyFrom(main);gaitDetail.GetUniversalAdditionalCameraData().cameraStack.Clear();gaitDetail.cullingMask&=~(1<<5);gaitDetail.depth=main.depth+20;gaitDetail.orthographic=true;gaitDetail.orthographicSize=1.35f;
            gaitDetailTarget=new RenderTexture(960,720,24,RenderTextureFormat.ARGB32);gaitDetailTarget.Create();gaitDetail.targetTexture=gaitDetailTarget;
            if(GaitLong)GaitLongSetup();
            gaitFrame=0;gaitRenderedFrame=-1;gaitDone=false;gaitActive=true;
            RenderPipelineManager.endFrameRendering+=GaitRendered;
            gaitDriver=new GameObject("OwnedProductionGaitInputFixture").AddComponent<EngineerGaitFixtureDriver>();
        }
        static Vector3 GaitSole(Transform foot,string side)
        {
            foreach(var renderer in G.player.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var weights=renderer.sharedMesh.boneWeights;var mesh=new Mesh();renderer.BakeMesh(mesh,true);var vertices=mesh.vertices;var selected=new List<Vector3>();
                bool IsFoot(int i)=>i>=0&&i<renderer.bones.Length&&(renderer.bones[i].name==side+"Foot"||renderer.bones[i].name==side+"Toe");
                for(int i=0;i<vertices.Length&&i<weights.Length;i++){var w=weights[i];float n=(IsFoot(w.boneIndex0)?w.weight0:0)+(IsFoot(w.boneIndex1)?w.weight1:0)+(IsFoot(w.boneIndex2)?w.weight2:0)+(IsFoot(w.boneIndex3)?w.weight3:0);if(n>.75f)selected.Add(renderer.transform.TransformPoint(vertices[i]));}
                Object.DestroyImmediate(mesh);if(selected.Count==0)continue;float floor=selected.Min(v=>v.y);var bottom=selected.Where(v=>v.y<floor+.003f).ToArray();return foot.InverseTransformPoint(bottom.Aggregate(Vector3.zero,(a,b)=>a+b)/bottom.Length);
            }
            throw new InvalidOperationException("Missing boot sole skin vertices: "+side);
        }
        public static void AdvanceGaitReview()
        {
            if(!gaitActive||gaitDone||gaitRenderedFrame==Time.frameCount)return;
            try
            {
                const float dt=1f/60;float t=gaitFrame*dt,stick=0;string phase="idle";
                if(t>=1&&t<3){stick=.15f;phase="walk";}else if(t>=3&&t<5){stick=.38f;phase="jog";}else if(t>=5&&t<9){stick=1;phase="full";}else if(t>=9&&t<11){stick=1;phase="turn-fire";}else if(t>=11&&t<11.25f){stick=1;phase="dash";}else if(t>=12&&t<14){stick=.38f;phase="aim-behind";}else if(t>=14)phase="stop";
                Vector2 offset=gaitCentre-G.player.p;var dir=offset.magnitude>5?offset.normalized:new Vector2(Mathf.Cos(t*.9f),Mathf.Sin(t*.9f));
                if(GaitLong&&t>=15)
                {
                    int segment=Mathf.Min(6,Mathf.FloorToInt((t-15)/2));
                    if(segment<6)GaitSegment(segment);
                    dir=Vector2.right;stick=segment==6?0:1;
                    phase=segment==0?"steady-full":segment==1?"boost130":segment==2?"terrain080":segment==3?"terrain065":segment==4?"ice-stop":segment==5?"wall-held":"wall-stop";
                    gaitFooting.movement=segment==2?.8f:segment==3?.65f:1;gaitFooting.friction=segment==4?.25f:1;
                    G.EnemySlow=gaitOldSlow*(segment==1?1.3f:1);
                    if(segment==4&&t>=24)stick=0;
                }
                float yaw=G.cfg.camera.yaw*Mathf.Deg2Rad;Vector2 Screen(Vector2 d)=>new Vector2(d.x*Mathf.Cos(yaw)-d.y*Mathf.Sin(yaw),-d.x*Mathf.Sin(yaw)-d.y*Mathf.Cos(yaw));
                var input=new InputFrame{move=Screen(dir*stick),dash=gaitFrame==660};
                bool firing=phase=="turn-fire"||phase=="aim-behind";if(firing)input.aim=Screen(phase=="aim-behind"?-dir:new Vector2(dir.x*.866f-dir.y*.5f,dir.x*.5f+dir.y*.866f));
                Vector2 previous=G.player.p;GaitMovement.Invoke(G,new object[]{dt,input});var actual=(G.player.p-previous)/dt;
                G.player.shootT=firing?.5f:0;G.player.hasTarget=firing;G.player.Visuals(dt,G.cfg,Read<Vector2>(G,"moveVel"),false);HeroLateUpdate.Invoke(G.player,null);GaitCamera.Invoke(G,new object[]{dt});
                // Keep the comparable first15s camera exact. Appended outer-lane fixtures look from inside the arena.
                Vector3 centre=G.player.transform.position+Vector3.up*.78f;
                float detailZ=GaitLong&&t>=15&&G.player.p.y<MazeMap.H*MazeMap.C*.5f?-3.5f:3.5f;
                gaitDetail.transform.position=centre+new Vector3(2.5f,1.15f,detailZ);gaitDetail.transform.LookAt(centre);
                var left=gaitLeft.TransformPoint(gaitLeftLocal);var right=gaitRight.TransformPoint(gaitRightLocal);var playback=Read<EngineerHeroPlayback>(G.player,"engineerPlayback");
                var row=new GaitFrame{frame=gaitFrame,enemySlow=G.EnemySlow,terrainMoveMul=Hazards.MoveMul,friction=Hazards.Friction,time=t,phase=phase,stick=stick,requestedSpeed=G.cfg.player.speed*G.cfg.weapons[G.player.weapon].moveMul*Hazards.MoveMul*G.EnemySlow*stick,actualSpeed=actual.magnitude,plane=G.player.p,rate=playback.GaitPlaybackRate,residual=playback.ResidualGroundSpeed,clip=playback.ActiveClip,leftSole=left,rightSole=right,leftHeight=left.y-gaitFloor,rightHeight=right.y-gaitFloor,leftSlip=Vector2.Distance(new Vector2(left.x,left.z),new Vector2(gaitPreviousLeft.x,gaitPreviousLeft.z))/dt,rightSlip=Vector2.Distance(new Vector2(right.x,right.z),new Vector2(gaitPreviousRight.x,gaitPreviousRight.z))/dt,rightGrip=Vector3.Distance(HeroPalm(G.player.Hand),G.player.gun.TransformPoint(CohesiveCharacterPolicy.ChibiGunGrip)),leftGrip=Vector3.Distance(HeroPalm(G.player.HandL),G.player.gun.TransformPoint(CohesiveCharacterPolicy.ChibiGunSupport)),facingDot=actual.sqrMagnitude>.01f?Vector3.Dot(G.player.VisualRoot.forward,MazeMap.ToWorld(actual.normalized)):1};
                var playbackType=playback.GetType();
                float Number(string name)=>playbackType.GetProperty(name)?.GetValue(playback) is float v?v:0;
                bool Flag(string name)=>playbackType.GetProperty(name)?.GetValue(playback) is bool v&&v;
                Vector3 Point(string name)=>playbackType.GetProperty(name)?.GetValue(playback) is Vector3 v?v:Vector3.zero;
                row.leftToe=Point("LeftToeWorld");row.rightToe=Point("RightToeWorld");row.leftHeel=Point("LeftHeelWorld");row.rightHeel=Point("RightHeelWorld");
                row.footPlantTelemetry=playbackType.GetProperty("LeftPlanted")!=null;
                row.gaitPhase=Number("GaitPhase");row.leftPlantError=Number("LeftPlantError");row.rightPlantError=Number("RightPlantError");row.maximumCorrection=Number("MaximumLegCorrection");row.leftContact=Flag("LeftContact");row.rightContact=Flag("RightContact");row.leftPlanted=Flag("LeftPlanted");row.rightPlanted=Flag("RightPlanted");
                float Knee(Transform foot)=>Vector3.Angle(foot.parent.position-foot.parent.parent.position,foot.position-foot.parent.position);
                row.leftKneeDegrees=Knee(gaitLeft);row.rightKneeDegrees=Knee(gaitRight);
                gaitReport.frames.Add(row);gaitPreviousLeft=left;gaitPreviousRight=right;gaitRenderedFrame=Time.frameCount;
            }
            catch(Exception error){heroFixtureFailure="Gait driver: "+error;gaitDone=true;}
        }
        static void GaitRendered(ScriptableRenderContext context,Camera[] cameras)
        {
            if(!gaitActive||gaitDone||gaitRenderedFrame!=Time.frameCount)return;
            try
            {
                void ReadFrame(RenderTexture rt,Texture2D pixels,MediaEncoder encoder){var previous=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();RenderTexture.active=previous;encoder.AddFrame(pixels);}
                ReadFrame(target,gaitMainPixels,gaitMainEncoder);ReadFrame(gaitDetailTarget,gaitDetailPixels,gaitDetailEncoder);
                if(gaitFrame%60==0)File.WriteAllBytes(Path.Combine(Out,"close-"+gaitFrame.ToString("0000")+".png"),gaitDetailPixels.EncodeToPNG());
                gaitFrame++;gaitReport.encodedFrames=gaitFrame;if(gaitFrame>=(GaitLong?1740:900))gaitDone=true;
            }
            catch(Exception error){heroFixtureFailure="Gait capture: "+error;gaitDone=true;}
        }
        static void EndGaitReview()
        {
            if(!gaitActive)return;gaitActive=false;RenderPipelineManager.endFrameRendering-=GaitRendered;
            gaitMainEncoder?.Dispose();gaitMainEncoder=null;gaitDetailEncoder?.Dispose();gaitDetailEncoder=null;
            File.WriteAllText(Path.Combine(Out,"gait-metrics.json"),JsonUtility.ToJson(gaitReport,true));
            if(gaitDriver!=null)Object.DestroyImmediate(gaitDriver.gameObject);if(gaitDetail!=null)Object.DestroyImmediate(gaitDetail.gameObject);
            if(gaitDetailTarget!=null){gaitDetailTarget.Release();Object.DestroyImmediate(gaitDetailTarget);}if(gaitMainPixels!=null)Object.DestroyImmediate(gaitMainPixels);if(gaitDetailPixels!=null)Object.DestroyImmediate(gaitDetailPixels);
            if(gaitFooting!=null){GaitHazards.Remove(gaitFooting);gaitFooting=null;if(G!=null)G.EnemySlow=gaitOldSlow;}
            if(G!=null){G.enabled=gaitGameEnabled;if(G.player!=null)G.player.enabled=gaitPlayerEnabled;}
        }
    }
    [DefaultExecutionOrder(31000)]public sealed class EngineerGaitFixtureDriver:MonoBehaviour{void LateUpdate()=>CohesiveEditorReview.AdvanceGaitReview();}
}
#endif
