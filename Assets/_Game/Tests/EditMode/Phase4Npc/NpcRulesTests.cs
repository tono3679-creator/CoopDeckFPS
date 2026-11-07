using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.NPC;
using NUnit.Framework;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>NPC mode state machine, steering math and card summaries (plain C#).</summary>
    public class NpcRulesTests
    {
        NpcProfile profile;

        [SetUp]
        public void SetUp() => profile = NpcProfile.CreateRuntimeDefault();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(profile);

        static NpcModeInput Input(float leaderDistance = 4f, bool engaged = false, bool alive = true, bool leaderDowned = false, bool evade = false) =>
            new NpcModeInput { Alive = alive, HasLeader = true, LeaderDistance = leaderDistance, HasEngagedEnemy = engaged, LeaderDowned = leaderDowned, WantsEvade = evade };

        [Test]
        public void StateMachine_FollowsEngagesAndLingers()
        {
            var machine = new NpcStateMachine();
            Assert.That(machine.Tick(0.1f, Input(), profile), Is.EqualTo(NpcMode.Follow));
            Assert.That(machine.Tick(0.1f, Input(engaged: true), profile), Is.EqualTo(NpcMode.Engage));
            Assert.That(machine.Tick(1f, Input(), profile), Is.EqualTo(NpcMode.Engage), "lingers a moment after the last enemy");
            machine.Tick(2f, Input(), profile);
            Assert.That(machine.Tick(0.1f, Input(), profile), Is.EqualTo(NpcMode.Follow));
        }

        [Test]
        public void StateMachine_RegroupHasHysteresis()
        {
            var machine = new NpcStateMachine();
            Assert.That(machine.Tick(0.1f, Input(leaderDistance: profile.regroupDistance + 1f, engaged: true), profile), Is.EqualTo(NpcMode.Regroup),
                "too far: catch up, even mid-fight");
            Assert.That(machine.Tick(0.1f, Input(leaderDistance: profile.regroupDistance - 2f, engaged: true), profile), Is.EqualTo(NpcMode.Regroup),
                "keeps catching up until close");
            Assert.That(machine.Tick(0.1f, Input(leaderDistance: profile.regroupDoneDistance - 1f, engaged: true), profile), Is.EqualTo(NpcMode.Engage));
        }

        [Test]
        public void StateMachine_PriorityDownedEvadeRescue()
        {
            var machine = new NpcStateMachine();
            Assert.That(machine.Tick(0.1f, Input(leaderDowned: true, engaged: true), profile), Is.EqualTo(NpcMode.Rescue));
            Assert.That(machine.Tick(0.1f, Input(leaderDowned: true, evade: true), profile), Is.EqualTo(NpcMode.Evade), "dodging comes first");
            Assert.That(machine.Tick(profile.evadeDuration * 0.5f, Input(leaderDowned: true), profile), Is.EqualTo(NpcMode.Evade), "the dodge runs its course");
            Assert.That(machine.Tick(profile.evadeDuration, Input(leaderDowned: true), profile), Is.EqualTo(NpcMode.Rescue));
            Assert.That(machine.Tick(0.1f, Input(alive: false, evade: true), profile), Is.EqualTo(NpcMode.Downed));
            Assert.That(machine.Tick(0.1f, Input(), profile), Is.EqualTo(NpcMode.Follow), "back up after a revive");
        }

        [Test]
        public void Steering_LocalMoveMatchesTheBody()
        {
            Vector2 forward = NpcSteering.LocalMove(Vector3.forward, Vector3.forward, Vector3.right);
            Assert.That(forward.y, Is.EqualTo(1f).Within(1e-4f));
            Vector2 strafe = NpcSteering.LocalMove(Vector3.forward, Vector3.right, Vector3.back); // body faces +X
            Assert.That(strafe.x, Is.EqualTo(-1f).Within(1e-4f), "world +Z is to the body's left");
            Assert.That(NpcSteering.LocalMove(new Vector3(3f, 0f, 4f), Vector3.forward, Vector3.right).magnitude, Is.LessThanOrEqualTo(1.0001f));
        }

        [Test]
        public void Steering_AimDeltaTurnsTowardTheTarget_WithoutOvershoot()
        {
            Vector2 error = NpcSteering.AimError(Vector3.forward, new Vector3(10f, 0f, 10f));
            Assert.That(error.x, Is.EqualTo(45f).Within(0.01f));
            Vector2 delta = NpcSteering.LookDelta(error, 11f, 300f, 1f / 60f);
            Assert.That(delta.x, Is.GreaterThan(0f));
            Assert.That(delta.x, Is.LessThanOrEqualTo(300f / 60f + 1e-4f), "turn speed cap");
            Vector2 tiny = NpcSteering.LookDelta(new Vector2(0.1f, -0.1f), 1000f, 3000f, 1f);
            Assert.That(tiny.x, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(tiny.y, Is.EqualTo(-0.1f).Within(1e-5f));
        }

        [Test]
        public void Steering_FireLineAndBlockedShots()
        {
            Vector3 origin = new Vector3(0f, 1.6f, 0f);
            Assert.That(NpcSteering.InFireLine(new Vector3(0.3f, 1.4f, 8f), origin, Vector3.forward, 40f, 0.9f), Is.True, "standing in front of the gun");
            Assert.That(NpcSteering.InFireLine(new Vector3(3f, 1.4f, 8f), origin, Vector3.forward, 40f, 0.9f), Is.False, "off to the side");
            Assert.That(NpcSteering.InFireLine(new Vector3(0f, 1.4f, -4f), origin, Vector3.forward, 40f, 0.9f), Is.False, "behind");

            Assert.That(NpcSteering.AllyBlocksShot(Vector3.zero, new Vector3(0f, 0f, 20f), new Vector3(0.4f, 0f, 10f), 0.9f), Is.True);
            Assert.That(NpcSteering.AllyBlocksShot(Vector3.zero, new Vector3(0f, 0f, 20f), new Vector3(0f, 0f, 25f), 0.9f), Is.False, "behind the target");
        }

        [Test]
        public void Steering_FollowSlotIsBesideAndBehind_OnTheChosenSide()
        {
            Vector3 right = NpcSteering.FollowSlot(Vector3.zero, Vector3.forward, 130f, 4.5f, 1);
            Vector3 left = NpcSteering.FollowSlot(Vector3.zero, Vector3.forward, 130f, 4.5f, -1);
            Assert.That(right.magnitude, Is.EqualTo(4.5f).Within(0.01f));
            Assert.That(right.z, Is.LessThan(0f), "behind the view direction");
            Assert.That(right.x, Is.GreaterThan(0f));
            Assert.That(left.x, Is.LessThan(0f));
            Assert.That(NpcSteering.InFireLine(right + Vector3.up, Vector3.up * 1.6f, Vector3.forward, 40f, 0.9f), Is.False);
        }

        [Test]
        public void CardTraits_DescribeHowEachCardReachesEnemies()
        {
            var bolt = ScriptableObject.CreateInstance<CardDefinition>();
            bolt.element = Element.Lightning;
            bolt.targeting = CardTargeting.Aim;
            bolt.range = 55f;
            bolt.effects = new List<SkillEffectSpec> { SkillEffectSpec.Damage(42f) };
            var nova = ScriptableObject.CreateInstance<CardDefinition>();
            nova.element = Element.Ice;
            nova.targeting = CardTargeting.AreaAroundSelf;
            nova.effects = new List<SkillEffectSpec> { SkillEffectSpec.Area(35f, 6f) };
            var buff = ScriptableObject.CreateInstance<CardDefinition>();
            buff.element = Element.Lightning;
            buff.targeting = CardTargeting.Self;
            buff.effects = new List<SkillEffectSpec> { SkillEffectSpec.Buff(0.3f, 8f) };
            try
            {
                NpcCardTraits b = NpcCardTraits.From(bolt);
                Assert.That(b.HitsEnemies && b.NeedsAim && !b.IsArea, Is.True);
                Assert.That(b.Reach, Is.EqualTo(55f));
                NpcCardTraits n = NpcCardTraits.From(nova);
                Assert.That(n.IsArea && !n.NeedsAim, Is.True);
                Assert.That(n.Reach, Is.EqualTo(6f));
                NpcCardTraits t = NpcCardTraits.From(buff);
                Assert.That(t.HitsEnemies, Is.False, "a self buff applies no element to enemies, so it cannot finish a combo");
                Assert.That(t.IsSupport, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(bolt);
                Object.DestroyImmediate(nova);
                Object.DestroyImmediate(buff);
            }
        }
    }

    /// <summary>Deck building for the NPC (and later players): collection, loadout limits, reset, balance.</summary>
    public class DeckLoadoutTests
    {
        readonly TestCards cards = new TestCards();

        [TearDown]
        public void TearDown() => cards.DestroyAll();

        CardDefinition Card(string id, Element element)
        {
            CardDefinition card = cards.Card(id, 25f);
            card.element = element;
            return card;
        }

        [Test]
        public void AddAndRemove_RespectSizeLimitsAndOwnedCopies()
        {
            CardDefinition bolt = Card("bolt", Element.Lightning);
            CardDefinition fire = Card("fire", Element.Fire);
            var collection = new CardCollection();
            collection.Add(bolt, 2);
            collection.Add(fire, 1);
            var loadout = new DeckLoadout(new[] { bolt, fire }, 2, 2, 4, collection);

            Assert.That(loadout.Count, Is.EqualTo(2));
            Assert.That(loadout.TryAdd(fire), Is.EqualTo(LoadoutEditResult.NoCopiesLeft), "only one fire card owned");
            Assert.That(loadout.TryAdd(bolt), Is.EqualTo(LoadoutEditResult.Ok));
            Assert.That(loadout.TryAdd(bolt), Is.EqualTo(LoadoutEditResult.NoCopiesLeft));
            collection.Add(bolt, 5);
            Assert.That(loadout.TryAdd(bolt), Is.EqualTo(LoadoutEditResult.Ok));
            Assert.That(loadout.TryAdd(bolt), Is.EqualTo(LoadoutEditResult.DeckFull), "max 4");

            Assert.That(loadout.TryRemoveAt(0), Is.EqualTo(LoadoutEditResult.Ok));
            Assert.That(loadout.TryRemoveAt(0), Is.EqualTo(LoadoutEditResult.Ok));
            Assert.That(loadout.TryRemoveAt(0), Is.EqualTo(LoadoutEditResult.BelowMinimum), "min 2");
            Assert.That(loadout.TryRemoveAt(9), Is.EqualTo(LoadoutEditResult.InvalidIndex));
        }

        [Test]
        public void Reset_ReturnsToTheStartingList_AndBalanceIsCounted()
        {
            CardDefinition bolt = Card("bolt", Element.Lightning);
            CardDefinition ice = Card("ice", Element.Ice);
            CardDefinition heal = Card("heal", Element.None);
            var collection = new CardCollection();
            collection.Add(bolt, 3);
            collection.Add(ice, 2);
            collection.Add(heal, 1);
            var start = new List<CardDefinition> { bolt, bolt, heal };
            var loadout = new DeckLoadout(start, 2, 1, 8, collection);

            loadout.TryAdd(ice);
            loadout.TryAdd(ice);
            int[] counts = loadout.ElementCounts();
            Assert.That(counts[(int)Element.Lightning], Is.EqualTo(2));
            Assert.That(counts[(int)Element.Ice], Is.EqualTo(2));
            Assert.That(counts[(int)Element.None], Is.EqualTo(1));

            int changes = 0;
            loadout.Changed += () => changes++;
            loadout.ResetToDefault();
            Assert.That(loadout.Cards, Is.EqualTo(start));
            Assert.That(loadout.DominantElement(), Is.EqualTo(Element.Lightning));
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void SetCards_SkipsWhatIsNotOwned_AndRewardsGrowTheCollection()
        {
            CardDefinition bolt = Card("bolt", Element.Lightning);
            CardDefinition rare = Card("rare", Element.Gravity);
            var collection = new CardCollection();
            collection.Add(bolt, 2);
            var loadout = new DeckLoadout(new[] { bolt }, 2, 1, 6, collection);

            Assert.That(loadout.SetCards(new[] { bolt, bolt, bolt, rare }), Is.EqualTo(2));
            collection.Add(rare); // a stage reward
            Assert.That(loadout.CanAdd(rare), Is.EqualTo(LoadoutEditResult.Ok));
            Assert.That(collection.Remove(rare), Is.True);
            Assert.That(collection.CountOf(rare), Is.Zero);
        }
    }

    /// <summary>Down → revive rules and the "in combat" memory.</summary>
    public class ReviveAndEngagementTests
    {
        [Test]
        public void Revive_FillsWhileAnAllyIsNear_DrainsWhenTheyLeave_BleedsOutOtherwise()
        {
            var model = new ReviveModel(2f, 10f);
            model.Down();
            Assert.That(model.IsDowned, Is.True);

            Assert.That(model.Tick(1.5f, 7), Is.EqualTo(ReviveTick.None));
            Assert.That(model.Progress01, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(model.BleedOutRemaining, Is.EqualTo(10f), "bleed-out pauses while someone helps");

            model.Tick(1f, 0);
            Assert.That(model.Progress01, Is.EqualTo(0.5f).Within(1e-4f), "drains at half speed when they step away");
            Assert.That(model.Tick(1f, 7), Is.EqualTo(ReviveTick.Revived));
            Assert.That(model.IsDowned, Is.False);

            model.Down();
            Assert.That(model.Tick(9.9f, 0), Is.EqualTo(ReviveTick.None));
            Assert.That(model.Tick(0.2f, 0), Is.EqualTo(ReviveTick.BledOut));
            Assert.That(model.Tick(5f, 7), Is.EqualTo(ReviveTick.None), "too late");
            model.Clear();
            Assert.That(model.IsDowned || model.HasBledOut, Is.False);
        }

        [Test]
        public void Engagement_LastsAWhileAfterTheLastHostileDamage()
        {
            var state = new EngagementState(5f);
            Assert.That(state.IsEngaged(0f, false), Is.False);
            Assert.That(state.IsEngaged(0f, true), Is.True, "a hostile nearby");
            state.NotifyHostileDamage(10f);
            Assert.That(state.IsEngaged(14f, false), Is.True);
            Assert.That(state.IsEngaged(15.5f, false), Is.False);
        }
    }

    /// <summary>The read-only combo questions agree with the resolver and never change state.</summary>
    public class ComboPlannerTests
    {
        static readonly List<ComboRule> Rules = new List<ComboRule>
        {
            new ComboRule(0, Element.Ice, Element.Lightning, 4f, 3f, 1.3f),
            new ComboRule(1, Element.Fire, Element.Gravity, 4f, 3f, 1.3f),
            new ComboRule(2, Element.Fire, Element.Lightning, 4f, 3f, 1.3f),
        };

        [Test]
        public void FollowUp_MatchesWhatTheResolverWouldDo_WithoutChangingAnything()
        {
            var auras = new ElementAuraModel();
            auras.Apply(Element.Ice, null, 1, TeamId.Players, 0);
            auras.Tick(1f);

            Assert.That(ComboPlanner.TryFindFollowUp(auras, Rules, Element.Lightning, 0.3f, out ComboOpportunity chance), Is.True);
            Assert.That(chance.RuleIndex, Is.EqualTo(0));
            Assert.That(chance.PrimedElement, Is.EqualTo(Element.Ice));
            Assert.That(chance.ApplierId, Is.EqualTo(1));
            Assert.That(chance.TimeLeft, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(auras.Has(Element.Ice), Is.True, "asking does not consume");

            Assert.That(ComboPlanner.TryFindFollowUp(auras, Rules, Element.Fire, 0.3f, out _), Is.False);
            Assert.That(ComboPlanner.FinisherMask(auras, Rules, 0.3f), Is.EqualTo(1 << (int)Element.Lightning));

            var hit = new ElementHit(null, Element.Lightning, null, 2, TeamId.Players, default, 0);
            Assert.That(ComboResolver.Resolve(auras, Rules, hit, out _), Is.True, "the resolver agrees");
            Assert.That(ComboPlanner.TryFindFollowUp(auras, Rules, Element.Lightning, 0f, out _), Is.False, "used up / on cooldown");
        }

        [Test]
        public void Pairs_KnowsTheThreeCombos()
        {
            Assert.That(ComboPlanner.Pairs(Rules, Element.Ice, Element.Lightning, out int a) && a == 0, Is.True);
            Assert.That(ComboPlanner.Pairs(Rules, Element.Gravity, Element.Fire, out int b) && b == 1, Is.True);
            Assert.That(ComboPlanner.Pairs(Rules, Element.Lightning, Element.Fire, out int c) && c == 2, Is.True);
            Assert.That(ComboPlanner.Pairs(Rules, Element.Ice, Element.Fire, out _), Is.False);
            Assert.That(ComboPlanner.Pairs(Rules, Element.Ice, Element.Ice, out _), Is.False);
        }
    }
}
