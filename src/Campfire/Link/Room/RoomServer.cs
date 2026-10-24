using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Room
{
    /// <summary>
    /// The room service on the host's computer. Joining players use it to check that they reached a room,
    /// learn the game port and keep the player list in sync.
    /// </summary>
    /// <remarks>
    /// It only listens on the loopback address; the virtual network delivers players' connections there, so the
    /// service is never exposed to the host's own local network.
    /// </remarks>
    internal sealed class RoomServer : IDisposable
    {
        public const int PreferredPort = 13448;

        private static readonly TimeSpan GuestTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(64);

        /// <summary>How long a removed player is kept out before they may join again.</summary>
        public static readonly TimeSpan RemovalTime = TimeSpan.FromMinutes(1);

        /// <summary>Error bodies that tell a joining Campfire why the host turned it away.</summary>
        public const string RemovedReply = "campfire:removed";
        public const string BlockedReply = "campfire:blocked";
        private static readonly byte[] ProtocolList = Encoding.UTF8.GetBytes(
            string.Join("\0", RoomProtocol.Ping, RoomProtocol.Protocols, RoomProtocol.ServerPort,
                RoomProtocol.PlayerPing, RoomProtocol.PlayerList));

        private readonly PlayerProfile _host;
        private readonly int _minecraftPort;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly List<Guest> _guests = new List<Guest>();
        private readonly Dictionary<string, DateTime> _removed = new Dictionary<string, DateTime>();

        private RoomServer(PlayerProfile host, int minecraftPort, TcpListener listener)
        {
            _host = host;
            _minecraftPort = minecraftPort;
            _listener = listener;
        }

        /// <summary>Raised on a worker thread when a player joins, leaves or renames.</summary>
        public event EventHandler PlayersChanged;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static RoomServer Start(PlayerProfile host, int minecraftPort)
        {
            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, PreferredPort);
                listener.Start();
            }
            catch (SocketException)
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
            }

            var server = new RoomServer(host, minecraftPort, listener);
            LinkLog.Info($"Room service listening on port {server.Port}.");
            _ = server.AcceptLoopAsync();
            return server;
        }

        /// <summary>The host followed by every guest, in joining order.</summary>
        public IReadOnlyList<PlayerProfile> GetPlayers()
        {
            lock (_guests)
                return new[] { _host }.Concat(_guests.Select(g => g.Profile)).ToList();
        }

        /// <summary>Drops guests that have not checked in for a while (they closed the game or lost the connection).</summary>
        public void RemoveSilentGuests()
        {
            bool changed;
            lock (_guests)
                changed = _guests.RemoveAll(g => DateTime.UtcNow - g.LastSeen > GuestTimeout) > 0;
            if (changed)
                PlayersChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Takes a guest out of the room; their computer is turned away for <see cref="RemovalTime"/>.</summary>
        public void Remove(string machineId)
        {
            lock (_guests)
            {
                _removed[machineId] = DateTime.UtcNow + RemovalTime;
                _guests.RemoveAll(g => g.Profile.MachineId == machineId);
            }
            PlayersChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
                {
                    return;
                }
                _ = ServeAsync(client);
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            using (idle.Token.Register(client.Close))
            {
                try
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    while (true)
                    {
                        idle.CancelAfter(IdleTimeout);
                        var request = await RoomFraming.ReadRequestAsync(stream, idle.Token).ConfigureAwait(false);
                        if (request == null)
                            return;

                        var (status, body) = Handle(request);
                        await RoomFraming.WriteResponseAsync(stream, status, body, idle.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException
                                           || ex is InvalidDataException || ex is OperationCanceledException)
                {
                    // The player left or the connection broke; the guest list times them out.
                }
            }
        }

        private (byte Status, byte[] Body) Handle(RoomFraming.Request request)
        {
            try
            {
                switch (request.Kind)
                {
                    case RoomProtocol.Ping:
                        return (RoomFraming.StatusOk, request.Body);
                    case RoomProtocol.Protocols:
                        return (RoomFraming.StatusOk, ProtocolList);
                    case RoomProtocol.ServerPort:
                        return (RoomFraming.StatusOk, new[] { (byte)(_minecraftPort >> 8), (byte)_minecraftPort });
                    case RoomProtocol.PlayerPing:
                        return HandlePlayerPing(request.Body);
                    case RoomProtocol.PlayerList:
                        return (RoomFraming.StatusOk, SerializePlayers());
                    default:
                        return (RoomFraming.StatusError, Encoding.UTF8.GetBytes("Requested protocol hasn't been implemented."));
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return (RoomFraming.StatusError, Encoding.UTF8.GetBytes(ex.Message));
            }
        }

        private (byte Status, byte[] Body) HandlePlayerPing(byte[] body)
        {
            var json = Json.Parse(Encoding.UTF8.GetString(body));
            var profile = PlayerProfile.FromJson(json, PlayerKind.Guest);
            if (profile == null)
                return (RoomFraming.StatusError, Encoding.UTF8.GetBytes("Invalid player profile."));
            if (profile.MachineId == _host.MachineId)
                return (RoomFraming.StatusError, Encoding.UTF8.GetBytes("The machine id belongs to the host."));
            if (Blocklist.Contains(profile))
                return (RoomFraming.StatusError, Encoding.UTF8.GetBytes(BlockedReply));
            lock (_guests)
            {
                if (_removed.TryGetValue(profile.MachineId, out var until) && until > DateTime.UtcNow)
                    return (RoomFraming.StatusError, Encoding.UTF8.GetBytes(RemovedReply));
            }

            bool changed;
            lock (_guests)
            {
                int index = _guests.FindIndex(g => g.Profile.MachineId == profile.MachineId);
                if (index < 0)
                {
                    _guests.Add(new Guest(profile));
                    changed = true;
                    LinkLog.Info($"Player joined: {profile.Name} ({profile.Vendor}).");
                }
                else
                {
                    changed = _guests[index].Profile.Name != profile.Name;
                    _guests[index] = new Guest(profile);
                }
            }
            if (changed)
                PlayersChanged?.Invoke(this, EventArgs.Empty);
            return (RoomFraming.StatusOk, Array.Empty<byte>());
        }

        private byte[] SerializePlayers()
        {
            var list = GetPlayers().Select(p => p.ToJson(includeKind: true)).ToList();
            return Encoding.UTF8.GetBytes(Json.Serialize(list));
        }

        private sealed class Guest
        {
            public Guest(PlayerProfile profile)
            {
                Profile = profile;
                LastSeen = DateTime.UtcNow;
            }

            public PlayerProfile Profile { get; }

            public DateTime LastSeen { get; }
        }
    }
}
