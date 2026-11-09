using System;
using System.Globalization;

namespace CoopDeck.Networking
{
    public enum NetRole : byte
    {
        None = 0,
        Host = 1,
        Client = 2,
    }

    /// <summary>
    /// Launch arguments of the two-client smoke test (Phase 2.5). Any of them switches a build to the Net_Smoke scene.
    /// <code>
    /// CoopDeckFPS.exe -nethost [-netport 7777] [-netlisten 0.0.0.0]
    /// CoopDeckFPS.exe -netclient 192.168.1.20 [-netport 7777]
    /// ... -netauto 30 -netout PerfCaptures\net_host.json [-netreconnect 14]   (unattended run: scripted walk, report, quit)
    /// </code>
    /// <c>-port</c> / <c>-ip</c> are not used: Unity Transport reads those itself.
    /// </summary>
    public struct NetLaunchOptions
    {
        public const string SceneName = "Net_Smoke";
        public const ushort DefaultPort = 7777;
        public const string Loopback = "127.0.0.1";

        public NetRole Role;

        /// <summary>Address the client connects to.</summary>
        public string Address;

        /// <summary>Address the host listens on: loopback by default (no firewall prompt), 0.0.0.0 to accept the LAN.</summary>
        public string ListenAddress;

        public ushort Port;

        /// <summary>Seconds of the unattended run (0 = played by hand).</summary>
        public float AutoSeconds;

        /// <summary>Report path of the unattended run (JSON; screenshots next to it).</summary>
        public string OutputPath;

        /// <summary>Client only: seconds after start to disconnect and join again (0 = never).</summary>
        public float ReconnectAt;

        public bool IsAuto => AutoSeconds > 0f;

        public static NetLaunchOptions Default => new NetLaunchOptions
        {
            Role = NetRole.None,
            Address = Loopback,
            ListenAddress = Loopback,
            Port = DefaultPort,
            OutputPath = string.Empty,
        };

        public static NetLaunchOptions FromCommandLine() => Parse(Environment.GetCommandLineArgs());

        public static NetLaunchOptions Parse(string[] args)
        {
            NetLaunchOptions o = Default;
            if (args == null)
            {
                return o;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (Is(a, "-nethost"))
                {
                    o.Role = NetRole.Host;
                }
                else if (Is(a, "-netclient"))
                {
                    o.Role = NetRole.Client;
                    if (TryValue(args, i, out string address))
                    {
                        o.Address = address;
                        i++;
                    }
                }
                else if (Is(a, "-netport") && TryValue(args, i, out string port) &&
                         ushort.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort p) && p > 0)
                {
                    o.Port = p;
                    i++;
                }
                else if (Is(a, "-netlisten") && TryValue(args, i, out string listen))
                {
                    o.ListenAddress = listen;
                    i++;
                }
                else if (Is(a, "-netauto") && TryValue(args, i, out string seconds) && TryFloat(seconds, out float auto))
                {
                    o.AutoSeconds = Math.Max(0f, auto);
                    i++;
                }
                else if (Is(a, "-netout") && TryValue(args, i, out string path))
                {
                    o.OutputPath = path;
                    i++;
                }
                else if (Is(a, "-netreconnect") && TryValue(args, i, out string at) && TryFloat(at, out float reconnect))
                {
                    o.ReconnectAt = Math.Max(0f, reconnect);
                    i++;
                }
            }

            return o;
        }

        static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);

        /// <summary>The argument after <paramref name="index"/> when it is a value (not the next switch).</summary>
        static bool TryValue(string[] args, int index, out string value)
        {
            value = index + 1 < args.Length ? args[index + 1] : null;
            return !string.IsNullOrEmpty(value) && !value.StartsWith("-", StringComparison.Ordinal);
        }

        static bool TryFloat(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
