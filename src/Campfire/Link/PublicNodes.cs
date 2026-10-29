using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link.Runtime;

namespace Campfire.Link
{
    /// <summary>One built-in public node. Pages show its region and provider, never its address.</summary>
    internal sealed class PublicNode
    {
        public PublicNode(string id, string url, string provider, bool limited = false)
        {
            Id = id;
            Url = url;
            Provider = provider;
            IsLimited = limited;
        }

        /// <summary>Stable name used in settings, so a node keeps its choice when its address changes.</summary>
        public string Id { get; }

        public string Url { get; }

        public string Provider { get; }

        /// <summary>Whether the provider caps relayed traffic, which slows down large transfers such as mod files.</summary>
        public bool IsLimited { get; }

        /// <summary>Localization key of the region and network shown for this node.</summary>
        public string RegionKey => "Nodes.Region." + Id;
    }

    internal enum NodeHealth
    {
        /// <summary>Not tested yet, or only custom nodes are in use.</summary>
        Unknown,
        Good,
        Slow,
        Down,
    }

    /// <summary>
    /// The built-in public nodes and their measured latency. Players only find each other when they share at least
    /// one node, so a node is only removed once it no longer answers anyone.
    /// </summary>
    internal static class PublicNodes
    {
        /// <summary>Results older than this are measured again when the list is shown.</summary>
        public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(10);

        /// <summary>Above this round-trip time a relayed game starts to feel sluggish.</summary>
        public const double HighLatencyMs = 150;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, double?> Latencies = new Dictionary<string, double?>();
        private static Task _test;

        /// <summary>
        /// Community nodes that relay traffic, spread over several providers so one blocked or busy node is not
        /// enough to stop a room. The last one is the only node earlier versions can still reach.
        /// </summary>
        public static IReadOnlyList<PublicNode> All { get; } = new[]
        {
            new PublicNode("sh-ct", "tcp://225284.xyz:11010", "海波"),
            new PublicNode("xm-ct", "tcp://easytier.weiai.org.cn:11010", "为爱，唯爱"),
            new PublicNode("cq-cm", "tcp://183.230.36.171:11010", "Just the Two of Us"),
            new PublicNode("zz-bgp", "udp://et.basd1.de:11010", "宇智波.希"),
            new PublicNode("sh-bgp", "tcp://et.sumnight.top:11010", "夏夜", limited: true),
            new PublicNode("cn-eo", "wss://et.chinokou.cn", "ChinoKou"),
            new PublicNode("yn-cu", "https://et-public.lctn.top", "凉城听暖"),
            new PublicNode("intl-cf", "wss://et.xn--6kr850c.com", "林然"),
            new PublicNode("intl-wk", "https://etnode.zkitefly.eu.org/node2", "zkitefly"),
        };

        /// <summary>Raised on the UI thread or a worker thread whenever a result or the testing state changes.</summary>
        public static event EventHandler Changed;

        /// <summary>When the last complete test finished, or <c>null</c> before the first one.</summary>
        public static DateTime? TestedAt { get; private set; }

        /// <summary>How the enabled nodes did in the last complete test; kept while a new test runs.</summary>
        public static NodeHealth Health { get; private set; }

        /// <summary>Lowest latency among the enabled nodes in the last complete test.</summary>
        public static double? BestLatency { get; private set; }

        public static bool IsTesting
        {
            get
            {
                lock (Sync)
                    return _test != null;
            }
        }

        /// <summary>Whether <paramref name="node"/> has been measured, and its latency in milliseconds if it answered.</summary>
        public static bool TryGetLatency(PublicNode node, out double? latency)
        {
            lock (Sync)
                return Latencies.TryGetValue(node.Id, out latency);
        }

        /// <summary>Measures every node at once. A test that is already running is joined rather than repeated.</summary>
        public static Task TestAsync()
        {
            lock (Sync)
            {
                if (_test == null)
                    _test = RunTestAsync();
                return _test;
            }
        }

        /// <summary>Works out <see cref="Health"/> again, for instance after the choice of nodes changed.</summary>
        public static void Evaluate()
        {
            if (TestedAt == null)
                return;

            List<double?> results;
            lock (Sync)
                results = All.Where(LinkSettings.IsNodeEnabled)
                    .Where(n => Latencies.ContainsKey(n.Id))
                    .Select(n => Latencies[n.Id])
                    .ToList();

            var reachable = results.Where(l => l.HasValue).Select(l => l.Value).ToList();
            BestLatency = reachable.Count > 0 ? reachable.Min() : (double?)null;
            Health = results.Count == 0 ? NodeHealth.Unknown
                : BestLatency == null ? NodeHealth.Down
                : BestLatency <= HighLatencyMs ? NodeHealth.Good
                : NodeHealth.Slow;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static async Task RunTestAsync()
        {
            lock (Sync)
                Latencies.Clear();
            Changed?.Invoke(null, EventArgs.Empty);
            try
            {
                await EasyTierPackage.InstallAsync(null, CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(All.Select(async node =>
                {
                    double? latency = await NodeProbe.MeasureAsync(node.Url, CancellationToken.None).ConfigureAwait(false);
                    lock (Sync)
                        Latencies[node.Id] = latency;
                    Changed?.Invoke(null, EventArgs.Empty);
                })).ConfigureAwait(false);
                TestedAt = DateTime.Now;
                Evaluate();
            }
            catch (Exception ex)
            {
                LinkLog.Warn("Node test failed: " + ex.Message);
                throw;
            }
            finally
            {
                lock (Sync)
                    _test = null;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }
    }
}
