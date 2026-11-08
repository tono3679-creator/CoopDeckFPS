// Fireball skill effects, called from the SG_FX_Fire* Shader Graphs (Custom Function, File mode) that
// FireballVfxFactory generates. Material properties are read from each graph's UnityPerMaterial block by
// reference name, so a graph declares every property its function uses.
//
// Blending: the flame bodies (shells, billows, flame particles, the trail, the ring) are alpha blended, so the
// fire keeps its orange and red over a bright sky or sand; only the white-hot core and the sparks add light.
//
// One fire language for every layer: a temperature (0 = cooled edge .. 1 = white-hot) is turned into colour by
// FbFireRamp (the art bible's fire tones, lightly posterized for the toon look), and shapes fade out by eroding
// against flowing 3D noise (a dissolve) instead of turning transparent.
//
// Particle vertex streams (set by the factory): TEXCOORD0 = (uv.x, uv.y, StableRandom.x, AgePercent).
// Mesh renderers have no streams: z and w read 0 and the scripts drive _Seed / _Dissolve instead.
// Cost: ALU only (value noise, 2-3 octaves), no texture fetches.
#ifndef COOPDECK_FIREBALL_VFX_INCLUDED
#define COOPDECK_FIREBALL_VFX_INCLUDED

// ------------------------------------------------------------------------------------------------ noise

float FbHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float FbNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float a = FbHash31(i);
    float b = FbHash31(i + float3(1, 0, 0));
    float c = FbHash31(i + float3(0, 1, 0));
    float d = FbHash31(i + float3(1, 1, 0));
    float e = FbHash31(i + float3(0, 0, 1));
    float g = FbHash31(i + float3(1, 0, 1));
    float h = FbHash31(i + float3(0, 1, 1));
    float k = FbHash31(i + float3(1, 1, 1));
    return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y),
                lerp(lerp(e, g, u.x), lerp(h, k, u.x), u.y), u.z);
}

// Three octaves, stretched to roughly 0..1.
float FbFbm(float3 p)
{
    float s = FbNoise(p) * 0.5333;
    s += FbNoise(p * 2.03 + 17.17) * 0.2667;
    s += FbNoise(p * 4.07 + 41.31) * 0.2;
    return saturate((s - 0.2) * 1.65);
}

// Two octaves for the cheap layers (sparks, smoke, vertex displacement).
float FbFbm2(float3 p)
{
    float s = FbNoise(p) * 0.65 + FbNoise(p * 2.03 + 17.17) * 0.35;
    return saturate((s - 0.18) * 1.6);
}

