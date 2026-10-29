using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// Measures one public node by joining it with a throwaway network, the same way a room connects.
    /// </summary>
    /// <remarks>
    /// The test network has a random name, accepts no incoming connections and never connects to other members,
    /// so the node only learns that this computer reached it.
    /// </remarks>
    internal static class NodeProbe
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        /// <summary>Round-trip time to the node in milliseconds, or <c>null</c> when it could not be reached.</summary>
        public static async Task<double?> MeasureAsync(string url, CancellationToken cancellation)
        {
            var config = new EasyTierConfig
            {
                NetworkName = "campfire-probe-" + Hex.Random(8),
                NetworkSecret = Hex.Random(16),
                Hostname = "campfire-probe",
                HideAddress = true,
            };
            config.Peers.Add(url);
            config.TcpWhitelist.Add("0");
            config.UdpWhitelist.Add("0");

            using (var instance = await Task.Run(() => EasyTierInstance.Start(config), cancellation).ConfigureAwait(false))
            {
                var deadline = DateTime.UtcNow + Timeout;
                double? first = null;
                while (DateTime.UtcNow < deadline && instance.IsAlive)
                {
                    await Task.Delay(1000, cancellation).ConfigureAwait(false);
                    var peers = await instance.GetPeersAsync(cancellation).ConfigureAwait(false);
                    // The node is the only peer that is not a member of the network.
                    double? latency = peers.Where(p => !p.IsLocal && !p.IsMember && p.LatencyMs > 0)
                        .Select(p => p.LatencyMs).FirstOrDefault();
                    if (latency == null)
                        continue;

                    // The first sample can still include the handshake; one more settles it.
                    if (first == null)
                    {
                        first = latency;
                        continue;
                    }
                    return Math.Min(first.Value, latency.Value);
                }
                return first;
            }
        }
    }
}
