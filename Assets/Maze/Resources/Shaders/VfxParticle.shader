// Particles for VfxKit (fire, gas, sparks, embers, glows, bubbles, droplets). Works with the Particle System's
// Texture Sheet Animation (it only moves uv0). _Additive 1: premultiplied add (fire, sparks, glows); 0: alpha blend (smoke, bubbles).
// Textures are imported raw (sRGB off) and colours are treated in gamma, like the preview page, then written out in linear.
Shader "Maze/VfxParticle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Additive ("Additive", Float) = 0
        _VColLinear ("Vertex colours arrive linear", Float) = 1
        [HideInInspector] _SrcBlend ("Src", Float) = 5
        [HideInInspector] _DstBlend ("Dst", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "Particle"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; float _Additive; float _VColLinear; float _SrcBlend; float _DstBlend;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col; o.uv = TRANSFORM_TEX(i.uv, _MainTex); return o; }
            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 vc = i.col.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                if (_VColLinear > 0.5) vc = LinearToSRGB(vc);
            #endif
                half3 c = saturate(t.rgb * vc);
            #ifndef UNITY_COLORSPACE_GAMMA
                c = SRGBToLinear(c);
            #endif
                if (_Additive > 0.5) return half4(c * i.col.a, 0);
                return half4(c, t.a * i.col.a);
            }
            ENDHLSL
        }
    }
}
