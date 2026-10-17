#ifndef MAZE_CRYO_KIT_INPUT_INCLUDED
#define MAZE_CRYO_KIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// One layout in every pass, including shadow/depth: eligible for the SRP Batcher.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _EmissionColor;
    float4 _BaseMap_ST, _BaseMap_Rotation;
    float4 _NormalMap_ST, _NormalMap_Rotation;
    float4 _RMMap_ST, _RMMap_Rotation;
    float4 _AOMap_ST, _AOMap_Rotation;
    float4 _EmissionMap_ST, _EmissionMap_Rotation;
    float4 _TransmissionMap_ST, _TransmissionMap_Rotation;
    float _BaseMap_UV, _NormalMap_UV, _RMMap_UV, _AOMap_UV;
    float _EmissionMap_UV, _TransmissionMap_UV;
    float _UseBaseMap, _UseNormalMap, _UseRMMap, _UseAOMap;
    float _UseEmissionMap, _UseTransmissionMap;
    float _Metallic, _Roughness, _NormalScale, _AOStrength;
    float _Clearcoat, _ClearcoatRoughness, _Transmission, _IsGlass;
    float _Cull, _ZWrite, _SrcBlend, _DstBlend;
CBUFFER_END

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
TEXTURE2D(_RMMap); SAMPLER(sampler_RMMap);
TEXTURE2D(_AOMap); SAMPLER(sampler_AOMap);
TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
TEXTURE2D(_TransmissionMap); SAMPLER(sampler_TransmissionMap);

float2 CryoUV(float2 uv0, float2 uv1, float channel, float4 st, float4 rotation)
{
    float2 uv = lerp(uv0, uv1, channel);
    return float2(st.x * uv.x + rotation.x * uv.y + st.z,
                  rotation.y * uv.x + st.y * uv.y + st.w);
}
#endif
