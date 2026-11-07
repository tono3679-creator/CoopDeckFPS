using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Weapons
{
    /// <summary>Static data for one weapon type. Runtime state (ammo, spread) lives in <see cref="WeaponState"/>.</summary>
    [CreateAssetMenu(menuName = "CoopDeck/Weapon Definition", fileName = "WD_NewWeapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string weaponId = "weapon.new";
        public string displayName = "New Weapon";

        [Header("Damage")]
        [Min(0f)] public float damage = 18f;
        public Element element = Element.None;
        [Tooltip("Applied on top of the target's zone multiplier when a Head zone is hit.")]
        [Min(1f)] public float headshotMultiplier = 2f;
        [Min(1f)] public float range = 150f;
        [Min(0f)] public float falloffStart = 40f;
        [Range(0f, 1f)] public float minDamageFactor = 0.6f;

        [Header("Fire")]
        [Min(1f)] public float roundsPerMinute = 600f;
        public bool automatic = true;
        [Min(1)] public int magazineSize = 30;
        [Min(0.05f)] public float reloadTime = 1.7f;
        [Min(1)] public int pelletsPerShot = 1;

        [Header("Spread (degrees, cone radius)")]
        [Min(0f)] public float baseSpread = 0.2f;
        [Min(0f)] public float spreadPerShot = 0.2f;
        [Min(0f)] public float maxBloom = 2.4f;
        [Tooltip("Bloom recovered per second.")]
        [Min(0f)] public float bloomRecovery = 7f;
        [Tooltip("Extra spread at full move speed.")]
        [Min(0f)] public float movingSpread = 0.6f;
        [Min(0f)] public float airborneSpread = 1.4f;

        [Header("Recoil (degrees per shot)")]
        [Min(0f)] public float recoilPitch = 0.55f;
        [Min(0f)] public float recoilYaw = 0.22f;
        [Min(0f)] public float firstShotRecoilMultiplier = 1.3f;

        [Header("Presentation")]
        public GameObject muzzleFlashPrefab;
        public GameObject tracerPrefab;
        public GameObject impactPrefab;
        public GameObject damageableImpactPrefab;

        public float SecondsPerShot => 60f / Mathf.Max(1f, roundsPerMinute);
    }
}
