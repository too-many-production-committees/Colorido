Shader "Custom/Billboard Occluded Shadow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Shadow Color", Color) = (0.05, 0.08, 0.14, 0.55)
        _AlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+20"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Greater
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _AlphaCutoff;

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
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 sampled = tex2D(_MainTex, i.uv);
                clip(sampled.a - _AlphaCutoff);

                fixed4 color = _Color;
                color.a *= sampled.a;
                return color;
            }
            ENDCG
        }
    }
}
