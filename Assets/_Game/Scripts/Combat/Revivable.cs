using System;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>
    /// Down and revive for players and NPC allies: at 0 HP the actor is downed instead of gone;
    /// any living ally (human or NPC) standing within <see cref="ReviveRadius"/> fills the revive
    /// bar and brings it back with part of its health. What happens after a bleed-out is up to the
    /// owner (<c>PlayerRespawn</c> respawns, the NPC regroups with its leader).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public class Revivable : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float reviveTime = 2.5f;
        [SerializeField, Min(0.1f)] float bleedOutTime = 25f;
        [SerializeField, Min(0.5f)] float reviveRadius = 2.4f;
        [SerializeField, Range(0.05f, 1f)] float reviveHealthFraction = 0.45f;

        Health health;
        ICombatActor self;
        ReviveModel model;

        public event Action<Revivable> Downed;

        /// <summary>Revived by an ally (the reviver may be null if it was destroyed).</summary>
        public event Action<Revivable, ICombatActor> RevivedByAlly;

        public event Action<Revivable> BledOut;

        public ReviveModel Model => model ??= new ReviveModel(reviveTime, bleedOutTime);
        public bool IsDowned => Model.IsDowned;
        public bool HasBledOut => Model.HasBledOut;
        public float Progress01 => Model.Progress01;
        public float BleedOutRemaining => Model.BleedOutRemaining;
        public float ReviveRadius => reviveRadius;
        public float ReviveHealthFraction => reviveHealthFraction;
        public ICombatActor CurrentReviver { get; private set; }
        public Health Health => health != null ? health : health = GetComponent<Health>();

        public void Configure(float reviveSeconds, float bleedOutSeconds, float radius, float healthFraction)
        {
            reviveTime = reviveSeconds;
            bleedOutTime = bleedOutSeconds;
            reviveRadius = radius;
            reviveHealthFraction = healthFraction;
            model = null;
        }

        void Awake()
        {
            health = GetComponent<Health>();
            self = GetComponent<ICombatActor>();
        }

        void OnEnable()
        {
            Health.Died += OnDied;
            Health.Revived += OnRevived;
        }

        void OnDisable()
        {
            Health.Died -= OnDied;
            Health.Revived -= OnRevived;
        }

        void OnDied(Health dead, DamageInfo info)
        {
            Model.Down();
            CurrentReviver = null;
            Downed?.Invoke(this);
        }

        void OnRevived(Health revived)
        {
            Model.Clear(); // revived by anything (ally, respawn, reset)
            CurrentReviver = null;
        }

        void Update()
        {
            if (!Model.IsDowned || Model.HasBledOut)
            {
                return;
            }

            CurrentReviver = FindReviver();
            int reviverId = CombatantRegistry.IdOf(CurrentReviver);
            ICombatActor reviver = CurrentReviver;
            switch (Model.Tick(Time.deltaTime, reviverId))
            {
                case ReviveTick.Revived:
                    CurrentReviver = null;
                    Health.Revive(reviveHealthFraction);
                    RevivedByAlly?.Invoke(this, reviver);
                    break;
                case ReviveTick.BledOut:
                    CurrentReviver = null;
                    BledOut?.Invoke(this);
                    break;
            }
        }

        /// <summary>Nearest living ally (not a training target) within revive range.</summary>
        ICombatActor FindReviver()
        {
            ICombatActor best = null;
            float bestDistance = reviveRadius * reviveRadius;
            Vector3 position = transform.position;
            var actors = CombatantRegistry.All; // indexed: no boxed enumerator (GC) in per-frame / per-think loops
            for (int ai = 0; ai < actors.Count; ai++)
            {
                ICombatActor actor = actors[ai];
                if (!IsLivingAlly(actor))
                {
                    continue;
                }

                Vector3 offset = actor.Position - position;
                offset.y *= 0.5f;
                float distance = offset.sqrMagnitude;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = actor;
                }
            }

            return best;
        }

        /// <summary>True when someone on the team is still up and could come to help.</summary>
        public bool HasLivingAlly()
        {
            var actors = CombatantRegistry.All; // indexed: no boxed enumerator (GC) in per-frame / per-think loops
            for (int ai = 0; ai < actors.Count; ai++)
            {
                ICombatActor actor = actors[ai];
                if (IsLivingAlly(actor))
                {
                    return true;
                }
            }

            return false;
        }

        bool IsLivingAlly(ICombatActor actor)
        {
            if (actor == null || actor is UnityEngine.Object o && o == null)
            {
                return false;
            }

            return !ReferenceEquals(actor, self) && actor.IsAlive && actor.Team == Health.Team && !(actor is ITrainingTarget);
        }
    }
}
