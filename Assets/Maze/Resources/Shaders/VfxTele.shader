// Enemy attack warnings (EnemyAI.Tele): a bright edge, a fill that sweeps in as the hit approaches, and stripes (circle) or
// chevrons (lane) flowing toward the danger. Alpha blended so it reads on light and dark floors.
// _Shape 0: circle on ZoneHazard.Disc (radius 1 in object xz, uvs unused; radius in metres from the object scale).
// _Shape 1: lane on the Unity quad (uv.y runs from the attacker to the far end; width / length in metres from the object scale).
// _Shape 2: plain flat colour (thin rings keep the old look).
// _BaseColor keeps the name the Tele code already sets; _Progress (0..1) is set by Tele.TickAll.
Shader "Maze/VfxTele"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1, 0.2, 0.15, 0.6)
        _Progress ("Progress", Range(0, 1)) = 0
        _Shape ("Shape (0 circle, 1 lane, 2 flat)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Tele"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; float _Progress; float _Shape;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 lp : TEXCOORD1; float2 sc : TEXCOORD2; };
            V vert(A i)
            {
                V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.lp = i.pos.xz;
                float4x4 m = GetObjectToWorldMatrix();
                o.sc = float2(length(float3(m[0].x, m[1].x, m[2].x)), length(float3(m[0].y, m[1].y, m[2].y)));
                return o;
            }
            float Band(float lo, float hi, float x) { return smoothstep(0, lo, x) * (1 - smoothstep(lo, hi, x)); }
            half4 frag(V i) : SV_Target
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                half3 c = _BaseColor.rgb;
            #else
                half3 c = LinearToSRGB(_BaseColor.rgb);
            #endif
                float t = _Time.y, a = 1;
                if (_Shape < 0.5)
                {
                    float R = i.sc.x, d = length(i.lp), m = (1 - d) * R;                      // metres in from the rim
                    float aa = fwidth(m) + 0.01, inside = smoothstep(-aa, aa, m);
                    float edge = inside * (1 - smoothstep(0.1, 0.16, m));
                    float fm = (_Progress - d) * R;
                    float fill = smoothstep(-aa, aa, fm) * inside * 0.34;
                    float front = smoothstep(-aa, aa, fm) * (1 - smoothstep(0.04, 0.1, fm)) * step(0.02, _Progress);
                    float s = frac((m + t * 1.4) / 0.7), stripe = smoothstep(0, 0.1, s) * (1 - smoothstep(0.28, 0.38, s)) * inside * 0.16;
                    a = max(max(edge * 0.95, front * 0.9), fill + stripe + 0.1 * inside);
                    c = lerp(c, 1, edge * 0.35 + front * 0.5);
                }
                else if (_Shape < 1.5)
                {
                    float W = i.sc.x, L = i.sc.y, x = i.uv.x * W, y = i.uv.y * L, ex = min(x, W - x), ey = min(y, L - y), m = min(ex, ey);
                    float aa = fwidth(m) + 0.01;
                    float edge = smoothstep(-aa, aa, m) * (1 - smoothstep(0.08, 0.14, ex));
                    float fill = step(y, _Progress * L) * 0.32;
                    float ch = frac((y - abs(x - W * 0.5) * 0.9) / 1.1 - t * 1.8), chev = smoothstep(0, 0.08, ch) * (1 - smoothstep(0.22, 0.3, ch)) * 0.3;
                    a = max(edge * 0.95, fill + chev + 0.1);
                    c = lerp(c, 1, edge * 0.35);
                }
                a *= saturate(_BaseColor.a * 1.6);   // Tele.TickAll ramps alpha to about 0.55 by impact; the preview's full strength
            #ifndef UNITY_COLORSPACE_GAMMA
                c = SRGBToLinear(c);
            #endif
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
