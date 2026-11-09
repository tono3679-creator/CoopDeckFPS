using System.Linq;
using System.Reflection;
using CoopDeck.Animation;
using CoopDeck.Input;
using CoopDeck.Networking;
using CoopDeck.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Phase 2.5 two-client smoke test (Docs/MULTIPLAYER_PLAN.md §7):
    /// <list type="bullet">
    /// <item><c>PF_NetPlayer</c>: a prefab variant of PF_Player with NetworkObject, an owner-authority NetworkTransform
    /// and <see cref="NetworkPlayer"/>. Its input reader starts disabled; only the owner turns it on.</item>
    /// <item><c>Net_Smoke</c>: a small walled yard, a lobby camera, the NetworkManager (Unity Transport, 30 Hz, approval
    /// for spawn points, no scene management) and the <see cref="NetSmokeSession"/> panel. Added to the build.</item>
    /// </list>
    /// The scene is built in an additive scene and closed again, so the scene open in the Editor stays as it is.
    /// </summary>
    public static class NetSmokeSceneBuilder
    {
        public const string ScenePath = GamePaths.Root + "/Scenes/" + NetLaunchOptions.SceneName + ".unity";
        public const string NetPlayerPath = GamePaths.CharacterPrefabs + "/PF_NetPlayer.prefab";
        public const string PlayerPath = GamePaths.CharacterPrefabs + "/PF_Player.prefab";

        /// <summary>Netcode's own default prefab list (it adds network prefabs to it on import).</summary>
        public const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

        public const uint TickRate = 30;

        /// <summary>Where players appear, in join order (the host first).</summary>
        public static readonly Vector3[] SpawnPoints =
        {
            new Vector3(-5f, 0.05f, -6f),
            new Vector3(5f, 0.05f, -6f),
            new Vector3(-5f, 0.05f, 6f),
            new Vector3(5f, 0.05f, 6f),
        };

        [MenuItem("CoopDeck/Setup/Build Net Smoke Scene (Phase 2.5)", priority = 20)]
        public static void BuildFromMenu()
        {
            BuildStandalone();
            EditorUtility.DisplayDialog("CoopDeck Net Smoke", $"{NetPlayerPath} と {ScenePath} を生成しました。", "OK");
        }

        /// <summary>From the generated PF_Player and existing materials; regenerates nothing else (live Editor safe).</summary>
        public static void BuildStandalone()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            if (player == null)
            {
                throw new System.InvalidOperationException($"{PlayerPath} is missing: run the bootstrap first.");
            }

            var m = new MaterialSet
            {
                Ground = Mat("M_Ground_Grid"),
                WallLight = Mat("M_Wall_Light"),
                WallDark = Mat("M_Wall_Dark"),
                Teal = Mat("M_Block_Teal"),
                Coral = Mat("M_Block_Coral"),
                Sun = Mat("M_Block_Sun"),
                Violet = Mat("M_Block_Violet"),
                Sky = Mat("M_Sky_Procedural"),
            };
            Build(player, m, AssetDatabase.LoadAssetAtPath<VolumeProfile>(GamePaths.Rendering + "/VP_Sandbox.asset"));
        }

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{GamePaths.EnvironmentMaterials}/{name}.mat");

        public static void Build(GameObject playerPrefab, MaterialSet m, VolumeProfile volume)
        {
            GameObject netPlayer = BuildNetPlayer(playerPrefab);
            NetworkPrefabsList prefabs = EnsurePrefabList(netPlayer);

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene); // lighting settings and new objects go to the new scene
            try
            {
                SandboxSceneBuilder.BuildLighting(m, volume);
                BuildYard(m);

                var spawnRoot = new GameObject("SpawnPoints").transform;
                var spawns = new Transform[SpawnPoints.Length];
                for (int i = 0; i < SpawnPoints.Length; i++)
                {
                    var spawn = new GameObject($"Spawn_{i}").transform;
                    spawn.SetParent(spawnRoot, false);
                    Vector3 toCenter = -SpawnPoints[i];
                    toCenter.y = 0f;
                    spawn.SetPositionAndRotation(SpawnPoints[i], Quaternion.LookRotation(toCenter));
                    spawns[i] = spawn;
                }

                Camera lobby = BuildLobbyCamera();

                var networkGo = new GameObject("NetworkManager");
                var transport = networkGo.AddComponent<UnityTransport>();
                var network = networkGo.AddComponent<NetworkManager>();
                network.NetworkConfig ??= new NetworkConfig();
                network.NetworkConfig.NetworkTransport = transport;
                network.NetworkConfig.PlayerPrefab = netPlayer;
                network.NetworkConfig.TickRate = TickRate;
                network.NetworkConfig.ConnectionApproval = true;
                network.NetworkConfig.EnableSceneManagement = false; // both sides load Net_Smoke themselves; players spawn dynamically
                network.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
                network.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
                EditorUtility.SetDirty(network);

                var sessionGo = new GameObject("NetSmokeSession");
                var session = sessionGo.AddComponent<NetSmokeSession>();
                EditorUtil.SetObject(session, "network", network);
                EditorUtil.SetObject(session, "lobbyCamera", lobby);
                EditorUtil.SetObjectArray(session, "spawnPoints", spawns.Cast<Object>().ToArray());

                EditorUtil.EnsureFolder(System.IO.Path.GetDirectoryName(ScenePath)?.Replace('\\', '/'));
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }

                EditorSceneManager.CloseScene(scene, true);
            }

            AddToBuild();
        }

        /// <summary>PF_Player + network parts, saved as a prefab variant (PF_Player itself is not touched).</summary>
        public static GameObject BuildNetPlayer(GameObject playerPrefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            try
            {
                instance.name = "NetPlayer";
                instance.AddComponent<NetworkObject>();

                // Owner authority (Phase 2.5): the owner's motor moves the body, everyone else interpolates.
                var sync = instance.AddComponent<NetworkTransform>();
                sync.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                sync.SyncRotAngleX = false;
                sync.SyncRotAngleZ = false;
                sync.SyncScaleX = false;
                sync.SyncScaleY = false;
                sync.SyncScaleZ = false;
                sync.InLocalSpace = false;
                sync.Interpolate = true;

                var reader = instance.GetComponent<PlayerInputReader>();
                reader.enabled = false; // only the owner reads devices (NetworkPlayer.BecomeLocal)

                var player = instance.AddComponent<NetworkPlayer>();
                EditorUtil.SetObject(player, "character", instance.GetComponent<FpsCharacter>());
                EditorUtil.SetObject(player, "input", reader);
                EditorUtil.SetObject(player, "firstPersonRig", instance.transform.Find("CameraPivot/PlayerCamera").gameObject);
                EditorUtil.SetObject(player, "bodyAnimator", instance.GetComponent<CharacterAnimatorDriver>());

                PrefabUtility.SaveAsPrefabAsset(instance, NetPlayerPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
            RefreshNetworkId(prefab.GetComponent<NetworkObject>());
            return prefab;
        }

        /// <summary>
        /// The prefab's network id (GlobalObjectIdHash) comes from the saved asset, so recompute it on the asset rather
        /// than keep the value the temporary scene instance had.
        /// </summary>
        static void RefreshNetworkId(NetworkObject networkObject)
        {
            MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
            AssetDatabase.SaveAssetIfDirty(networkObject.gameObject);
        }

        static NetworkPrefabsList EnsurePrefabList(GameObject netPlayer)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }

            if (!list.Contains(netPlayer))
            {
                list.Add(new NetworkPrefab { Prefab = netPlayer });
            }

            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssetIfDirty(list);
            return list;
        }

        static void BuildYard(MaterialSet m)
        {
            var yard = new GameObject("Environment").transform;
            SandboxSceneBuilder.Block(yard, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(52f, 1f, 52f), m.Ground);
            SandboxSceneBuilder.Block(yard, "Wall_North", new Vector3(0f, 2f, 26.5f), new Vector3(54f, 4f, 1f), m.WallLight);
            SandboxSceneBuilder.Block(yard, "Wall_South", new Vector3(0f, 2f, -26.5f), new Vector3(54f, 4f, 1f), m.WallLight);
            SandboxSceneBuilder.Block(yard, "Wall_East", new Vector3(26.5f, 2f, 0f), new Vector3(1f, 4f, 54f), m.WallLight);
            SandboxSceneBuilder.Block(yard, "Wall_West", new Vector3(-26.5f, 2f, 0f), new Vector3(1f, 4f, 54f), m.WallLight);

            // A few colored landmarks so position and facing are easy to read in screenshots.
            SandboxSceneBuilder.Block(yard, "Pillar_Teal", new Vector3(-16f, 2f, 16f), new Vector3(2f, 4f, 2f), m.Teal);
            SandboxSceneBuilder.Block(yard, "Pillar_Coral", new Vector3(16f, 2f, 16f), new Vector3(2f, 4f, 2f), m.Coral);
            SandboxSceneBuilder.Block(yard, "Pillar_Sun", new Vector3(16f, 2f, -16f), new Vector3(2f, 4f, 2f), m.Sun);
            SandboxSceneBuilder.Block(yard, "Pillar_Violet", new Vector3(-16f, 2f, -16f), new Vector3(2f, 4f, 2f), m.Violet);
            SandboxSceneBuilder.Block(yard, "Step", new Vector3(0f, 0.25f, 18f), new Vector3(6f, 0.5f, 3f), m.WallDark);
        }

        static Camera BuildLobbyCamera()
        {
            var go = new GameObject("LobbyCamera");
            go.tag = "MainCamera";
            go.transform.SetPositionAndRotation(new Vector3(0f, 14f, -24f), Quaternion.Euler(30f, 0f, 0f));
            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.farClipPlane = 300f;
            go.AddComponent<AudioListener>();
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            return camera;
        }

        static void AddToBuild()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(s => s.path != ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
