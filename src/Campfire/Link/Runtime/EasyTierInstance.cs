using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Launcher;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// A running virtual network process, controlled through its local management port.
    /// </summary>
    internal sealed class EasyTierInstance : IDisposable
    {
        private const int OutputLines = 300;
        private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(10);

        // Starts one at a time.
        private static readonly object StartSync = new object();

        private readonly Process _process;
        private readonly string _configPath;
        private readonly int _rpcPort;
        private readonly Queue<string> _output = new Queue<string>();
        private int _disposed;

        private EasyTierInstance(Process process, string configPath, int rpcPort)
        {
            _process = process;
            _configPath = configPath;
            _rpcPort = rpcPort;
        }

        /// <summary>Raised on a worker thread when the process exits on its own.</summary>
        public event EventHandler Exited;

        public bool IsAlive
        {
            get
            {
                try
                {
                    return !_process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public static EasyTierInstance Start(EasyTierConfig config)
        {
            lock (StartSync)
                return StartLocked(config);
        }

        private static EasyTierInstance StartLocked(EasyTierConfig config)
        {
            StopLeftovers();

            string runtimeDirectory = Path.Combine(LinkSettings.DataDirectory, "runtime");
            Directory.CreateDirectory(runtimeDirectory);
            string configPath = Path.Combine(runtimeDirectory, Guid.NewGuid().ToString("N") + ".toml");
            File.WriteAllText(configPath, config.ToToml(), new UTF8Encoding(false));

            int rpcPort = FindFreePort();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(EasyTierPackage.CorePath)
                {
                    Arguments = $"-c \"{configPath}\" -r 127.0.0.1:{rpcPort} --multi-thread",
                    WorkingDirectory = runtimeDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };

            var instance = new EasyTierInstance(process, configPath, rpcPort);
            process.OutputDataReceived += (sender, e) => instance.Capture(e.Data);
            process.ErrorDataReceived += (sender, e) => instance.Capture(e.Data);
            process.Exited += (sender, e) => instance.OnExited();

            LinkLog.Info($"Starting network instance (rpc {rpcPort}, host name {config.Hostname}, hidden address {config.HideAddress}).");
            process.Start();
            ChildProcessJob.Assign(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return instance;
        }

        /// <summary>Members of the network, this computer included. Empty when the process is not ready yet.</summary>
        public async Task<IReadOnlyList<EasyTierPeer>> GetPeersAsync(CancellationToken cancellation)
        {
            string json = await RunCliAsync(cancellation, "-o", "json", "peer").ConfigureAwait(false);
            if (json == null || !(Parse(json) is object[] items))
                return Array.Empty<EasyTierPeer>();

            return items.OfType<IDictionary<string, object>>().Select(EasyTierPeer.FromJson).ToList();
        }

        /// <summary>How many of the configured public nodes are currently connected, or -1 when unknown.</summary>
        public async Task<int> CountConnectedNodesAsync(CancellationToken cancellation)
        {
            string json = await RunCliAsync(cancellation, "-o", "json", "connector").ConfigureAwait(false);
            if (json == null || !(Parse(json) is object[] items))
                return -1;

            // Status 0 means connected.
            return items.OfType<IDictionary<string, object>>()
                .Count(item => item.TryGetValue("status", out var status) && Convert.ToInt32(status, CultureInfo.InvariantCulture) == 0);
        }

        /// <summary>Forwards connections to <paramref name="bind"/> on this computer to <paramref name="target"/> in the network.</summary>
        /// <param name="protocol">"tcp" or "udp".</param>
        public async Task<bool> AddPortForwardAsync(string protocol, IPEndPoint bind, IPEndPoint target, CancellationToken cancellation)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string output = await RunCliAsync(cancellation, "port-forward", "add", protocol, Format(bind), Format(target))
                    .ConfigureAwait(false);
                if (output != null)
                    return true;
                await Task.Delay(500 + attempt * 1000, cancellation).ConfigureAwait(false);
            }
            LinkLog.Warn($"Port forward {protocol} {Format(bind)} -> {Format(target)} failed.");
            return false;
        }

        /// <summary>The last lines the process printed, for error reports.</summary>
        public string GetRecentOutput()
        {
            lock (_output)
                return string.Join(Environment.NewLine, _output);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(3000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
            }
            _process.Dispose();

            try
            {
                File.Delete(_configPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            LinkLog.Info("Network instance stopped.");
        }

        // ======================== Helpers ========================

        /// <summary>Runs the command-line tool against this instance; returns its output, or <c>null</c> on failure.</summary>
        private async Task<string> RunCliAsync(CancellationToken cancellation, params string[] arguments)
        {
            if (!IsAlive)
                return null;

            var start = new ProcessStartInfo(EasyTierPackage.CliPath)
            {
                Arguments = $"-p 127.0.0.1:{_rpcPort} " + string.Join(" ", arguments.Select(QuoteArgument)),
                WorkingDirectory = EasyTierPackage.InstallDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            return await Task.Run(() =>
            {
                using (var cli = new Process { StartInfo = start })
                {
                    try
                    {
                        cli.Start();
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                    {
                        LinkLog.Warn("Command-line tool failed to start: " + ex.Message);
                        return null;
                    }

                    var stdout = cli.StandardOutput.ReadToEndAsync();
                    var stderr = cli.StandardError.ReadToEndAsync();
                    if (!cli.WaitForExit((int)CliTimeout.TotalMilliseconds))
                    {
                        try
                        {
                            cli.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        return null;
                    }
                    cli.WaitForExit();

                    if (cli.ExitCode != 0)
                    {
                        LinkLog.Info($"Command '{string.Join(" ", arguments)}' exited with {cli.ExitCode}: {stderr.Result.Trim()}");
                        return null;
                    }
                    return stdout.Result;
                }
            }, cancellation).ConfigureAwait(false);
        }

        private static object Parse(string json)
        {
            return Json.TryParse(json.Trim());
        }

        private void Capture(string line)
        {
            if (line == null)
                return;
            lock (_output)
            {
                _output.Enqueue(line);
                while (_output.Count > OutputLines)
                    _output.Dequeue();
            }
        }

        private void OnExited()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            int code;
            try
            {
                code = _process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = -1;
            }
            LinkLog.Error($"Network instance exited unexpectedly with code {code}. Recent output:{Environment.NewLine}{GetRecentOutput()}");
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private static string Format(IPEndPoint endPoint)
        {
            return endPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{endPoint.Address}]:{endPoint.Port}"
                : $"{endPoint.Address}:{endPoint.Port}";
        }

        private static string QuoteArgument(string argument)
        {
            return argument.IndexOfAny(new[] { ' ', '"' }) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>A port nobody listens on right now. Good enough for local control ports.</summary>
        internal static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// Ends network processes whose Campfire is gone. Processes whose parent still runs are left alone.
        /// </summary>
        private static void StopLeftovers()
        {
            bool othersAlive = false;
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(EasyTierPackage.CorePath)))
            {
                using (process)
                {
                    string image = LauncherProcess.TryGetImagePath(process);
                    if (image == null || !image.StartsWith(EasyTierPackage.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (HasLivingParent(process))
                    {
                        othersAlive = true;
                        continue;
                    }

                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                        LinkLog.Info("Stopped a leftover network process " + process.Id + ".");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }

            // Clears the configuration files of rooms that ended with a crash; only when no other room is alive.
            string runtimeDirectory = Path.Combine(LinkSettings.DataDirectory, "runtime");
            if (othersAlive || !Directory.Exists(runtimeDirectory))
                return;
            foreach (string file in Directory.GetFiles(runtimeDirectory, "*.toml"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        private static bool HasLivingParent(Process process)
        {
            try
            {
                var info = new ProcessBasicInformation();
                if (NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
                    return true;

                int parentId = info.InheritedFromUniqueProcessId.ToInt32();
                using (var parent = Process.GetProcessById(parentId))
                {
                    // Process ids are reused; a parent that started later is a different program.
                    return parent.StartTime <= process.StartTime;
                }
            }
            catch (ArgumentException)
            {
                // No process with the parent id: the Campfire that started it is gone.
                return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                // When in doubt, leave the process running.
                return true;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info,
            int size, out int returned);
    }
}
