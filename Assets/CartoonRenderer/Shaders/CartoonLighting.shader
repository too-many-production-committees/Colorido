Shader "Hidden/CartoonProjection/Lighting"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "CartoonLighting"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CartoonBaseColor);
            TEXTURE2D_X(_CartoonNormal);
            TEXTURE2D_X_FLOAT(_CartoonDepth);
            float4 _LightDirection;
            half4 _LightColor;
            int _ColorBands;
            float _ShadowThreshold;
            float _HighlightThreshold;
            float _LightingEnabled;
            int _SurfaceMode;

            // Per the ANGJustinl-fork revision, MaterialColorBlocks and ManualPartColor
            // receive their paint layers inside the capture pass. This shader only
            // quantizes the OriginalMaterial (camera color) path; all other modes are a
            // pass-through so no fullscreen PBR-derived gradient can leak back in.
            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D_X(_CartoonBaseColor, sampler_PointClamp, input.texcoord);
                if (_SurfaceMode != 0)
                    return half4(baseColor.rgb, 1);

                float rawDepth = SAMPLE_TEXTURE2D_X(_CartoonDepth, sampler_PointClamp, input.texcoord).r;
                if (Linear01Depth(rawDepth, _ZBufferParams) >= 0.9999)
                    return half4(baseColor.rgb, 1);

                if (_LightingEnabled < 0.5)
                    return half4(baseColor.rgb, 1);

                float3 normalWS = normalize(SAMPLE_TEXTURE2D_X(_CartoonNormal, sampler_PointClamp, input.texcoord).xyz);
                float ndotl = saturate(dot(normalWS, normalize(_LightDirection.xyz)));
                float band;
                if (_ColorBands <= 2)
                    band = ndotl < _ShadowThreshold ? 0.48 : 1.0;
                else if (_ColorBands == 3)
                    band = ndotl < _ShadowThreshold ? 0.42 : (ndotl < _HighlightThreshold ? 0.72 : 1.0);
                else
                {
                    float middle = lerp(_ShadowThreshold, _HighlightThreshold, 0.5);
                    band = ndotl < _ShadowThreshold ? 0.38 : (ndotl < middle ? 0.62 : (ndotl < _HighlightThreshold ? 0.82 : 1.05));
                }
                half3 lit = baseColor.rgb * lerp(half3(1, 1, 1), _LightColor.rgb, 0.18) * band;
                return half4(lit, 1);
            }
            ENDHLSL
        }
    }
}
