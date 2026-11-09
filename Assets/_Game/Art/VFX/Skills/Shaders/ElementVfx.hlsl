// Skill effects that the fire graphs cannot draw, called from the SG_FX_Element* Shader Graphs (Custom Function,
// File mode) that SkillVfxFactory generates. Same conventions as FireballVfx.hlsl: a 4-stop element ramp
// (_DeepColor -> _MidColor -> _HotColor -> _CoreColor, from Palette tones), no textures, value noise only,
// particle streams TEXCOORD0 = (uv.x, uv.y, StableRandom.x, AgePercent); mesh renderers get _Dissolve / _Reveal /
// _Opacity from script (ShapeAnimFx) instead of the age.
//
//   ElementShell  (additive, both faces): a barrier skin - bright fresnel rim, a hex lattice, a band sweeping up.
//   ElementSigil  (additive, both faces): flat procedural circles - heal / reticle / warning / vortex (_Mode 0..3).
//   ElementCrystal (alpha, front faces): faceted ice - flat facets from screen derivatives, a bright rim, inner glow.
#ifndef COOPDECK_ELEMENT_VFX_INCLUDED
#define COOPDECK_ELEMENT_VFX_INCLUDED

float EvHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float EvHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float EvNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float a = EvHash31(i);
    float b = EvHash31(i + float3(1, 0, 0));
    float c = EvHash31(i + float3(0, 1, 0));
    float d = EvHash31(i + float3(1, 1, 0));
    float e = EvHash31(i + float3(0, 0, 1));
    float g = EvHash31(i + float3(1, 0, 1));
    float h = EvHash31(i + float3(0, 1, 1));
    float k = EvHash31(i + float3(1, 1, 1));
    return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y), lerp(lerp(e, g, u.x), lerp(h, k, u.x), u.y), u.z);
}

float EvFbm(float3 p)
{
    float s = EvNoise(p) * 0.6 + EvNoise(p * 2.07 + 13.1) * 0.28 + EvNoise(p * 4.11 + 29.7) * 0.12;
    return saturate((s - 0.18) * 1.6);
}

#ifndef SHADERGRAPH_PREVIEW

float EvTime()
{
    return fmod(_TimeParameters.x, 600.0);
}

// Element ramp: t = 0 deep edge .. 1 white core; only the top end goes above the bloom threshold.
float3 EvRamp(float t)
{
    float3 c = lerp(_DeepColor.rgb, _MidColor.rgb, smoothstep(0.08, 0.45, t));
    c = lerp(c, _HotColor.rgb, smoothstep(0.45, 0.78, t));
    c = lerp(c, _CoreColor.rgb, smoothstep(0.8, 1.0, t));
    return c * (1.0 + (_CoreBoost - 1.0) * smoothstep(0.6, 1.0, t));
}

float EvNearFade(float3 positionWS)
{
    if (_NearFade <= 0.001)
    {
        return 1.0;
    }

    float d = distance(positionWS, _WorldSpaceCameraPos);
    return saturate((d - _NearFade * 0.35) / _NearFade);
}

// Distance (in cells) to the edge of a hex cell: 0 on the edge .. ~0.5 in the middle.
float EvHexEdge(float2 p)
{
    const float2 s = float2(1.0, 1.7320508);
    float4 c = floor(float4(p, p - float2(0.5, 1.0)) / s.xyxy) + 0.5;
    float4 h = float4(p - c.xy * s, p - (c.zw + 0.5) * s);
    float2 q = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
    q = abs(q);
    return 0.5 - max(dot(q, s * 0.5), q.x);
}

float EvRing(float r, float radius, float width)
{
    return saturate(1.0 - abs(r - radius) / max(width, 1e-4));
}

#endif

