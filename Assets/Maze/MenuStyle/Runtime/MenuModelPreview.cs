using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Maze
{
    // An isolated copy of the actual selected model. Only this copy and its camera/lights use layer 30.
    // Gameplay model, camera, assets and animation settings are never edited.
    public sealed class MenuModelPreview:MonoBehaviour
    {
        readonly System.Collections.Generic.List<Mesh> ownedMeshes = new();
        const int Layer=30;Transform rig,model;Camera camera;RenderTexture texture;Game game;string shown;int warm;
        public Texture Texture=>texture;
        public float FootV {get;private set;}=.08f;
        public void Init(Game g){game=g;rig=new GameObject("MenuPreviewRig").transform;rig.SetParent(transform,false);rig.position=new Vector3(0,-7000,0);texture=new RenderTexture(640,896,24,RenderTextureFormat.ARGB32){name="SelectedHeroMenuPreview",antiAliasing=4};texture.Create();
            camera=new GameObject("MenuPreviewCamera").AddComponent<Camera>();camera.transform.SetParent(rig,false);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<Layer;camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.targetTexture=texture;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            Light(new Vector3(-3,4,-4),new Color(1,.92f,.8f),4);Light(new Vector3(3,2,-1),new Color(.38f,.7f,1),3);}
        void Light(Vector3 p,Color c,float strength){var l=new GameObject("MenuPreviewLight").AddComponent<Light>();l.transform.SetParent(rig,false);l.transform.localPosition=p;l.type=LightType.Point;l.color=c;l.intensity=strength;l.range=14;l.cullingMask=1<<Layer;l.shadows=LightShadows.None;}
        public void Refresh()
        {
            string id=Prefs.Hero+"|"+Prefs.VanguardMeshy+"|"+CohesiveCharacterPolicy.UseRebuiltVanguard;if(id==shown&&model!=null)return;shown=id;
            if(model!=null){model.gameObject.SetActive(false);Destroy(model.gameObject);}ClearOwnedMeshes();var prefab=Resources.Load<GameObject>(CohesiveCharacterPolicy.HeroResource);if(prefab==null)throw new InvalidOperationException("Selected hero model is missing: " + CohesiveCharacterPolicy.HeroResource);
            model=Instantiate(prefab,rig).transform;model.name="SelectedHero";Mats.ConvertGltf(model.gameObject);CohesiveCharacterPolicy.ApplyHeroMaterials(model);model.localPosition=Vector3.zero;model.localRotation=Quaternion.Euler(0,160,0);model.localScale=Vector3.one;
            // Sample the retained walk clip at its most balanced stance; the menu has no weapon attachment.
            var legs=model.GetComponentsInChildren<Transform>().Where(t=>new[]{"upleg","leftleg","rightleg","foot","toe"}.Any(n=>t.name.ToLower().Contains(n))).Select(t=>(t,t.localPosition,t.localRotation)).ToArray();
            foreach(var a in model.GetComponentsInChildren<Animation>()){a.Stop();foreach(AnimationState st in a){st.enabled=false;}var walking=a.Cast<AnimationState>().FirstOrDefault(st=>st.name.ToLower().Contains(Prefs.Engineer?"idle":"walk"));if(walking!=null){walking.enabled=true;walking.weight=1;walking.normalizedTime=.25f;a.Sample();walking.enabled=false;}a.enabled=false;}
            foreach(var a in model.GetComponentsInChildren<Animator>())a.enabled=false;
            if(!Prefs.Engineer)foreach(var leg in legs){leg.t.localPosition=leg.localPosition;leg.t.localRotation=leg.localRotation;}
            foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
            // Capture the current approved Heavy pose as static meshes on the menu copy.
            // This prevents the skin's live bounds or bone updates from changing the framed image.
            foreach(var sk in model.GetComponentsInChildren<SkinnedMeshRenderer>()){
                var mesh=new Mesh();ownedMeshes.Add(mesh);sk.BakeMesh(mesh,false);mesh.RecalculateBounds();
                var frozen=new GameObject("MenuPoseMesh");frozen.transform.SetParent(sk.transform,false);frozen.layer=Layer;
                frozen.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=frozen.AddComponent<MeshRenderer>();mr.sharedMaterials=sk.sharedMaterials;sk.enabled=false;
            }
            var renderers=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();if(renderers.Length==0)throw new InvalidOperationException("Menu hero has no renderers.");
            foreach(var r in renderers){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
            var b=TightBounds(renderers);float scale=2.6f/Mathf.Max(.01f,b.size.y);model.localScale*=scale;b=TightBounds(renderers);model.position-=b.center-(rig.position+Vector3.up*1.3f);
            Debug.Log("MenuPreview bounds normalized: "+TightBounds(renderers)+" model "+model.position+" scale "+model.localScale);
            camera.transform.localPosition=new Vector3(0,1.5f,-6);camera.transform.LookAt(rig.position+Vector3.up*1.3f);camera.orthographicSize=1.54f;camera.aspect=640f/896;camera.enabled=true;warm=3;
            // Use the rendered copy's real projected sole, independent of the selected mesh.
            FootV=1;foreach(var mf in model.GetComponentsInChildren<MeshFilter>()) if(mf.GetComponent<Renderer>() is Renderer rr && rr.enabled)
                foreach(var v in mf.sharedMesh.vertices) FootV=Mathf.Min(FootV,camera.WorldToViewportPoint(mf.transform.TransformPoint(v)).y);
        }
        static Bounds TightBounds(Renderer[] rs){bool first=true;Bounds total=default;foreach(var r in rs){Bounds world;if(r is SkinnedMeshRenderer sk){var mesh=new Mesh();sk.BakeMesh(mesh,false);mesh.RecalculateBounds();var local=mesh.bounds;world=new Bounds(sk.transform.TransformPoint(local.center),Vector3.zero);for(int mask=0;mask<8;mask++){var v=local.center+Vector3.Scale(local.extents,new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1));world.Encapsulate(sk.transform.TransformPoint(v));}Destroy(mesh);}else world=r.bounds;if(first){total=world;first=false;}else total.Encapsulate(world);}return total;}
        void LateUpdate(){if(camera!=null&&warm>0&&--warm==0)camera.enabled=false;}
        void ClearOwnedMeshes(){foreach(var mesh in ownedMeshes)if(mesh!=null)Destroy(mesh);ownedMeshes.Clear();}
        void OnDestroy(){ClearOwnedMeshes();if(texture!=null){texture.Release();Destroy(texture);}if(rig!=null)Destroy(rig.gameObject);}
    }
}
