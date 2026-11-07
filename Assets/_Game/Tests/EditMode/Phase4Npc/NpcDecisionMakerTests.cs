using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.NPC;
using NUnit.Framework;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The NPC's utility layer on hand-made situations: combo follow-ups (and every reason not to
    /// take one), the priority bands, holding cards for an ally without waiting forever, area vs
    /// single target, tactics, pacing, and that it keeps shooting when no skill fits.
    /// </summary>
    public class NpcDecisionMakerTests
    {
        const int SelfId = 2;
        const int PlayerId = 1;
        const int Superconductor = 0;
        const int Singularity = 1;
        const int Overload = 2;

        static readonly List<ComboRule> Rules = new List<ComboRule>
        {
            new ComboRule(Superconductor, Element.Ice, Element.Lightning, 4f, 3f, 1.3f),
            new ComboRule(Singularity, Element.Fire, Element.Gravity, 4f, 3f, 1.3f),
            new ComboRule(Overload, Element.Fire, Element.Lightning, 4f, 3f, 1.3f),
        };

        NpcProfile profile;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            profile = Track(NpcProfile.CreateRuntimeDefault());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            created.Clear();
        }

        T Track<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        // ---------------------------------------------------------------- builders

        CardDefinition Card(Element element, CardTargeting targeting, float range, params SkillEffectSpec[] effects)
        {
            var card = Track(ScriptableObject.CreateInstance<CardDefinition>());
            card.element = element;
            card.targeting = targeting;
            card.range = range;
            card.gaugeCost = 25f;
            card.maxGauge = 100f;
            card.effects = new List<SkillEffectSpec>(effects);
            return card;
        }

        CardDefinition Bolt() => Card(Element.Lightning, CardTargeting.Aim, 50f, SkillEffectSpec.Damage(40f));
        CardDefinition Fireball() => Card(Element.Fire, CardTargeting.AimPoint, 35f, SkillEffectSpec.Area(30f, 4f));
        CardDefinition Heal() => Card(Element.None, CardTargeting.AreaAroundSelf, 1f, SkillEffectSpec.Heal(45f, 8f));

        static NpcHandCard Hand(int slot, CardDefinition card, float cooldown = 0f, float gauge = 100f, float heldFor = 0f, bool holdBlocked = false) =>
            new NpcHandCard
            {
                Present = true,
                Slot = slot,
                InstanceId = slot + 1,
                Name = "card" + slot,
                Traits = NpcCardTraits.From(card),
                Gauge = gauge,
                GaugeCost = card.gaugeCost,
                Cooldown = cooldown,
                HeldFor = heldFor,
                HoldBlocked = holdBlocked,
            };

        static NpcSituation Situation(params NpcHandCard[] hand)
        {
            var s = new NpcSituation
            {
                SelfId = SelfId,
                Team = TeamId.Players,
                Position = Vector3.zero,
                Health = 130f,
                Health01 = 1f,
                Rules = Rules,
                HasLeader = true,
                LeaderId = PlayerId,
                LeaderPosition = new Vector3(-3f, 0f, 0f),
                LeaderAimDirection = Vector3.forward,
                LeaderDistance = 3f,
            };
            for (int i = 0; i < hand.Length; i++)
            {
                s.Hand[i] = hand[i];
            }

            return s;
        }

        static int Enemy(NpcSituation s, Vector3 position, bool los = true, ElementAuraModel auras = null, bool melee = false, float health01 = 1f)
        {
            return s.AddEnemy(new NpcEnemyInfo
            {
                Id = 100 + s.EnemyCount,
                Position = position,
                Distance = Vector3.Distance(position, s.Position),
                LineOfSight = los,
                Health01 = health01,
                Engaged = true,
                IsMelee = melee,
                Auras = auras ?? new ElementAuraModel(),
            });
        }

        static ElementAuraModel Primed(Element element, int applierId, float age = 0.5f)
        {
            var auras = new ElementAuraModel();
            auras.Apply(element, null, applierId, TeamId.Players, 0);
            auras.Tick(age);
            return auras;
        }

        NpcDecision Decide(NpcSituation s, List<NpcFollowUp> followUps = null) => NpcDecisionMaker.Decide(s, profile, followUps);

        // ---------------------------------------------------------------- combo follow-ups

        [Test]
        public void PlayerFrozeTheTarget_LightningInHand_FinishesTheCombo_AsCoop()
        {
            NpcSituation s = Situation(Hand(0, Heal()), Hand(1, Bolt()));
            int target = Enemy(s, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId));
            var followUps = new List<NpcFollowUp>();

            NpcDecision d = Decide(s, followUps);

            Assert.That(d.Action, Is.EqualTo(NpcActionKind.ComboFinish));
            Assert.That(d.Slot, Is.EqualTo(1), "the lightning card");
            Assert.That(d.Target, Is.EqualTo(target));
            Assert.That(d.RuleIndex, Is.EqualTo(Superconductor));
            Assert.That(d.Coop, Is.True, "the player applied the ice");
            Assert.That(followUps, Has.Count.EqualTo(1), "the HUD can mark the target");
        }

        [Test]
        public void NoFollowUp_WhenTheFinisherIsOnCooldown_OutOfRange_OrHidden()
        {
            NpcSituation cooldown = Situation(Hand(0, Bolt(), cooldown: 0.5f));
            Enemy(cooldown, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId));
            Assert.That(Decide(cooldown).Action, Is.Not.EqualTo(NpcActionKind.ComboFinish), "cooldown");

            NpcSituation far = Situation(Hand(0, Bolt()));
            Enemy(far, new Vector3(0f, 0f, 70f), auras: Primed(Element.Ice, PlayerId));
            Assert.That(Decide(far).Action, Is.Not.EqualTo(NpcActionKind.ComboFinish), "range 50 m");

            NpcSituation hidden = Situation(Hand(0, Bolt()));
            Enemy(hidden, new Vector3(0f, 0f, 12f), los: false, auras: Primed(Element.Ice, PlayerId));
            Assert.That(Decide(hidden).Action, Is.Not.EqualTo(NpcActionKind.ComboFinish), "no line of fire");
        }

        [Test]
        public void NoFollowUp_WithoutAMatchingCard_ButItStillShoots()
        {
            NpcSituation s = Situation(Hand(0, Heal()), Hand(1, Fireball()));
            int target = Enemy(s, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId));

            NpcDecision d = Decide(s);

            Assert.That(d.Action, Is.Not.EqualTo(NpcActionKind.ComboFinish), "fire does not pair with ice");
            Assert.That(d.ShootTarget, Is.EqualTo(target), "the gun keeps working when no skill fits");
        }

        [Test]
        public void NoFollowUp_WhenTheWindowIsAlmostOver_OrTheComboIsOnCooldownOnThatTarget()
        {
            NpcSituation late = Situation(Hand(0, Bolt()));
            Enemy(late, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId, age: 3.9f));
            Assert.That(Decide(late).Action, Is.Not.EqualTo(NpcActionKind.ComboFinish), "0.1 s left is not enough to aim and land");

            ElementAuraModel recent = Primed(Element.Ice, PlayerId);
            recent.StartCooldown(Superconductor, 3f);
            NpcSituation cooldown = Situation(Hand(0, Bolt()));
            Enemy(cooldown, new Vector3(0f, 0f, 12f), auras: recent);
            Assert.That(Decide(cooldown).Action, Is.Not.EqualTo(NpcActionKind.ComboFinish), "3 s re-trigger limit");
        }

        [Test]
        public void OwnElement_StillCombos_ButScoresBelowAnAllysElement()
        {
            NpcSituation s = Situation(Hand(0, Bolt()));
            int own = Enemy(s, new Vector3(0f, 0f, 10f), auras: Primed(Element.Ice, SelfId));
            int ally = Enemy(s, new Vector3(0f, 0f, 14f), auras: Primed(Element.Ice, PlayerId));

            NpcDecision d = Decide(s);

            Assert.That(d.Action, Is.EqualTo(NpcActionKind.ComboFinish));
            Assert.That(d.Target, Is.EqualTo(ally), "co-op first");
            Assert.That(own, Is.Not.EqualTo(ally));
        }

        [Test]
        public void ComboSetup_AppliesFire_ForTheLeadersLightning_OnTheLeadersTarget()
        {
            NpcSituation s = Situation(Hand(0, Fireball()));
            s.FavoredElement = Element.Lightning; // Volt: fire is not its finisher, so it starts combos with it
            s.LeaderElementMask = 1 << (int)Element.Lightning;
            Enemy(s, new Vector3(4f, 0f, 9f));
            int leaderTarget = Enemy(s, new Vector3(-2f, 0f, 14f));
            s.LeaderTarget = leaderTarget;

            NpcDecision d = Decide(s);

            Assert.That(d.Action, Is.EqualTo(NpcActionKind.ComboSetup));
            Assert.That(d.Target, Is.EqualTo(leaderTarget));
            Assert.That(d.RuleIndex, Is.EqualTo(Overload));
            Assert.That(d.Coop, Is.True);

            s.SetupAllowed = false;
            Assert.That(Decide(s).Action, Is.Not.EqualTo(NpcActionKind.ComboSetup), "no re-applying right after a setup");
        }

        // ---------------------------------------------------------------- holding without waiting forever

        [Test]
        public void Finisher_IsSavedForTheLeadersElement_ThenReleased()
        {
            NpcSituation s = Situation(Hand(0, Bolt()));
            s.LeaderElementMask = 1 << (int)Element.Ice;
            Enemy(s, new Vector3(0f, 0f, 12f));

            NpcDecision held = Decide(s);
            Assert.That(held.ReservedMask & 1, Is.EqualTo(1), "saved for the player's ice");
            Assert.That(held.Action, Is.Not.EqualTo(NpcActionKind.SingleSkill));
            Assert.That(held.Action, Is.EqualTo(NpcActionKind.Shoot), "it fights with the gun meanwhile");

            s.Hand[0].HeldFor = profile.HoldFor(NpcTactic.Auto) + 0.01f;
            NpcDecision released = Decide(s);
            Assert.That(released.ReservedMask, Is.Zero, "the hold has a time limit");
            Assert.That(released.Slot, Is.EqualTo(0), "the card gets used once the hold is over");
            Assert.That(released.Action, Is.EqualTo(NpcActionKind.SingleSkill).Or.EqualTo(NpcActionKind.ComboSetup));

            s.Hand[0].HeldFor = 0f;
            s.Hand[0].HoldBlocked = true;
            Assert.That(Decide(s).ReservedMask, Is.Zero, "a card whose hold ran out is used normally for a while");
        }

        [Test]
        public void LightningNpc_SavesLightningToFinish_ButSpendsFireToStartCombos()
        {
            NpcSituation s = Situation(Hand(0, Bolt()), Hand(1, Fireball()));
            s.FavoredElement = Element.Lightning;
            s.LeaderElementMask = (1 << (int)Element.Ice) | (1 << (int)Element.Lightning);
            int target = Enemy(s, new Vector3(0f, 0f, 12f));
            s.LeaderTarget = target;

            NpcDecision d = Decide(s);

            Assert.That(d.ReservedMask, Is.EqualTo(1), "only the lightning finisher waits for the player's ice");
            Assert.That(d.Action, Is.EqualTo(NpcActionKind.ComboSetup), "fire goes on the player's target for their lightning");
            Assert.That(d.Slot, Is.EqualTo(1));
            Assert.That(d.RuleIndex, Is.EqualTo(Overload));
        }

        [Test]
        public void ComboFirst_HoldsLonger_FocusDoesNotHold()
        {
            Assert.That(profile.HoldFor(NpcTactic.ComboFirst), Is.GreaterThan(profile.HoldFor(NpcTactic.Auto)));

            NpcSituation s = Situation(Hand(0, Bolt(), heldFor: profile.HoldFor(NpcTactic.Auto) + 0.5f));
            s.LeaderElementMask = 1 << (int)Element.Ice;
            Enemy(s, new Vector3(0f, 0f, 12f));
            s.Tactic = NpcTactic.ComboFirst;
            Assert.That(Decide(s).ReservedMask, Is.EqualTo(1), "still within the longer hold");

            s.Tactic = NpcTactic.Focus;
            Assert.That(Decide(s).ReservedMask, Is.Zero);
        }

        // ---------------------------------------------------------------- priorities

        [Test]
        public void DangerousAttack_IsDodged_BeforeAnything()
        {
            NpcSituation s = Situation(Hand(0, Bolt()));
            Enemy(s, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId));
            s.Health = 20f;
            s.Health01 = 0.15f;
            s.AddThreat(new NpcThreat { Kind = ThreatKind.Melee, Origin = new Vector3(1.5f, 0f, 0f), Direction = Vector3.left, TimeLeft = 0.4f, Damage = 14f, Reach = 2.8f, Roll = 0.99f });

            NpcDecision d = Decide(s);

            Assert.That(d.Action, Is.EqualTo(NpcActionKind.Evade));
            Assert.That(d.EvadeDash, Is.True);
            Assert.That(Vector3.Dot(d.EvadeDirection, Vector3.right), Is.LessThan(0f), "away from (or beside) the attacker at +X, never into it");
        }

        [Test]
        public void HarmlessAttack_DodgeDependsOnTheRoll_AndOutOfReachIsIgnored()
        {
            NpcSituation s = Situation();
            s.AddThreat(new NpcThreat { Kind = ThreatKind.Projectile, Origin = new Vector3(0f, 0f, 15f), Direction = Vector3.back, TimeLeft = 0.5f, Damage = 9f, Roll = 0.1f });
            Assert.That(NpcDecisionMaker.EvaluateEvade(s, profile).Action, Is.EqualTo(NpcActionKind.Evade));

            s.Threats[0].Roll = 0.95f; // above dodgeChance 0.75
            Assert.That(NpcDecisionMaker.EvaluateEvade(s, profile).Action, Is.EqualTo(NpcActionKind.None));

            NpcSituation far = Situation();
            far.AddThreat(new NpcThreat { Kind = ThreatKind.Melee, Origin = new Vector3(0f, 0f, 8f), TimeLeft = 0.4f, Damage = 200f, Reach = 2.8f, Roll = 0f });
            Assert.That(NpcDecisionMaker.EvaluateEvade(far, profile).Action, Is.EqualTo(NpcActionKind.None), "a swing 8 m away cannot reach");
        }

        [Test]
        public void DownedLeader_IsRevived_BeforeCombos()
        {
            NpcSituation s = Situation(Hand(0, Bolt()));
            Enemy(s, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId));
            s.LeaderDowned = true;
            s.LeaderAlive = false;

            Assert.That(Decide(s).Action, Is.EqualTo(NpcActionKind.Revive));
        }

        [Test]
        public void DyingLeader_InRange_IsHealed_BeforeCombos()
        {
            NpcSituation s = Situation(Hand(0, Bolt()), Hand(1, Heal()));
            Enemy(s, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId));
            s.LeaderHealth01 = 0.2f;

            NpcDecision d = Decide(s);

            Assert.That(d.Action, Is.EqualTo(NpcActionKind.Support));
            Assert.That(d.Slot, Is.EqualTo(1));
        }

        [Test]
        public void AreaSkill_WhenSeveralEnemiesStandTogether_SingleTargetOtherwise()
        {
            NpcSituation group = Situation(Hand(0, Fireball()));
            Enemy(group, new Vector3(0f, 0f, 12f));
            Enemy(group, new Vector3(1.5f, 0f, 12.5f));
            Enemy(group, new Vector3(-1f, 0f, 13f));
            Assert.That(Decide(group).Action, Is.EqualTo(NpcActionKind.AreaSkill));

            NpcSituation alone = Situation(Hand(0, Fireball()));
            Enemy(alone, new Vector3(0f, 0f, 12f));
            NpcDecision d = Decide(alone);
            Assert.That(d.Action, Is.Not.EqualTo(NpcActionKind.AreaSkill));
        }

        [Test]
        public void Pacing_OrdinarySkillsWait_ButAComboFinishDoesNot()
        {
            NpcSituation s = Situation(Hand(0, Bolt()));
            Enemy(s, new Vector3(0f, 0f, 12f));
            s.SinceLastCast = 0.4f;
            Assert.That(Decide(s).Action, Is.EqualTo(NpcActionKind.Shoot), "not right after another skill");

            NpcSituation combo = Situation(Hand(0, Bolt()));
            Enemy(combo, new Vector3(0f, 0f, 12f), auras: Primed(Element.Ice, PlayerId));
            combo.SinceLastCast = 0.4f;
            Assert.That(Decide(combo).Action, Is.EqualTo(NpcActionKind.ComboFinish), "a combo window does not wait");
        }

        [Test]
        public void Focus_ShootsTheMarkedEnemy_EvenIfFarther()
        {
            NpcSituation s = Situation();
            Enemy(s, new Vector3(0f, 0f, 6f));
            int marked = Enemy(s, new Vector3(0f, 0f, 35f));
            s.FocusTarget = marked;
            s.Tactic = NpcTactic.Focus;

            Assert.That(Decide(s).ShootTarget, Is.EqualTo(marked));

            s.Tactic = NpcTactic.Auto;
            Assert.That(Decide(s).ShootTarget, Is.Not.EqualTo(marked), "auto prefers the closer threat");
        }

        [Test]
        public void TrainingDummies_AreIgnored_UntilTheTeamEngagesThem()
        {
            NpcSituation s = Situation(Hand(0, Bolt()));
            int dummy = Enemy(s, new Vector3(0f, 0f, 10f));
            s.Enemies[dummy].IsTraining = true;
            s.Enemies[dummy].Engaged = false;
            NpcDecision d = Decide(s);
            Assert.That(d.ShootTarget, Is.EqualTo(-1));
            Assert.That(d.Action, Is.EqualTo(NpcActionKind.Follow));

            s.Enemies[dummy].Engaged = true;
            Assert.That(Decide(s).ShootTarget, Is.EqualTo(dummy));
        }

        [Test]
        public void SameInput_SameDecision()
        {
            NpcSituation s = Situation(Hand(0, Bolt()), Hand(1, Fireball()));
            Enemy(s, new Vector3(0f, 0f, 12f), auras: Primed(Element.Fire, PlayerId));
            Enemy(s, new Vector3(3f, 0f, 9f));
            NpcDecision a = Decide(s);
            NpcDecision b = Decide(s);
            Assert.That(b.Action, Is.EqualTo(a.Action));
            Assert.That(b.Target, Is.EqualTo(a.Target));
            Assert.That(b.Slot, Is.EqualTo(a.Slot));
            Assert.That(a.Action, Is.EqualTo(NpcActionKind.ComboFinish), "fire + lightning = overload");
            Assert.That(a.RuleIndex, Is.EqualTo(Overload));
        }
    }
}
