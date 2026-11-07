using CoopDeck.Animation;
using CoopDeck.Combat;
using CoopDeck.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoopDeck.Tests
{
    public class LocomotionAndCombatTests
    {
        [Test]
        public void LocalBlend_IsRelativeToFacing()
        {
            Vector2 forward = LocomotionMath.LocalBlend(new Vector3(0f, 0f, 4f), Quaternion.identity, 4f);
            Assert.That(forward.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(forward.y, Is.EqualTo(1f).Within(1e-4f));

            Quaternion facingRight = Quaternion.Euler(0f, 90f, 0f);
            Vector2 alongFacing = LocomotionMath.LocalBlend(new Vector3(4f, 0f, 0f), facingRight, 4f);
            Assert.That(alongFacing.y, Is.EqualTo(1f).Within(1e-4f), "moving where it faces = forward walk");

            Vector2 strafe = LocomotionMath.LocalBlend(new Vector3(0f, 0f, 4f), facingRight, 4f);
            Assert.That(strafe.x, Is.EqualTo(-1f).Within(1e-4f), "world +Z while facing +X = strafe left");
        }

        [Test]
        public void LocalBlend_ClampsToTheBlendRadius_AndIgnoresVerticalSpeed()
        {
            Vector2 fast = LocomotionMath.LocalBlend(new Vector3(0f, -9f, 12f), Quaternion.identity, 4f);
            Assert.That(fast.magnitude, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void PlaybackRate_FollowsGroundSpeed_WithinLimits()
        {
            Assert.That(LocomotionMath.PlaybackRate(0f, 4f), Is.EqualTo(1f));
            Assert.That(LocomotionMath.PlaybackRate(4f, 4f), Is.EqualTo(1f));
            Assert.That(LocomotionMath.PlaybackRate(6f, 4f), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(LocomotionMath.PlaybackRate(20f, 4f), Is.EqualTo(1.8f));
            Assert.That(LocomotionMath.PlaybackRate(1f, 4f), Is.EqualTo(0.75f));
        }

        [Test]
        public void Weakpoint_HitsAreCritical_AndUseTheZoneMultiplier()
        {
            var go = new GameObject("Shooter");
            try
            {
                var health = go.AddComponent<Health>();
                health.Team = TeamId.Enemies;
                var core = new GameObject("Core");
                core.transform.SetParent(go.transform);
                core.AddComponent<SphereCollider>();
                var zone = core.AddComponent<HitZone>();
                zone.Configure(health, HitZoneType.Weakpoint, 1.75f);

                DamageResult result = zone.ApplyDamage(new DamageInfo { Amount = 20f, SourceTeam = TeamId.Players, HeadshotMultiplier = 2f });
                Assert.That(result.Damage, Is.EqualTo(35f).Within(1e-4f), "weak point ×1.75, no headshot bonus");
                Assert.That((result.Flags & DamageFlags.Critical) != 0, Is.True);
                Assert.That(result.IsHeadshot, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Registry_FindsTheNearestHostile_AndSkipsAlliesAndTheDead()
        {
            var near = new RegistryProbe(TeamId.Players, new Vector3(3f, 0f, 0f));
            var far = new RegistryProbe(TeamId.Players, new Vector3(10f, 0f, 0f));
            var ally = new RegistryProbe(TeamId.Enemies, new Vector3(1f, 0f, 0f));
            try
            {
                CombatantRegistry.Register(near);
                CombatantRegistry.Register(far);
                CombatantRegistry.Register(ally);

                Assert.That(CombatantRegistry.FindNearestHostile(TeamId.Enemies, Vector3.zero, 50f), Is.SameAs(near));
                near.Alive = false;
                Assert.That(CombatantRegistry.FindNearestHostile(TeamId.Enemies, Vector3.zero, 50f), Is.SameAs(far));
                Assert.That(CombatantRegistry.FindNearestHostile(TeamId.Enemies, Vector3.zero, 5f), Is.Null, "out of range");
            }
            finally
            {
                CombatantRegistry.Unregister(near);
                CombatantRegistry.Unregister(far);
                CombatantRegistry.Unregister(ally);
            }
        }
    }

    /// <summary>Minimal ICombatActor for registry tests (plain C#, no GameObject).</summary>
    public sealed class RegistryProbe : ICombatActor
    {
        public RegistryProbe(TeamId team, Vector3 position)
        {
            Team = team;
            Position = position;
        }

        public bool Alive = true;
        public string DisplayName => "Probe";
        public TeamId Team { get; }
        public Health Health => null;
        public CombatStatus Status => null;
        public Vector3 Position { get; }
        public bool IsAlive => Alive;
    }
}