float2 FbRotate(float2 v, float angle)
{
    float s;
    float c;
    sincos(angle, s, c);
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

#ifndef SHADERGRAPH_PREVIEW

// Wrapped so the noise keeps its precision in long sessions.
float FbTime()
{
    return fmod(_TimeParameters.x, 600.0);
}

// Temperature to colour: deep red -> orange -> hot yellow -> white core, with a light posterize (_Bands) so
// fire reads in the same cel style as the characters. The colours are the palette's (LDR); only the hottest
// part is pushed above the bloom threshold (_CoreBoost), so the glow comes from the core, not the edges.
float3 FbFireRamp(float t)
{
    float steps = max(_BandCount, 1.0);
    t = lerp(t, floor(t * steps + 0.5) / steps, _Bands);
    float3 c = lerp(_DeepColor.rgb, _MidColor.rgb, smoothstep(0.1, 0.42, t));
    c = lerp(c, _HotColor.rgb, smoothstep(0.42, 0.74, t));
    c = lerp(c, _CoreColor.rgb, smoothstep(0.76, 0.97, t));
    return c * (1.0 + (_CoreBoost - 1.0) * smoothstep(0.55, 1.0, t));
}

// 1 at the base centre .. 0 at the outline: a round base and a tip pointing up (billboard UV space, -1..1).
// 0 right in front of the camera .. 1 from about 1.4 x _NearFade on: big flames and smoke never fill the view
// when a blast goes off next to the player. _NearFade 0 turns it off (the hand charge, the flying shot).
float FbNearFade(float3 positionWS)
{
    if (_NearFade <= 0.001)
    {
        return 1.0;
    }

    float d = distance(positionWS, _WorldSpaceCameraPos);
    return saturate((d - _NearFade * 0.35) / _NearFade);
}

float FbFlameShape(float2 p)
{
    const float baseY = -0.3;
    const float baseR = 0.62;
    if (p.y < baseY)
    {
        return saturate(1.0 - length(p - float2(0.0, baseY)) / baseR);
    }

    float t = saturate((p.y - baseY) / 1.25);
    float halfWidth = baseR * pow(1.0 - t, 0.7) + 0.001;
    return saturate(1.0 - sqrt(p.x * p.x / (halfWidth * halfWidth) + t * t));
}

#endif

// ------------------------------------------------------------------------------------------------ core
// SG_FX_FireCore. Mode 0: the projectile's white-hot core (and the blast's first fireball): brightest where
// the sphere faces the camera, swirling energy and a pulse inside, an orange skin at the silhouette.
// Mode 1: a burning debris chunk (mesh particle) that cools from yellow to red along its life, glowing cracks.
void FireCore_float(float3 PositionOS, float3 NormalWS, float3 ViewDirWS, float4 UV, float4 VertexColor,
    out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(1.0, 0.75, 0.35);
    Alpha = 1.0;
#else
    float time = FbTime();
    float facing = saturate(abs(dot(normalize(NormalWS), normalize(ViewDirWS))));
    float seed = _Seed + UV.z * 17.0;
    float age = saturate(UV.w);
    if (_Mode < 0.5)
    {
        float3 p = PositionOS * _NoiseScale;
        p.xy = FbRotate(p.xy, time * _Swirl + seed);
        float energy = FbFbm(p + float3(0.0, 0.0, -time * _FlowSpeed) + seed);
        float pulse = 0.86 + 0.14 * sin(time * _PulseSpeed + seed * 6.2832);
        float heat = saturate(pow(facing, _RimPower) * 0.8 + (energy - 0.45) * 0.55 + 0.22) * pulse;
        Color = FbFireRamp(heat) * _Intensity * VertexColor.rgb;
        float erode = max(_Dissolve, age * _AgeDissolve);
        Alpha = smoothstep(erode - 0.05, erode + 0.05, energy * 0.55 + facing * 0.65) * VertexColor.a;
    }
    else
    {
        float cracks = FbFbm(PositionOS * _NoiseScale + seed);
        float crack = smoothstep(0.42, 0.66, cracks);
        float heat = saturate((1.0 - age * 0.9) * (0.3 + 0.7 * crack) + facing * 0.12);
        Color = FbFireRamp(heat) * _Intensity * VertexColor.rgb;
        Alpha = (1.0 - smoothstep(0.7, 1.0, age)) * VertexColor.a;
    }
#endif
}

// ------------------------------------------------------------------------------------------------ flame volume
// SG_FX_FlameVolume: 3D flames on meshes. On the projectile (FlameShell mesh, +Z = flight direction, uv.y 0 at
// the front .. 1 at the tail end, _TailFade 1) the surface flutters more toward the tail, the noise swirls
// around the flight axis and streams backward, and the tail burns away. In the blast (lumpy billow mesh
// particles, _TailFade 0) each billow swells, cools and erodes over its life (AgePercent).

void FlameVolumeVertex_float(float3 PositionOS, float3 NormalOS, float4 UV, out float3 Position)
{
#ifdef SHADERGRAPH_PREVIEW
    Position = PositionOS;
#else
    float time = FbTime();
    float seed = _Seed + UV.z * 23.0;
    float along = saturate(UV.y) * _TailFade;
    float3 p = PositionOS * _NoiseScale;
    p.xy = FbRotate(p.xy, time * _Swirl + along * 2.5);
    float n = FbFbm2(p + float3(0.0, 0.0, time * _FlowSpeed) + seed);
    float amplitude = _Displace * (0.35 + 0.65 * lerp(1.0, along, _TailFade)) * (1.0 + saturate(UV.w) * 0.6);
    Position = PositionOS + normalize(NormalOS) * (n - 0.45) * amplitude;
#endif
}

void FlameVolume_float(float3 PositionOS, float3 PositionWS, float3 NormalWS, float3 ViewDirWS, float4 UV, float4 VertexColor,
    out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(1.0, 0.5, 0.2);
    Alpha = 0.8;
#else
    float time = FbTime();
    float seed = _Seed + UV.z * 23.0;
    float age = saturate(UV.w);
    float along = saturate(UV.y) * _TailFade;
    float facing = saturate(abs(dot(normalize(NormalWS), normalize(ViewDirWS))));
    float body = pow(facing, _RimPower); // thick and hot through the middle, thin licks at the silhouette

    float3 p = PositionOS * (_NoiseScale * 1.6);
    p.xy = FbRotate(p.xy, time * _Swirl * 1.3 + along * 3.0 + seed);
    float n = FbFbm(p + float3(0.0, 0.0, time * _FlowSpeed * 1.4) + seed * 1.7);
    float fine = FbFbm2(p * 2.3 + float3(0.0, 0.0, time * _FlowSpeed * 2.2) + seed);
    n = n * 0.75 + fine * 0.25;

    // Tongues: the noise carves the outline and the inside into hot and cooler licks (not a flat translucent shell).
    float heat = saturate(body * 0.5 + (n - 0.45) * 1.3 + _HeatBias - along * 0.95 - age * 0.9);
    float erode = max(_Dissolve, age * _AgeDissolve) + along * 0.6;
    float density = (n - 0.32) * 1.7 + body * 0.35 - erode;
    heat = saturate(heat + (density - 0.25) * 0.5); // each lick: red at its edge, yellow inside
    Alpha = smoothstep(0.0, _EdgeSoftness, density) * _Opacity * VertexColor.a * FbNearFade(PositionWS);
    Color = FbFireRamp(heat) * _Intensity * VertexColor.rgb;
#endif
}

// ------------------------------------------------------------------------------------------------ particles
// SG_FX_FireParticle (alpha blended: flames and the trail keep their colour over sky or sand) and SG_FX_FireGlow
// (additive: sparks, embers, the hot trail core and the flash) share this. Mode 0: a flame tongue whose outline flickers and
// scrolls upward, cooling and eroding with age. Mode 1: a spark or ember (stretched particles make streaks),
// white-hot when young, red before it dies. Mode 2: a trail band (TrailRenderer, uv.x = 0 at the newest point
// .. 1 at the oldest) with fire streaming back along it; _TrailFlip swaps the direction.
void FireParticle_float(float3 PositionWS, float4 UV, float4 VertexColor, out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(1.0, 0.55, 0.2);
    Alpha = saturate(1.0 - length(UV.xy * 2.0 - 1.0));
#else
    float time = FbTime();
    float2 uv = UV.xy;
    float seed = UV.z * 31.0 + _Seed;
    float age = saturate(UV.w);
    float2 p = uv * 2.0 - 1.0;
    float heat;
    float density;

    if (_Mode < 0.5)
    {
        float2 q = uv * _NoiseScale + float2(seed, seed * 0.37 - time * _FlowSpeed);
        float w1 = FbFbm2(float3(q, seed));
        float w2 = FbFbm2(float3(q + 5.2, seed + 3.1));
        float tip = saturate(p.y * 0.5 + 0.5);
        p += (float2(w1, w2) - 0.5) * (0.3 + 0.6 * tip);
        float shape = FbFlameShape(p);
        float n = FbFbm(float3(uv * (_NoiseScale * 1.9) + float2(0.0, -time * _FlowSpeed * 1.5), seed + 7.0));
        heat = saturate(shape * 1.05 + (n - 0.5) * 0.55 + _HeatBias - age * _AgeHeat);
        density = shape * (0.65 + 0.7 * n) - age * 0.8;
    }
    else if (_Mode < 1.5)
    {
        float core = saturate(1.0 - length(p));
        heat = saturate(core * 1.35 + _HeatBias - age * _AgeHeat);
        density = core - 0.08;
    }
    else
    {
        float along = _TrailFlip > 0.5 ? 1.0 - uv.x : uv.x;
        float across = 1.0 - abs(uv.y * 2.0 - 1.0);
        float n = FbFbm(float3(along * _NoiseScale * 3.0 - time * _FlowSpeed, uv.y * _NoiseScale, seed));
        heat = saturate(across * 0.85 + (n - 0.5) * 0.7 + _HeatBias - along * 1.1);
        density = across * (0.42 + 0.85 * n) - along * 0.85;
    }

    Alpha = smoothstep(0.0, _EdgeSoftness, density) * VertexColor.a * _Opacity * FbNearFade(PositionWS);
    Color = FbFireRamp(heat) * _Intensity * VertexColor.rgb;
#endif
}

// ------------------------------------------------------------------------------------------------ smoke
// SG_FX_Smoke (alpha blended). Mode 0: a soft, noisy puff that glows warm from the fire while young, then
// greys out and thins away. Mode 1: the scorch left on the ground (dark burn with cinders that die out).
void Smoke_float(float3 PositionWS, float4 UV, float4 VertexColor, out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(0.25, 0.23, 0.3);
    Alpha = 0.5 * saturate(1.0 - length(UV.xy * 2.0 - 1.0));
#else
    float time = FbTime();
    float2 p = UV.xy * 2.0 - 1.0;
    float seed = UV.z * 13.0 + _Seed;
    float age = saturate(UV.w);
    if (_Mode < 0.5)
    {
        float n = FbFbm(float3(UV.xy * _NoiseScale + float2(seed, -time * _FlowSpeed), seed));
        float n2 = FbFbm2(float3(UV.xy * (_NoiseScale * 2.1) + seed, time * 0.3));
        float2 q = p + (float2(n, n2) - 0.5) * 0.6;
        float ball = saturate(1.0 - length(q));
        float density = ball * (0.55 + 0.8 * n) - age * 0.6 - 0.05;
        Alpha = smoothstep(0.0, _EdgeSoftness, density) * _Opacity;
        float glow = saturate(1.0 - age * 3.0) * (0.25 + 0.45 * (1.0 - ball));
        Color = lerp(_SmokeColor.rgb, _GlowColor.rgb, glow);
    }
    else
    {
        float n = FbFbm(float3(UV.xy * _NoiseScale + seed, seed));
        float mark = saturate(1.0 - length(p)) * (0.55 + 0.9 * n);
        Alpha = smoothstep(0.08, 0.08 + _EdgeSoftness, mark) * (1.0 - age) * _Opacity;
        float cinder = smoothstep(0.62, 0.85, n) * saturate(1.0 - age * 2.5);
        Color = lerp(_SmokeColor.rgb, _GlowColor.rgb, cinder);
    }

    Color *= VertexColor.rgb;
    Alpha *= VertexColor.a * FbNearFade(PositionWS);
#endif
}

// ------------------------------------------------------------------------------------------------ shockwave
// SG_FX_Shockwave (alpha blended, ShockwaveRing mesh: uv.x around, uv.y 0 inner .. 1 outer). A hot band near the
// outer edge, broken up by noise around the ring (sampled on a circle, so there is no seam), burning away with age.
void Shockwave_float(float4 UV, float4 VertexColor, out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(1.0, 0.6, 0.25);
    Alpha = UV.y * (1.0 - UV.y) * 4.0;
#else
    float time = FbTime();
    float age = saturate(UV.w);
    float seed = UV.z * 11.0 + _Seed;
    float angle = UV.x * 6.2832;
    float v = UV.y;
    float band = smoothstep(0.45, 0.82, v) * (1.0 - smoothstep(0.86, 1.0, v));
    float3 q = float3(cos(angle) * _NoiseScale, sin(angle) * _NoiseScale, v * 2.0 - time * _FlowSpeed + seed);
    float n = FbFbm(q);
    float density = band * (0.5 + 0.8 * n) - age * _AgeDissolve - 0.05;
    Alpha = smoothstep(0.0, _EdgeSoftness, density) * VertexColor.a * _Opacity;
    float heat = saturate(band * 0.8 + (n - 0.5) * 0.5 + _HeatBias - age * 0.8);
    Color = FbFireRamp(heat) * _Intensity * VertexColor.rgb;
#endif
}

#endif
