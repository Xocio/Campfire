using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// The networking component that multiplayer runs on. It is downloaded on first use into
    /// %LocalAppData%\Campfire\EasyTier and every file is checked against a known SHA-256 hash, so a mirror
    /// cannot slip in a modified program.
    /// </summary>
    internal static class EasyTierPackage
    {
        public const string Version = "2.6.4";

        private const string CoreFile = "easytier-core.exe";
        private const string CliFile = "easytier-cli.exe";
        private const string PacketFile = "Packet.dll";
        private const int ProbeSize = 64 * 1024;

        private static readonly string[] Files = { CoreFile, CliFile, PacketFile };

        // SHA-256 of each file in the official release, in the order of Files.
        private static readonly Dictionary<string, string[]> Hashes = new Dictionary<string, string[]>
        {
            ["x86_64"] = new[]
            {
                "da7eb2d24b5416f3d3407636949e964a0750e3f9dc53a828cb6799a57ead445d",
                "d8783e851e944b44a9b71b39fd02f227ec0a2a82b3165c55ead5dd32dcde53a1",
                "c7c03a87eac7243ccbe331554624b18803010b740e311fc8cfddb573096eacac",
            },
            ["arm64"] = new[]
            {
                "da912e2befd4c59c81228c0dac7e19f0190aa4221475117108ad2f0045b3065d",
                "295852b6d68ab5c675437f19f37e975e90d41814ec76eabc135d3eb367bfccd0",
                "fe56bf638c1f05751a4f36377d71f48fb7f43446e92475eda32dc0f4f2ebd60c",
            },
            ["i686"] = new[]
            {
                "657400c756aa4e2868b9505f669527733ba872dff3b8877cee24d7772616c76b",
                "e1d6a617e4cb3b9bf77c9d32616dace5d5715fabcbcb0f5f9969a98f3626d69a",
                "f0ab4f5457bd48958b72d6849148e1ece6dbc1059435dfe695051fc32b8cdc19",
            },
        };

        private static readonly object Sync = new object();
        private static bool _verified;

        /// <summary>"x86_64", "arm64" or "i686", matching the native processor of this computer.</summary>
        public static string Architecture { get; } = DetectArchitecture();

        /// <summary>The real Windows version, even where compatibility settings report an older one.</summary>
        public static Version WindowsVersion { get; } = GetRealWindowsVersion();

        /// <summary>The component runs on Windows 7 with Service Pack 1 and later.</summary>
        public static bool IsSupportedSystem { get; } = WindowsVersion >= new Version(6, 1, 7601);

        /// <summary>Windows 7, which needs two updates and cannot carry traffic over QUIC.</summary>
        public static bool IsWindows7 { get; } = WindowsVersion < new Version(6, 2);

        /// <summary>
        /// Whether Windows 7 has the update that lets programs choose where libraries load from
        /// (KB2533623, later replaced by KB3063858); the component does not start without it.
        /// </summary>
        public static bool HasLoaderUpdate
        {
            get
            {
                IntPtr kernel = GetModuleHandle("kernel32.dll");
                return kernel != IntPtr.Zero && GetProcAddress(kernel, "AddDllDirectory") != IntPtr.Zero;
            }
        }

        public static string InstallDirectory { get; } =
            Path.Combine(AppSettings.DataDirectory, "EasyTier", Version + "-" + Architecture);

        public static string CorePath => Path.Combine(InstallDirectory, CoreFile);

        public static string CliPath => Path.Combine(InstallDirectory, CliFile);

        /// <summary>Whether all files are present and intact. Hashes are checked once per run.</summary>
        public static bool IsInstalled()
        {
            lock (Sync)
            {
                if (_verified)
                    return true;
                _verified = Files.All(IsFileValid);
                return _verified;
            }
        }

        /// <summary>Deletes every downloaded version; it is downloaded again the next time it is needed.</summary>
        public static void Uninstall()
        {
            lock (Sync)
            {
                _verified = false;
                string root = Path.GetDirectoryName(InstallDirectory);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        /// <summary>Downloads whatever is missing or damaged, trying the fastest source first.</summary>
        public static async Task InstallAsync(IProgress<DownloadProgress> progress, CancellationToken cancellation)
        {
            if (await Task.Run(() => IsInstalled()).ConfigureAwait(false))
                return;

            Directory.CreateDirectory(InstallDirectory);
            using (var client = CreateClient())
            {
                var sources = await RankSourcesAsync(client, cancellation).ConfigureAwait(false);
                if (sources.Count == 0)
                    throw new LinkException("Link.Error.DownloadUnreachable");

                Exception last = null;
                foreach (var source in sources)
                {
                    try
                    {
                        LinkLog.Info($"Downloading component from {source.Url} (range: {source.SupportsRange}).");
                        if (source.SupportsRange)
                            await DownloadEntriesAsync(client, source, progress, cancellation).ConfigureAwait(false);
                        else
                            await DownloadArchiveAsync(client, source, progress, cancellation).ConfigureAwait(false);

                        lock (Sync)
                            _verified = Files.All(IsFileValid);
                        if (_verified)
                            return;
                        throw new InvalidDataException("Downloaded files failed verification.");
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        last = ex;
                        LinkLog.Warn($"Source {source.Url} failed: {ex.Message}");
                    }
                }
                throw new LinkException(last, "Link.Error.DownloadFailed", last?.Message);
            }
        }

        // ======================== Sources ========================

        private static IEnumerable<string> GetSourceUrls()
        {
            string asset = $"easytier-windows-{Architecture}-v{Version}.zip";
            string github = $"https://github.com/EasyTier/EasyTier/releases/download/v{Version}/{asset}";
            yield return $"https://gitee.com/easytier/EasyTier/releases/download/v{Version}/{asset}";
            yield return github;
            yield return "https://ghfast.top/" + github;
            yield return "https://gh-proxy.com/" + github;
        }

        /// <summary>
        /// Requests the first 64 KB from every source at once and orders them by the estimated total download
        /// time. Sources that support ranges only need the three files, about 40% of the archive.
        /// </summary>
        private static async Task<List<Source>> RankSourcesAsync(HttpClient client, CancellationToken cancellation)
        {
            var probes = GetSourceUrls().Select(url => ProbeAsync(client, url, cancellation)).ToList();
            var results = await Task.WhenAll(probes).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return results.Where(r => r != null).OrderBy(r => r.Score).ToList();
        }

        private static async Task<Source> ProbeAsync(HttpClient client, string url, CancellationToken cancellation)
        {
            var watch = Stopwatch.StartNew();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new RangeHeaderValue(0, ProbeSize - 1);
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                        .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return null;

                        bool partial = response.StatusCode == HttpStatusCode.PartialContent;
                        long? length = partial ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength;
                        if (length == null || length < ProbeSize)
                            return null;

                        // Read the probe window to measure real throughput, not just the connection time.
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            var counted = new ProgressStream(stream, response, null);
                            var buffer = new byte[16 * 1024];
                            int total = 0;
                            while (total < ProbeSize)
                            {
                                int read = await counted.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                                if (read == 0)
                                    break;
                                total += read;
                            }
                            if (total < ProbeSize)
                                return null;
                        }

                        double seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.05);
                        return new Source(url, partial, length.Value, seconds);
                    }
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    LinkLog.Info($"Probe {url} failed: {ex.Message}");
                    return null;
                }
            }
        }

        // ======================== Download ========================

        private static async Task DownloadEntriesAsync(HttpClient client, Source source, IProgress<DownloadProgress> progress,
            CancellationToken cancellation)
        {
            var zip = await RemoteZip.OpenAsync(client, source.Url, source.Length, cancellation).ConfigureAwait(false);
            var wanted = Files
                .Where(file => !IsFileValid(file))
                .Select(file => new { File = file, Entry = zip.Entries.FirstOrDefault(e => MatchesEntry(e.Name, file)) })
                .ToList();
            if (wanted.Any(w => w.Entry == null))
                throw new InvalidDataException("The archive does not contain the expected files.");

            long total = wanted.Sum(w => w.Entry.CompressedSize);
            long received = 0;
            progress?.Report(new DownloadProgress(0, total));

            foreach (var item in wanted)
            {
                string temp = TempPath(item.File);
                using (var output = File.Create(temp))
                {
                    await zip.ExtractAsync(item.Entry, output, bytes =>
                    {
                        received += bytes;
                        progress?.Report(new DownloadProgress(received, total));
                    }, cancellation).ConfigureAwait(false);
                }
                CommitFile(item.File, temp);
            }
        }

        private static async Task DownloadArchiveAsync(HttpClient client, Source source, IProgress<DownloadProgress> progress,
            CancellationToken cancellation)
        {
            string archive = Path.Combine(InstallDirectory, "download.zip.tmp");
            try
            {
                using (var response = await client.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellation)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? source.Length;
                    long received = 0;
                    progress?.Report(new DownloadProgress(0, total));

                    using (var network = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = File.Create(archive))
                    {
                        var counted = new ProgressStream(network, response, bytes =>
                        {
                            received += bytes;
                            progress?.Report(new DownloadProgress(received, total));
                        });
                        await counted.CopyToAsync(output, 81920, cancellation).ConfigureAwait(false);
                    }
                }

                using (var zip = ZipFile.OpenRead(archive))
                {
                    foreach (string file in Files.Where(f => !IsFileValid(f)))
                    {
                        var entry = zip.Entries.FirstOrDefault(e => MatchesEntry(e.FullName, file))
                            ?? throw new InvalidDataException("The archive does not contain " + file + ".");
                        string temp = TempPath(file);
                        entry.ExtractToFile(temp, overwrite: true);
                        CommitFile(file, temp);
                    }
                }
            }
            finally
            {
                TryDelete(archive);
            }
        }

        /// <summary>Moves a verified temporary file into place; a file that fails the check is discarded.</summary>
        private static void CommitFile(string file, string temp)
        {
            string target = Path.Combine(InstallDirectory, file);
            if (!string.Equals(ComputeHash(temp), ExpectedHash(file), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                throw new InvalidDataException(file + " failed verification.");
            }
            TryDelete(target);
            File.Move(temp, target);
        }

        // ======================== Helpers ========================

        private static bool IsFileValid(string file)
        {
            string path = Path.Combine(InstallDirectory, file);
            try
            {
                return File.Exists(path)
                       && string.Equals(ComputeHash(path), ExpectedHash(file), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string ExpectedHash(string file)
        {
            return Hashes[Architecture][Array.IndexOf(Files, file)];
        }

        private static string ComputeHash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return Hex.Of(sha.ComputeHash(stream));
        }

        private static bool MatchesEntry(string entryName, string file)
        {
            string name = entryName.Replace('\\', '/');
            return name.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, file, StringComparison.OrdinalIgnoreCase);
        }

        private static string TempPath(string file)
        {
            return Path.Combine(InstallDirectory, file + ".tmp");
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

        private static HttpClient CreateClient()
        {
            // TLS 1.2, which Windows 7 does not offer by default, is switched on at startup.
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Campfire/" + version);
            return client;
        }

        private static string DetectArchitecture()
        {
            try
            {
                if (IsWow64Process2(Process.GetCurrentProcess().Handle, out _, out ushort native))
                {
                    if (native == ImageFileMachineArm64)
                        return "arm64";
                    if (native == ImageFileMachineAmd64)
                        return "x86_64";
                    if (native == ImageFileMachineI386)
                        return "i686";
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Before Windows 10 1709.
            }
            return Environment.Is64BitOperatingSystem ? "x86_64" : "i686";
        }

        /// <summary>Unlike <see cref="Environment.OSVersion"/>, not affected by the compatibility manifest.</summary>
        private static Version GetRealWindowsVersion()
        {
            var info = new OsVersionInfo { Size = Marshal.SizeOf(typeof(OsVersionInfo)) };
            try
            {
                if (RtlGetVersion(ref info) == 0)
                    return new Version(info.MajorVersion, info.MinorVersion, info.BuildNumber);
            }
            catch (EntryPointNotFoundException)
            {
            }
            return Environment.OSVersion.Version;
        }

        /// <summary>
        /// Starts the component once to see that Windows can run it. On Windows 7 without the required updates it
        /// stops at once with a missing system function.
        /// </summary>
        public static async Task<bool> CanRunAsync()
        {
            try
            {
                var start = new ProcessStartInfo(CorePath, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var errors = process.StandardError.ReadToEndAsync();
                    var exited = Task.Run(() => process.WaitForExit(15000));
                    if (!await exited.ConfigureAwait(false))
                    {
                        process.Kill();
                        return true; // Slow but alive; the real start will tell.
                    }
                    await Task.WhenAll(output, errors).ConfigureAwait(false);
                    if (process.ExitCode != 0)
                        LinkLog.Warn($"Component self-test exited with 0x{process.ExitCode:X8}: {errors.Result.Trim()}");
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                LinkLog.Warn("Component self-test failed: " + ex.Message);
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OsVersionInfo
        {
            public int Size;
            public int MajorVersion;
            public int MinorVersion;
            public int BuildNumber;
            public int PlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string ServicePack;
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OsVersionInfo info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        private const ushort ImageFileMachineI386 = 0x014C;
        private const ushort ImageFileMachineAmd64 = 0x8664;
        private const ushort ImageFileMachineArm64 = 0xAA64;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        private sealed class Source
        {
            public Source(string url, bool supportsRange, long length, double probeSeconds)
            {
                Url = url;
                SupportsRange = supportsRange;
                Length = length;
                // Expected time = bytes to fetch / measured speed; ranged downloads fetch about 40% of the archive.
                Score = probeSeconds * (supportsRange ? 0.42 : 1.0) * length;
            }

            public string Url { get; }

            public bool SupportsRange { get; }

            public long Length { get; }

            public double Score { get; }
        }
    }

    internal readonly struct DownloadProgress
    {
        public DownloadProgress(long received, long total)
        {
            Received = received;
            Total = total;
        }

        public long Received { get; }

        public long Total { get; }

        public double Fraction => Total > 0 ? Math.Min(1.0, (double)Received / Total) : 0;
    }
}
