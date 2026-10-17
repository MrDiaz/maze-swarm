// Ground hazard surface (VfxKit): liquid (acid / poison), lava crust with glowing cracks, or charred ground with embers;
// batch 3 adds radiation (3), hive creep (4), water (5), ice (6) and electric current (7). _Power: water depth 0..1, current 0 off .. 1 live.
// Everything is measured in metres from the pool's edge ("e": > 0 inside), so rims and stains stay the same width at any size.
// Shapes: 0 disc (_Half.x = radius), 1 box (_Half = half extents), 2 cell mask (_Mask: blurred cell raster, 0.5 = the cell border).
// Colours are authored in gamma (like the preview page) and converted back to linear at the end.
Shader "Maze/VfxPool"
{
    Properties
    {
        _Noise ("Noise (R fbm, G worley, B cell edges, A low fbm)", 2D) = "gray" {}
        _Mask ("Cell mask", 2D) = "black" {}
        _MaskRect ("Mask rect (x0, z0, 1/w, 1/h) in world", Vector) = (0, 0, 1, 1)
        _MaskScale ("Mask to metres", Float) = 2
        _Half ("Half size (m)", Vector) = (2, 2, 0, 0)
        _Shape ("Shape 0 disc 1 box 2 mask", Float) = 0
        _Mode ("Mode 0 liquid 1 lava 2 char 3 radiation 4 creep 5 water 6 ice 7 current", Float) = 0
        _Power ("Power (water depth, current strength)", Float) = 1
        _Erode ("Edge erosion (m)", Float) = 0.6
        _Life ("Life 0..1 (grow in / fade out)", Float) = 1
        _Flow ("Flow speed", Float) = 1
        _QuietFluid ("Matte Cargo liquid", Float) = 0
        _Seed ("Seed", Float) = 0
        _Deep ("Deep", Color) = (0.07, 0.24, 0.02, 1)
        _Mid ("Mid / caustics", Color) = (0.55, 0.95, 0.12, 1)
        _Rim ("Rim / hot", Color) = (0.82, 1, 0.3, 1)
        _Stain ("Stain (rgb, a = strength)", Color) = (0.09, 0.1, 0.04, 0.62)
        _Dir ("Flow direction (xz)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Pool"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            CBUFFER_START(UnityPerMaterial)
            float4 _Noise_ST; float4 _Mask_ST; float4 _MaskRect; float _MaskScale; float4 _Half; float _Shape; float _Mode; float _Erode; float _Life; float _Flow; float _Seed; float _Power; float _QuietFluid;
            half4 _Deep; half4 _Mid; half4 _Rim; half4 _Stain; float4 _Dir;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float2 w : TEXCOORD0; float2 l : TEXCOORD1; };
            V vert(A i)
            {
                V o; float3 w = TransformObjectToWorld(i.pos.xyz); float3 org = TransformObjectToWorld(float3(0, 0, 0));
                o.pos = TransformWorldToHClip(w); o.w = w.xz; o.l = w.xz - org.xz; return o;
            }
            float4 N(float2 p) { return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, p); }
            half3 G(half3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return LinearToSRGB(c);
            #endif
            }
            half3 Out(half3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return SRGBToLinear(c);
            #endif
            }
            half4 frag(V i) : SV_Target
            {
                float2 w = i.w + _Seed; float2 l = i.l;
                float e;
                if (_Shape < 0.5) e = _Half.x * _Life - length(l);
                else if (_Shape < 1.5) e = min(_Half.x - abs(l.x), _Half.y - abs(l.y)) - (1 - _Life) * 2;
                else e = (SAMPLE_TEXTURE2D(_Mask, sampler_Mask, (i.w - _MaskRect.xy) * _MaskRect.zw).r - 0.5) * _MaskScale - (1 - _Life) * 2;
                float n0 = N(w * 0.09).a, n1 = N(w * 0.21).r;
                e += (n0 - 0.5) * _Erode + (n1 - 0.5) * _Erode * 0.45;
                float aa = fwidth(e) * 1.2 + 0.015;
                float inside = smoothstep(-aa, aa, e);
                float depth = smoothstep(0, 0.9, e);
                float t = _Time.y * _Flow;
                float2 warp = (N(w * 0.05 + t * 0.01).rg - 0.5) * (_Mode > 0.5 ? 0.35 : 1.2);
                float2 q = w * 0.32 + warp + _Dir.xy * t * 0.05;
                float e1 = N(q + float2(t * 0.031, t * 0.017)).b, e2 = N(q * 1.41 - float2(t * 0.022, -t * 0.029)).b;
                half3 deep = G(_Deep.rgb), mid = G(_Mid.rgb), rim = G(_Rim.rgb), stain = G(_Stain.rgb);
                half3 col; float alpha;
                if (_Mode < 0.5)
                {   // liquid
                    float web = (1 - smoothstep(0, 0.14, e1)) * 0.6 + (1 - smoothstep(0, 0.1, e2)) * 0.5;
                    float swirl = N(q * 0.5 + t * 0.013).r;
                    col = lerp(rim, deep, depth) * (0.82 + 0.36 * swirl);
                    col += mid * web * (0.35 + 0.65 * depth);
                    float mz = (e - 0.07) / 0.055; float men = exp(-mz * mz);
                    col += rim * men * 0.9;
                    float spec = pow(saturate(N(q * 0.7 + float2(-t * 0.02, t * 0.015)).r * 1.25 - 0.2), 9) * 1.6;
                    col += spec * depth;
                    alpha = 0.94;
                    if (_QuietFluid > 0.5)
                    {
                        // Cargo's rounded matte style: a readable spill with restrained
                        // liquid movement, without the original bright vortex/caustic web.
                        float ripple = 0.5 + 0.5 * sin(length(l) * 12 - t * 1.5 + n0 * 1.5);
                        col = lerp(mid * 0.82, deep, depth * 0.6) + mid * (0.025 * ripple);
                        col = lerp(col, rim, (1 - smoothstep(0, 0.07, abs(e))) * 0.28);
                        alpha = 0.70;
                    }
                }
                else if (_Mode > 2.5)
                {
                    float tt = _Time.y;
                    if (_Mode < 3.5)
                    {   // radiation: sickly glow film, contour pulses rolling out of hot spots, Geiger specks
                        float f = N(w * 0.08).a, band = frac(f * 5 - tt * 0.4);
                        float ring = smoothstep(0, 0.06, band) * (1 - smoothstep(0.1, 0.28, band));
                        float hot = smoothstep(0.55, 0.78, f) * (0.65 + 0.35 * sin(tt * 2.6 + f * 12));
                        float speck = step(0.975, N(w * 2.3 + floor(tt * 9) * 0.37).r);
                        float edge = 1 - smoothstep(0, 0.22, e);
                        col = deep * (0.7 + 0.6 * n1) + mid * ring * 0.75 + rim * (hot * 0.8 + speck + edge * 0.7);
                        alpha = min(saturate(0.42 + 0.3 * ring + 0.35 * hot + 0.5 * edge + speck), 0.92);
                    }
                    else if (_Mode < 4.5)
                    {   // hive creep: lumpy wet biomass, glowing veins with a slow heartbeat
                        float2 o2 = float2(0.25, 0.25);
                        float h = N(w * 0.11).a * 0.7 + N(w * 0.23).r * 0.3, h2 = N((w + o2) * 0.11).a * 0.7 + N((w + o2) * 0.23).r * 0.3;
                        float lit = saturate(0.55 + (h - h2) * 5);
                        float vb = N(w * 0.14 + warp * 0.08).b, vein = 1 - smoothstep(0, 0.09, vb), vdark = (1 - smoothstep(0.09, 0.2, vb)) - vein;
                        float beat = 0.5 + 0.5 * sin(tt * 2.4 - N(w * 0.08).a * 18); beat *= beat;
                        float l2 = lit * lit, l4 = l2 * l2, l8 = l4 * l4;
                        col = lerp(deep, mid, h) * (0.45 + 0.9 * lit) * (1 - vdark * 0.5) + rim * vein * (0.35 + 0.65 * beat) * smoothstep(0, 0.5, e);
                        col += half3(0.9, 0.8, 1) * (l8 * l2) * 0.35 * depth;
                        alpha = 0.96 * smoothstep(-0.04, 0.3, e);
                    }
                    else if (_Mode < 5.5)
                    {   // water: rippled translucent sheet, glints, caustics, broken foam at the edge
                        float2 p1 = w * 0.07 + float2(tt * 0.012, tt * 0.008), p2 = w * 0.09 - float2(tt * 0.01, -tt * 0.012), dd = float2(0.008, 0);
                        float h0 = N(p1).a + N(p2).a, hx = N(p1 + dd).a + N(p2 + dd).a, hz = N(p1 + dd.yx).a + N(p2 + dd.yx).a;
                        float refl = saturate(0.5 + (hx - h0 + hz - h0) * 6);
                        float caus = 1 - smoothstep(0, 0.05, N(w * 0.13 + warp * 0.05 + float2(tt * 0.01, -tt * 0.008)).b);
                        float foam = (1 - smoothstep(0, 0.3, e)) * smoothstep(0.45, 0.7, N(w * 0.35 + tt * 0.03).r + 0.25);
                        float r2 = refl * refl, r4 = r2 * r2, r8 = r4 * r4, r14 = r8 * r4 * r2;
                        col = lerp(deep, mid, refl * 0.7) + rim * caus * 0.22 * depth + (foam * 0.85 + r14 * 0.9);
                        alpha = min(saturate(lerp(0.4, 0.62, depth) + _Power * 0.22 + foam * 0.45 + caus * 0.08), 0.95);
                    }
                    else if (_Mode < 6.5)
                    {   // ice: frosted sheet, deep cracks, white frost rim, glints
                        float fr = N(w * 0.2).r, c1 = 1 - smoothstep(0, 0.06, N(w * 0.12).b), c2 = 1 - smoothstep(0, 0.05, N(w * 0.31 + 3.1).b);
                        col = lerp(mid, rim, fr * 0.6 + 0.2);
                        col = lerp(col, deep, c1 * 0.75 + c2 * 0.35 * depth);
                        col = lerp(col, deep * 1.1, depth * (0.25 + 0.25 * N(w * 0.06).a));
                        float rimF = 1 - smoothstep(0, 0.35, e + (N(w * 1.6).r - 0.5) * 0.25);
                        col = lerp(col, half3(0.97, 0.99, 1), rimF * 0.85);
                        col += step(0.982, N(w * 3.1).r) * (0.5 + 0.5 * sin(tt * 3 + N(w * 1.7).a * 30));
                        alpha = 0.88;
                    }
                    else
                    {   // electric current over grates and the water they touch: crawling arcs that crackle at 14 fps
                        float fz = floor(tt * 14);
                        float a1 = N(w * 0.2 + float2(fz * 0.131, fz * 0.077)).r, a2 = N(w * 0.37 - float2(fz * 0.093, fz * 0.151)).g;
                        float d1 = abs(a1 - 0.5), d2 = abs(a2 - 0.5);
                        float core = (1 - smoothstep(0, 0.012, d1)) + (1 - smoothstep(0, 0.009, d2)) * 0.8;
                        float glow = (1 - smoothstep(0, 0.07, d1)) * 0.6 + (1 - smoothstep(0, 0.05, d2)) * 0.45;
                        col = lerp(mid, rim, saturate(core));
                        alpha = saturate(glow * 0.7 + core + 0.12) * _Power;
                    }
                }
                else
                {   // lava (1) or charred ground with embers (2)
                    bool charred = _Mode > 1.5;
                    float crack = 1 - smoothstep(0.03, (charred ? 0.2 : 0.13) + 0.08 * N(w * 0.15).a, min(e1, e2 * 1.3));
                    float pulse = 0.75 + 0.25 * sin(_Time.y * 1.7 + n0 * 9);
                    float flick = charred ? (0.45 + 0.55 * N(w * 0.3 + float2(_Time.y * 0.9, _Time.y * 0.4)).r) : 1;
                    half3 hot = lerp(mid, rim, crack * pulse);
                    float glow = crack * pulse * flick * (charred ? smoothstep(0, 0.5, e) * 0.8 : 1);
                    col = lerp(deep * (0.75 + 0.5 * n1), hot, saturate(glow));
                    col += (charred ? 0.0h : 1.0h) * rim * 0.6 * (1 - smoothstep(0, 0.4, e));   // the hot lip of the channel
                    alpha = charred ? 0.9 * smoothstep(-0.1, 0.5, e) : 0.97;
                }
                float stainM = (1 - inside) * smoothstep(-0.55, 0, e) * (0.55 + 0.45 * n1);
                half3 outc = lerp(stain, col, inside);
                float a = max(inside * alpha, stainM * _Stain.a) * saturate(_Life * 3);
                return half4(Out(saturate(outc)), a);
            }
            ENDHLSL
        }
    }
}
