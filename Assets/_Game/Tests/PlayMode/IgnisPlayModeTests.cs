using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// The fox's IGNIS-27 Blazetail in play (Docs/IGNIS27_RIFLE.md): the 火種刻印 ignition on the 6th consecutive hit
    /// (22 on the target, 12 to at most 4 others within 2.5 m, the fire element into the existing combo system and the
    /// existing burn), no re-stacking from the ignition, the splash or the burn, no endless chains, skill gauges
    /// untouched, overload with lightning and the burning singularity with gravity through the existing combos and their
    /// 3 s cooldown, the fire rate / magazine / reload, and the balance against the VOLT-9 and the old rifle.
    /// </summary>
    public class IgnisPlayModeTests
    {
        const float Dt = 1f / 60f;
        const string IgnisPath = "Assets/_Game/ScriptableObjects/Weapons/WD_Ignis27Blazetail.asset";
        const string ThunderhopPath = "Assets/_Game/ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset";
        const string RiflePath = "Assets/_Game/ScriptableObjects/Weapons/WD_AssaultRifle.asset";
        const string OverloadPath = "Assets/_Game/ScriptableObjects/Combos/CB_Overload.asset";
        const string SingularityPath = "Assets/_Game/ScriptableObjects/Combos/CB_BurningSingularity.asset";

        readonly List<Object> spawned = new List<Object>();
        readonly List<DamageEvent> damage = new List<DamageEvent>();
        readonly List<ElementHit> elementHits = new List<ElementHit>();
        readonly List<ComboReport> combos = new List<ComboReport>();

        sealed class FixedAim : IAimProvider
        {
            public Ray Ray;
            public Ray GetAimRay() => Ray;
            public void AddRecoil(Vector2 kickDegrees) { }
            public void SetRecoilRecovery(float seconds) { }
        }

        sealed class Shooter
        {
            public TestPlayerActor Actor;
            public HitscanWeapon Weapon;
            public int Shots;
            public int Ignitions;
            public ChargeBurstReport Last;
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

        static T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
#else
            return null;
#endif
        }

        /// <summary>The real asset, optionally without spread so every bullet lands (exact counts).</summary>
        WeaponDefinition Weapon(string path, bool noSpread)
        {
            var asset = Load<WeaponDefinition>(path);
            if (!noSpread)
            {
                return asset;
            }

            WeaponDefinition copy = Track(Object.Instantiate(asset));
            copy.baseSpread = copy.spreadPerShot = copy.maxBloom = copy.movingSpread = copy.airborneSpread = 0f;
            return copy;
        }

        Health Enemy(Vector3 position, float maxHealth = 100000f, string name = "IgnisTarget")
        {
            var root = Track(new GameObject(name));
            root.transform.position = position;
            var health = root.AddComponent<Health>();
            health.Team = TeamId.Enemies;
            health.SetMaxHealth(maxHealth, true);
            root.AddComponent<CombatStatus>();
            root.AddComponent<ForceReceiver>();
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.6f, 2f, 0.6f);
            body.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);
            Physics.SyncTransforms();
            return health;
        }

        Shooter CreateShooter(WeaponDefinition definition, Vector3 eye, Vector3 aimAt)
        {
            var go = Track(new GameObject("IgnisShooter"));
            go.transform.position = eye;
            var shooter = new Shooter { Actor = go.AddComponent<TestPlayerActor>(), Weapon = go.AddComponent<HitscanWeapon>() };
            var aim = new FixedAim { Ray = new Ray(eye, (aimAt - eye).normalized) };
            shooter.Weapon.SetDefinition(definition);
            shooter.Weapon.Initialize(shooter.Actor, aim, TeamId.Players, Physics.DefaultRaycastLayers);
            shooter.Weapon.SetRandomSeed(27);
            shooter.Weapon.Fired += _ => shooter.Shots++;
            shooter.Weapon.ChargeBurstTriggered += r =>
            {
                shooter.Ignitions++;
                shooter.Last = r;
            };
            return shooter;
        }

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

        static void FireFor(Shooter shooter, float seconds, float dt = Dt)
        {
            for (float t = 0f; t < seconds; t += dt)
            {
                shooter.Weapon.SetTrigger(true, false);
                shooter.Weapon.Tick(dt);
            }

            shooter.Weapon.SetTrigger(false, false);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        float DamageTo(Health target, System.Func<DamageEvent, bool> filter = null)
        {
            float sum = 0f;
            foreach (DamageEvent e in damage)
            {
                if (e.Target == target && (filter == null || filter(e)))
                {
                    sum += e.Result.Damage;
                }
            }

            return sum;
        }

        static bool IsProc(DamageEvent e) => (e.Info.Flags & DamageFlags.WeaponProc) != 0;
        static bool IsBurn(DamageEvent e) => e.Info.Kind == DamageKind.Status && e.Info.Element == Element.Fire;

        int Count(System.Func<DamageEvent, bool> filter)
        {
            int n = 0;
            foreach (DamageEvent e in damage)
            {
                if (filter(e)) n++;
            }

            return n;
        }

        ComboSystem CreateComboSystem(params string[] paths)
        {
            var system = Track(new GameObject("ComboSystem")).AddComponent<ComboSystem>();
            var list = new List<ComboDefinition>();
            foreach (string p in paths)
            {
                list.Add(Load<ComboDefinition>(p));
            }

            system.Configure(list);
            return system;
        }

        // ------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator SixHits_Ignite_TargetPlusSplash_FireAndBurn_SkillGaugesUntouched()
        {
            yield return null;
            CreateComboSystem(); // keeps the element auras, no combo rules
            Health primary = Enemy(new Vector3(0f, 0f, 8f));
            Health near1 = Enemy(new Vector3(1.4f, 0f, 8.5f));   // within 2.5 m of the hit
            Health near2 = Enemy(new Vector3(-1.6f, 0f, 9f));
            Health far = Enemy(new Vector3(4f, 0f, 9f));         // outside the 2.5 m splash
            Shooter shooter = CreateShooter(Weapon(IgnisPath, true), new Vector3(0f, 1f, 0f), primary.transform.position + Vector3.up);

            var card = Track(ScriptableObject.CreateInstance<CardDefinition>());
            card.maxGauge = 100f;
            card.gaugeCost = 25f;
            card.effects = new List<SkillEffectSpec>();
            var deck = shooter.Actor.gameObject.AddComponent<DeckController>();
            deck.Rebuild(new[] { card, card, card, card }, 3, new CoopDeck.Items.GaugeItemDefinition[0], 3);

            FireShots(shooter, 5);
            Assert.That(shooter.Ignitions, Is.EqualTo(0));
            Assert.That(shooter.Weapon.ChargeBurst.Stacks, Is.EqualTo(5));
            FireShots(shooter, 1);
            Assert.That(shooter.Ignitions, Is.EqualTo(1), "the 6th consecutive hit ignites");
            Assert.That(DamageTo(primary), Is.EqualTo(6 * 18f + 22f).Within(0.01f), "6 bullets (no falloff at 8 m) + 22");
            Assert.That(DamageTo(near1), Is.EqualTo(12f).Within(0.01f), "12 to each enemy within 2.5 m");
            Assert.That(DamageTo(near2), Is.EqualTo(12f).Within(0.01f));
            Assert.That(DamageTo(far), Is.EqualTo(0f), "beyond 2.5 m: nothing");
            Assert.That(Count(IsProc), Is.EqualTo(3), "the target + two splash hits");
            Assert.That(shooter.Last.Chained.Count, Is.EqualTo(2), "the report lists the splash targets (for the effects)");

            Assert.That(elementHits.Count, Is.EqualTo(3), "one fire element hit per target, into the combo system");
            Assert.That(elementHits.TrueForAll(h => h.Element == Element.Fire && ReferenceEquals(h.Source, shooter.Actor)), Is.True);
            foreach (Health h in new[] { primary, near1, near2 })
            {
                var status = h.GetComponent<CombatStatus>();
                Assert.That(status.Elements.Has(Element.Fire), Is.True, "the existing aura holds the fire");
                Assert.That(status.Model.HasDamageOverTime(Element.Fire), Is.True, "the existing burn ticks");
            }

            Assert.That(far.GetComponent<CombatStatus>().Model.HasDamageOverTime(Element.Fire), Is.False);

            yield return new WaitForSeconds(4.6f); // the 4 s burns run out (CombatStatus ticks them every frame)
            float burned = DamageTo(primary, IsBurn);
            Assert.That(burned, Is.EqualTo(6f * 4f).Within(0.01f), "the existing burn: 6/s for 4 s (8 ticks of 3)");
            Assert.That(primary.GetComponent<CombatStatus>().Model.HasDamageOverTime(Element.Fire), Is.False, "and it ends");
            Assert.That(shooter.Weapon.ChargeBurst.Stacks, Is.EqualTo(0), "burn ticks add no stacks");
            Assert.That(shooter.Ignitions, Is.EqualTo(1), "nothing re-ignites on its own");

            for (int slot = 0; slot < 3; slot++)
            {
                Assert.That(deck.Deck.GetHandCard(slot).Gauge, Is.EqualTo(100f), "gunshots and the ignition never use skill gauges");
            }
        }

        [UnityTest]
        public IEnumerator Splash_HitsAtMostFourEnemies_NearestFirst()
        {
            yield return null;
            Health primary = Enemy(new Vector3(0f, 0f, 8f));
            var near = new List<Health>();
            for (int i = 0; i < 6; i++) // in a row to the side, 1.0 .. 2.25 m from the target: all within the 2.5 m splash
            {
                near.Add(Enemy(new Vector3(1f + i * 0.25f, 0f, 8f)));
            }

            Shooter shooter = CreateShooter(Weapon(IgnisPath, true), new Vector3(0f, 1f, -1f), primary.transform.position + Vector3.up);
            FireShots(shooter, 6);
            Assert.That(shooter.Ignitions, Is.EqualTo(1));
            int splashed = 0;
            for (int i = 0; i < near.Count; i++)
            {
                if (DamageTo(near[i]) > 0f) splashed++;
            }

            Assert.That(splashed, Is.EqualTo(4), "6 enemies in range, the splash takes 4");
            Assert.That(DamageTo(near[0]), Is.GreaterThan(0f), "the nearest are taken first");
            Assert.That(DamageTo(near[5]), Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator Ignition_NeverBuildsStacks_NorChainsOnItsOwn()
        {
            yield return null;
            Health primary = Enemy(new Vector3(0f, 0f, 8f));
            Health neighbour = Enemy(new Vector3(1.5f, 0f, 8f));
            Shooter shooter = CreateShooter(Weapon(IgnisPath, true), new Vector3(0f, 1f, 0f), primary.transform.position + Vector3.up);

            FireShots(shooter, 6);
            Assert.That(shooter.Ignitions, Is.EqualTo(1));
            Assert.That(shooter.Weapon.ChargeBurst.Stacks, Is.EqualTo(0), "the ignition's own damage adds no stacks");
            Assert.That(shooter.Weapon.ChargeBurst.Target, Is.SameAs(primary), "the splash did not move the brand to the neighbour");
            int procs = Count(IsProc);
            Assert.That(procs, Is.EqualTo(2), "the target + the neighbour, nothing more");

            // Time passes (the burns tick on both): no ignition and no new proc happens on its own.
            for (int i = 0; i < 300; i++)
            {
                shooter.Weapon.Tick(Dt);
                yield return null;
            }

            Assert.That(shooter.Ignitions, Is.EqualTo(1));
            Assert.That(Count(IsProc), Is.EqualTo(procs));

            // Aim at the neighbour: the splash gave it no stacks, so 5 hits are not enough.
            Shooter second = CreateShooter(Weapon(IgnisPath, true), new Vector3(1.5f, 1f, 0f), neighbour.transform.position + Vector3.up);
            FireShots(second, 5);
            Assert.That(second.Ignitions, Is.EqualTo(0));
            FireShots(second, 1);
            Assert.That(second.Ignitions, Is.EqualTo(1), "it takes six direct hits like any other target");
        }

        [UnityTest]
        public IEnumerator Ignition_CompletesOverloadAndBurningSingularity_WithTheExistingComboRules()
        {
            yield return null;
            var overload = Load<ComboDefinition>(OverloadPath);
            var singularity = Load<ComboDefinition>(SingularityPath);
            CreateComboSystem(OverloadPath, SingularityPath);
            var ally = Track(new GameObject("Ally")).AddComponent<TestPlayerActor>();
            int allyId = CombatantRegistry.IdOf(ally);

            // Overload: the ally's lightning on A, the ignition's fire completes it.
            Health a = Enemy(new Vector3(0f, 0f, 8f));
            CombatEvents.RaiseElementHit(new ElementHit(a, Element.Lightning, ally, allyId, TeamId.Players, a.transform.position, 0));
            Shooter shooter = CreateShooter(Weapon(IgnisPath, true), new Vector3(0f, 1f, 0f), a.transform.position + Vector3.up);
            FireShots(shooter, 6);
            yield return null;
            Assert.That(shooter.Ignitions, Is.EqualTo(1));
            Assert.That(combos.Count, Is.EqualTo(1), "lightning + the ignition's fire = overload");
            Assert.That(combos[0].Definition, Is.SameAs(overload));
            Assert.That(combos[0].IsCoop, Is.True, "the ally primed the lightning");
            Assert.That(shooter.Weapon.ChargeBurst.Stacks, Is.EqualTo(0), "combo damage adds no stacks");

            // The 3 s combo cooldown: lightning on A again, then fire reaches A within 3 s through the splash of an
            // ignition on B next to it (A's own 4 s brand cooldown would be too long to test this).
            Health b = Enemy(new Vector3(30f, 0f, 8f));
            a.transform.position = new Vector3(31.5f, 0f, 8f);
            Physics.SyncTransforms();
            CombatEvents.RaiseElementHit(new ElementHit(a, Element.Lightning, ally, allyId, TeamId.Players, a.transform.position, 0));
            Shooter second = CreateShooter(Weapon(IgnisPath, true), new Vector3(30f, 1f, 0f), b.transform.position + Vector3.up);
            FireShots(second, 6);
            yield return null;
            Assert.That(second.Ignitions, Is.EqualTo(1));
            Assert.That(second.Last.Chained.Contains(a), Is.True, "the splash reached A with fire");
            Assert.That(combos.Count, Is.EqualTo(1), "overload's 3 s cooldown on A still applies");

            // Burning singularity: the ally's gravity on C, the ignition's fire completes it.
            Health c = Enemy(new Vector3(-30f, 0f, 8f));
            CombatEvents.RaiseElementHit(new ElementHit(c, Element.Gravity, ally, allyId, TeamId.Players, c.transform.position, 0));
            Shooter third = CreateShooter(Weapon(IgnisPath, true), new Vector3(-30f, 1f, 0f), c.transform.position + Vector3.up);
            FireShots(third, 6);
            yield return null;
            Assert.That(third.Ignitions, Is.EqualTo(1));
            Assert.That(combos.Count, Is.EqualTo(2), "gravity + the ignition's fire = burning singularity");
            Assert.That(combos[1].Definition, Is.SameAs(singularity));
        }

        [UnityTest]
        public IEnumerator FullAuto_MagazineAndReload_FollowTheData()
        {
            yield return null;
            Health target = Enemy(new Vector3(0f, 0f, 8f));
            Shooter shooter = CreateShooter(Weapon(IgnisPath, true), new Vector3(0f, 1f, 0f), target.transform.position + Vector3.up);

            FireFor(shooter, 1f);
            Assert.That(shooter.Shots, Is.InRange(8, 9), "480 rpm: 8 rounds a second, holding the trigger (full auto)");

            FireShots(shooter, 28 - shooter.Shots);
            Assert.That(shooter.Weapon.State.Ammo, Is.EqualTo(0));
            for (int i = 0; i < 20 && !shooter.Weapon.State.IsReloading; i++)
            {
                shooter.Weapon.Tick(Dt);
            }

            Assert.That(shooter.Weapon.State.IsReloading, Is.True, "an empty magazine reloads by itself");
            float reload = 0f;
            while (shooter.Weapon.State.IsReloading && reload < 5f)
            {
                shooter.Weapon.Tick(Dt);
                reload += Dt;
            }

            Assert.That(reload, Is.EqualTo(2.1f).Within(0.05f));
            Assert.That(shooter.Weapon.State.Ammo, Is.EqualTo(28));
        }

        /// <summary>
        /// Balance against the VOLT-9 and the old rifle: 10 s of fire per weapon (reloads included, no spread, one
        /// target), at distances from 8 to 70 m, plus a group of four enemies at 12 m, and the time to kill the two
        /// enemy types. The numbers go to the log for Docs/IGNIS27_RIFLE.md §5; the asserts pin the intended roles.
        /// </summary>
        [UnityTest]
        public IEnumerator Balance_AgainstVolt9AndTheOldRifle()
        {
            yield return null;
            var log = new System.Text.StringBuilder("IGNIS-27 vs VOLT-9 vs プロトAR (10 s of fire, reloads included, no spread):\n");
            var dps = new Dictionary<(string, float), float>();
            foreach (float distance in new[] { 8f, 17f, 30f, 50f, 70f })
            {
                foreach ((string name, string path) in new[] { ("IGNIS", IgnisPath), ("VOLT", ThunderhopPath), ("AR", RiflePath) })
                {
                    damage.Clear();
                    float dealt = SingleTarget(path, distance, 10f, out int shots, out int procs, out float burn);
                    dps[(name, distance)] = dealt / 10f;
                    log.Append($"  {distance:0} m {name}: {dealt / 10f:0.0} DPS ({shots} rounds, {procs} procs, burn {burn:0})\n");
                }
            }

            foreach ((string name, string path) in new[] { ("IGNIS", IgnisPath), ("VOLT", ThunderhopPath), ("AR", RiflePath) })
            {
                damage.Clear();
                float group = Group(path, 12f, 10f, out int procs);
                log.Append($"  group of 4 at 12 m, {name}: {group / 10f:0.0} DPS over all four ({procs} procs)\n");
                dps[(name + "group", 12f)] = group / 10f;
            }

            foreach ((string enemy, float hp) in new[] { ("shooter", 90f), ("slasher", 140f) })
            {
                foreach (float distance in new[] { 8f, 30f })
                {
                    log.Append($"  time to kill a {enemy} ({hp:0}) at {distance:0} m: ");
                    foreach ((string name, string path) in new[] { ("IGNIS", IgnisPath), ("VOLT", ThunderhopPath), ("AR", RiflePath) })
                    {
                        log.Append($"{name} {TimeToKill(path, distance, hp):0.00} s  ");
                    }

                    log.Append('\n');
                }
            }

            // Handling: hit rate with the real spread (no recoil here: FixedAim), holding the trigger for 3 s, or in
            // bursts of 6 with a 0.4 s pause (the brand's count), on a 0.6 x 2 m target.
            foreach (float distance in new[] { 17f, 30f })
            {
                log.Append($"  hit rate at {distance:0} m (spread on, 3 s): ");
                foreach ((string name, string path) in new[] { ("IGNIS", IgnisPath), ("VOLT", ThunderhopPath), ("AR", RiflePath) })
                {
                    log.Append($"{name} hold {HitRate(path, distance, 0):P0} bursts {HitRate(path, distance, 6):P0}  ");
                }

                log.Append('\n');
            }

            Debug.Log(log.ToString());
            // Roles from the design: the VOLT-9 leads up close, the IGNIS-27 holds on past the VOLT-9's falloff and range.
            Assert.That(dps[("VOLT", 8f)], Is.GreaterThan(dps[("IGNIS", 8f)]), "up close the SMG out-damages the rifle");
            Assert.That(dps[("IGNIS", 30f)], Is.GreaterThan(dps[("VOLT", 30f)]), "at 30 m the rifle is ahead");
            Assert.That(dps[("VOLT", 70f)], Is.EqualTo(0f), "70 m is beyond the SMG's 55 m range");
            Assert.That(dps[("IGNIS", 70f)], Is.GreaterThan(0f), "and within the rifle's 85 m");
            Assert.That(dps[("IGNISgroup", 12f)], Is.GreaterThan(dps[("IGNIS", 8f)]), "the splash pays off on a group");
        }

        /// <summary>Share of rounds that land on a 0.6 x 2 m target with the weapon's real spread; burst 0 = hold the trigger.</summary>
        float HitRate(string path, float distance, int burst)
        {
            damage.Clear();
            Health target = Enemy(new Vector3(0f, 0f, distance));
            Shooter shooter = CreateShooter(Weapon(path, false), new Vector3(0f, 1f, 0f), target.transform.position + Vector3.up);
            int hits = 0;
            shooter.Weapon.Fired += e => { if (e.HitDamageable) hits++; };
            float pause = 0f;
            int inBurst = 0;
            for (float t = 0f; t < 3f; t += Dt)
            {
                bool hold = pause <= 0f;
                int before = shooter.Shots;
                shooter.Weapon.SetTrigger(hold, false);
                shooter.Weapon.Tick(Dt);
                pause -= Dt;
                if (burst > 0 && shooter.Shots > before && ++inBurst >= burst)
                {
                    inBurst = 0;
                    pause = 0.4f;
                }
            }

            shooter.Weapon.SetTrigger(false, false);
            int shots = shooter.Shots;
            target.gameObject.SetActive(false);
            shooter.Actor.gameObject.SetActive(false);
            return shots > 0 ? (float)hits / shots : 0f;
        }

        float SingleTarget(string path, float distance, float seconds, out int shots, out int procs, out float burn)
        {
            Health target = Enemy(new Vector3(0f, 0f, distance));
            Shooter shooter = CreateShooter(Weapon(path, true), new Vector3(0f, 1f, 0f), target.transform.position + Vector3.up);
            float burned = SimulateWithStatus(shooter, seconds, target);
            shots = shooter.Shots;
            procs = shooter.Ignitions;
            burn = burned;
            float dealt = DamageTo(target);
            target.gameObject.SetActive(false);
            shooter.Actor.gameObject.SetActive(false);
            return dealt;
        }

        float Group(string path, float distance, float seconds, out int procs)
        {
            var targets = new List<Health>();
            Health primary = Enemy(new Vector3(0f, 0f, distance));
            targets.Add(primary);
            targets.Add(Enemy(new Vector3(1.4f, 0f, distance + 0.5f)));
            targets.Add(Enemy(new Vector3(-1.4f, 0f, distance + 0.5f)));
            targets.Add(Enemy(new Vector3(0f, 0f, distance + 1.8f)));
            Shooter shooter = CreateShooter(Weapon(path, true), new Vector3(0f, 1f, 0f), primary.transform.position + Vector3.up);
            SimulateWithStatus(shooter, seconds, primary);
            procs = shooter.Ignitions;
            float dealt = 0f;
            foreach (Health t in targets)
            {
                dealt += DamageTo(t);
                t.gameObject.SetActive(false);
            }

            shooter.Actor.gameObject.SetActive(false);
            return dealt;
        }

        /// <summary>Fires for <paramref name="seconds"/> and ticks the targets' damage over time in step (no frames needed).</summary>
        float SimulateWithStatus(Shooter shooter, float seconds, Health watch)
        {
            var statuses = Object.FindObjectsByType<CombatStatus>();
            var ticks = new List<DotTick>();
            for (float t = 0f; t < seconds; t += Dt)
            {
                shooter.Weapon.SetTrigger(true, false);
                shooter.Weapon.Tick(Dt);
                foreach (CombatStatus s in statuses)
                {
                    if (s == null || !s.isActiveAndEnabled) continue;
                    ticks.Clear();
                    s.Model.Tick(Dt, ticks); // the same burn rules CombatStatus.Update applies each frame
                    foreach (DotTick tick in ticks)
                    {
                        s.GetComponent<Health>().ApplyDamage(new DamageInfo
                        {
                            Amount = tick.Amount, Element = tick.Element, Kind = DamageKind.Status, Source = tick.Source, SourceTeam = tick.SourceTeam,
                        });
                    }
                }
            }

            shooter.Weapon.SetTrigger(false, false);
            return DamageTo(watch, IsBurn);
        }

        float TimeToKill(string path, float distance, float hp)
        {
            damage.Clear();
            Health target = Enemy(new Vector3(0f, 0f, distance), hp);
            Shooter shooter = CreateShooter(Weapon(path, true), new Vector3(0f, 1f, 0f), target.transform.position + Vector3.up);
            float t = 0f;
            while (target.IsAlive && t < 20f)
            {
                shooter.Weapon.SetTrigger(true, false);
                shooter.Weapon.Tick(Dt);
                t += Dt;
            }

            shooter.Weapon.SetTrigger(false, false);
            target.gameObject.SetActive(false);
            shooter.Actor.gameObject.SetActive(false);
            return t;
        }
    }
}
