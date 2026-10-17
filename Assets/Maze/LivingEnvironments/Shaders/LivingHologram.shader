Shader "Maze/LivingHologram" {
 Properties { _BaseColor("Projection tint",Color)=(.18,.72,.95,.10) }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass {
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha One
   ZWrite Off Cull Back
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   CBUFFER_END
   struct A{float4 p:POSITION;float3 n:NORMAL;};
   struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 w:TEXCOORD1;};
   V vert(A i){V o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);return o;}
   half4 frag(V i):SV_Target{half rim=pow(1-saturate(abs(dot(normalize(i.n),normalize(GetWorldSpaceViewDir(i.w))))),2);return half4(_BaseColor.rgb, _BaseColor.a*(.24+.76*rim));}
   ENDHLSL
  }
 }
}
