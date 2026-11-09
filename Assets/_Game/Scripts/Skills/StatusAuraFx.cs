using CoopDeck.Combat;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Skills
{
    /// <summary>
    /// Keeps a status effect's look on a character for exactly as long as the status lasts (the caster spawns it under
    /// the character): the shield bubble until the shield is used up or runs out, the charge crackle while the damage
    /// buff lasts, the mark reticle while the enemy takes extra damage. When it ends, the mesh layers play their outro
    /// (<see cref="ShapeAnimFx"/>) and the particles stop emitting and fade.
    /// </summary>
    public class StatusAuraFx : MonoBehaviour
    {
        public enum Kind : byte
        {
            Shield = 0,
            DamageBuff = 1,
            Vulnerability = 2,
        }

        [SerializeField] Kind kind;
        [Tooltip("Seconds the outro takes before the effect returns to the pool.")]
        [SerializeField, Min(0.05f)] float fadeOut = 0.45f;
        [Tooltip("Grace after spawn before a missing status ends it (the status is applied in the same frame).")]
        [SerializeField, Min(0f)] float grace = 0.1f;

        CombatStatus status;
        PooledFx pooled;
        ShapeAnimFx shapes;
        float age;
        bool ending;
        bool resolved;

        public bool Ending => ending;

        public void Configure(Kind statusKind, float outroSeconds)
        {
            kind = statusKind;
            fadeOut = outroSeconds;
        }

        void OnEnable()
        {
            age = 0f;
            ending = false;
            resolved = false; // the pool may enable a new instance before parenting it: find the character on the first frame
            pooled = GetComponent<PooledFx>();
            shapes = GetComponent<ShapeAnimFx>();
        }

        void Update()
        {
            if (ending)
            {
                return;
            }

            if (!resolved)
            {
                resolved = true;
                status = GetComponentInParent<CombatStatus>();
            }

            age += Time.deltaTime;
            if (age < grace)
            {
                return;
            }

            Health health = status != null ? status.GetComponent<Health>() : null;
            if (status == null || !Active() || (health != null && !health.IsAlive))
            {
                End();
            }
        }

        bool Active()
        {
            StatusEffectsModel model = status.Model;
            switch (kind)
            {
                case Kind.Shield: return model.Shield > 0f;
                case Kind.DamageBuff: return model.DamageBuff > 0f;
                default: return model.Vulnerability > 0f;
            }
        }

        void End()
        {
            ending = true;
            if (shapes != null)
            {
                shapes.Release();
            }

            if (pooled != null)
            {
                pooled.StopEmitting(fadeOut);
            }
        }
    }
}
