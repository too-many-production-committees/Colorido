Shader "Hidden/CartoonProjection/ProjectedRedraw"
{
    Properties
    {
        _ProjectionPickMask ("Identity map (a = covered)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            // The captured identity buffer is clear where no object was projected. Blending
            // by that coverage keeps the original frame (sky and cleared background) visible
            // outside the rebuilt geometry instead of painting an opaque black sheet over it.
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_ProjectionPickMask); SAMPLER(sampler_ProjectionPickMask);
            float4x4 _ProjectionUvTransform;
            struct A { float3 p:POSITION; float4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };
            V Vert(A i) { V o; float2 aligned = mul(_ProjectionUvTransform, float4(i.p.xy, 0, 1)).xy;
                o.p=float4(aligned*2-1,0,1); o.uv=i.p.xy;
            #if UNITY_UV_STARTS_AT_TOP
                o.p.y=-o.p.y;
            #endif
                o.c=i.c; return o; }
            float4 Frag(V i):SV_Target {
                // The mask alpha is the coverage itself: 1 where an object was projected,
                // 0 for the captured background. Blending by it leaves the frame's sky and
                // cleared background visible outside the rebuilt geometry. Region colours are
                // stored sRGB-encoded by the capture and decoded here for the linear target.
                float coverage=SAMPLE_TEXTURE2D(_ProjectionPickMask,sampler_ProjectionPickMask,i.uv).a;
                return float4(SRGBToLinear(i.c.rgb),coverage);
            }
            ENDHLSL
        }
    }
}
