using System.Collections;
using System.Collections.Generic;
using CoopDeck.AI;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Enemies;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Enemy behaviour on a real NavMesh: chasing, telegraphed melee hits, dodging, projectiles,
    /// waves and pulls. Built in code (no generated prefabs) so each rule is tested on its own.
    /// </summary>
    public class EnemyPlayModeTests
    {
        const float Dt = 1f / 60f;
        readonly List<Object> spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
            Assert.That(GameLayers.Player, Is.GreaterThanOrEqualTo(0), "Player layer missing: run the bootstrap");
            Assert.That(GameLayers.Enemy, Is.GreaterThanOrEqualTo(0), "Enemy layer missing: run the bootstrap");
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
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

        /// <summary>A flat 40 m floor with a baked NavMesh (edges at ±20 m).</summary>
        void CreateArenaFloor()
        {
            var root = Track(new GameObject("TestNavFloor"));
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            Physics.SyncTransforms();

            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            Assert.That(surface.navMeshData, Is.Not.Null, "NavMesh failed to build");
        }

        TestPlayerActor CreatePlayer(Vector3 position)
        {
            var go = Track(new GameObject("TestPlayer"));
            go.layer = GameLayers.Player;
            go.transform.position = position;
            go.AddComponent<Health>();
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.4f;
            TestPlayerActor actor = go.AddComponent<TestPlayerActor>();
            Physics.SyncTransforms();
            return actor;
        }

        EnemyDefinition MeleeDefinition()
        {
            var def = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
            def.maxHealth = 100f;
            def.moveSpeed = 6f;
            def.acceleration = 40f;
            def.attackKind = EnemyAttackKind.Melee;
            def.attackRange = 2.2f;
            def.preferredRange = 1.6f;
            def.damage = 10f;
            def.windup = 0.3f;
            def.recover = 0.2f;
            def.cooldown = 5f;
            def.meleeArc = 70f;
            return def;
        }

        EnemyDefinition ShooterDefinition(GameObject projectile)
        {
            var def = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
            def.maxHealth = 80f;
            def.moveSpeed = 4f;
            def.acceleration = 30f;
            def.attackKind = EnemyAttackKind.Projectile;
            def.attackRange = 20f;
            def.preferredRange = 11f;
            def.damage = 9f;
            def.windup = 0.3f;
            def.recover = 0.2f;
            def.cooldown = 5f;
            def.projectilePrefab = projectile;
            def.projectileSpeed = 20f;
            def.projectileRadius = 0.25f;
            return def;
        }

        /// <summary>Inactive template for FxPool: PooledFx + EnemyProjectile.</summary>
        GameObject CreateProjectileTemplate()
        {
            var go = Track(new GameObject("TestEnemyProjectile"));
            go.SetActive(false);
            go.AddComponent<PooledFx>().Lifetime = 4f;
            go.AddComponent<EnemyProjectile>();
            return go;
        }

        EnemyBrain CreateEnemy(EnemyDefinition def, Vector3 position, Quaternion rotation)
        {
            var go = Track(new GameObject("TestEnemy"));
            go.SetActive(false); // add everything before Awake / OnEnable run
            go.layer = GameLayers.Enemy;
            go.transform.SetPositionAndRotation(position, rotation);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.8f;
            go.AddComponent<Health>();
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.4f;
            go.AddComponent<EnemyActor>().Initialize(def);
            EnemyBrain brain = go.AddComponent<EnemyBrain>();
            go.SetActive(true);
            return brain;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        [UnityTest]
        public IEnumerator Melee_ChasesTheTarget_TelegraphsAndHits()
        {
            CreateArenaFloor();
            TestPlayerActor player = CreatePlayer(new Vector3(0f, 0f, 6f));
            EnemyBrain enemy = CreateEnemy(MeleeDefinition(), new Vector3(0f, 0f, -8f), Quaternion.identity);

            float windupAt = -1f;
            float hitAt = -1f;
            float distanceAtHit = 0f;
            enemy.WindupStarted += _ => { if (windupAt < 0f) windupAt = Time.time; };
            player.Health.Damaged += (info, result) =>
            {
                hitAt = Time.time;
                distanceAtHit = FlatDistance(enemy.transform.position, player.Position);
            };

            for (int i = 0; i < 360 && hitAt < 0f; i++)
            {
                yield return null;
            }

            Assert.That(hitAt, Is.GreaterThan(0f), "the enemy never hit the standing target");
            Assert.That(player.Health.Current, Is.EqualTo(player.Health.Max - 10f).Within(0.01f));
            Assert.That(windupAt, Is.GreaterThan(0f), "attacks are telegraphed");
            Assert.That(hitAt - windupAt, Is.GreaterThanOrEqualTo(0.3f - 0.001f), "the hit comes after the wind-up");
            Assert.That(distanceAtHit, Is.LessThanOrEqualTo(2.8f), "it walked up to the target");
        }

        [UnityTest]
        public IEnumerator Melee_DodgingDuringTheWindup_AvoidsTheHit()
        {
            CreateArenaFloor();
            TestPlayerActor player = CreatePlayer(new Vector3(0f, 0f, 4f));
            EnemyBrain enemy = CreateEnemy(MeleeDefinition(), new Vector3(0f, 0f, -4f), Quaternion.identity);

            bool released = false;
            bool dodged = false;
            enemy.WindupStarted += _ =>
            {
                // Step out of reach as soon as the telegraph shows (like a dash).
                player.transform.position += new Vector3(6f, 0f, 0f);
                dodged = true;
            };
            enemy.AttackReleased += _ => released = true;

            for (int i = 0; i < 360 && !released; i++)
            {
                yield return null;
            }

            Assert.That(dodged, Is.True, "the enemy never started an attack");
            Assert.That(released, Is.True, "the attack never resolved");
            Assert.That(player.Health.Current, Is.EqualTo(player.Health.Max), "a dodged telegraph does no damage");
        }

        [UnityTest]
        public IEnumerator Projectile_HitsAStandingTarget()
        {
            CreateArenaFloor();
            TestPlayerActor player = CreatePlayer(new Vector3(0f, 0f, 12f));
            EnemyBrain enemy = CreateEnemy(ShooterDefinition(CreateProjectileTemplate()), Vector3.zero, Quaternion.identity);

            bool released = false;
            enemy.AttackReleased += _ => released = true;
            for (int i = 0; i < 300 && player.Health.Current >= player.Health.Max; i++)
            {
                yield return null;
            }

            Assert.That(released, Is.True, "the shooter never fired");
            Assert.That(player.Health.Current, Is.EqualTo(player.Health.Max - 9f).Within(0.01f), "the projectile hit");
            Assert.That(FlatDistance(enemy.transform.position, player.Position), Is.GreaterThan(8f), "shooters keep their distance");
        }

        [UnityTest]
        public IEnumerator Projectile_SidestepAfterTheShot_Misses()
        {
            CreateArenaFloor();
            TestPlayerActor player = CreatePlayer(new Vector3(0f, 0f, 12f));
            EnemyBrain enemy = CreateEnemy(ShooterDefinition(CreateProjectileTemplate()), Vector3.zero, Quaternion.identity);

            int releasedFrame = -1;
            enemy.AttackReleased += _ =>
            {
                releasedFrame = Time.frameCount;
                player.transform.position += new Vector3(3f, 0f, 0f); // strafe out of the line of fire
                Physics.SyncTransforms();
            };

            for (int i = 0; i < 300 && releasedFrame < 0; i++)
            {
                yield return null;
            }

            Assert.That(releasedFrame, Is.GreaterThan(0), "the shooter never fired");

            // 12 m at 20 m/s arrives in ~0.6 s; wait 1.2 s (the cooldown blocks a second shot).
            for (int i = 0; i < 72; i++)
            {
                yield return null;
            }

            Assert.That(player.Health.Current, Is.EqualTo(player.Health.Max), "projectiles are dodgeable");
        }

        [UnityTest]
        public IEnumerator WaveDirector_SpawnsAlertedEnemies_AndStartsTheNextWaveWhenCleared()
        {
            // A brainless enemy keeps this test about the director, not about movement.
            var template = Track(new GameObject("TestWaveEnemy"));
            template.transform.position = new Vector3(0f, -100f, 0f);
            template.AddComponent<Health>();
            template.AddComponent<EnemyActor>().Initialize(MeleeDefinition());

            var directorGo = Track(new GameObject("TestWaveDirector"));
            var pointA = Track(new GameObject("SpawnA")).transform;
            var pointB = Track(new GameObject("SpawnB")).transform;
            pointA.position = new Vector3(-4f, 0f, 6f);
            pointB.position = new Vector3(4f, 0f, 6f);
            var director = directorGo.AddComponent<WaveDirector>();
            director.Configure(
                new List<GameObject> { template },
                new List<WaveDirector.WaveCounts>
                {
                    new WaveDirector.WaveCounts { counts = new List<int> { 2 } },
                    new WaveDirector.WaveCounts { counts = new List<int> { 1 } },
                },
                new[] { pointA, pointB },
                new Vector3(20f, 6f, 20f));

            var spawnedEnemies = new List<EnemyActor>();
            director.EnemySpawned += e =>
            {
                spawnedEnemies.Add(e);
                Track(e.gameObject);
            };

            yield return null;
            Assert.That(director.Schedule.Phase, Is.EqualTo(WavePhase.Idle), "nothing spawns while the arena is empty");

            CreatePlayer(Vector3.zero);
            for (int i = 0; i < 120 && director.AliveEnemies.Count < 2; i++)
            {
                yield return null;
            }

            Assert.That(director.Schedule.WaveNumber, Is.EqualTo(1));
            Assert.That(spawnedEnemies.Count, Is.EqualTo(2));
            Assert.That(spawnedEnemies.TrueForAll(e => e.AlertOnSpawn), "arena enemies hunt the player from the start");
            Assert.That(spawnedEnemies[0].transform.position, Is.EqualTo(pointA.position));
            Assert.That(spawnedEnemies[1].transform.position, Is.EqualTo(pointB.position));

            foreach (EnemyActor e in spawnedEnemies.ToArray())
            {
                e.Health.ApplyDamage(new DamageInfo { Amount = 1000f, SourceTeam = TeamId.Players });
            }

            Assert.That(director.AliveEnemies.Count, Is.EqualTo(0));
            for (int i = 0; i < 400 && director.Schedule.WaveNumber < 2; i++)
            {
                yield return null;
            }

            Assert.That(director.Schedule.WaveNumber, Is.EqualTo(2), "the next wave starts after the intermission");
            for (int i = 0; i < 120 && spawnedEnemies.Count < 3; i++)
            {
                yield return null;
            }

            Assert.That(spawnedEnemies.Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator Pull_MovesANavMeshAgent_ButNotOffTheNavMesh()
        {
            CreateArenaFloor();
            var go = Track(new GameObject("TestPulledAgent"));
            go.transform.position = new Vector3(14f, 0f, 0f);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            var receiver = go.AddComponent<ForceReceiver>();
            yield return null;
            Assert.That(agent.isOnNavMesh, Is.True);

            // Pulled 12 m toward a point past the floor edge (x = 20): it must stop at the edge.
            receiver.Pull(new Vector3(40f, 0f, 0f), 30f, 1f);
            for (int i = 0; i < 70; i++)
            {
                yield return null;
            }

            Assert.That(go.transform.position.x, Is.GreaterThan(18f), "the pull moved the agent");
            Assert.That(go.transform.position.x, Is.LessThanOrEqualTo(20.05f), "the agent stays on the NavMesh");
            Assert.That(agent.isOnNavMesh, Is.True);
        }
    }
}
