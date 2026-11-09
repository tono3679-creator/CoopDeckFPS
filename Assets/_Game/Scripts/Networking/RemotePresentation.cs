using CoopDeck.Core;
using Unity.Netcode;
using UnityEngine;

namespace CoopDeck.Networking
{
    /// <summary>
    /// The command source of a character simulated on another machine. It issues nothing: the owner runs the
    /// character, this copy only shows the pose it sends. Its <see cref="ControllerKind.Remote"/> keeps HUD and tools
    /// from taking it for the local human.
    /// </summary>
    public sealed class RemoteCommandSource : ICharacterCommandSource, IControllerIdentity
    {
        public static readonly RemoteCommandSource Instance = new RemoteCommandSource();

        public ControllerKind Kind => ControllerKind.Remote;

        public CharacterCommands GetCommands(float deltaTime) => default;
    }

    /// <summary>
    /// Velocity of a character moved by received poses (the full-body animation reads it like the motor's).
    /// Smoothed so the blend tree does not flicker between network ticks; a jump longer than
    /// <see cref="TeleportSpeed"/> (respawn, spawn) resets it instead of showing a burst of speed.
    /// </summary>
    public sealed class RemoteMotion : ILocomotionSource
    {
        public const float TeleportSpeed = 40f;

        readonly float smoothingTime;
        Vector3 lastPosition;
        bool hasPosition;

        public RemoteMotion(float smoothingSeconds = 0.12f)
        {
            smoothingTime = Mathf.Max(0.001f, smoothingSeconds);
        }

        public Vector3 Velocity { get; private set; }

        public bool IsGrounded { get; set; } = true;

        public void Sample(Vector3 position, float deltaTime)
        {
            if (!hasPosition || deltaTime <= 0f)
            {
                lastPosition = position;
                hasPosition = true;
                return;
            }

            Vector3 raw = (position - lastPosition) / deltaTime;
            lastPosition = position;
            if (raw.magnitude > TeleportSpeed)
            {
                Velocity = Vector3.zero;
                return;
            }

            float blend = 1f - Mathf.Exp(-deltaTime / smoothingTime);
            Velocity = Vector3.Lerp(Velocity, raw, blend);
        }

        public void Reset(Vector3 position)
        {
            lastPosition = position;
            hasPosition = true;
            Velocity = Vector3.zero;
        }
    }

    /// <summary>
    /// What the owner sends besides the transform: view pitch (the body's spine and head aim with it) and the
    /// locomotion flags the full-body animation needs. Pitch travels in hundredths of a degree.
    /// </summary>
    public struct NetPose : System.IEquatable<NetPose>, INetworkSerializeByMemcpy
    {
        public short PitchCenti;
        public byte Flags;

        const byte GroundedFlag = 1;
        const byte SprintingFlag = 2;

        public static NetPose From(float pitchDegrees, bool grounded, bool sprinting) => new NetPose
        {
            PitchCenti = (short)Mathf.Clamp(Mathf.RoundToInt(pitchDegrees * 100f), short.MinValue, short.MaxValue),
            Flags = (byte)((grounded ? GroundedFlag : 0) | (sprinting ? SprintingFlag : 0)),
        };

        public float Pitch => PitchCenti / 100f;
        public bool Grounded => (Flags & GroundedFlag) != 0;
        public bool Sprinting => (Flags & SprintingFlag) != 0;

        public bool Equals(NetPose other) => PitchCenti == other.PitchCenti && Flags == other.Flags;
        public override bool Equals(object obj) => obj is NetPose other && Equals(other);
        public override int GetHashCode() => (PitchCenti << 8) ^ Flags;
    }
}
