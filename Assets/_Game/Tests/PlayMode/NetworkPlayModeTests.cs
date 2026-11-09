using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CoopDeck.Core;
using CoopDeck.Input;
using CoopDeck.Networking;
using CoopDeck.Player;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Phase 2.5 in one process: hosting in the Net_Smoke scene (local player spawns, owns its body, moves through
    /// CharacterCommands, hands the view back on leave) and how a copy of another machine's player is shown.
    /// Two real processes are checked by the unattended run (Docs/MULTIPLAYER_PLAN.md §7).
    /// </summary>
    public class NetworkPlayModeTests
    {
        const string ScenePath = "Assets/_Game/Scenes/Net_Smoke.unity";
        const string NetPlayerPath = "Assets/_Game/Prefabs/Characters/PF_NetPlayer.prefab";
        const float Dt = 1f / 60f;

        Scene scene;
        GameObject copy;

        sealed class Forward : ICharacterCommandSource, IControllerIdentity
        {
            public ControllerKind Kind => ControllerKind.Scripted;

            public CharacterCommands GetCommands(float deltaTime) => new CharacterCommands { Move = new Vector2(0f, 1f), LookDelta = new Vector2(0f, 0.5f) };
        }

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = Dt;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (copy != null)
            {
                Object.Destroy(copy);
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network != null)
            {
                if (network.IsListening)
                {
                    network.Shutdown();
                }

                while (network != null && network.ShutdownInProgress)
                {
                    yield return null;
                }

                Object.Destroy(network.gameObject); // it lives in DontDestroyOnLoad
            }

            if (scene.IsValid() && scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }

            yield return null;
        }

        IEnumerator LoadSmokeScene()
        {
            AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            while (!load.isDone)
            {
                yield return null;
            }

            scene = SceneManager.GetSceneByPath(ScenePath);
            yield return null; // Start
        }

        static ushort FreePort()
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
        }

        NetSmokeSession Session() => Object.FindAnyObjectByType<NetSmokeSession>();

        static void SetPort(NetSmokeSession session, ushort port)
        {
            typeof(NetSmokeSession).GetField("portField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(session, port.ToString());
        }

        [UnityTest]
        public IEnumerator Host_SpawnsTheLocalPlayerWhichOwnsAndDrivesItsBody()
        {
            yield return LoadSmokeScene();
            NetSmokeSession session = Session();
            Assert.That(session, Is.Not.Null);
            Camera lobby = scene.GetRootGameObjects().Select(r => r.GetComponent<Camera>()).First(c => c != null);
            Assert.That(lobby.gameObject.activeSelf, Is.True, "the lobby camera shows the yard before anyone joins");

            SetPort(session, FreePort());
            Assert.That(session.StartHost(), Is.True);
            for (int i = 0; i < 30 && NetworkPlayer.Spawned.Count == 0; i++)
            {
                yield return null;
            }

            Assert.That(NetworkPlayer.Spawned.Count, Is.EqualTo(1));
            NetworkPlayer local = NetworkPlayer.Spawned[0];
            Assert.That(local.IsOwner, Is.True);
            Assert.That(local.IsRemoteCopy, Is.False);
            Assert.That(local.FirstPersonActive, Is.True);
            Assert.That(local.Character.enabled, Is.True);
            Assert.That(local.Character.ControllerKind, Is.EqualTo(ControllerKind.Human), "devices → PlayerInputReader as in single player");
            Assert.That(local.GetComponent<PlayerInputReader>().enabled, Is.True);
            Assert.That(local.GetComponent<NetworkTransform>().CanCommitToTransform, Is.True, "the owner sends the transform");
            Assert.That(lobby.gameObject.activeSelf, Is.False, "the player's camera took over");
            Assert.That((local.transform.position - NetSmokeTestPoints.HostSpawn).magnitude, Is.LessThan(0.5f), "approval put the host on spawn 0");
            Assert.That(FpsCharacter.FindLocalHuman(), Is.EqualTo(local.Character));

            // Input → CharacterCommands → FpsCharacter.Step still moves the body with the network layer on top.
            local.Character.SetCommandSource(new Forward());
            Vector3 start = local.transform.position;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.That(Vector3.Distance(start, local.transform.position), Is.GreaterThan(1f));
            // Written in LateUpdate: the coroutine reads it one frame (0.5°) behind the look.
            Assert.That(local.Pose.Pitch, Is.EqualTo(local.Character.Look.Pitch).Within(0.75f), "the owner publishes its pitch");
            Assert.That(local.Pose.Pitch, Is.GreaterThan(10f));

            session.Leave();
            for (int i = 0; i < 60 && (NetworkPlayer.Spawned.Count > 0 || session.Network.ShutdownInProgress); i++)
            {
                yield return null;
            }

            Assert.That(NetworkPlayer.Spawned.Count, Is.EqualTo(0));
            Assert.That(lobby.gameObject.activeSelf, Is.True, "leaving hands the view back to the lobby camera");
        }

        [UnityTest]
        public IEnumerator RemoteCopy_ShowsTheBodyWithoutInputCamerasOrSimulation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
            copy = Object.Instantiate(prefab, new Vector3(40f, 0.05f, 40f), Quaternion.identity);
            var player = copy.GetComponent<NetworkPlayer>();
            yield return null;

            player.BecomeRemote();
            yield return null;

            Assert.That(player.IsRemoteCopy, Is.True);
            Assert.That(player.Character.ControllerKind, Is.EqualTo(ControllerKind.Remote));
            Assert.That(player.Character.enabled, Is.False, "the owner simulates, not this copy");
            Assert.That(copy.GetComponent<PlayerInputReader>().enabled, Is.False);
            Assert.That(player.FirstPersonActive, Is.False, "no second camera, listener or view model");
            Assert.That(copy.GetComponentsInChildren<Camera>().Length, Is.EqualTo(0));
            Assert.That(FpsCharacter.FindLocalHuman(), Is.Not.EqualTo(player.Character), "never taken for the local player");

            Renderer[] body = copy.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.layer != GameLayers.ViewModelIndex).ToArray();
            Assert.That(body.Length, Is.GreaterThan(0));
            Assert.That(body.All(r => r.shadowCastingMode != ShadowCastingMode.ShadowsOnly), Is.True, "others see the whole body");

            Vector3 before = copy.transform.position;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.That(copy.transform.position, Is.EqualTo(before), "nothing moves a copy but the received transform");
        }
    }

    static class NetSmokeTestPoints
    {
        public static readonly Vector3 HostSpawn = new Vector3(-5f, 0.05f, -6f);
    }
}
