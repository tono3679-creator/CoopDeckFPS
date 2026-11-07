using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>
    /// A hazard volume that damages everything of an opposing team standing in it at a fixed
    /// interval (sandbox: lets you test heal and shield cards). Uses an overlap query, so it
    /// works with CharacterControllers without trigger setup.
    /// </summary>
    public class DamageZone : MonoBehaviour
    {
        [SerializeField] Vector3 size = new Vector3(4f, 2f, 4f);
        [SerializeField, Min(0f)] float damagePerTick = 6f;
        [SerializeField, Min(0.05f)] float interval = 0.5f;
        [SerializeField] Element element = Element.Fire;
        [SerializeField] TeamId team = TeamId.Enemies;

        readonly HashSet<Health> hitThisTick = new HashSet<Health>();
        float timer;

        public void Configure(Vector3 volumeSize, float damage, float tickInterval, Element damageElement)
        {
            size = volumeSize;
            damagePerTick = damage;
            interval = tickInterval;
            element = damageElement;
        }

        void Update()
        {
            timer += Time.deltaTime;
            if (timer < interval)
            {
                return;
            }

            timer -= interval;
            hitThisTick.Clear();
            Vector3 center = transform.position + Vector3.up * (size.y * 0.5f);
            foreach (Collider collider in Physics.OverlapBox(center, size * 0.5f, transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Health health = collider.GetComponentInParent<Health>();
                if (health == null || !health.IsAlive || health.Team == team || !hitThisTick.Add(health))
                {
                    continue;
                }

                health.ApplyDamage(new DamageInfo
                {
                    Amount = damagePerTick,
                    Element = element,
                    Kind = DamageKind.Environment,
                    SourceTeam = team,
                    Point = health.transform.position + Vector3.up,
                    Normal = Vector3.up,
                });
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.4f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.up * (size.y * 0.5f), size);
        }
    }
}
