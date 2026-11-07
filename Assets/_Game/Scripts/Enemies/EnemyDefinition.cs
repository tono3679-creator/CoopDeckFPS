using UnityEngine;

namespace CoopDeck.Enemies
{
    public enum EnemyAttackKind
    {
        /// <summary>Closes in and strikes after a visible wind-up.</summary>
        Melee = 0,

        /// <summary>Keeps distance and fires a slow, dodgeable projectile after a wind-up.</summary>
        Projectile = 1,
    }

    /// <summary>Tuning for one enemy type. Prefabs reference one of these; spawners only pick prefabs.</summary>
    [CreateAssetMenu(menuName = "CoopDeck/Enemy Definition", fileName = "ED_NewEnemy")]
    public class EnemyDefinition : ScriptableObject
    {
        public string enemyId = "enemy.new";
        public string displayName = "New Enemy";

        [Header("Body")]
        [Min(1f)] public float maxHealth = 120f;
        [Min(0.1f)] public float moveSpeed = 4f;
        [Min(0.1f)] public float acceleration = 20f;
        [Min(1f)] public float angularSpeed = 720f;

        [Header("Senses")]
        [Tooltip("Notices a target in line of sight within this distance (or immediately when hit).")]
        [Min(1f)] public float detectionRange = 30f;
        [Tooltip("Gives up the chase beyond this distance.")]
        [Min(1f)] public float loseInterestRange = 60f;

        [Header("Attack")]
        public EnemyAttackKind attackKind = EnemyAttackKind.Melee;
        [Tooltip("Starts the wind-up when the target is this close and visible.")]
        [Min(0.5f)] public float attackRange = 2.2f;
        [Tooltip("Where the agent stops approaching.")]
        [Min(0.1f)] public float preferredRange = 1.6f;
        [Min(0f)] public float damage = 14f;
        [Tooltip("Telegraph time before the hit lands: the player's window to dodge.")]
        [Min(0f)] public float windup = 0.55f;
        [Min(0f)] public float recover = 0.35f;
        [Min(0f)] public float cooldown = 1.4f;
        [Tooltip("Melee only: half-angle in front of the enemy that the strike covers.")]
        [Range(10f, 180f)] public float meleeArc = 70f;

        [Header("Projectile")]
        public GameObject projectilePrefab;
        [Min(1f)] public float projectileSpeed = 22f;
        [Min(0.05f)] public float projectileRadius = 0.25f;

        [Header("Presentation")]
        public GameObject windupFx;
        public GameObject deathFx;
    }
}
