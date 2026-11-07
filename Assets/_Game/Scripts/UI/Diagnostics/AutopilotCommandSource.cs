using System;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.UI.Diagnostics
{
    /// <summary>
    /// Scripted commands for unattended runs: sweeps the view left and right around the starting
    /// direction, strafes, fires in bursts and pulls the aim back down against recoil.
    /// Plugging it into a character proves the character does not depend on real input.
    /// </summary>
    public sealed class AutopilotCommandSource : ICharacterCommandSource
    {
        readonly Func<float> currentPitch;
        readonly float sweepDegrees;
        readonly float sweepSpeed;
        readonly float burstOn;
        readonly float burstOff;
        float time;
        float nextSkillTime = 1.5f;
        int nextSkillSlot;

        /// <summary>Seconds between skill-card uses (cycles through the three slots). 0 disables skills.</summary>
        public float SkillInterval { get; set; } = 2.2f;

        /// <summary>False: never pulls the trigger (skills only), e.g. so enemies live long enough for combos.</summary>
        public bool FireWeapon { get; set; } = true;

        /// <summary>
        /// Optional point to aim at (e.g. the nearest enemy's chest). When it returns a value the
        /// autopilot turns toward it and only fires once on target; null keeps the sweep.
        /// Needs <see cref="AimReader"/>.
        /// </summary>
        public Func<Vector3?> TargetProvider { get; set; }

        /// <summary>The character's current aim ray, used with <see cref="TargetProvider"/>.</summary>
        public Func<Ray> AimReader { get; set; }

        /// <summary>Maximum turn rate while tracking a target, degrees per second.</summary>
        public float TrackingTurnSpeed { get; set; } = 240f;

        /// <param name="pitchReader">Returns the character's current pitch (degrees, up positive) for recoil control. May be null.</param>
        public AutopilotCommandSource(Func<float> pitchReader = null, float sweepAmplitudeDegrees = 35f,
            float sweepRadiansPerSecond = 0.5f, float burstSeconds = 1.4f, float pauseSeconds = 0.6f)
        {
            currentPitch = pitchReader;
            sweepDegrees = sweepAmplitudeDegrees;
            sweepSpeed = sweepRadiansPerSecond;
            burstOn = burstSeconds;
            burstOff = pauseSeconds;
        }

        public CharacterCommands GetCommands(float deltaTime)
        {
            time += deltaTime;
            float cycle = burstOn + burstOff;
            bool firing = cycle > 0f && time % cycle < burstOn;

            // Derivative of yaw = A·sin(ωt): oscillates around the starting direction.
            float yawDelta = sweepDegrees * sweepSpeed * Mathf.Cos(time * sweepSpeed) * deltaTime;

            // Aim slightly below the horizon (targets are at chest height) and fight recoil climb.
            float pitch = currentPitch != null ? currentPitch() : 0f;
            float pitchDelta = Mathf.Clamp((-2f - pitch) * 4f, -60f, 60f) * deltaTime;

            Vector3? target = TargetProvider != null && AimReader != null ? TargetProvider() : null;
            if (target.HasValue)
            {
                Ray aim = AimReader();
                Vector3 to = target.Value - aim.origin;
                float flat = new Vector2(to.x, to.z).magnitude;
                float yawError = Mathf.DeltaAngle(Mathf.Atan2(aim.direction.x, aim.direction.z) * Mathf.Rad2Deg,
                    Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float pitchError = Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg - Mathf.Asin(Mathf.Clamp(aim.direction.y, -1f, 1f)) * Mathf.Rad2Deg;
                yawDelta = Mathf.Clamp(yawError * 12f, -TrackingTurnSpeed, TrackingTurnSpeed) * deltaTime;
                pitchDelta = Mathf.Clamp(pitchError * 12f, -TrackingTurnSpeed, TrackingTurnSpeed) * deltaTime;
                firing &= Mathf.Abs(yawError) < 4f && Mathf.Abs(pitchError) < 4f;
            }

            var commands = new CharacterCommands
            {
                Move = new Vector2(Mathf.Sin(time * 0.8f) * 0.6f, 0f),
                LookDelta = new Vector2(yawDelta, pitchDelta),
                FireHeld = firing && FireWeapon,
                FirePressed = firing && FireWeapon && time % cycle < deltaTime,
            };

            // Skills fire alongside the gun, like a player would.
            if (SkillInterval > 0f && time >= nextSkillTime)
            {
                commands.SetSkillPressed(nextSkillSlot);
                nextSkillSlot = (nextSkillSlot + 1) % CharacterCommands.SkillSlotCount;
                nextSkillTime = time + SkillInterval;
            }

            return commands;
        }
    }
}
