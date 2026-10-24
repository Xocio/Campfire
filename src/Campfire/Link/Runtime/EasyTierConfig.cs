using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// Settings of one virtual network instance, written to a TOML file.
    /// </summary>
    /// <remarks>
    /// The instance is started from a file rather than command-line flags because only file-based instances
    /// accept port forwards added while running, which joining players need once the host is found.
    /// </remarks>
    internal sealed class EasyTierConfig
    {
        public string NetworkName { get; set; }

        public string NetworkSecret { get; set; }

        public string Hostname { get; set; }

        /// <summary>A fixed virtual address, or <c>null</c> to get one automatically.</summary>
        public string IPv4 { get; set; }

        public List<string> Peers { get; } = new List<string>();

        /// <summary>Ports other members may reach on this computer. "0" alone blocks every incoming connection.</summary>
        public List<string> TcpWhitelist { get; } = new List<string>();

        public List<string> UdpWhitelist { get; } = new List<string>();

        /// <summary>When set, traffic only flows over direct connections and never through a relay node.</summary>
        public bool DirectOnly { get; set; }

        /// <summary>
        /// When set, this computer never connects directly to other members and does not listen for them, so
        /// they cannot learn its address. Traffic goes through relay nodes.
        /// </summary>
        public bool HideAddress { get; set; }

        public string ToToml()
        {
            var toml = new StringBuilder();
            toml.AppendLine("hostname = " + Quote(Hostname));
            if (IPv4 != null)
                toml.AppendLine("ipv4 = " + Quote(IPv4));
            else
                toml.AppendLine("dhcp = true");

            // Random ports on every interface (IPv6 is added automatically); none when the address is hidden.
            toml.AppendLine(HideAddress ? "listeners = []" : "listeners = [\"tcp://0.0.0.0:0\", \"udp://0.0.0.0:0\"]");
            toml.AppendLine("tcp_whitelist = " + Array(TcpWhitelist));
            toml.AppendLine("udp_whitelist = " + Array(UdpWhitelist));
            toml.AppendLine();

            toml.AppendLine("[network_identity]");
            toml.AppendLine("network_name = " + Quote(NetworkName));
            toml.AppendLine("network_secret = " + Quote(NetworkSecret));
            toml.AppendLine();

            foreach (string peer in Peers)
            {
                toml.AppendLine("[[peer]]");
                toml.AppendLine("uri = " + Quote(peer));
                toml.AppendLine();
            }

            toml.AppendLine("[flags]");
            toml.AppendLine("no_tun = true");
            toml.AppendLine("latency_first = true");
            toml.AppendLine("enable_kcp_proxy = true");
            if (EasyTierPackage.IsWindows7)
            {
                // QUIC does not work on Windows 7, so traffic stays on TCP and KCP there.
                toml.AppendLine("enable_quic_proxy = false");
                toml.AppendLine("disable_quic_input = true");
            }
            else
            {
                toml.AppendLine("enable_quic_proxy = true");
            }
            // Zstandard compression.
            toml.AppendLine("data_compress_algo = 2");
            // Never act as a relay for strangers' networks.
            toml.AppendLine("private_mode = true");
            toml.AppendLine("p2p_only = " + (DirectOnly && !HideAddress ? "true" : "false"));
            if (HideAddress)
            {
                toml.AppendLine("disable_p2p = true");
                toml.AppendLine("disable_udp_hole_punching = true");
                toml.AppendLine("disable_tcp_hole_punching = true");
                toml.AppendLine("disable_sym_hole_punching = true");
                toml.AppendLine("disable_upnp = true");
            }
            return toml.ToString();
        }

        private static string Array(IEnumerable<string> values)
        {
            return "[" + string.Join(", ", values.Select(Quote)) + "]";
        }

        private static string Quote(string value)
        {
            var quoted = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\')
                    quoted.Append('\\').Append(c);
                else if (c < 0x20)
                    quoted.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else
                    quoted.Append(c);
            }
            return quoted.Append('"').ToString();
        }
    }
}
