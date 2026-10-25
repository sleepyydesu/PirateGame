Shader "PirateGame/Procedural Slash Additive"
{
    Properties
    {
        [HDR] _BaseColor ("Tint", Color) = (1,1,1,1)

        _DepthFadeDistance
        ("Depth Fade Distance", Range(0.001, 2)) = 0.18

        _UVScrollSpeed
        ("UV Scroll Speed", Float) = 0

        _DetailStrength
        ("UV Detail Strength", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "SlashAdditive"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _DepthFadeDistance;
                float _UVScrollSpeed;
                float _DetailStrength;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 screenPos : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs p =
                    GetVertexPositionInputs(input.positionOS);

                output.positionHCS = p.positionCS;

                output.screenPos =
                    ComputeScreenPos(p.positionCS);

                output.eyeDepth =
                    -TransformWorldToView(p.positionWS).z;

                output.uv = input.uv;
                output.color = input.color * _BaseColor;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV =
                    input.screenPos.xy /
                    max(input.screenPos.w, 0.0001);

                float rawSceneDepth =
                    SampleSceneDepth(screenUV);

                #if !UNITY_REVERSED_Z
                    rawSceneDepth = lerp(
                        UNITY_NEAR_CLIP_VALUE,
                        1.0,
                        rawSceneDepth);
                #endif

                float sceneEyeDepth =
                    LinearEyeDepth(
                        rawSceneDepth,
                        _ZBufferParams);

                // Fade the ribbon as it approaches opaque scene geometry.
                float depthGap =
                    max(0.0, sceneEyeDepth - input.eyeDepth);

                float depthFade = saturate(
                    depthGap /
                    max(_DepthFadeDistance, 0.001));

                // Optional subtle UV variation; disabled by default.
                float uvPulse =
                    1.0 -
                    _DetailStrength *
                    (
                        0.5 +
                        0.5 * sin(
                            (
                                input.uv.x +
                                _Time.y * _UVScrollSpeed
                            ) * 45.0
                        )
                    );

                half alpha =
                    input.color.a *
                    depthFade *
                    uvPulse;

                return half4(input.color.rgb, alpha);
            }

            ENDHLSL
        }
    }

    FallBack Off
}