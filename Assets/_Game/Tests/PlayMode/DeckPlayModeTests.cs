using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.Input;
using CoopDeck.Items;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Deck × FPS integration: skill slots from commands and from real input devices, skills and
    /// shooting side by side, the full card cycle in play, gauge items, and each shared effect.
    /// </summary>
    public class DeckPlayModeTests
    {
        const float Dt = 1f / 60f;
        readonly List<Object> spawned = new List<Object>();
        readonly List<InputDevice> devices = new List<InputDevice>();

        sealed class ScriptedCommands : ICharacterCommandSource
        {
            public CharacterCommands Next;

            public CharacterCommands GetCommands(float deltaTime)
            {
                CharacterCommands c = Next;
                Next.FirePressed = false;
                Next.SkillPressedMask = 0;
                Next.UseItemPressed = false;
                Next.CycleItemPressed = false;
                Next.CardSelectRequest = 0;
                Next.CardSelectStep = 0;
                return c;
            }
        }

        sealed class Rig
        {
            public GameObject Root;
            public FpsCharacter Character;
            public DeckController Deck;
            public SkillCaster Caster;
            public HitscanWeapon Weapon;
            public Health Health;
            public CombatStatus Status;
            public ScriptedCommands Commands;
        }

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (InputDevice device in devices)
            {
                if (device.added)
                {
                    InputSystem.RemoveDevice(device);
                }
            }

            devices.Clear();
            foreach (Object o in spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            spawned.Clear();
        }

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        // ---------------------------------------------------------------- helpers

        CardDefinition Card(string id, float cost, CardTargeting targeting, params SkillEffectSpec[] effects)
        {
            var card = Track(ScriptableObject.CreateInstance<CardDefinition>());
            card.cardId = id;
            card.displayName = id;
            card.maxGauge = 100f;
            card.gaugeCost = cost;
            card.cooldown = 0f;
            card.targeting = targeting;
            card.range = 60f;
            card.effects = new List<SkillEffectSpec>(effects);
            return card;
        }

        CardDefinition Lance(float cost = 25f) => Card("lance", cost, CardTargeting.Aim, SkillEffectSpec.Damage(30f));

        GaugeItemDefinition Item(string id, GaugeItemTarget target, float amount)
        {
            var item = Track(ScriptableObject.CreateInstance<GaugeItemDefinition>());
            item.itemId = id;
            item.displayName = id;
            item.target = target;
            item.amount = amount;
            item.maxCarry = 2;
            item.startCount = 2;
            return item;
        }

        void Ground()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(80f, 1f, 80f);
            Physics.SyncTransforms();
        }

        Health Target(Vector3 position, float maxHealth = 1000f)
        {
            var root = Track(new GameObject("Target"));
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

        Rig CreateRig(IEnumerable<CardDefinition> cards, IEnumerable<GaugeItemDefinition> items = null, bool realInput = false)
        {
            var rig = new Rig();
            rig.Root = Track(new GameObject("TestPlayer"));
            rig.Root.layer = GameLayers.Player >= 0 ? GameLayers.Player : 0;
            var controller = rig.Root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            rig.Root.AddComponent<FpsMotor>();
            rig.Health = rig.Root.AddComponent<Health>();
            rig.Health.Team = TeamId.Players;
            rig.Status = rig.Root.AddComponent<CombatStatus>();

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(rig.Root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1f, 0f);
            var look = rig.Root.AddComponent<FpsLook>();
            look.Configure(rig.Root.transform, pivot.transform, null);

            rig.Weapon = rig.Root.AddComponent<HitscanWeapon>();
            var weaponDef = Track(ScriptableObject.CreateInstance<WeaponDefinition>());
            weaponDef.damage = 10f;
            weaponDef.baseSpread = 0f;
            weaponDef.spreadPerShot = 0f;
            weaponDef.movingSpread = 0f;
            weaponDef.airborneSpread = 0f;
            weaponDef.recoilPitch = 0f;
            weaponDef.recoilYaw = 0f;
            rig.Weapon.SetDefinition(weaponDef);

            rig.Caster = rig.Root.AddComponent<SkillCaster>();
            if (realInput)
            {
                rig.Root.AddComponent<PlayerInputReader>();
            }

            rig.Character = rig.Root.AddComponent<FpsCharacter>();
            rig.Deck = rig.Root.AddComponent<DeckController>();
            rig.Deck.Rebuild(cards, 3, items, 1234);

            if (!realInput)
            {
                rig.Commands = new ScriptedCommands();
                rig.Character.CommandSource = rig.Commands;
            }

            return rig;
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        // ---------------------------------------------------------------- tests

        [UnityTest]
        public IEnumerator SkillSlotCommands_UseTheMatchingCard_SpendItsGauge_AndHitTheTarget()
        {
            Ground();
            Health target = Target(new Vector3(0f, 0f, 10f));
            Rig rig = CreateRig(Enumerable.Repeat(Lance(), 12));
            yield return Frames(3);

            for (int slot = 0; slot < 3; slot++)
            {
                float before = target.Current;
                CardRuntime card = rig.Deck.Deck.GetHandCard(slot);
                rig.Commands.Next.SetSkillPressed(slot);
                yield return null;
                Assert.That(card.Gauge, Is.EqualTo(75f), $"slot {slot} spent its own cost");
                Assert.That(target.Current, Is.EqualTo(before - 30f).Within(1e-3f), $"slot {slot} skill hit");
                Assert.That(rig.Deck.SelectedSlot, Is.EqualTo(slot), "selection follows the last card used");
            }
        }

        [UnityTest]
        public IEnumerator ShootingAndSkills_RunTogether_WithoutTouchingEachOther()
        {
            Ground();
            Health target = Target(new Vector3(0f, 0f, 10f));
            Rig rig = CreateRig(Enumerable.Repeat(Lance(), 12));
            yield return Frames(3);

            // Shooting alone never spends gauge.
            rig.Commands.Next.FireHeld = true;
            rig.Commands.Next.FirePressed = true;
            yield return Frames(30);
            Assert.That(rig.Weapon.State.Ammo, Is.LessThan(30));
            for (int slot = 0; slot < 3; slot++)
            {
                Assert.That(rig.Deck.Deck.GetHandCard(slot).Gauge, Is.EqualTo(100f));
            }

            // Skill while the trigger is held: both happen in the same frame.
            int ammoBefore = rig.Weapon.State.Ammo;
            float hpBefore = target.Current;
            rig.Commands.Next.SetSkillPressed(1);
            yield return Frames(7);
            Assert.That(rig.Deck.Deck.GetHandCard(1).Gauge, Is.EqualTo(75f), "exactly one skill use");
            Assert.That(rig.Weapon.State.Ammo, Is.LessThan(ammoBefore), "the gun kept firing");
            Assert.That(hpBefore - target.Current, Is.GreaterThanOrEqualTo(30f + 10f), "skill and bullets both landed");

            // A skill alone does not consume ammo or start a reload.
            rig.Commands.Next.FireHeld = false;
            yield return Frames(15);
            int ammo = rig.Weapon.State.Ammo;
            bool reloading = rig.Weapon.IsReloading;
            rig.Commands.Next.SetSkillPressed(2);
            yield return null;
            Assert.That(rig.Weapon.State.Ammo, Is.EqualTo(ammo));
            Assert.That(rig.Weapon.IsReloading, Is.EqualTo(reloading));
        }

        [UnityTest]
        public IEnumerator UsingCardsUp_CyclesTheWholeDeck_AndReshuffles_WithoutLosingCards()
        {
            Ground();
            Target(new Vector3(0f, 0f, 10f), 100000f);
            var defs = new List<CardDefinition>();
            for (int i = 0; i < 12; i++)
            {
                defs.Add(Card($"once_{i}", 100f, CardTargeting.Aim, SkillEffectSpec.Damage(5f)));
            }

            Rig rig = CreateRig(defs);
            DeckState deck = rig.Deck.Deck;
            int reshuffles = 0;
            int depleted = 0;
            deck.Reshuffled += _ => reshuffles++;
            deck.CardDepleted += (s, c) => depleted++;
            yield return Frames(3);

            for (int press = 0; press < 15; press++)
            {
                rig.Commands.Next.SetSkillPressed(press % 3);
                yield return null;
                Assert.That(deck.ActiveCardCount, Is.EqualTo(12));
                Assert.That(deck.HandCount, Is.EqualTo(3), "an emptied slot is refilled at once");
            }

            Assert.That(depleted, Is.EqualTo(15));
            Assert.That(reshuffles, Is.GreaterThanOrEqualTo(1), "15 one-shot cards from a 12-card deck must reshuffle");
        }

        [UnityTest]
        public IEnumerator ItemAndSelectionCommands_RestoreGauge()
        {
            Ground();
            Target(new Vector3(0f, 0f, 10f), 100000f);
            GaugeItemDefinition potion = Item("potion", GaugeItemTarget.SelectedCard, 40f);
            GaugeItemDefinition crystal = Item("crystal", GaugeItemTarget.AllHandCards, 25f);
            Rig rig = CreateRig(Enumerable.Repeat(Lance(50f), 12), new[] { potion, crystal });
            yield return Frames(3);
            DeckState deck = rig.Deck.Deck;

            rig.Commands.Next.SetSkillPressed(1); // slot 1 → 50, and becomes the selection
            yield return null;
            rig.Commands.Next.UseItemPressed = true; // potion on slot 1
            yield return null;
            Assert.That(deck.GetHandCard(1).Gauge, Is.EqualTo(90f));
            Assert.That(rig.Deck.Items.GetCount(potion), Is.EqualTo(1));

            rig.Commands.Next.SetSkillPressed(0);
            yield return null;
            rig.Commands.Next.SetSkillPressed(2);
            yield return null;
            rig.Commands.Next.CycleItemPressed = true; // → crystal
            yield return null;
            Assert.That(rig.Deck.Items.Selected, Is.SameAs(crystal));
            rig.Commands.Next.UseItemPressed = true;
            yield return null;
            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(75f));
            Assert.That(deck.GetHandCard(1).Gauge, Is.EqualTo(100f), "90 + 25 is capped at 100");
            Assert.That(deck.GetHandCard(2).Gauge, Is.EqualTo(75f));

            rig.Commands.Next.RequestCardSelect(0);
            yield return null;
            Assert.That(rig.Deck.SelectedSlot, Is.EqualTo(0));
            rig.Commands.Next.CardSelectStep = -1;
            yield return null;
            Assert.That(rig.Deck.SelectedSlot, Is.EqualTo(2), "selection wraps around");
        }

        // ---------------------------------------------------------------- real devices

        IEnumerator Press(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        IEnumerator Press(Gamepad gamepad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button));
            yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardAndGamepad_DriveSkillsItemsSelectionAndShooting()
        {
            // An unfocused Editor (or batch mode) would otherwise drop keyboard events and disable
            // devices in the background. Use isolated settings for this test only.
            InputSettings previousSettings = InputSystem.settings;
            var settings = ScriptableObject.CreateInstance<InputSettings>();
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = settings;
            try
            {
                yield return KeyboardAndGamepadScenario();
            }
            finally
            {
                InputSystem.settings = previousSettings;
                Object.Destroy(settings);
            }
        }

        IEnumerator KeyboardAndGamepadScenario()
        {
            Ground();
            Target(new Vector3(0f, 0f, 10f), 100000f);
            GaugeItemDefinition potion = Item("potion", GaugeItemTarget.SelectedCard, 40f);
            GaugeItemDefinition crystal = Item("crystal", GaugeItemTarget.AllHandCards, 25f);
            var keyboard = InputSystem.AddDevice<Keyboard>("TestKeyboard");
            var gamepad = InputSystem.AddDevice<Gamepad>("TestGamepad");
            devices.Add(keyboard);
            devices.Add(gamepad);

            Rig rig = CreateRig(Enumerable.Repeat(Lance(10f), 12), new[] { potion, crystal }, realInput: true);
            yield return Frames(3);
            DeckState deck = rig.Deck.Deck;

            // Keyboard Q / E / F → slots 1-3.
            Key[] keys = { Key.Q, Key.E, Key.F };
            for (int slot = 0; slot < 3; slot++)
            {
                yield return Press(keyboard, keys[slot]);
                Assert.That(deck.GetHandCard(slot).Gauge, Is.EqualTo(90f), $"keyboard {keys[slot]}");
            }

            // Gamepad LB / RB / Y → slots 1-3.
            GamepadButton[] buttons = { GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.North };
            for (int slot = 0; slot < 3; slot++)
            {
                yield return Press(gamepad, buttons[slot]);
                Assert.That(deck.GetHandCard(slot).Gauge, Is.EqualTo(80f), $"gamepad {buttons[slot]}");
            }

            // Card selection: keys 1/2/3 and D-pad left/right.
            yield return Press(keyboard, Key.Digit1);
            Assert.That(rig.Deck.SelectedSlot, Is.EqualTo(0));
            yield return Press(gamepad, GamepadButton.DpadRight);
            Assert.That(rig.Deck.SelectedSlot, Is.EqualTo(1));
            yield return Press(gamepad, GamepadButton.DpadLeft);
            yield return Press(gamepad, GamepadButton.DpadLeft);
            Assert.That(rig.Deck.SelectedSlot, Is.EqualTo(2));

            // Items: G (keyboard) and D-pad up (gamepad) use, D-pad down / T cycle.
            yield return Press(keyboard, Key.G);
            Assert.That(deck.GetHandCard(2).Gauge, Is.EqualTo(100f), "potion on the selected card");
            yield return Press(gamepad, GamepadButton.DpadDown);
            Assert.That(rig.Deck.Items.Selected, Is.SameAs(crystal));
            yield return Press(gamepad, GamepadButton.DpadUp);
            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(100f), "crystal on the whole hand");
            yield return Press(keyboard, Key.T);
            Assert.That(rig.Deck.Items.Selected, Is.SameAs(potion));

            // Gamepad trigger still shoots, without touching any gauge.
            int ammo = rig.Weapon.State.Ammo;
            InputSystem.QueueStateEvent(gamepad, new GamepadState { rightTrigger = 1f });
            yield return Frames(10);
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return null;
            Assert.That(rig.Weapon.State.Ammo, Is.LessThan(ammo));
            Assert.That(deck.GetHandCard(0).Gauge, Is.EqualTo(100f));
        }

        // ---------------------------------------------------------------- effects

        IEnumerator ReadyRig(Rig rig)
        {
            yield return Frames(2);
            Assert.That(rig.Caster.IsInitialized, Is.True);
        }

        [UnityTest]
        public IEnumerator AreaDamage_HitsOnlyInsideTheRadius()
        {
            Ground();
            Health center = Target(new Vector3(0f, 0f, 10f));
            Health near = Target(new Vector3(3f, 0f, 10f));
            Health far = Target(new Vector3(9f, 0f, 10f));
            Rig rig = CreateRig(new[] { Lance() });
            yield return ReadyRig(rig);

            rig.Caster.CastCard(Card("vortex", 34f, CardTargeting.AimPoint, SkillEffectSpec.Area(30f, 4f)));
            Assert.That(center.Max - center.Current, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(near.Max - near.Current, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(far.Current, Is.EqualTo(far.Max));
        }

        [UnityTest]
        public IEnumerator DamageOverTime_TicksOverItsDuration()
        {
            Ground();
            Health target = Target(new Vector3(0f, 0f, 10f));
            Rig rig = CreateRig(new[] { Lance() });
            yield return ReadyRig(rig);

            rig.Caster.CastCard(Card("burn", 34f, CardTargeting.AimPoint, SkillEffectSpec.DamageOverTime(12f, 2f, 3f)));
            Assert.That(target.Current, Is.EqualTo(target.Max), "no instant damage");
            yield return Frames(150);
            Assert.That(target.Max - target.Current, Is.EqualTo(24f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator Heal_And_Shield_ProtectTheCaster()
        {
            Ground();
            Rig rig = CreateRig(new[] { Lance() });
            yield return ReadyRig(rig);

            rig.Health.ApplyDamage(new DamageInfo { Amount = 50f, SourceTeam = TeamId.Enemies });
            SkillCastReport heal = rig.Caster.CastCard(Card("heal", 50f, CardTargeting.AreaAroundSelf, SkillEffectSpec.Heal(45f, 8f)));
            Assert.That(rig.Health.Current, Is.EqualTo(95f).Within(1e-3f));
            Assert.That(heal.Healed, Is.EqualTo(45f).Within(1e-3f));

            rig.Caster.CastCard(Card("shield", 50f, CardTargeting.Self, SkillEffectSpec.Shield(60f, 8f)));
            DamageResult hit = rig.Health.ApplyDamage(new DamageInfo { Amount = 30f, SourceTeam = TeamId.Enemies });
            Assert.That(hit.Absorbed, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(rig.Health.Current, Is.EqualTo(95f).Within(1e-3f));
            Assert.That(rig.Status.Model.Shield, Is.EqualTo(30f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator AttackBuff_And_Marking_RaiseWeaponDamage()
        {
            Ground();
            Health target = Target(new Vector3(0f, 0f, 10f));
            Rig rig = CreateRig(new[] { Lance() });
            yield return ReadyRig(rig);

            rig.Caster.CastCard(Card("charge", 50f, CardTargeting.Self, SkillEffectSpec.Buff(0.3f, 8f)));
            float before = target.Current;
            rig.Commands.Next.FireHeld = true;
            rig.Commands.Next.FirePressed = true;
            yield return null;
            rig.Commands.Next.FireHeld = false;
            yield return Frames(10);
            Assert.That(before - target.Current, Is.EqualTo(13f).Within(1e-3f), "10 × 1.3");

            rig.Caster.CastCard(Card("mark", 34f, CardTargeting.Aim, SkillEffectSpec.Mark(0.3f, 8f)));
            before = target.Current;
            rig.Commands.Next.FireHeld = true;
            rig.Commands.Next.FirePressed = true;
            yield return null;
            rig.Commands.Next.FireHeld = false;
            yield return null;
            Assert.That(before - target.Current, Is.EqualTo(16.9f).Within(1e-3f), "10 × 1.3 buff × 1.3 mark");
        }

        [UnityTest]
        public IEnumerator Pull_DragsEnemiesTowardTheCentre()
        {
            Ground();
            Health left = Target(new Vector3(-5f, 0f, 10f));
            Health right = Target(new Vector3(5f, 0f, 10f));
            Rig rig = CreateRig(new[] { Lance() });
            yield return ReadyRig(rig);

            CardDefinition collapse = Card("collapse", 50f, CardTargeting.AimPoint, SkillEffectSpec.PullTo(8f, 7f, 1.2f));
            collapse.range = 10f; // aimed at open ground 10 m ahead
            rig.Caster.CastCard(collapse);
            yield return Frames(80);

            Assert.That(Mathf.Abs(left.transform.position.x), Is.LessThan(2f));
            Assert.That(Mathf.Abs(right.transform.position.x), Is.LessThan(2f));
        }

        [UnityTest]
        public IEnumerator ChainDamage_JumpsToNearbyEnemies()
        {
            Ground();
            Health first = Target(new Vector3(0f, 0f, 10f));
            Health second = Target(new Vector3(0f, 0f, 14f));
            Health third = Target(new Vector3(4f, 0f, 16f));
            Rig rig = CreateRig(new[] { Lance() });
            yield return ReadyRig(rig);

            SkillCastReport report = rig.Caster.CastCard(Card("chain", 40f, CardTargeting.Aim, SkillEffectSpec.Damage(40f, 3, 9f)));
            Assert.That(first.Max - first.Current, Is.EqualTo(40f).Within(1e-3f));
            Assert.That(second.Max - second.Current, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(third.Max - third.Current, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(report.EnemiesAffected, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator DelayedImpact_LandsAfterTheDelay()
        {
            Ground();
            Health target = Target(new Vector3(0f, 0f, 10f));
            Rig rig = CreateRig(new[] { Lance() });
            yield return ReadyRig(rig);

            CardDefinition meteor = Card("meteor", 100f, CardTargeting.AimPoint, SkillEffectSpec.Area(130f, 5f));
            meteor.impactDelay = 0.5f;
            SkillCastReport report = rig.Caster.CastCard(meteor);
            Assert.That(report.Delayed, Is.True);
            Assert.That(target.Current, Is.EqualTo(target.Max));
            yield return Frames(45);
            Assert.That(target.Max - target.Current, Is.EqualTo(130f).Within(1e-3f));
        }
    }
}
