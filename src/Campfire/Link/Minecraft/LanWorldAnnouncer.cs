using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Makes the host's world appear under "Scan for LAN worlds" in the joining player's multiplayer screen,
    /// using the same announcement Minecraft sends for "Open to LAN".
    /// </summary>
    /// <remarks>
    /// The game lets an offline account into a LAN world only when the world was picked from the list.
    /// </remarks>
    internal sealed class LanWorldAnnouncer : IDisposable
    {
        private static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");
        private const int AnnouncePort = 4445;
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);

        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Func<string> _motd;
        private readonly int _port;
        private readonly bool _localOnly;

        /// <param name="motd">Text of the entry, read on every announcement.</param>
        /// <param name="port">Local port that forwards to the host's game.</param>
        /// <param name="localOnly">
        /// Announce only to the game on this computer. The entry points at 127.0.0.1 and nothing reaches the network.
        /// </param>
        public LanWorldAnnouncer(Func<string> motd, int port, bool localOnly)
        {
            _motd = motd;
            _port = port;
            _localOnly = localOnly;
            _ = RunAsync();
        }

        public void Dispose()
        {
            _stop.Cancel();
        }

        /// <summary>"[MOTD]text[/MOTD][AD]port[/AD]", the format Minecraft reads.</summary>
        public static string Format(string motd, int port)
        {
            return "[MOTD]" + motd + "[/MOTD][AD]" + port + "[/AD]";
        }

        private async Task RunAsync()
        {
            // The game on this computer is told directly, from the same address the multicast uses.
            var lan = _localOnly ? null : FindLanAddress();
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var targets = new List<IPEndPoint>();
            if (lan != null)
            {
                try
                {
                    // Bound to its own address, a datagram to that address keeps it as the sender.
                    socket.Bind(new IPEndPoint(lan, 0));
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, lan.GetAddressBytes());
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
                    targets.Add(new IPEndPoint(lan, AnnouncePort));
                    targets.Add(new IPEndPoint(Group, AnnouncePort));
                }
                catch (SocketException)
                {
                    socket.Close();
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    targets.Clear();
                }
            }
            if (targets.Count == 0)
                targets.Add(new IPEndPoint(IPAddress.Loopback, AnnouncePort));

            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    byte[] message = Encoding.UTF8.GetBytes(Format(_motd(), _port));
                    foreach (var target in targets)
                    {
                        try
                        {
                            socket.SendTo(message, target);
                        }
                        catch (SocketException)
                        {
                            // An adapter went down, or no game is listening yet; keep trying.
                        }
                    }

                    try
                    {
                        await Task.Delay(Interval, _stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
            finally
            {
                socket.Close();
            }
        }

        /// <summary>
        /// The address of the network that other devices share with this computer: an adapter that has a gateway and
        /// is not a tunnel, a virtual machine or a proxy. <c>null</c> when there is none.
        /// </summary>
        private static IPAddress FindLanAddress()
        {
            var candidates = new List<(IPAddress Address, bool Gateway)>();
            try
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up || !network.SupportsMulticast
                        || network.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || network.NetworkInterfaceType == NetworkInterfaceType.Tunnel
                        || IsVirtual(network.Name + " " + network.Description))
                        continue;

                    var properties = network.GetIPProperties();
                    var address = properties.UnicastAddresses
                        .Select(a => a.Address)
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal));
                    if (address != null)
                        candidates.Add((address, properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))));
                }
            }
            catch (NetworkInformationException)
            {
                return null;
            }
            return candidates.Where(c => c.Gateway).Select(c => c.Address).FirstOrDefault()
                   ?? candidates.Select(c => c.Address).FirstOrDefault();
        }

        private static readonly string[] VirtualMarks =
        {
            "tun", "tap", "vpn", "virtual", "vmware", "vbox", "hyper-v", "vethernet", "wsl", "wintun", "tunnel", "clash", "meta", "sing-box", "easytier", "loopback", "bluetooth", "蓝牙",
        };

        private static bool IsVirtual(string text)
        {
            return VirtualMarks.Any(mark => text.IndexOf(mark, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
