using System;using TMPro;using System.Linq;using System.Collections.Generic;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;
namespace Maze {
 // Isolated 3D room rendered behind the existing Canvas. Never edits gameplay camera, progress or light settings.
 [DefaultExecutionOrder(5000)]
 public sealed class LivingEnvironmentView:MonoBehaviour {
  const int Layer=29;static int nextRoom;Transform world,room,heavy;Camera preview;UnityEngine.UI.RawImage image;Canvas canvas;RenderTexture texture;bool armory;Vector3 heroScale;Light keyLight,savedSun;bool ownsSun;
  readonly List<Material> owned=new();readonly List<Mesh> meshes=new();readonly List<(Transform t,Vector3 p,Quaternion q,int kind)> moving=new();
  public static readonly string[] MappedZoneIds={"cryo-station","verdant-colony","kessler-refinery"};
  public Camera PreviewCamera=>preview;public Transform World=>world;public bool Rendering=>preview!=null&&preview.enabled;public bool IsArmory=>armory;
  public static LivingEnvironmentView Attach(Transform parent,bool workshop=false){var im=Holo.Raw(Holo.Fill(parent,"Living3DEnvironment"),null,Color.white);im.raycastTarget=false;var v=im.gameObject.AddComponent<LivingEnvironmentView>();v.Init(im,workshop);return v;}
  void Init(UnityEngine.UI.RawImage im,bool workshop){
   image=im;armory=workshop;canvas=GetComponentInParent<Canvas>()?.rootCanvas;
   world=new GameObject(workshop?"Living Armory World":"Living Command Ship World").transform;world.position=new Vector3(nextRoom++*80,-9000,0);
   var src=Resources.Load<GameObject>("LivingEnvironments/"+(workshop?"ArmoryWorkshop":"CommandShip"));if(src==null)throw new InvalidOperationException("Authored living room prefab missing");room=Instantiate(src,world,false).transform;room.name=workshop?"Cozy Armory Workshop":"Mobile Command Ship";
   foreach(var r in room.GetComponentsInChildren<Renderer>()){r.sharedMaterials=r.sharedMaterials.Select(CloneRoomMaterial).ToArray();}
   foreach(var t in room.GetComponentsInChildren<Transform>()){int kind=t.name.StartsWith("HoloIsland")&&t.parent==room?1:t.name.StartsWith("CloudDrift")?2:t.name=="ServiceShoulder"?3:0;if(kind>0)moving.Add((t,t.localPosition,t.localRotation,kind));}
   AddHeavy();AddDisplayWeapon(workshop);foreach(var m in owned)if(m.HasProperty("_HeavyContact"))m.SetVector("_HeavyContact",new Vector4(workshop?-4.1f:-4.5f,workshop ? .9f : -1.1f,1.15f,.72f));
   preview=new GameObject("Living Room Camera").AddComponent<Camera>();preview.transform.SetParent(world,false);preview.transform.localPosition=new Vector3(0,6.2f,-14);preview.transform.LookAt(world.position+new Vector3(0,1.55f,1.5f));preview.orthographic=false;preview.fieldOfView=workshop?34:39;preview.nearClipPlane=.1f;preview.farClipPlane=65;preview.cullingMask=1<<Layer;preview.clearFlags=CameraClearFlags.SolidColor;preview.backgroundColor=new Color(.58f,.76f,.89f);preview.allowHDR=false;preview.allowMSAA=true;preview.depth=-20;
   var data=preview.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.renderShadows=true;data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
   keyLight=new GameObject("Room soft key").AddComponent<Light>();keyLight.transform.SetParent(world,false);keyLight.type=LightType.Directional;keyLight.color=new Color(1,.93f,.82f);keyLight.intensity=1;keyLight.shadows=LightShadows.Soft;keyLight.shadowStrength=.8f;keyLight.shadowBias=.025f;keyLight.shadowNormalBias=.18f;keyLight.cullingMask=1<<Layer;keyLight.transform.rotation=Quaternion.LookRotation(-new Vector3(-.46f,.77f,-.49f));keyLight.enabled=false;RenderPipelineManager.beginCameraRendering+=BeforeCamera;RenderPipelineManager.endCameraRendering+=AfterCamera;
   if(!workshop)AddZoneLabels();SetLayers(world);preview.enabled=false;
  }
  void AddZoneLabels(){
   var names=new[]{"HoloIslandCryoStation","HoloIslandVerdantColony","HoloIslandKesslerRefinery"};
   for(int i=0;i<names.Length;i++){
    var zone=SectorDefs.Get(MappedZoneIds[i]);var node=room.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==names[i]);
    if(zone==null||node==null){Debug.LogError("Map table requires existing zone and authored model: "+MappedZoneIds[i]);continue;}
    var go=new GameObject("MapZoneLabel_"+zone.id);go.transform.SetParent(world,false);go.transform.position=new Vector3(node.position.x,world.position.y+(i==0?1.12f:2.65f),node.position.z+(i==0?-.87f:-.15f));go.transform.rotation=preview.transform.rotation;
    var text=go.AddComponent<TextMeshPro>();text.font=Resources.Load<TMP_FontAsset>("BlueUI/Fonts/Body");text.text=zone.name;text.fontSize=text.fontSizeMax=i==0?2.0f:1.55f;text.fontSizeMin=1.25f;text.enableAutoSizing=true;text.alignment=TextAlignmentOptions.Center;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;text.rectTransform.sizeDelta=new Vector2(2.3f,.4f);text.color=BlueUi.Navy;text.renderer.shadowCastingMode=ShadowCastingMode.Off;
   }
  }
  void BeforeCamera(ScriptableRenderContext context,Camera cam){if(cam!=preview||keyLight==null)return;savedSun=RenderSettings.sun;ownsSun=true;keyLight.enabled=true;RenderSettings.sun=keyLight;}
  void AfterCamera(ScriptableRenderContext context,Camera cam){if(cam!=preview)return;RestoreSun();}
  void RestoreSun(){if(ownsSun){RenderSettings.sun=savedSun;ownsSun=false;}if(keyLight!=null)keyLight.enabled=false;}
  Material CloneRoomMaterial(Material src){var found=owned.FirstOrDefault(m=>m.name==src.name);if(found!=null)return found;var m=new Material(src){name=src.name};if(m.HasProperty("_SceneOrigin"))m.SetVector("_SceneOrigin",world.position);owned.Add(m);return m;}
  Material PreviewMaterial(Material src){var m=new Material(Shader.Find("Maze/LivingSurface")){name="Living preview "+src.name};m.SetColor("_BaseColor",src.HasProperty("_BaseColor")?src.GetColor("_BaseColor"):Color.white);if(src.HasProperty("_BaseMap"))m.SetTexture("_BaseMap",src.GetTexture("_BaseMap"));if(src.HasProperty("_BumpMap")&&src.GetTexture("_BumpMap")!=null){m.SetTexture("_BumpMap",src.GetTexture("_BumpMap"));m.SetFloat("_NormalStrength",.65f);}m.SetFloat("_Roughness",.7f);owned.Add(m);return m;}
  void AddHeavy(){
   var baked=new List<(SkinnedMeshRenderer skin,Mesh mesh)>();
   var src=Resources.Load<GameObject>("Models/hero_vanguard_meshy");if(src==null)throw new InvalidOperationException("Approved Heavy is missing");heavy=Instantiate(src,world,false).transform;heavy.name="Heavy — actual approved 3D hero";Mats.ConvertGltf(heavy.gameObject);heavy.localPosition=Vector3.zero;heavy.localRotation=Quaternion.Euler(0,160,0);
   var legs=heavy.GetComponentsInChildren<Transform>().Where(t=>new[]{"upleg","leftleg","rightleg","foot","toe"}.Any(n=>t.name.ToLowerInvariant().Contains(n))).Select(t=>(t,p:t.localPosition,q:t.localRotation)).ToArray();
   foreach(var anim in heavy.GetComponentsInChildren<Animation>()){anim.Stop();foreach(AnimationState state in anim)state.enabled=false;var stance=anim.Cast<AnimationState>().FirstOrDefault(a=>a.name.ToLowerInvariant().Contains("walk"));if(stance!=null){stance.enabled=true;stance.weight=1;stance.normalizedTime=.25f;anim.Sample();stance.enabled=false;}anim.enabled=false;}
   foreach(var a in heavy.GetComponentsInChildren<Animator>())a.enabled=false;foreach(var leg in legs){leg.t.localPosition=leg.p;leg.t.localRotation=leg.q;}
   foreach(var sk in heavy.GetComponentsInChildren<SkinnedMeshRenderer>()){var mesh=new Mesh();sk.BakeMesh(mesh,false);mesh.RecalculateBounds();meshes.Add(mesh);baked.Add((sk,mesh));var frozen=new GameObject("Heavy frozen stance");frozen.transform.SetParent(sk.transform,false);frozen.AddComponent<MeshFilter>().sharedMesh=mesh;var r=frozen.AddComponent<MeshRenderer>();r.sharedMaterials=sk.sharedMaterials.Select(PreviewMaterial).ToArray();sk.enabled=false;}
   foreach(var r in heavy.GetComponentsInChildren<MeshRenderer>()){if(!r.sharedMaterials.All(m=>m.shader.name=="Maze/LivingSurface"))r.sharedMaterials=r.sharedMaterials.Select(PreviewMaterial).ToArray();r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;}
   var rs=heavy.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();var b=BoundsOf(rs);heavy.localScale*= (armory?4.65f:4.15f)/Mathf.Max(.01f,b.size.y);b=BoundsOf(rs);var target=world.position+new Vector3(armory?-4.1f:-4.5f,0,armory ? .9f : -1.1f);heavy.position+=target-new Vector3(b.center.x,b.min.y,b.center.z);PoseHands();var placedScale=heavy.localScale;var placedPosition=heavy.localPosition;heavy.localScale=Vector3.one;heavy.localPosition=Vector3.zero;foreach(var row in baked){row.skin.BakeMesh(row.mesh,false);row.mesh.RecalculateBounds();}heavy.localPosition=placedPosition;heavy.localScale=placedScale;heroScale=heavy.localScale;
  }
  Transform Bone(string name)=>heavy.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name.EndsWith(":"+name,StringComparison.Ordinal));
  void AimHand(string side,Vector3 target,Vector3 pole){
   var upper=Bone(side+"Arm");var lower=Bone(side+"ForeArm");var hand=Bone(side+"Hand");if(upper==null||lower==null||hand==null)return;
   var a=upper.position;var b=lower.position;var c=hand.position;float l1=Vector3.Distance(a,b),l2=Vector3.Distance(b,c);var dir=(target-a).normalized;float d=Mathf.Clamp(Vector3.Distance(a,target),Mathf.Abs(l1-l2)+.001f,l1+l2-.001f);target=a+dir*d;
   var bend=Vector3.ProjectOnPlane(pole-a,dir).normalized;if(bend.sqrMagnitude<.1f)bend=Vector3.right;
   float along=(l1*l1+d*d-l2*l2)/(2*d);var elbow=a+dir*along+bend*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
   upper.rotation=Quaternion.FromToRotation(b-a,elbow-a)*upper.rotation;
   lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation;
   foreach(var t in hand.GetComponentsInChildren<Transform>())if(t.name.EndsWith("2")||t.name.EndsWith("3"))t.localRotation*=Quaternion.Euler(t.name.Contains("Thumb")?12:24,0,0);
  }
  void PoseHands(){
   var o=world.position;
   if(armory){AimHand("Right",o+new Vector3(-4.80f,2.30f,-.35f),o+new Vector3(-5.8f,2.6f,.30f));AimHand("Left",o+new Vector3(-3.60f,2.23f,-.30f),o+new Vector3(-2.65f,2.65f,.40f));}
   else {AimHand("Right",o+new Vector3(-4.85f,2.14f,-1.76f),o+new Vector3(-5.9f,2.4f,-1.0f));AimHand("Left",o+new Vector3(-4.05f,1.75f,-1.92f),o+new Vector3(-3.2f,2.35f,-.70f));}
   var head=Bone("Head");if(head!=null&&armory)head.rotation=Quaternion.AngleAxis(7,heavy.right)*head.rotation;
  }
  void AddDisplayWeapon(bool service){
   var src=Resources.Load<GameObject>("Models/minigun");if(src==null)return;var gun=Instantiate(src,world,false).transform;gun.name=service?"Heavy minigun on service bench":"Heavy command minigun";Mats.ConvertGltf(gun.gameObject);
   if(!service)gun.localRotation=Quaternion.Euler(0,24,-38);
   foreach(var r in gun.GetComponentsInChildren<Renderer>()){r.sharedMaterials=r.sharedMaterials.Select(PreviewMaterial).ToArray();r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;}
   var rs=gun.GetComponentsInChildren<Renderer>();var b=BoundsOf(rs);gun.localScale*=(service?2.8f:2.50f)/Mathf.Max(.01f,b.size.x);b=BoundsOf(rs);
   var target=world.position+(service?new Vector3(-3.1f,1.60f,-.05f):new Vector3(-4.1f,.30f,-1.90f));gun.position+=target-new Vector3(b.center.x,b.min.y,b.center.z);
  }

  public static Bounds BoundsOf(IEnumerable<Renderer> rs){bool first=true;Bounds b=default;foreach(var r in rs){if(first){b=r.bounds;first=false;}else b.Encapsulate(r.bounds);}return b;}
  static void SetLayers(Transform t){foreach(var x in t.GetComponentsInChildren<Transform>(true))x.gameObject.layer=Layer;}
  void Resize(){if(image==null||preview==null)return;var r=image.rectTransform.rect;if(r.width<2||r.height<2)return;float aspect=r.width/r.height;int w=Mathf.RoundToInt(Mathf.Min(1920,Mathf.Max(1024,r.width))),h=Mathf.RoundToInt(w/aspect);if(texture!=null&&texture.width==w&&texture.height==h)return;if(texture!=null){preview.targetTexture=null;texture.Release();Destroy(texture);}texture=new RenderTexture(w,h,16,RenderTextureFormat.ARGB32){name=armory?"Armory live 3D":"Command ship live 3D",antiAliasing=2};texture.Create();preview.targetTexture=texture;preview.aspect=aspect;image.texture=texture;}
  void ReleaseTarget(){if(texture==null)return;if(preview!=null)preview.targetTexture=null;if(image!=null)image.texture=null;texture.Release();Destroy(texture);texture=null;}
  void UpdateVisibility(){if(preview==null)return;bool visible=isActiveAndEnabled&&image!=null&&image.gameObject.activeInHierarchy&&(canvas==null||canvas.enabled);if(visible){Resize();preview.enabled=texture!=null;}else{preview.enabled=false;ReleaseTarget();}}
  void LateUpdate(){UpdateVisibility();if(!Rendering)return;float t=Time.unscaledTime;foreach(var item in moving){if(item.kind==1){item.t.localPosition=item.p+Vector3.up*Mathf.Sin(t*.55f+item.p.x)*.025f;item.t.localRotation=item.q*Quaternion.Euler(0,Mathf.Sin(t*.13f)*2.5f,0);}else if(item.kind==2)item.t.localPosition=item.p+Vector3.right*Mathf.Sin(t*.035f+item.p.x)*.15f;else item.t.localRotation=item.q*Quaternion.Euler(0,0,Mathf.Sin(t*.42f)*1.2f);}if(heavy!=null)heavy.localScale=new Vector3(heroScale.x,heroScale.y*(1+Mathf.Sin(t*1.2f)*.0008f),heroScale.z);}
  void OnEnable(){if(image!=null)UpdateVisibility();}
  void OnDisable(){if(preview!=null)preview.enabled=false;RestoreSun();ReleaseTarget();}
  void OnDestroy(){RenderPipelineManager.beginCameraRendering-=BeforeCamera;RenderPipelineManager.endCameraRendering-=AfterCamera;RestoreSun();ReleaseTarget();if(world!=null)Destroy(world.gameObject);foreach(var m in owned)if(m!=null)Destroy(m);foreach(var m in meshes)if(m!=null)Destroy(m);}
 }
}
