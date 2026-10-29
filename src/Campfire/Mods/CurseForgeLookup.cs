using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Online;

namespace Campfire.Mods
{
    /// <summary>
    /// Finds the host's jars on CurseForge through the Campfire service, by the fingerprints the host listed. The
    /// service keeps the CurseForge key; builds without the service skip this and fall back on the host.
    /// </summary>
    internal static class CurseForgeLookup
    {
        private const int BatchSize = 200;
        private static readonly string[] FileHosts = { "edge.forgecdn.net", "mediafilez.forgecdn.net" };

        /// <summary>
        /// The files CurseForge has for <paramref name="mods"/>, by SHA-1. Only files whose hash and size match the
        /// host's and that CurseForge lets others download are returned; when the service cannot be reached the
        /// result is empty.
        /// </summary>
        public static async Task<Dictionary<string, OfficialFile>> FindAsync(IEnumerable<ModInfo> mods, CancellationToken cancellation)
        {
            var result = new Dictionary<string, OfficialFile>(StringComparer.Ordinal);
            if (!OnlineService.IsAvailable)
                return result;

            var byPrint = mods.Where(m => m.Fingerprint != null)
                .GroupBy(m => m.Fingerprint.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var batch in byPrint.Keys.Select((p, i) => (p, i)).GroupBy(x => x.i / BatchSize, x => x.p))
            {
                string body = Json.Serialize(new Dictionary<string, object>
                {
                    ["fingerprints"] = batch.Select(p => (object)(double)p).ToArray(),
                });
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    using (var response = await OnlineService.Client.PostAsync(OnlineService.BaseUrl + "/v1/mods/fingerprints",
                               new StringContent(body, Encoding.UTF8, "application/json"), OnlineService.WithTimeout(timeout, 20)).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            LinkLog.Warn("The service answered the CurseForge mod lookup with " + (int)response.StatusCode + ".");
                            continue;
                        }
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (Json.TryParse(text) is Dictionary<string, object> json && json.TryGetValue("files", out object files) && files is object[] list)
                            Collect(list, byPrint, result);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
                {
                    LinkLog.Warn("The CurseForge mod lookup failed: " + ex.Message);
                }
            }
            LinkLog.Info($"CurseForge has {result.Count} of {byPrint.Count} looked-up mod file(s).");
            return result;
        }

        private static void Collect(object[] files, Dictionary<uint, List<ModInfo>> byPrint, Dictionary<string, OfficialFile> result)
        {
            foreach (var file in files.OfType<Dictionary<string, object>>())
            {
                if (!(file.TryGetValue("fingerprint", out object p) && p is double print && print >= 0 && print <= uint.MaxValue)
                    || !byPrint.TryGetValue((uint)print, out var mods))
                    continue;
                string sha1 = file.TryGetValue("sha1", out object s) ? (s as string)?.ToLowerInvariant() : null;
                string url = file.TryGetValue("url", out object u) ? u as string : null;
                if (!IsAllowed(url))
                    continue;
                // A fingerprint ignores whitespace bytes, so the hash decides whether it is really the host's file.
                foreach (var mod in mods.Where(m => m.Sha1 == sha1))
                    result[mod.Sha1] = new OfficialFile(url);
            }
        }

        private static bool IsAllowed(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                   && FileHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        }
    }
}
