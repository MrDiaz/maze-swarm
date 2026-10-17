Shader "Maze/PolishPaint" {
 Properties { _BaseColor("Existing damage / fuse tint",Color)=(1,1,1,1) }
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
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   CBUFFER_END
   struct A{float4 p:POSITION;float3 n:NORMAL;half4 color:COLOR;};
   struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half3 color:TEXCOORD2;};
   V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.color=a.color.rgb;return o;}
   half4 frag(V i):SV_Target{
    half3 n=normalize(i.n),key=GetMainLight().direction;half h=saturate(n.y*.5+.5),ndl=saturate(dot(n,key));
    half shadow=MainLightRealtimeShadow(TransformWorldToShadowCoord(i.w));
    half3 ambient=lerp(half3(.47,.51,.67),half3(.8,.82,.97),h)*.50;
    half3 light=ambient+half3(1,.97,.92)*ndl*1.12*lerp(.15,1,shadow);
    half3 view=normalize(GetWorldSpaceViewDir(i.w));half spec=pow(saturate(dot(n,normalize(view+key))),38)*.18*shadow;
    half3 c=i.color*_BaseColor.rgb*light+spec*half3(1,.97,.92);
    return half4(c/(1+c*.10),1);
   }
   ENDHLSL
  }
  UsePass "Maze/LivingSurface/ShadowCaster"
 }
}
