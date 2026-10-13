// Readability pass for the hero (comparison renders only for now): drawn as extra materials on the hero's mesh.
// Pass "Rim": additive cool Fresnel rim. Pass "Outline": faint dark inverted hull pushed out along the normal.
Shader "Maze/HeroRim"
{
    Properties
    {
        _RimColor ("Rim colour", Color) = (0.45, 0.75, 1, 1)
        _RimPower ("Rim power", Float) = 3
        _RimStrength ("Rim strength", Float) = 0.6
        _OutlineColor ("Outline colour", Color) = (0.05, 0.07, 0.1, 0.55)
        _OutlineWidth ("Outline width (world)", Float) = 0.012
        [Toggle] _Outline ("Outline mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Rim"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _RimColor; half _RimPower; half _RimStrength; half4 _OutlineColor; half _OutlineWidth; half _Outline;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 w : TEXCOORD1; };
            V vert(A i)
            {
                V o; float3 w = TransformObjectToWorld(i.pos.xyz);
                float3 n = TransformObjectToWorldNormal(i.n);
                if (_Outline > 0.5) w += n * _OutlineWidth;
                o.pos = TransformWorldToHClip(w); o.n = n; o.w = w; return o;
            }
            half4 frag(V i) : SV_Target
            {
                if (_Outline > 0.5) discard;
                float3 v = normalize(GetWorldSpaceViewDir(i.w));
                half f = pow(1 - saturate(dot(normalize(i.n), v)), _RimPower);
                return half4(_RimColor.rgb * f * _RimStrength, 0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _RimColor; half _RimPower; half _RimStrength; half4 _OutlineColor; half _OutlineWidth; half _Outline;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; };
            V vert(A i)
            {
                V o; float3 w = TransformObjectToWorld(i.pos.xyz) + TransformObjectToWorldNormal(i.n) * _OutlineWidth;
                o.pos = TransformWorldToHClip(w); return o;
            }
            half4 frag(V i) : SV_Target { if (_Outline < 0.5) discard; return _OutlineColor; }
            ENDHLSL
        }
    }
}
