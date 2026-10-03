Shader "Hidden/CartoonProjection/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "CartoonComposite"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CartoonBaseColor);
            TEXTURE2D_X(_CartoonSceneColor);
            TEXTURE2D_X_FLOAT(_CartoonDepth);
            TEXTURE2D_X(_CartoonNormal);
            TEXTURE2D_X(_CartoonLighting);
            TEXTURE2D_X(_CartoonOutline);
            TEXTURE2D_X(_CartoonObjectId);
            int _DebugView;
            int _BackgroundMode;
            half4 _BackgroundColor;
            half4 _OutlineColor;
            float _SilhouetteStrength;
            float _OcclusionStrength;
            float _DetailStrength;
            float _RegionBoundaryStrength;
            float _HeroDetailOutlineStrength;
            float _OutlineEnabled;

            half3 DebugIdColor(float id)
            {
                // Deterministic hash-like tints so neighbouring ids are distinguishable.
                return frac(id * float3(7.31, 3.97, 11.13) + 0.37);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 baseColor = SAMPLE_TEXTURE2D_X(_CartoonBaseColor, sampler_PointClamp, uv);
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_CartoonSceneColor, sampler_PointClamp, uv);
                half4 lighting = SAMPLE_TEXTURE2D_X(_CartoonLighting, sampler_PointClamp, uv);
                float rawDepth = SAMPLE_TEXTURE2D_X(_CartoonDepth, sampler_PointClamp, uv).r;
                float3 normalWS = SAMPLE_TEXTURE2D_X(_CartoonNormal, sampler_PointClamp, uv).xyz;
                float ndotl = SAMPLE_TEXTURE2D_X(_CartoonNormal, sampler_PointClamp, uv).a;
                half4 masks = SAMPLE_TEXTURE2D_X(_CartoonOutline, sampler_PointClamp, uv);
                float4 ids = SAMPLE_TEXTURE2D_X(_CartoonObjectId, sampler_PointClamp, uv);
                // Flags: 0.75 hero, 0.9 fixed-base; hero ink boost only for the former.
                float flag = ids.a;
                float paintLayer = round(ids.b * 255.0);

                float silhouette = masks.r;
                if (_HeroDetailOutlineStrength > 0.001 && flag > 0.6 && flag < 0.85)
                    silhouette = saturate(silhouette * max(1.0, _HeroDetailOutlineStrength));
                float weightedOutline = saturate(
                    silhouette * _SilhouetteStrength +
                    masks.g * _OcclusionStrength +
                    masks.b * _RegionBoundaryStrength +
                    masks.a * _DetailStrength);
                half4 finalColor = lerp(lighting, _OutlineColor, weightedOutline * _OutlineEnabled);

                if (_BackgroundMode == 1 && Linear01Depth(rawDepth, _ZBufferParams) >= 0.9999)
                    finalColor = _BackgroundColor;

                if (_DebugView == 1) return baseColor;
                if (_DebugView == 2)
                {
                    float d = Linear01Depth(rawDepth, _ZBufferParams);
                    return half4(d, d, d, 1);
                }
                if (_DebugView == 3) return half4(normalWS * 0.5 + 0.5, 1);
                if (_DebugView == 4) return lighting;
                if (_DebugView == 5) return half4(masks.rrr, 1);
                if (_DebugView == 6) return half4(masks.ggg, 1);
                if (_DebugView == 7) return half4(masks.aaa, 1);
                if (_DebugView == 8) return half4(weightedOutline.xxx, 1);
                if (_DebugView == 9) return sceneColor;
                if (_DebugView == 10) return sceneColor; // sampled albedo approximated by camera color
                if (_DebugView == 11) return baseColor;
                if (_DebugView == 12) return half4(DebugIdColor(ids.g), 1);
                if (_DebugView == 13) return half4(DebugIdColor(ids.r), 1);
                if (_DebugView == 14) return half4(normalWS * 0.5 + 0.5, 1); // geometric face normal view
                if (_DebugView == 15) return half4(lighting.aaa, 1);
                if (_DebugView == 16) return half4(saturate(ndotl * 0.5 + 0.5).xxx, 1); // NdotL
                if (_DebugView == 17)
                {
                    // Paint layer id: shadow = deep blue, base = mid grey, highlight = warm yellow.
                    half3 layerColor = paintLayer < 0.5 ? half3(0.1, 0.15, 0.45)
                        : paintLayer < 1.5 ? half3(0.5, 0.5, 0.5)
                        : half3(0.95, 0.9, 0.3);
                    return half4(layerColor, 1);
                }
                if (_DebugView == 18) return half4((paintLayer < 0.5).xxx, 1); // shadow mask
                if (_DebugView == 19) return half4((abs(paintLayer - 1.0) < 0.5).xxx, 1); // base mask
                if (_DebugView == 20) return half4((paintLayer > 1.5).xxx, 1); // highlight mask
                return finalColor;
            }
            ENDHLSL
        }
    }
}
