using System;
using System.Collections.Generic;
using System.Globalization;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// One member of the virtual network, as reported by the command-line tool.
    /// </summary>
    internal sealed class EasyTierPeer
    {
        public string Hostname { get; private set; }

        /// <summary>Virtual IPv4 address; empty for public nodes, which are not network members.</summary>
        public string IPv4 { get; private set; }

        public bool IsLocal { get; private set; }

        /// <summary>Whether traffic goes straight to this peer rather than through a relay.</summary>
        public bool IsDirect { get; private set; }

        /// <summary>Round-trip time in milliseconds, or <c>null</c> when not measured yet.</summary>
        public double? LatencyMs { get; private set; }

        /// <summary>Transport in use, such as "udp", "tcp" or "quic".</summary>
        public string Tunnel { get; private set; }

        public NatKind Nat { get; private set; }

        public bool IsMember => !string.IsNullOrEmpty(IPv4);

        public static EasyTierPeer FromJson(IDictionary<string, object> item)
        {
            string cost = GetString(item, "cost");
            return new EasyTierPeer
            {
                Hostname = GetString(item, "hostname"),
                IPv4 = GetString(item, "ipv4"),
                IsLocal = cost == "Local",
                IsDirect = cost == "p2p",
                LatencyMs = double.TryParse(GetString(item, "lat_ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    ? lat
                    : (double?)null,
                Tunnel = GetString(item, "tunnel_proto"),
                Nat = ParseNat(GetString(item, "nat_type")),
            };
        }

        public static NatKind ParseNat(string value)
        {
            switch (value)
            {
                case "OpenInternet":
                case "NoPat":
                    return NatKind.Open;
                case "FullCone":
                    return NatKind.FullCone;
                case "Restricted":
                    return NatKind.Restricted;
                case "PortRestricted":
                    return NatKind.PortRestricted;
                case "Symmetric":
                case "SymUdpFirewall":
                case "SymmetricEasyInc":
                case "SymmetricEasyDec":
                    return NatKind.Symmetric;
                default:
                    return NatKind.Unknown;
            }
        }

        private static string GetString(IDictionary<string, object> item, string key)
        {
            return item.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
        }
    }

    /// <summary>How open a network is to incoming connections; more open networks connect more easily.</summary>
    internal enum NatKind
    {
        Unknown,
        Open,
        FullCone,
        Restricted,
        PortRestricted,
        Symmetric,
    }
}
