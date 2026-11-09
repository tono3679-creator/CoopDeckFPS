using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Enemies;
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
    /// The Thunderhop's discharge on screen (Docs/LIGHTNING_VFX.md): the first enemy bursts on the discharge frame and the
    /// chained enemy later, as its arc arrives (its hit flash moved there too), while every damage still lands on the
    /// discharge frame; a chain target gone before its beat still shows where it was; two players discharging at once
    /// each show their own discharge once and only the shooter's own view jolts; no floor ring under an enemy in the air.
    /// </summary>
    public class DischargeFxPlayModeTests
    {
        const float Dt = 1f / 60f;
        const string ThunderhopPath = "Assets/_Game/ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset";
        const string Hit = "FX_Lightning_Hit";
        const string ChainHit = "FX_Lightning_Hit_Chain";

        readonly List<Object> spawned = new List<Object>();
        readonly List<DamageEvent> damage = new List<DamageEvent>();
        VfxQuality previousQuality;

        sealed class FixedAim : IAimProvider
        {
            public Ray Ray;
            public Ray GetAimRay() => Ray;
            public void AddRecoil(Vector2 kickDegrees) { }
        }

        sealed class Shooter
        {
            public HitscanWeapon Weapon;
            public WeaponFx Fx;
            public CameraShaker View;
            public int Shots;
            public int Discharges;
        }

        [SetUp]
        public void SetUp()
        {
            CombatEvents.DamageApplied += OnDamage;
            previousQuality = PerformanceSettings.Vfx;
            PerformanceSettings.SetVfxQuality(VfxQuality.Medium); // the Ally X default: floor ring on, no light
        }

        [TearDown]
        public void TearDown()
        {
            CombatEvents.DamageApplied -= OnDamage;
            PerformanceSettings.SetVfxQuality(previousQuality);
            foreach (Object o in spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            spawned.Clear();
            damage.Clear();
        }

        void OnDamage(DamageEvent e) => damage.Add(e);

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        WeaponDefinition Thunderhop()
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(ThunderhopPath);
#else
            WeaponDefinition asset = null;
#endif
            Assert.That(asset, Is.Not.Null, ThunderhopPath);
            WeaponDefinition copy = Track(Object.Instantiate(asset));
            copy.baseSpread = copy.spreadPerShot = copy.maxBloom = copy.movingSpread = copy.airborneSpread = 0f; // every bullet lands
            return copy;
        }

        void Floor()
        {
            GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            floor.transform.position = new Vector3(0f, -0.5f, 8f);
            floor.transform.localScale = new Vector3(30f, 1f, 30f);
        }

        Health Enemy(Vector3 position)
        {
            var root = Track(new GameObject("DischargeTarget"));
            root.transform.position = position;
            var health = root.AddComponent<Health>();
            health.Team = TeamId.Enemies;
            health.SetMaxHealth(100000f, true);
            root.AddComponent<CombatStatus>();
            root.AddComponent<ForceReceiver>();
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.8f, 2f, 0.8f);
            body.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);
            root.AddComponent<HitFlash>();
            Physics.SyncTransforms();
            return health;
        }

        Shooter CreateShooter(WeaponDefinition definition, Vector3 eye, Vector3 aimAt)
        {
            var go = Track(new GameObject("Shooter"));
            go.transform.position = eye;
            var shooter = new Shooter { Weapon = go.AddComponent<HitscanWeapon>() };
            var aim = new FixedAim { Ray = new Ray(eye, (aimAt - eye).normalized) };
            shooter.Weapon.SetDefinition(definition);
            shooter.Weapon.Initialize(go.AddComponent<TestPlayerActor>(), aim, TeamId.Players, Physics.DefaultRaycastLayers);
            shooter.Weapon.SetRandomSeed(42);
            shooter.Weapon.Fired += _ => shooter.Shots++;
            shooter.Weapon.ChargeBurstTriggered += _ => shooter.Discharges++;
            var view = new GameObject("View");
            view.transform.SetParent(go.transform, false);
            shooter.View = view.AddComponent<CameraShaker>();
            shooter.Fx = go.AddComponent<WeaponFx>();
            return shooter;
        }

        static void FireShots(Shooter shooter, int shots)
        {
            int target = shooter.Shots + shots;
            int guard = 0;
            while (shooter.Shots < target && guard++ < 100000)
            {
                shooter.Weapon.SetTrigger(true, false);
                shooter.Weapon.Tick(Dt);
            }

            shooter.Weapon.SetTrigger(false, false);
            shooter.Weapon.Tick(0f);
        }

        static LightningImpactFx[] Impacts(string name) =>
            Object.FindObjectsByType<LightningImpactFx>(FindObjectsInactive.Exclude).Where(i => i.name == name).ToArray();

        float ProcDamageTo(Health target) =>
            damage.Where(e => e.Target == target && (e.Info.Flags & DamageFlags.WeaponProc) != 0).Sum(e => e.Result.Damage);

        /// <summary>Pooled hits from an earlier test go back to the pool first (their lifetime is 0.5 s).</summary>
        static IEnumerator Drain()
        {
            yield return new WaitForSeconds(0.6f);
        }

        // ------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator FirstEnemyBurstsNow_ChainedEnemyWhenTheArcArrives_DamageAllOnTheDischargeFrame()
        {
            yield return Drain();
            Floor();
            Health first = Enemy(new Vector3(0f, 0f, 8f));
            Health second = Enemy(new Vector3(2.5f, 0f, 9f));
            Shooter shooter = CreateShooter(Thunderhop(), new Vector3(0f, 1f, 0f), first.transform.position + Vector3.up);
            yield return null;

            FireShots(shooter, 12);
            float t0 = Time.time;
            Assert.That(shooter.Discharges, Is.EqualTo(1));
            Assert.That(ProcDamageTo(second), Is.GreaterThan(0f), "the chained damage lands on the discharge frame");
            Assert.That(Impacts(Hit).Length, Is.EqualTo(1), "beat 1 on the discharge frame");
            Assert.That(Impacts(ChainHit).Length, Is.EqualTo(0), "the chained enemy waits for its beat");
            Assert.That(shooter.Fx.PendingHops, Is.EqualTo(1));
            Assert.That(Impacts(Hit)[0].GroundRingShown, Is.True, "a floor ring under a standing enemy");
            Assert.That(Impacts(Hit)[0].GroundRing.position.y, Is.EqualTo(0.03f).Within(0.05f));
            Assert.That(Impacts(Hit)[0].GetComponentInChildren<AudioSource>().isPlaying, Is.True, "the burst sound on the impact frame");

            var firstFlash = first.GetComponent<HitFlash>();
            var secondFlash = second.GetComponent<HitFlash>();
            Assert.That(firstFlash.Flashing, Is.True);
            Assert.That(firstFlash.FlashStrength, Is.EqualTo(0.5f).Within(1e-4f), "softened under the burst");
            Assert.That(secondFlash.Flashing, Is.False, "not two bodies white at once");
            Assert.That(secondFlash.FlashPending, Is.True);

            DischargeTiming timing = shooter.Fx.Timing;
            Assert.That(timing.ArcStart(0), Is.InRange(0.065f, 0.085f));
            while (Time.time - t0 < timing.ArcStart(0) - 0.01f)
            {
                Assert.That(Impacts(ChainHit).Length, Is.EqualTo(0));
                Assert.That(secondFlash.Flashing, Is.False);
                yield return null;
            }

            bool burst = false;
            bool flashed = false;
            float limit = t0 + timing.Arrival(0) + 0.15f;
            while (Time.time < limit && !(burst && flashed))
            {
                burst |= Impacts(ChainHit).Length == 1;
                flashed |= secondFlash.Flashing;
                yield return null;
            }

            Assert.That(burst, Is.True, "the chained enemy bursts as the arc arrives");
            Assert.That(Impacts(ChainHit)[0].GetComponentInChildren<AudioSource>().isPlaying, Is.True, "its zap with it");
            Assert.That(flashed, Is.True, "and lights up then");
            Assert.That(secondFlash.FlashStrength, Is.EqualTo(0.45f).Within(1e-4f));
            Assert.That(shooter.Fx.PendingHops, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ChainTargetGoneBeforeItsBeat_StillShowsWhereItWas()
        {
            yield return Drain();
            Floor();
            Health first = Enemy(new Vector3(0f, 0f, 8f));
            Health second = Enemy(new Vector3(2.5f, 0f, 9f));
            Vector3 where = second.transform.position + Vector3.up;
            Shooter shooter = CreateShooter(Thunderhop(), new Vector3(0f, 1f, 0f), first.transform.position + Vector3.up);
            shooter.Weapon.ChargeBurstTriggered += report =>
            {
                if (report.Chained.Count > 0 && report.Chained[0] != null)
                {
                    Object.Destroy(report.Chained[0].gameObject);
                }
            };
            yield return null;

            FireShots(shooter, 12);
            float limit = Time.time + shooter.Fx.Timing.Arrival(0) + 0.15f;
            LightningImpactFx chained = null;
            while (Time.time < limit && chained == null)
            {
                chained = Impacts(ChainHit).FirstOrDefault();
                yield return null;
            }

            Assert.That(second == null, Is.True, "the target is gone");
            Assert.That(chained, Is.Not.Null, "its beat still plays");
            Assert.That(Vector3.Distance(chained.transform.position, where), Is.LessThan(0.8f), "where it was");
        }

        [UnityTest]
        public IEnumerator TwoPlayersDischargingAtOnce_EachShowsItsOwnOnce_AndBothViewsJolt()
        {
            yield return Drain();
            Floor();
            Health first = Enemy(new Vector3(0f, 0f, 8f));
            Enemy(new Vector3(2.5f, 0f, 9f));
            WeaponDefinition thunderhop = Thunderhop();
            Shooter a = CreateShooter(thunderhop, new Vector3(-1f, 1f, 0f), first.transform.position + Vector3.up);
            Shooter b = CreateShooter(thunderhop, new Vector3(1f, 1f, 0f), first.transform.position + Vector3.up);
            yield return null;

            FireShots(a, 12);
            FireShots(b, 12);
            float t0 = Time.time;
            Assert.That(a.Discharges, Is.EqualTo(1));
            Assert.That(b.Discharges, Is.EqualTo(1));
            Assert.That(Impacts(Hit).Length, Is.EqualTo(2), "one first-strike burst per discharge");
            Assert.That(a.Fx.PendingHops, Is.EqualTo(1), "each weapon shows its own discharge once");
            Assert.That(b.Fx.PendingHops, Is.EqualTo(1));

            int most = 0;
            float kickA = 0f;
            float kickB = 0f;
            while (Time.time < t0 + a.Fx.Timing.Arrival(0) + 0.15f)
            {
                most = Mathf.Max(most, Impacts(ChainHit).Length);
                kickA = Mathf.Max(kickA, a.View.KickAmount);
                kickB = Mathf.Max(kickB, b.View.KickAmount);
                yield return null;
            }

            Assert.That(most, Is.EqualTo(2), "one chained burst per discharge, not doubled");
            Assert.That(kickA, Is.GreaterThan(0f));
            Assert.That(kickB, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator OnlyTheDischargingPlayersViewJolts()
        {
            yield return Drain();
            Floor();
            Health first = Enemy(new Vector3(0f, 0f, 8f));
            WeaponDefinition thunderhop = Thunderhop();
            Shooter shooting = CreateShooter(thunderhop, new Vector3(-1f, 1f, 0f), first.transform.position + Vector3.up);
            Shooter watching = CreateShooter(thunderhop, new Vector3(1f, 1f, 0f), first.transform.position + Vector3.up);
            yield return null;

            FireShots(shooting, 12);
            Assert.That(shooting.Discharges, Is.EqualTo(1));
            float kickShooting = 0f;
            float kickWatching = 0f;
            float t0 = Time.time;
            while (Time.time < t0 + 0.12f)
            {
                kickShooting = Mathf.Max(kickShooting, shooting.View.KickAmount);
                kickWatching = Mathf.Max(kickWatching, watching.View.KickAmount);
                yield return null;
            }

            Assert.That(kickShooting, Is.GreaterThan(0f));
            Assert.That(kickWatching, Is.EqualTo(0f), "another player's view is untouched");
        }

        [UnityTest]
        public IEnumerator EnemyInTheAir_GetsNoFloorRing()
        {
            yield return Drain();
            Floor();
            Health first = Enemy(new Vector3(0f, 0f, 8f));
            Enemy(new Vector3(2.5f, 3f, 9f)); // hovering 3 m up
            Shooter shooter = CreateShooter(Thunderhop(), new Vector3(0f, 1f, 0f), first.transform.position + Vector3.up);
            yield return null;

            FireShots(shooter, 12);
            Assert.That(Impacts(Hit)[0].GroundRingShown, Is.True);
            float limit = Time.time + shooter.Fx.Timing.Arrival(0) + 0.15f;
            LightningImpactFx chained = null;
            while (Time.time < limit && chained == null)
            {
                chained = Impacts(ChainHit).FirstOrDefault();
                yield return null;
            }

            Assert.That(chained, Is.Not.Null);
            Assert.That(chained.GroundRingShown, Is.False, "the chest burst carries a hit in the air");
        }
    }
}
