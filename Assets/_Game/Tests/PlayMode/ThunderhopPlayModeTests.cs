using System.Collections;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoopDeck.Tests
{
    /// <summary>
    /// The VOLT-9 Thunderhop in play (Docs/THUNDERHOP_SMG.md): full-auto fire, magazine and reload,
    /// falloff, the 帯電連射 discharge (primary + one chained enemy, lightning element hits), no
    /// re-stacking or endless chains, the existing combo rules (aura, 3 s combo cooldown), skill
    /// gauges untouched, and the damage compared with the fox's rifle.
    /// </summary>
    public class ThunderhopPlayModeTests
    {
        const float Dt = 1f / 60f;
        const string ThunderhopPath = "Assets/_Game/ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset";
        const string RiflePath = "Assets/_Game/ScriptableObjects/Weapons/WD_AssaultRifle.asset";

        readonly List<Object> spawned = new List<Object>();
        readonly List<DamageEvent> damage = new List<DamageEvent>();
        readonly List<ElementHit> elementHits = new List<ElementHit>();
        readonly List<ComboReport> combos = new List<ComboReport>();

        sealed class FixedAim : IAimProvider
        {
            public Ray Ray;
            public float RecoveryAsked = -1f;
            public Ray GetAimRay() => Ray;
            public void AddRecoil(Vector2 kickDegrees) { }
            public void SetRecoilRecovery(float seconds) => RecoveryAsked = seconds;
        }

        sealed class Shooter
        {
            public TestPlayerActor Actor;
            public HitscanWeapon Weapon;
            public FixedAim Aim;
            public int Shots;
            public int Discharges;
        }

        [SetUp]
        public void SetUp()
        {
            CombatEvents.DamageApplied += OnDamage;
            CombatEvents.ElementHitApplied += OnElementHit;
            ComboEvents.Triggered += OnCombo;
        }

        [TearDown]
        public void TearDown()
        {
            CombatEvents.DamageApplied -= OnDamage;
            CombatEvents.ElementHitApplied -= OnElementHit;
            ComboEvents.Triggered -= OnCombo;
            foreach (Object o in spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            spawned.Clear();
            damage.Clear();
            elementHits.Clear();
            combos.Clear();
        }

        void OnDamage(DamageEvent e) => damage.Add(e);
        void OnElementHit(ElementHit hit) => elementHits.Add(hit);
        void OnCombo(ComboReport report) => combos.Add(report);

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        static WeaponDefinition Load(string path)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
#else
            return null;
#endif
        }

        /// <summary>The real asset, optionally without spread so every bullet lands (exact counts).</summary>
        WeaponDefinition Weapon(string path, bool noSpread)
        {
            WeaponDefinition asset = Load(path);
            Assert.That(asset, Is.Not.Null, path);
            if (!noSpread)
            {
                return asset;
            }

            WeaponDefinition copy = Track(Object.Instantiate(asset));
            copy.baseSpread = copy.spreadPerShot = copy.maxBloom = copy.movingSpread = copy.airborneSpread = 0f;
            return copy;
        }

        Health Enemy(Vector3 position, float maxHealth = 100000f)
        {
            var root = Track(new GameObject("ThunderhopTarget"));
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

        Shooter CreateShooter(WeaponDefinition definition, Vector3 eye, Vector3 aimAt)
        {
            var go = Track(new GameObject("Shooter"));
            go.transform.position = eye;
            var shooter = new Shooter
            {
                Actor = go.AddComponent<TestPlayerActor>(),
                Weapon = go.AddComponent<HitscanWeapon>(),
                Aim = new FixedAim { Ray = new Ray(eye, (aimAt - eye).normalized) },
            };
            shooter.Weapon.SetDefinition(definition);
            shooter.Weapon.Initialize(shooter.Actor, shooter.Aim, TeamId.Players, Physics.DefaultRaycastLayers);
            shooter.Weapon.SetRandomSeed(42);
            shooter.Weapon.Fired += _ => shooter.Shots++;
            shooter.Weapon.ChargeBurstTriggered += _ => shooter.Discharges++;
            return shooter;
        }

        /// <summary>Holds the trigger until <paramref name="shots"/> rounds were fired (simulated time, one frame).</summary>
        static void FireShots(Shooter shooter, int shots, float dt = Dt)
        {
            int target = shooter.Shots + shots;
            int guard = 0;
            while (shooter.Shots < target && guard++ < 100000)
            {
                shooter.Weapon.SetTrigger(true, false);
                shooter.Weapon.Tick(dt);
            }

            shooter.Weapon.SetTrigger(false, false);
            shooter.Weapon.Tick(0f);
        }

        /// <summary>Holds the trigger for a duration (reloads included).</summary>
        static void FireFor(Shooter shooter, float seconds, float dt = Dt)
        {
            for (float t = 0f; t < seconds; t += dt)
            {
                shooter.Weapon.SetTrigger(true, false);
                shooter.Weapon.Tick(dt);
            }

            shooter.Weapon.SetTrigger(false, false);
        }

        /// <summary>The existing combo system (it also keeps the element auras) with one rule: ice + lightning.</summary>
        ComboDefinition CreateComboSystem()
        {
            var comboGo = Track(new GameObject("ComboSystem"));
            var system = comboGo.AddComponent<ComboSystem>();
            var superconductor = Track(ScriptableObject.CreateInstance<ComboDefinition>());
            superconductor.comboId = "superconductor";
            superconductor.displayName = "superconductor";
            superconductor.elementA = Element.Ice;
            superconductor.elementB = Element.Lightning;
            superconductor.kind = ComboKind.ChainBurst;
            superconductor.window = 4f;
            superconductor.cooldown = 3f;
            superconductor.coopMultiplier = 1.3f;
            var tier = new ComboTierSettings { damage = 40f, radius = 0f, chainCount = 3, chainRange = 9f, chainFalloff = 0.5f };
            superconductor.tiers = new[] { tier, tier, tier };
            system.Configure(new List<ComboDefinition> { superconductor });
            return superconductor;
        }

        float DamageTo(Health target, bool procOnly = false)
        {
            float sum = 0f;
            foreach (DamageEvent e in damage)
            {
                if (e.Target == target && (!procOnly || (e.Info.Flags & DamageFlags.WeaponProc) != 0))
                {
                    sum += e.Result.Damage;
                }
            }

            return sum;
        }

        int ProcEvents()
        {
            int count = 0;
            foreach (DamageEvent e in damage)
            {
                if ((e.Info.Flags & DamageFlags.WeaponProc) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator TwelveHits_Discharge_PrimaryPlusOneChain_LightningHits_SkillGaugesUntouched()
        {
            yield return null;
            CreateComboSystem(); // keeps the element auras (no ice here, so no combo)
            Health primary = Enemy(new Vector3(0f, 0f, 8f));
            Health near = Enemy(new Vector3(3f, 0f, 9f));   // within 6 m of the primary
            Health far = Enemy(new Vector3(-9f, 0f, 14f));  // outside the chain range
            Shooter shooter = CreateShooter(Weapon(ThunderhopPath, true), new Vector3(0f, 1f, 0f), primary.transform.position + Vector3.up);
            Assert.That(shooter.Aim.RecoveryAsked, Is.EqualTo(0.16f).Within(1e-4f), "the weapon's recoil recovery reaches the aim");

            // A skill deck next to the gun: shooting must not touch it.
            var card = Track(ScriptableObject.CreateInstance<CardDefinition>());
            card.maxGauge = 100f;
            card.gaugeCost = 25f;
            card.effects = new List<SkillEffectSpec>();
            var deck = shooter.Actor.gameObject.AddComponent<DeckController>();
            deck.Rebuild(new[] { card, card, card, card }, 3, new CoopDeck.Items.GaugeItemDefinition[0], 3);

            FireShots(shooter, 11);
            Assert.That(shooter.Discharges, Is.EqualTo(0));
            Assert.That(shooter.Weapon.ChargeBurst.Stacks, Is.EqualTo(11));

            FireShots(shooter, 1);
            Assert.That(shooter.Discharges, Is.EqualTo(1), "the 12th consecutive hit discharges");
            float bullet = shooter.Weapon.Definition.damage;
            Assert.That(DamageTo(primary), Is.EqualTo(12 * bullet + 18f).Within(0.01f), "12 bullets (no falloff at 8 m) + 18");
            Assert.That(DamageTo(primary, true), Is.EqualTo(18f).Within(0.01f));
            Assert.That(DamageTo(near), Is.EqualTo(12f).Within(0.01f), "one chained enemy within 6 m takes 12");
            Assert.That(DamageTo(far), Is.EqualTo(0f), "beyond 6 m: no chain");
            Assert.That(ProcEvents(), Is.EqualTo(2), "primary + one chain, nothing more");

            Assert.That(elementHits.Count, Is.EqualTo(2));
            Assert.That(elementHits.TrueForAll(h => h.Element == Element.Lightning && ReferenceEquals(h.Source, shooter.Actor)), Is.True);
            Assert.That(primary.GetComponent<CombatStatus>().Elements.Has(Element.Lightning), Is.True, "the existing aura system holds the lightning");
            Assert.That(near.GetComponent<CombatStatus>().Elements.Has(Element.Lightning), Is.True);

            for (int slot = 0; slot < 3; slot++)
            {
                Assert.That(deck.Deck.GetHandCard(slot).Gauge, Is.EqualTo(100f), "gunshots and the discharge never use skill gauges");
            }
        }

        [UnityTest]
        public IEnumerator Discharge_NeverBuildsStacks_NorChainsOnItsOwn()
        {
            yield return null;
            Health primary = Enemy(new Vector3(0f, 0f, 8f));
            Enemy(new Vector3(2f, 0f, 8f));
            Enemy(new Vector3(4f, 0f, 8f));
            Shooter shooter = CreateShooter(Weapon(ThunderhopPath, true), new Vector3(0f, 1f, 0f), primary.transform.position + Vector3.up);

            FireShots(shooter, 12);
            Assert.That(shooter.Discharges, Is.EqualTo(1));
            Assert.That(shooter.Weapon.ChargeBurst.Stacks, Is.EqualTo(0), "the discharge's own damage adds no stacks");
            Assert.That(ProcEvents(), Is.EqualTo(2), "one chain target only, no further hops");

            // Let time pass without shooting: nothing more happens on its own.
            for (int i = 0; i < 180; i++)
            {
                shooter.Weapon.Tick(Dt);
                yield return null;
            }

            Assert.That(shooter.Discharges, Is.EqualTo(1));
            Assert.That(ProcEvents(), Is.EqualTo(2));

            // 11 more hits: still not enough (the discharge did not count towards the next one).
            FireShots(shooter, 11);
            Assert.That(shooter.Discharges, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Discharge_CompletesAComboWithIce_AndTheComboCooldownHolds()
        {
            yield return null;
            ComboDefinition superconductor = CreateComboSystem();

            Health primary = Enemy(new Vector3(0f, 0f, 8f));
            Shooter shooter = CreateShooter(Weapon(ThunderhopPath, true), new Vector3(0f, 1f, 0f), primary.transform.position + Vector3.up);
            var iceCaster = Track(new GameObject("IceCaster")).AddComponent<TestPlayerActor>();

            CombatEvents.RaiseElementHit(new ElementHit(primary, Element.Ice, iceCaster, CombatantRegistry.IdOf(iceCaster), TeamId.Players, primary.transform.position, 0));
            FireShots(shooter, 12);
            Assert.That(shooter.Discharges, Is.EqualTo(1));
            Assert.That(combos.Count, Is.EqualTo(1), "ice + the discharge's lightning = superconductor");
            Assert.That(combos[0].Definition, Is.SameAs(superconductor));
            Assert.That(combos[0].IsCoop, Is.True, "another actor primed the ice");
            int stacksAfterCombo = shooter.Weapon.ChargeBurst.Stacks;
            Assert.That(stacksAfterCombo, Is.EqualTo(0), "combo damage adds no stacks either");

            // Prime again and discharge again within 3 s: the existing combo cooldown blocks a second combo.
            CombatEvents.RaiseElementHit(new ElementHit(primary, Element.Ice, iceCaster, CombatantRegistry.IdOf(iceCaster), TeamId.Players, primary.transform.position, 0));
            FireShots(shooter, 24); // 12 more stacks + the 1.25 s per-target wait
            Assert.That(shooter.Discharges, Is.EqualTo(2));
            Assert.That(combos.Count, Is.EqualTo(1), "the combo's 3 s cooldown still applies");
        }

        [UnityTest]
        public IEnumerator FullAuto_MagazineAndReload_FollowTheData()
        {
            yield return null;
            Health target = Enemy(new Vector3(0f, 0f, 8f));
            Shooter shooter = CreateShooter(Weapon(ThunderhopPath, true), new Vector3(0f, 1f, 0f), target.transform.position + Vector3.up);

            FireFor(shooter, 1f);
            Assert.That(shooter.Shots, Is.InRange(15, 16), "900 rpm: 15 rounds a second, holding the trigger (full auto)");

            FireShots(shooter, 40 - shooter.Shots);
            Assert.That(shooter.Weapon.State.Ammo, Is.EqualTo(0));
            for (int i = 0; i < 10 && !shooter.Weapon.State.IsReloading; i++)
            {
                shooter.Weapon.Tick(Dt); // the last round's cadence ends first
            }

            Assert.That(shooter.Weapon.State.IsReloading, Is.True, "an empty magazine reloads by itself");
            float reload = 0f;
            while (shooter.Weapon.State.IsReloading && reload < 5f)
            {
                shooter.Weapon.Tick(Dt);
                reload += Dt;
            }

            Assert.That(reload, Is.EqualTo(1.65f).Within(0.05f));
            Assert.That(shooter.Weapon.State.Ammo, Is.EqualTo(40));
        }

        [UnityTest]
        public IEnumerator Thunderhop_ComparedWithTheFoxRifle()
        {
            yield return null;
            var results = new System.Text.StringBuilder("Thunderhop vs fox rifle (6 s of fire, reloads included, no spread):\n");
            foreach (float distance in new[] { 8f, 17f, 30f })
            {
                float smg = Sustained(ThunderhopPath, distance, out int smgShots, out int smgProcs);
                damage.Clear();
                float rifle = Sustained(RiflePath, distance, out int rifleShots, out _);
                damage.Clear();
                results.Append($"  {distance:0} m: SMG {smg / 6f:0.0} DPS ({smgShots} rounds, {smgProcs} discharges)  rifle {rifle / 6f:0.0} DPS ({rifleShots} rounds)\n");

                Assert.That(smgShots, Is.GreaterThan(rifleShots), "the SMG fires faster");
                if (distance >= 24f)
                {
                    Assert.That(smg, Is.LessThan(rifle), "past its falloff the SMG falls behind");
                }
            }

            Debug.Log(results.ToString());
        }

        float Sustained(string path, float distance, out int shots, out int discharges)
        {
            Health target = Enemy(new Vector3(0f, 0f, distance));
            Shooter shooter = CreateShooter(Weapon(path, true), new Vector3(0f, 1f, 0f), target.transform.position + Vector3.up);
            FireFor(shooter, 6f);
            shots = shooter.Shots;
            discharges = shooter.Discharges;
            float dealt = DamageTo(target);
            target.gameObject.SetActive(false);
            shooter.Actor.gameObject.SetActive(false);
            return dealt;
        }
    }
}
