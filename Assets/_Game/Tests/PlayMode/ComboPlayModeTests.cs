using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Element combos end to end: real skill casts (SkillCaster) apply elements, the ComboSystem
    /// fires superconductor / singularity / overload, combo damage never chains into more combos,
    /// co-op credit, and the deck cycle stays exact while combos happen.
    /// </summary>
    public class ComboPlayModeTests
    {
        const float Dt = 1f / 60f;
        readonly List<Object> spawned = new List<Object>();
        readonly List<ComboReport> triggered = new List<ComboReport>();
        readonly List<ComboReport> finished = new List<ComboReport>();
        readonly List<DamageEvent> comboDamage = new List<DamageEvent>();
        readonly List<DamageEvent> allDamage = new List<DamageEvent>();
        ComboSystem system;

        sealed class FixedAim : IAimProvider
        {
            public Ray Ray;
            public Ray GetAimRay() => Ray;
            public void AddRecoil(Vector2 kickDegrees) { }
        }

        sealed class Caster
        {
            public TestPlayerActor Actor;
            public SkillCaster Skills;
            public FixedAim Aim;

            public void CastAt(CardDefinition card, Health target, int level = 0)
            {
                Vector3 from = Actor.transform.position + Vector3.up * 1.5f;
                Vector3 to = target.transform.position + Vector3.up * 1f;
                Aim.Ray = new Ray(from, (to - from).normalized);
                Skills.CastCard(card, level);
            }
        }

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
            ComboEvents.Triggered += OnTriggered;
            ComboEvents.Finished += OnFinished;
            CombatEvents.DamageApplied += OnDamage;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            ComboEvents.Triggered -= OnTriggered;
            ComboEvents.Finished -= OnFinished;
            CombatEvents.DamageApplied -= OnDamage;
            ComboSystem.TierOverride = null;
            foreach (Object o in spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            spawned.Clear();
            triggered.Clear();
            finished.Clear();
            comboDamage.Clear();
            allDamage.Clear();
        }

        void OnTriggered(ComboReport report) => triggered.Add(report);
        void OnFinished(ComboReport report) => finished.Add(report);

        void OnDamage(DamageEvent e)
        {
            allDamage.Add(e);
            if (e.Info.Kind == DamageKind.Combo)
            {
                comboDamage.Add(e);
            }
        }

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        // ---------------------------------------------------------------- rig

        void Ground()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(120f, 1f, 120f);
            Physics.SyncTransforms();
        }

        Health Enemy(Vector3 position, float maxHealth = 1000f)
        {
            var root = Track(new GameObject("ComboTarget"));
            root.transform.position = position;
            var health = root.AddComponent<Health>();
            health.Team = TeamId.Enemies;
            health.SetMaxHealth(maxHealth, true);
            root.AddComponent<CombatStatus>();
            root.AddComponent<ForceReceiver>();
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(1f, 2f, 1f);
            body.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);
            Physics.SyncTransforms();
            return health;
        }

        Caster CreateCaster(Vector3 position, string name = "Caster")
        {
            var go = Track(new GameObject(name));
            go.transform.position = position;
            go.AddComponent<Health>();
            var caster = new Caster
            {
                Actor = go.AddComponent<TestPlayerActor>(),
                Skills = go.AddComponent<SkillCaster>(),
                Aim = new FixedAim(),
            };
            caster.Skills.Initialize(caster.Actor, caster.Aim, Physics.DefaultRaycastLayers, GameLayers.SkillQueryMask);
            return caster;
        }

        ComboDefinition Combo(string id, Element a, Element b, ComboKind kind, ComboTierSettings tier)
        {
            var combo = Track(ScriptableObject.CreateInstance<ComboDefinition>());
            combo.comboId = id;
            combo.displayName = id;
            combo.elementA = a;
            combo.elementB = b;
            combo.kind = kind;
            combo.window = 4f;
            combo.cooldown = 3f;
            combo.coopMultiplier = 1.3f;
            combo.tiers = new[] { tier, tier, tier };
            return combo;
        }

        void CreateComboSystem()
        {
            var go = Track(new GameObject("ComboSystem"));
            system = go.AddComponent<ComboSystem>();
            system.Configure(new List<ComboDefinition>
            {
                Combo("superconductor", Element.Ice, Element.Lightning, ComboKind.ChainBurst,
                    new ComboTierSettings { damage = 40f, radius = 0f, chainCount = 3, chainRange = 9f, chainFalloff = 0.5f }),
                Combo("singularity", Element.Fire, Element.Gravity, ComboKind.Singularity,
                    new ComboTierSettings { damage = 50f, radius = 4f, pullRadius = 8f, pullSpeed = 6f, pullDuration = 0.5f }),
                Combo("overload", Element.Fire, Element.Lightning, ComboKind.Explosion,
                    new ComboTierSettings { damage = 30f, radius = 5f }),
            });
        }

        CardDefinition Card(string id, Element element, float cost, params SkillEffectSpec[] effects)
        {
            var card = Track(ScriptableObject.CreateInstance<CardDefinition>());
            card.cardId = id;
            card.displayName = id;
            card.element = element;
            card.maxGauge = 100f;
            card.gaugeCost = cost;
            card.cooldown = 0f;
            card.targeting = CardTargeting.Aim;
            card.range = 80f;
            card.effects = new List<SkillEffectSpec>(effects);
            return card;
        }

        CardDefinition Ice() => Card("ice", Element.Ice, 25f, SkillEffectSpec.Damage(10f));
        CardDefinition Lightning() => Card("lightning", Element.Lightning, 25f, SkillEffectSpec.Damage(10f));
        CardDefinition Gravity() => Card("gravity", Element.Gravity, 25f, SkillEffectSpec.Damage(10f));

        /// <summary>Like the incendiary grenade: burn only, no direct damage.</summary>
        CardDefinition Burn() => Card("burn", Element.Fire, 25f, SkillEffectSpec.DamageOverTime(4f, 2f));

        float ComboDamageTo(Health target) => comboDamage.Where(e => e.Target == target).Sum(e => e.Result.Damage);
        int ComboHitsOn(Health target) => comboDamage.Count(e => e.Target == target);

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        // ---------------------------------------------------------------- tests

        [UnityTest]
        public IEnumerator Superconductor_FrozenEnemyHitByLightning_ChainsToNearbyEnemies()
        {
            Ground();
            CreateComboSystem();
            Caster player = CreateCaster(Vector3.zero);
            Health a = Enemy(new Vector3(0f, 0f, 10f));
            Health b = Enemy(new Vector3(3f, 0f, 10f));
            Health c = Enemy(new Vector3(6f, 0f, 12f));
            Health far = Enemy(new Vector3(0f, 0f, 45f));
            yield return null;

            player.CastAt(Ice(), a);
            Assert.That(a.GetComponent<CombatStatus>().Elements.Has(Element.Ice), Is.True, "frozen");
            Assert.That(triggered, Is.Empty);

            player.CastAt(Lightning(), a);
            Assert.That(triggered.Count, Is.EqualTo(1));
            Assert.That(triggered[0].Definition.comboId, Is.EqualTo("superconductor"));
            Assert.That(finished[0].EnemiesHit, Is.EqualTo(3));

            Assert.That(ComboDamageTo(a), Is.EqualTo(40f).Within(1e-3f), "burst on the frozen enemy");
            Assert.That(ComboDamageTo(b), Is.EqualTo(20f).Within(1e-3f), "first jump (×0.5)");
            Assert.That(ComboDamageTo(c), Is.EqualTo(10f).Within(1e-3f), "second jump (×0.5)");
            Assert.That(ComboDamageTo(far), Is.EqualTo(0f), "out of chain range");
            Assert.That(a.Current, Is.EqualTo(1000f - 10f - 10f - 40f).Within(1e-3f), "skill hits + combo, nothing doubled");
            Assert.That(new[] { a, b, c }.All(h => ComboHitsOn(h) == 1), Is.True, "each enemy hit once by the combo");

            DamageEvent first = comboDamage[0];
            Assert.That((first.Info.Flags & DamageFlags.ComboDamage) != 0, Is.True);
            Assert.That(first.Info.Source, Is.SameAs(player.Actor), "the attacker is recorded");
            Assert.That(first.Info.SourceTeam, Is.EqualTo(TeamId.Players));
            Assert.That(a.GetComponent<CombatStatus>().Elements.Has(Element.Ice), Is.False, "the ice was used up");
        }

        [UnityTest]
        public IEnumerator ComboDamage_NeverStartsAnotherCombo_AndTheCooldownHolds()
        {
            Ground();
            CreateComboSystem();
            Caster player = CreateCaster(Vector3.zero);
            Health a = Enemy(new Vector3(0f, 0f, 10f));
            Health b = Enemy(new Vector3(3f, 0f, 10f));
            Health c = Enemy(new Vector3(-3f, 0f, 10f));
            yield return null;

            foreach (Health h in new[] { a, b, c })
            {
                player.CastAt(Ice(), h);
            }

            player.CastAt(Lightning(), a);
            Assert.That(triggered.Count, Is.EqualTo(1), "the chain's lightning damage on frozen B and C starts nothing");
            Assert.That(b.GetComponent<CombatStatus>().Elements.Has(Element.Ice), Is.True, "B keeps its ice");
            Assert.That(c.GetComponent<CombatStatus>().Elements.Has(Element.Ice), Is.True);

            player.CastAt(Ice(), a);
            player.CastAt(Lightning(), a);
            Assert.That(triggered.Count, Is.EqualTo(1), "same combo on A within 3 s: blocked");

            yield return Frames(10);
            Assert.That(triggered.Count, Is.EqualTo(1), "nothing fires on its own later");

            player.CastAt(Lightning(), b);
            Assert.That(triggered.Count, Is.EqualTo(2), "a different frozen enemy can still be finished");
        }

        [UnityTest]
        public IEnumerator Overload_BurnThenLightning_ExplodesAroundTheTarget()
        {
            Ground();
            CreateComboSystem();
            Caster player = CreateCaster(Vector3.zero);
            Health a = Enemy(new Vector3(0f, 0f, 10f));
            Health near = Enemy(new Vector3(3f, 0f, 10f));
            Health outside = Enemy(new Vector3(7f, 0f, 13f));
            yield return null;

            player.CastAt(Burn(), a);
            Assert.That(a.GetComponent<CombatStatus>().Elements.Has(Element.Fire), Is.True, "a burn without direct damage still applies fire");

            player.CastAt(Lightning(), a);
            Assert.That(triggered.Count, Is.EqualTo(1));
            Assert.That(triggered[0].Definition.comboId, Is.EqualTo("overload"));
            Assert.That(ComboDamageTo(a), Is.EqualTo(30f).Within(1e-3f));
            Assert.That(ComboDamageTo(near), Is.EqualTo(30f).Within(1e-3f));
            Assert.That(ComboDamageTo(outside), Is.EqualTo(0f), "outside the 5 m blast");
        }

        [UnityTest]
        public IEnumerator Singularity_PullsEnemiesIn_ThenExplodes()
        {
            Ground();
            CreateComboSystem();
            Caster player = CreateCaster(Vector3.zero);
            Health a = Enemy(new Vector3(0f, 0f, 10f));
            Health b = Enemy(new Vector3(6f, 0f, 10f));
            yield return null;

            float before = Vector3.Distance(a.transform.position, b.transform.position);
            player.CastAt(Gravity(), a);
            player.CastAt(Burn(), a);
            Assert.That(triggered.Count, Is.EqualTo(1));
            Assert.That(triggered[0].Definition.comboId, Is.EqualTo("singularity"));
            Assert.That(comboDamage, Is.Empty, "the blast waits for the pull");

            yield return Frames(45); // 0.75 s > 0.5 s pull
            float after = Vector3.Distance(a.transform.position, b.transform.position);
            Assert.That(after, Is.LessThan(before - 1.5f), "B was pulled toward the singularity");
            Assert.That(finished.Count, Is.EqualTo(1));
            Assert.That(ComboDamageTo(a), Is.EqualTo(50f).Within(1e-3f));
            Assert.That(ComboDamageTo(b), Is.EqualTo(50f).Within(1e-3f), "pulled into the blast radius (it started outside 4 m)");
            Assert.That(finished[0].EnemiesHit, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator WrongPairs_AndExpiredElements_DoNotFire()
        {
            Ground();
            CreateComboSystem();
            Caster player = CreateCaster(Vector3.zero);
            Health a = Enemy(new Vector3(0f, 0f, 10f));
            Health b = Enemy(new Vector3(4f, 0f, 10f));
            Health c = Enemy(new Vector3(-4f, 0f, 10f));
            yield return null;

            player.CastAt(Ice(), a);
            player.CastAt(Burn(), a);
            Assert.That(triggered, Is.Empty, "ice + fire is not a combo");
            Assert.That(a.GetComponent<CombatStatus>().Elements.ActiveCount, Is.EqualTo(2), "both elements stay");

            player.CastAt(Ice(), c);
            player.CastAt(Gravity(), c);
            player.CastAt(Ice(), c);
            Assert.That(triggered, Is.Empty, "ice + gravity is not a combo, and repeating ice only refreshes it");
            Assert.That(comboDamage, Is.Empty);

            player.CastAt(Ice(), b);
            yield return Frames(255); // 4.25 s
            Assert.That(b.GetComponent<CombatStatus>().Elements.Has(Element.Ice), Is.False, "ice wore off");
            player.CastAt(Lightning(), b);
            Assert.That(triggered, Is.Empty, "too late for a superconductor");
        }

        [UnityTest]
        public IEnumerator Coop_AllyFinishingTheCombo_DealsThirtyPercentMore()
        {
            Ground();
            CreateComboSystem();
            Caster first = CreateCaster(new Vector3(-2f, 0f, 0f), "PlayerOne");
            Caster second = CreateCaster(new Vector3(2f, 0f, 0f), "PlayerTwo");
            Health a = Enemy(new Vector3(0f, 0f, 10f));
            yield return null;

            first.CastAt(Ice(), a);
            second.CastAt(Lightning(), a);

            Assert.That(triggered.Count, Is.EqualTo(1));
            ComboReport report = triggered[0];
            Assert.That(report.IsCoop, Is.True);
            Assert.That(report.Trigger.Applier, Is.SameAs(first.Actor), "who applied the element");
            Assert.That(report.Trigger.Completer, Is.SameAs(second.Actor), "who finished it");
            Assert.That(report.Trigger.ApplierId, Is.Not.EqualTo(report.Trigger.CompleterId));
            Assert.That(report.Trigger.ApplierId, Is.EqualTo(CombatantRegistry.IdOf(first.Actor)));
            Assert.That(ComboDamageTo(a), Is.EqualTo(40f * 1.3f).Within(1e-3f));
            Assert.That((comboDamage[0].Info.Flags & DamageFlags.Coop) != 0, Is.True);
        }

        [UnityTest]
        public IEnumerator KillingBlow_StillFinishesTheCombo()
        {
            Ground();
            CreateComboSystem();
            Caster player = CreateCaster(Vector3.zero);
            Health a = Enemy(new Vector3(0f, 0f, 10f), maxHealth: 15f);
            Health b = Enemy(new Vector3(3f, 0f, 10f));
            yield return null;

            player.CastAt(Ice(), a);
            player.CastAt(Lightning(), a);
            Assert.That(a.IsAlive, Is.False);
            Assert.That(triggered.Count, Is.EqualTo(1), "the lightning that killed A still completed the combo");
            Assert.That(ComboDamageTo(b), Is.EqualTo(20f).Within(1e-3f), "and the chain jumped on to B");

            yield return Frames(2);
            Assert.That(a.GetComponent<CombatStatus>().Elements.ActiveCount, Is.EqualTo(0), "the dead keep no elements");
        }

        [UnityTest]
        public IEnumerator GroupCombo_PlaysOneBigEffect_AndCapsHitsPerTargetPerFrame()
        {
            Ground();
            CreateComboSystem();
            var effect = Track(new GameObject("TestComboFx"));
            effect.SetActive(false);
            effect.AddComponent<Effects.PooledFx>().Lifetime = 1f;
            foreach (ComboDefinition combo in system.Combos)
            {
                foreach (ComboTierSettings tier in combo.tiers)
                {
                    tier.fx = effect;
                }
            }

            Caster player = CreateCaster(Vector3.zero);
            Health a = Enemy(new Vector3(0f, 0f, 10f));
            Health b = Enemy(new Vector3(1.5f, 0f, 10.5f));
            Health c = Enemy(new Vector3(-1.5f, 0f, 10.5f));
            yield return null;

            CardDefinition fireArea = Card("fire_area", Element.Fire, 25f, SkillEffectSpec.Area(5f, 6f));
            fireArea.targeting = CardTargeting.AimPoint;
            CardDefinition boltArea = Card("bolt_area", Element.Lightning, 25f, SkillEffectSpec.Area(5f, 6f));
            boltArea.targeting = CardTargeting.AimPoint;
            player.CastAt(fireArea, a);
            player.CastAt(boltArea, a);

            Assert.That(triggered.Count, Is.EqualTo(3), "an overload on each of the three burning enemies");
            Assert.That(system.SkippedEffects, Is.EqualTo(2), "but one big effect for the group");
            foreach (Health h in new[] { a, b, c })
            {
                Assert.That(ComboHitsOn(h), Is.EqualTo(ComboDamageBudget.DefaultMaxHitsPerTargetPerFrame),
                    "three overlapping blasts in one frame: capped at two hits per enemy");
                Assert.That(ComboDamageTo(h), Is.EqualTo(60f).Within(1e-3f));
            }
        }

        [UnityTest]
        public IEnumerator DeckCycle_StaysExact_WhileCombosHappen()
        {
            Ground();
            CreateComboSystem();
            Caster player = CreateCaster(Vector3.zero);
            Health target = Enemy(new Vector3(0f, 0f, 10f), maxHealth: 100000f);
            yield return null;

            var deckGo = player.Actor.gameObject;
            var deck = deckGo.AddComponent<DeckController>();
            CardDefinition ice = Card("ice", Element.Ice, 50f, SkillEffectSpec.Damage(5f));
            CardDefinition lightning = Card("lightning", Element.Lightning, 50f, SkillEffectSpec.Damage(5f));
            var cards = Enumerable.Repeat(ice, 6).Concat(Enumerable.Repeat(lightning, 6)).ToList();
            deck.Rebuild(cards, 3, null, 777);
            DeckState state = deck.Deck;

            int uses = 0;
            int depleted = 0;
            var wrongGauge = new List<string>();
            state.CardUsed += r =>
            {
                uses++;
                float expected = Mathf.Max(0f, r.GaugeBefore - r.Card.GaugeCost);
                if (Mathf.Abs(r.GaugeAfter - expected) > 1e-3f)
                {
                    wrongGauge.Add($"{r.Card} {r.GaugeBefore}->{r.GaugeAfter}");
                }
            };
            state.CardDepleted += (slot, card) => depleted++;

            float gaugeAtCombo = -1f;
            Action<ComboReport> snapshot = _ => gaugeAtCombo = TotalGauge(state);
            ComboEvents.Triggered += snapshot;
            try
            {
                for (int i = 0; i < 30; i++)
                {
                    int combosBefore = triggered.Count;
                    player.Aim.Ray = new Ray(new Vector3(0f, 1.5f, 0f), (target.transform.position + Vector3.up - new Vector3(0f, 1.5f, 0f)).normalized);
                    Assert.That(deck.UseSlot(i % 3), Is.True, $"use {i}");
                    if (triggered.Count > combosBefore)
                    {
                        Assert.That(TotalGauge(state), Is.EqualTo(gaugeAtCombo).Within(1e-3f), "a combo never touches card gauges");
                    }

                    Assert.That(state.ActiveCardCount, Is.EqualTo(12), "no card lost or duplicated");
                    Assert.That(state.AllCards().Select(c => c.InstanceId).Distinct().Count(), Is.EqualTo(12));
                    yield return Frames(40); // 0.67 s: lets the 3 s combo cooldown run out now and then
                }
            }
            finally
            {
                ComboEvents.Triggered -= snapshot;
            }

            Assert.That(wrongGauge, Is.Empty, "every use spent exactly its cost");
            Assert.That(uses, Is.EqualTo(30));
            Assert.That(depleted, Is.EqualTo(15), "50-cost cards: every second use empties a card, discarded once");
            Assert.That(triggered.Count, Is.GreaterThan(0), "combos happened during the cycle");
        }

        static float TotalGauge(DeckState state) => state.AllCards().Sum(c => c.Gauge);
    }
}
