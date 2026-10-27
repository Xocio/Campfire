using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Checks whether a Minecraft server answers on a local port.
    /// </summary>
    internal static class MinecraftProbe
    {
        /// <summary>
        /// Sends the legacy server-list ping (0xFE). Every Java Edition server, old or new, answers it with a
        /// disconnect packet that starts with 0xFF.
        /// </summary>
        public static async Task<bool> IsServerAsync(int port, TimeSpan timeout)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    var exchange = ExchangeAsync(client, port);
                    if (await Task.WhenAny(exchange, Task.Delay(timeout)).ConfigureAwait(false) != exchange)
                        return false;
                    return await exchange.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is System.IO.IOException)
                {
                    return false;
                }
            }
        }

        private static async Task<bool> ExchangeAsync(TcpClient client, int port)
        {
            await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 0xFE }, 0, 1).ConfigureAwait(false);
            var reply = new byte[1];
            return await stream.ReadAsync(reply, 0, 1).ConfigureAwait(false) == 1 && reply[0] == 0xFF;
        }

        /// <summary>
        /// Asks a local world for its version name, message and player limit with the 1.4 form of the legacy
        /// ping (0xFE 0x01), which servers since 1.4 answer. Returns <c>null</c> when it gives no such answer.
        /// </summary>
        public static async Task<ServerStatus> GetStatusAsync(int port, TimeSpan timeout)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    var exchange = StatusExchangeAsync(client, port);
                    if (await Task.WhenAny(exchange, Task.Delay(timeout)).ConfigureAwait(false) != exchange)
                        return null;
                    return await exchange.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is System.IO.IOException)
                {
                    return null;
                }
            }
        }

        private static async Task<ServerStatus> StatusExchangeAsync(TcpClient client, int port)
        {
            await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 0xFE, 0x01 }, 0, 2).ConfigureAwait(false);

            // 0xFF, the length in UTF-16 units as a big-endian short, then the text in UTF-16BE.
            var head = await ReadExactlyAsync(stream, 3).ConfigureAwait(false);
            if (head == null || head[0] != 0xFF)
                return null;
            int length = (head[1] << 8) | head[2];
            var body = await ReadExactlyAsync(stream, length * 2).ConfigureAwait(false);
            if (body == null)
                return null;

            // "§1", protocol, version name, message, players, player limit.
            var parts = System.Text.Encoding.BigEndianUnicode.GetString(body).Split('\0');
            if (parts.Length < 6 || parts[0] != "§1")
                return null;
            int.TryParse(parts[5], out int max);
            return new ServerStatus(parts[2], parts[3], max);
        }

        private static async Task<byte[]> ReadExactlyAsync(System.IO.Stream stream, int count)
        {
            var buffer = new byte[count];
            for (int read = 0; read < count;)
            {
                int n = await stream.ReadAsync(buffer, read, count - read).ConfigureAwait(false);
                if (n == 0)
                    return null;
                read += n;
            }
            return buffer;
        }
    }

    /// <summary>What a world says about itself in the server list.</summary>
    internal sealed class ServerStatus
    {
        public ServerStatus(string version, string message, int maxPlayers)
        {
            Version = version ?? "";
            Message = message ?? "";
            MaxPlayers = maxPlayers;
        }

        /// <summary>The version name, such as "1.20.1".</summary>
        public string Version { get; }

        /// <summary>
        /// The server list message. A LAN world shows "player - world name", where the world name is the
        /// world's name as the player gave it.
        /// </summary>
        public string Message { get; }

        public int MaxPlayers { get; }

        /// <summary>The world name of a LAN world, or the whole message for anything else, without color codes.</summary>
        public string WorldName
        {
            get
            {
                string text = System.Text.RegularExpressions.Regex.Replace(Message, "§.?", "");
                int dash = text.IndexOf(" - ", StringComparison.Ordinal);
                return (dash >= 0 ? text.Substring(dash + 3) : text).Trim();
            }
        }
    }
}
