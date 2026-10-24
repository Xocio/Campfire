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
    }
}
