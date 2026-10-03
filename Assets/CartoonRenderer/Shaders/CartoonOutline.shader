Shader "Hidden/CartoonProjection/Outline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "CartoonOutlineMasks"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CartoonNormal);
            TEXTURE2D_X(_CartoonObjectId);
            TEXTURE2D_X_FLOAT(_CartoonDepth);
            float4 _InternalTexelSize;
            float _OutlineWidth;
            float _DepthThreshold;
            float _NormalThreshold;
            float _DepthEdgeStrength;
            float _NormalEdgeStrength;
            float _OutlineEnabled;

            // Mask channels: R = silhouette, G = object occlusion, B = region boundary,
            // A = normal detail. Region boundaries compare the G region-id channel with a
            // tolerance well below one id step (1/255) so ids never alias but quantization
            // noise cannot produce false lines.
            static const float IdTolerance = 0.001;

            float EyeDepth(float2 uv)
            {
                float raw = SAMPLE_TEXTURE2D_X(_CartoonDepth, sampler_PointClamp, uv).r;
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            float IsBackground(float2 uv)
            {
                float raw = SAMPLE_TEXTURE2D_X(_CartoonDepth, sampler_PointClamp, uv).r;
                return step(0.9999, Linear01Depth(raw, _ZBufferParams));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_OutlineEnabled < 0.5)
                    return 0;

                float2 uv = input.texcoord;
                float2 stepUV = _InternalTexelSize.xy * max(1.0, round(_OutlineWidth));
                float centerBg = IsBackground(uv);
                float centerDepth = EyeDepth(uv);
                float2 centerId = SAMPLE_TEXTURE2D_X(_CartoonObjectId, sampler_PointClamp, uv).rg;
                float3 centerNormal = normalize(SAMPLE_TEXTURE2D_X(_CartoonNormal, sampler_PointClamp, uv).xyz);
                float silhouette = 0;
                float depthDelta = 0;
                float normalDelta = 0;
                float objectBoundary = 0;
                float regionBoundary = 0;

                const float2 directions[8] = {
                    float2(-1,0), float2(1,0), float2(0,-1), float2(0,1),
                    float2(-0.707,-0.707), float2(0.707,-0.707),
                    float2(-0.707,0.707), float2(0.707,0.707)
                };
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float2 sampleUV = saturate(uv + directions[i] * stepUV);
                    float neighborBg = IsBackground(sampleUV);
                    silhouette = max(silhouette, abs(centerBg - neighborBg));
                    if (centerBg < 0.5 && neighborBg < 0.5)
                    {
                        float neighborDepth = EyeDepth(sampleUV);
                        depthDelta = max(depthDelta, abs(neighborDepth - centerDepth) / max(centerDepth, 0.001));
                        float2 neighborId = SAMPLE_TEXTURE2D_X(_CartoonObjectId, sampler_PointClamp, sampleUV).rg;
                        bool differentObject = abs(neighborId.r - centerId.r) > IdTolerance;
                        bool differentRegion = abs(neighborId.g - centerId.g) > IdTolerance;
                        objectBoundary = max(objectBoundary, differentObject ? 1.0 : 0.0);
                        regionBoundary = max(regionBoundary, (!differentObject && differentRegion) ? 1.0 : 0.0);
                        float3 neighborNormal = normalize(SAMPLE_TEXTURE2D_X(_CartoonNormal, sampler_PointClamp, sampleUV).xyz);
                        normalDelta = max(normalDelta, 1.0 - saturate(dot(centerNormal, neighborNormal)));
                    }
                }

                float occlusion = max(objectBoundary, smoothstep(_DepthThreshold, _DepthThreshold * 2.5, depthDelta));
                occlusion *= _DepthEdgeStrength;
                occlusion *= 1.0 - silhouette;
                float detail = smoothstep(_NormalThreshold, _NormalThreshold * 2.0, normalDelta) * _NormalEdgeStrength;
                detail *= (1.0 - silhouette) * (1.0 - saturate(occlusion));
                regionBoundary *= 1.0 - silhouette;
                return half4(saturate(silhouette), saturate(occlusion), saturate(regionBoundary), saturate(detail));
            }
            ENDHLSL
        }
    }
}
