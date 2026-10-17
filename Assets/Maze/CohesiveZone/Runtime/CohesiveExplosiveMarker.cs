using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Cargo scenery and destructible tanks share a local-pipeline mesh. This static
    // warning distinguishes shootable explosives without changing their collision/fuse.
    public sealed class CohesiveExplosiveMarker : MonoBehaviour
    {
        static Mesh darkMesh, fillMesh;
        static Material darkMaterial, fillMaterial;
        Camera camera;
        public static void Attach(Transform tank, float bodyHeight)
        {
            if (tank == null) return;
            if (darkMesh == null)
            {
                var points = new List<Vector3>(); var indices = new List<int>();
                Triangle(points, indices, new Vector3(-.31f,-.27f,.02f), new Vector3(.31f,-.27f,.02f), new Vector3(0,.29f,.02f));
                Quad(points, indices, -.036f,-.065f,.036f,.135f,0);
                Quad(points, indices, -.037f,-.17f,.037f,-.105f,0);
                darkMesh = Make("Cargo explosive warning border and mark", points, indices);
                points = new List<Vector3>(); indices = new List<int>();
                Triangle(points, indices, new Vector3(-.26f,-.23f,.01f), new Vector3(.26f,-.23f,.01f), new Vector3(0,.235f,.01f));
                fillMesh = Make("Cargo explosive warning face", points, indices);
                darkMaterial = Mats.Unlit(new Color32(29,42,68,255)); darkMaterial.enableInstancing = true;
                fillMaterial = Mats.Unlit(new Color32(255,185,47,255)); fillMaterial.enableInstancing = true;
            }
            var root = new GameObject("Cargo explosive warning").transform; root.SetParent(tank,false);
            root.localPosition = Vector3.up * (bodyHeight + .30f);
            Part(root,"Warning outline and exclamation",darkMesh,darkMaterial);
            Part(root,"Amber warning face",fillMesh,fillMaterial);
            root.gameObject.AddComponent<CohesiveExplosiveMarker>();
        }
        static void Part(Transform root,string label,Mesh mesh,Material material)
        {
            var go = new GameObject(label); go.transform.SetParent(root,false); go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
        }
        static void Triangle(List<Vector3> p,List<int> t,Vector3 a,Vector3 b,Vector3 c)
        { int n=p.Count;p.Add(a);p.Add(b);p.Add(c);t.AddRange(new[]{n,n+1,n+2,n,n+2,n+1}); }
        static void Quad(List<Vector3> p,List<int> t,float x0,float y0,float x1,float y1,float z)
        { Triangle(p,t,new Vector3(x0,y0,z),new Vector3(x1,y0,z),new Vector3(x1,y1,z));Triangle(p,t,new Vector3(x0,y0,z),new Vector3(x1,y1,z),new Vector3(x0,y1,z)); }
        static Mesh Make(string name,List<Vector3> p,List<int> t)
        {var m=new Mesh{name=name};m.SetVertices(p);m.SetTriangles(t,0);m.RecalculateBounds();var normals=new Vector3[p.Count];for(int i=0;i<normals.Length;i++)normals[i]=Vector3.back;m.normals=normals;return m;}
        void LateUpdate()
        { if(camera==null)camera=Camera.main;if(camera!=null)transform.rotation=camera.transform.rotation; }
    }
}
