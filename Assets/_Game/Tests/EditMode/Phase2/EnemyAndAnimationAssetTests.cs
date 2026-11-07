using System.Collections.Generic;
using System.Linq;
using CoopDeck.AI;
using CoopDeck.Animation;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Enemies;
using CoopDeck.Player;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

namespace CoopDeck.Tests
{
    /// <summary>Checks the generated Phase 2 assets (run CoopDeck > Setup > Run Phase 1 Bootstrap first).</summary>
    public class EnemyAndAnimationAssetTests
    {
        const string AnimFolder = "Assets/_Game/Animations/Placeholder";
        const string ControllerPath = AnimFolder + "/AC_PlaceholderHumanoid.controller";
        const string ShooterOverridePath = AnimFolder + "/AOC_Shooter.overrideController";
        const string UpperMaskPath = AnimFolder + "/AM_UpperBody.mask";
        const string SlasherPath = "Assets/_Game/Prefabs/Enemies/PF_Enemy_Slasher.prefab";
        const string ShooterPath = "Assets/_Game/Prefabs/Enemies/PF_Enemy_Shooter.prefab";
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string ScenePath = "Assets/_Game/Scenes/Sandbox_Phase1.unity";
        const string NavMeshPath = "Assets/_Game/Scenes/Sandbox_Phase1_NavMesh.asset";

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run the bootstrap");
            return asset;
        }

        static AnimatorState State(AnimatorStateMachine machine, string name)
        {
            return machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        }

        [Test]
        public void Controller_HasTheSharedParameters()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            var expected = new Dictionary<string, AnimatorControllerParameterType>
            {
                { AnimatorParams.MoveX, AnimatorControllerParameterType.Float },
                { AnimatorParams.MoveY, AnimatorControllerParameterType.Float },
                { AnimatorParams.Speed, AnimatorControllerParameterType.Float },
                { AnimatorParams.LocomotionSpeed, AnimatorControllerParameterType.Float },
                { AnimatorParams.IsGrounded, AnimatorControllerParameterType.Bool },
                { AnimatorParams.VerticalSpeed, AnimatorControllerParameterType.Float },
                { AnimatorParams.Fire, AnimatorControllerParameterType.Trigger },
                { AnimatorParams.Reload, AnimatorControllerParameterType.Trigger },
                { AnimatorParams.Attack, AnimatorControllerParameterType.Trigger },
                { AnimatorParams.Hit, AnimatorControllerParameterType.Trigger },
                { AnimatorParams.Dead, AnimatorControllerParameterType.Bool },
            };

