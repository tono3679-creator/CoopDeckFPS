#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Skills;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The generated fireball card (run the bootstrap first) cast through a real SkillCaster: it charges, launches
    /// after the cast delay, flies, and its existing effects resolve where it lands - on an enemy (direct hit,
    /// splash, burn, fire element hit, combo with lightning, flames clinging to the target), on a wall (the
    /// blast turned to the wall, no damage), through allies, or nowhere (out of range). Pooling stays constant
    /// and optional layers follow the VFX quality.
    /// </summary>
    public class FireballPlayModeTests
    {
        const float Dt = 1f / 60f;
        const string CardPath = "Assets/_Game/ScriptableObjects/Cards/CD_Fireball.asset";
        const string BoltPath = "Assets/_Game/ScriptableObjects/Cards/CD_Thunderbolt.asset";
        const string OverloadPath = "Assets/_Game/ScriptableObjects/Combos/CB_Overload.asset";
        const string BurningPath = "Assets/_Game/Prefabs/Skills/Fireball/FX_Burning.prefab";

        readonly List<Object> spawned = new List<Object>();
        readonly List<DamageEvent> damage = new List<DamageEvent>();
        readonly List<ElementHit> elementHits = new List<ElementHit>();
        readonly List<ProjectileImpact> landed = new List<ProjectileImpact>();
        int launched;

        sealed class FixedAim : IAimProvider
        {
            public Ray Ray;
            public Ray GetAimRay() => Ray;
            public void AddRecoil(Vector2 kickDegrees) { }
        }

        CardDefinition fireball;
        SkillCaster caster;
        FixedAim aim;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
            CombatEvents.DamageApplied += OnDamage;
            CombatEvents.ElementHitApplied += OnElementHit;
            fireball = AssetDatabase.LoadAssetAtPath<CardDefinition>(CardPath);
            Assert.That(fireball, Is.Not.Null, CardPath + " missing: run the bootstrap");
            ReleaseLiveEffects(); // the pool outlives a test: blasts or burns still playing from the previous one
        }

        static void ReleaseLiveEffects()
        {
            foreach (PooledFx fx in Object.FindObjectsByType<PooledFx>(FindObjectsInactive.Exclude))
            {
                fx.ReleaseNow();
            }
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            CombatEvents.DamageApplied -= OnDamage;
            CombatEvents.ElementHitApplied -= OnElementHit;
            PerformanceSettings.ResetVfxQuality();
            foreach (Object o in spawned.Where(o => o != null))
            {
                Object.Destroy(o);
            }

            spawned.Clear();
            damage.Clear();
            elementHits.Clear();
            landed.Clear();
            launched = 0;
        }

        void OnDamage(DamageEvent e) => damage.Add(e);
        void OnElementHit(ElementHit hit) => elementHits.Add(hit);

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        void Ground()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(120f, 1f, 120f);
            Physics.SyncTransforms();
        }

        Health Character(string name, Vector3 position, TeamId team)
        {
            var root = Track(new GameObject(name));
            root.transform.position = position;
            var health = root.AddComponent<Health>();
            health.Team = team;
            health.SetMaxHealth(1000f, true);
            root.AddComponent<CombatStatus>();
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);
            Physics.SyncTransforms();
            return health;
        }

        void Caster()
        {
            var go = Track(new GameObject("FireballCaster"));
            go.AddComponent<Health>();
            var actor = go.AddComponent<TestPlayerActor>();
            caster = go.AddComponent<SkillCaster>();
            var hand = new GameObject("Hand").transform;
            hand.SetParent(go.transform, false);
            hand.localPosition = new Vector3(-0.25f, 1.3f, 0.35f);
            caster.SetCastHand(hand);
            aim = new FixedAim();
            caster.Initialize(actor, aim, Physics.DefaultRaycastLayers, GameLayers.SkillQueryMask);
            caster.ProjectileLaunched += (card, point) => launched++;
            caster.ProjectileEnded += impact => landed.Add(impact);
        }

        void AimAt(Vector3 point)
        {
            Vector3 eye = caster.transform.position + Vector3.up * 1.6f;
            aim.Ray = new Ray(eye, (point - eye).normalized);
        }

        IEnumerator UntilLanded(float timeout = 3f)
        {
            float t = 0f;
            while (landed.Count == 0 && t < timeout)
            {
                t += Dt;
                yield return null;
            }

            Assert.That(landed.Count, Is.EqualTo(1), "the fireball ended");
        }

        static IEnumerable<T> Live<T>() where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);

        [UnityTest]
        public IEnumerator Fireball_FliesToTheEnemy_HitsSplashesAndBurns_ThroughTheUsualEffects()
        {
            Ground();
            Caster();
            Health target = Character("Target", new Vector3(0f, 0f, 18f), TeamId.Enemies);
            Health neighbour = Character("Neighbour", new Vector3(1.6f, 0f, 18.6f), TeamId.Enemies);
            AimAt(target.transform.position + Vector3.up * 1.1f);

            SkillCastReport report = caster.CastCard(fireball);
            Assert.That(report.Delayed, Is.True);
            Assert.That(caster.PendingCount, Is.EqualTo(1), "charging in the hand");
            Assert.That(Live<PooledFx>().Any(f => f.name == "FX_Fireball_Cast"), Is.True, "the charge plays at once");
            yield return null;
            Assert.That(launched, Is.Zero, "the cast motion has not brought the palm forward yet");

            for (int i = 0; i < 8; i++)
            {
                yield return null;
            }

            Assert.That(launched, Is.EqualTo(1), "launched at about 0.12 s");
            Assert.That(caster.FlyingCount, Is.EqualTo(1));
            Assert.That(damage, Is.Empty, "nothing happens before it arrives");
            FireballProjectileFx visual = Live<FireballProjectileFx>().Single();

            yield return UntilLanded();
            ProjectileImpact impact = landed[0];
            Assert.That(impact.Hit, Is.True);
            Assert.That(impact.Target, Is.EqualTo(target));
            Assert.That(impact.TimeInFlight, Is.EqualTo(17.5f / 45f).Within(0.08f), "flies at 45 m/s");
            Assert.That(visual.IsEnding, Is.True, "the flying fireball hands over to the blast");

            float onTarget = damage.Where(d => d.Target == target).Sum(d => d.Result.Damage);
            float onNeighbour = damage.Where(d => d.Target == neighbour).Sum(d => d.Result.Damage);
            Assert.That(onTarget, Is.EqualTo(65f).Within(0.01f), "45 direct + 20 splash");
            Assert.That(onNeighbour, Is.EqualTo(20f).Within(0.01f), "splash within 2.5 m");
            Assert.That(damage.All(d => d.Info.Kind == DamageKind.Skill && d.Info.Element == Element.Fire), Is.True);
            Assert.That(target.GetComponent<CombatStatus>().Model.HasDamageOverTime(Element.Fire), Is.True, "burning");
            Assert.That(neighbour.GetComponent<CombatStatus>().Model.HasDamageOverTime(Element.Fire), Is.True);
            Assert.That(elementHits.Select(h => (h.Target, h.Element)), Is.EquivalentTo(new[] { (target, Element.Fire), (neighbour, Element.Fire) }),
                "one fire element hit per enemy: combos see it like any skill");

            FireballExplosionFx blast = Live<FireballExplosionFx>().Single();
            Assert.That(Vector3.Distance(blast.transform.position, impact.Point), Is.LessThan(0.01f), "the blast is where it hit");
            Assert.That(blast.OnCharacter, Is.True);
            Assert.That(Vector3.Angle(blast.transform.up, Vector3.back), Is.LessThan(1f), "on a character it faces back toward the shot (a vertical ring)");
            Assert.That(blast.Radius, Is.EqualTo(2.5f));
            PooledFx[] flares = Live<PooledFx>().Where(f => f.name == "FX_Fireball_TargetFlare").ToArray();
            Assert.That(flares.Select(f => f.transform.parent).ToArray(), Is.EquivalentTo(new[] { target.transform, neighbour.transform }),
                "flames cling to each enemy hit, once each");

            float before = target.Current;
            for (int i = 0; i < 70; i++)
            {
                yield return null;
            }

            Assert.That(target.Current, Is.LessThan(before - 5f), "the burn keeps ticking");
        }

        [UnityTest]
        public IEnumerator Fireball_OnAWall_BurstsAlongTheWall_WithoutHurtingAnyone()
        {
            Ground();
            Caster();
            GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = new Vector3(0f, 2f, 12f);
            wall.transform.localScale = new Vector3(10f, 4f, 0.5f);
            Physics.SyncTransforms();
            AimAt(new Vector3(0f, 1.6f, 12f));
            caster.CastCard(fireball);
            yield return UntilLanded();

            Assert.That(landed[0].Hit, Is.True);
            Assert.That(landed[0].Target, Is.Null);
            Assert.That(landed[0].Point.z, Is.EqualTo(11.75f).Within(0.05f), "on the wall's face");
            Assert.That(Vector3.Angle(landed[0].Normal, Vector3.back), Is.LessThan(1f));
            FireballExplosionFx blast = Live<FireballExplosionFx>().Single();
            Assert.That(blast.OnCharacter, Is.False);
            Assert.That(Vector3.Angle(blast.transform.up, Vector3.back), Is.LessThan(1f), "flames and the ring spread along the wall");
            Assert.That(damage, Is.Empty);
            Assert.That(elementHits, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Fireball_PassesThroughAllies_AndRunsOutOfRange_Harmlessly()
        {
            Ground();
            Caster();
            Health ally = Character("Ally", new Vector3(0f, 0f, 5f), TeamId.Players);
            Health enemy = Character("Enemy", new Vector3(0f, 0f, 14f), TeamId.Enemies);
            AimAt(enemy.transform.position + Vector3.up * 1.1f);
            caster.CastCard(fireball);
            yield return UntilLanded();
            Assert.That(landed[0].Target, Is.EqualTo(enemy), "flew through the ally");
            Assert.That(damage.Any(d => d.Target == ally), Is.False);

            landed.Clear();
            damage.Clear();
            aim.Ray = new Ray(caster.transform.position + Vector3.up * 1.6f, (Vector3.up + Vector3.forward * 0.3f).normalized);
            caster.CastCard(fireball);
            yield return UntilLanded();
            Assert.That(landed[0].Hit, Is.False, "nothing in the sky");
            Assert.That(landed[0].TimeInFlight, Is.EqualTo(fireball.range / 45f).Within(0.05f));
            Assert.That(damage.Where(d => d.Info.Kind == DamageKind.Skill), Is.Empty, "no blast (the first shot's burn may still tick)");
        }

        [UnityTest]
        public IEnumerator Fireball_CompletesOverload_OnALightningMarkedEnemy()
        {
            Ground();
            Caster();
            var comboGo = Track(new GameObject("ComboSystem"));
            var system = comboGo.AddComponent<ComboSystem>();
            var overload = AssetDatabase.LoadAssetAtPath<ComboDefinition>(OverloadPath);
            var bolt = AssetDatabase.LoadAssetAtPath<CardDefinition>(BoltPath);
            Assert.That(overload, Is.Not.Null);
            Assert.That(bolt, Is.Not.Null);
            system.Configure(new List<ComboDefinition> { overload });
            var combos = new List<ComboReport>();
            void OnCombo(ComboReport r) => combos.Add(r);
            ComboEvents.Triggered += OnCombo;
            try
            {
                Health enemy = Character("Enemy", new Vector3(0f, 0f, 12f), TeamId.Enemies);
                AimAt(enemy.transform.position + Vector3.up * 1.1f);
                caster.CastCard(bolt);
                yield return null;
                caster.CastCard(fireball);
                yield return UntilLanded();
                Assert.That(combos.Count, Is.EqualTo(1));
                Assert.That(combos[0].Definition, Is.EqualTo(overload), "lightning then the fireball's fire: overload");
            }
            finally
            {
                ComboEvents.Triggered -= OnCombo;
            }
        }

        [UnityTest]
        public IEnumerator Burning_Flames_FollowTheBurn_AndDieOutWithIt()
        {
            Ground();
            Caster();
            var presenterGo = Track(new GameObject("Auras"));
            var presenter = presenterGo.AddComponent<ElementAuraPresenter>();
            presenter.Configure(null, null, null, null, AssetDatabase.LoadAssetAtPath<GameObject>(BurningPath));
            Health enemy = Character("Enemy", new Vector3(0f, 0f, 10f), TeamId.Enemies);
            AimAt(enemy.transform.position + Vector3.up * 1.1f);
            caster.CastCard(fireball);
            yield return UntilLanded();
            yield return null;
            Assert.That(presenter.BurningCount, Is.EqualTo(1), "flames while it burns");
            Assert.That(Live<PooledFx>().Single(f => f.name == "FX_Burning").transform.parent, Is.EqualTo(enemy.transform));

            for (int i = 0; i < 200; i++)
            {
                yield return null;
            }

            Assert.That(enemy.GetComponent<CombatStatus>().Model.HasDamageOverTime(Element.Fire), Is.False);
            Assert.That(presenter.BurningCount, Is.Zero, "the flames end with the burn");
        }

        [UnityTest]
        public IEnumerator RepeatedFireballs_ReuseThePool_AndFollowTheVfxQuality()
        {
            Ground();
            Caster();
            GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = new Vector3(0f, 2f, 9f);
            wall.transform.localScale = new Vector3(20f, 4f, 0.5f);
            Physics.SyncTransforms();
            caster.Prewarm(fireball);

            int Pooled() => Object.FindObjectsByType<PooledFx>(FindObjectsInactive.Include)
                .Count(f => f.name.StartsWith("FX_Fireball"));

            IEnumerator Volley()
            {
                for (int i = 0; i < 4; i++)
                {
                    AimAt(new Vector3(-3f + i * 2f, 1.5f, 9f));
                    caster.CastCard(fireball);
                    for (int f = 0; f < 6; f++)
                    {
                        yield return null;
                    }
                }

                for (int f = 0; f < 150; f++)
                {
                    yield return null; // every shot lands and every effect returns to the pool
                }
            }

            yield return Volley();
            yield return Volley(); // the pool grows to the most effects ever alive at once
            int warm = Pooled();
            yield return Volley();
            Assert.That(Pooled(), Is.EqualTo(warm), "no new instances once the pool is warm");

            PerformanceSettings.SetVfxQuality(VfxQuality.Low);
            AimAt(new Vector3(0f, 1.5f, 9f));
            caster.CastCard(fireball);
            for (int f = 0; f < 9; f++)
            {
                yield return null;
            }

            FireballProjectileFx low = Live<FireballProjectileFx>().Single(p => !p.IsEnding);
            Assert.That(low.transform.Find("Body/FlameOuterLayer").gameObject.activeSelf, Is.False, "low: no outer shell");
            Assert.That(low.transform.Find("GlowLayer").gameObject.activeSelf, Is.False, "low: no light");
            Assert.That(low.transform.Find("Body/Core").GetComponent<Renderer>().enabled, Is.True, "the core always shows");

            for (int f = 0; f < 60; f++)
            {
                yield return null;
            }

            PerformanceSettings.SetVfxQuality(VfxQuality.High);
            caster.CastCard(fireball);
            for (int f = 0; f < 9; f++)
            {
                yield return null;
            }

            FireballProjectileFx high = Live<FireballProjectileFx>().Single(p => !p.IsEnding);
            Assert.That(high.transform.Find("Body/FlameOuterLayer").gameObject.activeSelf, Is.True);
            Assert.That(high.transform.Find("GlowLayer").gameObject.activeSelf, Is.True);
        }
    }
}
#endif
