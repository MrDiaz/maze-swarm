Shader "Maze/FirstCryoParticle"
{
    Properties { _MainTex("Texture", 2D) = "white" {} _Mode("0 RGBA, 1 black fire, 2 alpha only", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; float _Mode;
            CBUFFER_END
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; half4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; half4 c:COLOR; float height:TEXCOORD1; };
            V vert(A i) { V o; float3 world = TransformObjectToWorld(i.p.xyz); o.p = TransformWorldToHClip(world); o.uv = i.uv; o.c = i.c; o.height = world.y; return o; }
            half4 frag(V i):SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half alpha = _Mode > .5 && _Mode < 1.5 ? max(tex.r, max(tex.g, tex.b)) : tex.a;
                half3 color = _Mode > 1.5 ? half3(1,1,1) : _Mode > .5 ? tex.rgb / max(alpha, .025) : tex.rgb;
                return half4(color * i.c.rgb, alpha * i.c.a * smoothstep(.01, .14, i.height));
            }
            ENDHLSL
        }
    }
}
