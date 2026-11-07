using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>
    /// A slow, visible enemy shot: travels in a straight line, sphere-casts ahead each frame and
    /// damages the first hostile it touches. Pooled through <see cref="FxPool"/>.
    /// </summary>
    [RequireComponent(typeof(PooledFx))]
    public class EnemyProjectile : MonoBehaviour
    {
        [SerializeField] GameObject impactFx;

        Vector3 velocity;
        float damage;
        float radius = 0.25f;
        ICombatActor source;
        TeamId team = TeamId.Enemies;
        int mask = Physics.DefaultRaycastLayers;
        float remainingLife;
        bool flying;
        PooledFx pooled;
        TrailRenderer trail;

        public bool IsFlying => flying;

        void Awake()
        {
            pooled = GetComponent<PooledFx>();
            trail = GetComponentInChildren<TrailRenderer>();
        }

        public void SetImpactFx(GameObject prefab)
        {
            impactFx = prefab;
        }

        public void Launch(Vector3 position, Vector3 direction, float speed, float shotDamage, float shotRadius,
            ICombatActor shooter, TeamId shooterTeam, int hitMask, float lifetime = 3f)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            velocity = direction.normalized * speed;
            damage = shotDamage;
            radius = shotRadius;
            source = shooter;
            team = shooterTeam;
            mask = hitMask;
            remainingLife = lifetime;
            flying = true;
            if (trail != null)
            {
                trail.Clear();
            }
        }

        void Update()
        {
            if (!flying)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector3 step = velocity * dt;
            float distance = step.magnitude;
            if (distance > 0f && Physics.SphereCast(transform.position, radius, step / distance, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
            {
                Health target = hit.collider.GetComponentInParent<Health>();
                if (target != null && target.IsAlive && target.Team != team && DamageRules.CanDamage(team, target.Team))
                {
                    target.ApplyDamage(new DamageInfo
                    {
                        Amount = damage,
                        Kind = DamageKind.Weapon,
                        Source = source,
                        SourceTeam = team,
                        Point = hit.point,
                        Normal = hit.normal,
                        Direction = velocity.normalized,
                    });
                }

                Finish(hit.point, hit.normal);
                return;
            }

            transform.position += step;
            remainingLife -= dt;
            if (remainingLife <= 0f)
            {
                Finish(transform.position, -velocity.normalized);
            }
        }

        void Finish(Vector3 point, Vector3 normal)
        {
            flying = false;
            if (impactFx != null)
            {
                FxPool.Spawn(impactFx, point, Quaternion.LookRotation(normal));
            }

            if (pooled != null)
            {
                pooled.ReleaseNow();
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