// ------------------------------------------------------------------------------------------------ shell
void ElementShell_float(float3 PositionOS, float3 PositionWS, float3 NormalWS, float3 ViewDirWS, float4 UV, float4 VertexColor,
    out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(0.4, 1.0, 0.7);
    Alpha = 0.5;
#else
    float time = EvTime();
    float facing = saturate(abs(dot(normalize(NormalWS), normalize(ViewDirWS))));
    float rim = pow(1.0 - facing, _RimPower);

    // Hex lattice on a sphere: longitude/latitude unwrapped (seam at the back is fine for a 1-2 m bubble).
    float3 n = normalize(PositionOS);
    float2 sph = float2(atan2(n.z, n.x) * 3.0, asin(clamp(n.y, -1.0, 1.0)) * 3.0) * _Segments * 0.32;
    float edge = EvHexEdge(sph);
    float lattice = 1.0 - smoothstep(0.0, 0.06, edge);
    float cellPulse = EvHash21(floor(sph * 1.3)) ;
    float shimmer = 0.5 + 0.5 * sin(time * _FlowSpeed + cellPulse * 6.2832);

    // A bright band sweeping up the bubble, and noise so the skin is not uniform.
    float sweep = EvRing(frac(n.y * 0.5 + 0.5 - time * 0.35), 0.5, 0.08);
    float grain = EvFbm(PositionOS * _NoiseScale + float3(0.0, time * 0.4, _Seed));

    // _Reveal grows the skin up from the ground (0 = none .. 1 = whole); _Dissolve eats it away.
    float height = n.y * 0.5 + 0.5;
    float reveal = smoothstep(height - 0.08, height + 0.02, _Reveal);
    float eaten = smoothstep(_Dissolve - 0.08, _Dissolve + 0.08, grain);

    float energy = rim * 0.9 + lattice * (0.35 + 0.4 * shimmer) * (0.4 + rim) + sweep * 0.5 + grain * 0.12;
    float heat = saturate(energy * 0.85 + 0.12);
    Color = EvRamp(heat) * _Intensity * VertexColor.rgb;
    Alpha = saturate(energy) * reveal * eaten * _Opacity * VertexColor.a * EvNearFade(PositionWS);
#endif
}

// ------------------------------------------------------------------------------------------------ sigil
// Flat circle on a quad (uv 0..1, centre 0.5). _Mode 0: heal - rings, ticks and a turning cross pattern;
// 1: reticle - a ring with four brackets closing in; 2: warning - rings with inward chevrons, pulsing;
// 3: vortex - spiral arms turning into the centre (gravity). Particles reveal over age; mesh layers use _Reveal.
void ElementSigil_float(float3 PositionWS, float4 UV, float4 VertexColor, out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(0.4, 1.0, 0.7);
    Alpha = saturate(1.0 - length(UV.xy * 2.0 - 1.0));
#else
    float time = EvTime();
    float age = saturate(UV.w);
    float2 p = UV.xy * 2.0 - 1.0;
    float r = length(p);
    float a = atan2(p.y, p.x);
    float seed = UV.z * 7.0 + _Seed;
    float spin = time * _Spin + seed;
    float reveal = max(_Reveal, smoothstep(0.0, 0.25, age)); // draw in fast, then hold
    float fade = 1.0 - smoothstep(0.75, 1.0, age) * _AgeFade;
    float shape = 0.0;
    float heat = 0.5;

    if (_Mode < 0.5)
    {
        float outer = EvRing(r, 0.93, 0.035) + EvRing(r, 0.84, 0.012);
        float inner = EvRing(r, 0.5, 0.02) * 0.8;
        float ticks = EvRing(r, 0.885, 0.03) * step(0.6, frac((a + spin * 0.4) * _Segments / 6.2832));
        float runes = EvRing(r, 0.67, 0.07) * step(0.55, EvHash21(float2(floor((a - spin * 0.25) * _Segments * 0.5 / 6.2832 * 6.0), seed)));
        float cross = (1.0 - smoothstep(0.03, 0.07, abs(p.x))) + (1.0 - smoothstep(0.03, 0.07, abs(p.y)));
        cross *= (1.0 - smoothstep(0.12, 0.24, r)) * 0.5;
        float glow = (1.0 - smoothstep(0.0, 0.95, r)) * 0.1;
        shape = outer + inner + ticks * 0.8 + runes * 0.5 + cross + glow;
        heat = saturate(0.35 + outer * 0.5 + cross * 0.6);
        shape *= step(r, reveal * 1.02);
    }
    else if (_Mode < 1.5)
    {
        float close = 1.0 - saturate(_Reveal); // ShapeAnimFx drives _Reveal 0 -> 1: the brackets close in
        float ring = EvRing(r, 0.55, 0.075);
        float rotA = a + spin;
        float bracketAngle = abs(frac(rotA / 1.5708 + 0.5) - 0.5) * 1.5708; // distance to the nearest diagonal
        float brackets = EvRing(r, 0.8 + close * 0.3, 0.1) * (1.0 - smoothstep(0.2, 0.27, bracketAngle));
        float dot0 = 1.0 - smoothstep(0.08, 0.13, r);
        float pulse = 0.75 + 0.25 * sin(time * 8.0);
        shape = (ring + brackets) * pulse + dot0 + (1.0 - smoothstep(0.0, 0.7, r)) * 0.18;
        heat = saturate(0.55 + brackets * 0.4 + dot0 * 0.5);
    }
    else if (_Mode < 2.5)
    {
        float pulse = 0.6 + 0.4 * sin(time * 14.0);
        float outer = EvRing(r, 0.95, 0.04) * (0.8 + 0.2 * pulse);
        float inner = EvRing(r, 0.95 - age * 0.9, 0.05);                  // closing in as the strike approaches
        float chev = frac(r * 3.0 + time * 2.5);
        float chevrons = smoothstep(0.75, 0.85, chev) * (1.0 - smoothstep(0.9, 1.0, chev)) * step(0.5, frac(a * 6.0 / 6.2832 + 0.25));
        float fill = (1.0 - smoothstep(0.0, 0.95, r)) * 0.22 * pulse;
        shape = outer + inner + chevrons * 0.5 * step(r, 0.95) + fill;
        heat = saturate(0.4 + outer * 0.5 + inner * 0.4);
    }
    else
    {
        float twist = a * 3.0 + r * 9.0 - time * _Spin * 3.0 + seed;
        float arms = pow(saturate(0.5 + 0.5 * sin(twist)), 6.0);
        float n = EvFbm(float3(p * _NoiseScale, time * 0.6 + seed));
        float body = (1.0 - smoothstep(0.15, 0.98, r)) * smoothstep(0.08, 0.22, r);
        float lip = EvRing(r, 0.2, 0.06);
        shape = arms * body * (0.6 + 0.8 * n) + lip * 1.2;
        heat = saturate(0.25 + arms * 0.55 * (1.0 - r) + lip * 0.6);
    }

    Color = EvRamp(heat) * _Intensity * VertexColor.rgb;
    Alpha = saturate(shape) * fade * _Opacity * VertexColor.a * step(r, 1.0) * EvNearFade(PositionWS);
#endif
}

