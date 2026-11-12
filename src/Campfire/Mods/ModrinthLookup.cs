using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Link.Runtime;
using Campfire.Online;

namespace Campfire.Mods
{
    /// <summary>A jar on Modrinth or CurseForge that is exactly the host's, and the address it is served from.</summary>
    internal sealed class OfficialFile
    {
        public OfficialFile(string url)
        {
            Url = url;
        }

        public string Url { get; }
    }

    /// <summary>
    /// Finds the host's jars on Modrinth by their SHA-1. Only the exact file counts.
    /// </summary>
    internal static class ModrinthLookup
    {
        private const string Api = "https://api.modrinth.com/v2/version_files";
        private const string FileHost = "cdn.modrinth.com";
        private const int BatchSize = 100;

        internal static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() =>
        {
            // No overall timeout; ProgressStream ends a download when the server stops sending.
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Campfire/" + OnlineService.CurrentVersion.ToString(3));
            return client;
        });

        /// <summary>
        /// The files Modrinth has for <paramref name="hashes"/>, by hash. Hashes it does not know are left out;
        /// when Modrinth cannot be reached the result is empty.
        /// </summary>
        public static async Task<Dictionary<string, OfficialFile>> FindAsync(IEnumerable<string> hashes, CancellationToken cancellation)
        {
            var result = new Dictionary<string, OfficialFile>(StringComparer.Ordinal);
            foreach (var batch in hashes.Distinct().Select((h, i) => (h, i)).GroupBy(x => x.i / BatchSize, x => x.h))
            {
                string body = Json.Serialize(new Dictionary<string, object>
                {
                    ["hashes"] = batch.Cast<object>().ToArray(),
                    ["algorithm"] = "sha1",
                });
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        using (var response = await Client.Value.PostAsync(Api, new StringContent(body, Encoding.UTF8, "application/json"), timeout.Token)
                                   .ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                LinkLog.Warn("Modrinth answered the mod lookup with " + (int)response.StatusCode + ".");
                                continue;
                            }
                            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (Json.TryParse(text) is Dictionary<string, object> versions)
                                Collect(versions, result);
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested
                                           || ex is IOException)
                {
                    LinkLog.Warn("The mod lookup on Modrinth failed: " + ex.Message);
                }
            }
            LinkLog.Info($"Modrinth has {result.Count} of the needed mod file(s).");
            return result;
        }

        /// <summary>
        /// The newest file of Modrinth project <paramref name="projectId"/> for <paramref name="gameVersion"/> and one of
        /// <paramref name="loaders"/>, releases first; <c>null</c> when there is none or Modrinth cannot be reached.
        /// </summary>
        public static async Task<(OfficialFile File, ModInfo Mod)?> FindNewestAsync(string projectId, string gameVersion,
            IReadOnlyList<string> loaders, IReadOnlyList<string> ids, CancellationToken cancellation)
        {
            string query = "?loaders=" + Uri.EscapeDataString(Json.Serialize(loaders.Cast<object>().ToArray()))
                           + "&game_versions=" + Uri.EscapeDataString(Json.Serialize(new object[] { gameVersion }));
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    using (var response = await Client.Value.GetAsync("https://api.modrinth.com/v2/project/" + projectId + "/version" + query,
                               timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            LinkLog.Warn("Modrinth answered the version lookup with " + (int)response.StatusCode + ".");
                            return null;
                        }
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var versions = (Json.TryParse(text) as object[] ?? new object[0]).OfType<Dictionary<string, object>>()
                            .OrderBy(v => v.TryGetValue("version_type", out object type) && type as string == "release" ? 0 : 1)
                            .ToList();
                        foreach (var version in versions)
                        {
                            var found = PrimaryFile(version, ids);
                            if (found != null)
                                return found;
                        }
                        return null;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested
                                       || ex is IOException)
            {
                LinkLog.Warn("The version lookup on Modrinth failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>The primary file of one version, or its first one, when its address is allowed.</summary>
        private static (OfficialFile File, ModInfo Mod)? PrimaryFile(Dictionary<string, object> version, IReadOnlyList<string> ids)
        {
            if (!(version.TryGetValue("files", out object files) && files is object[] list))
                return null;
            var all = list.OfType<Dictionary<string, object>>().ToList();
            var file = all.FirstOrDefault(f => f.TryGetValue("primary", out object primary) && primary is bool p && p) ?? all.FirstOrDefault();
            if (file == null)
                return null;

            string url = file.TryGetValue("url", out object u) ? u as string : null;
            string name = file.TryGetValue("filename", out object n) ? n as string : null;
            string sha1 = file.TryGetValue("hashes", out object hashes) && hashes is Dictionary<string, object> map
                          && map.TryGetValue("sha1", out object value) ? (value as string)?.ToLowerInvariant() : null;
            if (!IsAllowed(url) || !ModInfo.IsSafeFileName(name) || sha1 == null || sha1.Length != 40
                || !(file.TryGetValue("size", out object rawSize) && rawSize is double size) || size <= 0)
                return null;

            string number = version.TryGetValue("version_number", out object v) ? v as string : null;
            var mod = new ModInfo(name, sha1, (long)size, ids, null, number, ModSide.Both);
            return (new OfficialFile(url), mod);
        }

        /// <summary>Each version lists its files; the one whose hash was asked for is taken, if its address is allowed.</summary>
        private static void Collect(Dictionary<string, object> versions, Dictionary<string, OfficialFile> result)
        {
            foreach (var pair in versions)
            {
                if (!(pair.Value is Dictionary<string, object> version) || !(version.TryGetValue("files", out object files) && files is object[] list))
                    continue;
                foreach (var file in list.OfType<Dictionary<string, object>>())
                {
                    string sha1 = file.TryGetValue("hashes", out object hashes) && hashes is Dictionary<string, object> map
                                  && map.TryGetValue("sha1", out object value) ? value as string : null;
                    string url = file.TryGetValue("url", out object u) ? u as string : null;
                    if (sha1 != null && string.Equals(sha1, pair.Key, StringComparison.OrdinalIgnoreCase) && IsAllowed(url))
                        result[pair.Key] = new OfficialFile(url);
                }
            }
        }

        internal static bool IsAllowed(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                   && string.Equals(uri.Host, FileHost, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Downloads <paramref name="file"/>, from Modrinth or CurseForge, to <paramref name="path"/> and checks it against the host's hash.</summary>
        public static async Task DownloadAsync(OfficialFile file, ModInfo mod, string path, IProgress<long> progress, CancellationToken cancellation)
        {
            LinkLog.Info("Downloading " + mod.FileName + " from " + new Uri(file.Url).Host + ".");
            string temporary = path + ".tmp";
            try
            {
                using (var response = await Client.Value.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new ModSyncException("Download");
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var watched = new ProgressStream(source, response, null))
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await watched.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + read > mod.Size)
                                throw new ModSyncException("Checksum");
                            await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                            progress.Report(output.Length);
                        }
                    }
                }
                ModSync.Verify(temporary, mod);
                ModSync.Replace(temporary, path);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException
                                       || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
            {
                LinkLog.Warn("Downloading " + mod.FileName + " from " + new Uri(file.Url).Host + " failed: " + ex.Message);
                throw new ModSyncException("Download", ex);
            }
            finally
            {
                ModSync.TryDelete(temporary);
            }
        }
    }
}
