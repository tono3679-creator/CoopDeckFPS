using System.Collections.Generic;
using CoopDeck.Animation;
using CoopDeck.Cards;
using CoopDeck.Skills;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>Plain rules behind the fox animation: stride matching, hit weight, cast motion per card, springs.</summary>
    public class FoxAnimationLogicTests
    {
        static readonly LocomotionRing[] Rings = { new LocomotionRing(1.5f, 0.56f), new LocomotionRing(6.5f, 0.46f), new LocomotionRing(9.5f, 0.42f) };

        [Test]
        public void RingRate_IsOneOnTheRings_AndInsideTheWalkRing()
        {
            Assert.That(LocomotionMath.RingPlaybackRate(0f, Rings), Is.EqualTo(1f));
            Assert.That(LocomotionMath.RingPlaybackRate(0.8f, Rings), Is.EqualTo(1f), "the still centre clip shrinks the stride instead");
            Assert.That(LocomotionMath.RingPlaybackRate(1.5f, Rings), Is.EqualTo(1f));
            Assert.That(LocomotionMath.RingPlaybackRate(6.5f, Rings), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(LocomotionMath.RingPlaybackRate(9.5f, Rings), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void RingRate_BetweenRings_MakesTheBlendedStrideMatchTheSpeed()
        {
            const float speed = 4f;
            float w = (speed - 1.5f) / 5f;
            float stride = Mathf.Lerp(1.5f * 0.56f, 6.5f * 0.46f, w);
            float cycle = Mathf.Lerp(0.56f, 0.46f, w);
            float rate = LocomotionMath.RingPlaybackRate(speed, Rings);
            Assert.That(stride / cycle * rate, Is.EqualTo(speed).Within(1e-3f), "planted foot moves at the ground speed");
            Assert.That(LocomotionMath.RingPlaybackRate(20f, Rings), Is.EqualTo(1.6f), "fast pushes (dash, knockback) clamp");
        }

        [Test]
        public void HeavyHit_IsAFifthOfMaxHealth()
        {
            Assert.That(LocomotionMath.IsHeavyHit(10f, 100f), Is.False);
            Assert.That(LocomotionMath.IsHeavyHit(20f, 100f), Is.True);
            Assert.That(LocomotionMath.IsHeavyHit(5f, 0f), Is.False);
        }

        static CardDefinition Card(CardTargeting targeting, CardRarity rarity = CardRarity.Common, float delay = 0f, params SkillEffectSpec[] effects)
        {
            var card = ScriptableObject.CreateInstance<CardDefinition>();
            card.targeting = targeting;
            card.rarity = rarity;
            card.impactDelay = delay;
            card.effects = new List<SkillEffectSpec>(effects);
            return card;
        }

        [Test]
        public void CastMotion_FollowsHowTheCardIsCast()
        {
            Assert.That(SkillMotionRules.For(Card(CardTargeting.Aim, effects: SkillEffectSpec.Damage(30f))), Is.EqualTo(SkillMotion.QuickCast));
            Assert.That(SkillMotionRules.For(Card(CardTargeting.Aim, delay: 0.6f, effects: SkillEffectSpec.Damage(60f))), Is.EqualTo(SkillMotion.Charged));
            Assert.That(SkillMotionRules.For(Card(CardTargeting.AimPoint, effects: SkillEffectSpec.Area(20f, 4f))), Is.EqualTo(SkillMotion.AreaCast));
            Assert.That(SkillMotionRules.For(Card(CardTargeting.AreaAroundSelf, effects: SkillEffectSpec.Area(20f, 5f))), Is.EqualTo(SkillMotion.AreaCast));
            Assert.That(SkillMotionRules.For(Card(CardTargeting.AreaAroundSelf, effects: SkillEffectSpec.Heal(40f, 8f))), Is.EqualTo(SkillMotion.SelfBuff));
            Assert.That(SkillMotionRules.For(Card(CardTargeting.Self, effects: SkillEffectSpec.Shield(40f, 5f))), Is.EqualTo(SkillMotion.SelfBuff));
            Assert.That(SkillMotionRules.For(Card(CardTargeting.Aim, CardRarity.Legendary, effects: SkillEffectSpec.Damage(200f))), Is.EqualTo(SkillMotion.Ultimate));
            Assert.That(SkillMotionRules.For(null), Is.EqualTo(SkillMotion.QuickCast));
        }

        [Test]
        public void AngleSpring_SettlesOnTheTarget_EvenAtLowFrameRates()
        {
            foreach (float dt in new[] { 1f / 120f, 1f / 30f, 0.1f })
            {
                var spring = new AngleSpring();
                float peak = 0f;
                for (float t = 0f; t < 3f; t += dt)
                {
                    spring.Step(20f, dt, 2.4f, 0.55f);
                    peak = Mathf.Max(peak, spring.Value);
                }

                Assert.That(spring.Value, Is.EqualTo(20f).Within(0.5f), $"settles (dt {dt})");
                Assert.That(peak, Is.InRange(20f, 28f), $"some overshoot, no blow-up (dt {dt})");
            }
        }

        [Test]
        public void TailAndEars_ReactToSpeedHitsCastsAndDowned()
        {
            Assert.That(SecondaryMotionMath.TailPitch(6.5f, 0f, true, false, 0f), Is.GreaterThan(SecondaryMotionMath.TailPitch(0f, 0f, true, false, 0f)), "running lifts the tail");
            Assert.That(SecondaryMotionMath.TailPitch(0f, -8f, false, false, 0f), Is.GreaterThan(10f), "falling lifts the tail");
            Assert.That(SecondaryMotionMath.TailPitch(3f, 0f, true, true, 0f), Is.LessThan(-20f), "downed: the tail lies down");
            Assert.That(SecondaryMotionMath.TailPitch(0f, 0f, true, false, 1f), Is.GreaterThan(40f), "ultimate fans it up");
            Assert.That(SecondaryMotionMath.TailSwayFrequency(6.5f), Is.GreaterThan(SecondaryMotionMath.TailSwayFrequency(0f)));
            Assert.That(SecondaryMotionMath.EarPitch(6.5f, false, 0f, 0f), Is.LessThan(-10f), "ears tilt back when running");
            Assert.That(SecondaryMotionMath.EarPitch(0f, false, 1f, 0f), Is.LessThan(-40f), "a hit flattens them");
            Assert.That(SecondaryMotionMath.EarPitch(0f, false, 0f, 1f), Is.GreaterThan(5f), "casting perks them up");
        }

        [Test]
        public void Driver_SkipsParametersAController_DoesNotHave()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Game/Animations/Placeholder/AC_PlaceholderHumanoid.controller");
            Assert.That(controller, Is.Not.Null);
            var go = new GameObject("driver");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                var driver = go.AddComponent<CharacterAnimatorDriver>();
                driver.Configure(animator, null, 4f, 4f);
                driver.Fire();
                driver.Reload(1.7f);
                driver.Skill(SkillMotion.Ultimate);
                driver.Dash(Vector3.right);
                driver.Jump();
                driver.Hit(true);
                driver.Interact(InteractionMotion.Emote);
                driver.SetReviving(true);
                driver.SetSprinting(true);
                driver.SetDowned(true);
                Assert.That(driver.IsDowned, Is.True);
                driver.Revive();
                driver.Respawned();
                LogAssert.NoUnexpectedReceived(); // no "Parameter does not exist" warnings on the placeholder
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
