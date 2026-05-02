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
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf PixelLit vertex:vert fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float _TexturePixels;
        float _ColorSteps;
        float _ShadowThreshold;
        float _ShadowStrength;
        float _HighlightThreshold;
        float _HighlightStrength;
        fixed4 _HighlightColor;
        float _AmbientStrength;
        float _VertexSnap;

        struct Input
        {
            float2 uv_MainTex;
        };

        void vert(inout appdata_full v)
        {
            if (_VertexSnap <= 0.0001)
                return;

            float3 worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
            worldPosition = floor(worldPosition / _VertexSnap + 0.5) * _VertexSnap;
            v.vertex = mul(unity_WorldToObject, float4(worldPosition, 1.0));
        }

        float3 QuantizeColor(float3 color, float steps)
        {
            steps = max(2.0, steps);
            return floor(saturate(color) * (steps - 1.0) + 0.5) / (steps - 1.0);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 uv = IN.uv_MainTex;

            if (_TexturePixels > 1.0)
            {
                uv = (floor(uv * _TexturePixels) + 0.5) / _TexturePixels;
            }

            fixed4 sampled = tex2D(_MainTex, uv) * _Color;
            o.Albedo = QuantizeColor(sampled.rgb, _ColorSteps);
            o.Alpha = sampled.a;
        }

        half4 LightingPixelLit(SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndotl = saturate(dot(s.Normal, lightDir));
            half lit = step(_ShadowThreshold, ndotl);
            half lightAmount = lerp(_ShadowStrength, 1.0h, lit) * atten;
            lightAmount = max(lightAmount, _AmbientStrength);

            half highlight = step(_HighlightThreshold, ndotl) * _HighlightStrength;

            half4 color;
            color.rgb = s.Albedo * _LightColor0.rgb * lightAmount;
            color.rgb += _HighlightColor.rgb * highlight;
            color.a = s.Alpha;
            return color;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
