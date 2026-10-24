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
    /// Connection from a joining player to the host's room service, reached through a local port forward.
    /// </summary>
    internal sealed class RoomClient : IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(64);

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private RoomClient(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        /// <summary>
        /// Connects and confirms that a room service answers. Returns <c>null</c> when nothing usable answers yet;
        /// a forwarded port may accept connections before the host is actually reachable.
        /// </summary>
        public static async Task<RoomClient> TryConnectAsync(int localPort, CancellationToken cancellation)
        {
            var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            RoomClient room = null;
            try
            {
                var connect = client.ConnectAsync(IPAddress.Loopback, localPort);
                if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(5), cancellation)).ConfigureAwait(false) != connect)
                    return null;
                await connect.ConfigureAwait(false);

                room = new RoomClient(client);
                var response = await room.SendAsync(RoomProtocol.Ping, RoomProtocol.Fingerprint, TimeSpan.FromSeconds(8), cancellation)
                    .ConfigureAwait(false);
                if (response.IsOk && response.Body.SequenceEqual(RoomProtocol.Fingerprint))
                {
                    var result = room;
                    room = null;
                    return result;
                }
                return null;
            }
            catch (Exception ex) when (IsConnectionError(ex))
            {
                return null;
            }
            finally
            {
                if (room != null)
                    room.Dispose();
                else if (!client.Connected)
                    client.Close();
            }
        }

        /// <summary>The host's game port.</summary>
        public async Task<int> GetServerPortAsync(CancellationToken cancellation)
        {
            var response = await SendAsync(RoomProtocol.ServerPort, null, RequestTimeout, cancellation).ConfigureAwait(false);
            if (!response.IsOk || response.Body.Length != 2)
                throw new InvalidDataException("The host has no game running (status " + response.Status + ").");
            return (response.Body[0] << 8) | response.Body[1];
        }

        /// <summary>Tells the host this player is still here.</summary>
        public async Task SendHeartbeatAsync(PlayerProfile self, CancellationToken cancellation)
        {
            string json = Json.Serialize(self.ToJson(includeKind: false));
            var response = await SendAsync(RoomProtocol.PlayerPing, Encoding.UTF8.GetBytes(json), RequestTimeout, cancellation)
                .ConfigureAwait(false);
            if (!response.IsOk)
            {
                string reply = Encoding.UTF8.GetString(response.Body);
                if (reply == RoomServer.RemovedReply || reply == RoomServer.BlockedReply)
                    throw new RoomRejectedException(reply == RoomServer.BlockedReply);
                throw new InvalidDataException("Heartbeat rejected: " + reply);
            }
        }

        /// <summary>Everyone in the room as the host sees it.</summary>
        public async Task<IReadOnlyList<PlayerProfile>> GetPlayersAsync(CancellationToken cancellation)
        {
            var response = await SendAsync(RoomProtocol.PlayerList, null, RequestTimeout, cancellation).ConfigureAwait(false);
            if (!response.IsOk)
                throw new InvalidDataException("Player list rejected.");

            if (!(Json.TryParse(Encoding.UTF8.GetString(response.Body)) is object[] items))
                throw new InvalidDataException("Player list is not an array.");
            return items.Select(item => PlayerProfile.FromJson(item, null)).Where(p => p != null).ToList();
        }

        public void Dispose()
        {
            _client.Close();
            _lock.Dispose();
        }

        /// <summary>Whether <paramref name="ex"/> means the connection failed rather than a programming error.</summary>
        public static bool IsConnectionError(Exception ex)
        {
            return ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidDataException
                   || ex is TimeoutException || ex is ArgumentException || ex is InvalidOperationException;
        }

        private async Task<RoomFraming.Response> SendAsync(string kind, byte[] body, TimeSpan timeout, CancellationToken cancellation)
        {
            await _lock.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                // Socket reads ignore tokens on .NET Framework, so closing the socket is what ends a stuck request.
                using (var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (expiry.Token.Register(_client.Close))
                {
                    expiry.CancelAfter(timeout);
                    try
                    {
                        await RoomFraming.WriteRequestAsync(_stream, kind, body, expiry.Token).ConfigureAwait(false);
                        return await RoomFraming.ReadResponseAsync(_stream, expiry.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (expiry.IsCancellationRequested && !cancellation.IsCancellationRequested)
                    {
                        throw new TimeoutException("The host did not answer " + kind + ".", ex);
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}

namespace Campfire.Link.Room
{
    /// <summary>The host took this player out of the room, or blocked them.</summary>
    internal sealed class RoomRejectedException : Exception
    {
        public RoomRejectedException(bool blocked)
            : base(blocked ? "The host blocked this player." : "The host removed this player from the room.")
        {
            Blocked = blocked;
        }

        public bool Blocked { get; }
    }
}
