Shader "Custom/Pixel Perfect Post"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PixelJitter ("Pixel Jitter", Range(0, 0.2)) = 0.035
        _ColorSteps ("Color Steps", Range(2, 32)) = 10
        _NoiseScale ("Noise Scale", Float) = 1
        _NoiseSpeed ("Noise Speed", Float) = 8
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _PixelJitter;
            float _ColorSteps;
            float _NoiseScale;
            float _NoiseSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 pixel = floor(i.uv / _MainTex_TexelSize.xy);
                float frame = floor(_Time.y * _NoiseSpeed);
                float noise = Hash21(pixel * _NoiseScale + frame);
                float signedNoise = noise * 2.0 - 1.0;

                fixed4 color = tex2D(_MainTex, i.uv);
                color.rgb += signedNoise * _PixelJitter;

                float steps = max(2.0, _ColorSteps);
                color.rgb = floor(saturate(color.rgb) * (steps - 1.0) + 0.5) / (steps - 1.0);

                return color;
            }
            ENDCG
        }
    }
}
