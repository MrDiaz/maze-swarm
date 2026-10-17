Shader "Maze/CryoKit PBR"
{
    Properties
    {
        [MainColor] _BaseColor("Base color (linear)", Vector) = (1,1,1,1)
        _EmissionColor("Emission (linear)", Vector) = (0,0,0,0)
        [MainTexture] _BaseMap("Base color sRGB", 2D) = "white" {}
        _NormalMap("OpenGL XYZ normal (linear)", 2D) = "bump" {}
        _RMMap("G roughness / B metallic (linear)", 2D) = "white" {}
        _AOMap("R contact AO (linear, authored UV1)", 2D) = "white" {}
        _EmissionMap("Emission sRGB", 2D) = "white" {}
        _TransmissionMap("R transmission (linear)", 2D) = "white" {}
        _Metallic("Metallic factor", Range(0,1)) = 1
        _Roughness("Roughness factor", Range(0,1)) = 1
        _NormalScale("Normal scale", Range(0,2)) = 1
        _AOStrength("AO strength", Range(0,1)) = 1
        _Clearcoat("Clearcoat", Range(0,1)) = 0
        _ClearcoatRoughness("Clearcoat roughness", Range(0,1)) = 0
        _Transmission("Transmission factor", Range(0,1)) = 0
        [HideInInspector] _IsGlass("Is glass", Float) = 0
        [HideInInspector] _UseBaseMap("Base map enabled", Float) = 0
        [HideInInspector] _UseNormalMap("Normal map enabled", Float) = 0
        [HideInInspector] _UseRMMap("RM map enabled", Float) = 0
        [HideInInspector] _UseAOMap("AO map enabled", Float) = 0
        [HideInInspector] _UseEmissionMap("Emission map enabled", Float) = 0
        [HideInInspector] _UseTransmissionMap("Transmission map enabled", Float) = 0
        [HideInInspector] _BaseMap_UV("Base UV", Float) = 0
        [HideInInspector] _NormalMap_UV("Normal UV", Float) = 0
        [HideInInspector] _RMMap_UV("RM UV", Float) = 0
        [HideInInspector] _AOMap_UV("AO UV", Float) = 1
        [HideInInspector] _EmissionMap_UV("Emission UV", Float) = 0
        [HideInInspector] _TransmissionMap_UV("Transmission UV", Float) = 0
        [HideInInspector] _BaseMap_Rotation("Base transform off diagonals", Vector) = (0,0,0,0)
        [HideInInspector] _NormalMap_Rotation("Normal transform off diagonals", Vector) = (0,0,0,0)
        [HideInInspector] _RMMap_Rotation("RM transform off diagonals", Vector) = (0,0,0,0)
        [HideInInspector] _AOMap_Rotation("AO transform off diagonals", Vector) = (0,0,0,0)
        [HideInInspector] _EmissionMap_Rotation("Emission transform off diagonals", Vector) = (0,0,0,0)
        [HideInInspector] _TransmissionMap_Rotation("Transmission transform off diagonals", Vector) = (0,0,0,0)
        [HideInInspector] _Cull("Cull", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CryoVertex
            #pragma fragment CryoFragment
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _CLEARCOAT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "CryoKitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct CryoAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct CryoVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float4 uvs : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half4 fogAndVertexLight : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            CryoVaryings CryoVertex(CryoAttributes v)
            {
                CryoVaryings o = (CryoVaryings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uvs = float4(v.uv0, v.uv1);
                o.shadowCoord = GetShadowCoord(p);
                o.fogAndVertexLight = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
                return o;
            }
            half4 CryoFragment(CryoVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv0 = i.uvs.xy, uv1 = i.uvs.zw;
                SurfaceData s = (SurfaceData)0;
                s.albedo = _BaseColor.rgb;
                if (_UseBaseMap > 0.5)
                    s.albedo *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, CryoUV(uv0, uv1, _BaseMap_UV, _BaseMap_ST, _BaseMap_Rotation)).rgb;
                half roughness = _Roughness;
                s.metallic = _Metallic;
                if (_UseRMMap > 0.5)
                {
                    half4 rm = SAMPLE_TEXTURE2D(_RMMap, sampler_RMMap, CryoUV(uv0, uv1, _RMMap_UV, _RMMap_ST, _RMMap_Rotation));
                    roughness *= rm.g;
                    s.metallic *= rm.b;
                }
                s.smoothness = 1 - saturate(roughness);
                s.normalTS = half3(0, 0, 1);
                if (_UseNormalMap > 0.5)
                {
                    // Textures remain raw RGB, never DXT5nm/ASTC normal swizzles.
                    half3 encoded = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, CryoUV(uv0, uv1, _NormalMap_UV, _NormalMap_ST, _NormalMap_Rotation)).rgb;
                    s.normalTS = normalize(half3((encoded.xy * 2 - 1) * _NormalScale, encoded.z * 2 - 1));
                }
                s.occlusion = 1;
                if (_UseAOMap > 0.5)
                    s.occlusion = lerp(1, SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, CryoUV(uv0, uv1, _AOMap_UV, _AOMap_ST, _AOMap_Rotation)).r, _AOStrength);
                s.emission = _EmissionColor.rgb;
                if (_UseEmissionMap > 0.5)
                    s.emission *= SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, CryoUV(uv0, uv1, _EmissionMap_UV, _EmissionMap_ST, _EmissionMap_Rotation)).rgb;
                s.clearCoatMask = _Clearcoat;
                s.clearCoatSmoothness = 1 - _ClearcoatRoughness;
                s.alpha = _BaseColor.a;

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                half3 n = NormalizeNormalPerPixel(i.normalWS);
                half3 t = normalize(i.tangentWS.xyz - n * dot(n, i.tangentWS.xyz));
                half3 b = i.tangentWS.w * cross(n, t);
                d.tangentToWorld = half3x3(t, b, n);
                d.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(s.normalTS, d.tangentToWorld)) * IS_FRONT_VFACE(face, 1, -1);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = i.shadowCoord;
                d.fogCoord = i.fogAndVertexLight.x;
                d.vertexLighting = i.fogAndVertexLight.yzw;
                // Maze rooms are procedural and have no baked lightmaps: use ambient SH/probes.
                // Authored UV1 stays exclusively the kit's contact AO atlas.
                d.bakedGI = SampleSH(d.normalWS);
                d.shadowMask = half4(1, 1, 1, 1);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                if (_IsGlass > 0.5)
                {
                    half transmission = _Transmission;
                    if (_UseTransmissionMap > 0.5)
                        transmission *= SAMPLE_TEXTURE2D(_TransmissionMap, sampler_TransmissionMap, CryoUV(uv0, uv1, _TransmissionMap_UV, _TransmissionMap_ST, _TransmissionMap_Rotation)).r;
                    half grazing = pow(1 - saturate(abs(dot(d.normalWS, d.viewDirectionWS))), 4);
                    // Mobile approximation: frost-dependent opacity + grazing reflection.
                    // Does not pretend to implement volume, refraction, or multisurface transmission.
                    s.alpha = saturate(0.12h + 0.70h * (1 - transmission) + 0.25h * grazing);
                }
                half4 color = UniversalFragmentPBR(d, s);
                color.rgb = MixFog(color.rgb, d.fogCoord);
                color.a = s.alpha;
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "CryoKitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            ShadowVaryings ShadowVertex(ShadowAttributes v)
            {
                ShadowVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float3 l = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    l = normalize(_LightPosition - p);
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, l)));
                return o;
            }
            half4 ShadowFragment(ShadowVaryings i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #include "CryoKitInput.hlsl"
            struct DepthAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            DepthVaryings DepthVertex(DepthAttributes v)
            {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half DepthFragment(DepthVaryings i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); return i.positionCS.z; }
            ENDHLSL
        }
    }
    Fallback Off
}
