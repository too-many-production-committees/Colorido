Shader "Hidden/CartoonProjection/PaintHistory"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PaintHistoryCopy"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CartoonNormal);
            SAMPLER(sampler_CartoonNormal);

            // Copies this frame's per-pixel NdotL (normal buffer alpha) into the R16
            // history buffer consumed by the next frame's capture hysteresis.
            float Frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D_X(_CartoonNormal, sampler_CartoonNormal, input.texcoord).a;
            }
            ENDHLSL
        }
    }
}
