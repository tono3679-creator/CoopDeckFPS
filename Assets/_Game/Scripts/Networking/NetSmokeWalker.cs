using System;
using CoopDeck.Core;
using CoopDeck.Player;
using UnityEngine;

namespace CoopDeck.Networking
{
    /// <summary>
    /// Scripted commands for the unattended smoke run: walks in a circle (sprinting part of the way, strafing and
    /// jumping), and in the face windows stops and turns toward the other player so the screenshot shows them.
    /// It goes through the same path as a human: CharacterCommands → FpsCharacter.Step.
    /// </summary>
    public sealed class NetSmokeWalker : ICharacterCommandSource, IControllerIdentity
    {
        const float CycleSeconds = 8f;
        const float JumpInterval = 2.7f;
        const float MaxTurnSpeed = 240f;
        const float EyeHeight = 1.62f;
        const float ChestHeight = 1.2f;

        readonly Transform body;
        readonly FpsLook look;
        readonly float turnRate;
        readonly Func<Vector3?> target;
        readonly Vector2[] faceWindows;
        float time;
        float nextJump = 1.5f;

        /// <param name="turnDegreesPerSecond">Circle direction and size (walk 6.5 m/s at 45°/s ≈ 8 m radius).</param>
        /// <param name="faceWindows">Seconds (x = start, y = end) during which it stands still facing the other player.</param>
        public NetSmokeWalker(Transform body, FpsLook look, float turnDegreesPerSecond, Func<Vector3?> target, params Vector2[] faceWindows)
        {
            this.body = body;
            this.look = look;
            turnRate = turnDegreesPerSecond;
            this.target = target;
            this.faceWindows = faceWindows ?? Array.Empty<Vector2>();
        }

        public ControllerKind Kind => ControllerKind.Scripted;

        public float Time => time;

        public bool Facing => InFaceWindow(time);

        public CharacterCommands GetCommands(float deltaTime)
        {
            time += deltaTime;
            var c = default(CharacterCommands);

            if (InFaceWindow(time))
            {
                Vector3? other = target?.Invoke();
                if (other.HasValue && look != null)
                {
                    c.LookDelta = TurnToward(other.Value, deltaTime);
                }

                return c;
            }

            float cycle = time % CycleSeconds;
            if (cycle < 6f)
            {
                c.Move = new Vector2(0f, 1f);
                c.LookDelta.x = turnRate * deltaTime;
                c.SprintHeld = cycle >= 3f && cycle < 5f;
            }
            else
            {
                c.Move = new Vector2(Mathf.Sign(turnRate), 0.4f).normalized; // strafe outward a little
            }

            if (look != null)
            {
                float wantPitch = 8f * Mathf.Sin(time * 0.9f);
                c.LookDelta.y = Mathf.Clamp(Mathf.DeltaAngle(look.Pitch, wantPitch), -60f * deltaTime, 60f * deltaTime);
            }

            if (time >= nextJump)
            {
                c.JumpPressed = true;
                nextJump += JumpInterval;
            }

            return c;
        }

        bool InFaceWindow(float t)
        {
            foreach (Vector2 w in faceWindows)
            {
                if (t >= w.x && t < w.y)
                {
                    return true;
                }
            }

            return false;
        }

        Vector2 TurnToward(Vector3 other, float deltaTime)
        {
            Vector3 eye = body.position + Vector3.up * EyeHeight;
            Vector3 to = other + Vector3.up * ChestHeight - eye;
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float wantPitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            float step = MaxTurnSpeed * deltaTime;
            return new Vector2(
                Mathf.Clamp(Mathf.DeltaAngle(look.Yaw, wantYaw), -step, step),
                Mathf.Clamp(Mathf.DeltaAngle(look.Pitch, wantPitch), -step, step));
        }
    }
}
