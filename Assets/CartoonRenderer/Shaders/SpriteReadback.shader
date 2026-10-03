Shader "Hidden/CartoonProjection/SpriteReadback"
{
    // Copies a sprite's rect out of its texture for a GPU readback, keeping the stored bytes
    // identical to the texture's own sRGB data. The destination is a plain (non-sRGB) render
    // texture, so sampling an sRGB texture has to be encoded back explicitly.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
        Name "SpriteReadback"
        Cull Off ZWrite Off ZTest Always
        HLSLPROGRAM
        #pragma vertex Vert
        #pragma fragment Frag
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        float4 _MainTex_ST;
        struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
        struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
        V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=TRANSFORM_TEX(i.uv,_MainTex); return o; }
        float4 Frag(V i):SV_Target
        {
            float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
            return float4(LinearToSRGB(c.rgb), c.a);
        }
        ENDHLSL
        }
    }
}
