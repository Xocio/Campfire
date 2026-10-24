using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Campfire.Link
{
    /// <summary>
    /// Quick look at the local network interfaces.
    /// </summary>
    internal static class NetworkCheck
    {
        /// <summary>
        /// Whether this computer has a global IPv6 address. Two players with IPv6 can almost always reach each
        /// other directly, which gives the lowest latency.
        /// </summary>
        public static bool HasPublicIPv6()
        {
            return GetUnicastAddresses().Any(IsGlobalIPv6);
        }

        /// <summary>IPv4 addresses of interfaces that are up, loopback included.</summary>
        public static IReadOnlyList<IPAddress> GetIPv4Addresses()
        {
            var addresses = GetUnicastAddresses().Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
            if (!addresses.Contains(IPAddress.Loopback))
                addresses.Add(IPAddress.Loopback);
            return addresses;
        }

        private static IEnumerable<IPAddress> GetUnicastAddresses()
        {
            NetworkInterface[] interfaces;
            try
            {
                interfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                yield break;
            }

            foreach (var network in interfaces)
            {
                if (network.OperationalStatus != OperationalStatus.Up)
                    continue;

                UnicastIPAddressInformationCollection unicast;
                try
                {
                    unicast = network.GetIPProperties().UnicastAddresses;
                }
                catch (NetworkInformationException)
                {
                    continue;
                }

                foreach (var info in unicast)
                    yield return info.Address;
            }
        }

        private static bool IsGlobalIPv6(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.IsIPv6Teredo)
                return false;

            // Global unicast addresses live in 2000::/3.
            byte first = address.GetAddressBytes()[0];
            return (first & 0xE0) == 0x20;
        }
    }
}
