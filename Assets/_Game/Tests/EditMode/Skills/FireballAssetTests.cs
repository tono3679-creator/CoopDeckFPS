using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Skills;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The generated fireball (FireballVfxFactory, CardDataFactory.EnsureFireball; run the bootstrap first): the
    /// card's numbers, its place in the starter deck, the Shader Graphs compiling, every layer drawn with them in
    /// the palette's fire tones, quality-gated layers, particle streams, 3D meshes and the blast's surface handling.
    /// </summary>
    public class FireballAssetTests
    {
        const string CardPath = "Assets/_Game/ScriptableObjects/Cards/CD_Fireball.asset";
        const string StarterDeckPath = "Assets/_Game/ScriptableObjects/Cards/Decks/DD_Starter.asset";
        const string Prefabs = "Assets/_Game/Prefabs/Skills/Fireball";
        const string Art = "Assets/_Game/Art/VFX/Fireball";

        static readonly string[] Graphs = { "SG_FX_FireCore", "SG_FX_FlameVolume", "SG_FX_FireParticle", "SG_FX_FireGlow", "SG_FX_Smoke", "SG_FX_Shockwave" };
        static readonly string[] PrefabNames = { "FX_Fireball_Cast", "FX_Fireball_Projectile", "FX_Fireball_Explosion", "FX_Fireball_TargetFlare", "FX_Burning" };

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run the bootstrap");
            return asset;
        }

        static GameObject Prefab(string name) => Load<GameObject>($"{Prefabs}/{name}.prefab");

        [Test]
        public void Card_IsAFireProjectile_WithTheAgreedNumbers()
        {
            var card = Load<CardDefinition>(CardPath);
            Assert.That(card.cardId, Is.EqualTo("card.fireball"));
            Assert.That(card.element, Is.EqualTo(Element.Fire));
            Assert.That(card.targeting, Is.EqualTo(CardTargeting.Aim));
            Assert.That(card.gaugeCost, Is.EqualTo(25f));
            Assert.That(card.cooldown, Is.EqualTo(0.5f));
            Assert.That(card.projectile.IsProjectile, Is.True);
            Assert.That(card.projectile.speed, Is.EqualTo(45f));
            Assert.That(card.projectile.launchDelay, Is.EqualTo(0.12f).Within(1e-4f), "launched when the quick cast's palm comes forward");
            Assert.That(card.effects.Select(e => (e.kind, e.amount, e.radius)), Is.EqualTo(new[]
            {
                (SkillEffectKind.Damage, 45f, 0f),
                (SkillEffectKind.AreaDamage, 20f, 2.5f),
                (SkillEffectKind.DamageOverTime, 6f, 2.5f),
            }));
            Assert.That(card.effects[2].duration, Is.EqualTo(3f));
            Assert.That(SkillMotionRules.For(card), Is.EqualTo(CoopDeck.Animation.SkillMotion.QuickCast));
        }

        [Test]
        public void Card_PlaysItsOwnEffects_ForEveryPhase()
        {
            var card = Load<CardDefinition>(CardPath);
            Assert.That(card.castFx, Is.EqualTo(Prefab("FX_Fireball_Cast")));
            Assert.That(card.projectileFx, Is.EqualTo(Prefab("FX_Fireball_Projectile")));
            Assert.That(card.areaFx, Is.EqualTo(Prefab("FX_Fireball_Explosion")));
            Assert.That(card.impactFx, Is.EqualTo(Prefab("FX_Fireball_TargetFlare")));
            Assert.That(card.attachImpactFx, Is.True, "the flames cling to the enemy hit");
            Assert.That(card.projectileFx.GetComponent<IProjectileFx>(), Is.Not.Null);
            Assert.That(card.areaFx.GetComponent<IRadiusFx>(), Is.Not.Null);
            Assert.That(card.areaFx.GetComponent<ISurfaceFx>(), Is.Not.Null);
            Assert.That(card.areaFx.GetComponent<FireballExplosionFx>().AuthoredRadius, Is.EqualTo(card.MaxRadius), "the blast is drawn for the card's radius");
            Assert.That(card.icon, Is.Not.Null);
        }

        [Test]
        public void StarterDeck_HasTheFireball_AsThe13thCard()
        {
            var deck = Load<DeckDefinition>(StarterDeckPath);
            var card = Load<CardDefinition>(CardPath);
            Assert.That(deck.cards.Count, Is.EqualTo(13));
            Assert.That(deck.cards.Count(c => c == card), Is.EqualTo(1));
            Assert.That(deck.cards.Distinct().Count(), Is.EqualTo(13), "the 12 starting cards stay");
        }

        [Test]
        public void ShaderGraphs_Import_AndCompile_WithoutErrors()
        {
            foreach (string graph in Graphs)
            {
                var shader = Load<Shader>($"{Art}/Shaders/{graph}.shadergraph");
                Assert.That(shader.name, Is.EqualTo("CoopDeck/FX/" + graph));
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, graph + " has errors (see the console)");
                ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
                Assert.That(messages.Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m => m.message), Is.Empty, graph);
                Assert.That(shader.isSupported, Is.True, graph);
                Assert.That(shader.FindSubshaderTagValue(0, new UnityEngine.Rendering.ShaderTagId("RenderType")).name, Is.EqualTo("Transparent"), graph);
            }
        }

        [Test]
        public void Materials_DrawAfterTheWorld()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Art + "/Materials" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent), material.name);
            }
        }

        [Test]
        public void EveryLayer_IsDrawnWithTheFireballGraphs_InThePaletteFireTones()
        {
            var renderers = new List<Renderer>();
            foreach (string name in PrefabNames)
            {
                renderers.AddRange(Prefab(name).GetComponentsInChildren<Renderer>(true));
            }

            Assert.That(renderers.Count, Is.GreaterThan(30));
            foreach (Renderer renderer in renderers)
            {
                Material m = renderer.sharedMaterial;
                Assert.That(m, Is.Not.Null, renderer.name);
                Assert.That(m.shader.name, Does.StartWith("CoopDeck/FX/SG_FX_"), $"{renderer.name}: {m.name}");
                AssertColor(m, "_DeepColor", Palette.Fire.Deep);
                AssertColor(m, "_MidColor", Palette.Fire.Main);
                AssertColor(m, "_CoreColor", Palette.Fire.Core);
                AssertColor(m, "_SmokeColor", Palette.Smoke);
            }
        }

        static void AssertColor(Material m, string property, Color expected)
        {
            Color c = m.GetColor(property);
            Assert.That(new[] { c.r, c.g, c.b }, Is.EqualTo(new[] { expected.r, expected.g, expected.b }).Within(0.002f), $"{m.name}.{property}");
        }

        [Test]
        public void Projectile_IsLayered_AndScalesWithQuality()
        {
            GameObject projectile = Prefab("FX_Fireball_Projectile");
            Assert.That(projectile.GetComponent<PooledFx>(), Is.Not.Null);
            MeshRenderer[] meshes = projectile.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(meshes.Select(r => r.name), Is.EquivalentTo(new[] { "Core", "FlameInner", "FlameOuter" }), "a 3D core inside two flame shells");
            Assert.That(projectile.GetComponentsInChildren<TrailRenderer>(true).Length, Is.EqualTo(2));
            Assert.That(projectile.GetComponentsInChildren<ParticleSystem>(true).Length, Is.GreaterThanOrEqualTo(5));
            Assert.That(Gate(projectile, "FlameOuterLayer"), Is.EqualTo(VfxQuality.Medium));
            Assert.That(Gate(projectile, "GlowLayer"), Is.EqualTo(VfxQuality.High));
            Assert.That(Gate(projectile, "SmokeLayer"), Is.EqualTo(VfxQuality.High));

            ParticleSystem flames = projectile.transform.Find("Flames").GetComponent<ParticleSystem>();
            Assert.That(flames.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World), "flames stay behind: the fire streams backward");
            Assert.That(flames.emission.rateOverDistanceMultiplier, Is.GreaterThan(0f), "emitted per metre, so a fast shot leaves no gaps");
        }

        [Test]
        public void Explosion_Is3D_LeavesAnAftermath_AndScalesWithQuality()
        {
            GameObject blast = Prefab("FX_Fireball_Explosion");
            ParticleSystemRenderer[] renderers = blast.GetComponentsInChildren<ParticleSystemRenderer>(true);
            string[] meshLayers = renderers.Where(r => r.renderMode == ParticleSystemRenderMode.Mesh).Select(r => r.name).ToArray();
            Assert.That(meshLayers, Is.SupersetOf(new[] { "Fireball", "Billows", "Shockwave", "Debris", "Scorch" }), "volumetric billows, a ring and solid debris");
            Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(12));

            ParticleSystem debris = blast.GetComponentsInChildren<ParticleSystem>(true).First(p => p.name == "Debris");
            Assert.That(debris.collision.enabled, Is.True, "burning chunks fall and bounce");
            Assert.That(debris.main.gravityModifier.constant, Is.GreaterThan(0.5f));
            Assert.That(blast.GetComponentsInChildren<Light>(true).Single().GetComponentInParent<VfxQualityGate>(true).MinimumQuality, Is.EqualTo(VfxQuality.High));
            Assert.That(blast.GetComponentsInChildren<VfxQualityGate>(true).Any(g => g.MinimumQuality == VfxQuality.Medium), Is.True);
            Assert.That(blast.GetComponentsInChildren<ParticleSystem>(true).Count(p => p.GetComponentInParent<VfxQualityGate>(true) == null), Is.GreaterThanOrEqualTo(8),
                "the low quality still has a full blast (fewer particles, no extras)");

            var so = new SerializedObject(blast.GetComponent<FireballExplosionFx>());
            SerializedProperty surfaceOnly = so.FindProperty("surfaceOnly");
            Assert.That(surfaceOnly.arraySize, Is.EqualTo(1));
            Assert.That(surfaceOnly.GetArrayElementAtIndex(0).objectReferenceValue.name, Is.EqualTo("Scorch"), "no scorch in mid-air on a character");
        }

        [Test]
        public void Particles_CarryRandomAndAge_ToTheShaders()
        {
            var expected = new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent,
            };
            var streams = new List<ParticleSystemVertexStream>();
            foreach (string name in PrefabNames)
            {
                foreach (ParticleSystemRenderer renderer in Prefab(name).GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    renderer.GetActiveVertexStreams(streams);
                    Assert.That(streams, Is.EqualTo(expected), $"{name}/{renderer.name}: TEXCOORD0 = (uv, random, age)");
                    if (renderer.renderMode != ParticleSystemRenderMode.Mesh && renderer.name != "Flash")
                    {
                        Assert.That(renderer.maxParticleSize, Is.LessThanOrEqualTo(0.5f), $"{name}/{renderer.name} never fills the screen");
                    }
                }
            }
        }

        [Test]
        public void Meshes_AreClosed3DShapes_FacingOutward()
        {
            Mesh shell = Load<Mesh>($"{Art}/Meshes/M_Fireball_FlameShell.asset");
            Assert.That(shell.vertexCount, Is.GreaterThan(600), "enough vertices for the flames to move");
            Assert.That(shell.bounds.min.z, Is.LessThan(-1.4f), "a comet tail behind the flight direction");
            Assert.That(shell.bounds.max.z, Is.EqualTo(0.5f).Within(0.01f));
            foreach (string name in new[] { "M_Fireball_Core", "M_Fireball_FlameShell", "M_Fireball_Billow", "M_Fireball_EmberChunk" })
            {
                Mesh mesh = Load<Mesh>($"{Art}/Meshes/{name}.asset");
                Vector3[] v = mesh.vertices;
                Vector3[] normals = mesh.normals;
                int[] t = mesh.triangles;
                bool shellMesh = name == "M_Fireball_FlameShell"; // not convex: its authored normals say where outside is
                Vector3 center = mesh.bounds.center;
                int inward = 0;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    Vector3 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    Vector3 outward = shellMesh ? normals[t[i]] + normals[t[i + 1]] + normals[t[i + 2]] : c - center;
                    if (n.sqrMagnitude > 1e-12f && outward.sqrMagnitude > 1e-6f && Vector3.Dot(n, outward) < 0f)
                    {
                        inward++;
                    }
                }

                Assert.That(inward, Is.Zero, name + " triangles facing inward");
            }
        }

        static VfxQuality? Gate(GameObject root, string child)
        {
            Transform t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == child);
            Assert.That(t, Is.Not.Null, child);
            return t.GetComponent<VfxQualityGate>()?.MinimumQuality;
        }

        [Test]
        public void VisibleShot_LeavesTheHand_AndJoinsTheCrosshairPath()
        {
            Assert.That(SkillCaster.VisualOffsetWeight(0f), Is.EqualTo(1f));
            Assert.That(SkillCaster.VisualOffsetWeight(SkillCaster.ConvergeDistance), Is.EqualTo(0f));
            float previous = 1f;
            for (float d = 0.25f; d <= SkillCaster.ConvergeDistance; d += 0.25f)
            {
                float w = SkillCaster.VisualOffsetWeight(d);
                Assert.That(w, Is.LessThanOrEqualTo(previous));
                previous = w;
            }
        }
    }
}