            foreach (KeyValuePair<string, AnimatorControllerParameterType> pair in expected)
            {
                AnimatorControllerParameter p = controller.parameters.FirstOrDefault(x => x.name == pair.Key);
                Assert.That(p, Is.Not.Null, pair.Key);
                Assert.That(p.type, Is.EqualTo(pair.Value), pair.Key);
            }
        }

        [Test]
        public void Controller_BaseLayer_IsAn8DirectionLocomotionBlendTree_WithFallAndDeath()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
            Assert.That(baseLayer.defaultState, Is.Not.Null);
            Assert.That(baseLayer.defaultState.name, Is.EqualTo("Locomotion"));

            var tree = baseLayer.defaultState.motion as BlendTree;
            Assert.That(tree, Is.Not.Null, "locomotion is a blend tree");
            Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.FreeformDirectional2D));
            Assert.That(tree.blendParameter, Is.EqualTo(AnimatorParams.MoveX));
            Assert.That(tree.blendParameterY, Is.EqualTo(AnimatorParams.MoveY));
            Assert.That(tree.children.Length, Is.EqualTo(9), "idle + 8 directions");
            Assert.That(tree.children.Count(c => c.position == Vector2.zero), Is.EqualTo(1), "one idle at the center");
            Assert.That(tree.children.All(c => c.motion is AnimationClip), Is.True);

            Assert.That(State(baseLayer, "Airborne"), Is.Not.Null);
            Assert.That(State(baseLayer, "Death"), Is.Not.Null);
            Assert.That(baseLayer.anyStateTransitions.Any(t => t.destinationState != null && t.destinationState.name == "Death"), Is.True);
        }

        [Test]
        public void Controller_UpperBodyLayer_IsMasked_AndHasTheActionStates()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            Assert.That(controller.layers.Length, Is.EqualTo(2));
            AnimatorControllerLayer upper = controller.layers[1];
            Assert.That(upper.name, Is.EqualTo("UpperBody"));
            Assert.That(upper.defaultWeight, Is.EqualTo(1f));
            Assert.That(upper.avatarMask, Is.SameAs(Load<AvatarMask>(UpperMaskPath)));

            foreach (string name in new[] { "Empty", "Attack", "Hit", "Fire", "Reload" })
            {
                Assert.That(State(upper.stateMachine, name), Is.Not.Null, name);
            }
        }

        [Test]
        public void UpperBodyMask_MovesTheTorsoAndArms_ButNotTheLegs()
        {
            var mask = Load<AvatarMask>(UpperMaskPath);
            var active = new Dictionary<string, bool>();
            for (int i = 0; i < mask.transformCount; i++)
            {
                active[mask.GetTransformPath(i)] = mask.GetTransformActive(i);
            }

            Assert.That(active["Hips/Spine"], Is.True);
            Assert.That(active["Hips/Spine/Shoulder_L"], Is.True);
            Assert.That(active["Hips/Spine/Shoulder_R"], Is.True);
            Assert.That(active["Hips"], Is.False);
            Assert.That(active["Hips/Hip_L"], Is.False);
            Assert.That(active["Hips/Hip_R"], Is.False);
        }

        [Test]
        public void EveryPlaceholderClip_HasCurves()
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimFolder });
            Assert.That(guids.Length, Is.GreaterThanOrEqualTo(16), "idle, 8 walks, fall, death, melee, shoot, hit, fire, reload");
            foreach (string guid in guids)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(AnimationUtility.GetCurveBindings(clip), Is.Not.Empty, clip.name);
                Assert.That(clip.length, Is.GreaterThan(0f), clip.name);
            }
        }

        [Test]
        public void ShooterOverride_SwapsOnlyTheAttackClip()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            var shooter = Load<AnimatorOverrideController>(ShooterOverridePath);
            Assert.That(shooter.runtimeAnimatorController, Is.SameAs(controller));

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            shooter.GetOverrides(overrides);
            List<KeyValuePair<AnimationClip, AnimationClip>> swapped = overrides.Where(o => o.Value != null).ToList();
            Assert.That(swapped.Count, Is.EqualTo(1));
            Assert.That(swapped[0].Key.name, Does.Contain("AttackMelee"));
            Assert.That(swapped[0].Value.name, Does.Contain("AttackShoot"));
        }

        [TestCase(SlasherPath, EnemyAttackKind.Melee, false)]
        [TestCase(ShooterPath, EnemyAttackKind.Projectile, true)]
        public void EnemyPrefab_IsFullyWired(string path, EnemyAttackKind kind, bool hasWeakpoint)
        {
            var prefab = Load<GameObject>(path);
            Assert.That(prefab.layer, Is.EqualTo(GameLayers.EnemyIndex));
            Assert.That(prefab.GetComponent<NavMeshAgent>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Health>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ForceReceiver>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyBrain>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterAnimatorDriver>(), Is.Not.Null);

            EnemyDefinition def = prefab.GetComponent<EnemyActor>().Definition;
            Assert.That(def, Is.Not.Null);
            Assert.That(def.attackKind, Is.EqualTo(kind));
            Assert.That(def.windup, Is.GreaterThan(0.3f), "attacks must be readable");
            Assert.That(def.windupFx, Is.Not.Null);
            Assert.That(def.deathFx, Is.Not.Null);
            if (kind == EnemyAttackKind.Projectile)
            {
                Assert.That(def.projectilePrefab, Is.Not.Null);
                Assert.That(def.projectilePrefab.GetComponent<EnemyProjectile>(), Is.Not.Null);
            }

            HitZone[] zones = prefab.GetComponentsInChildren<HitZone>(true);
            Assert.That(zones.Count(z => z.Zone == HitZoneType.Body), Is.EqualTo(1));
            Assert.That(zones.Count(z => z.Zone == HitZoneType.Head), Is.EqualTo(1));
            Assert.That(zones.Count(z => z.Zone == HitZoneType.Weakpoint), Is.EqualTo(hasWeakpoint ? 1 : 0));
            Assert.That(zones.All(z => z.GetComponent<Collider>() != null), Is.True);

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(animator.applyRootMotion, Is.False, "the NavMeshAgent moves the body");

            var builder = prefab.GetComponentInChildren<RigBuilder>(true);
            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.layers.Count, Is.EqualTo(1));
            var aim = prefab.GetComponentInChildren<MultiAimConstraint>(true);
            Assert.That(aim, Is.Not.Null);
            Assert.That(aim.data.constrainedObject, Is.Not.Null);
            Assert.That(aim.data.sourceObjects.Count, Is.EqualTo(1));
            Assert.That(aim.data.sourceObjects[0].transform.name, Is.EqualTo("LookTarget"));
        }

        [Test]
        public void PlayerPrefab_HasAShadowCastingAnimatedBody()
        {
            var prefab = Load<GameObject>(PlayerPath);
            FpsCharacter character = prefab.GetComponent<FpsCharacter>();
            var so = new SerializedObject(character);
            var driver = so.FindProperty("bodyAnimator").objectReferenceValue as CharacterAnimatorDriver;
            Assert.That(driver, Is.Not.Null, "FpsCharacter.bodyAnimator");

            var animator = prefab.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController is AnimatorController);
            Assert.That(animator, Is.Not.Null, "body animator uses the humanoid controller");
            Renderer[] bodyRenderers = animator.GetComponentsInChildren<Renderer>(true);
            Assert.That(bodyRenderers, Is.Not.Empty);
            Assert.That(bodyRenderers.All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly), Is.True,
                "the local player only sees the body's shadow, never the mesh");
        }

        [Test]
        public void SandboxScene_HasABakedNavMesh_AndAConfiguredArena()
        {
            var navData = Load<NavMeshData>(NavMeshPath);
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                List<NavMeshSurface> surfaces = roots.SelectMany(r => r.GetComponentsInChildren<NavMeshSurface>(true)).ToList();
                Assert.That(surfaces.Count, Is.EqualTo(1));
                Assert.That(surfaces[0].navMeshData, Is.SameAs(navData));

                List<WaveDirector> directors = roots.SelectMany(r => r.GetComponentsInChildren<WaveDirector>(true)).ToList();
                Assert.That(directors.Count, Is.EqualTo(1));
                var so = new SerializedObject(directors[0]);
                SerializedProperty prefabs = so.FindProperty("enemyPrefabs");
                Assert.That(prefabs.arraySize, Is.EqualTo(2));
                for (int i = 0; i < prefabs.arraySize; i++)
                {
                    var enemy = prefabs.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                    Assert.That(enemy, Is.Not.Null);
                    Assert.That(enemy.GetComponent<EnemyBrain>(), Is.Not.Null);
                }

                Assert.That(so.FindProperty("waves").arraySize, Is.GreaterThanOrEqualTo(3));
                SerializedProperty points = so.FindProperty("spawnPoints");
                Assert.That(points.arraySize, Is.GreaterThanOrEqualTo(2));

                // Spawn points must be inside the arena volume (enemies spawn where the fight is).
                for (int i = 0; i < points.arraySize; i++)
                {
                    var point = points.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    Assert.That(point, Is.Not.Null);
                    Bounds zone = directors[0].ZoneBounds;
                    Vector3 flat = point.position;
                    flat.y = zone.center.y;
                    Assert.That(zone.Contains(flat), Is.True, point.name + " inside the arena");
                }

                Assert.That(navData.sourceBounds.size.x, Is.GreaterThan(20f), "NavMesh covers the level");
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
