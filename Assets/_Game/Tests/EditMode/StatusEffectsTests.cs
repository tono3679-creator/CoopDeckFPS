using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoopDeck.Tests
{
    public class StatusEffectsTests
    {
        [Test]
        public void Shield_AbsorbsFirst_ThenBreaks_AndExpires()
        {
            var model = new StatusEffectsModel();
            model.AddShield(60f, 8f);

            Assert.That(model.AbsorbDamage(25f, out float absorbed), Is.EqualTo(0f));
            Assert.That(absorbed, Is.EqualTo(25f));
            Assert.That(model.AbsorbDamage(50f, out absorbed), Is.EqualTo(15f), "35 left in the shield");
            Assert.That(absorbed, Is.EqualTo(35f));
            Assert.That(model.Shield, Is.EqualTo(0f));

            model.AddShield(30f, 1f);
            model.Tick(1.1f, null);
            Assert.That(model.Shield, Is.EqualTo(0f), "expired");
        }

        [Test]
        public void DamageBuff_And_Vulnerability_ScaleDamage_KeepStrongest_AndExpire()
        {
            var model = new StatusEffectsModel();
            model.AddDamageBuff(0.3f, 8f);
            model.AddDamageBuff(0.1f, 2f);
            Assert.That(model.OutgoingDamageMultiplier, Is.EqualTo(1.3f).Within(1e-5f));

            model.AddVulnerability(0.3f, 4f);
            Assert.That(model.IncomingDamageMultiplier, Is.EqualTo(1.3f).Within(1e-5f));

            model.Tick(5f, null);
            Assert.That(model.IncomingDamageMultiplier, Is.EqualTo(1f));
            Assert.That(model.OutgoingDamageMultiplier, Is.EqualTo(1.3f).Within(1e-5f));
            model.Tick(4f, null);
            Assert.That(model.OutgoingDamageMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void DamageOverTime_DealsDpsTimesDuration_InHalfSecondTicks()
        {
            var model = new StatusEffectsModel();
            model.AddDamageOverTime(null, TeamId.Players, Element.Fire, 12f, 4f);
            var ticks = new List<DotTick>();
            for (int i = 0; i < 300; i++)
            {
                model.Tick(1f / 60f, ticks);
            }

            Assert.That(ticks.Count, Is.EqualTo(8));
            float total = 0f;
            foreach (DotTick tick in ticks)
            {
                total += tick.Amount;
                Assert.That(tick.Element, Is.EqualTo(Element.Fire));
            }

            Assert.That(total, Is.EqualTo(48f).Within(1e-3f));
            Assert.That(model.DamageOverTimeCount, Is.EqualTo(0));
            Assert.That(model.LastElement, Is.EqualTo(Element.Fire), "element recorded for the Phase 4 combo system");
        }

        [Test]
        public void DamageOverTime_StacksAreCapped()
        {
            var model = new StatusEffectsModel();
            for (int i = 0; i < 20; i++)
            {
                model.AddDamageOverTime(null, TeamId.Players, Element.Fire, 5f, 3f);
            }

            Assert.That(model.DamageOverTimeCount, Is.EqualTo(StatusEffectsModel.MaxDamageOverTimeStacks));
        }

        [Test]
        public void Health_UsesShieldAndVulnerabilityFromCombatStatus()
        {
            var go = new GameObject("Target");
            try
            {
                var health = go.AddComponent<Health>();
                health.Team = TeamId.Enemies;
                var status = go.AddComponent<CombatStatus>();
                status.AddVulnerability(0.5f, 5f);
                status.AddShield(20f, 5f);

                DamageResult result = health.ApplyDamage(new DamageInfo { Amount = 20f, SourceTeam = TeamId.Players });

                // 20 × 1.5 = 30, shield takes 20, HP takes 10.
                Assert.That(result.Absorbed, Is.EqualTo(20f).Within(1e-4f));
                Assert.That(result.Damage, Is.EqualTo(10f).Within(1e-4f));
                Assert.That(result.Applied, Is.True);
                Assert.That(health.Current, Is.EqualTo(90f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
