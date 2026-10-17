// Chibi pass for the hero (HeroChibi, off by default): clean colour blocks with soft, simple light.
// A wrapped Lambert stepped through a soft two-tone ramp, the scene's ambient, main-light shadows, a gentle Fresnel rim
// and a faint soft dark outline (inverted hull). No normal, metal or detail maps.
Shader "Maze/ChibiToon"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade tint", Color) = (0.72, 0.76, 0.92, 1)
        _RampMid ("Ramp middle", Range(0, 1)) = 0.48
        _RampSoft ("Ramp softness", Range(0.01, 0.5)) = 0.22
        _RimColor ("Rim colour", Color) = (1, 0.97, 0.9, 1)
        _RimStrength ("Rim strength", Float) = 0.32
        _Spec ("Soft highlight", Float) = 0.12
        _OutlineColor ("Outline colour", Color) = (0.08, 0.09, 0.14, 0.6)
        _OutlineWidth ("Outline width (world)", Float) = 0.007
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST; half4 _BaseColor; half4 _ShadeColor; half _RampMid; half _RampSoft; half4 _RimColor; half _RimStrength; half _Spec;
        half4 _OutlineColor; half _OutlineWidth;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ChibiForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 w : TEXCOORD2; half fog : TEXCOORD3; };
            V vert(A i)
            {
                V o; o.w = TransformObjectToWorld(i.pos.xyz); o.n = TransformObjectToWorldNormal(i.n);
                o.pos = TransformWorldToHClip(o.w); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.fog = ComputeFogFactor(o.pos.z); return o;
            }
            half4 frag(V i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 n = normalize(i.n); float3 v = normalize(GetWorldSpaceViewDir(i.w));
                Light L = GetMainLight(TransformWorldToShadowCoord(i.w));
                half wrap = dot(n, L.direction) * 0.5 + 0.5;
                half lit = smoothstep(_RampMid - _RampSoft, _RampMid + _RampSoft, wrap * lerp(0.35, 1, L.shadowAttenuation));
                half3 light = lerp(_ShadeColor.rgb * 0.55, 1, lit) * L.color;
                half3 amb = max(SampleSH(n), 0.25) * 0.55;
                half rim = pow(1 - saturate(dot(n, v)), 3) * _RimStrength;
                half spec = smoothstep(0.86, 0.96, dot(n, normalize(L.direction + v))) * _Spec * lit;
                half3 c = albedo * (light * 0.75 + amb) + (_RimColor.rgb * rim + spec) * L.color;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front ZWrite On Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; };
            V vert(A i) { V o; float3 w = TransformObjectToWorld(i.pos.xyz) + TransformObjectToWorldNormal(i.n) * _OutlineWidth; o.pos = TransformWorldToHClip(w); return o; }
            half4 frag(V i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
