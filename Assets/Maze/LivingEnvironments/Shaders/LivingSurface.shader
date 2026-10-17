Shader "Maze/LivingSurface" {
 Properties {
  _BaseColor("Pigment",Color)=(1,1,1,1)
  _BaseMap("Albedo",2D)="white"{}
  _BumpMap("Normal",2D)="bump"{}
  _NormalStrength("Normal strength",Float)=0
  _EmissionColor("Quiet practical light",Color)=(0,0,0,1)
  _ContactMap("Floor contact",2D)="white"{}
  _FloorAO("Floor contact enabled",Float)=0
  _SceneOrigin("Room origin",Vector)=(0,0,0,0)
  _Roughness("Soft highlight",Range(0,1))=.65
  _Sky("Panoramic sky",Float)=0
  _CeilingFill("Warm interior canopy",Float)=0
  _VertexAO("Authored vertex contact",Float)=0
  _HeavyContact("Heavy contact ellipse",Vector)=(-4.5,-1.1,1.1,.7)
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass {
   Tags {"LightMode"="UniversalForward"}
   Cull Back ZWrite On
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);TEXTURE2D(_ContactMap);SAMPLER(sampler_ContactMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor,_EmissionColor,_BaseMap_ST,_SceneOrigin,_HeavyContact;float _NormalStrength,_FloorAO,_Roughness,_Sky,_CeilingFill,_VertexAO;
   CBUFFER_END
   struct A {float4 p:POSITION;float3 n:NORMAL;float4 tangent:TANGENT;float2 uv:TEXCOORD0;half4 contact:COLOR;};
   struct V {float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 world:TEXCOORD1;float2 uv:TEXCOORD2;float4 tangent:TEXCOORD3;half contact:TEXCOORD4;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.tangent=float4(TransformObjectToWorldDir(a.tangent.xyz),a.tangent.w*GetOddNegativeScale());o.contact=lerp(1,max(.38,a.contact.r),_VertexAO);return o;}
   half4 frag(V i):SV_Target {
    if(_CeilingFill>.5)return half4(_BaseColor.rgb*.78,1);
    if(_Sky>.5){half t=saturate((i.world.y-_SceneOrigin.y)/7);half3 horizon=half3(1,.78,.66),zenith=half3(.45,.74,.96);half3 sky=lerp(horizon,zenith,smoothstep(.05,.80,t));return half4(sky,1);}
    half3 n=normalize(i.n);if(_NormalStrength>.01){half3 t=normalize(i.tangent.xyz),b=cross(n,t)*i.tangent.w;half3 nn=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_NormalStrength);n=normalize(nn.x*t+nn.y*b+nn.z*n);}
    half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
    half3 key=GetMainLight().direction;half hemi=saturate(n.y*.5+.5),ndl=saturate(dot(n,key));
    half3 ambient=lerp(half3(.23,.29,.43),half3(.77,.83,.96),hemi)*.48;
    half shadow=MainLightRealtimeShadow(TransformWorldToShadowCoord(i.world));
    half3 light=ambient+half3(1,.91,.78)*ndl*.80*lerp(.34,1,shadow)+half3(.27,.46,.74)*saturate(dot(n,normalize(half3(.7,.3,.6))))*.16;
    half3 bounce=0;half ao=i.contact;if(_FloorAO>.5){float3 q=i.world-_SceneOrigin.xyz;ao*=SAMPLE_TEXTURE2D(_ContactMap,sampler_ContactMap,float2((q.x+13)/26,(q.z+6)/26)).r;bounce=half3(.15,.075,.025)*exp(-pow((q.z-3.5)/2.5,2));float2 hero=(q.xz-_HeavyContact.xy)/max(_HeavyContact.zw,.01);ao*=1-.45*exp(-dot(hero,hero)*2.2);}
    half3 view=normalize(GetWorldSpaceViewDir(i.world));half3 h=normalize(key+view);half spec=pow(saturate(dot(n,h)),22)*(.17+.13*(1-_Roughness));half rim=pow(1-saturate(dot(n,view)),4)*.06;
    half3 c=albedo*(light+bounce)*ao+half3(1,.95,.83)*spec*shadow+half3(.5,.68,.9)*rim+_EmissionColor.rgb;
    float3 local=i.world-_SceneOrigin.xyz;half haze=saturate((local.z-10)/24)*.35;half skyT=saturate(local.y/7);half3 air=lerp(half3(1,.78,.66),half3(.45,.74,.96),smoothstep(.05,.80,skyT));c=lerp(c,air,haze);return half4(min(c,1.35),1);
   }
   ENDHLSL
  }
  Pass {
   Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
   ZWrite On ZTest LEqual ColorMask 0 Cull Back
   HLSLPROGRAM
   #pragma vertex shadowVert
   #pragma fragment shadowFrag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
   float3 _LightDirection;
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
   float4 shadowVert(A i):SV_POSITION {float3 p=TransformObjectToWorld(i.positionOS.xyz);float3 n=TransformObjectToWorldNormal(i.normalOS);float4 q=TransformWorldToHClip(ApplyShadowBias(p,n,_LightDirection));
    #if UNITY_REVERSED_Z
    q.z=min(q.z,UNITY_NEAR_CLIP_VALUE*q.w);
    #else
    q.z=max(q.z,UNITY_NEAR_CLIP_VALUE*q.w);
    #endif
    return q;}
   half4 shadowFrag():SV_Target{return 0;}
   ENDHLSL
  }
 }
}
