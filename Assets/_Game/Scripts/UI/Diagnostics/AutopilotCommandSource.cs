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

            return new CharacterCommands
            {
                Move = new Vector2(Mathf.Sin(time * 0.8f) * 0.6f, 0f),
                LookDelta = new Vector2(yawDelta, pitchDelta),
                FireHeld = firing,
                FirePressed = firing && time % cycle < deltaTime,
            };
        }
    }
}
