using System.Collections.Generic;
using System.Linq;
using CoopDeck.Combat;
using CoopDeck.Core;
using NUnit.Framework;

namespace CoopDeck.Tests
{
    public class ElementAuraModelTests
    {
        [Test]
        public void Apply_StoresTheElement_WithApplierTeamAndPower()
        {
            var model = new ElementAuraModel();
            model.Apply(Element.Ice, null, 7, TeamId.Players, 1);

            Assert.That(model.Has(Element.Ice), Is.True);
            Assert.That(model.TryGet(Element.Ice, out ElementAura aura), Is.True);
            Assert.That(aura.ApplierId, Is.EqualTo(7));
            Assert.That(aura.ApplierTeam, Is.EqualTo(TeamId.Players));
            Assert.That(aura.Power, Is.EqualTo(1));
            Assert.That(aura.Remaining, Is.EqualTo(ElementAuraModel.DefaultDuration));
            Assert.That(model.Has(Element.Fire), Is.False);
        }

        [Test]
        public void Element_ExpiresAfterFourSeconds()
        {
            var model = new ElementAuraModel();
            var removed = new List<(Element, ElementRemoveReason)>();
            model.Removed += (e, r) => removed.Add((e, r));
            model.Apply(Element.Fire, null, 1, TeamId.Players, 0);

            model.Tick(3.9f);
            Assert.That(model.Has(Element.Fire), Is.True);
            model.Tick(0.2f);
            Assert.That(model.Has(Element.Fire), Is.False);
            Assert.That(removed, Is.EqualTo(new[] { (Element.Fire, ElementRemoveReason.Expired) }));
        }

        [Test]
        public void Reapplying_RefreshesDuration_AndApplier()
        {
            var model = new ElementAuraModel();
            model.Apply(Element.Gravity, null, 1, TeamId.Players, 0);
            model.Tick(3f);
            model.Apply(Element.Gravity, null, 2, TeamId.Players, 2);
            model.Tick(3f);

            Assert.That(model.TryGet(Element.Gravity, out ElementAura aura), Is.True, "refreshed, so still active after 6 s total");
            Assert.That(aura.ApplierId, Is.EqualTo(2));
            Assert.That(aura.Power, Is.EqualTo(2));
            Assert.That(aura.Age, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void SeveralElements_Coexist_AndConsumeRemovesOnlyOne()
        {
            var model = new ElementAuraModel();
            model.Apply(Element.Ice, null, 1, TeamId.Players, 0);
            model.Apply(Element.Fire, null, 1, TeamId.Players, 0);
            Assert.That(model.ActiveCount, Is.EqualTo(2));

            Assert.That(model.Consume(Element.Ice), Is.True);
            Assert.That(model.Consume(Element.Ice), Is.False, "already used up");
            Assert.That(model.Has(Element.Fire), Is.True);
            Assert.That(model.ActiveAuras().Select(a => a.Element), Is.EqualTo(new[] { Element.Fire }));
        }

        [Test]
        public void Clear_RemovesEverything_ButKeepsCooldownsUnlessAsked()
        {
            var model = new ElementAuraModel();
            model.Apply(Element.Lightning, null, 1, TeamId.Players, 0);
            model.StartCooldown(0, 3f);
            model.Clear();
            Assert.That(model.ActiveCount, Is.EqualTo(0));
            Assert.That(model.CooldownRemaining(0), Is.EqualTo(3f));

            model.Clear(includeCooldowns: true);
            Assert.That(model.CooldownRemaining(0), Is.EqualTo(0f));
        }

        [Test]
        public void Cooldowns_CountDown_Independently()
        {
            var model = new ElementAuraModel();
            model.StartCooldown(0, 3f);
            model.StartCooldown(2, 1f);
            model.Tick(1.5f);
            Assert.That(model.CooldownRemaining(0), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(model.CooldownRemaining(2), Is.EqualTo(0f));
            model.Tick(1.6f);
            Assert.That(model.CooldownRemaining(0), Is.EqualTo(0f));
        }

        [Test]
        public void IceAndGravity_SlowMovement_AndStack()
        {
            var model = new ElementAuraModel();
            Assert.That(model.MoveSpeedMultiplier, Is.EqualTo(1f));
            model.Apply(Element.Ice, null, 1, TeamId.Players, 0);
            Assert.That(model.MoveSpeedMultiplier, Is.EqualTo(ElementAuraModel.IceMoveMultiplier));
            model.Apply(Element.Gravity, null, 1, TeamId.Players, 0);
            Assert.That(model.MoveSpeedMultiplier, Is.EqualTo(ElementAuraModel.IceMoveMultiplier * ElementAuraModel.GravityMoveMultiplier).Within(1e-5f));
            model.Apply(Element.Fire, null, 1, TeamId.Players, 0);
            model.Consume(Element.Ice);
            Assert.That(model.MoveSpeedMultiplier, Is.EqualTo(ElementAuraModel.GravityMoveMultiplier).Within(1e-5f));
        }

        [Test]
        public void NoneElement_IsIgnored()
        {
            var model = new ElementAuraModel();
            model.Apply(Element.None, null, 1, TeamId.Players, 0);
            Assert.That(model.ActiveCount, Is.EqualTo(0));
        }
    }
}
