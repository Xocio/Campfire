using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Online
{
    /// <summary>
    /// Downloads, verifies and installs a new Campfire.exe.
    /// </summary>
    /// <remarks>
    /// A file is only installed when its ECDSA P-256 signature matches <see cref="PublicKey"/>, so neither the
    /// service nor a download mirror can hand out a modified program. Windows lets a running program be renamed,
    /// so installing renames the running file to ".old", puts the new one in its place and starts it.
    /// </remarks>
    internal static class Updater
    {
        /// <summary>Public half of the release signing key, X and Y in base64. See tools/new-signing-key.ps1.</summary>
        private const string PublicKey = "MKs7EnkhMauplagoZmgoOqkNRbQiwlTDVIT1zHitN7KsJL7uG8/WPZAG+Lisj/k0wCbHGcRpxMlPqr1y/DkYlw==";

        private const long MaxSize = 64L * 1024 * 1024;

        private static readonly SemaphoreSlim DownloadLock = new SemaphoreSlim(1, 1);

        public static string ExecutablePath { get; } = Process.GetCurrentProcess().MainModule.FileName;

        private static string OldPath => ExecutablePath + ".old";

        private static string StagingDirectory => Path.Combine(AppSettings.DataDirectory, "update");

        public static bool IsNewer(UpdateInfo update)
        {
            return update != null && update.Version > OnlineService.ShortVersion;
        }

        public static bool IsRequired(Manifest manifest)
        {
            return manifest?.MinVersion != null && OnlineService.ShortVersion < manifest.MinVersion && IsNewer(manifest.Update);
        }

        /// <summary>Whether Campfire may replace its own file; otherwise updates are downloaded by hand.</summary>
        public static bool CanInstall()
        {
            try
            {
                string probe = Path.Combine(Path.GetDirectoryName(ExecutablePath), ".campfire-write-test");
                File.WriteAllBytes(probe, new byte[0]);
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Removes what the previous update left behind.</summary>
        public static void CleanUp()
        {
            TryDelete(OldPath);
            try
            {
                if (!Directory.Exists(StagingDirectory))
                    return;
                foreach (string file in Directory.GetFiles(StagingDirectory))
                {
                    var staged = OnlineService.ParseVersion(Path.GetFileNameWithoutExtension(file).Replace("Campfire-", ""));
                    if (staged == null || staged <= OnlineService.ShortVersion)
                        TryDelete(file);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Downloads and verifies the update, or reuses an earlier download. Returns the verified file, or <c>null</c>
        /// when no source delivered a correctly signed file.
        /// </summary>
        public static async Task<string> DownloadAsync(UpdateInfo update, CancellationToken cancellation = default)
        {
            string target = Path.Combine(StagingDirectory, $"Campfire-{update.Version.ToString(3)}.exe");
            await DownloadLock.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (File.Exists(target) && File.Exists(target + ".sig")
                    && Verify(File.ReadAllBytes(target), File.ReadAllText(target + ".sig"), update.Version))
                    return target;

                for (int i = 0; i < update.Downloads.Length; i++)
                {
                    try
                    {
                        string signature = await ReadTextAsync(update.Signatures[i], cancellation).ConfigureAwait(false);
                        byte[] data = await ReadBytesAsync(update.Downloads[i], cancellation).ConfigureAwait(false);
                        if (!Verify(data, signature, update.Version))
                            continue;
                        Directory.CreateDirectory(StagingDirectory);
                        File.WriteAllBytes(target, data);
                        File.WriteAllText(target + ".sig", signature);
                        return target;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested
                        || ex is IOException || ex is InvalidDataException)
                    {
                        // Try the next source.
                    }
                }
                return null;
            }
            finally
            {
                DownloadLock.Release();
            }
        }

        private static async Task<string> ReadTextAsync(string url, CancellationToken cancellation)
        {
            using (var response = await OnlineService.Client.GetAsync(url, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> ReadBytesAsync(string url, CancellationToken cancellation)
        {
            using (var response = await OnlineService.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxSize)
                    throw new InvalidDataException("The download is too large.");
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks the signature, and that the file really is the promised version, so an older signed release cannot
        /// be passed off as a newer one.
        /// </summary>
        internal static bool Verify(byte[] data, string signature, Version expected)
        {
            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature.Trim());
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] key = Convert.FromBase64String(PublicKey);
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = key.Take(32).ToArray(), Y = key.Skip(32).ToArray() },
            };
            using (var ecdsa = ECDsa.Create(parameters))
            {
                if (signatureBytes.Length != 64 || !ecdsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256))
                    return false;
            }
            return VersionOf(data) == expected;
        }

        /// <summary>The three-part assembly version of a program file, read without running or loading it.</summary>
        private static Version VersionOf(byte[] data)
        {
            string temp = Path.Combine(Path.GetTempPath(), "campfire-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.WriteAllBytes(temp, data);
                var version = AssemblyName.GetAssemblyName(temp).Version;
                return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
            }
            catch (Exception ex) when (ex is BadImageFormatException || ex is IOException || ex is FileLoadException)
            {
                return null;
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>
        /// Puts the verified file in place of the running program. Returns the path to start, or throws when the
        /// files could not be swapped; the running program is then left untouched.
        /// </summary>
        public static string Install(string staged)
        {
            TryDelete(OldPath);
            File.Move(ExecutablePath, OldPath);
            try
            {
                File.Copy(staged, ExecutablePath);
            }
            catch
            {
                File.Move(OldPath, ExecutablePath);
                throw;
            }
            TryDelete(staged);
            TryDelete(staged + ".sig");
            return ExecutablePath;
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
