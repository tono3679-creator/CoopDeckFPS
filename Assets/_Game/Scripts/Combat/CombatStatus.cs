using System;
using System.Collections.Generic;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Combat
{
    /// <summary>
    /// Runtime host for <see cref="StatusEffectsModel"/>: applies damage-over-time ticks to the
    /// owner's <see cref="Health"/> and clears everything on death or respawn. <see cref="Health"/>
    /// reads the shield and vulnerability from here when damage arrives.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatStatus : MonoBehaviour
    {
        readonly StatusEffectsModel model = new StatusEffectsModel();
        readonly ElementAuraModel elements = new ElementAuraModel();
        readonly List<DotTick> ticks = new List<DotTick>();
        Health health;

        public StatusEffectsModel Model => model;

        /// <summary>Elements on this actor (combo partners). Kept until the end of the frame of death so a killing blow can still finish a combo.</summary>
        public ElementAuraModel Elements => elements;

        /// <summary>Movement factor from elements (Ice / Gravity slow). AI applies it to its agent.</summary>
        public float MoveSpeedMultiplier => elements.MoveSpeedMultiplier;

        /// <summary>Raised when a status is added (UI refresh).</summary>
        public event Action Changed;

        void Awake()
        {
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            if (health != null)
            {
                health.Died += OnDied;
                health.Revived += OnRevived;
            }
        }

        void OnDisable()
        {
            if (health != null)
            {
                health.Died -= OnDied;
                health.Revived -= OnRevived;
            }
        }

        void OnDied(Health dead, DamageInfo info) => model.Clear();

        void OnRevived(Health revived)
        {
            model.Clear();
            elements.Clear(includeCooldowns: true);
        }

        void Update()
        {
            ticks.Clear();
            model.Tick(Time.deltaTime, ticks);
            elements.Tick(Time.deltaTime);
            if (health == null)
            {
                return;
            }

            if (!health.IsAlive && elements.ActiveCount > 0)
            {
                elements.Clear(); // the dead keep no elements (cleared a frame later, see Elements)
            }

            foreach (DotTick tick in ticks)
            {
                if (!health.IsAlive)
                {
                    break;
                }

                health.ApplyDamage(new DamageInfo
                {
                    Amount = tick.Amount,
                    Element = tick.Element,
                    Kind = DamageKind.Status,
                    Source = tick.Source,
                    SourceTeam = tick.SourceTeam,
                    Point = transform.position + Vector3.up * 1.2f,
                    Normal = Vector3.up,
                });
            }
        }

        public void AddShield(float amount, float duration)
        {
            model.AddShield(amount, duration);
            Changed?.Invoke();
        }

        public void AddDamageBuff(float bonus, float duration)
        {
            model.AddDamageBuff(bonus, duration);
            Changed?.Invoke();
        }

        public void AddVulnerability(float bonus, float duration)
        {
            model.AddVulnerability(bonus, duration);
            Changed?.Invoke();
        }

        public void AddDamageOverTime(ICombatActor source, TeamId sourceTeam, Element element, float damagePerSecond, float duration)
        {
            model.AddDamageOverTime(source, sourceTeam, element, damagePerSecond, duration);
            Changed?.Invoke();
        }
    }
}
