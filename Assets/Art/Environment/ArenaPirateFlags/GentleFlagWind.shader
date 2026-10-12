Shader "PirateGame/Gentle Flag Wind"
{
    Properties
    {
        [MainTexture] _BaseMap("Flag cloth", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Smoothness("Cloth smoothness", Range(0,1)) = 0.08
        _WindAmplitude("Gentle wave amplitude (metres)", Range(0,0.4)) = 0.18
        _WindSpeed("Wave speed", Range(0,3)) = 1.45
        _Flutter("Edge flutter (metres)", Range(0,0.1)) = 0.035
        [HideInInspector] _ClothSize("Cloth size", Vector) = (4.2,2.8,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" }
        Cull Off
        ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            float _WindAmplitude, _WindSpeed, _Flutter;
            float4 _ClothSize;
        CBUFFER_END
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half fog : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        // The UV's zero edge is sewn to the mast. Its displacement and slope stay zero.
        void Wave(inout Attributes a)
        {
            float u = saturate(a.uv.x), v = a.uv.y;
            float envelope = u*u;
            float phase = dot(TransformObjectToWorld(float3(0,0,0)).xz, float2(0.37,0.21));
            float t = _Time.y * _WindSpeed;
            float gust = 0.85 + 0.15*sin(t*0.31 + phase);
            float p = u*7.5 - t + v*0.8 + phase;
            float q = u*15.0 - t*2.1 - v*1.4 + phase*1.7;
            float r = u*5.0 - t*0.7 + phase;
            float z = _WindAmplitude*gust*sin(p) + _Flutter*u*sin(q);
            float dzdu = 2*u*z + envelope*(_WindAmplitude*gust*7.5*cos(p) + _Flutter*sin(q) + _Flutter*u*15*cos(q));
            float dzdv = envelope*(_WindAmplitude*gust*0.8*cos(p) - _Flutter*u*1.4*cos(q));
            float lift = _WindAmplitude*0.14;
            float dydu = lift*(2*u*sin(r) + envelope*5*cos(r));
            float3 dx = float3(0,dydu,dzdu)/_ClothSize.x;
            float3 dy = float3(0,0,dzdv)/_ClothSize.y;
            float3 tangent = a.tangentOS.xyz;
            float3 bitangent = cross(a.normalOS,tangent)*a.tangentOS.w;
            tangent += dx*tangent.x + dy*tangent.y;
            bitangent += dx*bitangent.x + dy*bitangent.y;
            a.normalOS = normalize(cross(tangent,bitangent)*a.tangentOS.w);
            a.positionOS.y += envelope*lift*sin(r);
            a.positionOS.z += envelope*z;
        }
        Varyings FlagVertex(Attributes a)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(a);
            UNITY_TRANSFER_INSTANCE_ID(a,o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            Wave(a);
            VertexPositionInputs p = GetVertexPositionInputs(a.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(a.normalOS);
            o.uv = TRANSFORM_TEX(a.uv,_BaseMap);
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FlagVertex
            #pragma fragment FlagFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 FlagFragment(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                InputData data = (InputData)0;
                data.positionWS = i.positionWS;
                data.normalWS = normalize(i.normalWS)*IS_FRONT_VFACE(face,1,-1);
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    data.shadowCoord = float4(GetNormalizedScreenSpaceUV(i.positionCS),0,1);
                #else
                    data.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #endif
                data.bakedGI = SampleSH(data.normalWS);
                data.vertexLighting = VertexLighting(i.positionWS,data.normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                data.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0,0,1);
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(data,surface);
                color.rgb = MixFog(color.rgb,i.fog);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FlagShadowVertex
            #pragma fragment EmptyFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            Varyings FlagShadowVertex(Attributes a)
            {
                Varyings o = FlagVertex(a);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition-o.positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection)));
                return o;
            }
            half4 EmptyFragment(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FlagVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half DepthFragment(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FlagVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFragment(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n = normalize(i.normalWS)*IS_FRONT_VFACE(face,1,-1);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*0.5+0.5)),0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
