using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.NPC;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>Static enemy target for NPC tests: an Enemies-team Health that registers like EnemyActor does.</summary>
    [RequireComponent(typeof(Health))]
    public class TestEnemyActor : MonoBehaviour, ICombatActor
    {
        Health health;
        CombatStatus status;

        public string DisplayName => name;
        public TeamId Team => Health.Team;
        public Health Health => health != null ? health : health = GetComponent<Health>();
        public CombatStatus Status => status != null ? status : status = GetComponent<CombatStatus>();
        public Vector3 Position => transform.position;
        public bool IsAlive => Health.IsAlive;

        void OnEnable() => CombatantRegistry.Register(this);

        void OnDisable() => CombatantRegistry.Unregister(this);
    }

    /// <summary>
    /// The NPC ally end to end on a real NavMesh with real bodies: following, finding and shooting
    /// enemies, its own deck (gauges, discards, draws, independent of the player's), co-op combos in
    /// both directions with the ×1.3 bonus and no runaway chains, down / revive, orders, dodging,
    /// swapping the controller, and deck edits locked during combat.
    /// </summary>
    public class NpcPlayModeTests
    {
        const float Dt = 1f / 60f;
        readonly List<Object> spawned = new List<Object>();
        readonly List<ComboReport> combos = new List<ComboReport>();
        readonly List<DamageEvent> damage = new List<DamageEvent>();
        ComboSystem comboSystem;

        sealed class ScriptedCommands : ICharacterCommandSource
        {
            public CharacterCommands Next;

            public CharacterCommands GetCommands(float deltaTime)
            {
                CharacterCommands c = Next;
                Next.FirePressed = false;
                Next.SkillPressedMask = 0;
                Next.OrderTacticPressed = false;
                Next.OrderMarkPressed = false;
                Next.DashPressed = false;
                return c;
            }
        }

        sealed class Body
        {
            public GameObject Root;
            public FpsCharacter Character;
            public DeckController Deck;
            public Health Health;
            public Revivable Revivable;
            public ScriptedCommands Commands;
            public NpcController Npc;
        }

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
            Assert.That(GameLayers.Player, Is.GreaterThanOrEqualTo(0), "Player layer missing: run the bootstrap");
            ComboEvents.Triggered += OnCombo;
            CombatEvents.DamageApplied += OnDamage;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            ComboEvents.Triggered -= OnCombo;
            CombatEvents.DamageApplied -= OnDamage;
            InputGate.Release(this);
            foreach (Object o in spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            spawned.Clear();
            combos.Clear();
            damage.Clear();
        }

        void OnCombo(ComboReport report) => combos.Add(report);
        void OnDamage(DamageEvent e) => damage.Add(e);

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        static IEnumerator Seconds(float seconds) => Frames(Mathf.CeilToInt(seconds / Dt));

        /// <summary>Runs frames until the condition holds or the time runs out.</summary>
        static IEnumerator Until(System.Func<bool> condition, float seconds)
        {
            int frames = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < frames && !condition(); i++)
            {
                yield return null;
            }
        }

        // ---------------------------------------------------------------- world

        void Arena(float size = 60f)
        {
            var root = Track(new GameObject("NpcTestFloor"));
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(size, 1f, size);
            Physics.SyncTransforms();
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            Assert.That(surface.navMeshData, Is.Not.Null);
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

        void Combos()
        {
            var go = Track(new GameObject("ComboSystem"));
            comboSystem = go.AddComponent<ComboSystem>();
            comboSystem.Configure(new List<ComboDefinition>
            {
                Combo("superconductor", Element.Ice, Element.Lightning, ComboKind.ChainBurst,
                    new ComboTierSettings { damage = 40f, radius = 0f, chainCount = 0, chainRange = 9f, chainFalloff = 0.5f }),
                Combo("singularity", Element.Fire, Element.Gravity, ComboKind.Singularity,
                    new ComboTierSettings { damage = 50f, radius = 4f, pullRadius = 8f, pullSpeed = 6f, pullDuration = 0.3f }),
                Combo("overload", Element.Fire, Element.Lightning, ComboKind.Explosion,
                    new ComboTierSettings { damage = 30f, radius = 3f }),
            });
        }

        CardDefinition Card(string id, Element element, float cost, CardTargeting targeting, params SkillEffectSpec[] effects)
        {
            var card = Track(ScriptableObject.CreateInstance<CardDefinition>());
            card.cardId = id;
            card.displayName = id;
            card.element = element;
            card.maxGauge = 100f;
            card.gaugeCost = cost;
            card.cooldown = 0.2f;
            card.targeting = targeting;
            card.range = 60f;
            card.effects = new List<SkillEffectSpec>(effects);
            return card;
        }

        CardDefinition Bolt(float cost = 25f) => Card("bolt", Element.Lightning, cost, CardTargeting.Aim, SkillEffectSpec.Damage(12f));
        CardDefinition Ice() => Card("ice", Element.Ice, 25f, CardTargeting.Aim, SkillEffectSpec.Damage(12f));
        CardDefinition Fire() => Card("fire", Element.Fire, 25f, CardTargeting.AimPoint, SkillEffectSpec.Area(8f, 3f));
        CardDefinition Plain() => Card("plain", Element.None, 25f, CardTargeting.Aim, SkillEffectSpec.Damage(5f));
        CardDefinition HealCard() => Card("heal", Element.None, 50f, CardTargeting.AreaAroundSelf, SkillEffectSpec.Heal(40f, 8f));

        WeaponDefinition Weapon(float damage = 6f)
        {
            var def = Track(ScriptableObject.CreateInstance<WeaponDefinition>());
            def.damage = damage;
            def.baseSpread = 0.1f;
            def.spreadPerShot = 0f;
            def.movingSpread = 0f;
            def.airborneSpread = 0f;
            def.recoilPitch = 0.1f;
            def.recoilYaw = 0f;
            return def;
        }

        NpcProfile Profile()
        {
            NpcProfile profile = Track(NpcProfile.CreateRuntimeDefault());
            profile.thinkInterval = 0.1f;
            profile.reactionTime = 0.1f;
            profile.aimError = 0.2f;
            profile.dodgeChance = 1f;
            profile.castInterval = 0.6f;
            return profile;
        }

        /// <summary>The shared FPS body (same components for the player and the NPC).</summary>
        Body CreateBody(string name, Vector3 position, IEnumerable<CardDefinition> cards, int handSize, bool npc, Body leader = null,
            NpcProfile profile = null, float maxHealth = 150f)
        {
            var body = new Body { Root = Track(new GameObject(name)) };
            GameObject go = body.Root;
            go.SetActive(false);
            go.layer = GameLayers.Player;
            go.transform.position = position;
            var controller = go.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.minMoveDistance = 0f;
            go.AddComponent<FpsMotor>();
            body.Health = go.AddComponent<Health>();
            body.Health.Team = TeamId.Players;
            body.Health.SetMaxHealth(maxHealth, true);
            go.AddComponent<CombatStatus>();
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(go.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            var look = go.AddComponent<FpsLook>();
            look.Configure(go.transform, pivot.transform, null);
            var weapon = go.AddComponent<HitscanWeapon>();
            weapon.SetDefinition(Weapon());
            go.AddComponent<SkillCaster>();
            body.Revivable = go.AddComponent<Revivable>();
            body.Revivable.Configure(1.5f, 4f, 2.4f, 0.45f);
            go.AddComponent<AllyCollisionFilter>();
            if (npc)
            {
                body.Npc = go.AddComponent<NpcController>();
            }

            body.Character = go.AddComponent<FpsCharacter>();
            body.Deck = go.AddComponent<DeckController>();
            if (npc)
            {
                body.Npc.Configure(profile ?? Profile(), body.Character, leader?.Character, comboSystem, seed: 99);
            }

            go.SetActive(true);
            if (!npc)
            {
                body.Commands = new ScriptedCommands();
                body.Character.CommandSource = body.Commands;
            }

            body.Deck.Rebuild(cards, handSize, null, 1234);
            Physics.SyncTransforms();
            return body;
        }

        Health Enemy(Vector3 position, float maxHealth = 2000f, string name = "Enemy")
        {
            var go = Track(new GameObject(name));
            go.SetActive(false);
            go.layer = GameLayers.Enemy;
            go.transform.position = position;
            var health = go.AddComponent<Health>();
            health.Team = TeamId.Enemies;
            health.SetMaxHealth(maxHealth, true);
            go.AddComponent<CombatStatus>();
            go.AddComponent<ForceReceiver>();
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.45f;
            go.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);
            go.AddComponent<TestEnemyActor>();
            go.SetActive(true);
            Physics.SyncTransforms();
            return health;
        }

        static void AimAt(Body body, Vector3 target)
        {
            Vector3 eye = body.Character.Look.PitchPivot.position;
            Vector3 to = target + Vector3.up * 1.1f - eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            body.Character.Look.SetRotation(yaw, pitch);
        }

        float DamageFrom(ICombatActor source, Health target, DamageKind kind) =>
            damage.Where(e => e.Target == target && ReferenceEquals(e.Info.Source, source) && e.Info.Kind == kind).Sum(e => e.Result.Damage);

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        // ---------------------------------------------------------------- 1. follow

        [UnityTest]
        public IEnumerator Npc_FollowsThePlayer_AndStaysOutOfTheLineOfFire()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, -2f), new[] { HealCard() }, 2, npc: true, leader: player);
            yield return Frames(10);

            player.Character.Motor.Teleport(new Vector3(-6f, 0.05f, 16f));
            Physics.SyncTransforms();
            Vector3 start = npc.Character.Position;
            yield return Until(() => FlatDistance(npc.Character.Position, player.Character.Position) < 7.5f, 6f);
            yield return Seconds(1.5f);

            float distance = FlatDistance(npc.Character.Position, player.Character.Position);
            Assert.That(distance, Is.LessThan(npc.Npc.Profile.followMaxDistance + 1.5f), "caught up with the player");
            Assert.That(FlatDistance(npc.Character.Position, start), Is.GreaterThan(8f), "walked there on the NavMesh");
            Assert.That(npc.Npc.Mode, Is.EqualTo(NpcMode.Follow));
            Ray aim = player.Character.Look.GetAimRay();
            Assert.That(NpcSteering.InFireLine(npc.Character.Position + Vector3.up * 1.1f, aim.origin, aim.direction, 40f, 0.9f), Is.False,
                "does not stand in front of the player's gun");
        }

        // ---------------------------------------------------------------- 2. detect and shoot

        [UnityTest]
        public IEnumerator Npc_DetectsAnEnemy_AndShootsIt()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(2.5f, 0.05f, -1f), new[] { HealCard() }, 2, npc: true, leader: player);
            Health enemy = Enemy(new Vector3(4f, 0f, 14f));

            yield return Until(() => DamageFrom(npc.Character, enemy, DamageKind.Weapon) > 0f, 5f);

            Assert.That(npc.Npc.Mode, Is.EqualTo(NpcMode.Engage));
            Assert.That(DamageFrom(npc.Character, enemy, DamageKind.Weapon), Is.GreaterThan(0f), "the NPC's rifle hit the enemy");
            Assert.That(npc.Npc.ShootTarget, Is.Not.Null);
            Assert.That(npc.Npc.ThinkCount, Is.LessThan(Mathf.CeilToInt(5f / npc.Npc.Profile.thinkInterval) + 5), "thinks on an interval, not every frame");
        }

        // ---------------------------------------------------------------- 3–5. skills, gauge, draw

        [UnityTest]
        public IEnumerator Npc_UsesASkill_AndTheCardsGaugeDrops()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            CardDefinition bolt = Bolt(25f);
            Body npc = CreateBody("Npc", new Vector3(2.5f, 0.05f, -1f), new[] { bolt, Plain() }, 2, npc: true, leader: player);
            Health enemy = Enemy(new Vector3(3f, 0f, 14f));
            var used = new List<CardUseResult>();
            npc.Deck.Deck.CardUsed += used.Add;

            yield return Until(() => used.Any(u => u.Card.Definition == bolt), 6f);

            CardUseResult use = used.First(u => u.Card.Definition == bolt);
            Assert.That(use.GaugeBefore, Is.EqualTo(100f));
            Assert.That(use.GaugeAfter, Is.EqualTo(75f), "spent the card's own cost");
            Assert.That(use.Card.Gauge, Is.EqualTo(75f).Or.EqualTo(50f), "gauge lives on the NPC's copy");
            Assert.That(npc.Npc.CastCount, Is.GreaterThan(0));
            yield return Frames(2);
            Assert.That(DamageFrom(npc.Character, enemy, DamageKind.Skill), Is.GreaterThan(0f), "the skill hit through the shared SkillCaster");
        }

        [UnityTest]
        public IEnumerator Npc_CardAtZero_IsDiscarded_AndTheNextOneDrawn_ThenTheDiscardReshuffles()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            CardDefinition oneShot = Bolt(100f);
            Body npc = CreateBody("Npc", new Vector3(2.5f, 0.05f, -1f), new[] { oneShot, oneShot, oneShot }, 2, npc: true, leader: player);
            Enemy(new Vector3(3f, 0f, 14f));
            DeckState deck = npc.Deck.Deck;
            int depleted = 0, drawn = 0, reshuffled = 0;
            deck.CardDepleted += (slot, card) => depleted++;
            deck.CardDrawn += (slot, card) => drawn++;
            deck.Reshuffled += n => reshuffled++;

            yield return Until(() => reshuffled > 0, 10f);

            Assert.That(depleted, Is.GreaterThanOrEqualTo(2), "a card used down to 0 goes to the discard pile");
            Assert.That(drawn, Is.GreaterThanOrEqualTo(2), "and the empty slot draws");
            Assert.That(reshuffled, Is.GreaterThan(0), "an empty draw pile reshuffles the discard pile");
            Assert.That(deck.ActiveCardCount, Is.EqualTo(3), "no card lost");
            Assert.That(deck.HandSize, Is.EqualTo(2));
        }

        // ---------------------------------------------------------------- 6 & 12. independent decks

        [UnityTest]
        public IEnumerator NpcDeck_IsIndependent_FromThePlayersDeck_EvenWithTheSameCards()
        {
            Arena();
            Combos();
            CardDefinition bolt = Bolt(25f);
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { bolt, Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(2.5f, 0.05f, -1f), new[] { bolt, Plain() }, 2, npc: true, leader: player);
            Enemy(new Vector3(3f, 0f, 14f));
            int playerEvents = 0;
            player.Deck.Deck.CardUsed += r => playerEvents++;
            player.Deck.Deck.GaugeChanged += (s, c, a, b) => playerEvents++;
            CardRuntime playerBolt = player.Deck.Deck.AllCards().First(c => c.Definition == bolt);

            yield return Until(() => npc.Deck.Deck.AllCards().Any(c => c.Definition == bolt && c.Gauge < 100f), 6f);

            CardRuntime npcBolt = npc.Deck.Deck.AllCards().First(c => c.Definition == bolt);
            Assert.That(npcBolt.Gauge, Is.LessThan(100f), "the NPC spent its own copy");
            Assert.That(playerBolt.Gauge, Is.EqualTo(100f), "the player's copy of the same card is untouched");
            Assert.That(npcBolt, Is.Not.SameAs(playerBolt));
            Assert.That(playerEvents, Is.Zero, "no event leaked into the player's deck");
            Assert.That(npc.Deck.Deck, Is.Not.SameAs(player.Deck.Deck));
            Assert.That(npc.Deck.Deck.HandSize, Is.EqualTo(2));
            Assert.That(player.Deck.Deck.HandSize, Is.EqualTo(3));

            // Rebuilding the NPC's deck (a loadout edit) leaves the player's deck alone.
            DeckState before = player.Deck.Deck;
            npc.Deck.Rebuild(new[] { Plain(), Plain() }, 2, null, 5);
            Assert.That(player.Deck.Deck, Is.SameAs(before));
            Assert.That(playerBolt.Gauge, Is.EqualTo(100f));
        }

        // ---------------------------------------------------------------- 7. player freezes → NPC lightning

        [UnityTest]
        public IEnumerator PlayerFreezes_NpcFollowsUpWithLightning_SuperconductorAsCoop()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Ice() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, -1f), new[] { Bolt(), Bolt() }, 2, npc: true, leader: player);
            Health enemy = Enemy(new Vector3(0f, 0f, 15f));
            yield return Seconds(0.5f);
            Assert.That(combos, Is.Empty);

            AimAt(player, enemy.transform.position);
            player.Commands.Next.SetSkillPressed(0); // the player's ice card, through the normal deck path
            yield return Frames(2);
            Assert.That(enemy.GetComponent<CombatStatus>().Elements.Has(Element.Ice) || combos.Count > 0, Is.True, "the player's ice landed");

            yield return Until(() => combos.Count > 0, 2.5f);

            Assert.That(combos, Has.Count.EqualTo(1));
            ComboReport combo = combos[0];
            Assert.That(combo.Definition.comboId, Is.EqualTo("superconductor"));
            Assert.That(combo.IsCoop, Is.True);
            Assert.That(combo.Trigger.Applier, Is.SameAs(player.Character), "the player set it up");
            Assert.That(combo.Trigger.Completer, Is.SameAs(npc.Character), "the NPC finished it");
            Assert.That(combo.Trigger.DamageMultiplier, Is.EqualTo(1.3f).Within(1e-4f));
            Assert.That(npc.Npc.LastSkillAction, Is.EqualTo(NpcActionKind.ComboFinish));
            Assert.That(npc.Npc.ComboFinishCasts, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- 8. NPC fire → player lightning

        [UnityTest]
        public IEnumerator NpcAppliesFire_PlayerFinishesWithLightning_OverloadAsCoop()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Bolt() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, -1f), new[] { Fire(), Fire() }, 2, npc: true, leader: player);
            Health enemy = Enemy(new Vector3(0f, 0f, 15f));
            AimAt(player, enemy.transform.position); // the player's target: the NPC sets it up for them
            ElementAuraModel auras = enemy.GetComponent<CombatStatus>().Elements;
            int npcId = CombatantRegistry.IdOf(npc.Character);

            yield return Until(() => auras.TryGet(Element.Fire, out ElementAura a) && a.ApplierId == npcId, 4f);
            Assert.That(auras.TryGet(Element.Fire, out ElementAura fire), Is.True, "the NPC applied fire");
            Assert.That(fire.ApplierId, Is.EqualTo(npcId));
            Assert.That(npc.Npc.LastSkillAction, Is.EqualTo(NpcActionKind.ComboSetup), "deliberately, for the player's lightning");

            AimAt(player, enemy.transform.position);
            player.Commands.Next.SetSkillPressed(0);
            yield return Frames(3);

            ComboReport overload = combos.FirstOrDefault(c => c.Definition.comboId == "overload");
            Assert.That(overload.Definition, Is.Not.Null, "overload fired");
            Assert.That(overload.IsCoop, Is.True);
            Assert.That(overload.Trigger.Applier, Is.SameAs(npc.Character));
            Assert.That(overload.Trigger.Completer, Is.SameAs(player.Character));
        }

        // ---------------------------------------------------------------- 9. the co-op bonus

        [UnityTest]
        public IEnumerator CoopCombo_DealsThirtyPercentMore_ThanTheSameComboSolo()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, 0f), new[] { HealCard() }, 2, npc: true, leader: player);
            yield return Frames(2);
            npc.Character.SetCommandSource(null); // cast by hand below: this test is about the numbers
            Health solo = Enemy(new Vector3(-12f, 0f, 14f), name: "Solo");
            Health coop = Enemy(new Vector3(12f, 0f, 14f), name: "Coop");
            yield return Frames(2);
            SkillCaster playerCaster = player.Character.GetComponent<SkillCaster>();
            SkillCaster npcCaster = npc.Character.GetComponent<SkillCaster>();

            AimAt(player, solo.transform.position);
            yield return Frames(1);
            playerCaster.CastCard(Ice());
            playerCaster.CastCard(Bolt());
            yield return Frames(1);

            AimAt(player, coop.transform.position);
            AimAt(npc, coop.transform.position);
            yield return Frames(1);
            playerCaster.CastCard(Ice());
            npcCaster.CastCard(Bolt());
            yield return Frames(1);

            float soloCombo = damage.Where(e => e.Target == solo && e.Info.Kind == DamageKind.Combo).Sum(e => e.Result.Damage);
            float coopCombo = damage.Where(e => e.Target == coop && e.Info.Kind == DamageKind.Combo).Sum(e => e.Result.Damage);
            Assert.That(soloCombo, Is.EqualTo(40f).Within(0.01f), "same caster: no bonus");
            Assert.That(coopCombo, Is.EqualTo(52f).Within(0.01f), "player + NPC: ×1.3");
            Assert.That(damage.Where(e => e.Target == coop && e.Info.Kind == DamageKind.Combo).All(e => (e.Info.Flags & DamageFlags.Coop) != 0), Is.True);
            Assert.That(combos.Count(c => c.IsCoop), Is.EqualTo(1));
            Assert.That(combos.Count(c => !c.IsCoop), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- 10. no runaway chains or double damage

        [UnityTest]
        public IEnumerator CoopCombo_NeverChains_AndHitsEachEnemyOnce_WhileTheNpcKeepsCasting()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Ice(), Ice(), Ice() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, -1f), new[] { Bolt(), Bolt(), Bolt() }, 2, npc: true, leader: player);
            Health enemy = Enemy(new Vector3(0f, 0f, 15f));
            AimAt(player, enemy.transform.position);
            player.Commands.Next.SetSkillPressed(0);
            yield return Until(() => combos.Count > 0, 2.5f);
            Assert.That(combos, Has.Count.EqualTo(1));

            // The player re-applies ice right away and the NPC keeps casting lightning: the 3 s limit holds.
            yield return Frames(15);
            AimAt(player, enemy.transform.position);
            player.Commands.Next.SetSkillPressed(1);
            yield return Seconds(1.8f);

            Assert.That(combos.Count(c => c.Target == enemy), Is.EqualTo(1), "same combo on the same enemy waits 3 s");
            Assert.That(damage.Count(e => e.Target == enemy && e.Info.Kind == DamageKind.Combo), Is.EqualTo(1), "one combo hit, never doubled");
            Assert.That(damage.Where(e => e.Info.Kind == DamageKind.Combo).All(e => (e.Info.Flags & DamageFlags.ComboDamage) != 0), Is.True);
        }

        // ---------------------------------------------------------------- 11. down and revive

        [UnityTest]
        public IEnumerator Npc_GoesDown_IsRevivedByThePlayer_AndRecoversAfterBleedingOut()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(1.5f, 0.05f, 0f), new[] { Plain() }, 2, npc: true, leader: player, maxHealth: 100f);
            yield return Frames(5);
            var down = new DamageInfo { Amount = 1000f, SourceTeam = TeamId.Enemies, Kind = DamageKind.Weapon };

            npc.Health.ApplyDamage(down);
            Assert.That(npc.Revivable.IsDowned, Is.True);
            Assert.That(npc.Npc.Mode, Is.EqualTo(NpcMode.Downed));
            Vector3 downedAt = npc.Character.Position;
            yield return Seconds(2f); // the player stands right next to it
            Assert.That(npc.Health.IsAlive, Is.True, "revived by standing close");
            Assert.That(npc.Health.Current, Is.EqualTo(45f).Within(0.5f), "with part of its health");
            Assert.That(FlatDistance(npc.Character.Position, downedAt), Is.LessThan(1.5f), "it did not move while down");

            player.Character.Motor.Teleport(new Vector3(0f, 0.05f, 20f));
            Physics.SyncTransforms();
            yield return Frames(30);
            npc.Health.ApplyDamage(down);
            Assert.That(npc.Revivable.IsDowned, Is.True);
            player.Character.Motor.Teleport(new Vector3(-20f, 0.05f, 20f)); // nobody nearby: it bleeds out
            Physics.SyncTransforms();
            yield return Until(() => npc.Health.IsAlive, 4f + npc.Npc.Profile.recoverDelay + 1.5f);

            Assert.That(npc.Health.IsAlive, Is.True, "back in the fight after bleeding out");
            Assert.That(npc.Health.Current, Is.EqualTo(50f).Within(0.5f));
            Assert.That(FlatDistance(npc.Character.Position, player.Character.Position), Is.LessThan(6f), "rejoins its leader");
        }

        [UnityTest]
        public IEnumerator DownedPlayer_IsRescuedByTheNpc()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            player.Revivable.Configure(1.5f, 15f, 2.4f, 0.45f);
            Body npc = CreateBody("Npc", new Vector3(12f, 0.05f, -6f), new[] { Plain() }, 2, npc: true, leader: player);
            yield return Frames(10);

            player.Health.ApplyDamage(new DamageInfo { Amount = 1000f, SourceTeam = TeamId.Enemies, Kind = DamageKind.Weapon });
            yield return Frames(10);
            Assert.That(npc.Npc.Mode, Is.EqualTo(NpcMode.Rescue), "priority 2: help the downed ally");

            yield return Until(() => player.Health.IsAlive, 8f);
            Assert.That(player.Health.IsAlive, Is.True, "the NPC came over and revived the player");
            Assert.That(player.Revivable.IsDowned, Is.False);
        }

        // ---------------------------------------------------------------- 13. the player's FPS and deck cycle are unchanged

        [UnityTest]
        public IEnumerator PlayerShootingAndDeckCycle_WorkAsBefore_WithTheNpcFighting()
        {
            Arena();
            Combos();
            CardDefinition card = Card("lance", Element.None, 50f, CardTargeting.Aim, SkillEffectSpec.Damage(5f));
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { card, card, card, card }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, -1f), new[] { Bolt(), Plain() }, 2, npc: true, leader: player);
            Health enemy = Enemy(new Vector3(0f, 0f, 14f));
            AimAt(player, enemy.transform.position);
            int reshuffles = 0;
            player.Deck.Deck.Reshuffled += n => reshuffles++;

            player.Commands.Next.FireHeld = true;
            player.Commands.Next.FirePressed = true;
            for (int round = 0; round < 6; round++)
            {
                for (int slot = 0; slot < 3; slot++)
                {
                    AimAt(player, enemy.transform.position);
                    player.Commands.Next.SetSkillPressed(slot);
                    yield return Frames(15);
                }
            }

            player.Commands.Next.FireHeld = false;
            Assert.That(DamageFrom(player.Character, enemy, DamageKind.Weapon), Is.GreaterThan(0f), "the player's rifle still hits");
            Assert.That(DamageFrom(player.Character, enemy, DamageKind.Skill), Is.GreaterThan(0f));
            Assert.That(reshuffles, Is.GreaterThan(0), "the player's deck cycled through and reshuffled");
            Assert.That(player.Deck.Deck.ActiveCardCount, Is.EqualTo(4));
            Assert.That(npc.Npc.ThinkCount, Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------- orders, dodging, controller swap, deck lock

        [UnityTest]
        public IEnumerator Orders_CycleTheTactic_AndMarkAFocusTarget_ThatTheNpcShoots()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, -1f), new[] { HealCard() }, 2, npc: true, leader: player);
            Health near = Enemy(new Vector3(-3f, 0f, 9f), name: "Near");
            Health far = Enemy(new Vector3(9f, 0f, 22f), name: "Far");
            yield return Frames(5);

            player.Commands.Next.OrderTacticPressed = true;
            yield return Frames(2);
            Assert.That(npc.Npc.Tactic, Is.EqualTo(NpcTactic.Focus));
            player.Commands.Next.OrderTacticPressed = true;
            yield return Frames(2);
            Assert.That(npc.Npc.Tactic, Is.EqualTo(NpcTactic.ComboFirst));
            player.Commands.Next.OrderTacticPressed = true;
            yield return Frames(2);
            Assert.That(npc.Npc.Tactic, Is.EqualTo(NpcTactic.Auto));

            AimAt(player, far.transform.position);
            player.Commands.Next.OrderMarkPressed = true;
            yield return Frames(2);
            Assert.That(npc.Npc.FocusTarget, Is.Not.Null);
            Assert.That(npc.Npc.FocusTarget.Health, Is.SameAs(far), "the enemy under the crosshair");
            Assert.That(npc.Npc.Tactic, Is.EqualTo(NpcTactic.Focus), "marking switches auto to focus");

            float before = DamageFrom(npc.Character, far, DamageKind.Weapon);
            yield return Seconds(3f);
            Assert.That(DamageFrom(npc.Character, far, DamageKind.Weapon), Is.GreaterThan(before), "it attacks the marked enemy");
            Assert.That(DamageFrom(npc.Character, far, DamageKind.Weapon), Is.GreaterThan(DamageFrom(npc.Character, near, DamageKind.Weapon)));
        }

        [UnityTest]
        public IEnumerator TelegraphedMeleeAttack_IsDodged()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(-5f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(0f, 0.05f, 0f), new[] { HealCard() }, 2, npc: true, leader: player, maxHealth: 30f);
            Health attacker = Enemy(new Vector3(0f, 0f, 1.8f), name: "Slasher");
            yield return Frames(10);
            Vector3 start = npc.Character.Position;
            float charges = npc.Character.Motor.DashCharge;

            CombatEvents.RaiseThreat(new ThreatWarning(attacker.GetComponent<TestEnemyActor>(), npc.Character, ThreatKind.Melee,
                attacker.transform.position, Vector3.back, 0.55f, 14f, 2.8f));
            yield return Seconds(0.55f);

            Assert.That(npc.Npc.EvadeCount, Is.EqualTo(1));
            Assert.That(npc.Character.Motor.DashCharge, Is.LessThan(charges), "used a dash");
            Assert.That(FlatDistance(npc.Character.Position, attacker.transform.position), Is.GreaterThan(2.8f), "out of reach when the swing lands");
            Assert.That(FlatDistance(npc.Character.Position, start), Is.GreaterThan(1.5f));
        }

        [UnityTest]
        public IEnumerator Controllers_HumanAndAi_DriveTheSameBody_AndCanBeSwapped()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(-5f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            Body npc = CreateBody("Npc", new Vector3(0f, 0.05f, 0f), new[] { HealCard() }, 2, npc: true, leader: player);
            Assert.That(npc.Character.ControllerKind, Is.EqualTo(ControllerKind.AI));

            var stand = new ScriptedCommands();
            stand.Next.Move = new Vector2(0f, 1f);
            npc.Character.SetCommandSource(stand); // a "remote human" takes the NPC slot
            Vector3 start = npc.Character.Position;
            yield return Seconds(0.5f);
            Assert.That(npc.Character.ControllerKind, Is.EqualTo(ControllerKind.Scripted));
            Assert.That(FlatDistance(npc.Character.Position, start), Is.GreaterThan(1.5f), "the same body obeys the new source");
            Assert.That(npc.Deck.Deck.HandSize, Is.EqualTo(2), "deck and health stay with the body");

            npc.Character.SetCommandSource(npc.Npc);
            yield return Frames(5);
            Assert.That(npc.Character.ControllerKind, Is.EqualTo(ControllerKind.AI));
            Assert.That(FpsCharacter.FindLocalHuman(), Is.SameAs(player.Character), "the HUD still finds the player");
        }

        [UnityTest]
        public IEnumerator DeckEdits_AreLockedInCombat_AndChangeTheNpcsStyleAfterwards()
        {
            Arena();
            Combos();
            Body player = CreateBody("Player", new Vector3(0f, 0.05f, 0f), new[] { Plain() }, 3, npc: false);
            CardDefinition bolt = Bolt();
            CardDefinition fire = Fire();
            var start = Track(ScriptableObject.CreateInstance<DeckDefinition>());
            start.cards = new List<CardDefinition> { bolt, bolt, bolt, bolt };
            start.handSize = 2;
            Body npc = CreateBody("Npc", new Vector3(3f, 0.05f, -1f), start.cards, 2, npc: true, leader: player);
            var fireDeck = Track(ScriptableObject.CreateInstance<DeckDefinition>());
            fireDeck.cards = new List<CardDefinition> { fire, fire, fire, fire };
            fireDeck.handSize = 2;
            var monitor = Track(new GameObject("Squad")).AddComponent<TeamEngagementMonitor>();
            DeckLoadoutController loadout = npc.Root.AddComponent<DeckLoadoutController>();
            loadout.Configure(npc.Deck, start,
                new List<DeckLoadoutController.OwnedCard> { new DeckLoadoutController.OwnedCard { card = fire, copies = 4 } },
                new List<DeckDefinition> { fireDeck }, monitor);

            Health enemy = Enemy(new Vector3(0f, 0f, 12f));
            yield return Frames(20);
            Assert.That(loadout.IsLocked, Is.True, "an enemy is close: in combat");
            Assert.That(loadout.TryAdd(fire), Is.EqualTo(LoadoutEditResult.Locked));

            Object.Destroy(enemy.gameObject);
            damage.Clear();
            yield return Seconds(5.5f); // the fight memory fades
            monitor.Refresh();
            Assert.That(loadout.IsLocked, Is.False);
            int rebuilds = npc.Deck.RebuildCount;
            Assert.That(loadout.TryRemoveAt(0), Is.EqualTo(LoadoutEditResult.BelowMinimum), "4 cards is the minimum");
            Assert.That(loadout.ApplyPreset(0), Is.EqualTo(LoadoutEditResult.Ok), "out of combat: edits apply");
            Assert.That(npc.Deck.RebuildCount, Is.GreaterThan(rebuilds), "the live deck was rebuilt from the list");
            Assert.That(npc.Deck.Deck.HandSize, Is.EqualTo(2));
            Assert.That(npc.Deck.Deck.AllCards().All(c => c.Definition == fire), Is.True, "now a fire deck");

            // New style: with only fire cards, the NPC starts combos with fire instead of finishing with lightning.
            Health next = Enemy(new Vector3(-2f, 0f, 14f));
            yield return Until(() => npc.Npc.LastSkill == fire, 6f);
            Assert.That(npc.Npc.LastSkill, Is.SameAs(fire));
            Assert.That(next.GetComponent<CombatStatus>().Elements.Has(Element.Fire) || combos.Count > 0, Is.True);
        }
    }
}
