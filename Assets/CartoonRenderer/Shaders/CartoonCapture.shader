Shader "Hidden/CartoonProjection/Capture"
{
    Properties
    {
        _CartoonShapeColor("Shape Color", Color) = (0.8, 0.8, 0.8, 1)
        _CartoonShapeId("Shape ID", Float) = 0.0039215686
        _CartoonCutoff("Alpha Cutoff", Float) = 0
        _CartoonCutoutTex("Cutout Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ShapeProjection"
            Tags { "LightMode"="UniversalForward" }
            ZTest LEqual ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(ShapeProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _CartoonShapeColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _CartoonShapeId)
            UNITY_INSTANCING_BUFFER_END(ShapeProperties)

            TEXTURE2D(_CartoonCutoutTex);
            SAMPLER(sampler_CartoonCutoutTex);
            // Previous frame NdotL for threshold hysteresis (material-local imported history).
            TEXTURE2D_X_FLOAT(_CartoonPrevNdotL);
            SAMPLER(sampler_CartoonPrevNdotL);
            float _CartoonCutoff;
            float4 _CartoonMainTex_ST;
            float _FacetedNormals;

            // Paint layer classification (ANGJustinl fork): deformed geometric face
            // normal vs a fixed world light direction; thresholds with hysteresis.
            float4 _PaintLightDirection;   // xyz = direction toward light
            float4 _PaintTexelSize;        // xy = 1/internal size
            float _PaintLayerMode;         // 0 base only, 1 shadow+base, 2 shadow+base+highlight
            float _ShadowThresholdEnter;
            float _ShadowThresholdExit;
            float _HighlightThresholdEnter;
            float _HighlightThresholdExit;
            float _ShadowStrength;
            float _HighlightStrength;
            float _PaintHysteresis;
            float _HeroStaticLayers;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                // Baked region data: x = regionId/255, y = flag (0 none, 0.25 standard,
                // 0.5 aggressive, 0.75 hero, 0.9 fixed-base). Zero when unbaked.
                float2 regionInfo : TEXCOORD3;
                float4 vertexColor : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv0 : TEXCOORD2;
                float2 regionInfo : TEXCOORD3;
                float4 vertexColor : COLOR0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv0 = input.uv0;
                output.regionInfo = input.regionInfo;
                output.vertexColor = input.vertexColor;
                return output;
            }

            struct ShapeOutput
            {
                half4 color : SV_Target0;
                float depth : SV_Target1;
                half4 normal : SV_Target2;   // xyz = shade normal, w = NdotL (history source)
                float4 objectId : SV_Target3; // R = object, G = region, B = paint layer, A = flag
            };

            // Linear sRGB <-> OKLab for paint layer lightness shifts.
            float3 LinearToOklab(float3 c)
            {
                float l_ = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
                float m_ = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
                float s_ = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;
                float l = pow(max(l_, 1e-5), 1.0 / 3.0);
                float m = pow(max(m_, 1e-5), 1.0 / 3.0);
                float s = pow(max(s_, 1e-5), 1.0 / 3.0);
                return float3(
                    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
            }

            float3 OklabToLinear(float3 lab)
            {
                float l_ = lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z;
                float m_ = lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z;
                float s_ = lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z;
                float l = l_ * l_ * l_;
                float m = m_ * m_ * m_;
                float s = s_ * s_ * s_;
                return float3(
                     4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
            }

            ShapeOutput Frag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ShapeOutput output;

                if (_CartoonCutoff > 0.001)
                {
                    float2 uv = input.uv0 * _CartoonMainTex_ST.xy + _CartoonMainTex_ST.zw;
                    half alpha = SAMPLE_TEXTURE2D(_CartoonCutoutTex, sampler_CartoonCutoutTex, uv).a;
                    clip(alpha - _CartoonCutoff);
                }

                // Region base color: baked vertex color > CartoonShapePart.fillColor.
                bool hasBakedData = input.regionInfo.y > 0.1;
                half4 partColor = UNITY_ACCESS_INSTANCED_PROP(ShapeProperties, _CartoonShapeColor);
                float regionFlag = input.regionInfo.y;
                half3 regionColor = (hasBakedData ? input.vertexColor.rgb : partColor.rgb);

                // Geometric face normal from deformed world positions; flip-corrected
                // against the smooth normal so backfaces never invert the shading.
                float3 smoothNormal = normalize(input.normalWS);
                float3 geometricNormal = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                if (dot(geometricNormal, smoothNormal) < 0.0)
                    geometricNormal = -geometricNormal;
                float3 shadeNormal = _FacetedNormals > 0.5 ? geometricNormal : smoothNormal;

                // Per-triangle classification. ddx/ddy are constant across a primitive,
                // so every pixel of a triangle lands in the same paint layer.
                float ndotl = dot(geometricNormal, normalize(_PaintLightDirection.xyz));
                float paintLayer = 1.0; // base
                bool dynamicAllowed = !(regionFlag > 0.85) && !(_HeroStaticLayers > 0.5 && regionFlag > 0.65 && regionFlag < 0.85);
                if (_PaintLayerMode > 0.5 && dynamicAllowed)
                {
                    float shadowThreshold = _ShadowThresholdEnter;
                    float highlightThreshold = _HighlightThresholdEnter;
                    if (_PaintHysteresis > 0.5)
                    {
                        float2 historyUV = input.positionCS.xy * _PaintTexelSize.xy;
                        float prevNdotl = SAMPLE_TEXTURE2D_X(_CartoonPrevNdotL, sampler_CartoonPrevNdotL, historyUV).r;
                        // Inside the band between enter and exit thresholds the triangle
                        // keeps its previous layer instead of flickering.
                        if (prevNdotl < _ShadowThresholdEnter)
                            shadowThreshold = _ShadowThresholdExit;
                        if (prevNdotl > _HighlightThresholdEnter)
                            highlightThreshold = _HighlightThresholdExit;
                    }
                    if (ndotl < shadowThreshold)
                        paintLayer = 0.0;
                    else if (_PaintLayerMode > 1.5 && ndotl > highlightThreshold)
                        paintLayer = 2.0;
                }

                half3 paintColor = regionColor;
                if (paintLayer < 0.5)
                {
                    float3 lab = LinearToOklab(regionColor);
                    lab.x *= saturate(1.0 - _ShadowStrength);
                    lab.y *= 0.97; // slight desaturation, hue kept
                    paintColor = saturate(OklabToLinear(lab));
                }
                else if (paintLayer > 1.5)
                {
                    float3 lab = LinearToOklab(regionColor);
                    lab.x = saturate(lab.x * (1.0 + _HighlightStrength));
                    paintColor = saturate(OklabToLinear(lab));
                }

                float objectId = UNITY_ACCESS_INSTANCED_PROP(ShapeProperties, _CartoonShapeId);
                output.color = half4(paintColor, 1);
                output.depth = input.positionCS.z;
                output.normal = half4(shadeNormal, ndotl);
                output.objectId = float4(objectId, input.regionInfo.x, paintLayer / 255.0, regionFlag);
                return output;
            }
            ENDHLSL
        }
    }
}
