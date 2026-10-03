Shader "Hidden/CartoonProjection/Simplify"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "RegionAreaFilter"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CartoonBaseColor);
            TEXTURE2D_X(_CartoonObjectId);
            float4 _InternalTexelSize;
            float _MinimumArea;
            float _IdTolerance;

            // First-version shape simplification: inside a 5x5 window, regions smaller
            // than _MinimumArea pixels adopt their neighbour region. Colors are copied
            // verbatim (no blur), so block interiors stay flat and boundaries become
            // steadier polylines. The first non-center id found stands in for the
            // dominant neighbour, which is the common case for jaggy edges and islands.
            struct FilterOutput
            {
                half4 color : SV_Target0;
                float4 id : SV_Target1; // R = object, G = region, B = paint layer, A = flag
            };

            FilterOutput Frag(Varyings input)
            {
                FilterOutput output;
                float2 uv = input.texcoord;
                float4 centerId = SAMPLE_TEXTURE2D_X(_CartoonObjectId, sampler_PointClamp, uv);
                half4 centerColor = SAMPLE_TEXTURE2D_X(_CartoonBaseColor, sampler_PointClamp, uv);
                if (centerId.r < _IdTolerance && centerId.g < _IdTolerance)
                {
                    output.color = centerColor; // background stays untouched
                    output.id = centerId;
                    return output;
                }

                float matchCount = 0;
                float4 otherId = float4(-1, -1, -1, -1);
                half4 otherColor = centerColor;
                const int radius = 2;
                [unroll] for (int dy = -radius; dy <= radius; dy++)
                {
                    [unroll] for (int dx = -radius; dx <= radius; dx++)
                    {
                        float2 sampleUV = saturate(uv + float2(dx, dy) * _InternalTexelSize.xy);
                        float4 sampleId = SAMPLE_TEXTURE2D_X(_CartoonObjectId, sampler_PointClamp, sampleUV);
                        // Match the full identity: object + region + paint layer, so a
                        // tiny shadow island merges into its base region, not sideways.
                        bool matchesCenter = abs(sampleId.r - centerId.r) < _IdTolerance &&
                                             abs(sampleId.g - centerId.g) < _IdTolerance &&
                                             abs(sampleId.b - centerId.b) < _IdTolerance;
                        if (matchesCenter)
                        {
                            matchCount += 1.0;
                        }
                        else if (otherId.r < 0.0)
                        {
                            otherId = sampleId;
                            otherColor = SAMPLE_TEXTURE2D_X(_CartoonBaseColor, sampler_PointClamp, sampleUV);
                        }
                    }
                }

                if (matchCount >= _MinimumArea || otherId.r < 0.0)
                {
                    output.color = centerColor;
                    output.id = centerId;
                    return output;
                }
                output.color = otherColor;
                output.id = otherId;
                return output;
            }
            ENDHLSL
        }
    }
}
