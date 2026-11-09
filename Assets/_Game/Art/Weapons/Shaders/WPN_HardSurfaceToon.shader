// Stylised hard-surface shader for weapons (URP Forward / Forward+). The cel look of CoopDeck/Character/Toon
// (two-tone main light, soft rim, inverted-hull outline) plus what painted armour, polymer and metal need up close:
// - a material mask (R white paint, G red paint, B dark polymer, A bare metal, the rest = gold/orange accent) picks
//   per-material smoothness, metalness, specular and edge-highlight strength (the *_Params vectors below)
// - a surface map baked from the sculpt: R convex edges -> edge highlights (the light catch of a bevel),
//   G ambient occlusion and B concave creases -> panel-gap shading
// - a stylised specular band and environment reflection (sky / reflection probes) so panels are not flat colour
// - an outline whose width is kept between a minimum and maximum number of pixels, so it never breaks up into
//   one-pixel dots (first person) or grows into a smear
// Maps come from ArtSource/Tools/rebuild_weapon_hq.py; FireRifleAssetFactory sets the materials up.
Shader "CoopDeck/Weapon/HardSurfaceToon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Cel Shading)]
        _ShadeColor ("Shade Color (multiplies the base)", Color) = (0.64, 0.55, 0.68, 1)
        _ShadeThreshold ("Shade Threshold (N.L)", Range(-1, 1)) = 0.0
        _ShadeSoftness ("Shade Edge Softness", Range(0.001, 0.5)) = 0.12
        _ShadowStrength ("Cast Shadow Strength", Range(0, 1)) = 1
        _AmbientStrength ("Ambient (Probes) Strength", Range(0, 2)) = 0.6
        _MaxLightIntensity ("Max Light Intensity", Range(0.5, 4)) = 1.2
        _DiffuseWrap ("Lit-side Gradient (0 flat, 1 Lambert)", Range(0, 1)) = 0.55

        [Header(Rim)]
        _RimColor ("Rim Color", Color) = (1, 0.86, 0.72, 1)
        _RimPower ("Rim Power", Range(0.5, 10)) = 4.5
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.18

        [Header(Normal Map)]
        [Toggle(_NORMALMAP)] _UseNormalMap ("Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1

        [Header(Materials and Edges)]
        [Toggle(_SURFACE_MAPS)] _UseSurfaceMaps ("Material Mask and Surface Map", Float) = 0
        [NoScaleOffset] _MaterialMask ("Material Mask (R white, G red, B polymer, A metal)", 2D) = "black" {}
        [NoScaleOffset] _SurfaceMap ("Surface Map (R edge, G AO, B crease)", 2D) = "green" {}
        // x smoothness, y metallic, z specular strength, w edge highlight strength
        _WhitePaintParams ("White Paint (smooth, metal, spec, edge)", Vector) = (0.5, 0, 0.5, 0.8)
        _RedPaintParams ("Red Paint (smooth, metal, spec, edge)", Vector) = (0.55, 0, 0.55, 0.85)
        _PolymerParams ("Dark Polymer (smooth, metal, spec, edge)", Vector) = (0.42, 0, 0.35, 0.8)
        _MetalParams ("Bare Metal (smooth, metal, spec, edge)", Vector) = (0.7, 0.9, 1, 1)
        _AccentParams ("Gold Accent (smooth, metal, spec, edge)", Vector) = (0.62, 0.55, 0.7, 0.7)
        _EdgeLift ("Edge Highlight Lift", Range(0, 1)) = 0.35
        _EdgeTint ("Edge Highlight Tint", Color) = (1, 0.93, 0.85, 1)
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _CreaseDarkening ("Crease Darkening", Range(0, 1)) = 0.5

        [Header(Specular and Reflection)]
        _SpecularThreshold ("Specular Band Threshold", Range(0, 1)) = 0.35
        _SpecularSoftness ("Specular Band Softness", Range(0.001, 0.5)) = 0.25
        _SpecularToon ("Specular Banding (0 smooth, 1 toon)", Range(0, 1)) = 0.6
        _ReflectionStrength ("Environment Reflection", Range(0, 2)) = 1
        _PaintFresnel ("Paint Clear-coat Fresnel", Range(0, 1)) = 0.35
        _PaintReflectance ("Paint Clear-coat Reflectance (facing)", Range(0, 0.3)) = 0.08
        // First person: a highlight from a fixed direction relative to the camera (view space, towards the light), so
        // the gloss reads whatever the sun does. 0 for the world material.
        _ViewLightDirection ("View-space Highlight Direction", Vector) = (-0.35, 0.8, 0.45, 0)
        _ViewLightSpecular ("View-space Highlight Strength", Range(0, 2)) = 0
        _ViewLightPower ("View-space Highlight Breadth (exponent)", Range(1, 64)) = 10

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission ("Emission", Float) = 0
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.17, 0.09, 0.07, 1)
        _OutlineWidth ("Outline Width (m, at 4 m or closer)", Range(0, 0.02)) = 0.003
        _OutlineMinPixels ("Outline Min Width (px)", Range(0, 4)) = 0
        _OutlineMaxPixels ("Outline Max Width (px)", Range(0.5, 16)) = 8
        [Toggle(_OUTLINE_UV3_NORMALS)] _OutlineUV3Normals ("Outline Uses Smoothed Normals (UV3)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap);
        TEXTURE2D(_BumpMap);
        TEXTURE2D(_MaterialMask);
        TEXTURE2D(_SurfaceMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _ShadowStrength;
            half _AmbientStrength;
            half _MaxLightIntensity;
            half _DiffuseWrap;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half _BumpScale;
            half4 _WhitePaintParams;
            half4 _RedPaintParams;
            half4 _PolymerParams;
            half4 _MetalParams;
            half4 _AccentParams;
            half _EdgeLift;
            half4 _EdgeTint;
            half _OcclusionStrength;
            half _CreaseDarkening;
            half _SpecularThreshold;
            half _SpecularSoftness;
            half _SpecularToon;
            half _ReflectionStrength;
            half _PaintFresnel;
            half4 _ViewLightDirection;
            half _ViewLightSpecular;
            half _ViewLightPower;
            half _PaintReflectance;
            half4 _EmissionColor;
            half4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineMinPixels;
            float _OutlineMaxPixels;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float4 tangentOS  : TANGENT;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _SURFACE_MAPS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                half fogFactor    : TEXCOORD3;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD4;
            #endif
            #if defined(_NORMALMAP)
                half4 tangentWS   : TEXCOORD5; // w: bitangent sign
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_NORMALMAP)
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            #endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(positionInputs);
            #endif
                return output;
            }

            half CelStep(half nDotL)
            {
                return smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, nDotL);
            }

            // Lit-side intensity: the cel step, with a little of Lambert's falloff kept on the lit side so broad
            // panels turning away from the sun read as turning (a flat toon plateau makes armour look painted on).
            half CelDiffuse(half nDotL)
            {
                half step = CelStep(nDotL);
                half lambert = saturate(nDotL * 0.5 + 0.5);
                return step * lerp(1.0, lambert * 1.25, _DiffuseWrap);
            }

            // Blinn-Phong lobe sharpened by smoothness, then pulled toward a soft-edged band (the toon highlight).
            half SpecularTerm(half nDotH, half smoothness)
            {
                half exponent = exp2(10.0 * smoothness + 1.0);
                half lobe = pow(nDotH, exponent) * (exponent + 8.0) / 64.0; // brighter as it narrows
                half band = smoothstep(_SpecularThreshold - _SpecularSoftness, _SpecularThreshold + _SpecularSoftness, saturate(lobe));
                return lerp(lobe, band * max(lobe, 1.0) * 0.8, _SpecularToon);
            }

            half3 AdditionalLight(Light light, half3 normalWS, half3 diffuseColor)
            {
                half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                half3 color = min(light.color, _MaxLightIntensity);
                return diffuseColor * color * CelStep(dot(normalWS, light.direction)) * attenuation;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
            #if defined(_NORMALMAP)
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, input.uv), _BumpScale);
                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,
                    half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS)));
            #else
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            #endif
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // per-material response
            #if defined(_SURFACE_MAPS)
                half4 mask = SAMPLE_TEXTURE2D(_MaterialMask, sampler_BaseMap, input.uv);
                half accent = saturate(1.0 - dot(mask, half4(1, 1, 1, 1)));
                half4 params = mask.r * _WhitePaintParams + mask.g * _RedPaintParams + mask.b * _PolymerParams
                             + mask.a * _MetalParams + accent * _AccentParams;
                half3 surface = SAMPLE_TEXTURE2D(_SurfaceMap, sampler_BaseMap, input.uv).rgb;
                half edge = surface.r;
                half occlusion = lerp(1.0, surface.g, _OcclusionStrength) * (1.0 - surface.b * _CreaseDarkening);
            #else
                half4 params = _PolymerParams;
                half edge = 0;
                half occlusion = 1;
            #endif
                half smoothness = saturate(params.x);
                half metallic = saturate(params.y);
                half specularStrength = params.z;
                half edgeStrength = params.w;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
                half4 shadowMask = half4(1, 1, 1, 1);

                // metals keep less diffuse and take their colour from reflections and the highlight
                half3 diffuseColor = albedo * (1.0 - metallic * 0.7);
                half3 specularColor = lerp(half3(1, 1, 1), albedo * 1.4 + 0.1, metallic);

                Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, shadowMask);
                half nDotL = dot(normalWS, mainLight.direction);
                half castShadow = smoothstep(0.5 - _ShadeSoftness, 0.5 + _ShadeSoftness, mainLight.shadowAttenuation);
                half lit = CelStep(nDotL) * lerp(1.0, castShadow, _ShadowStrength);
                half litDiffuse = CelDiffuse(nDotL) * lerp(1.0, castShadow, _ShadowStrength);
                half3 mainColor = min(mainLight.color, _MaxLightIntensity);
                half3 color = diffuseColor * mainColor * lerp(_ShadeColor.rgb, half3(1, 1, 1), litDiffuse);
                half3 ambient = SampleSH(normalWS);
                color += diffuseColor * ambient * _AmbientStrength;
                color *= occlusion;

                // specular highlight of the main light (lit side only)
                half3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
                half nDotH = saturate(dot(normalWS, halfDir));
                color += specularColor * mainColor * SpecularTerm(nDotH, smoothness) * specularStrength * lit * occlusion;
                // the camera-relative highlight (first person)
                half3 viewLightWS = SafeNormalize(mul((float3x3)UNITY_MATRIX_I_V, _ViewLightDirection.xyz));
                half nDotHView = saturate(dot(normalWS, SafeNormalize(viewLightWS + viewDirWS)));
                half viewLobe = pow(nDotHView, _ViewLightPower);
                half viewBand = smoothstep(_SpecularThreshold - _SpecularSoftness, _SpecularThreshold + _SpecularSoftness, viewLobe);
                color += specularColor * lerp(viewLobe, viewBand, _SpecularToon) * specularStrength * smoothness * _ViewLightSpecular * occlusion;

            #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    color += AdditionalLight(GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS, diffuseColor);
                }
                #endif
                LIGHT_LOOP_BEGIN(lightCount)
                    color += AdditionalLight(GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS, diffuseColor);
                LIGHT_LOOP_END
            #endif

                // environment: tinted reflection for metal, a thin clear-coat sheen at grazing angles for paint
                half nDotV = saturate(dot(normalWS, viewDirWS));
                half fresnel = pow(1.0 - nDotV, 4.0);
                half perceptualRoughness = 1.0 - smoothness;
                half3 reflectDir = reflect(-viewDirWS, normalWS);
                half3 environment = GlossyEnvironmentReflection(reflectDir, input.positionWS, perceptualRoughness, occlusion,
                                                               inputData.normalizedScreenSpaceUV);
                half reflectAmount = lerp((_PaintReflectance + _PaintFresnel * fresnel) * smoothness, lerp(0.35, 1.0, fresnel), metallic);
                color += environment * specularColor * reflectAmount * _ReflectionStrength;

                // edge highlights: convex edges catch the light like a polished bevel (strongest on the lit side)
                half3 lightLevel = mainColor * (0.4 + 0.6 * lit) + ambient * _AmbientStrength;
                half3 edgeColor = (albedo * 1.3 + _EdgeLift * _EdgeTint.rgb) * lightLevel;
                color = lerp(color, max(color, edgeColor), saturate(edge * edgeStrength));

                // rim
                half rim = pow(saturate(1.0 - nDotV), _RimPower) * _RimStrength;
                color += _RimColor.rgb * mainColor * rim * (0.35 + 0.65 * lit);

            #if defined(_EMISSION)
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, input.uv).rgb * _EmissionColor.rgb;
            #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _OUTLINE_UV3_NORMALS

            struct OutlineAttributes
            {
                float4 positionOS     : POSITION;
                float3 normalOS       : NORMAL;
                float3 smoothNormalOS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(OutlineAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            #if defined(_OUTLINE_UV3_NORMALS)
                float3 normalOS = input.smoothNormalOS; // rigid meshes only: UV3 is not skinned
            #else
                float3 normalOS = input.normalOS;
            #endif
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float4 positionCS = TransformWorldToHClip(positionWS);

                // Extrude in screen space: the world width (fixed up close, up to 3x at range) converted to pixels at
                // this depth, then kept between the min and max pixel widths.
                float2 screen = _ScaledScreenParams.xy;
                float2 directionPx = TransformWorldToHClipDir(normalWS).xy * screen;
                float lengthPx = length(directionPx);
                if (lengthPx > 1e-6)
                {
                    float distanceScale = clamp(distance(positionWS, GetCameraPositionWS()) / 4.0, 1.0, 3.0);
                    float pixelsPerMetre = abs(UNITY_MATRIX_P[1][1]) * 0.5 * screen.y / max(positionCS.w, 1e-4);
                    float widthPx = clamp(_OutlineWidth * distanceScale * pixelsPerMetre, _OutlineMinPixels, _OutlineMaxPixels);
                    positionCS.xy += (directionPx / lengthPx) * widthPx * 2.0 / screen * positionCS.w;
                }
                output.positionCS = positionCS;
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                half3 packedNormalWS = PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5));
                return half4(packedNormalWS, 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
