Shader "Hidden/CartoonProjection/ProjectedCharacter"
{
    // Draws a rebuilt 2D character shape in its own local space.
    //
    // Depth test is LEqual with no depth write, so the character is occluded by scene geometry
    // in front of it exactly like the original sprite, instead of being composited on top.
    // Sprite alpha supplies the silhouette, which keeps soft/transparent edges intact.
    Properties
    {
        _MainTex ("Sprite texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
        Name "ProjectedCharacter"
        Tags { "LightMode"="SRPDefaultUnlit" }
        Cull Off ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        HLSLPROGRAM
        #pragma vertex Vert
        #pragma fragment Frag
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        float4 _Color;
        struct A { float3 p:POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };
        struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };
        V Vert(A i)
        {
            V o;
            o.p = TransformObjectToHClip(i.p);
            o.uv = i.uv;
            o.c = i.c;
            return o;
        }
        float4 Frag(V i):SV_Target
        {
            float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.c.a * _Color.a;
            // Region colours are stored sRGB-encoded, matching the scene capture convention.
            float3 rgb = SRGBToLinear(i.c.rgb) * _Color.rgb;
            return float4(rgb, alpha);
        }
        ENDHLSL
        }
    }
}
