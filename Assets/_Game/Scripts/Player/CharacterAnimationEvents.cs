using System;
using CoopDeck.Animation;
using CoopDeck.Combat;
using CoopDeck.Weapons;
using UnityEngine;

namespace CoopDeck.Player
{
    /// <summary>
    /// Turns a character's gameplay events into full-body animation calls (players, NPC allies and later network
    /// proxies all use it through <see cref="FpsCharacter"/>): shots, reloads, hits, jumps, dashes, sprint, aim
    /// direction, down / revive / bleed-out / respawn and reviving an ally. Gameplay never waits on any of it.
    /// </summary>
    public sealed class CharacterAnimationEvents : IDisposable
    {
        readonly FpsCharacter character;
        readonly CharacterAnimatorDriver driver;
        readonly FpsMotor motor;
        readonly FpsLook look;
        readonly HitscanWeapon weapon;
        readonly Health health;
        readonly Revivable revivable;
        bool pendingRespawn;

        public CharacterAnimationEvents(FpsCharacter character, CharacterAnimatorDriver driver, FpsMotor motor, FpsLook look,
            HitscanWeapon weapon, Health health, Revivable revivable)
        {
            this.character = character;
            this.driver = driver;
            this.motor = motor;
            this.look = look;
            this.weapon = weapon;
            this.health = health;
            this.revivable = revivable;

            if (weapon != null)
            {
                weapon.Fired += OnFired;
                weapon.ReloadStarted += OnReload;
            }

            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.Died += OnDied;
                health.Revived += OnRevived;
            }

            if (revivable != null)
            {
                revivable.Downed += OnDowned;
                revivable.RevivedByAlly += OnRevivedByAlly;
                revivable.BledOut += OnBledOut;
            }

            if (motor != null)
            {
                motor.Jumped += OnJumped;
                motor.Dashed += OnDashed;
            }
        }

        /// <summary>Per-frame state: sprint, aim direction, reviving someone, and a pending respawn.</summary>
        public void Tick()
        {
            if (driver == null)
            {
                return;
            }

            if (pendingRespawn)
            {
                pendingRespawn = false;
                driver.Respawned();
            }

            if (motor != null)
            {
                driver.SetSprinting(motor.IsSprinting);
            }

            if (look != null)
            {
                driver.SetAim(look.GetAimRay().direction);
            }

            driver.SetReviving(character != null && character.IsAlive && Revivable.IsReviving(character));
        }

        void OnFired(ShotEvent shot) => driver.Fire();

        void OnReload() => driver.Reload(weapon.Definition != null ? weapon.Definition.reloadTime : 1.7f);

        void OnDamaged(DamageInfo info, DamageResult result)
        {
            // a shield soaking the hit still makes the body flinch (feedback that you were hit)
            float taken = result.Damage + result.Absorbed;
            if (result.Applied && taken > 0f && !result.Killed)
            {
                driver.Hit(LocomotionMath.IsHeavyHit(taken, health.Max));
            }
        }

        void OnDied(Health dead, DamageInfo info)
        {
            if (revivable == null)
            {
                driver.SetDead(true); // nobody can revive this character: straight to the death pose
            }
        }

        void OnDowned(Revivable r) => driver.SetDowned(true);

        void OnBledOut(Revivable r) => driver.SetDead(true);

        // Health.Revived fires for ally revives too; RevivedByAlly follows synchronously and cancels the respawn.
        void OnRevived(Health h) => pendingRespawn = true;

        void OnRevivedByAlly(Revivable r, ICombatActor reviver)
        {
            pendingRespawn = false;
            driver.Revive();
        }

        void OnJumped() => driver.Jump();

        void OnDashed()
        {
            Vector3 dir = motor.DashDirection;
            driver.Dash(dir.sqrMagnitude > 0.01f ? dir : character.transform.forward);
        }

        public void Dispose()
        {
            if (weapon != null)
            {
                weapon.Fired -= OnFired;
                weapon.ReloadStarted -= OnReload;
            }

            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
                health.Revived -= OnRevived;
            }

            if (revivable != null)
            {
                revivable.Downed -= OnDowned;
                revivable.RevivedByAlly -= OnRevivedByAlly;
                revivable.BledOut -= OnBledOut;
            }

            if (motor != null)
            {
                motor.Jumped -= OnJumped;
                motor.Dashed -= OnDashed;
            }
        }
    }
}
