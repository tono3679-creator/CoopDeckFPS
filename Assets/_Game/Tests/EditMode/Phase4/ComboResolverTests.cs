using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using NUnit.Framework;

namespace CoopDeck.Tests
{
    public class ComboResolverTests
    {
        const int Superconductor = 0;
        const int Singularity = 1;
        const int Overload = 2;

        static readonly List<ComboRule> Rules = new List<ComboRule>
        {
            new ComboRule(Superconductor, Element.Ice, Element.Lightning, 4f, 3f, 1.3f),
            new ComboRule(Singularity, Element.Fire, Element.Gravity, 4f, 3f, 1.3f),
            new ComboRule(Overload, Element.Fire, Element.Lightning, 4f, 3f, 1.3f),
        };

        static ElementHit Hit(Element element, int sourceId = 1, TeamId team = TeamId.Players, int power = 0) =>
            new ElementHit(null, element, null, sourceId, team, default, power);

        static bool Resolve(ElementAuraModel auras, Element element, out ComboTrigger trigger, int sourceId = 1, int power = 0) =>
            ComboResolver.Resolve(auras, Rules, Hit(element, sourceId, TeamId.Players, power), out trigger);

        [TestCase(Element.Ice, Element.Lightning, Superconductor)]
        [TestCase(Element.Lightning, Element.Ice, Superconductor)]
        [TestCase(Element.Fire, Element.Gravity, Singularity)]
        [TestCase(Element.Gravity, Element.Fire, Singularity)]
        [TestCase(Element.Fire, Element.Lightning, Overload)]
        [TestCase(Element.Lightning, Element.Fire, Overload)]
        public void MatchingPair_FiresTheCombo_InEitherOrder(Element first, Element second, int expectedRule)
        {
            var auras = new ElementAuraModel();
            Assert.That(Resolve(auras, first, out _), Is.False, "the first element only applies");
            Assert.That(auras.Has(first), Is.True);

            auras.Tick(1f);
            Assert.That(Resolve(auras, second, out ComboTrigger trigger), Is.True);
            Assert.That(trigger.RuleId, Is.EqualTo(expectedRule));
            Assert.That(trigger.PartnerElement, Is.EqualTo(first));
            Assert.That(trigger.IncomingElement, Is.EqualTo(second));
            Assert.That(auras.Has(first), Is.False, "the partner element is used up");
            Assert.That(auras.Has(second), Is.False, "the completing element is not applied");
        }

        [TestCase(Element.Ice, Element.Fire)]
        [TestCase(Element.Ice, Element.Gravity)]
        [TestCase(Element.Lightning, Element.Gravity)]
        [TestCase(Element.Ice, Element.Ice)]
        [TestCase(Element.Fire, Element.Fire)]
        public void NonMatchingPair_DoesNotFire_AndBothElementsStay(Element first, Element second)
        {
            var auras = new ElementAuraModel();
            Resolve(auras, first, out _);
            Assert.That(Resolve(auras, second, out _), Is.False);
            Assert.That(auras.Has(first), Is.True);
            Assert.That(auras.Has(second), Is.True);
        }

