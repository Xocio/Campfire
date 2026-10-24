using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    /// Entering through that list matters: a LAN world checks every login with the account service, and the game
    /// only lets an offline account carry on when the world was picked from the list. A typed address ends with an
    /// invalid session error instead.
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

        /// <param name="motd">Text of the entry, read on every announcement so it follows the display language.</param>
        /// <param name="port">Local port that forwards to the host's game.</param>
        /// <param name="localOnly">
        /// Announce only to the game on this computer. The announcement goes straight to the loopback address, so the
        /// entry points at 127.0.0.1 and nothing reaches the network.
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
            var target = _localOnly ? new IPEndPoint(IPAddress.Loopback, AnnouncePort) : new IPEndPoint(Group, AnnouncePort);
            var sockets = _localOnly ? new List<Socket> { new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) } : CreateMulticastSenders();
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    byte[] message = Encoding.UTF8.GetBytes(Format(_motd(), _port));
                    foreach (var socket in sockets)
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
                foreach (var socket in sockets)
                    socket.Close();
            }
        }

        /// <summary>
        /// One sender per interface so the game hears it whichever adapter it listens on. The sockets are never bound
        /// explicitly; they only send, so Windows Firewall has nothing to ask about.
        /// </summary>
        private static List<Socket> CreateMulticastSenders()
        {
            var sockets = new List<Socket>();
            foreach (var address in NetworkCheck.GetIPv4Addresses())
            {
                try
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                    sockets.Add(socket);
                }
                catch (SocketException)
                {
                }
            }
            return sockets;
        }
    }
}
