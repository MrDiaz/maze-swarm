Shader "Maze/CryoSoftSurface"
{
 Properties {
  _BaseColor("Matte pigment (linear)", Color)=(.6,.7,.8,1)
  _EmissionColor("Practical glow",Color)=(0,0,0,1)
  _FloorAOMap("Baked quiet contact depth",2D)="white"{}
  _AOSize("Contact map footprint (m)",Vector)=(44,32,0,0)
  _FloorAO("Use floor contact map",Float)=0
  _AOIntensity("Contact depth",Range(0,1))=.55
  _Gloss("Broad highlight",Range(0,1))=.2
  _AmbientStrength("Cool fill",Float)=.45
  _KeyGain("Directional shape",Float)=1.23
  _WarmSpill("Warm fixture spill",Float)=.20
  _Glass("Optical chamber",Float)=0
  _ShadowSpread("Four-tap shadow spread",Float)=2.5
  _ShadowOpacity("Soft shadow weight",Range(0,1))=.55
  _Shoulder("Highlight shoulder (linear)",Float)=.75
  _SkyFill("Sky fill (linear)",Color)=(.39,.47,.61,1)
  _GroundFill("Ground fill (linear)",Color)=(.16,.22,.34,1)
  _ShadowTint("Shadowed key tint",Color)=(0,0,0,1)
  _KeyColor("Key colour",Color)=(1,.95,.89,1)
  _Sheen("Plastic sheen",Float)=0
  _ShadowTaps("Shadow taps (4 or 8)",Float)=4
  _RefMode("Reference-match lighting (fills/key from the colours above)",Float)=0
  _EmitAfter("Emission after the highlight shoulder (HDR glow)",Float)=0
  _GlassAlpha("Glass body opacity (ref mode)",Float)=.21
  _BaseMap("Albedo map (props; white = the pigment alone)",2D)="white"{}
  _BumpMap("Normal map (props)",2D)="bump"{}
  _NormalStrength("Normal map strength (0 = off)",Float)=0
  _UseVertexAO("Vertex-colour contact (the Blender rooms; 0 for props)",Float)=1
  _TexGlow("Painted lights glow (props: saturated bright texels emit)",Float)=0
  [HideInInspector] _SrcBlend("Source",Float)=1
  [HideInInspector] _DstBlend("Destination",Float)=0
  [HideInInspector] _ZWrite("Depth",Float)=1
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass {
   Name "CryoSoftForward" Tags {"LightMode"="UniversalForward"}
   Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite] Cull Back
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_FloorAOMap); SAMPLER(sampler_FloorAOMap);
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor, _EmissionColor;
   float _FloorAO, _AOIntensity, _Gloss, _Glass, _ShadowSpread, _ShadowOpacity, _AmbientStrength, _KeyGain, _WarmSpill, _Shoulder, _Sheen, _ShadowTaps, _RefMode, _EmitAfter, _GlassAlpha;
   float4 _SkyFill, _GroundFill, _ShadowTint, _KeyColor, _AOSize, _BaseMap_ST;
   float _NormalStrength, _UseVertexAO, _TexGlow;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;half4 color:COLOR;float2 uv:TEXCOORD0;};
   struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;half ao:TEXCOORD2;half fog:TEXCOORD3;float2 uv:TEXCOORD4;half4 tangent:TEXCOORD5;};
   V vert(A i){ V o; o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normalOS);
    o.ao=_UseVertexAO>.5?i.color.r:1;o.fog=ComputeFogFactor(o.positionCS.z);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);
    o.tangent=half4(TransformObjectToWorldDir(i.tangentOS.xyz),i.tangentOS.w*GetOddNegativeScale());return o; }
   half SoftShadow(float3 world){
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    float4 c=TransformWorldToShadowCoord(world);
    if(BEYOND_SHADOW_FAR(c))return 1;
    float2 d=_MainLightShadowmapSize.xy*_ShadowSpread;
    half s=0;
    s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(-.75,-.25),c.z));
    s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(.25,-.75),c.z));
    s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(.75,.25),c.z));
    s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(-.25,.75),c.z));
    half taps=4;
    if(_ShadowTaps>6){   // a tighter 8-tap ring for crisper contact shadows
     s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(0,0),c.z));
     s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(-.4,.4),c.z));
     s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(.4,-.4),c.z));
     s+=SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,sampler_LinearClampCompare,float3(c.xy+d*float2(.4,.4),c.z));
     taps=8;
    }
    return lerp(1,s/taps,_ShadowOpacity*GetMainLightShadowParams().x);
    #else
    return 1;
    #endif
   }
   half4 frag(V i):SV_Target {
    half3 n=normalize(i.normal);
    if(_NormalStrength>0){   // props: Meshy's tangent-space detail on the decimated mesh
     half3 tn=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_NormalStrength);
     half3 t=normalize(i.tangent.xyz);half3 b=cross(n,t)*i.tangent.w;n=normalize(tn.x*t+tn.y*b+tn.z*n);
    }
    half3 albedo=_BaseColor.rgb*SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
    half3 view=GetWorldSpaceNormalizeViewDir(i.world);Light key=GetMainLight();
    half ndl=saturate(dot(n,key.direction));half hemi=saturate(n.y*.5+.5);
    half3 ambient=(_RefMode>.5?lerp(_GroundFill.rgb,_SkyFill.rgb,hemi):lerp(half3(.16,.22,.34),half3(.39,.47,.61),hemi))*_AmbientStrength;
    half ao=i.ao;
    if(_FloorAO>.5)ao=SAMPLE_TEXTURE2D(_FloorAOMap,sampler_FloorAOMap,float2(i.world.x/_AOSize.x,-i.world.z/_AOSize.y)).r;
    ao=lerp(1,saturate(ao),_AOIntensity);
    half shadow=SoftShadow(i.world);
    half3 keyLight=(_RefMode>.5?_KeyColor.rgb:half3(1,.95,.89))*ndl*_KeyGain*lerp(1,ao,.50);
    half3 diffuse=albedo*(ambient*ao+keyLight*shadow+keyLight*(1-shadow)*_ShadowTint.rgb);
    half3 h=normalize(key.direction+view);half spec=pow(saturate(dot(n,h)),lerp(20,70,_Gloss));
    half rim=pow(1-saturate(dot(n,view)),3);
    diffuse+=half3(.68,.78,.91)*spec*_Gloss*.22*shadow+_EmissionColor.rgb*(1-_EmitAfter);
    if(_Sheen>0){   // soft plastic: a broad highlight and a cool rim so rounded edges catch light
     half broad=pow(saturate(dot(n,h)),12)*shadow;
     diffuse+=(_KeyColor.rgb*broad*.35+_SkyFill.rgb*rim*.6*hemi)*_Sheen*_RefMode;
    }
    if(_FloorAO>.5 && _WarmSpill>0){
     float2 q=float2(i.world.x,i.world.z);
     float2 d0=q-float2(20.39,-13.8),d1=q-float2(25.61,-13.8),d2=q-float2(30.39,-27.8),d3=q-float2(33.61,-27.8);
     half spill=exp2(-dot(d0,d0)*1.4)+exp2(-dot(d1,d1)*1.4)+exp2(-dot(d2,d2)*1.4)+exp2(-dot(d3,d3)*1.4);
     diffuse+=half3(.30,.115,.018)*spill*_WarmSpill;
    }
    half alpha=1;
    if(_Glass>.5){diffuse+=half3(.18,.46,.55)*rim+half3(.21,.42,.48)*spec;alpha=(_RefMode>.5?_GlassAlpha:.21)+rim*.65;}
    half3 excess=max(diffuse-_Shoulder,0);diffuse=min(diffuse,_Shoulder)+excess/(1+excess*4);
    diffuse+=_EmissionColor.rgb*_EmitAfter;   // HDR: practical lights go above 1 so the room's bloom picks them up
    if(_TexGlow>0){   // props: the texture's saturated, bright cyan / amber lamps emit (navy is too dark, white too grey)
     half mx=max(albedo.r,max(albedo.g,albedo.b)),mn=min(albedo.r,min(albedo.g,albedo.b));half sat=(mx-mn)/max(mx,1e-3);
     half lamp=saturate((sat-.55)*3)*saturate((mx-.5)*3);diffuse+=albedo/max(_BaseColor.rgb,.01)*lamp*_TexGlow;
    }
    diffuse=MixFog(diffuse,i.fog);return half4(diffuse,alpha);
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
