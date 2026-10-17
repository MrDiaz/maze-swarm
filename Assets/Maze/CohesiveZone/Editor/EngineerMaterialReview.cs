#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Maze.EditorTools
{
    public static partial class CohesiveEditorReview
    {
        [Serializable]sealed class MaterialProbe
        {
            public string shader,format,graphicsFormat,filter,quality,cleanCandidate,candidateSha256;public int textureWidth,textureHeight,mipCount,aniso,msaa;public float mipBias,renderScale,smoothness;public bool readable,srgb;public List<string> captures=new();
        }
        static MaterialProbe materialProbe;
        static Camera materialCamera;static RenderTexture materialTarget;
        static Material[] materialOriginal,materialCopies;static SkinnedMeshRenderer materialSkin;
        static Texture2D materialTexture,materialCleanTexture;static FilterMode materialFilter;static int materialAniso;static float materialBias;
        static void AddMaterialReviewSteps()
        {
            report.conditions="Controlled actual Engineer material A/B on existing Cargo floor5; unchanged rig009 and pose, same diagnostic camera for every variant; protected temporary save. Additional gameplay-view screenshot. No phone or source-detail recovery claim.";
            Add(()=>{G.ToMenu();Progress.Sim=true;G.menu.ReturnToTitle();Resize(1920,1080);Delay(.7);});
            Add(()=>{Click(Title.PlayButton);Delay(.6);});Add(()=>{Click(P.HeroesButton);Delay(.3);});Add(()=>{Click(Button(P,"Hero_engineer"));Delay(.3);});Add(()=>{Click(Button(P,"Back"));Delay(.3);});
            Add(()=>{Click(P.DeployButton);WaitFor(()=>MapSelect.I.Open&&MapSelect.I.StyledBriefButton.IsActive(),12);});Add(()=>{SelectActualZone();Delay(.3);});Add(()=>{Click(MapSelect.I.StyledBriefButton);Delay(.5);});Add(()=>{Click(MapSelect.I.StyledDeployButton);WaitFor(()=>G.state==Game.State.Playing&&G.run&&!G.menu.Busy&&!MapSelect.I.Open,25);});
            Add(()=>{G.DebugFloor(5);G.demo=false;G.godMode=true;G.holdFire=true;Delay(3);});
            Add(()=>{G.swarm.Clear();BeginHeroFixture();heroFixtureVelocity=Vector2.zero;G.player.facing=2.52f;typeof(Player).GetField("heroYaw",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(G.player,2.52f);Delay(.5);});
            Add(()=>{BeginMaterialProbe();Shot("material-current-gameplay","Production camera and1920x1080 render fixture; RT1sample explicitly");Delay(.3);});
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COHESIVE_REVIEW_CLEAN_ALBEDO")))
            {
                foreach(var choice in new[]{("original",false,.30f),("clean-texture",true,.30f),("clean-matte",true,.15f)})
                {
                    var variant=choice;
                    Add(()=>{foreach(var mat in materialCopies){mat.SetTexture("_BaseMap",variant.Item2?materialCleanTexture:materialTexture);mat.SetFloat("_Smoothness",variant.Item3);}MaterialYaw(2.52f);Delay(.2);});
                    Add(()=>{MaterialShot(variant.Item1+"-front");MaterialYaw(2.52f+Mathf.PI*.5f);Delay(.2);});
                    Add(()=>{MaterialShot(variant.Item1+"-side");MaterialYaw(2.52f+Mathf.PI);Delay(.2);});
                    Add(()=>{MaterialShot(variant.Item1+"-back");});
                }
                Add(()=>{File.WriteAllText(Path.Combine(Out,"material-probe.json"),JsonUtility.ToJson(materialProbe,true));EndMaterialProbe();EndHeroFixture();});
                return;
            }
            Add(()=>{MaterialShot("current");materialTexture.filterMode=FilterMode.Trilinear;materialTexture.anisoLevel=4;materialTexture.mipMapBias=-.25f;Delay(.2);});
            Add(()=>{MaterialShot("trilinear-aniso4-bias-minus025");materialTexture.filterMode=materialFilter;materialTexture.anisoLevel=materialAniso;materialTexture.mipMapBias=materialBias;foreach(var m in materialCopies)if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.12f);Delay(.2);});
            Add(()=>{MaterialShot("matte012");materialTexture.filterMode=FilterMode.Trilinear;materialTexture.anisoLevel=4;materialTexture.mipMapBias=-.25f;Delay(.2);});
            Add(()=>{MaterialShot("combined");File.WriteAllText(Path.Combine(Out,"material-probe.json"),JsonUtility.ToJson(materialProbe,true));EndMaterialProbe();EndHeroFixture();});
        }
        static void MaterialYaw(float value)
        {G.player.facing=value;typeof(Player).GetField("heroYaw",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(G.player,value);G.player.Visuals(0,G.cfg,Vector2.zero,false);HeroLateUpdate.Invoke(G.player,null);}
        static void BeginMaterialProbe()
        {
            materialSkin=G.player.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().First();materialOriginal=materialSkin.sharedMaterials;materialCopies=materialOriginal.Select(m=>new Material(m)).ToArray();materialSkin.sharedMaterials=materialCopies;
            var mat=materialCopies[0];materialTexture=mat.GetTexture("_BaseMap") as Texture2D;Check(materialTexture!=null,"Actual imported Engineer albedo exists");materialFilter=materialTexture.filterMode;materialAniso=materialTexture.anisoLevel;materialBias=materialTexture.mipMapBias;
            string cleanPath=Environment.GetEnvironmentVariable("COHESIVE_REVIEW_CLEAN_ALBEDO");
            if(!string.IsNullOrEmpty(cleanPath))
            {
                var bytes=File.ReadAllBytes(cleanPath);materialCleanTexture=new Texture2D(2,2,TextureFormat.RGBA32,true,false);Check(materialCleanTexture.LoadImage(bytes),"Staged clean albedo decoded");materialCleanTexture.Apply(true,true);materialCleanTexture.filterMode=materialFilter;materialCleanTexture.anisoLevel=materialAniso;materialCleanTexture.mipMapBias=materialBias;materialCleanTexture.wrapMode=materialTexture.wrapMode;
                Check(materialCleanTexture.width==2048&&materialCleanTexture.height==2048,"Clean comparison retains2K dimensions");
            }
            var urp=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            materialProbe=new MaterialProbe{shader=mat.shader.name,textureWidth=materialTexture.width,textureHeight=materialTexture.height,format=materialTexture.format.ToString(),graphicsFormat=materialTexture.graphicsFormat.ToString(),filter=materialFilter.ToString(),mipCount=materialTexture.mipmapCount,aniso=materialAniso,mipBias=materialBias,readable=materialTexture.isReadable,srgb=UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(materialTexture.graphicsFormat),smoothness=mat.GetFloat("_Smoothness"),renderScale=urp.renderScale,msaa=urp.msaaSampleCount,quality=Prefs.HighQuality?"HIGH":"LOW",cleanCandidate=cleanPath,candidateSha256=materialCleanTexture!=null?Hash(File.ReadAllBytes(cleanPath)):null};
            materialCamera=new GameObject("OwnedMatchedMaterialCamera").AddComponent<Camera>();materialCamera.CopyFrom(main);materialCamera.GetUniversalAdditionalCameraData().cameraStack.Clear();materialCamera.cullingMask&=~(1<<5);materialCamera.depth=main.depth+20;materialCamera.orthographic=true;materialCamera.orthographicSize=1.3f;
            Vector3 centre=G.player.transform.position+Vector3.up*.8f;materialCamera.transform.position=centre+new Vector3(2.5f,1.15f,3.5f);materialCamera.transform.LookAt(centre);
            materialTarget=new RenderTexture(960,720,24,RenderTextureFormat.ARGB32){antiAliasing=4};materialTarget.Create();materialCamera.targetTexture=materialTarget;
        }
        static void MaterialShot(string name)
        {
            var savedPosition=materialCamera.transform.position;var savedRotation=materialCamera.transform.rotation;float savedSize=materialCamera.orthographicSize;
            void Capture(string suffix)
            {
                materialCamera.Render();var previous=RenderTexture.active;RenderTexture.active=materialTarget;var pixels=new Texture2D(960,720,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,960,720),0,0);pixels.Apply();RenderTexture.active=previous;string path=Path.Combine(Out,"material-"+name+suffix+".png");var bytes=pixels.EncodeToPNG();File.WriteAllBytes(path,bytes);Object.DestroyImmediate(pixels);materialProbe.captures.Add(path);
                report.captures.Add(new Capture{file=path,stage=name+suffix,conditions="Identical actual idle pose/camera960x720,4xMSAA target; only named texture/material variant. Matched view uses actual production view angle and67px projected skin height like supplied reference; no sharpen",width=960,height=720,floor=G.floor,sha256=Hash(bytes)});Save();
            }
            Capture("-close");
            materialCamera.transform.rotation=main.transform.rotation;materialCamera.transform.position=G.player.transform.position+Vector3.up*.8f-main.transform.forward*20;
            var mesh=new Mesh();materialSkin.BakeMesh(mesh,true);float low=1,high=0;foreach(var vertex in mesh.vertices){float y=materialCamera.WorldToViewportPoint(materialSkin.transform.TransformPoint(vertex)).y;low=Mathf.Min(low,y);high=Mathf.Max(high,y);}Object.DestroyImmediate(mesh);
            materialCamera.orthographicSize*=((high-low)*720/67);Capture("-matched67px");
            materialCamera.transform.SetPositionAndRotation(savedPosition,savedRotation);materialCamera.orthographicSize=savedSize;
        }

        static void EndMaterialProbe()
        {
            if(materialTexture!=null){materialTexture.filterMode=materialFilter;materialTexture.anisoLevel=materialAniso;materialTexture.mipMapBias=materialBias;}
            if(materialSkin!=null&&materialOriginal!=null)materialSkin.sharedMaterials=materialOriginal;
            if(materialCopies!=null)foreach(var mat in materialCopies)if(mat!=null)Object.DestroyImmediate(mat);materialCopies=null;
            if(materialCleanTexture!=null)Object.DestroyImmediate(materialCleanTexture);materialCleanTexture=null;
            if(materialCamera!=null)Object.DestroyImmediate(materialCamera.gameObject);if(materialTarget!=null){materialTarget.Release();Object.DestroyImmediate(materialTarget);}
        }
    }
}
#endif
