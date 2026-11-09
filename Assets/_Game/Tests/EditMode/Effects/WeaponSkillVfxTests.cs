using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>The rebuilt shot and skill effects (Docs/WEAPON_SKILL_VFX.md): shaders, assignments, layers, timings.</summary>
    public class WeaponSkillVfxTests
    {
        const string Cards = "Assets/_Game/ScriptableObjects/Cards/";
        const string Weapons = "Assets/_Game/ScriptableObjects/Weapons/";

        static CardDefinition Card(string file) => AssetDatabase.LoadAssetAtPath<CardDefinition>(Cards + file + ".asset");

        static readonly string[] Graphs =
        {
            "Assets/_Game/Art/VFX/Skills/Shaders/SG_FX_ElementShell.shadergraph",
            "Assets/_Game/Art/VFX/Skills/Shaders/SG_FX_ElementSigil.shadergraph",
            "Assets/_Game/Art/VFX/Skills/Shaders/SG_FX_ElementCrystal.shadergraph",
        };

        [Test]
        public void ElementGraphs_Compile()
        {
            foreach (string path in Graphs)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Assert.That(shader, Is.Not.Null, path);
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, path);
            }
        }

        [TestCase("WD_Ignis27Blazetail", "Ignis")]
        [TestCase("WD_Volt9Thunderhop", "Thunderhop")]
        public void Weapons_UseTheLayeredShotEffects(string weaponFile, string folder)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Weapons + weaponFile + ".asset");
            Assert.That(weapon.muzzleFlashPrefab.name, Does.EndWith("_Muzzle"));
            Assert.That(AssetDatabase.GetAssetPath(weapon.muzzleFlashPrefab), Does.Contain("/VFX/" + folder + "/"));

            var tracer = weapon.tracerPrefab.GetComponent<ShotTracerFx>();
            Assert.That(tracer, Is.Not.Null, "streak + wake tracer");

            foreach (GameObject impact in new[] { weapon.impactPrefab, weapon.damageableImpactPrefab })
            {
                Assert.That(impact.GetComponentsInChildren<ParticleSystem>(true).Length, Is.GreaterThanOrEqualTo(4), impact.name + ": layered");
            }

            Assert.That(weapon.impactPrefab.transform.Find("Medium/Scorch"), Is.Not.Null, "a scorch on the world (Medium and up)");
            Assert.That(weapon.damageableImpactPrefab.transform.Find("Medium/Scorch"), Is.Null, "no scorch on characters");
            Assert.That(weapon.muzzleFlashPrefab.GetComponentInChildren<Light>(true), Is.Not.Null);
            Assert.That(weapon.muzzleFlashPrefab.GetComponentInChildren<LightFlash>(true), Is.Not.Null, "lights only through LightFlash (High only)");
        }

        [Test]
        public void EveryStarterCard_HasItsOwnEffects()
        {
            string[] cards = { "CD_FlameVortex", "CD_Incendiary", "CD_MeteorStrike", "CD_IceLance", "CD_FrostNova", "CD_ChainLightning",
                "CD_ThunderCharge", "CD_GravitySpike", "CD_GravityCollapse", "CD_Shield", "CD_AreaHeal", "CD_MarkTarget" };
            foreach (string file in cards)
            {
                CardDefinition card = Card(file);
                GameObject[] slots = { card.beamFx, card.impactFx, card.areaFx, card.auraFx, card.telegraphFx };
                Assert.That(slots.Any(s => s != null), Is.True, file);
                foreach (GameObject fx in slots.Where(s => s != null))
                {
                    string path = AssetDatabase.GetAssetPath(fx);
                    Assert.That(path.Contains("/Prefabs/Skills/FX_Skill_"), Is.False, $"{file}: still a shared generic effect ({path})");
                    Assert.That(fx.GetComponent<PooledFx>(), Is.Not.Null, path);
                }
            }
        }

        [Test]
        public void AreaEffects_FollowTheRadius_AndLastAsLongAsTheCard()
        {
            foreach (string file in new[] { "CD_FlameVortex", "CD_Incendiary", "CD_MeteorStrike", "CD_FrostNova", "CD_GravityCollapse", "CD_AreaHeal" })
            {
                CardDefinition card = Card(file);
                Assert.That(card.areaFx.GetComponent<IRadiusFx>(), Is.Not.Null, file + ": sized by the card radius");
                Assert.That(card.areaFx.GetComponent<GroundedFx>(), Is.Not.Null, file + ": dropped onto the ground under the hit");
            }

            Assert.That(Card("CD_MeteorStrike").telegraphFx.GetComponent<GroundedFx>(), Is.Not.Null, "the warning sits on the ground");
            Assert.That(Card("CD_GravitySpike").areaFx.GetComponent<GroundedFx>(), Is.Not.Null, "the pull's disc sits on the ground");

            float burn = Card("CD_Incendiary").effects.Where(e => e.kind == SkillEffectKind.DamageOverTime).Max(e => e.duration);
            Assert.That(Card("CD_Incendiary").areaFx.GetComponent<PooledFx>().Lifetime, Is.GreaterThanOrEqualTo(burn), "the ground burns for the burn");

            float pull = Card("CD_GravityCollapse").effects.Where(e => e.kind == SkillEffectKind.Pull).Max(e => e.duration);
            Assert.That(Card("CD_GravityCollapse").areaFx.GetComponent<PooledFx>().Lifetime, Is.GreaterThan(pull), "the well lasts the pull");

            CardDefinition meteor = Card("CD_MeteorStrike");
            var fall = meteor.telegraphFx.GetComponent<FallingStrikeFx>();
            Assert.That(fall, Is.Not.Null, "the meteor falls during the warning");
            Assert.That(fall.Duration, Is.EqualTo(meteor.impactDelay).Within(1e-4f), "and lands when the damage lands");
        }

        [Test]
        public void StatusEffects_LastWhileTheStatusLasts()
        {
            Assert.That(Card("CD_Shield").auraFx.GetComponent<StatusAuraFx>(), Is.Not.Null);
            Assert.That(Card("CD_ThunderCharge").auraFx.GetComponent<StatusAuraFx>(), Is.Not.Null);
            CardDefinition mark = Card("CD_MarkTarget");
            Assert.That(mark.impactFx.GetComponent<StatusAuraFx>(), Is.Not.Null);
            Assert.That(mark.attachImpactFx, Is.True, "the reticle follows the marked enemy");
            Assert.That(Card("CD_Shield").auraFx.GetComponent<ShapeAnimFx>(), Is.Not.Null, "the barrier grows and breaks");
        }

        [Test]
        public void Beams_AreBeamEffects()
        {
            foreach (string file in new[] { "CD_IceLance", "CD_ChainLightning", "CD_GravitySpike", "CD_MarkTarget", "CD_FlameVortex" })
            {
                Assert.That(Card(file).beamFx.GetComponent<IBeamFx>(), Is.Not.Null, file);
            }

            Assert.That(Card("CD_ChainLightning").beamFx.GetComponent<LightningBoltFx>(), Is.Not.Null, "the stylised bolt, not a line");
            Assert.That(Card("CD_IceLance").beamFx.GetComponent<ThrownBeamFx>(), Is.Not.Null, "a thrown spear");
        }

        [Test]
        public void HeavyLayers_AreQualityGated_AndColoursComeFromThePalette()
        {
            foreach (string file in new[] { "CD_FlameVortex", "CD_MeteorStrike", "CD_GravityCollapse" })
            {
                CardDefinition card = Card(file);
                Assert.That(card.areaFx.GetComponentsInChildren<VfxQualityGate>(true).Length, Is.GreaterThan(0), file);
            }

            var crystal = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/VFX/Skills/Materials/M_FX_Skill_Ice_Crystal.mat");
            Assert.That(crystal.GetColor("_MidColor"), Is.EqualTo(Palette.Ice.Main));
            var vortex = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/VFX/Skills/Materials/M_FX_Skill_Fire_Vortex.mat");
            Assert.That(vortex.GetColor("_MidColor"), Is.EqualTo(Palette.Fire.Main));
        }
    }
}
