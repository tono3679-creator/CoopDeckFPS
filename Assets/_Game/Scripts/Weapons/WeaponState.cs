using UnityEngine;

namespace CoopDeck.Weapons
{
    /// <summary>
    /// Ammo, fire cadence, reload and bloom as plain data (no physics, no MonoBehaviour).
    /// The fire cooldown carries its remainder across frames so the fire rate is exact at any frame rate.
    /// </summary>
    public sealed class WeaponState
    {
        readonly float secondsPerShot;
        readonly float reloadDuration;
        readonly float spreadPerShot;
        readonly float maxBloom;
        readonly float bloomRecovery;

        float cooldown;
        float reloadRemaining;

        public WeaponState(int magazineSize, float secondsPerShot, float reloadDuration,
            float spreadPerShot = 0f, float maxBloom = 0f, float bloomRecovery = 0f)
        {
            MagazineSize = Mathf.Max(1, magazineSize);
            this.secondsPerShot = Mathf.Max(0.001f, secondsPerShot);
            this.reloadDuration = Mathf.Max(0.01f, reloadDuration);
            this.spreadPerShot = Mathf.Max(0f, spreadPerShot);
            this.maxBloom = Mathf.Max(0f, maxBloom);
            this.bloomRecovery = Mathf.Max(0f, bloomRecovery);
            Ammo = MagazineSize;
        }

        public static WeaponState FromDefinition(WeaponDefinition definition)
        {
            return new WeaponState(definition.magazineSize, definition.SecondsPerShot, definition.reloadTime,
                definition.spreadPerShot, definition.maxBloom, definition.bloomRecovery);
        }

        public int MagazineSize { get; }
        public int Ammo { get; private set; }
        public float Bloom { get; private set; }
        public int ShotsSinceTriggerDown { get; private set; }
        public bool IsReloading => reloadRemaining > 0f;
        public float ReloadProgress => IsReloading ? 1f - reloadRemaining / reloadDuration : 0f;
        public bool IsCoolingDown => cooldown > 0f;
        public bool CanFire => !IsReloading && cooldown <= 0f && Ammo > 0;
        public bool NeedsReload => Ammo <= 0 && !IsReloading;

        /// <summary>Consumes one round if the weapon can fire right now.</summary>
        public bool TryFire()
        {
            if (!CanFire)
            {
                return false;
            }

            Ammo--;
            cooldown += secondsPerShot;
            Bloom = Mathf.Min(maxBloom, Bloom + spreadPerShot);
            ShotsSinceTriggerDown++;
            return true;
        }

        /// <summary>Starts a reload. Returns false when already reloading or the magazine is full.</summary>
        public bool StartReload()
        {
            if (IsReloading || Ammo >= MagazineSize)
            {
                return false;
            }

            reloadRemaining = reloadDuration;
            return true;
        }

        public void CancelReload()
        {
            reloadRemaining = 0f;
        }

        /// <summary>Advances timers. Returns true on the frame a reload completes.</summary>
        public bool Tick(float deltaTime, bool triggerHeld)
        {
            // A negative cooldown is the part of this frame left over after the next shot became
            // due; it keeps the fire rate exact at any frame rate. It only exists while a shot is
            // pending: a weapon that is already ready (or cannot shoot: trigger up, reloading,
            // empty) never banks time, so the first trigger pull always fires exactly one round.
            if (cooldown > 0f)
            {
                cooldown -= deltaTime;
            }
            else
            {
                cooldown = 0f;
            }

            if (!triggerHeld || IsReloading || Ammo <= 0)
            {
                cooldown = Mathf.Max(cooldown, 0f);
            }

            if (!triggerHeld)
            {
                ShotsSinceTriggerDown = 0;
            }

            Bloom = Mathf.MoveTowards(Bloom, 0f, bloomRecovery * deltaTime);

            if (!IsReloading)
            {
                return false;
            }

            reloadRemaining -= deltaTime;
            if (reloadRemaining > 0f)
            {
                return false;
            }

            reloadRemaining = 0f;
            Ammo = MagazineSize;
            return true;
        }
    }
}
