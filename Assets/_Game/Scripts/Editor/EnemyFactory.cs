using CoopDeck.AI;
using CoopDeck.Animation;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Enemies;
using CoopDeck.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    public sealed class EnemySet
    {
        public EnemyDefinition Slasher;
        public EnemyDefinition Shooter;
        public GameObject SlasherPrefab;
        public GameObject ShooterPrefab;
        public GameObject ProjectilePrefab;
    }

    /// <summary>
    /// Enemy definitions (tuning kept once created) and the generated enemy prefabs:
    /// NavMeshAgent + Health/CombatStatus/ForceReceiver + EnemyActor/EnemyBrain, body / head /
    /// weak-point hit zones on animated bones, the placeholder rig with Animator, and an
    /// Animation Rigging Multi-Aim constraint that turns the head toward the target.
    /// </summary>
    public static class EnemyFactory
    {
        const string DataFolder = GamePaths.Root + "/ScriptableObjects/Enemies";

        public static EnemySet EnsureEnemies(MaterialSet m, FxPrefabSet fx, SkillFxSet skillFx, GameObject windupFx, AnimationSet animations)
        {
            EditorUtil.EnsureFolder(DataFolder);
            EditorUtil.EnsureFolder(GamePaths.EnemyPrefabs);

            var set = new EnemySet();
            set.ProjectilePrefab = BuildProjectile(m, skillFx.Burst(Element.Fire));

            set.Slasher = EnsureDefinition("ED_Slasher", d =>
            {
                d.enemyId = "enemy.slasher";
                d.displayName = "スラッシャー";
                d.maxHealth = 140f;
                d.moveSpeed = 4.2f;
                d.acceleration = 24f;
                d.attackKind = EnemyAttackKind.Melee;
                d.attackRange = 2.2f;
                d.preferredRange = 1.6f;
                d.damage = 14f;
                d.windup = 0.55f;
                d.recover = 0.4f;
                d.cooldown = 1.3f;
                d.meleeArc = 70f;
            });
            set.Shooter = EnsureDefinition("ED_Shooter", d =>
            {
                d.enemyId = "enemy.shooter";
                d.displayName = "シューター";
                d.maxHealth = 90f;
                d.moveSpeed = 3.4f;
                d.acceleration = 18f;
                d.attackKind = EnemyAttackKind.Projectile;
                d.attackRange = 20f;
                d.preferredRange = 11f;
                d.damage = 9f;
                d.windup = 0.7f;
                d.recover = 0.3f;
                d.cooldown = 1.8f;
                d.projectileSpeed = 20f;
                d.projectileRadius = 0.25f;
            });

            foreach (EnemyDefinition def in new[] { set.Slasher, set.Shooter })
            {
                if (def.windupFx == null) def.windupFx = windupFx;
                if (def.deathFx == null) def.deathFx = fx.EnemyDeath;
                EditorUtility.SetDirty(def);
            }

            if (set.Shooter.projectilePrefab == null) set.Shooter.projectilePrefab = set.ProjectilePrefab;
            AssetDatabase.SaveAssets();

            set.SlasherPrefab = BuildEnemy("PF_Enemy_Slasher", set.Slasher, animations.Humanoid,
                m.EnemyBody, m.EnemyBase, m.EnemyHead, m.EnemyEye, null, m);
            set.ShooterPrefab = BuildEnemy("PF_Enemy_Shooter", set.Shooter, animations.Shooter,
                m.Violet, m.EnemyBase, m.Teal, m.GlowPink, m.GlowCyan, m);
            return set;
        }

        static EnemyDefinition EnsureDefinition(string assetName, System.Action<EnemyDefinition> initialize)
        {
            string path = $"{DataFolder}/{assetName}.asset";
            var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<EnemyDefinition>();
                initialize(def);
                AssetDatabase.CreateAsset(def, path);
            }

            return def;
        }

        static GameObject BuildProjectile(MaterialSet m, GameObject impactFx)
        {
            var root = new GameObject("PF_EnemyProjectile");
            root.AddComponent<PooledFx>().Lifetime = 3.5f;
            var projectile = root.AddComponent<EnemyProjectile>();
            projectile.SetImpactFx(impactFx);
            EditorUtil.VisualPrimitive(PrimitiveType.Sphere, "Core", root.transform, Vector3.zero, Vector3.one * 0.34f, m.GlowPink, castShadows: false);

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(root.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.sharedMaterial = m.FxEnemySpark;
            trail.time = 0.22f;
            trail.startWidth = 0.3f;
            trail.endWidth = 0f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.startColor = new Color(1f, 0.6f, 0.9f, 1f);
            trail.endColor = new Color(1f, 0.3f, 0.7f, 0f);

            string path = GamePaths.EnemyPrefabs + "/PF_EnemyProjectile.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject BuildEnemy(string name, EnemyDefinition def, RuntimeAnimatorController controller,
            Material body, Material limbs, Material head, Material accent, Material weakpointMaterial, MaterialSet m)
        {
            var root = new GameObject(name);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.42f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.speed = def.moveSpeed;
            agent.acceleration = def.acceleration;
            agent.angularSpeed = def.angularSpeed;
            agent.stoppingDistance = def.preferredRange;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            var body3 = root.AddComponent<Rigidbody>();
            body3.isKinematic = true;
            body3.useGravity = false;

            var health = root.AddComponent<Health>();
            EditorUtil.SetFloat(health, "maxHealth", def.maxHealth);
            EditorUtil.SetInt(health, "team", (int)TeamId.Enemies);
            root.AddComponent<CombatStatus>();
            root.AddComponent<ForceReceiver>();
            var actor = root.AddComponent<EnemyActor>();
            EditorUtil.SetObject(actor, "definition", def);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.8f, 0f);
            capsule.height = 1.55f;
            capsule.radius = 0.42f;
            root.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);

            RigParts rig = PlaceholderRigBuilder.Build(root.transform, body, limbs, head, accent, shadowsOnly: false);
            var animator = rig.Visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var headCollider = rig.Head.gameObject.AddComponent<SphereCollider>();
            headCollider.center = new Vector3(0f, 0.18f, 0f);
            headCollider.radius = 0.22f;
            rig.Head.gameObject.AddComponent<HitZone>().Configure(health, HitZoneType.Head, 1f);

            if (weakpointMaterial != null)
            {
                // Glowing core on the chest: a weak point worth aiming for (×1.75).
                GameObject core = EditorUtil.VisualPrimitive(PrimitiveType.Sphere, "WeakpointCore", rig.Spine, new Vector3(0f, 0.32f, 0.17f), Vector3.one * 0.2f, weakpointMaterial);
                var coreCollider = core.AddComponent<SphereCollider>();
                coreCollider.radius = 0.65f; // relative to the 0.2 scale → ~0.13 m
                core.AddComponent<HitZone>().Configure(health, HitZoneType.Weakpoint, 1.75f);
            }

            var lookTarget = new GameObject("LookTarget").transform;
            lookTarget.SetParent(root.transform, false);
            lookTarget.localPosition = new Vector3(0f, 1.6f, 4f);
            var eye = new GameObject("Eye").transform;
            eye.SetParent(root.transform, false);
            eye.localPosition = new Vector3(0f, 1.75f, 0.2f);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, 1.35f, 0.65f);

            // Animation Rigging: the head tracks the target within ±70°.
            var rigGo = new GameObject("Rig");
            rigGo.transform.SetParent(rig.Visual, false);
            var rigComponent = rigGo.AddComponent<Rig>();
            var aimGo = new GameObject("HeadAim");
            aimGo.transform.SetParent(rigGo.transform, false);
            var aim = aimGo.AddComponent<MultiAimConstraint>();
            aim.data.constrainedObject = rig.Head;
            var sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(lookTarget, 1f));
            aim.data.sourceObjects = sources;
            aim.data.aimAxis = MultiAimConstraintData.Axis.Z;
            aim.data.upAxis = MultiAimConstraintData.Axis.Y;
            aim.data.limits = new Vector2(-70f, 70f);
            aim.data.constrainedXAxis = true;
            aim.data.constrainedYAxis = true;
            aim.data.constrainedZAxis = false;
            var builder = rig.Visual.gameObject.AddComponent<RigBuilder>();
            builder.layers.Add(new RigLayer(rigComponent, true));

            var driver = root.AddComponent<CharacterAnimatorDriver>();
            var brain = root.AddComponent<EnemyBrain>();
            driver.Configure(animator, brain, def.moveSpeed, 3.5f);
            brain.Configure(eye, muzzle, lookTarget, driver);

            var flash = root.AddComponent<HitFlash>();
            EditorUtil.SetObjectArray(flash, "renderers", rig.BodyRenderers.ToArray());

            var bar = new GameObject("HealthBar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, 2.45f, 0f);
            GameObject background = EditorUtil.VisualPrimitive(PrimitiveType.Quad, "Background", bar.transform, Vector3.zero, new Vector3(1.04f, 0.14f, 1f), m.BarBackground, castShadows: false);
            GameObject fill = EditorUtil.VisualPrimitive(PrimitiveType.Quad, "Fill", bar.transform, new Vector3(0f, 0f, -0.001f), new Vector3(1f, 0.1f, 1f), m.BarFill, castShadows: false);
            bar.AddComponent<WorldHealthBar>().Configure(health, fill.transform, background.transform);

            EditorUtil.SetLayerRecursively(root, GameLayers.EnemyIndex);

            string path = $"{GamePaths.EnemyPrefabs}/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
