using System;
using System.Collections.Generic;
using CoopDeck.Animation;
using CoopDeck.Input;
using CoopDeck.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.Networking
{
    /// <summary>
    /// A player body over the network (Phase 2.5: movement only, owner authority, Docs/MULTIPLAYER_PLAN.md §7).
    /// The owner keeps the single-player path unchanged: devices → <see cref="PlayerInputReader"/> →
    /// CharacterCommands → <see cref="FpsCharacter.Step"/>, and the NetworkTransform (owner authority) sends the
    /// result. Every other machine shows a copy: no input, no simulation, no cameras or first-person parts, the full
    /// body visible and animated from the received pose. Combat, deck and combos are not synchronised yet (Phase 6).
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] FpsCharacter character;
        [SerializeField] PlayerInputReader input;
        [Tooltip("The first-person rig: world camera, view-model camera, audio listener, arms and gun.")]
        [SerializeField] GameObject firstPersonRig;
        [SerializeField] CharacterAnimatorDriver bodyAnimator;
        [Tooltip("Pitch changes smaller than this (degrees) are not sent.")]
        [SerializeField, Min(0f)] float pitchSendThreshold = 0.25f;
        [Tooltip("How fast a copy's pitch follows the received value (1/s).")]
        [SerializeField, Min(0.1f)] float pitchFollowSpeed = 18f;

        static readonly List<NetworkPlayer> spawned = new List<NetworkPlayer>();

        readonly NetworkVariable<NetPose> pose = new NetworkVariable<NetPose>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        RemoteMotion remoteMotion;
        float shownPitch;

        /// <summary>This machine's own player appeared (the scene's lobby camera hands over to it).</summary>
        public static event Action<NetworkPlayer> LocalPlayerSpawned;

        /// <summary>This machine's own player went away (disconnect).</summary>
        public static event Action<NetworkPlayer> LocalPlayerDespawned;

        /// <summary>False for unattended runs: the local player is driven by a script, not this machine's devices.</summary>
        public static bool ReadDevicesForLocalPlayer { get; set; } = true;

        public static IReadOnlyList<NetworkPlayer> Spawned => spawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            spawned.Clear();
            LocalPlayerSpawned = null;
            LocalPlayerDespawned = null;
            ReadDevicesForLocalPlayer = true;
        }

        public FpsCharacter Character => character;

        /// <summary>True on machines that only show this player (another machine simulates it).</summary>
        public bool IsRemoteCopy { get; private set; }

        public NetPose Pose => pose.Value;
        public RemoteMotion RemoteMotion => remoteMotion;
        public bool FirstPersonActive => firstPersonRig != null && firstPersonRig.activeSelf;

        public override void OnNetworkSpawn()
        {
            spawned.Add(this);
            if (IsOwner)
            {
                BecomeLocal();
            }
            else
            {
                BecomeRemote();
            }
        }

        public override void OnNetworkDespawn()
        {
            spawned.Remove(this);
            if (!IsRemoteCopy)
            {
                LocalPlayerDespawned?.Invoke(this);
            }
        }

        /// <summary>This machine plays this body: the single-player setup as it is in PF_Player.</summary>
        public void BecomeLocal()
        {
            IsRemoteCopy = false;
            if (input != null)
            {
                input.enabled = ReadDevicesForLocalPlayer;
            }

            if (firstPersonRig != null)
            {
                firstPersonRig.SetActive(true);
            }

            name = "NetPlayer (local)";
            LocalPlayerSpawned?.Invoke(this);
        }

        /// <summary>
        /// Another machine plays this body: stop its input and simulation, drop the first-person rig (its view-model
        /// layer would otherwise draw over this machine's view) and show the whole body.
        /// </summary>
        public void BecomeRemote()
        {
            IsRemoteCopy = true;
            if (input != null)
            {
                input.enabled = false;
            }

            if (character != null)
            {
                character.SetCommandSource(RemoteCommandSource.Instance);
                character.enabled = false; // the owner simulates; this copy only shows the received pose
            }

            if (firstPersonRig != null)
            {
                firstPersonRig.SetActive(false);
            }

            ShowBody();
            remoteMotion = new RemoteMotion();
            remoteMotion.Reset(transform.position);
            if (bodyAnimator != null)
            {
                bodyAnimator.SetLocomotionSource(remoteMotion);
            }

            shownPitch = pose.Value.Pitch;
            name = $"NetPlayer (client {OwnerClientId})";
        }

        void LateUpdate()
        {
            if (!IsSpawned || character == null)
            {
                return;
            }

            if (IsOwner)
            {
                SendPose();
            }
            else if (IsRemoteCopy)
            {
                ShowPose(Time.deltaTime);
            }
        }

        void SendPose()
        {
            FpsLook look = character.Look;
            FpsMotor motor = character.Motor;
            NetPose next = NetPose.From(look != null ? look.Pitch : 0f, motor == null || motor.IsGrounded, motor != null && motor.IsSprinting);
            NetPose current = pose.Value;
            if (next.Flags != current.Flags || Mathf.Abs(next.Pitch - current.Pitch) >= pitchSendThreshold)
            {
                pose.Value = next;
            }
        }

        void ShowPose(float deltaTime)
        {
            NetPose received = pose.Value;
            remoteMotion.IsGrounded = received.Grounded;
            remoteMotion.Sample(transform.position, deltaTime);

            shownPitch = Mathf.Lerp(shownPitch, received.Pitch, 1f - Mathf.Exp(-pitchFollowSpeed * deltaTime));
            FpsLook look = character.Look;
            if (look != null)
            {
                look.SetRotation(transform.eulerAngles.y, shownPitch); // yaw stays what the NetworkTransform set
            }

            if (bodyAnimator != null)
            {
                bodyAnimator.SetSprinting(received.Sprinting);
                if (look != null)
                {
                    bodyAnimator.SetAim(look.GetAimRay().direction);
                }
            }
        }

        void ShowBody()
        {
            Transform rig = firstPersonRig != null ? firstPersonRig.transform : null;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (rig != null && r.transform.IsChildOf(rig))
                {
                    continue;
                }

                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                {
                    r.shadowCastingMode = ShadowCastingMode.On; // the local player sees only its shadow; others see the body
                }
            }
        }
    }
}
