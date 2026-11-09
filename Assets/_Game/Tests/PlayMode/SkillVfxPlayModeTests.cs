#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Combat;
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
    /// The rebuilt skill and shot effects at run time (Docs/WEAPON_SKILL_VFX.md), cast through a real SkillCaster:
    /// the shot tracer flies and fades, the ice spear flies to the target and the shatter plays there, the meteor falls
    /// during the warning and is gone when the blast plays, the shield barrier follows its ally and breaks when the
    /// shield is used up, the mark reticle rides on the enemy, the area effects take the card's radius.
    /// </summary>
    public class SkillVfxPlayModeTests
    {
        const float Dt = 1f / 60f;
        const string Cards = "Assets/_Game/ScriptableObjects/Cards/";

        readonly List<Object> spawned = new List<Object>();
        SkillCaster caster;
        FixedAim aim;

        sealed class FixedAim : IAimProvider
        {
            public Ray Ray;
            public Ray GetAimRay() => Ray;
            public void AddRecoil(Vector2 kickDegrees) { }
        }

        static CardDefinition Card(string file)
        {
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(Cards + file + ".asset");
            Assert.That(card, Is.Not.Null, file + " missing: run the bootstrap");
            return card;
        }

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
            foreach (PooledFx fx in Object.FindObjectsByType<PooledFx>(FindObjectsInactive.Exclude))
            {
                fx.ReleaseNow();
            }
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            PerformanceSettings.ResetVfxQuality();
            foreach (PooledFx fx in Object.FindObjectsByType<PooledFx>(FindObjectsInactive.Exclude))
            {
                fx.ReleaseNow(); // auras parented to characters go back to the pool before the characters are destroyed
            }

            foreach (Object o in spawned.Where(o => o != null))
            {
                Object.Destroy(o);
            }

            spawned.Clear();
        }

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        static IEnumerable<T> Live<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);

        static PooledFx LiveFx(string name) => Live<PooledFx>().FirstOrDefault(f => f.name == name);

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

        Health Caster()
        {
            var go = Track(new GameObject("Caster"));
            var health = go.AddComponent<Health>();
            go.AddComponent<CombatStatus>();
            var actor = go.AddComponent<TestPlayerActor>();
            caster = go.AddComponent<SkillCaster>();
            var hand = new GameObject("Hand").transform;
            hand.SetParent(go.transform, false);
            hand.localPosition = new Vector3(-0.25f, 1.3f, 0.35f);
            caster.SetCastHand(hand);
            caster.SetCastOrigin(hand);
            aim = new FixedAim();
            caster.Initialize(actor, aim, Physics.DefaultRaycastLayers, GameLayers.SkillQueryMask);
            return health;
        }

        void AimAt(Vector3 point)
        {
            Vector3 eye = caster.transform.position + Vector3.up * 1.6f;
            aim.Ray = new Ray(eye, (point - eye).normalized);
        }

        [UnityTest]
        public IEnumerator ShotTracer_FliesToTheHit_AndItsWakeFades()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/VFX/Ignis/FX_Ignis_Tracer.prefab");
            PooledFx fx = FxPool.Spawn(prefab, Vector3.zero, Quaternion.identity);
            var tracer = fx.GetComponent<ShotTracerFx>();
            tracer.Play(Vector3.zero, new Vector3(0f, 0f, 30f));
            Assert.That(tracer.Playing, Is.True);
            yield return Frames(3);
            Assert.That(tracer.Playing, Is.True, "still on its way (30 m at 240 m/s)");
            yield return Frames(30);
            Assert.That(tracer.Playing, Is.False, "arrived and the wake faded");
        }

        [UnityTest]
        public IEnumerator IceLance_ThrowsASpearThatShattersOnTheTarget()
        {
            Ground();
            Caster();
            Health target = Character("Target", new Vector3(0f, 0f, 14f), TeamId.Enemies);
            AimAt(target.transform.position + Vector3.up * 1.1f);
            caster.CastCard(Card("CD_IceLance"));
            yield return null;

            ThrownBeamFx spear = Live<ThrownBeamFx>().Single();
            Assert.That(spear.Flying, Is.True);
            Assert.That(LiveFx("FX_IceLance_Shatter"), Is.Not.Null, "the shatter plays on the target");
            yield return Frames(12);
            Assert.That(spear.Flying, Is.False, "14 m at 110 m/s");
        }

        [UnityTest]
        public IEnumerator GroundEffects_AimedAtAnEnemysChest_StandOnTheFloorUnderIt()
        {
            Ground();
            Caster();
            Health target = Character("Target", new Vector3(0f, 0f, 10f), TeamId.Enemies);
            Vector3 chest = target.transform.position + Vector3.up * 1.1f;
            AimAt(chest);
            caster.CastCard(Card("CD_FlameVortex"));
            yield return null;

            PooledFx tornado = LiveFx("FX_FlameVortex_Area");
            Assert.That(tornado, Is.Not.Null, "the tornado plays where the bolt hit");
            Assert.That(tornado.transform.position.y, Is.EqualTo(0f).Within(0.1f), "on the floor, not at chest height");
            Assert.That(Vector3.ProjectOnPlane(tornado.transform.position - chest, Vector3.up).magnitude, Is.LessThan(0.8f), "right under the hit");
            Assert.That(Vector3.Angle(tornado.transform.up, Vector3.up), Is.LessThan(1f), "standing upright");

            CardDefinition meteor = Card("CD_MeteorStrike");
            caster.CastCard(meteor);
            yield return null;
            FallingStrikeFx warning = Live<FallingStrikeFx>().Single();
            Assert.That(warning.transform.position.y, Is.EqualTo(0f).Within(0.1f), "the warning and the falling rock aim at the floor");
            yield return Frames(Mathf.RoundToInt(meteor.impactDelay / Dt) + 6);
            PooledFx blast = LiveFx("FX_MeteorStrike_Blast");
            Assert.That(blast, Is.Not.Null);
            Assert.That(blast.transform.position.y, Is.EqualTo(0f).Within(0.1f), "the crater and rings on the floor");
        }

        [UnityTest]
        public IEnumerator Meteor_FallsDuringTheWarning_AndIsGoneWhenTheBlastPlays()
        {
            Ground();
            Caster();
            Character("Target", new Vector3(0f, 0f, 16f), TeamId.Enemies);
            CardDefinition meteor = Card("CD_MeteorStrike");
            AimAt(new Vector3(0f, 0f, 16f));
            caster.CastCard(meteor);
            yield return null;

            FallingStrikeFx fall = Live<FallingStrikeFx>().Single();
            float startHeight = fall.Body.position.y;
            Assert.That(startHeight, Is.GreaterThan(10f), "starts high in the sky");
            yield return Frames(Mathf.RoundToInt(meteor.impactDelay * 0.5f / Dt));
            Assert.That(fall.Body.gameObject.activeSelf, Is.True);
            Assert.That(fall.Body.position.y, Is.LessThan(startHeight), "falling");

            yield return Frames(Mathf.RoundToInt(meteor.impactDelay * 0.5f / Dt) + 6);
            Assert.That(fall.Body == null || !fall.Body.gameObject.activeInHierarchy, Is.True, "landed");
            PooledFx blast = LiveFx("FX_MeteorStrike_Blast");
            Assert.That(blast, Is.Not.Null, "the blast plays when the damage lands");
            Assert.That(Vector3.Distance(blast.transform.position, new Vector3(0f, 0f, 16f)), Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Shield_BarrierFollowsTheAlly_AndBreaksWhenTheShieldIsUsedUp()
        {
            Ground();
            Health self = Caster();
            caster.CastCard(Card("CD_Shield"));
            yield return null;

            StatusAuraFx barrier = Live<StatusAuraFx>().Single();
            Assert.That(barrier.transform.parent, Is.EqualTo(self.transform), "parented to the ally");
            yield return Frames(10);
            Assert.That(barrier.Ending, Is.False, "holds while the shield holds");

            self.ApplyDamage(new DamageInfo { Amount = 500f, Kind = DamageKind.Environment, SourceTeam = TeamId.Neutral, Point = self.transform.position });
            yield return Frames(3);
            Assert.That(barrier.Ending, Is.True, "breaks when the shield is used up");
            yield return Frames(40);
            Assert.That(barrier.gameObject.activeInHierarchy, Is.False, "and goes back to the pool");
        }

        [UnityTest]
        public IEnumerator Mark_ReticleRidesOnTheEnemy()
        {
            Ground();
            Caster();
            Health target = Character("Target", new Vector3(0f, 0f, 12f), TeamId.Enemies);
            AimAt(target.transform.position + Vector3.up * 1.1f);
            caster.CastCard(Card("CD_MarkTarget"));
            yield return null;

            StatusAuraFx reticle = Live<StatusAuraFx>().Single();
            Assert.That(reticle.transform.parent, Is.EqualTo(target.transform));
            target.transform.position += new Vector3(3f, 0f, 0f);
            yield return Frames(2);
            Assert.That(Mathf.Abs(reticle.transform.position.x - target.transform.position.x), Is.LessThan(0.01f), "follows the enemy");
            Assert.That(reticle.Ending, Is.False, "while the mark lasts");
        }

        [UnityTest]
        public IEnumerator AreaEffects_TakeTheCardRadius_AndDropHeavyLayersOnLowQuality()
        {
            Ground();
            Caster();
            Character("Target", new Vector3(0f, 0f, 12f), TeamId.Enemies);
            AimAt(new Vector3(0f, 0f, 12f));
            PerformanceSettings.SetVfxQuality(VfxQuality.Low);
            caster.CastCard(Card("CD_GravityCollapse"));
            yield return null;
            PooledFx well = LiveFx("FX_GravityCollapse_Area");
            Assert.That(well, Is.Not.Null);
            Assert.That(well.GetComponent<RadiusScaledFx>().Radius, Is.EqualTo(Card("CD_GravityCollapse").MaxRadius).Within(1e-3f));
            Assert.That(well.transform.Find("Full/Medium").gameObject.activeSelf, Is.False, "rubble only from Medium up");

            PerformanceSettings.SetVfxQuality(VfxQuality.High);
            caster.CastCard(Card("CD_FrostNova"));
            yield return null;
            PooledFx nova = LiveFx("FX_FrostNova_Area");
            Assert.That(nova, Is.Not.Null);
            Assert.That(Vector3.Distance(nova.transform.position, caster.transform.position), Is.LessThan(0.3f), "around the caster");
        }
    }
}
#endif
