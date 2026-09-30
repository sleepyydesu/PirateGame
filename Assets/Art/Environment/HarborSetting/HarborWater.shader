Shader "Harbor/Sheltered Water"
{
 Properties
 {
  _ShallowColor ("Shallow water", Color) = (0.16,0.38,0.32,1)
  _DeepColor ("Deep water", Color) = (0.025,0.14,0.18,1)
  _NormalMap ("Small ripples", 2D) = "bump" {}
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
  Pass
  {
   Name "HarborWater"
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
   CBUFFER_START(UnityPerMaterial)
   half4 _ShallowColor;
   half4 _DeepColor;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 depth:TEXCOORD1; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float depth:TEXCOORD1; float fog:TEXCOORD2; };
   Varyings Vert(Attributes v)
   {
    Varyings o;
    float3 p=TransformObjectToWorld(v.positionOS.xyz);
    p.y += (sin(p.x*0.67+p.z*0.38+_Time.y*0.8)+sin(p.x*1.13-p.z*0.59+_Time.y*1.1))*0.014*saturate(v.depth.x*1.5);
    o.positionWS=p; o.positionCS=TransformWorldToHClip(p); o.depth=v.depth.x;
    o.fog=ComputeFogFactor(o.positionCS.z); return o;
   }
   half4 Frag(Varyings i):SV_Target
   {
    clip(i.depth-0.015);
    float2 uv=i.positionWS.xz;
    half3 n1=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv*0.13+_Time.y*float2(0.013,0.007)));
    half3 n2=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv*0.21+_Time.y*float2(-0.008,0.011)));
    float sx=cos(uv.x*1.27+uv.y*0.43+_Time.y*0.8)*0.075;
    float sz=cos(uv.x*0.45-uv.y*1.56+_Time.y*1.1)*0.06;
    half3 n=normalize(half3((n1.x+n2.x)*0.48+sx,1,(n1.y+n2.y)*0.48+sz));
    half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
    half fresnel=0.06+0.65*pow(1-saturate(dot(n,view)),4);
    half3 color=lerp(_ShallowColor.rgb,_DeepColor.rgb,saturate(i.depth/2.6));
    color*=0.75+0.25*saturate(dot(n,sun.direction));
    color=lerp(color,half3(0.42,0.58,0.65),fresnel);
    half spec=pow(saturate(dot(n,normalize(sun.direction+view))),150)*0.7;
    color+=sun.color*spec*sun.shadowAttenuation;
    color*=lerp(0.64,1,sun.shadowAttenuation);
    half foam=(1-smoothstep(0.03,0.22,i.depth))*(0.5+0.5*sin(uv.x*7.1+sin(uv.y*5.2)+_Time.y*0.55));
    color=lerp(color,half3(0.68,0.73,0.64),foam*0.55);
    color=MixFog(color,i.fog);
    return half4(color,lerp(0.45,0.96,saturate(i.depth*1.5)));
   }
   ENDHLSL
  }
 }
}