// ------------------------------------------------------------------------------------------------ crystal
// Faceted ice (alpha blended). Facet normals come from the screen-space derivatives of the world position, so any
// low-poly mesh reads cut and sharp. Deep blue inside, a white rim at grazing angles, glints on facets that face the
// light direction, frosty noise. Ages (particles) or _Dissolve (meshes) crumble it away from the tips.
void ElementCrystal_float(float3 PositionOS, float3 PositionWS, float3 ViewDirWS, float4 UV, float4 VertexColor,
    out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(0.5, 0.85, 1.0);
    Alpha = 0.85;
#else
    float time = EvTime();
    float3 facet = normalize(cross(ddy(PositionWS), ddx(PositionWS)));
    float3 v = normalize(ViewDirWS);
    float facing = saturate(abs(dot(facet, v)));
    float rim = pow(1.0 - facing, _RimPower);
    float3 lightDir = normalize(float3(-0.45, 0.8, -0.35));
    float glint = pow(saturate(dot(reflect(-v, facet), lightDir)), 24.0);
    float frost = EvFbm(PositionOS * _NoiseScale + _Seed + UV.z * 5.0);
    float age = saturate(UV.w);
    float erode = max(_Dissolve, age * _AgeFade);

    float heat = saturate(0.3 + facing * 0.25 + rim * 0.55 + frost * 0.15 + glint);
    Color = EvRamp(heat) * _Intensity * VertexColor.rgb;
    float density = 0.55 + frost * 0.6 - erode * 1.2;
    Alpha = smoothstep(0.0, _EdgeSoftness, density) * saturate(0.62 + rim * 0.4) * _Opacity * VertexColor.a * EvNearFade(PositionWS);
#endif
}

#endif
