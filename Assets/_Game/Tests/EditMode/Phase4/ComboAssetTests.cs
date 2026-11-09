using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Skills;
using CoopDeck.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopDeck.Tests
{
    /// <summary>Checks the generated combo data, effects, practice deck and scene wiring (run the bootstrap first).</summary>
    public class ComboAssetTests
    {
        const string ComboFolder = "Assets/_Game/ScriptableObjects/Combos";
        const string StarterDeckPath = "Assets/_Game/ScriptableObjects/Cards/Decks/DD_Starter.asset";
        const string PracticeDeckPath = "Assets/_Game/ScriptableObjects/Cards/Decks/DD_ComboPractice.asset";
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string ScenePath = "Assets/_Game/Scenes/Sandbox_Phase1.unity";

        static ComboDefinition Load(string assetName)
        {
            var combo = AssetDatabase.LoadAssetAtPath<ComboDefinition>($"{ComboFolder}/{assetName}.asset");
            Assert.That(combo, Is.Not.Null, assetName + " missing: run the bootstrap");
            return combo;
        }

        [TestCase("CB_Superconductor", Element.Ice, Element.Lightning, ComboKind.ChainBurst, "超伝導バースト")]
        [TestCase("CB_BurningSingularity", Element.Fire, Element.Gravity, ComboKind.Singularity, "灼熱特異点")]
        [TestCase("CB_Overload", Element.Fire, Element.Lightning, ComboKind.Explosion, "オーバーロード")]
        public void Combo_HasTheSpecifiedRules(string asset, Element a, Element b, ComboKind kind, string name)
        {
            ComboDefinition combo = Load(asset);
            Assert.That(combo.elementA, Is.EqualTo(a));
            Assert.That(combo.elementB, Is.EqualTo(b));
            Assert.That(combo.kind, Is.EqualTo(kind));
            Assert.That(combo.displayName, Is.EqualTo(name));
            Assert.That(combo.window, Is.EqualTo(4f), "4 s window");
            Assert.That(combo.cooldown, Is.EqualTo(3f), "3 s re-trigger limit per target");
            Assert.That(combo.coopMultiplier, Is.EqualTo(1.3f).Within(1e-5f), "+30 % co-op bonus");
        }

        [TestCase("CB_Superconductor")]
        [TestCase("CB_BurningSingularity")]
        [TestCase("CB_Overload")]
        public void Combo_HasThreeGrowingTiers_WithEffects(string asset)
        {
            ComboDefinition combo = Load(asset);
            Assert.That(combo.tiers.Length, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                ComboTierSettings tier = combo.tiers[i];
                Assert.That(tier.fx, Is.Not.Null, $"{asset} tier {i} effect");
                Assert.That(tier.fx.GetComponent<PooledFx>(), Is.Not.Null);
                Assert.That(tier.damage, Is.GreaterThan(0f));
                Assert.That(tier.shake, Is.InRange(0f, 0.5f), "short, small shakes only");
                if (i > 0)
                {
                    ComboTierSettings previous = combo.tiers[i - 1];
                    Assert.That(tier.damage, Is.GreaterThan(previous.damage), $"{asset} tier {i} stronger");
                    Assert.That(tier.radius + tier.chainCount + tier.pullRadius,
                        Is.GreaterThan(previous.radius + previous.chainCount + previous.pullRadius), $"{asset} tier {i} reaches further");
                    int layers = tier.fx.GetComponentsInChildren<ParticleSystem>(true).Length;
                    int previousLayers = previous.fx.GetComponentsInChildren<ParticleSystem>(true).Length;
                    Assert.That(layers, Is.GreaterThan(previousLayers), $"{asset} tier {i} effect has more layers");
                }
            }

            if (combo.kind == ComboKind.ChainBurst)
            {
                Assert.That(combo.arcFx, Is.Not.Null);
                Assert.That(combo.arcFx.GetComponent<LightningArcFx>(), Is.Not.Null);
            }

            if (combo.kind == ComboKind.Singularity)
            {
                Assert.That(combo.tiers.All(t => t.hitFx != null), Is.True, "blast effect");
                Assert.That(combo.tiers.All(t => t.fx.GetComponentInChildren<SpiralTrailFx>(true) != null), Is.True, "trail spiral");
                Assert.That(combo.tiers.All(t => t.fx.GetComponentsInChildren<TrailRenderer>(true).Length > 0), Is.True);
            }
        }

        [Test]
        public void ComboParticles_CannotCoverTheScreen()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs/Combos" });
            Assert.That(guids.Length, Is.GreaterThanOrEqualTo(18));
            foreach (string guid in guids)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (ParticleSystemRenderer renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    if (renderer.renderMode == ParticleSystemRenderMode.HorizontalBillboard)
                    {
                        continue; // flat on the ground
                    }

                    Assert.That(renderer.maxParticleSize, Is.LessThanOrEqualTo(0.2f), $"{prefab.name}/{renderer.name}");
                }
            }
        }

        [Test]
        public void OptionalLayers_AreGatedByEffectQuality()
        {
            ComboDefinition overload = Load("CB_Overload");
            VfxQualityGate[] gates = overload.tiers[2].fx.GetComponentsInChildren<VfxQualityGate>(true);
            Assert.That(gates.Any(g => g.MinimumQuality == VfxQuality.Medium), Is.True);
            Assert.That(gates.Any(g => g.MinimumQuality == VfxQuality.High), Is.True);
            Assert.That(overload.tiers[0].fx.GetComponentsInChildren<ParticleSystem>(true)
                .Count(p => p.GetComponentInParent<VfxQualityGate>(true) == null), Is.GreaterThanOrEqualTo(2), "a base layer always plays");
        }

        [Test]
        public void PracticeDeck_IsTwelveExistingElementalCards_WithAllComboPairs()
        {
            var starter = AssetDatabase.LoadAssetAtPath<DeckDefinition>(StarterDeckPath);
            var practice = AssetDatabase.LoadAssetAtPath<DeckDefinition>(PracticeDeckPath);
            Assert.That(practice, Is.Not.Null);
            Assert.That(practice.cards.Count, Is.EqualTo(12));
            Assert.That(practice.handSize, Is.EqualTo(3));
            Assert.That(practice.cards.All(c => starter.cards.Contains(c)), Is.True, "only the existing 12 card types");
            List<Element> elements = practice.cards.Select(c => c.element).ToList();
            Assert.That(elements, Has.Member(Element.Ice));
            Assert.That(elements, Has.Member(Element.Lightning));
            Assert.That(elements, Has.Member(Element.Fire));
            Assert.That(elements, Has.Member(Element.Gravity));
            Assert.That(practice.cards.Count(c => c.cardId == "card.chain_lightning"), Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Player_HasCameraShake_AndThePracticeDeck()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            CameraShaker shaker = prefab.GetComponentInChildren<CameraShaker>(true);
            Assert.That(shaker, Is.Not.Null);
            Assert.That(shaker.GetComponent<Camera>(), Is.Not.Null, "on the view camera, below the aim pivot");
            var deck = prefab.GetComponent<DeckController>();
            var so = new SerializedObject(deck);
            Assert.That(so.FindProperty("practiceDeck").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void Scene_HasTheComboSystem_AuraPresenter_AndComboHud()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                List<ComboSystem> systems = roots.SelectMany(r => r.GetComponentsInChildren<ComboSystem>(true)).ToList();
                Assert.That(systems.Count, Is.EqualTo(1));
                var so = new SerializedObject(systems[0]);
                SerializedProperty combos = so.FindProperty("combos");
                Assert.That(combos.arraySize, Is.EqualTo(3));
                for (int i = 0; i < combos.arraySize; i++)
                {
                    Assert.That(combos.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
                }

                ElementAuraPresenter presenter = systems[0].GetComponent<ElementAuraPresenter>();
                Assert.That(presenter, Is.Not.Null);
                var presenterSo = new SerializedObject(presenter);
                foreach (string field in new[] { "fireAura", "iceAura", "lightningAura", "gravityAura" })
                {
                    Assert.That(presenterSo.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
                }

                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<ComboHud>(true)).Count(), Is.EqualTo(1));
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void VfxQuality_FollowsTheTier_UntilChosen()
        {
            PerformanceTier tier = PerformanceSettings.Tier;
            try
            {
                PerformanceSettings.ResetVfxQuality();
                PerformanceSettings.SetTier(PerformanceTier.Handheld);
                Assert.That(PerformanceSettings.Vfx, Is.EqualTo(VfxQuality.Medium));
                Assert.That(PerformanceSettings.EffectDensity, Is.EqualTo(0.8f), "the handheld keeps most particles");
                Assert.That(PerformanceSettings.EffectLightsEnabled, Is.True, "the handheld lights explosions too");

                PerformanceSettings.SetVfxQuality(VfxQuality.Low);
                PerformanceSettings.SetTier(PerformanceTier.High);
                Assert.That(PerformanceSettings.Vfx, Is.EqualTo(VfxQuality.Low), "an explicit choice sticks");
                Assert.That(PerformanceSettings.EffectDensity, Is.EqualTo(0.3f));
                Assert.That(PerformanceSettings.EffectLightsEnabled, Is.False, "low has no effect lights");
                Assert.That(PerformanceSettings.ShakeFor(VfxQuality.Low), Is.EqualTo(0.5f));

                Assert.That(PerformanceSettings.CycleVfxQuality(), Is.EqualTo(VfxQuality.High));
                Assert.That(PerformanceSettings.EffectLightsEnabled, Is.True);
                Assert.That(PerformanceSettings.TryParseVfx("medium", out VfxQuality parsed) && parsed == VfxQuality.Medium, Is.True);
            }
            finally
            {
                PerformanceSettings.SetTier(tier);
                PerformanceSettings.ResetVfxQuality();
            }
        }
    }
}
