using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace CoopDeck.Networking
{
    /// <summary>JSON report of one unattended smoke run (one per machine; the two are compared afterwards).</summary>
    [Serializable]
    public class NetSmokeReport
    {
        public string role;
        public string unityVersion;
        public string address;
        public int port;
        public int tickRate;
        public float seconds;
        public ulong localClientId;
        public int localSpawns;
        public int clientsConnected;
        public int clientsDisconnected;
        public int maxPlayersSeen;
        public float maxOwnSpeed;
        public float maxRemoteSpeed;
        public float averageRttMs;
        public float maxRttMs;
        public int errors;
        public int warnings;
        public List<string> errorMessages = new List<string>();
        public List<string> warningMessages = new List<string>();
        public List<string> screenshots = new List<string>();
        public List<Event> events = new List<Event>();
        public List<Check> checks = new List<Check>();
        public List<Sample> own = new List<Sample>();
        public List<Sample> remote = new List<Sample>();

        [Serializable]
        public struct Event
        {
            public float t;
            public string what;
        }

        /// <summary>Who drives each player body on this machine (ownership must hold: only the own body simulates).</summary>
        [Serializable]
        public struct Check
        {
            public float t;
            public ulong client;
            public bool owner;
            public string controller;
            public bool simulating;
            public bool firstPerson;
        }

        /// <summary>A position at server time <c>t</c> (shared by both machines, so the paths can be lined up).</summary>
        [Serializable]
        public struct Sample
        {
            public double t;
            public ulong client;
            public float x;
            public float y;
            public float z;
            public float yaw;
            public float pitch;
        }
    }

    /// <summary>Fills a <see cref="NetSmokeReport"/> while the run goes on.</summary>
    public sealed class NetSmokeRecorder
    {
        const float SampleInterval = 0.05f;
        const float CheckInterval = 1f;
        const float SettleSeconds = 0.5f;
        const int MaxMessages = 20;

        readonly NetworkManager network;
        readonly Dictionary<ulong, (Vector3 position, float since)> lastRemote = new Dictionary<ulong, (Vector3, float)>();
        float clock;
        float nextSample;
        float nextCheck;
        double rttSum;
        int rttCount;

        public NetSmokeRecorder(NetworkManager network, NetLaunchOptions options)
        {
            this.network = network;
            Report = new NetSmokeReport
            {
                role = options.Role.ToString(),
                unityVersion = Application.unityVersion,
                address = options.Role == NetRole.Host ? options.ListenAddress : options.Address,
                port = options.Port,
                tickRate = network != null ? (int)network.NetworkConfig.TickRate : 0,
            };
            Application.logMessageReceived += OnLog;
        }

        public NetSmokeReport Report { get; }

        public float Clock => clock;

        public void Note(string what)
        {
            Report.events.Add(new NetSmokeReport.Event { t = clock, what = what });
        }

        public void Tick(float deltaTime)
        {
            clock += deltaTime;
            bool listening = network != null && network.IsListening;
            if (!listening)
            {
                lastRemote.Clear();
                return;
            }

            Report.maxPlayersSeen = Mathf.Max(Report.maxPlayersSeen, NetworkPlayer.Spawned.Count);
            TrackSpeeds(deltaTime);

            if (clock >= nextSample)
            {
                nextSample = clock + SampleInterval;
                double t = network.ServerTime.Time;
                foreach (NetworkPlayer p in NetworkPlayer.Spawned)
                {
                    var sample = new NetSmokeReport.Sample
                    {
                        t = t,
                        client = p.OwnerClientId,
                        x = p.transform.position.x,
                        y = p.transform.position.y,
                        z = p.transform.position.z,
                        yaw = p.transform.eulerAngles.y,
                        pitch = p.IsRemoteCopy ? p.Pose.Pitch : p.Character != null && p.Character.Look != null ? p.Character.Look.Pitch : 0f,
                    };
                    (p.IsRemoteCopy ? Report.remote : Report.own).Add(sample);
                }

                SampleRtt();
            }

            if (clock >= nextCheck)
            {
                nextCheck = clock + CheckInterval;
                foreach (NetworkPlayer p in NetworkPlayer.Spawned)
                {
                    Report.checks.Add(new NetSmokeReport.Check
                    {
                        t = clock,
                        client = p.OwnerClientId,
                        owner = p.IsOwner,
                        controller = p.Character != null ? p.Character.ControllerKind.ToString() : "None",
                        simulating = p.Character != null && p.Character.isActiveAndEnabled,
                        firstPerson = p.FirstPersonActive,
                    });
                }
            }
        }

        void TrackSpeeds(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            foreach (NetworkPlayer p in NetworkPlayer.Spawned)
            {
                if (!p.IsRemoteCopy)
                {
                    if (p.Character != null && p.Character.Motor != null)
                    {
                        Report.maxOwnSpeed = Mathf.Max(Report.maxOwnSpeed, p.Character.Motor.Velocity.magnitude);
                    }

                    continue;
                }

                ulong id = p.NetworkObjectId;
                Vector3 now = p.transform.position;
                if (lastRemote.TryGetValue(id, out var last))
                {
                    if (clock - last.since > SettleSeconds)
                    {
                        Report.maxRemoteSpeed = Mathf.Max(Report.maxRemoteSpeed, (now - last.position).magnitude / deltaTime);
                    }

                    lastRemote[id] = (now, last.since);
                }
                else
                {
                    lastRemote[id] = (now, clock); // first frame of this copy: spawn, not a jump
                }
            }
        }

        void SampleRtt()
        {
            if (!(network.NetworkConfig.NetworkTransport is Unity.Netcode.Transports.UTP.UnityTransport transport))
            {
                return;
            }

            ulong peer = network.IsServer ? FirstRemoteClient() : NetworkManager.ServerClientId;
            if (peer == ulong.MaxValue)
            {
                return;
            }

            ulong rtt = transport.GetCurrentRtt(peer);
            rttSum += rtt;
            rttCount++;
            Report.averageRttMs = (float)(rttSum / rttCount);
            Report.maxRttMs = Mathf.Max(Report.maxRttMs, rtt);
        }

        ulong FirstRemoteClient()
        {
            foreach (ulong id in network.ConnectedClientsIds)
            {
                if (id != network.LocalClientId)
                {
                    return id;
                }
            }

            return ulong.MaxValue;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                Report.errors++;
                if (Report.errorMessages.Count < MaxMessages)
                {
                    Report.errorMessages.Add(condition);
                }
            }
            else if (type == LogType.Warning)
            {
                Report.warnings++;
                if (Report.warningMessages.Count < MaxMessages)
                {
                    Report.warningMessages.Add(condition);
                }
            }
        }

        public void Write(string path)
        {
            Application.logMessageReceived -= OnLog;
            Report.seconds = clock;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string full = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(full, JsonUtility.ToJson(Report, true));
        }

        public void Dispose() => Application.logMessageReceived -= OnLog;
    }
}
