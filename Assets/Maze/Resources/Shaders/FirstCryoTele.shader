Shader "Maze/FirstCryoTele"
{
    Properties { _BaseColor("Colour", Color)=(1,.3,.15,.6) _Progress("Progress", Range(0,1))=0 _Shape("Shape", Float)=0 _Friendly("Friendly skill", Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; float _Progress, _Shape, _Friendly;
            CBUFFER_END
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float2 local:TEXCOORD1; float2 scale:TEXCOORD2; };
            V vert(A i)
            {
                V o; o.p=TransformObjectToHClip(i.p.xyz); o.uv=i.uv; o.local=i.p.xz;
                float4x4 m=GetObjectToWorldMatrix(); o.scale=float2(length(float3(m[0].x,m[1].x,m[2].x)),length(float3(m[0].y,m[1].y,m[2].y))); return o;
            }
            half4 frag(V i):SV_Target
            {
                half3 color = _Friendly > .5 ? half3(.025,.35,.58) : _BaseColor.rgb;
                float edge=1, detail=0, fill=0;
                if (_Shape < .5)
                {
                    float radius=max(.01,i.scale.x), distance=length(i.local), metres=(1-distance)*radius, aa=fwidth(metres)+.008;
                    float inside=smoothstep(-aa,aa,metres); edge=inside*(1-smoothstep(.09,.18,metres));
                    float front=(_Progress-distance)*radius; detail=smoothstep(-aa,aa,front)*(1-smoothstep(.035,.075,front));
                    float hatch=step(.68,frac((i.local.x+i.local.y)*radius*1.5));
                    fill=inside*(.08+.08*_Progress+hatch*.065)*step(distance,_Progress);
                }
                else if (_Shape < 1.5)
                {
                    float x=i.uv.x*i.scale.x, y=i.uv.y*i.scale.y, side=min(x,i.scale.x-x);
                    float aa=fwidth(side)+.008; edge=1-smoothstep(.055,.13,side);
                    float arrow=frac(y/1.05-abs(x-i.scale.x*.5)*.75-_Time.y*.75);
                    detail=step(.8,arrow)*.3; fill=.11+step(y,_Progress*i.scale.y)*.12;
                }
                // Dark core keeps the exact danger boundary readable on the pale deck; no additive bloom.
                float alpha=max(edge*.9, max(detail*.62,fill));
                color=lerp(color*.32,color,saturate(detail+fill*2));
                if (_Shape > 1.5) { color = _Friendly > .5 ? half3(.025,.35,.58) : _BaseColor.rgb*.75; alpha=.85; }
                return half4(color, alpha*saturate(max(_Friendly > .5 ? .5 : .68, _BaseColor.a*1.5)));
            }
            ENDHLSL
        }
    }
}
