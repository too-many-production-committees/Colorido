Shader "Hidden/CartoonProjection/ProjectedCapture"
{
    Properties
    {
        _ProjectionMap ("Albedo", 2D) = "white" {}
        _ProjectionST ("UV transform", Vector) = (1,1,0,0)
        _ProjectionTint ("Tint", Vector) = (1,1,1,1)
        _ProjectionId ("Source ID", Vector) = (0,0,0,0)
        _ProjectionCutoff ("Cutoff", Float) = 0.001
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_ProjectionMap); SAMPLER(sampler_ProjectionMap);
            float4 _ProjectionST, _ProjectionTint, _ProjectionId;
            float _ProjectionCutoff;
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv*_ProjectionST.xy+_ProjectionST.zw; return o; }
            struct O { float4 color:SV_Target0; float4 id:SV_Target1; };
            O Frag(V i) {
                float4 c=SAMPLE_TEXTURE2D(_ProjectionMap,sampler_ProjectionMap,i.uv)*_ProjectionTint;
                clip(c.a-_ProjectionCutoff);
                // Store explicitly sRGB-encoded bytes in an 8-bit UNorm attachment.
                // Both readback paths must disable hardware sRGB conversion. The redraw
                // decodes these bytes once for its linear output target.
                O o; o.color=float4(LinearToSRGB(c.rgb),1); o.id=_ProjectionId; return o;
            }
            ENDHLSL
        }
    }
}
