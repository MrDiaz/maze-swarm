using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Small shared silhouette set: the same smooth glyphs in HUD controls and pickup badges.
    public static class PolishGlyphs
    {
        static readonly Dictionary<string, Sprite> cache = new();
        static readonly float[] barrelRows={-.18f,0,.18f}, chevrons={-.43f,.19f};
        static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var d = b - a; return (p - a - d * Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude)).magnitude - r;
        }
        static float Rounded(Vector2 p, Vector2 centre, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - half + Vector2.one * radius;
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
        }
        static float Shape(string name, Vector2 p)
        {
            if(name=="plate")return Rounded(p,Vector2.zero,new Vector2(.96f,.96f),.20f);
            float d = 10;
            if (name == "barrage")
            {
                d = Rounded(p, new Vector2(-.28f,.02f), new Vector2(.33f,.28f), .12f);
                foreach (float y in barrelRows) d = Mathf.Min(d,Capsule(p,new Vector2(-.04f,y),new Vector2(.72f,y),.07f));
                d = Mathf.Min(d,Rounded(p,new Vector2(.48f,0),new Vector2(.055f,.31f),.04f));
                d = Mathf.Min(d,Capsule(p,new Vector2(-.38f,-.21f),new Vector2(-.52f,-.60f),.105f));
                d = Mathf.Min(d,Capsule(p,new Vector2(-.58f,.05f),new Vector2(-.78f,-.09f),.13f));
                d = Mathf.Min(d,Capsule(p,new Vector2(-.40f,.36f),new Vector2(-.10f,.36f),.06f));
            }
            else if (name == "dash")
            {
                foreach (float x in chevrons)
                {
                    d = Mathf.Min(d,Capsule(p,new Vector2(x-.20f,.40f),new Vector2(x+.16f,0),.115f));
                    d = Mathf.Min(d,Capsule(p,new Vector2(x+.16f,0),new Vector2(x-.20f,-.40f),.115f));
                }
            }
            else if (name == "freeze")
            {
                for (int arm=0;arm<6;arm++)
                {
                    float a=arm*Mathf.PI/3; var along=new Vector2(Mathf.Cos(a),Mathf.Sin(a)); var side=new Vector2(-along.y,along.x);
                    d=Mathf.Min(d,Capsule(p,Vector2.zero,along*.72f,.07f));
                    d=Mathf.Min(d,Capsule(p,along*.42f,along*.23f+side*.20f,.055f));
                    d=Mathf.Min(d,Capsule(p,along*.42f,along*.23f-side*.20f,.055f));
                }
            }
            return d;
        }
        public static Sprite Get(string name)
        {
            if (name != "barrage" && name != "dash" && name != "freeze" && name != "plate") return null;
            if (cache.TryGetValue(name,out var sprite) && sprite != null) return sprite;
            const int n=256; var pixels=new Color32[n*n];
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                var p=new Vector2(((x+.5f)/n-.5f)*2,((y+.5f)/n-.5f)*2);
                byte alpha=(byte)Mathf.RoundToInt(255*Mathf.Clamp01(.5f-Shape(name,p)*n*.5f)); pixels[y*n+x]=new Color32(255,255,255,alpha);
            }
            var texture=new Texture2D(n,n,TextureFormat.RGBA32,false) { name="Polished "+name+" glyph",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false,true);
            sprite=Sprite.Create(texture,new Rect(0,0,n,n),Vector2.one*.5f,100,0,SpriteMeshType.FullRect); sprite.name="Polished "+name;
            return cache[name]=sprite;
        }
    }
}
