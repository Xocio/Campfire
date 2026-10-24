using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Localization;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Attaches the small Java agent that adds the room header, footer and welcome message from inside
    /// the host's own Minecraft server. This is the only path that works in a normal LAN world, whose
    /// login connection is encrypted before Campfire can see any play packets.
    /// </summary>
    internal static class JavaAgent
    {
        private const string AgentResource = "Campfire.CampfireAgent.jar";
        private const string AttachResource = "Campfire.CampfireAttach.jar";
        private const string HookResource = "Campfire.CampfireHook.jar";
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        /// <summary>How long to wait for the hook to confirm it runs; the server ticks twenty times a second.</summary>
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(6);

        /// <summary>The room state the hook reads; see <see cref="AgentState"/>.</summary>
        internal static string StatePath(int processId) => Path.Combine(LinkSettings.DataDirectory, "overlay-" + processId + ".txt");

        /// <summary>Written by the hook once it shows the overlay in the game.</summary>
        internal static string ReadyPath(int processId) => Path.Combine(LinkSettings.DataDirectory, "overlay-" + processId + ".ready");

        /// <summary>
        /// Loads the agent into <paramref name="processId"/> and waits until it shows the overlay. Returns
        /// <c>false</c> when the game runs on a JRE without the attach module, the agent cannot be loaded or
        /// the game is not a version it knows; the room itself keeps working. An <see cref="AgentState"/>
        /// for the process must already exist, since the hook only runs while that state is fresh.
        /// </summary>
        public static async Task<bool> AttachAsync(int processId, CancellationToken cancellation)
        {
            if (processId <= 0)
                return false;

            await Gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                string ready = ReadyPath(processId);

                string java = FindJava(processId);
                if (java == null)
                {
                    LinkLog.Warn("Could not find the Java executable of the running game; in-game overlay skipped.");
                    return false;
                }

                // Content hashes, not sizes: two builds can produce jars of exactly the same length.
                string directory = Path.Combine(LinkSettings.DataDirectory, "agent",
                    "v" + ResourceHash(AgentResource) + "-" + ResourceHash(HookResource) + "-" + ResourceHash(AttachResource));
                Directory.CreateDirectory(directory);
                string agent = Extract(AgentResource, Path.Combine(directory, "CampfireAgent.jar"));
                string attach = Extract(AttachResource, Path.Combine(directory, "CampfireAttach.jar"));
                Extract(HookResource, Path.Combine(directory, "CampfireHook.jar"));

                string options = "state=" + LinkSettings.DataDirectory;
                var startInfo = new ProcessStartInfo(java)
                {
                    Arguments = "--add-modules jdk.attach -cp " + Quote(attach) + " CampfireAttach "
                                + processId.ToString(CultureInfo.InvariantCulture) + " " + Quote(agent) + " " + Quote(options),
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    bool exited = await Task.Run(() => process.WaitForExit(20000), cancellation).ConfigureAwait(false);
                    if (!exited)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        LinkLog.Warn("The Java agent did not finish attaching in time.");
                        return false;
                    }

                    string errorText = await error.ConfigureAwait(false);
                    await output.ConfigureAwait(false);
                    if (process.ExitCode != 0)
                    {
                        LinkLog.Warn("The Java agent could not be attached: " + errorText.Trim());
                        return false;
                    }
                }

                // Loading the agent says nothing about the game: an older or modded version has other class
                // names, and the hook then stays idle. Only its own confirmation counts.
                var deadline = DateTime.UtcNow + ReadyTimeout;
                while (!File.Exists(ready))
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        LinkLog.Warn("The Java agent was loaded, but the game did not start the overlay; see agent.log.");
                        return false;
                    }
                    await Task.Delay(250, cancellation).ConfigureAwait(false);
                }

                LinkLog.Info("In-game overlay agent attached to game process " + processId + ".");
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception
                                       || ex is NotSupportedException || ex is ArgumentException)
            {
                LinkLog.Warn("The Java agent could not be attached: " + ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                LinkLog.Warn("The Java agent could not be attached: " + ex.Message);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static string FindJava(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    string path = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path))
                        return null;

                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        string console = Path.Combine(directory, "java.exe");
                        if (File.Exists(console))
                            return console;
                    }
                    return path;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException
                                       || ex is System.ComponentModel.Win32Exception || ex is NotSupportedException)
            {
                return null;
            }
        }

        private static string ResourceHash(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded resource " + name + ".");
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                }
            }
        }

        private static string Extract(string name, string path)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded resource " + name + ".");

                if (File.Exists(path) && new FileInfo(path).Length == stream.Length)
                    return path;

                string temporary = path + ".tmp";
                using (var file = File.Create(temporary))
                    stream.CopyTo(file);
                File.Copy(temporary, path, true);
                File.Delete(temporary);
                return path;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    /// <summary>
    /// Tells the hook in the game that the room is open, and in which language to speak. The file is rewritten
    /// every few seconds and deleted when the room ends; the hook stops and clears the player list as soon as
    /// it is gone or no longer fresh, which also covers Campfire being closed abruptly.
    /// </summary>
    internal sealed class AgentState : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly object _sync = new object();
        private readonly string _path;
        private readonly string _readyPath;
        private readonly Timer _timer;
        private bool _disposed;

        public AgentState(int processId)
        {
            _path = JavaAgent.StatePath(processId);
            _readyPath = JavaAgent.ReadyPath(processId);
            // A confirmation from an earlier room must not count for this one.
            TryDelete(_readyPath);
            Write();
            _timer = new Timer(_ => Write(), null, Interval, Interval);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            _timer.Dispose();
            TryDelete(_path);
            TryDelete(_readyPath);
        }

        private void Write()
        {
            var lines = new[]
            {
                "time=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                "tabTitle=" + Text.Get("Link.Game.TabTitle"),
                "welcome=" + Text.Get("Link.Game.Welcome"),
                "host=" + Text.Get("Link.Game.Host"),
                "online=" + Text.Get("Link.Game.Online"),
                "players=" + Text.Get("Link.Game.PlayersValue"),
                "latency=" + Text.Get("Link.Game.Latency"),
                "latencyValue=" + Text.Get("Link.Game.LatencyValue"),
                "measuring=" + Text.Get("Link.Game.LatencyMeasuring"),
            };

            lock (_sync)
            {
                if (_disposed)
                    return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    // Written aside and swapped in, so the hook never reads half a file.
                    string temporary = _path + ".tmp";
                    File.WriteAllText(temporary, string.Join("\n", lines) + "\n", Utf8);
                    if (File.Exists(_path))
                        File.Replace(temporary, _path, null);
                    else
                        File.Move(temporary, _path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // The hook keeps the last state for a while; the next write usually succeeds.
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
