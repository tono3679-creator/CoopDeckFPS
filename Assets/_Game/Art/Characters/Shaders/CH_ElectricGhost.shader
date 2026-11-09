// Dash afterimage of an electric character: a baked copy of the body drawn as an additive rim (bright at grazing
// angles, nearly clear face-on) with scrolling scan bands, faded out by _Fade (set per ghost from script).
Shader "CoopDeck/Character/Electric Ghost"
{
    Properties
    {
        [HDR] _GhostColor ("Colour", Color) = (0.3, 0.8, 1.0, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _Fill ("Face-on Fill", Range(0, 1)) = 0.08
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        _BandDensity ("Scan Bands per Metre", Range(0, 80)) = 28
        _BandSpeed ("Scan Band Speed (m/s)", Range(-5, 5)) = 1.5
        _Fade ("Fade (set by script)", Range(0, 1)) = 1
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
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GhostColor;
                half _RimPower;
                half _Fill;
                half _Intensity;
                half _BandDensity;
                half _BandSpeed;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                half3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                half band = 0.65 + 0.35 * sin((input.positionWS.y - _Time.y * _BandSpeed) * _BandDensity * 6.2831853);
                half3 rgb = _GhostColor.rgb * ((rim + _Fill) * band * _Intensity * _Fade);
                rgb = MixFogColor(rgb, half3(0, 0, 0), input.fogFactor);
                return half4(rgb, 0);
            }
            ENDHLSL
        }
    }
}
