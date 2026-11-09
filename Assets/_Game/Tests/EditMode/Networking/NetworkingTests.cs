using System.IO;
using System.Linq;
using CoopDeck.Core;
using CoopDeck.Input;
using CoopDeck.Networking;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopDeck.Tests
{
    /// <summary>Phase 2.5 networking: launch arguments, the pose format, remote motion and the generated assets.</summary>
    public class NetworkingTests
    {
        const string NetPlayerPath = "Assets/_Game/Prefabs/Characters/PF_NetPlayer.prefab";
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string ScenePath = "Assets/_Game/Scenes/Net_Smoke.unity";

        // ------------------------------------------------------------------ launch arguments

        [Test]
        public void LaunchOptions_NoArgumentsMeansNoNetworking()
        {
            NetLaunchOptions o = NetLaunchOptions.Parse(new[] { "CoopDeckFPS.exe", "-perfrun", "30" });
            Assert.That(o.Role, Is.EqualTo(NetRole.None));
            Assert.That(o.Port, Is.EqualTo(NetLaunchOptions.DefaultPort));
            Assert.That(o.IsAuto, Is.False);
        }

        [Test]
        public void LaunchOptions_HostListensOnLoopbackByDefault()
        {
            NetLaunchOptions o = NetLaunchOptions.Parse(new[] { "x.exe", "-nethost", "-netport", "7790" });
            Assert.That(o.Role, Is.EqualTo(NetRole.Host));
            Assert.That(o.ListenAddress, Is.EqualTo(NetLaunchOptions.Loopback), "no firewall prompt unless asked for the LAN");
            Assert.That(o.Port, Is.EqualTo(7790));
        }

        [Test]
        public void LaunchOptions_ClientAddressIsOptional()
        {
            NetLaunchOptions withAddress = NetLaunchOptions.Parse(new[] { "x.exe", "-netclient", "192.168.1.20", "-netlisten", "0.0.0.0" });
            Assert.That(withAddress.Role, Is.EqualTo(NetRole.Client));
            Assert.That(withAddress.Address, Is.EqualTo("192.168.1.20"));
            Assert.That(withAddress.ListenAddress, Is.EqualTo("0.0.0.0"));

            NetLaunchOptions bare = NetLaunchOptions.Parse(new[] { "x.exe", "-netclient", "-netauto", "26" });
            Assert.That(bare.Address, Is.EqualTo(NetLaunchOptions.Loopback), "the next switch is not an address");
            Assert.That(bare.AutoSeconds, Is.EqualTo(26f));
        }

        [Test]
        public void LaunchOptions_UnattendedRunAndBadValues()
        {
            NetLaunchOptions o = NetLaunchOptions.Parse(new[]
            {
                "x.exe", "-NETCLIENT", "10.0.0.2", "-netauto", "26.5", "-netout", @"PerfCaptures\net_client.json", "-netreconnect", "14",
                "-netport", "0",
            });
            Assert.That(o.Role, Is.EqualTo(NetRole.Client), "switches ignore case");
            Assert.That(o.IsAuto, Is.True);
            Assert.That(o.AutoSeconds, Is.EqualTo(26.5f));
            Assert.That(o.OutputPath, Is.EqualTo(@"PerfCaptures\net_client.json"));
            Assert.That(o.ReconnectAt, Is.EqualTo(14f));
            Assert.That(o.Port, Is.EqualTo(NetLaunchOptions.DefaultPort), "port 0 is ignored");
        }

        // ------------------------------------------------------------------ pose and remote motion

        [Test]
        public void NetPose_KeepsPitchToAHundredthOfADegreeAndFlags()
        {
            NetPose pose = NetPose.From(-37.456f, grounded: false, sprinting: true);
            Assert.That(pose.Pitch, Is.EqualTo(-37.46f).Within(0.0051f));
            Assert.That(pose.Grounded, Is.False);
            Assert.That(pose.Sprinting, Is.True);
            Assert.That(NetPose.From(85f, true, false), Is.EqualTo(NetPose.From(85.001f, true, false)));
            Assert.That(NetPose.From(85f, true, false), Is.Not.EqualTo(NetPose.From(85f, false, false)));
        }

        [Test]
        public void RemoteMotion_FollowsReceivedMovement()
        {
            var motion = new RemoteMotion(0.12f);
            Vector3 p = Vector3.zero;
            motion.Sample(p, 1f / 60f);
            Assert.That(motion.Velocity, Is.EqualTo(Vector3.zero), "the first sample only sets the start");

            for (int i = 0; i < 60; i++)
            {
                p += new Vector3(6.5f / 60f, 0f, 0f);
                motion.Sample(p, 1f / 60f);
            }

            Assert.That(motion.Velocity.x, Is.EqualTo(6.5f).Within(0.05f), "one second at walking speed");
            Assert.That(motion.Velocity.z, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void RemoteMotion_ATeleportIsNotShownAsSpeed()
        {
            var motion = new RemoteMotion();
            motion.Sample(Vector3.zero, 1f / 60f);
            motion.Sample(new Vector3(0.1f, 0f, 0f), 1f / 60f);
            Assert.That(motion.Velocity.magnitude, Is.GreaterThan(0f));

            motion.Sample(new Vector3(30f, 0f, 0f), 1f / 60f); // respawn across the yard
            Assert.That(motion.Velocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void RemoteCommandSource_IssuesNothingAndIsRemote()
        {
            ICharacterCommandSource source = RemoteCommandSource.Instance;
            Assert.That(((IControllerIdentity)source).Kind, Is.EqualTo(ControllerKind.Remote));
            CharacterCommands c = source.GetCommands(0.1f);
            Assert.That(c.Move, Is.EqualTo(Vector2.zero));
            Assert.That(c.FireHeld || c.JumpPressed || c.SkillPressedMask != 0, Is.False);
        }

        // ------------------------------------------------------------------ generated assets

        [Test]
        public void NetPlayer_IsAVariantOfThePlayerWithOwnerAuthoritySync()
        {
            var netPlayer = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.That(netPlayer, Is.Not.Null, "run the bootstrap (or CoopDeck > Setup > Build Net Smoke Scene)");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(netPlayer), Is.EqualTo(player), "PF_Player stays the single source");
            Assert.That(player.GetComponent<NetworkObject>(), Is.Null, "PF_Player itself has no network parts");

            var networkObject = netPlayer.GetComponent<NetworkObject>();
            Assert.That(networkObject, Is.Not.Null);
            Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

            var sync = netPlayer.GetComponent<NetworkTransform>();
            Assert.That(sync.AuthorityMode, Is.EqualTo(NetworkTransform.AuthorityModes.Owner));
            Assert.That(sync.SyncRotAngleY, Is.True);
            Assert.That(sync.SyncRotAngleX || sync.SyncRotAngleZ, Is.False, "pitch travels separately; the body only yaws");
            Assert.That(sync.SyncScaleX || sync.SyncScaleY || sync.SyncScaleZ, Is.False);
            Assert.That(sync.Interpolate, Is.True);

            Assert.That(netPlayer.GetComponent<PlayerInputReader>().enabled, Is.False, "only the owner turns input on");

            var so = new SerializedObject(netPlayer.GetComponent<NetworkPlayer>());
            foreach (string field in new[] { "character", "input", "firstPersonRig", "bodyAnimator" })
            {
                Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            }

            var rig = (GameObject)so.FindProperty("firstPersonRig").objectReferenceValue;
            Assert.That(rig.GetComponentInChildren<Camera>(true), Is.Not.Null, "the rig holds the cameras");
            Assert.That(rig.GetComponentInChildren<AudioListener>(true), Is.Not.Null);
        }

        [Test]
        public void SmokeScene_IsInTheBuildWithAConfiguredNetworkManager()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(scenes[0].path, Does.EndWith("Sandbox_Phase1.unity"), "the game still starts in the sandbox");
            Assert.That(scenes.Any(s => s.path == ScenePath && s.enabled), Is.True);
            Assert.That(Path.GetFileNameWithoutExtension(ScenePath), Is.EqualTo(NetLaunchOptions.SceneName));

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                NetworkManager network = roots.Select(r => r.GetComponent<NetworkManager>()).Single(n => n != null);
                NetworkConfig config = network.NetworkConfig;
                var netPlayer = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
                Assert.That(config.PlayerPrefab, Is.EqualTo(netPlayer));
                Assert.That(config.NetworkTransport, Is.InstanceOf<UnityTransport>());
                Assert.That(config.ConnectionApproval, Is.True, "approval puts each player on a spawn point");
                Assert.That(config.EnableSceneManagement, Is.False);
                Assert.That(config.TickRate, Is.EqualTo(30u));
                Assert.That(config.Prefabs.NetworkPrefabsLists.Any(l => l != null && l.Contains(netPlayer)), Is.True);

                NetSmokeSession session = roots.Select(r => r.GetComponent<NetSmokeSession>()).Single(s => s != null);
                var so = new SerializedObject(session);
                Assert.That(so.FindProperty("network").objectReferenceValue, Is.EqualTo(network));
                Assert.That(so.FindProperty("lobbyCamera").objectReferenceValue, Is.Not.Null);
                Assert.That(so.FindProperty("spawnPoints").arraySize, Is.EqualTo(4));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void Networking_IsReferencedByNoOtherGameplayAssembly()
        {
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts", "*.asmdef", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name == "CoopDeck.Networking" || name == "CoopDeck.Editor")
                {
                    continue;
                }

                Assert.That(File.ReadAllText(path), Does.Not.Contain("\"CoopDeck.Networking\""), $"{name} must not depend on networking (ARCHITECTURE.md §2)");
                Assert.That(File.ReadAllText(path), Does.Not.Contain("Unity.Netcode"), $"{name} must not depend on Netcode");
            }
        }
    }
}
