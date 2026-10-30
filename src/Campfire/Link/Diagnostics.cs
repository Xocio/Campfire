using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link.Minecraft;
using Campfire.Link.Runtime;
using Campfire.Localization;

namespace Campfire.Link
{
    internal enum DiagState
    {
        Pending,
        Running,
        Ok,

        /// <summary>Nothing wrong, but worth knowing.</summary>
        Info,
        Warn,
        Fail,
    }

    internal sealed class DiagResult
    {
        public DiagResult(DiagState state, string detail)
        {
            State = state;
            Detail = detail;
        }

        public DiagState State { get; }

        public string Detail { get; }
    }

    internal sealed class DiagCheck
    {
        public DiagCheck(string key, Func<CancellationToken, Task<DiagResult>> run)
        {
            Key = key;
            Run = run;
        }

        /// <summary>Name of the check; its title is the text <c>Diag.&lt;Key&gt;</c>.</summary>
        public string Key { get; }

        public Func<CancellationToken, Task<DiagResult>> Run { get; }
    }

    /// <summary>
    /// The checks behind "Multiplayer diagnostics": what a room needs from this computer, one after the other.
    /// Nothing is changed or sent to other players.
    /// </summary>
    internal static class Diagnostics
    {
        public static IReadOnlyList<DiagCheck> Checks { get; } = new[]
        {
            new DiagCheck("System", CheckSystem),
            new DiagCheck("Component", CheckComponent),
            new DiagCheck("Name", CheckName),
            new DiagCheck("Network", CheckNetwork),
            new DiagCheck("Nodes", CheckNodes),
            new DiagCheck("Relay", CheckRelay),
            new DiagCheck("Port", CheckPort),
            new DiagCheck("Game", CheckGame),
            new DiagCheck("World", CheckWorld),
        };

        private static Task<DiagResult> Done(DiagState state, string key, params object[] arguments)
        {
            return Task.FromResult(new DiagResult(state, Text.Format(key, arguments)));
        }

        private static Task<DiagResult> CheckSystem(CancellationToken token)
        {
            if (!EasyTierPackage.IsSupportedSystem)
                return Done(DiagState.Fail, "Diag.System.Old");
            if (EasyTierPackage.IsWindows7 && !EasyTierPackage.HasLoaderUpdate)
                return Done(DiagState.Fail, "Diag.System.Windows7");
            return Done(DiagState.Ok, "Diag.System.Ok", EasyTierPackage.WindowsVersion, EasyTierPackage.Architecture);
        }

        private static async Task<DiagResult> CheckComponent(CancellationToken token)
        {
            if (!EasyTierPackage.IsInstalled())
                return new DiagResult(DiagState.Info, Text.Get("Diag.Component.Missing"));
            if (EasyTierPackage.IsWindows7 && !await EasyTierPackage.CanRunAsync().ConfigureAwait(false))
                return new DiagResult(DiagState.Fail, Text.Get("Diag.Component.Broken"));
            return new DiagResult(DiagState.Ok, Text.Get("Diag.Component.Ok"));
        }

        private static Task<DiagResult> CheckName(CancellationToken token)
        {
            return LinkSettings.PlayerName.Length > 0
                ? Done(DiagState.Ok, "Diag.Name.Ok", LinkSettings.PlayerName)
                : Done(DiagState.Warn, "Diag.Name.Empty");
        }

        private static Task<DiagResult> CheckNetwork(CancellationToken token)
        {
            return Task.Run(() =>
            {
                if (!NetworkCheck.GetIPv4Addresses().Any(a => !IPAddress.IsLoopback(a)))
                    return new DiagResult(DiagState.Fail, Text.Get("Diag.Network.None"));
                return new DiagResult(DiagState.Ok, Text.Get(NetworkCheck.HasPublicIPv6() ? "Diag.Network.OkV6" : "Diag.Network.Ok"));
            });
        }

        private static async Task<DiagResult> CheckNodes(CancellationToken token)
        {
            if (!LinkSettings.UsePublicNodes)
                return new DiagResult(DiagState.Info, Text.Get("Diag.Nodes.Own"));
            if (!EasyTierPackage.IsInstalled())
                return new DiagResult(DiagState.Info, Text.Get("Diag.Nodes.NeedComponent"));

            // The measurement has its own time limit; waiting here only lets the page stop early.
            var test = PublicNodes.TestAsync();
            var cancelled = new TaskCompletionSource<bool>();
            using (token.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(test, cancelled.Task).ConfigureAwait(false) != test)
                    token.ThrowIfCancellationRequested();
            }

            var nodes = PublicNodes.All.Where(LinkSettings.IsNodeEnabled).ToList();
            var latencies = new List<double>();
            foreach (var node in nodes)
            {
                if (PublicNodes.TryGetLatency(node, out double? latency) && latency.HasValue)
                    latencies.Add(latency.Value);
            }

            if (latencies.Count == 0)
                return new DiagResult(DiagState.Fail, Text.Get("Diag.Nodes.None"));
            double best = latencies.Min();
            string summary = Text.Format("Diag.Nodes.Ok", latencies.Count, nodes.Count, LinkSession.FormatLatency(best));
            return new DiagResult(best > PublicNodes.HighLatencyMs ? DiagState.Warn : DiagState.Ok, summary);
        }

        private static Task<DiagResult> CheckRelay(CancellationToken token)
        {
            if (LinkSettings.HideAddress)
                return Done(DiagState.Info, "Diag.Relay.Hidden");
            return LinkSettings.AllowRelay ? Done(DiagState.Ok, "Diag.Relay.Ok") : Done(DiagState.Warn, "Diag.Relay.Off");
        }

        private static Task<DiagResult> CheckPort(CancellationToken token)
        {
            int port = LinkSettings.LocalPort;
            if (port == 0)
                return Done(DiagState.Ok, "Diag.Port.Auto");

            return Task.Run(() =>
            {
                try
                {
                    var properties = IPGlobalProperties.GetIPGlobalProperties();
                    bool used = properties.GetActiveTcpListeners().Any(p => p.Port == port)
                                || properties.GetActiveUdpListeners().Any(p => p.Port == port);
                    return new DiagResult(used ? DiagState.Fail : DiagState.Ok,
                        Text.Format(used ? "Diag.Port.Busy" : "Diag.Port.Free", port));
                }
                catch (NetworkInformationException)
                {
                    return new DiagResult(DiagState.Info, Text.Get("Diag.Port.Unknown"));
                }
            });
        }

        private static async Task<DiagResult> CheckGame(CancellationToken token)
        {
            var account = await GameAccount.RefreshAsync().ConfigureAwait(false);
            return account == null
                ? new DiagResult(DiagState.Info, Text.Get("Diag.Game.None"))
                : new DiagResult(DiagState.Ok, Text.Format("Diag.Game.Ok", account.Name));
        }

        private static async Task<DiagResult> CheckWorld(CancellationToken token)
        {
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    int port = await LocalWorldFinder.WaitForLocalWorldAsync(limit.Token).ConfigureAwait(false);
                    return new DiagResult(DiagState.Ok, Text.Format("Diag.World.Ok", port));
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return new DiagResult(DiagState.Info, Text.Get("Diag.World.None"));
                }
            }
        }
    }
}
