Shader "TriForge/Fantasy Worlds/FWGrass "
{
Properties
{
    _Smoothness("Smoothness", Range(0,1)) = 1
    _BaseColorSaturation("Base Color Saturation", Range(0,1)) = 1
    _NormalScale("Normal Scale", Float) = 1
    _RootColor("Root Color", Color) = (0.2235294,0.2431373,0.1058824,1)
    _RootMaskStrength("Root Mask Strength", Float) = 2
    _WindStrength("Wind Strength", Range(0,1)) = 1
    _WindRootMaskStrength("Wind Root Mask Strength", Range(0,1)) = 1
    _SecondaryBendingStrength("Secondary Bending Strength", Range(0,1)) = 0
    _WindDirectionMap("Wind Direction Map", 2D) = "white" {}
    _WindMap("Wind Map", 2D) = "white" {}
    _RotationMapInfluence("Rotation Map Influence", Range(0,1)) = 0
    _WindRotationMapSpeed("Wind Rotation Map Speed", Range(0,1)) = 1
    _HFRotationMapInfluence("HF Rotation Map Influence", Range(0,1)) = 0.35
    _MaskMap("Mask Map", 2D) = "white" {}
    _MainBendingStrength("Main Bending Strength", Range(0,5)) = 1
    _NormalMap("Normal Map", 2D) = "bump" {}
    [Toggle(_TFW_FLIPNORMALS)] _FlipBackNormals("Flip Back Normals", Float) = 0
    _Cutoff("Mask Clip Value", Float) = 0.5
    _BaseColor("Base Color", 2D) = "white" {}
    _Color("Color", Color) = (1,1,1,0)
    [Toggle(_TF_ENABLE_WIND_ON)] _TF_ENABLE_WIND("TF_ENABLE_WIND", Float) = 1
    _FadeFalloff("Fade Falloff", Range(1,5)) = 2
    _FadeDistance("Fade Distance", Float) = 30
    [HideInInspector] _texcoord3("", 2D) = "white" {}
    [HideInInspector] _texcoord("", 2D) = "white" {}
    [HideInInspector] __dirty("", Int) = 1
    [Header(Forward Rendering Options)]
    [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
    [ToggleOff] _GlossyReflections("Reflections", Float) = 1.0
}
SubShader
{
    Tags{ "RenderType" = "TransparentCutout" "Queue" = "AlphaTest+0" "DisableBatching" = "True" "UniversalMaterialType" = "Cutout" }
    Cull Off

    HLSLINCLUDE
    #include "TFW_URP_Common.hlsl"
    TEXTURE2D(_BaseColor);   SAMPLER(sampler_BaseColor);
    TEXTURE2D(_NormalMap);   SAMPLER(sampler_NormalMap);
    TEXTURE2D(_MaskMap);     SAMPLER(sampler_MaskMap);
    TEXTURE2D(_WindMap);     SAMPLER(sampler_WindMap);
    TEXTURE2D(_WindDirectionMap); SAMPLER(sampler_WindDirectionMap);
    float3 TF_WIND_DIRECTION; float TF_WIND_STRENGTH; float TF_GRASS_WIND_STRENGTH; float TF_ROTATION_MAP_INFLUENCE;
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor_ST; float4 _NormalMap_ST; float4 _MaskMap_ST;
        float4 _Color; float4 _RootColor;
        float _Smoothness; float _BaseColorSaturation; float _NormalScale; float _RootMaskStrength;
        float _WindStrength; float _WindRootMaskStrength; float _SecondaryBendingStrength;
        float _RotationMapInfluence; float _WindRotationMapSpeed; float _HFRotationMapInfluence;
        float _MainBendingStrength; float _FadeFalloff; float _FadeDistance; float _Cutoff;
    CBUFFER_END

    struct Attributes
    {
        float4 pos : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT;
        float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; float4 uv3 : TEXCOORD3;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct Varyings
    {
        float4 clipPos : SV_POSITION; float2 uv0 : TEXCOORD0; float2 uv3 : TEXCOORD1;
        float3 positionWS : TEXCOORD2; float3 normalWS : TEXCOORD3;
        float3 tangentWS : TEXCOORD4; float3 bitangentWS : TEXCOORD5;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float3 GrassWindOffset(float3 posOS, float4 uv1, float uv2y, float uv3x)
    {
        float rootMask = saturate(pow(abs((1.0 - uv2y)), _WindRootMaskStrength));
        float3 windVec = TFW_WindVec(TF_WIND_DIRECTION);
        float3 worldPos = mul(unity_ObjectToWorld, float4(posOS,1)).xyz;
        float2 baseUV = (worldPos / -200.0).xz;
        float4 dirMap = lerp(SAMPLE_TEXTURE2D_LOD(_WindDirectionMap, sampler_WindDirectionMap, _Time.y*float2(0.04,0)+baseUV, 0),
                             SAMPLE_TEXTURE2D_LOD(_WindDirectionMap, sampler_WindDirectionMap, baseUV, 0), _WindRotationMapSpeed);
        float2 lf = dirMap.rg*2.0 - 1.0;
        float2 hf = dirMap.ba - 0.5;
        float3 dirLF = float3(-lf.x, 0, lf.y);
        float3 dirHF = float3(-hf.x, 0, hf.y);
        float3 windDir = lerp(windVec, lerp(dirLF, dirHF, _HFRotationMapInfluence), _RotationMapInfluence * TF_ROTATION_MAP_INFLUENCE);
        float3 windDirOS = mul(unity_WorldToObject, float4(windDir,0)).xyz;
        float2 p90 = _Time.y*float2(0.02,0) + (worldPos/-200.0).xy;
        float3 objOrigin = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
        float2 p78 = _Time.y*float2(1,0) + ((uv1.xy + 2.0)*50.0 + objOrigin.xz);
        float perlin = snoise(p78);
        float3 pivot = 0.01 * float3(-uv1.x, 0, -uv1.y);
        float windMapR = SAMPLE_TEXTURE2D_LOD(_WindMap, sampler_WindMap, p90, 0).r;
        float angle = radians(((0.3 + windMapR*0.7)*100.0*_MainBendingStrength) + (perlin*_SecondaryBendingStrength*30.0))
                      * _WindStrength * TF_WIND_STRENGTH * TF_GRASS_WIND_STRENGTH * uv3x;
        float3 rotated = RotateAroundAxis(pivot, posOS, normalize(windDirOS), angle);
        return rootMask * (rotated - posOS);
    }
    half GrassAlpha(float2 uv0, float3 positionWS)
    {
        half a = SAMPLE_TEXTURE2D(_BaseColor, sampler_BaseColor, uv0*_BaseColor_ST.xy + _BaseColor_ST.zw).a;
        float distMask = 1.0 - saturate(pow(distance(positionWS, _WorldSpaceCameraPos)/_FadeDistance, _FadeFalloff));
        return lerp(0.0, a, distMask);
    }
    Varyings TFW_Vert(Attributes v, bool shadow)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
        float3 pos = v.pos.xyz;
        #ifdef _TF_ENABLE_WIND_ON
        pos += GrassWindOffset(pos, v.uv1, v.uv2.y, v.uv3.x);
        #endif
        if (shadow)
        {
            float3 posWS = TransformObjectToWorld(pos);
            float3 nrmWS = TransformObjectToWorldNormal(v.normal);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            float3 lightDir = normalize(_LightPosition - posWS);
            #else
            float3 lightDir = _LightDirection;
            #endif
            posWS = ApplyShadowBias(posWS, nrmWS, lightDir);
            o.clipPos = TransformWorldToHClip(posWS);
            #if UNITY_REVERSED_Z
            o.clipPos.z = min(o.clipPos.z, o.clipPos.w * UNITY_NEAR_CLIP_VALUE);
            #else
            o.clipPos.z = max(o.clipPos.z, -o.clipPos.w * UNITY_NEAR_CLIP_VALUE);
            #endif
        }
        else
        {
            VertexPositionInputs vp = GetVertexPositionInputs(pos);
            VertexNormalInputs vn = GetVertexNormalInputs(v.normal, v.tangent);
            o.clipPos = vp.positionCS; o.positionWS = vp.positionWS;
            o.normalWS = vn.normalWS; o.tangentWS = vn.tangentWS; o.bitangentWS = vn.bitangentWS;
        }
        o.uv0 = v.uv0.xy; o.uv3 = v.uv3.xy;
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
        #pragma shader_feature_local _TF_ENABLE_WIND_ON
        #pragma shader_feature_local _TFW_FLIPNORMALS
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _SHADOWS_SOFT
        Varyings vert(Attributes v){ return TFW_Vert(v, false); }
        half4 frag(Varyings i, bool face : SV_IsFrontFace) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            float3 nts = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv0*_NormalMap_ST.xy+_NormalMap_ST.zw), _NormalScale);
            #ifdef _TFW_FLIPNORMALS
            nts = float3(nts.xy, face ? nts.z : -nts.z);
            #endif
            half3x3 TBN = half3x3(i.tangentWS, i.bitangentWS, i.normalWS);
            half3 N = normalize(TransformTangentToWorld(nts, TBN));
            half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
            float4 base = SAMPLE_TEXTURE2D(_BaseColor, sampler_BaseColor, i.uv0*_BaseColor_ST.xy+_BaseColor_ST.zw);
            float3 desat = lerp(base.rgb, dot(base.rgb, float3(0.299,0.587,0.114)).xxx, 1.0-_BaseColorSaturation);
            float rootMask = pow(abs(saturate(_RootMaskStrength + (1.0 - i.uv3.y))), 1.0);
            float3 albedo = lerp(_RootColor.rgb, (_Color*float4(desat,0)).rgb, rootMask);
            half smoothness = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv0*_MaskMap_ST.xy+_MaskMap_ST.zw).a * _Smoothness;
            half alpha = GrassAlpha(i.uv0, i.positionWS);
            clip(alpha - _Cutoff);
            InputData id = (InputData)0;
            id.positionWS = i.positionWS; id.normalWS = N; id.viewDirectionWS = V;
            id.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            id.bakedGI = SampleSH(N);
            id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.clipPos);
            half3 color = UniversalFragmentPBR(id, albedo, 0.0, half3(0,0,0), smoothness, 1.0, half3(0,0,0), 1.0);
            return half4(color, 1.0);
        }
        ENDHLSL
    }
    Pass
    {
        Name "ShadowCaster" Tags{ "LightMode" = "ShadowCaster" }
        ZWrite On ColorMask 0 Cull Off
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.0
        #pragma multi_compile_instancing
        #pragma shader_feature_local _TF_ENABLE_WIND_ON
        #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
        Varyings vert(Attributes v){ return TFW_Vert(v, true); }
        half4 frag(Varyings i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            clip(GrassAlpha(i.uv0, i.positionWS) - _Cutoff);
            return 0;
        }
        ENDHLSL
    }
}
Fallback "Hidden/Universal Render Pipeline/FallbackError"
}