// Glowing lines painted on a character (the Lightning Rabbit's bolt marks, battery tubes and lenses), drawn additively
// over the body on a copy of only the glowing triangles (the soft toon shader has no emission).
// Colour = (base colour kept by _ColorFromBase, the rest _GlowColor) x glow mask x intensity x (1 + _GlowBoost), with a
// fine electric flicker. _GlowBoost is driven per renderer from script (ElectricBodyFx) through a MaterialPropertyBlock.
// The copy shares the body's vertices and skin, so it is drawn at the same depth: ZTest LEqual with a small depth offset.
Shader "CoopDeck/Character/Glow Lines"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [NoScaleOffset] _GlowMask ("Glow Mask (R)", 2D) = "black" {}
        [HDR] _GlowColor ("Glow Colour", Color) = (0.35, 0.85, 1.0, 1)
        _ColorFromBase ("Keep Base Colour", Range(0, 1)) = 0.6
        _Intensity ("Intensity", Range(0, 8)) = 1.8
        _GlowBoost ("Boost (set by script)", Range(0, 8)) = 0
        _Flicker ("Electric Flicker", Range(0, 1)) = 0.3
        _FlickerRate ("Flicker Rate (per second)", Range(1, 60)) = 18
        _FlickerScale ("Flicker Cell Count", Range(4, 256)) = 96
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GlowLines"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_GlowMask);
            SAMPLER(sampler_GlowMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _GlowColor;
                half _ColorFromBase;
                half _Intensity;
                half _GlowBoost;
                half _Flicker;
                half _FlickerRate;
                half _FlickerScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_GlowMask, sampler_GlowMask, input.uv).r;
                half3 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 colour = baseColor * _ColorFromBase + _GlowColor.rgb * (1.0 - _ColorFromBase);

                // Electric flicker: cells of the texture dim at random, re-rolled _FlickerRate times a second.
                float tick = floor(_Time.y * _FlickerRate);
                half flicker = 1.0 - _Flicker * Hash(floor(input.uv * _FlickerScale) + tick);

                half3 rgb = colour * (mask * _Intensity * (1.0 + _GlowBoost) * flicker);
                rgb = MixFogColor(rgb, half3(0, 0, 0), input.fogFactor); // additive: fade to black in fog
                return half4(rgb, 0);
            }
            ENDHLSL
        }
    }
}
