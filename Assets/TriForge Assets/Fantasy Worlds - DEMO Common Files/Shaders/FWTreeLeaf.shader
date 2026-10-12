Shader "TriForge/Fantasy Worlds/FWTreeLeaf "
{
Properties
{
    _Smoothness("Smoothness", Range(0,1)) = 0
    _BaseColor("Base Color", 2D) = "white" {}
    _BendingMaskStrength1("Bending Mask Strength", Range(0.05,2)) = 1.236128
    [Header(Translucency)]
    _Translucency("Strength", Range(0,50)) = 1
    _TransNormalDistortion("Normal Distortion", Range(0,1)) = 0.1
    _TransScattering("Scaterring Falloff", Range(1,50)) = 2
    _TransDirect("Direct", Range(0,1)) = 1
    _TransAmbient("Ambient", Range(0,1)) = 0.2
    _TransShadow("Shadow", Range(0,1)) = 0.9
    _NormalMap("Normal Map", 2D) = "bump" {}
    _NormalScale("Normal Scale", Float) = 1
    _LeafFlutterStrength("Leaf Flutter Strength", Range(0,2)) = 0.3
    _WindOverallStrength("Wind Overall Strength", Range(0,1)) = 1
    _ParentWindStrength("Parent Wind Strength", Range(0,2)) = 0.5
    _ParentWindMapScale("Parent Wind Map Scale", Range(0,5)) = 1
    _AOIntensity("AO Intensity", Range(0,1)) = 1
    _MaskClip("Mask Clip", Range(0,1)) = 0.5588235
    _Color("Color", Color) = (1,1,1,0)
    _VertexAOIntensity("Vertex AO Intensity", Range(0,1)) = 1
    _BaseColorSaturation("Base Color Saturation", Range(0,1)) = 1
    _ChildWindStrength("Child Wind Strength", Range(0,2)) = 0.5
    _ChildWindMapScale("Child Wind Map Scale", Range(0,5)) = 0
    [Toggle(_DISTANCEBASEDMASKCLIP_ON)] _DistanceBasedMaskClip("Distance Based Mask Clip", Float) = 1
    _MainWindStrength("Main Wind Strength", Range(0,2)) = 0.5
    _MainWindScale("Main Wind Scale", Range(0,1)) = 1
    _MainBendMaskStrength("Main Bend Mask Strength", Range(0,5)) = 0
    _Undercolor("Undercolor", Color) = (1,1,1,0)
    _UndercolorAmount("Undercolor Amount", Range(0,1)) = 0.5
    [HideInInspector] _texcoord("", 2D) = "white" {}
    [HideInInspector] __dirty("", Int) = 1
    [Header(Forward Rendering Options)]
    [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
    [ToggleOff] _GlossyReflections("Reflections", Float) = 1.0
}
SubShader
{
    Tags{ "RenderType" = "TransparentCutout" "Queue" = "AlphaTest+0" "UniversalMaterialType" = "Cutout" }
    Cull Off

    HLSLINCLUDE
    #include "TFW_URP_Common.hlsl"
    TEXTURE2D(_BaseColor); SAMPLER(sampler_BaseColor);
    TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
    float3 TF_WIND_DIRECTION; float TF_WIND_STRENGTH;
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor_ST; float4 _NormalMap_ST; float4 _Color; float4 _Undercolor;
        float _Smoothness; float _BendingMaskStrength1; float _Translucency; float _TransNormalDistortion;
        float _TransScattering; float _TransDirect; float _TransAmbient; float _TransShadow;
        float _NormalScale; float _LeafFlutterStrength; float _WindOverallStrength;
        float _ParentWindStrength; float _ParentWindMapScale; float _AOIntensity; float _MaskClip;
        float _VertexAOIntensity; float _BaseColorSaturation; float _ChildWindStrength; float _ChildWindMapScale;
        float _MainWindStrength; float _MainWindScale; float _MainBendMaskStrength; float _UndercolorAmount;
    CBUFFER_END

    struct Attributes
    {
        float4 pos : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT;
        float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2;
        float4 uv3 : TEXCOORD3; float4 uv4 : TEXCOORD4; float4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct Varyings
    {
        float4 clipPos : SV_POSITION; float2 uv0 : TEXCOORD0; float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2; float3 tangentWS : TEXCOORD3; float3 bitangentWS : TEXCOORD4;
        float vertexAO : TEXCOORD5; float underFac : TEXCOORD6;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float3 TreeWindOffset(float3 posOS, float4 uv0, float4 uv1, float4 uv2, float4 uv3, float4 uv4, float3 worldPos)
    {
        float3 append18 = float3(-uv2.y, -uv3.y, uv3.x);
        float3 temp20 = 0.001 * append18;
        float childMask = saturate((dot(temp20,temp20) > 0.0001 ? 1.0 : 0.0) * 100.0);
        float selfBendMask = 1.0 - uv4.y;
        float3 windVec = TFW_WindVec(TF_WIND_DIRECTION);
        float3 append11 = float3(-uv1.x, uv2.x, -uv1.y);
        float3 selfPivot = 0.001 * append11;
        float3 origin = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
        float2 p48 = _Time.y*float2(0,0.85) + ((selfPivot + origin/-2.0).z).xx;
        float perlin48 = snoise(p48*_ChildWindMapScale)*0.5+0.5;
        float childRot = radians(perlin48 * 12.0 * _ChildWindStrength);
        float3 rotated81 = RotateAroundAxis(selfPivot, posOS, windVec, childRot);
        float3 childResult = (childMask * selfBendMask) * (rotated81 - posOS);
        float temp113 = saturate(4.0 * pow(selfBendMask, _BendingMaskStrength1));
        float trunkMask = saturate((dot(selfPivot,selfPivot) > 0.0001 ? 1.0 : 0.0) * 1000.0);
        float3 parentPivot = temp20;
        float3 lerp51 = lerp(selfPivot, parentPivot, childMask);
        float2 p61 = _Time.y*float2(0,0.45) + (lerp51.z).xx;
        float perlin60 = snoise(p61*_ParentWindMapScale)*0.5+0.5;
        float parentRot = radians(pow(abs(perlin60),3.0) * 25.0 * _ParentWindStrength);
        float3 rotated96 = RotateAroundAxis(lerp51, childResult + posOS, windVec, parentRot);
        float mainBendMask = saturate(pow(abs(uv4.x), _MainBendMaskStrength));
        float3 parentResult = childResult + ((((temp113*(1.0-childMask)+childMask)*trunkMask)*(rotated96-posOS))*mainBendMask);
        float2 p71 = _Time.y*float2(0,0.07) + (pow(abs(origin/(-15.0*_MainWindScale)),2.0)).xz;
        float perlin70 = snoise(p71*2.0)*0.5+0.5;
        float mainRot = radians(perlin70 * 25.0 * _MainWindStrength);
        float3 temp125 = parentResult + posOS;
        float3 rotated121 = RotateAroundAxis(float3(0,0,0), temp125, windVec, mainRot);
        float temp148 = pow(mainBendMask, 5.0);
        float2 p86 = _Time.y*float2(-0.2,0.4) + (worldPos/-8.0).xz;
        float perlin85 = snoise(p86*10.0)*0.5+0.5;
        float3 windPart = (parentResult + (rotated121 - temp125)*temp148) * _WindOverallStrength * TF_WIND_STRENGTH;
        float3 flutter = _LeafFlutterStrength * (uv0.y * perlin85) * TF_WIND_STRENGTH * 0.6;
        return windPart + flutter;
    }
    half LeafAlpha(float2 uv0, float3 positionWS)
    {
        half a = SAMPLE_TEXTURE2D(_BaseColor, sampler_BaseColor, uv0*_BaseColor_ST.xy+_BaseColor_ST.zw).a;
        #ifdef _DISTANCEBASEDMASKCLIP_ON
        float threshold = lerp(_MaskClip, _MaskClip*0.4, distance(positionWS, _WorldSpaceCameraPos)/150.0);
        #else
        float threshold = _MaskClip;
        #endif
        return pow(abs(a), threshold);
    }
    Varyings TFW_Vert(Attributes v, bool shadow)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
        float3 worldPos0 = mul(unity_ObjectToWorld, v.pos).xyz;
        float3 pos = v.pos.xyz + TreeWindOffset(v.pos.xyz, v.uv0, v.uv1, v.uv2, v.uv3, v.uv4, worldPos0);
        o.vertexAO = v.color.r;
        o.underFac = saturate(v.normal.y + 2.0*_UndercolorAmount);
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
        }
        else
        {
            VertexPositionInputs vp = GetVertexPositionInputs(pos);
            VertexNormalInputs vn = GetVertexNormalInputs(v.normal, v.tangent);
            o.clipPos = vp.positionCS; o.positionWS = vp.positionWS;
            o.normalWS = vn.normalWS; o.tangentWS = vn.tangentWS; o.bitangentWS = vn.bitangentWS;
        }
        o.uv0 = v.uv0.xy;
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
        #pragma shader_feature_local _DISTANCEBASEDMASKCLIP_ON
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _SHADOWS_SOFT
        Varyings vert(Attributes v){ return TFW_Vert(v, false); }
        half4 frag(Varyings i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            half3x3 TBN = half3x3(i.tangentWS, i.bitangentWS, i.normalWS);
            half3 N = normalize(TransformTangentToWorld(UnpackScaleNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv0*_NormalMap_ST.xy+_NormalMap_ST.zw), _NormalScale), TBN));
            half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
            float4 base = SAMPLE_TEXTURE2D(_BaseColor, sampler_BaseColor, i.uv0*_BaseColor_ST.xy+_BaseColor_ST.zw);
            float3 desat = lerp(base.rgb, dot(base.rgb, float3(0.299,0.587,0.114)).xxx, 1.0-_BaseColorSaturation);
            float3 albedo = lerp(_Undercolor.rgb, _Color.rgb, i.underFac) * desat;
            half alpha = LeafAlpha(i.uv0, i.positionWS);
            clip(alpha - _MaskClip);
            half occlusion = saturate(saturate((1.0-_VertexAOIntensity)+i.vertexAO) + (1.0-_AOIntensity));
            InputData id = (InputData)0;
            id.positionWS = i.positionWS; id.normalWS = N; id.viewDirectionWS = V;
            id.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            id.bakedGI = SampleSH(N);
            id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.clipPos);
            half3 color = UniversalFragmentPBR(id, albedo, 0.0, half3(0,0,0), _Smoothness, occlusion, half3(0,0,0), 1.0);
            // Translucency (ported from LightingStandardCustom)
            Light ml = GetMainLight(id.shadowCoord);
            half3 lightAtten = lerp(ml.color, ml.color * ml.shadowAttenuation, _TransShadow);
            half3 lightDir = ml.direction + N * _TransNormalDistortion;
            half transVdotL = pow(saturate(dot(V, -lightDir)), _TransScattering);
            half3 indirect = SampleSH(N);
            color += albedo * (lightAtten * (transVdotL*_TransDirect + indirect*_TransAmbient) * _Translucency);
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
        #pragma shader_feature_local _DISTANCEBASEDMASKCLIP_ON
        #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
        Varyings vert(Attributes v){ return TFW_Vert(v, true); }
        half4 frag(Varyings i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            clip(LeafAlpha(i.uv0, i.positionWS) - _MaskClip);
            return 0;
        }
        ENDHLSL
    }
}
Fallback "Hidden/Universal Render Pipeline/FallbackError"
}