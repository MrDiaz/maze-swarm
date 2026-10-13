// Energy beam (VfxKit): laser fences, sentry turret lines, floor glow strips. Additive.
// Mesh (VfxKit.BeamMesh): vertices on the local z axis (0..1), uv.x = side (-1 / +1). The object sits at the start point, looks at
// the end point and is scaled z = length. The vertex shader widens it sideways: toward the camera (an axis billboard, so a fence
// is never seen edge-on) or, with _Flat = 1, flat on the floor.
Shader "Maze/VfxBeam"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _Color ("Colour", Color) = (1, 0.12, 0.2, 1)
        _Width ("Width (m)", Float) = 0.6
        _Core ("Core width (0..1)", Float) = 0.1
        _Intensity ("Intensity", Float) = 1
        _Scroll ("Scroll speed", Float) = 1.4
        _Flat ("Lie flat on the floor", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
            float4 _Noise_ST; half4 _Color; float _Width; float _Core; float _Intensity; float _Scroll; float _Flat;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i)
            {
                float3 a = TransformObjectToWorld(float3(0, 0, 0)), b = TransformObjectToWorld(float3(0, 0, 1));
                float len = max(length(b - a), 1e-4); float3 axis = (b - a) / len;
                float3 p = TransformObjectToWorld(float3(0, 0, i.pos.z));
                float3 s = _Flat > 0.5 ? cross(axis, float3(0, 1, 0)) : cross(axis, normalize(_WorldSpaceCameraPos - p));
                s = normalize(s + 1e-5);
                float side = i.uv.x;
                p += s * side * _Width * 0.5;
                V o; o.pos = TransformWorldToHClip(p); o.uv = float2(side * 0.5 + 0.5, i.pos.z * len); return o;
            }
            float4 N(float2 p) { return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, p); }
            half4 frag(V i) : SV_Target
            {
                float x = i.uv.x * 2 - 1;
                float xc = x / _Core; float core = exp(-xc * xc), glow = exp(-abs(x) * 3.2) * (1 - abs(x));
                float t = _Time.y;
                float n = N(float2(i.uv.y * 0.09 - t * _Scroll, 0.37)).r, n2 = N(float2(i.uv.y * 0.23 + t * _Scroll * 0.7, 0.71)).b;
                float pulse = smoothstep(0.85, 1, frac(i.uv.y * 0.18 - t * 1.6));
            #ifdef UNITY_COLORSPACE_GAMMA
                half3 c0 = _Color.rgb;
            #else
                half3 c0 = LinearToSRGB(_Color.rgb);
            #endif
                half3 c = c0 * glow * (0.55 + 0.7 * n + 0.5 * pulse) + lerp(c0, 1, 0.75) * core * (0.85 + 0.3 * n2);
                c = saturate(c * _Intensity);
            #ifndef UNITY_COLORSPACE_GAMMA
                c = SRGBToLinear(c);
            #endif
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
