using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Weapons
{
    /// <summary>
    /// Plays muzzle flash, tracer and impact effects for a <see cref="HitscanWeapon"/>.
    /// Swapping the prefabs on the <see cref="WeaponDefinition"/> changes the look without code.
    /// </summary>
    [RequireComponent(typeof(HitscanWeapon))]
    public class WeaponFx : MonoBehaviour
    {
        [SerializeField] HitscanWeapon weapon;
        [Tooltip("Layer for the muzzle flash. Use the ViewModel layer for first-person weapons, -1 to keep the prefab's layer.")]
        [SerializeField] int muzzleFlashLayer = -1;
        [SerializeField, Min(0)] int prewarmCount = 6;

        void Awake()
        {
            if (weapon == null)
            {
                weapon = GetComponent<HitscanWeapon>();
            }
        }

        void Start()
        {
            WeaponDefinition def = weapon.Definition;
            if (def == null)
            {
                return;
            }

            FxPool.Prewarm(def.tracerPrefab, prewarmCount);
            FxPool.Prewarm(def.impactPrefab, prewarmCount);
            FxPool.Prewarm(def.damageableImpactPrefab, prewarmCount);
            FxPool.Prewarm(def.muzzleFlashPrefab, 2);
        }

        void OnEnable()
        {
            weapon.Fired += OnFired;
        }

        void OnDisable()
        {
            weapon.Fired -= OnFired;
        }

        public void SetMuzzleFlashLayer(int layer)
        {
            muzzleFlashLayer = layer;
        }

        void OnFired(ShotEvent shot)
        {
            WeaponDefinition def = weapon.Definition;
            if (def == null)
            {
                return;
            }

            Transform muzzle = weapon.Muzzle;
            if (def.muzzleFlashPrefab != null && muzzle != null)
            {
                FxPool.Spawn(def.muzzleFlashPrefab, muzzle.position, muzzle.rotation, muzzle, muzzleFlashLayer);
            }

            if (def.tracerPrefab != null)
            {
                PooledFx tracer = FxPool.Spawn(def.tracerPrefab, shot.Muzzle, Quaternion.identity);
                if (tracer != null && tracer.TryGetComponent(out TracerFx tracerFx))
                {
                    tracerFx.Play(shot.Muzzle, shot.End);
                }
            }

            if (shot.HitSomething)
            {
                GameObject impact = shot.HitDamageable && def.damageableImpactPrefab != null
                    ? def.damageableImpactPrefab
                    : def.impactPrefab;
                if (impact != null)
                {
                    FxPool.Spawn(impact, shot.End, Quaternion.LookRotation(shot.Normal));
                }
            }
        }
    }
}
