using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Link.Runtime;
using Campfire.Online;

namespace Campfire.Worlds
{
    /// <summary>
    /// Downloads map files from CurseForge into the data folder, where the import page reads them.
    /// </summary>
    /// <remarks>
    /// A file is checked against the SHA-1 the service lists for it. CurseForge serves the same files from two
    /// hosts; a failed download is tried once more on the other. Downloads are kept for a week.
    /// </remarks>
    internal static class MapDownloader
    {
        private const long MaxSize = 4L * 1024 * 1024 * 1024;
        private static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);
        private static readonly string[] Hosts = { "edge.forgecdn.net", "mediafilez.forgecdn.net" };

        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() =>
        {
            // The whole download may take long; ProgressStream ends it when the server stops sending instead.
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Campfire/" + OnlineService.CurrentVersion.ToString(3));
            return client;
        });

        private static string Root => Path.Combine(AppSettings.DataDirectory, "downloads", "maps");

        /// <summary>
        /// Downloads <paramref name="file"/> and returns where it was saved.
        /// </summary>
        /// <param name="progress">Receives the share downloaded, from 0 to 1.</param>
        /// <exception cref="IOException">No host delivered the file intact.</exception>
        public static async Task<string> DownloadAsync(MapFile file, IProgress<double> progress, CancellationToken cancellation)
        {
            if (file.Url == null || !MapImages.IsAllowed(file.Url))
                throw new InvalidOperationException("The file has no download address.");
            if (file.Size > MaxSize)
                throw new IOException("The file is larger than 4 GB.");

            string folder = Path.Combine(Root, file.Id.ToString(CultureInfo.InvariantCulture));
            string path = Path.Combine(folder, SafeName(file));
            if (await Task.Run(() => IsComplete(path, file), cancellation).ConfigureAwait(false))
            {
                Touch(folder);
                return path;
            }

            await Task.Run(() => Prune(folder), cancellation).ConfigureAwait(false);
            Directory.CreateDirectory(folder);
            string temporary = path + ".tmp";
            Exception last = null;
            foreach (string url in Urls(file.Url))
            {
                try
                {
                    LinkLog.Info("Downloading map file from " + url + ".");
                    await DownloadToAsync(url, temporary, file, progress, cancellation).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(file.Sha1) && !string.Equals(Sha1(temporary), file.Sha1, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded file failed its checksum.");
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(temporary, path);
                    return path;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is TimeoutException
                                           || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
                {
                    LinkLog.Warn("Map download from " + url + " failed: " + ex.Message);
                    last = ex;
                }
                finally
                {
                    TryDelete(temporary);
                    if (cancellation.IsCancellationRequested)
                        RemoveIfEmpty(folder);
                }
            }
            RemoveIfEmpty(folder);
            throw new IOException(last?.Message ?? "The file could not be downloaded.", last);
        }

        private static async Task DownloadToAsync(string url, string target, MapFile file, IProgress<double> progress, CancellationToken cancellation)
        {
            using (var response = await Client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? file.Size;
                if (total > MaxSize || file.Size > 0 && total != file.Size)
                    throw new InvalidDataException("The server offered a file of the wrong size.");

                long received = 0;
                progress?.Report(0);
                using (var network = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var counted = new ProgressStream(network, response, bytes =>
                    {
                        received += bytes;
                        if (received > MaxSize || total > 0 && received > total)
                            throw new InvalidDataException("The server sent more than the file holds.");
                        if (total > 0)
                            progress?.Report((double)received / total);
                    });
                    await counted.CopyToAsync(output, 81920, cancellation).ConfigureAwait(false);
                }
                if (total > 0 && received != total)
                    throw new IOException("The download ended early.");
            }
        }

        /// <summary>The address as listed, then the same file on CurseForge's other host.</summary>
        private static IEnumerable<string> Urls(string url)
        {
            yield return url;
            var uri = new Uri(url);
            if (!Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                yield break;
            foreach (string host in Hosts.Where(h => !h.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)))
                yield return new UriBuilder(uri) { Host = host }.Uri.AbsoluteUri;
        }

        private static bool IsComplete(string path, MapFile file)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || file.Size > 0 && info.Length != file.Size)
                    return false;
                return string.IsNullOrEmpty(file.Sha1) || string.Equals(Sha1(path), file.Sha1, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Removes downloads not used for a week, except <paramref name="keep"/>.</summary>
        private static void Prune(string keep)
        {
            if (!Directory.Exists(Root))
                return;
            foreach (var folder in new DirectoryInfo(Root).EnumerateDirectories())
            {
                if (!string.Equals(folder.FullName, keep, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - folder.LastWriteTimeUtc > KeepFor)
                {
                    try
                    {
                        folder.Delete(true);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // Still in use; it goes next time.
                    }
                }
            }
        }

        private static void Touch(string folder)
        {
            try
            {
                Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>The file name the author gave, made safe; it becomes the world name when the map has no folder.</summary>
        private static string SafeName(MapFile file)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string name = new string((file.FileName ?? "").Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (name.Length == 0)
                name = file.Id.ToString(CultureInfo.InvariantCulture) + ".zip";
            if (name.Length > 100)
                name = name.Substring(0, 100 - Path.GetExtension(name).Length) + Path.GetExtension(name);
            return name;
        }

        private static string Sha1(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void RemoveIfEmpty(string folder)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
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
