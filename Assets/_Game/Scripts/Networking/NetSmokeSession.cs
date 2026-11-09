using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopDeck.Networking
{
    /// <summary>
    /// The Net_Smoke scene's session (Phase 2.5): host or join over Unity Transport, give each joining player a spawn
    /// point, hand the view from the lobby camera to the local player. Started from the small on-screen panel, or from
    /// the launch arguments (<see cref="NetLaunchOptions"/>); with <c>-netauto</c> the local player walks a script,
    /// the run is recorded (<see cref="NetSmokeRecorder"/>) and the build quits.
    /// </summary>
    public class NetSmokeSession : MonoBehaviour
    {
        [SerializeField] NetworkManager network;
        [SerializeField] Camera lobbyCamera;
        [SerializeField] Transform[] spawnPoints = new Transform[0];

        NetLaunchOptions options;
        string addressField;
        string portField;
        bool lan;
        int spawnCount;
        NetSmokeRecorder recorder;
        float startTime;
        float reconnectAt = -1f;
        bool reconnected;
        int shotsTaken;
        string lastFailure;

        public NetworkManager Network => network;
        public NetLaunchOptions Options => options;
        public NetSmokeRecorder Recorder => recorder;

        void Awake()
        {
            options = NetLaunchOptions.FromCommandLine();
            addressField = options.Address;
            portField = options.Port.ToString();
            lan = options.ListenAddress != NetLaunchOptions.Loopback;
            Application.runInBackground = true; // two windows on one machine: the unfocused one must keep running
            NetworkPlayer.ReadDevicesForLocalPlayer = !options.IsAuto;
        }

        void OnEnable()
        {
            NetworkPlayer.LocalPlayerSpawned += OnLocalPlayerSpawned;
            NetworkPlayer.LocalPlayerDespawned += OnLocalPlayerDespawned;
            if (network != null)
            {
                network.NetworkConfig.ConnectionApproval = true;
                network.ConnectionApprovalCallback = Approve;
                network.OnClientConnectedCallback += OnClientConnected;
                network.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        void OnDisable()
        {
            NetworkPlayer.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            NetworkPlayer.LocalPlayerDespawned -= OnLocalPlayerDespawned;
            if (network != null)
            {
                if (network.ConnectionApprovalCallback == Approve)
                {
                    network.ConnectionApprovalCallback = null;
                }

                network.OnClientConnectedCallback -= OnClientConnected;
                network.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            recorder?.Dispose();
        }

        void Start()
        {
            startTime = Time.realtimeSinceStartup;
            if (options.IsAuto)
            {
                recorder = new NetSmokeRecorder(network, options);
            }

            if (options.Role == NetRole.Host)
            {
                StartHost();
            }
            else if (options.Role == NetRole.Client)
            {
                StartClient();
            }
        }

        public bool StartHost()
        {
            if (!Configure(listen: true))
            {
                return false;
            }

            spawnCount = 0;
            bool ok = network.StartHost();
            Note(ok ? $"host started on {options.ListenAddress}:{options.Port}" : "host failed to start");
            lastFailure = ok ? null : "Host を開始できませんでした（ポート使用中？）";
            return ok;
        }

        public bool StartClient()
        {
            if (!Configure(listen: false))
            {
                return false;
            }

            bool ok = network.StartClient();
            Note(ok ? $"client connecting to {options.Address}:{options.Port}" : "client failed to start");
            lastFailure = ok ? null : "接続を開始できませんでした";
            return ok;
        }

        public void Leave()
        {
            if (network != null && network.IsListening)
            {
                Note("leaving");
                network.Shutdown();
            }
        }

        bool Configure(bool listen)
        {
            if (network == null || network.IsListening || network.ShutdownInProgress)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(addressField))
            {
                options.Address = addressField.Trim();
            }

            if (ushort.TryParse(portField, out ushort port) && port > 0)
            {
                options.Port = port;
            }

            if (listen && options.Role == NetRole.None)
            {
                options.ListenAddress = lan ? "0.0.0.0" : NetLaunchOptions.Loopback;
            }

            var transport = network.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null)
            {
                transport = network.GetComponent<UnityTransport>();
                network.NetworkConfig.NetworkTransport = transport;
            }

            // Force our values over Unity Transport's own -port / -ip arguments.
            transport.SetConnectionData(true, options.Address, options.Port, listen ? options.ListenAddress : null);
            return true;
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            Transform spawn = spawnPoints.Length > 0 ? spawnPoints[spawnCount % spawnPoints.Length] : transform;
            spawnCount++;
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = spawn.position;
            response.Rotation = spawn.rotation;
            response.Pending = false;
        }

        void OnClientConnected(ulong clientId)
        {
            if (recorder != null && network.IsServer && clientId != network.LocalClientId)
            {
                recorder.Report.clientsConnected++;
            }

            Note($"connected: client {clientId}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (recorder != null && network.IsServer && clientId != network.LocalClientId)
            {
                recorder.Report.clientsDisconnected++;
            }

            Note($"disconnected: client {clientId}");
        }

        void OnLocalPlayerSpawned(NetworkPlayer player)
        {
            if (lobbyCamera != null)
            {
                lobbyCamera.gameObject.SetActive(false);
            }

            if (recorder != null)
            {
                recorder.Report.localSpawns++;
                recorder.Report.localClientId = player.OwnerClientId;
            }

            Note($"local player spawned (client {player.OwnerClientId})");
            if (options.IsAuto && player.Character != null)
            {
                // Host circles one way, clients the other; both stop and face each other for the screenshots.
                float turn = network.IsServer ? 45f : -45f;
                float elapsed = Elapsed;
                Vector2[] faces = network.IsServer
                    ? new[] { new Vector2(10f, 12.5f), new Vector2(22f, 24.5f) }
                    : new[] { new Vector2(9f, 11.5f), new Vector2(20f, 22.5f) };
                for (int i = 0; i < faces.Length; i++)
                {
                    faces[i] -= new Vector2(elapsed, elapsed); // windows are in run time; the walker counts from now
                }

                player.Character.SetCommandSource(new NetSmokeWalker(player.transform, player.Character.Look, turn, () => FindOther(player), faces));
            }
        }

        void OnLocalPlayerDespawned(NetworkPlayer player)
        {
            if (lobbyCamera != null)
            {
                lobbyCamera.gameObject.SetActive(true);
            }

            Note("local player despawned");
        }

        static Vector3? FindOther(NetworkPlayer self)
        {
            foreach (NetworkPlayer p in NetworkPlayer.Spawned)
            {
                if (p != self && p != null)
                {
                    return p.transform.position;
                }
            }

            return null;
        }

        float Elapsed => Time.realtimeSinceStartup - startTime;

        void Update()
        {
            if (recorder == null)
            {
                return;
            }

            recorder.Tick(Time.unscaledDeltaTime);
            float t = Elapsed;

            // Reconnect test (client): leave, wait for the shutdown to finish, join again.
            if (options.Role == NetRole.Client && options.ReconnectAt > 0f && !reconnected)
            {
                if (reconnectAt < 0f && t >= options.ReconnectAt)
                {
                    Leave();
                    reconnectAt = t + 1.5f;
                }
                else if (reconnectAt > 0f && t >= reconnectAt && !network.ShutdownInProgress && !network.IsListening)
                {
                    reconnected = true;
                    StartClient();
                }
            }

            float[] shots = network.IsServer ? new[] { 12.2f, 24.2f } : new[] { 11.2f, 22.2f };
            if (shotsTaken < shots.Length && t >= shots[shotsTaken])
            {
                TakeScreenshot(++shotsTaken);
            }

            if (t >= options.AutoSeconds)
            {
                Finish();
            }
        }

        void TakeScreenshot(int index)
        {
            if (string.IsNullOrEmpty(options.OutputPath))
            {
                return;
            }

            string full = Path.GetFullPath(options.OutputPath);
            string path = Path.Combine(Path.GetDirectoryName(full) ?? ".", $"{Path.GetFileNameWithoutExtension(full)}_shot{index}.png");
            ScreenCapture.CaptureScreenshot(path);
            recorder.Report.screenshots.Add(path);
            Note($"screenshot {index}");
        }

        void Finish()
        {
            Note("finished");
            recorder.Write(options.OutputPath);
            recorder = null;
            if (network != null && network.IsListening)
            {
                network.Shutdown();
            }

            Debug.Log($"[CoopDeck] Net smoke run finished: {options.OutputPath}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Note(string what)
        {
            recorder?.Note(what);
            Debug.Log($"[CoopDeck] Net: {what}");
        }

        void OnGUI()
        {
            const float width = 320f;
            GUILayout.BeginArea(new Rect(16f, 16f, width, 220f), GUI.skin.box);
            GUILayout.Label("CoopDeck — Net smoke test (Phase 2.5)");
            if (network == null)
            {
                GUILayout.Label("NetworkManager missing");
            }
            else if (!network.IsListening)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Address", GUILayout.Width(60f));
                addressField = GUILayout.TextField(addressField ?? string.Empty);
                GUILayout.Label("Port", GUILayout.Width(32f));
                portField = GUILayout.TextField(portField ?? string.Empty, GUILayout.Width(56f));
                GUILayout.EndHorizontal();
                lan = GUILayout.Toggle(lan, "Host: accept LAN (0.0.0.0)");
                GUILayout.BeginHorizontal();
                GUI.enabled = !network.ShutdownInProgress;
                if (GUILayout.Button("Host"))
                {
                    StartHost();
                }

                if (GUILayout.Button("Join"))
                {
                    StartClient();
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(lastFailure))
                {
                    GUILayout.Label(lastFailure);
                }
            }
            else
            {
                string role = network.IsHost ? "Host" : network.IsConnectedClient ? "Client" : "Connecting...";
                GUILayout.Label($"{role}  id {network.LocalClientId}  players {NetworkPlayer.Spawned.Count}  {RttLabel()}");
                GUILayout.Label("Esc: free the cursor");
                if (GUILayout.Button("Leave"))
                {
                    Leave();
                }
            }

            GUILayout.EndArea();
        }

        string RttLabel()
        {
            if (!(network.NetworkConfig.NetworkTransport is UnityTransport transport) || network.IsHost)
            {
                return string.Empty;
            }

            return network.IsConnectedClient ? $"rtt {transport.GetCurrentRtt(NetworkManager.ServerClientId)} ms" : string.Empty;
        }
    }

    /// <summary>A build started with a Net_Smoke argument goes straight to that scene.</summary>
    static class NetLaunch
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LoadSmokeSceneIfRequested()
        {
#if !UNITY_EDITOR
            NetLaunchOptions options = NetLaunchOptions.FromCommandLine();
            if (options.Role != NetRole.None && SceneManager.GetActiveScene().name != NetLaunchOptions.SceneName &&
                Application.CanStreamedLevelBeLoaded(NetLaunchOptions.SceneName))
            {
                SceneManager.LoadScene(NetLaunchOptions.SceneName);
            }
#endif
        }
    }
}
