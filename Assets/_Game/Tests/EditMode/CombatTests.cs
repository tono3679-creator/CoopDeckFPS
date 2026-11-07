using CoopDeck.Combat;
using CoopDeck.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoopDeck.Tests
{
    public class CombatTests
    {
        [Test]
        public void HealthModel_ClampsAtZero_AndReportsDealtDamage()
        {
            var model = new HealthModel(100f);
            Assert.That(model.TakeDamage(30f), Is.EqualTo(30f));
            Assert.That(model.TakeDamage(500f), Is.EqualTo(70f));
            Assert.That(model.Current, Is.EqualTo(0f));
            Assert.That(model.IsAlive, Is.False);
            Assert.That(model.TakeDamage(10f), Is.EqualTo(0f), "dead targets take no more damage");
        }

        [Test]
        public void HealthModel_HealNeverExceedsMax_AndNeverRevives()
        {
            var model = new HealthModel(100f);
            model.TakeDamage(40f);
            Assert.That(model.Heal(100f), Is.EqualTo(40f));
            Assert.That(model.Current, Is.EqualTo(100f));

            model.TakeDamage(100f);
            Assert.That(model.Heal(50f), Is.EqualTo(0f));
            Assert.That(model.IsAlive, Is.False);
        }

        [Test]
        public void DamageRules_TeamsAndNeutral()
        {
            Assert.That(DamageRules.CanDamage(TeamId.Players, TeamId.Enemies), Is.True);
            Assert.That(DamageRules.CanDamage(TeamId.Players, TeamId.Players), Is.False, "no friendly fire");
            Assert.That(DamageRules.CanDamage(TeamId.Enemies, TeamId.Neutral), Is.True);
            Assert.That(DamageRules.CanDamage(TeamId.Neutral, TeamId.Players), Is.True, "environment hurts everyone");
        }

        [Test]
        public void DamageRules_HeadshotMultiplierOnlyOnHeads()
        {
            Assert.That(DamageRules.ZoneMultiplier(HitZoneType.Head, 1f, 2f), Is.EqualTo(2f));
            Assert.That(DamageRules.ZoneMultiplier(HitZoneType.Body, 1f, 2f), Is.EqualTo(1f));
            Assert.That(DamageRules.ZoneMultiplier(HitZoneType.Armor, 0.5f, 2f), Is.EqualTo(0.5f));
            Assert.That(DamageRules.ZoneMultiplier(HitZoneType.Head, 0.5f, 2f), Is.EqualTo(1f), "armored head");
            Assert.That(DamageRules.ZoneMultiplier(HitZoneType.Head, 1f, 0f), Is.EqualTo(1f), "0 means no bonus");
        }

        [Test]
        public void DamageRules_DistanceFalloff()
        {
            Assert.That(DamageRules.DistanceFalloff(10f, 40f, 150f, 0.6f), Is.EqualTo(1f));
            Assert.That(DamageRules.DistanceFalloff(150f, 40f, 150f, 0.6f), Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(DamageRules.DistanceFalloff(95f, 40f, 150f, 0.6f), Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void HitZone_ForwardsToHealth_WithHeadshotBonus_AndRaisesEvents()
        {
            var go = new GameObject("Target");
            try
            {
                var health = go.AddComponent<Health>();
                health.Team = TeamId.Enemies;
                var headGo = new GameObject("Head");
                headGo.transform.SetParent(go.transform);
                headGo.AddComponent<SphereCollider>();
                var head = headGo.AddComponent<HitZone>();
                head.Configure(health, HitZoneType.Head, 1f);

                DamageResult? damagedResult = null;
                bool died = false;
                health.Damaged += (info, result) => damagedResult = result;
                health.Died += (h, info) => died = true;

                var hit = new DamageInfo { Amount = 20f, SourceTeam = TeamId.Players, HeadshotMultiplier = 2f };
                DamageResult first = head.ApplyDamage(hit);

                Assert.That(first.Applied, Is.True);
                Assert.That(first.Damage, Is.EqualTo(40f));
                Assert.That(first.IsHeadshot, Is.True);
                Assert.That(health.Current, Is.EqualTo(60f));
                Assert.That(damagedResult.HasValue, Is.True);

                DamageResult second = head.ApplyDamage(hit);
                Assert.That(second.Killed, Is.False);
                Assert.That(second.Damage, Is.EqualTo(40f));
                Assert.That(health.Current, Is.EqualTo(20f));
                Assert.That(died, Is.False, "not dead yet at 20 HP");

                DamageResult third = head.ApplyDamage(hit);
                Assert.That(third.Damage, Is.EqualTo(20f), "only the remaining HP is dealt");
                Assert.That(third.Killed, Is.True);
                Assert.That(died, Is.True);
                Assert.That(health.IsAlive, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Health_BlocksFriendlyFire()
        {
            var go = new GameObject("Ally");
            try
            {
                var health = go.AddComponent<Health>();
                health.Team = TeamId.Players;
                DamageResult result = health.ApplyDamage(new DamageInfo { Amount = 50f, SourceTeam = TeamId.Players });
                Assert.That(result.Applied, Is.False);
                Assert.That(health.Current, Is.EqualTo(health.Max));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
