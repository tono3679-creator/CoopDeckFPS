using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Combos
{
    /// <summary>
    /// Shows which elements sit on which enemy: a looping effect per element (frost, flames,
    /// sparks, gravity rings) attached to the target while the element lasts. Purely visual: it
    /// only reads <see cref="CombatStatus.Elements"/>, so it can be removed without changing combos.
    /// </summary>
    public class ElementAuraPresenter : MonoBehaviour
    {
        [Tooltip("Looping effect per element, indexed Fire, Ice, Lightning, Gravity.")]
        [SerializeField] GameObject fireAura;
        [SerializeField] GameObject iceAura;
        [SerializeField] GameObject lightningAura;
        [SerializeField] GameObject gravityAura;

        readonly HashSet<CombatStatus> tracked = new HashSet<CombatStatus>();
        readonly Dictionary<(CombatStatus, Element), PooledFx> shown = new Dictionary<(CombatStatus, Element), PooledFx>();
        readonly List<CombatStatus> stale = new List<CombatStatus>();
        readonly List<(CombatStatus, Element)> toRelease = new List<(CombatStatus, Element)>();

        static readonly Element[] Elements = { Element.Fire, Element.Ice, Element.Lightning, Element.Gravity };

        public int ShownCount => shown.Count;

        public void Configure(GameObject fire, GameObject ice, GameObject lightning, GameObject gravity)
        {
            fireAura = fire;
            iceAura = ice;
            lightningAura = lightning;
            gravityAura = gravity;
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
        }
    }
}
