using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Combos
{
    /// <summary>
    /// Shows which elements sit on which enemy: a looping effect per element (frost, flames,
    /// sparks, gravity rings) attached to the target while the element lasts, and small flames on
    /// the body while a fire damage over time (a burn) ticks. Purely visual: it only reads
    /// <see cref="CombatStatus.Elements"/> and <see cref="CombatStatus.Model"/>, so it can be removed
    /// without changing combos or burns.
    /// </summary>
    public class ElementAuraPresenter : MonoBehaviour
    {
        [Tooltip("Looping effect per element, indexed Fire, Ice, Lightning, Gravity.")]
        [SerializeField] GameObject fireAura;
        [SerializeField] GameObject iceAura;
        [SerializeField] GameObject lightningAura;
        [SerializeField] GameObject gravityAura;

        [Tooltip("Looping flames on the body while a fire damage over time is ticking (synchronised with the burn).")]
        [SerializeField] GameObject burningFx;
        [Tooltip("Seconds the burning flames take to die out after the burn ends.")]
        [SerializeField, Min(0f)] float burningFadeOut = 0.6f;

        readonly HashSet<CombatStatus> tracked = new HashSet<CombatStatus>();
        readonly Dictionary<(CombatStatus, Element), PooledFx> shown = new Dictionary<(CombatStatus, Element), PooledFx>();
        readonly Dictionary<CombatStatus, PooledFx> burning = new Dictionary<CombatStatus, PooledFx>();
        readonly List<CombatStatus> burningEnded = new List<CombatStatus>();
        readonly List<CombatStatus> stale = new List<CombatStatus>();
        readonly List<(CombatStatus, Element)> toRelease = new List<(CombatStatus, Element)>();

        static readonly Element[] Elements = { Element.Fire, Element.Ice, Element.Lightning, Element.Gravity };

        public int ShownCount => shown.Count;

        /// <summary>Targets currently showing the burning flames.</summary>
        public int BurningCount => burning.Count;

        public void Configure(GameObject fire, GameObject ice, GameObject lightning, GameObject gravity, GameObject burn = null)
        {
            fireAura = fire;
            iceAura = ice;
            lightningAura = lightning;
            gravityAura = gravity;
            burningFx = burn;
        }

        void Start()
        {
            FxPool.Prewarm(burningFx, 4);
        }

        void OnEnable()
        {
            CombatEvents.ElementHitApplied += OnElementHit;
        }

        void OnDisable()
        {
            CombatEvents.ElementHitApplied -= OnElementHit;
            foreach (PooledFx fx in shown.Values)
            {
                if (fx != null)
                {
                    fx.ReleaseNow();
                }
            }

            shown.Clear();
            foreach (PooledFx fx in burning.Values)
            {
                if (fx != null)
                {
                    fx.ReleaseNow();
                }
            }

            burning.Clear();
            tracked.Clear();
        }

        void OnElementHit(ElementHit hit)
        {
            if (hit.Target != null && hit.Target.TryGetComponent(out CombatStatus status))
            {
                tracked.Add(status);
            }
        }

        GameObject PrefabFor(Element element)
        {
            switch (element)
            {
                case Element.Fire: return fireAura;
                case Element.Ice: return iceAura;
                case Element.Lightning: return lightningAura;
                case Element.Gravity: return gravityAura;
                default: return null;
            }
        }

        // After the combo system has resolved this frame's hits.
        void LateUpdate()
        {
            stale.Clear();
            foreach (CombatStatus status in tracked)
            {
                if (status == null)
                {
                    stale.Add(status);
                    continue;
                }

                bool any = false;
                foreach (Element element in Elements)
                {
                    var key = (status, element);
                    bool has = status.Elements.Has(element);
                    shown.TryGetValue(key, out PooledFx fx);
                    if (fx != null && !fx.gameObject.activeSelf)
                    {
                        fx = null; // released elsewhere (pool reuse)
                        shown.Remove(key);
                    }

                    if (has && fx == null)
                    {
                        GameObject prefab = PrefabFor(element);
                        if (prefab != null)
                        {
                            PooledFx spawned = FxPool.Spawn(prefab, status.transform.position, Quaternion.identity, status.transform);
                            if (spawned != null)
                            {
                                spawned.transform.localPosition = Vector3.zero;
                                spawned.transform.localScale = Vector3.one;
                                shown[key] = spawned;
                            }
                        }
                    }
                    else if (!has && fx != null)
                    {
                        fx.ReleaseNow();
                        shown.Remove(key);
                    }

                    any |= has;
                }

                any |= UpdateBurning(status);
                if (!any)
                {
                    stale.Add(status);
                }
            }

            foreach (CombatStatus status in stale)
            {
                tracked.Remove(status);
            }

            // Effects whose target was destroyed or forgotten.
            toRelease.Clear();
            foreach (KeyValuePair<(CombatStatus, Element), PooledFx> pair in shown)
            {
                if (pair.Key.Item1 == null || !tracked.Contains(pair.Key.Item1))
                {
                    toRelease.Add(pair.Key);
                }
            }

            foreach ((CombatStatus, Element) key in toRelease)
            {
                if (shown.TryGetValue(key, out PooledFx fx) && fx != null)
                {
                    fx.ReleaseNow();
                }

                shown.Remove(key);
            }

            burningEnded.Clear();
            foreach (KeyValuePair<CombatStatus, PooledFx> pair in burning)
            {
                if (pair.Key == null || !tracked.Contains(pair.Key))
                {
                    burningEnded.Add(pair.Key);
                }
            }

            foreach (CombatStatus status in burningEnded)
            {
                if (burning.TryGetValue(status, out PooledFx fx) && fx != null && fx.gameObject.activeSelf && !fx.Stopping)
                {
                    fx.StopEmitting(burningFadeOut);
                }

                burning.Remove(status);
            }
        }

        /// <summary>Keeps the burning flames in step with the target's fire damage over time. True while it burns.</summary>
        bool UpdateBurning(CombatStatus status)
        {
            bool burns = burningFx != null && status.Model.HasDamageOverTime(Element.Fire)
                         && (!status.TryGetComponent(out Health health) || health.IsAlive);
            burning.TryGetValue(status, out PooledFx fx);
            if (fx != null && (!fx.gameObject.activeSelf || fx.Stopping))
            {
                burning.Remove(status); // finished elsewhere, or fading out from an earlier burn
                fx = null;
            }

            if (burns && fx == null)
            {
                PooledFx spawned = FxPool.Spawn(burningFx, status.transform.position, status.transform.rotation, status.transform);
                if (spawned != null)
                {
                    spawned.transform.localPosition = Vector3.zero;
                    spawned.transform.localRotation = Quaternion.identity;
                    spawned.transform.localScale = Vector3.one;
                    burning[status] = spawned;
                }
            }
            else if (!burns && fx != null)
            {
                fx.StopEmitting(burningFadeOut); // the flames die out with the burn instead of vanishing
                burning.Remove(status);
            }

            return burns;
        }
    }
}
