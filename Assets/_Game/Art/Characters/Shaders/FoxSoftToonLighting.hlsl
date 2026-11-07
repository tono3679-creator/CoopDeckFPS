// Soft stylized lighting for characters, called from SG_Character_SoftToon (Custom Function, File mode).
// Per-texel surface parameters come from a generated mask (FoxGunnerLookFactory):
//   Surface.r  softness  0 = crisp terminator (leather, metal) .. 1 = wrapped, soft gradient (fur, face)
//   Surface.g  smoothness (specular size and strength; fur and cloth stay matte)
//   Surface.b  metallic   (tints the highlight and adds a cheap probe reflection)
//   Surface.a  warmth     (warm light bleeding into the terminator: face and fur, a little on cloth)
// Material properties are read from the graph's UnityPerMaterial block by reference name.
// Cost: one main light (with shadows), Forward+ / per-object additional lights (diffuse only),
// probe ambient, one Blinn lobe, one rim. No extra texture fetch beyond the two the graph samples.
#ifndef COOPDECK_FOX_SOFT_TOON_INCLUDED
#define COOPDECK_FOX_SOFT_TOON_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Soft two-zone diffuse: a wrapped Lambert gradient blended toward a wide smoothstep "toon" step.
// _ToonBlend keeps the step gentle; softness widens the wrap and the step for fur and face.
float SoftToonDiffuse(float nDotL, float softness)
{
    float wrap = lerp(0.08, 0.45, softness);
    float wrapped = saturate((nDotL + wrap) / (1.0 + wrap));
    float width = lerp(_ToonWidth * 0.6, _ToonWidth * 1.4, softness);
    float stepped = smoothstep(_ToonCenter - width, _ToonCenter + width, wrapped);
    return lerp(wrapped, stepped, _ToonBlend * (1.0 - 0.5 * softness));
}

float3 ClampLight(float3 color)
{
    return min(color, _MaxLightIntensity);
}
#endif

// MappedNormalWS: the normal map (baked from Meshy's high-res sculpt by refine_character.py) in world space.
// _NormalStrength blends it over the mesh normal; 0, or a mesh without tangents (the first-person arms), keeps
// the mesh normal.
void FoxSoftToon_float(float3 Albedo, float4 Surface, float3 PositionWS, float3 NormalWS, float3 ViewDirWS, float2 ScreenUV,
    float3 MappedNormalWS, out float3 Color)
{
#ifdef SHADERGRAPH_PREVIEW
    float ndl = dot(normalize(NormalWS), normalize(float3(0.4, 0.8, -0.45)));
    Color = Albedo * (0.45 + 0.55 * smoothstep(-0.2, 0.7, ndl));
#else
    float3 n = normalize(NormalWS);
    float mappedLength = dot(MappedNormalWS, MappedNormalWS);
    if (_NormalStrength > 0.001 && mappedLength > 0.25 && mappedLength < 4.0) // false for NaN as well
    {
        n = normalize(lerp(n, MappedNormalWS * rsqrt(mappedLength), _NormalStrength));
    }
    float3 v = normalize(ViewDirWS);
    float softness = Surface.r;
    float smoothness = Surface.g;
    float metallic = Surface.b;
    float warmth = Surface.a;

    float3 diffuseColor = Albedo * (1.0 - 0.8 * metallic);
    float3 specularColor = lerp(float3(0.04, 0.04, 0.04), Albedo, metallic);
    float specPower = exp2(1.0 + 10.0 * smoothness);
    float specScale = smoothness * smoothness * _SpecularStrength * (specPower + 8.0) / 25.0;

    // Main light (shadow coords per pixel; screen-space shadows read the screen UV).
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = float4(ScreenUV, 0.0, 1.0);
#else
    float4 shadowCoord = TransformWorldToShadowCoord(PositionWS);
#endif
    Light mainLight = GetMainLight(shadowCoord, PositionWS, half4(1, 1, 1, 1));
    float3 lightColor = ClampLight(mainLight.color);
    float nDotL = dot(n, mainLight.direction);
    float shadow = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
    float lit = SoftToonDiffuse(nDotL, softness) * shadow;

    float3 color = diffuseColor * lightColor * lit;

    // Warm bleed around the terminator (light scattering in fur and skin), strongest just inside the shadow.
    float terminator = saturate(1.0 - abs(nDotL + 0.1) * 2.2) * shadow;
    color += Albedo * _SubsurfaceColor.rgb * lightColor * terminator * warmth * 0.45;

    // Specular: broad and faint on matte areas, tighter on leather, tinted on metal.
    float3 h = normalize(mainLight.direction + v);
    float spec = pow(saturate(dot(n, h)), specPower) * specScale * lit;
    color += specularColor * lightColor * spec;

    // Ambient from probes; metal also reflects the probe in the mirror direction (cheap stand-in for a probe fetch).
    color += diffuseColor * SampleSH(n) * _AmbientStrength;
    color += specularColor * SampleSH(reflect(-v, n)) * metallic * smoothness * 0.8;

#if defined(_ADDITIONAL_LIGHTS)
    // The cluster light loop macros read positionWS / normalizedScreenSpaceUV from "inputData".
    InputData inputData = (InputData)0;
    inputData.positionWS = PositionWS;
    inputData.normalWS = n;
    inputData.normalizedScreenSpaceUV = ScreenUV;
    uint lightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, PositionWS, half4(1, 1, 1, 1));
        color += diffuseColor * ClampLight(light.color) * SoftToonDiffuse(dot(n, light.direction), softness)
            * light.distanceAttenuation * light.shadowAttenuation;
    }
    #endif
    LIGHT_LOOP_BEGIN(lightCount)
        Light light = GetAdditionalLight(lightIndex, PositionWS, half4(1, 1, 1, 1));
        color += diffuseColor * ClampLight(light.color) * SoftToonDiffuse(dot(n, light.direction), softness)
            * light.distanceAttenuation * light.shadowAttenuation;
    LIGHT_LOOP_END
#endif

    // Rim: a thin sheen on fur, face and cloth, mostly on the lit side; tinted by the surface colour.
    float fresnel = pow(1.0 - saturate(dot(n, v)), _RimPower);
    float rimMask = lerp(0.35, 1.0, softness);
    float3 rimTint = lerp(_RimColor.rgb, Albedo * 1.6, 0.5);
    color += rimTint * lightColor * fresnel * _RimStrength * rimMask * (0.25 + 0.75 * lit);

    Color = color;
#endif
}

#endif
