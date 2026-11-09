// Stylized lightning (Docs/LIGHTNING_VFX.md), called from the SG_FX_Lightning* Shader Graphs (Custom Function,
// File mode) that LightningVfxFactory generates. Every graph declares every property below (the file is compiled
// whole into each graph).
//
// The look: a thick, readable bolt, not a thin realistic line: a thin white-hot core (HDR, blooms) inside a wide
// cyan body (a little over 1) that deepens to azure toward its soft edge (below 1).
// Alpha blending, not additive: the glow and the edge cover the background, so the bolt keeps its colour over a bright
// sky or floor (additive cyan on a bright background only turns white). Alpha blending computes
// out = Color * Alpha + background * (1 - Alpha), so Color below is the emission per unit of coverage.
//
// LightningBolt  - Line Renderer strips (main bolt, branches, residual arcs). TEXCOORD0.xy = (along 0..1, across 0..1),
//                  vertex colour = strength tint (rgb) and life fade (a).
// LightningFlash - particles. TEXCOORD0 = (uv.x, uv.y, StableRandom.x, AgePercent) (streams set by the factory).
//                  _Mode 0 = star flash with spikes, 1 = expanding shock ring, 2 = soft glow.
// Cost: ALU only (hashed value noise), no texture fetches. Alpha blending (transparent queue).
#ifndef COOPDECK_LIGHTNING_VFX_INCLUDED
#define COOPDECK_LIGHTNING_VFX_INCLUDED

float LvHash11(float x)
{
    x = frac(x * 0.1031);
    x *= x + 33.33;
    x *= x + x;
    return frac(x);
}

float LvNoise1(float x)
{
    float i = floor(x);
    float f = frac(x);
    float u = f * f * (3.0 - 2.0 * f);
    return lerp(LvHash11(i), LvHash11(i + 1.0), u);
}

float LvTime()
{
    return fmod(_TimeParameters.x, 600.0);
}

// ------------------------------------------------------------------------------------------------ bolt strips

void LightningBolt_float(float4 UV, float4 VertexColor, out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    float dp = abs(UV.y * 2.0 - 1.0);
    Color = lerp(float3(0.35, 0.86, 0.96), float3(1.0, 1.0, 1.0), saturate(1.0 - dp * 4.0));
    Alpha = saturate(1.0 - dp);
#else
    float time = LvTime();
    float d = abs(UV.y * 2.0 - 1.0); // 0 = centre of the strip, 1 = its edge

    // Energy flowing along the bolt (UV scroll): two noise octaves moving in opposite directions.
    float along = UV.x * _NoiseScale + _Seed;
    float flow = LvNoise1(along - time * _FlowSpeed) * 0.6 + LvNoise1(along * 2.7 + time * _FlowSpeed * 1.7 + 13.1) * 0.4;

    // White-hot core whose width breathes with the flow; the glow covers the strip and falls off to its edge.
    float coreWidth = _CoreWidth * (0.7 + 0.6 * flow);
    float core = 1.0 - smoothstep(coreWidth * 0.55, coreWidth, d);
    float halo = exp(-d * d * _GlowFalloff) * (0.8 + 0.2 * flow) * (1.0 - smoothstep(0.72, 1.0, d)); // gone at the strip edge
    float inner = exp(-d * d * _GlowFalloff * 0.8); // 1 next to the core (cyan) .. toward the edge (deeper azure)

    // Bright pulses running from the start to the end.
    float pulse = smoothstep(0.72, 1.0, frac(UV.x * _PulseCount - time * _PulseSpeed + _Seed * 0.37));
    halo = saturate(halo + pulse * 0.25 * exp(-d * d * _GlowFalloff * 0.45));
    inner = saturate(inner + pulse * 0.35);
    core = saturate(core + pulse * 0.35 * (1.0 - smoothstep(0.0, coreWidth * 1.4, d)));

    // Soft ends instead of a hard cut.
    float ends = smoothstep(0.0, 0.035, UV.x) * (1.0 - smoothstep(0.93, 1.0, UV.x));

    float glowAlpha = saturate(halo * _HaloOpacity) * (1.0 - core);
    float coverage = saturate(core + glowAlpha);
    float3 rim = lerp(_EdgeColor.rgb, _GlowColor.rgb, inner);
    float3 emission = _CoreColor.rgb * _CoreBoost * core + rim * glowAlpha;

    Alpha = coverage * ends * VertexColor.a;
    Color = emission / max(coverage, 1e-4) * _Intensity * VertexColor.rgb;
#endif
}

// ------------------------------------------------------------------------------------------------ hit particles

void LightningFlash_float(float4 UV, float4 VertexColor, out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    float rp = length(UV.xy * 2.0 - 1.0);
    Color = float3(0.8, 0.95, 1.0);
    Alpha = saturate(1.0 - rp);
#else
    float2 p = UV.xy * 2.0 - 1.0;
    float r = length(p);
    float age = saturate(UV.w);
    float seed = UV.z * 37.0 + _Seed;
    float shape;
    float hot;
    float3 rim = _GlowColor.rgb;

    if (_Mode < 0.5)
    {
        // Star flash: a round hot centre with uneven electric spikes.
        float angle = atan2(p.y, p.x);
        float spikes = pow(saturate(abs(cos(angle * _Rays * 0.5 + seed))), 18.0);
        float reach = 0.55 + 0.45 * LvNoise1(angle * _Rays / 6.2832 * 3.0 + seed * 5.0);
        float rays = spikes * (1.0 - smoothstep(0.0, reach, r));
        float centre = exp(-r * r * _GlowFalloff);
        shape = saturate(centre + rays * (1.0 - age * 0.6));
        hot = saturate(centre * 1.4 + rays * 0.6);
    }
    else if (_Mode < 1.5)
    {
        // Shock ring: a bright band racing outward (fast, then easing), thinning and fading out.
        float grow = 1.0 - (1.0 - age) * (1.0 - age);
        float radius = 0.2 + 0.75 * grow;
        float width = lerp(0.26, 0.07, age);
        float band = 1.0 - smoothstep(0.0, width, abs(r - radius));
        float flicker = 0.75 + 0.25 * LvNoise1(atan2(p.y, p.x) * 6.0 + seed * 3.0 + LvTime() * 20.0);
        shape = band * flicker * (1.0 - smoothstep(0.55, 1.0, age));
        hot = band * 0.5 * (1.0 - age);
    }
    else
    {
        // Soft glow behind the flash: a see-through tint (cyan in the middle, azure outside) that colours
        // the enemy instead of washing it out white.
        float soft = exp(-r * r * _GlowFalloff * 0.6);
        shape = soft * (1.0 - smoothstep(0.75, 1.0, r)) * _HaloOpacity;
        rim = lerp(_EdgeColor.rgb, _GlowColor.rgb, soft * soft);
        hot = 0.0;
    }

    Alpha = saturate(shape) * VertexColor.a;
    Color = lerp(rim, _CoreColor.rgb * _CoreBoost, saturate(hot)) * _Intensity * VertexColor.rgb;
#endif
}

#endif
