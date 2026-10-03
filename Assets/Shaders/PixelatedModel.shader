// URP port of the original Built-in surface shader.
// Property names, default values and the quantized look are kept identical so existing
// .mat assets keep working; only the lighting integration moved to HLSL.
Shader "Custom/Pixelated Model"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _TexturePixels ("Texture Pixel Grid", Float) = 64
        _ColorSteps ("Color Steps", Range(2, 32)) = 8
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.45
        _HighlightThreshold ("Highlight Threshold", Range(0, 1)) = 0.72
        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 0.55
        _HighlightColor ("Highlight Color", Color) = (1, 0.95, 0.8, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.25
        _VertexSnap ("Vertex Snap World Size", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _HighlightColor;
                float _TexturePixels;
                float _ColorSteps;
                float _ShadowThreshold;
                float _ShadowStrength;
                float _HighlightThreshold;
                float _HighlightStrength;
                float _AmbientStrength;
                float _VertexSnap;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            float3 QuantizeColor(float3 color, float steps)
            {
                steps = max(2.0, steps);
                return floor(saturate(color) * (steps - 1.0) + 0.5) / (steps - 1.0);
            }

            // Original vertex stage snapped world position to a grid before transforming.
            float3 SnapWorldPosition(float3 positionWS)
            {
                if (_VertexSnap <= 0.0001)
                    return positionWS;
                return floor(positionWS / _VertexSnap + 0.5) * _VertexSnap;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = SnapWorldPosition(positionWS);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_TexturePixels > 1.0)
                    uv = (floor(uv * _TexturePixels) + 0.5) / _TexturePixels;

                half4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * _Color;
                half3 albedo = QuantizeColor(sampled.rgb, _ColorSteps);

                Light mainLight = GetMainLight(input.shadowCoord);
                half3 normalWS = normalize(input.normalWS);
                half ndotl = saturate(dot(normalWS, mainLight.direction));

                half lit = step(_ShadowThreshold, ndotl);
                half lightAmount = lerp(_ShadowStrength, 1.0h, lit) * mainLight.shadowAttenuation;
                lightAmount = max(lightAmount, _AmbientStrength);

                half highlight = step(_HighlightThreshold, ndotl) * _HighlightStrength;

                half3 color = albedo * mainLight.color * lightAmount;
                color += _HighlightColor.rgb * highlight;
                return half4(color, sampled.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _HighlightColor;
                float _TexturePixels;
                float _ColorSteps;
                float _ShadowThreshold;
                float _ShadowStrength;
                float _HighlightThreshold;
                float _HighlightStrength;
                float _AmbientStrength;
                float _VertexSnap;
            CBUFFER_END

            float3 SnapWorldPosition(float3 positionWS)
            {
                if (_VertexSnap <= 0.0001)
                    return positionWS;
                return floor(positionWS / _VertexSnap + 0.5) * _VertexSnap;
            }

            // Snap is applied in world space, so the shadow pass repeats it to stay aligned
            // with the forward pass instead of casting from unsnapped geometry.
            float4 GetShadowPositionHClip(float3 positionOS)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);
                positionWS = SnapWorldPosition(positionWS);
                return TransformWorldToHClip(positionWS);
            }

            float4 ShadowVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return GetShadowPositionHClip(positionOS.xyz);
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _HighlightColor;
                float _TexturePixels;
                float _ColorSteps;
                float _ShadowThreshold;
                float _ShadowStrength;
                float _HighlightThreshold;
                float _HighlightStrength;
                float _AmbientStrength;
                float _VertexSnap;
            CBUFFER_END

            float3 SnapWorldPosition(float3 positionWS)
            {
                if (_VertexSnap <= 0.0001)
                    return positionWS;
                return floor(positionWS / _VertexSnap + 0.5) * _VertexSnap;
            }

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                positionWS = SnapWorldPosition(positionWS);
                return TransformWorldToHClip(positionWS);
            }

            half4 DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
