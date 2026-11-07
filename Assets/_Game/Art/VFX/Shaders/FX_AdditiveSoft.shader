// Cheap additive glow for particles and line renderers (muzzle flash, sparks, tracers).
// No texture: the soft shape is computed from UVs, so it costs one ALU-only pass.
// _Radial = 1: round dot (particles). _Radial = 0: soft band across V (line renderers).
Shader "CoopDeck/FX/Additive Soft"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 10)) = 1
        _Radial ("Radial (1) / Band (0)", Range(0, 1)) = 1
        _Falloff ("Falloff Power", Range(0.2, 8)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
                half _Radial;
                half _Falloff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                half radial = saturate(1.0 - length(centered));
                half band = saturate(1.0 - abs(centered.y));
                half mask = pow(max(lerp(band, radial, _Radial), 0.0001), _Falloff);
                half3 rgb = input.color.rgb * _BaseColor.rgb * (_Intensity * mask * input.color.a * _BaseColor.a);
                // Additive: fade toward black in fog instead of toward the fog color.
                rgb = MixFogColor(rgb, half3(0, 0, 0), input.fogFactor);
                return half4(rgb, 0);
            }
            ENDHLSL
        }
    }
}
