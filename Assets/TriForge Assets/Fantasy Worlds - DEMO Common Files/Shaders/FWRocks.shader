Shader "FWRocks "
{
Properties
{
    _Color("Color", Color) = (1,1,1,0)
    _Albedo("Albedo", 2D) = "white" {}
    _Normal("Normal", 2D) = "bump" {}
    _MaskMap("Mask Map", 2D) = "white" {}
    _LayerColor("Layer Color", Color) = (1,1,1,0)
    _LayerAlbedo("Layer Albedo", 2D) = "white" {}
    _LayerNormal("Layer Normal", 2D) = "bump" {}
    _LayerMask("Layer Mask", 2D) = "white" {}
    _LayerNormalScale("Layer Normal Scale", Range(0,2)) = 1
    _LayerTiling("Layer Tiling", Range(0.1,5)) = 1
    _TopMaskIntensity("Top Mask Intensity", Range(0,3)) = 1
    _TopMaskAmount("Top Mask Amount", Range(0,1)) = 0.62
    _TopHeightBlendAmount("Top Height Blend Amount", Range(0,5)) = 1
    _TopHeightUpStrength("Top Height Up Strength", Range(1,20)) = 1
    _TopHeightMapStrength("Top Height Map Strength", Range(1,5)) = 2.851197
    _LayerSmoothness("Layer Smoothness", Range(0,1)) = 1
    [HideInInspector] _texcoord("", 2D) = "white" {}
    [HideInInspector] __dirty("", Int) = 1
}
SubShader
{
    Tags{ "RenderType" = "Opaque" "Queue" = "Geometry+0" }
    Cull Back

    HLSLINCLUDE
    #include "TFW_URP_Common.hlsl"
    TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
    TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
    TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
    TEXTURE2D(_LayerAlbedo); SAMPLER(sampler_LayerAlbedo);
    TEXTURE2D(_LayerNormal); SAMPLER(sampler_LayerNormal);
    TEXTURE2D(_LayerMask); SAMPLER(sampler_LayerMask);
    CBUFFER_START(UnityPerMaterial)
        float4 _Color; float4 _LayerColor; float4 _Albedo_ST; float4 _Normal_ST; float4 _MaskMap_ST;
        float _LayerNormalScale; float _LayerTiling; float _TopMaskIntensity; float _TopMaskAmount;
        float _TopHeightBlendAmount; float _TopHeightUpStrength; float _TopHeightMapStrength; float _LayerSmoothness;
    CBUFFER_END

    struct Attributes
    {
        float4 pos : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float4 uv0 : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct Varyings
    {
        float4 clipPos : SV_POSITION; float2 uv0 : TEXCOORD0; float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2; float3 tangentWS : TEXCOORD3; float3 bitangentWS : TEXCOORD4;
        float3 positionOS : TEXCOORD5; float3 normalOS : TEXCOORD6;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float4 TFW_TriColor(TEXTURE2D_PARAM(tex, smp), float3 pos, float3 nrm, float2 tiling)
    {
        float3 proj = pow(abs(nrm), 1.0);
        proj /= (proj.x+proj.y+proj.z) + 0.00001;
        float3 ns = sign(nrm);
        float4 x = SAMPLE_TEXTURE2D(tex, smp, tiling * pos.zy * float2(ns.x,1));
        float4 y = SAMPLE_TEXTURE2D(tex, smp, tiling * pos.xz * float2(ns.y,1));
        float4 z = SAMPLE_TEXTURE2D(tex, smp, tiling * pos.xy * float2(-ns.z,1));
        return x*proj.x + y*proj.y + z*proj.z;
    }
    float3 TFW_TriNormal(TEXTURE2D_PARAM(tex, smp), float3 pos, float3 nrm, float2 tiling, float scale)
    {
        float3 proj = pow(abs(nrm), 1.0);
        proj /= (proj.x+proj.y+proj.z) + 0.00001;
        float3 ns = sign(nrm);
        float4 x = SAMPLE_TEXTURE2D(tex, smp, tiling * pos.zy * float2(ns.x,1));
        float4 y = SAMPLE_TEXTURE2D(tex, smp, tiling * pos.xz * float2(ns.y,1));
        float4 z = SAMPLE_TEXTURE2D(tex, smp, tiling * pos.xy * float2(-ns.z,1));
        float3 xn = float3(UnpackScaleNormal(x, scale).xy * float2(ns.x,1) + nrm.zy, nrm.x).zyx;
        float3 yn = float3(UnpackScaleNormal(y, scale).xy * float2(ns.y,1) + nrm.xz, nrm.y).xzy;
        float3 zn = float3(UnpackScaleNormal(z, scale).xy * float2(-ns.z,1) + nrm.xy, nrm.z).xyz;
        return normalize(xn*proj.x + yn*proj.y + zn*proj.z);
    }
    Varyings TFW_Vert(Attributes v, bool shadow)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
        o.positionOS = v.pos.xyz; o.normalOS = v.normal; o.uv0 = v.uv0.xy;
        if (shadow)
        {
            o.clipPos = TransformWorldToHClip(TransformObjectToWorld(v.pos.xyz));
        }
        else
        {
            VertexPositionInputs vp = GetVertexPositionInputs(v.pos.xyz);
            VertexNormalInputs vn = GetVertexNormalInputs(v.normal, v.tangent);
            o.clipPos = vp.positionCS; o.positionWS = vp.positionWS;
            o.normalWS = vn.normalWS; o.tangentWS = vn.tangentWS; o.bitangentWS = vn.bitangentWS;
        }
        return o;
    }
    ENDHLSL

    Pass
    {
        Name "ForwardLit" Tags{ "LightMode" = "UniversalForward" }
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.0
        #pragma multi_compile_instancing
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _SHADOWS_SOFT
        Varyings vert(Attributes v){ return TFW_Vert(v, false); }
        half4 frag(Varyings i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            float3 nrmOS = normalize(i.normalOS);
            float2 tiling = _LayerTiling.xx;
            float3 baseN = UnpackNormal(SAMPLE_TEXTURE2D(_Normal, sampler_Normal, i.uv0*_Normal_ST.xy+_Normal_ST.zw));
            half3x3 TBN = half3x3(i.tangentWS, i.bitangentWS, i.normalWS);
            float3 baseNWorld = normalize(TransformTangentToWorld(baseN, TBN));
            float4 layerMaskTex = TFW_TriColor(TEXTURE2D_ARGS(_LayerMask, sampler_LayerMask), i.positionOS, nrmOS, tiling);
            float layerHeight = layerMaskTex.z;
            float t135 = abs(normalize(baseNWorld).y) - _TopMaskAmount;
            float lerp147 = lerp(0.0, pow(layerHeight, _TopHeightBlendAmount)*_TopHeightMapStrength, t135);
            float inner = pow(saturate(pow(max(t135,0),5.0)*_TopHeightUpStrength + lerp147), 0.4) * _TopMaskIntensity;
            float topMask = saturate(pow(inner, 6.2));
            float3 layerNObj = TFW_TriNormal(TEXTURE2D_ARGS(_LayerNormal, sampler_LayerNormal), i.positionOS, nrmOS, tiling, _LayerNormalScale);
            float3 layerNWorld = TransformObjectToWorldDir(layerNObj);
            float3 layerNTS = float3(dot(layerNWorld, i.tangentWS), dot(layerNWorld, i.bitangentWS), dot(layerNWorld, i.normalWS));
            float3 N = normalize(lerp(baseN, layerNTS, topMask));
            half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
            float3 albedo = lerp((_Color * SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, i.uv0*_Albedo_ST.xy+_Albedo_ST.zw)).rgb,
                                 (_LayerColor * TFW_TriColor(TEXTURE2D_ARGS(_LayerAlbedo, sampler_LayerAlbedo), i.positionOS, nrmOS, tiling)).rgb, topMask);
            float4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv0*_MaskMap_ST.xy+_MaskMap_ST.zw);
            half smoothness = lerp(mask.a, layerMaskTex.a * _LayerSmoothness, topMask);
            half occlusion = mask.g;
            InputData id = (InputData)0;
            id.positionWS = i.positionWS; id.normalWS = N; id.viewDirectionWS = V;
            id.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            id.bakedGI = SampleSH(N);
            id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.clipPos);
            half3 color = UniversalFragmentPBR(id, albedo, 0.0, half3(0,0,0), smoothness, occlusion, half3(0,0,0), 1.0);
            return half4(color, 1.0);
        }
        ENDHLSL
    }
    Pass
    {
        Name "ShadowCaster" Tags{ "LightMode" = "ShadowCaster" }
        ZWrite On ColorMask 0
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.0
        #pragma multi_compile_instancing
        Varyings vert(Attributes v){ return TFW_Vert(v, true); }
        half4 frag(Varyings i) : SV_Target { return 0; }
        ENDHLSL
    }
}
Fallback "Hidden/Universal Render Pipeline/FallbackError"
}