        [Test]
        public void AfterTheWindow_NoCombo_AndTheNewElementIsApplied()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _);
            auras.Tick(4.05f);
            Assert.That(Resolve(auras, Element.Lightning, out _), Is.False, "ice wore off after 4 s");
            Assert.That(auras.Has(Element.Lightning), Is.True);
        }

        [Test]
        public void JustInsideTheWindow_StillFires()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _);
            auras.Tick(3.95f);
            Assert.That(Resolve(auras, Element.Lightning, out _), Is.True);
        }

        [Test]
        public void SameComboOnTheSameTarget_HasAThreeSecondCooldown()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _);
            Assert.That(Resolve(auras, Element.Lightning, out _), Is.True);

            Resolve(auras, Element.Ice, out _);
            auras.Tick(1f);
            Assert.That(Resolve(auras, Element.Lightning, out _), Is.False, "on cooldown");
            Assert.That(auras.Has(Element.Ice), Is.True, "nothing is used up during the cooldown");

            auras.Tick(2.1f);
            Assert.That(Resolve(auras, Element.Ice, out _), Is.True, "lightning (applied during the cooldown) + ice after it");
        }

        [Test]
        public void Cooldown_IsPerCombo_OtherCombosStillFire()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _);
            Resolve(auras, Element.Lightning, out _); // superconductor
            Resolve(auras, Element.Fire, out _);
            Assert.That(Resolve(auras, Element.Lightning, out ComboTrigger trigger), Is.True);
            Assert.That(trigger.RuleId, Is.EqualTo(Overload));
        }

        [Test]
        public void SeveralPartners_TheMostRecentReacts()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _);
            auras.Tick(1f);
            Resolve(auras, Element.Fire, out _); // newer
            Assert.That(Resolve(auras, Element.Lightning, out ComboTrigger trigger), Is.True);
            Assert.That(trigger.RuleId, Is.EqualTo(Overload));
            Assert.That(auras.Has(Element.Ice), Is.True, "the older partner stays for a later combo");
        }

        [Test]
        public void DifferentAllies_AreACoopCombo_WithTheBonus()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _, sourceId: 1);
            Assert.That(Resolve(auras, Element.Lightning, out ComboTrigger trigger, sourceId: 2), Is.True);
            Assert.That(trigger.IsCoop, Is.True);
            Assert.That(trigger.DamageMultiplier, Is.EqualTo(1.3f).Within(1e-5f));
            Assert.That(trigger.ApplierId, Is.EqualTo(1));
            Assert.That(trigger.CompleterId, Is.EqualTo(2));
            Assert.That(trigger.ApplierTeam, Is.EqualTo(TeamId.Players));
        }

        [Test]
        public void SameActor_IsNotCoop()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Fire, out _, sourceId: 5);
            Resolve(auras, Element.Gravity, out ComboTrigger trigger, sourceId: 5);
            Assert.That(trigger.IsCoop, Is.False);
            Assert.That(trigger.DamageMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void UnknownActors_AreNotCoop()
        {
            Assert.That(ComboResolver.IsCoop(0, TeamId.Players, 3, TeamId.Players), Is.False);
            Assert.That(ComboResolver.IsCoop(2, TeamId.Players, 3, TeamId.Enemies), Is.False, "different teams");
            Assert.That(ComboResolver.IsCoop(2, TeamId.Players, 3, TeamId.Players), Is.True);
        }

        [TestCase(0, 0, ComboTier.Normal)]
        [TestCase(1, 0, ComboTier.Enhanced)]
        [TestCase(0, 1, ComboTier.Enhanced)]
        [TestCase(2, 1, ComboTier.Ultimate)]
        [TestCase(5, 5, ComboTier.Ultimate)]
        public void Tier_ComesFromTheStrongerCard(int applierLevel, int completerLevel, ComboTier expected)
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _, power: applierLevel);
            Resolve(auras, Element.Lightning, out ComboTrigger trigger, power: completerLevel);
            Assert.That(trigger.Tier, Is.EqualTo(expected));
        }

        [Test]
        public void WhenCombosAreNotAllowed_TheHitOnlyApplies()
        {
            var auras = new ElementAuraModel();
            Resolve(auras, Element.Ice, out _);
            Assert.That(ComboResolver.Resolve(auras, Rules, Hit(Element.Lightning), out _, allowCombo: false), Is.False);
            Assert.That(auras.Has(Element.Ice), Is.True);
            Assert.That(auras.Has(Element.Lightning), Is.True);
        }

        [Test]
        public void DamageBudget_CapsHitsPerTargetPerFrame()
        {
            var budget = new ComboDamageBudget(2);
            Assert.That(budget.TryHit(10, 1), Is.True);
            Assert.That(budget.TryHit(10, 1), Is.True);
            Assert.That(budget.TryHit(10, 1), Is.False, "third hit in one frame");
            Assert.That(budget.TryHit(11, 1), Is.True, "other targets unaffected");
            Assert.That(budget.TryHit(10, 2), Is.True, "next frame resets");
        }
    }
}
