using System.Collections.Generic;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Stylized lightning (Docs/LIGHTNING_VFX.md): the zig-zag path, the per-quality amounts, and the generated
    /// Shader Graph materials and prefabs the Thunderhop's 帯電連射 uses.
    /// </summary>
    public class LightningVfxTests
    {
        const string Prefabs = "Assets/_Game/Prefabs/VFX/Lightning/";

        [Test]
        public void Path_StartsAndEndsExactly_StaysWithinTheAmplitude_AndRepeatsWithTheSameSeed()
        {
            var start = new Vector3(0f, 1f, 0f);
            var end = new Vector3(0f, 1f, 10f);
            var points = new List<Vector3>();
            var offsets = new List<Vector2>();
            LightningPath.Build(start, end, 12, 0.6f, new System.Random(5), offsets, true, 0f, points);

            Assert.That(points.Count, Is.EqualTo(13));
            Assert.That(points[0], Is.EqualTo(start));
            Assert.That(points[12], Is.EqualTo(end));
            foreach (Vector3 p in points)
            {
                float off = new Vector2(p.x, p.y - 1f).magnitude;
                Assert.That(off, Is.LessThanOrEqualTo(0.6f + 1e-4f));
            }

            var again = new List<Vector3>();
            LightningPath.Build(start, end, 12, 0.6f, new System.Random(5), new List<Vector2>(), true, 0f, again);
            Assert.That(again, Is.EqualTo(points), "same seed, same bolt");

            // A re-roll keeps the big shape (the stored offsets) and only adds detail, still inside the amplitude.
            var flicker = new List<Vector3>();
            LightningPath.Build(start, end, 12, 0.6f, new System.Random(9), offsets, false, 0.35f, flicker);
            for (int i = 0; i < flicker.Count; i++)
            {
                Assert.That(new Vector2(flicker[i].x, flicker[i].y - 1f).magnitude, Is.LessThanOrEqualTo(0.6f + 1e-4f));
            }
        }

        [Test]
        public void Path_IsAStylizedZigZag_MostKinksSwapSides()
        {
            var points = new List<Vector3>();
            var offsets = new List<Vector2>();
            int swaps = 0;
            int pairs = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                LightningPath.Build(Vector3.zero, Vector3.forward * 10f, 14, 0.6f, new System.Random(seed), offsets, true, 0f, points);
                for (int i = 1; i < offsets.Count; i++)
                {
                    pairs++;
                    if (Mathf.Sign(offsets[i].x) != Mathf.Sign(offsets[i - 1].x))
                    {
                        swaps++;
                    }
                }
            }

            Assert.That(swaps / (float)pairs, Is.InRange(0.6f, 0.9f));
            Assert.That(LightningPath.Segments(1f, 0.6f), Is.EqualTo(6));
            Assert.That(LightningPath.Segments(60f, 0.6f), Is.EqualTo(24));
        }

        [Test]
        public void Amounts_FollowTheQuality_AndTheChainedStrikeGetsFewerBranches()
        {
            Assert.That(LightningPath.BranchCount(VfxQuality.High, 1f), Is.EqualTo(4));
            Assert.That(LightningPath.BranchCount(VfxQuality.High, 0.65f), Is.EqualTo(2));
            Assert.That(LightningPath.BranchCount(VfxQuality.Medium, 1f), Is.EqualTo(2), "Ally X default");
            Assert.That(LightningPath.BranchCount(VfxQuality.Medium, 0.65f), Is.EqualTo(1));
            Assert.That(LightningPath.BranchCount(VfxQuality.Low, 1f), Is.EqualTo(0));
            Assert.That(LightningPath.ResidualArcCount(VfxQuality.High), Is.EqualTo(3));
            Assert.That(LightningPath.ResidualArcCount(VfxQuality.Medium), Is.EqualTo(2));
            Assert.That(LightningPath.ResidualArcCount(VfxQuality.Low), Is.EqualTo(1));

            // A bolt lasts its flashes plus the last one's fade.
            Assert.That(LightningBoltFx.DurationFor(1, 0.06f, 0.1f), Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(LightningBoltFx.DurationFor(3, 0.1f, 0.15f), Is.EqualTo(0.35f).Within(1e-5f));
        }

        [Test]
        public void DischargeTiming_SecondBeatComesAfterTheFirst_AndEachHopArrivesAfterItStarts()
        {
            DischargeTiming timing = DischargeTiming.Default;
            Assert.That(timing.ArcStart(0), Is.InRange(0.065f, 0.085f), "the chained arc 65–85 ms after the first strike");
            Assert.That(timing.Arrival(0), Is.EqualTo(timing.ArcStart(0) + timing.chainTravel).Within(1e-6f));
            Assert.That(timing.chainTravel, Is.InRange(0.02f, 0.06f), "the tip visibly runs, but the beat stays tight");
            Assert.That(timing.ArcStart(1), Is.GreaterThan(timing.Arrival(0)), "a third enemy waits for the second one's burst");

            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "FX_Lightning_Bolt.prefab").GetComponent<LightningBoltFx>();
            Assert.That(bolt.Duration, Is.InRange(0.08f, 0.12f), "the first strike's white core is gone 80–120 ms after it lands");
            Assert.That(bolt.Strokes, Is.EqualTo(1));
        }

        [Test]
        public void Materials_HaveAWhiteHotCore_ACyanBody_AndAnAzureEdge_AndTheStrikeComesFromHigh()
        {
            const string folder = "Assets/_Game/Art/VFX/Lightning/Materials/";
            var bolt = AssetDatabase.LoadAssetAtPath<Material>(folder + "M_FX_Lightning_Bolt.mat");
            Assert.That(bolt, Is.Not.Null);
            Color glow = bolt.GetColor("_GlowColor");
            Color edge = bolt.GetColor("_EdgeColor");
            Assert.That(bolt.GetFloat("_CoreBoost"), Is.InRange(2.5f, 4f), "HDR core that blooms without drowning the colour");
            Assert.That(glow.maxColorComponent, Is.InRange(1f, 1.3f), "the cyan body blooms a little (much brighter turns white in the tonemapper)");
            Assert.That(edge.maxColorComponent, Is.LessThanOrEqualTo(1f), "the edge does not bloom");
            Assert.That(edge.g / edge.b, Is.LessThan(glow.g / glow.b), "the edge is a deeper azure than the cyan body");
            Assert.That(bolt.GetFloat("_CoreWidth"), Is.LessThanOrEqualTo(0.15f), "a thin white core: the cyan body carries the colour");
            Assert.That(bolt.GetFloat("_HaloOpacity"), Is.InRange(0.6f, 1f), "the glow covers a bright background instead of adding to it");
            foreach (string graph in new[] { "SG_FX_LightningBolt", "SG_FX_LightningFlash" })
            {
                string text = System.IO.File.ReadAllText("Assets/_Game/Art/VFX/Lightning/Shaders/" + graph + ".shadergraph");
                Assert.That(text, Does.Contain("\"m_AlphaMode\": 0"), graph + " uses alpha blending (additive cyan turned white on bright scenes)");
            }

            var softGlow = AssetDatabase.LoadAssetAtPath<Material>(folder + "M_FX_Lightning_Glow.mat");
            Assert.That(softGlow.GetFloat("_HaloOpacity"), Is.LessThanOrEqualTo(0.5f), "a see-through tint around the hit, not a white blob");

            Assert.That(WeaponFx.StrikeHeight, Is.GreaterThanOrEqualTo(6f), "the strike comes in from above the top of the screen");
        }

        [Test]
        public void GeneratedPrefabs_UseTheShaderGraphs_AndAreWiredIntoTheThunderhop()
        {
            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "FX_Lightning_Bolt.prefab");
            var hit = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "FX_Lightning_Hit.prefab");
            var hitChain = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "FX_Lightning_Hit_Chain.prefab");
            Assert.That(bolt, Is.Not.Null, "run LightningVfxFactory (bootstrap / ThunderhopAssetFactory)");
            Assert.That(hit, Is.Not.Null);
            Assert.That(hitChain, Is.Not.Null);

            Assert.That(bolt.GetComponent<LightningBoltFx>(), Is.Not.Null);
            LineRenderer[] strips = bolt.GetComponentsInChildren<LineRenderer>(true);
            Assert.That(strips.Length, Is.EqualTo(5), "main + 4 branches");
            foreach (LineRenderer strip in strips)
            {
                Assert.That(strip.sharedMaterial.shader.name, Does.EndWith("SG_FX_LightningBolt"));
            }

            Assert.That(hit.GetComponentInChildren<SurfaceArcFx>(true), Is.Not.Null, "residual arcs");
            Transform ring = hit.transform.Find("GroundRing");
            Assert.That(ring.GetComponent<VfxQualityGate>().MinimumQuality, Is.EqualTo(VfxQuality.Medium));
            Assert.That(ring.GetComponent<ParticleSystemRenderer>().renderMode, Is.EqualTo(ParticleSystemRenderMode.HorizontalBillboard), "a ring on the floor, apart from the chest flash");
            ParticleSystem.MainModule ringMain = ring.GetComponent<ParticleSystem>().main;
            Assert.That(ringMain.startLifetime.constantMax, Is.InRange(0.15f, 0.22f), "spreads and fades in 150–220 ms");
            var impact = hit.GetComponent<LightningImpactFx>();
            Assert.That(impact, Is.Not.Null);
            Assert.That(impact.GroundRing, Is.SameAs(ring));
            Assert.That(hit.GetComponentInChildren<AudioSource>(true).clip, Is.Not.Null, "the burst sound");
            Assert.That(hitChain.GetComponentInChildren<AudioSource>(true).clip, Is.Not.SameAs(hit.GetComponentInChildren<AudioSource>(true).clip), "a higher zap on the chained enemy");
            ParticleSystem.Burst shards = hit.transform.Find("Shards").GetComponent<ParticleSystem>().emission.GetBurst(0);
            Assert.That(shards.minCount, Is.EqualTo(8));
            Assert.That(shards.maxCount, Is.EqualTo(12));
            Assert.That(hit.transform.Find("Flash").GetComponent<ParticleSystem>().main.startLifetime.constantMax, Is.InRange(0.035f, 0.055f), "a 35–55 ms white peak");
            Assert.That(hit.transform.Find("Light").GetComponent<VfxQualityGate>().MinimumQuality, Is.EqualTo(VfxQuality.High));
            var flash = hit.transform.Find("Flash").GetComponent<ParticleSystemRenderer>();
            Assert.That(flash.sharedMaterial.shader.name, Does.EndWith("SG_FX_LightningFlash"));
            Assert.That(flash.maxParticleSize, Is.LessThanOrEqualTo(0.25f), "its spikes pass the outline but never fill the screen up close");
            float chainFlash = hitChain.transform.Find("Flash").GetComponent<ParticleSystem>().main.startSize.constantMax;
            float firstFlash = hit.transform.Find("Flash").GetComponent<ParticleSystem>().main.startSize.constantMax;
            Assert.That(chainFlash / firstFlash, Is.EqualTo(0.7f).Within(0.02f), "the second burst is about 70 % of the first");

            var thunderhop = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Game/ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset");
            Assert.That(thunderhop.chargeBurst.arcFx, Is.SameAs(bolt));
            Assert.That(thunderhop.chargeBurst.burstFx, Is.SameAs(hit));
            Assert.That(thunderhop.chargeBurst.chainBurstFx, Is.SameAs(hitChain));
        }
    }
}
