using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Worlds;

namespace Campfire.Mods
{
    /// <summary>A newer file on Modrinth for one jar in a mods folder.</summary>
    internal sealed class ModUpdate
    {
        public ModUpdate(string path, string title, string currentVersion, string newVersion, string url, string fileName, string sha1, long size)
        {
            Path = path;
            Title = title;
            CurrentVersion = currentVersion;
            NewVersion = newVersion;
            Url = url;
            FileName = fileName;
            Sha1 = sha1;
            Size = size;
        }

        /// <summary>The jar in the mods folder.</summary>
        public string Path { get; }

        public string Title { get; }

        public string CurrentVersion { get; }

        public string NewVersion { get; }

        public string Url { get; }

        public string FileName { get; }

        public string Sha1 { get; }

        public long Size { get; }
    }

    /// <summary>
    /// Looks up newer versions of the jars in a mods folder on Modrinth, by their SHA-1, for the version's game
    /// version and loader. Only files Modrinth knows are covered; a file is offered when Modrinth has a version of
    /// the same project published later for the same game version and loader.
    /// </summary>
    internal static class ModUpdates
    {
        private const string Api = "https://api.modrinth.com/v2/version_files";
        private const int BatchSize = 100;

        /// <summary>The loader names Modrinth files versions under for <paramref name="loader"/>; empty for none.</summary>
        private static string[] Loaders(InstalledVersion version)
        {
            switch (version.Loader)
            {
                case VersionLoader.Fabric:
                    return new[] { "fabric" };
                case VersionLoader.Quilt:
                    return new[] { "quilt", "fabric" };
                case VersionLoader.Forge:
                    return new[] { "forge" };
                case VersionLoader.NeoForge:
                    return version.GameVersion == "1.20.1" ? new[] { "neoforge", "forge" } : new[] { "neoforge" };
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>
        /// The updates for the jars at <paramref name="paths"/>. Throws <see cref="ModSyncException"/> with "Lookup"
        /// when Modrinth cannot be reached.
        /// </summary>
        public static async Task<IReadOnlyList<ModUpdate>> FindAsync(InstalledVersion version, IReadOnlyList<string> paths,
            IReadOnlyDictionary<string, string> titles, CancellationToken cancellation)
        {
            var loaders = Loaders(version);
            if (loaders.Length == 0 || string.IsNullOrEmpty(version.GameVersion) || paths.Count == 0)
                return Array.Empty<ModUpdate>();

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            await Task.Run(() =>
            {
                foreach (string path in paths)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        hashes[ModReader.Sha1(path)] = path;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
            }, cancellation).ConfigureAwait(false);

            var updates = new List<ModUpdate>();
            foreach (var batch in hashes.Keys.Select((h, i) => (h, i)).GroupBy(x => x.i / BatchSize, x => x.h))
            {
                var list = batch.ToList();
                var current = await PostAsync(Api, new Dictionary<string, object>
                {
                    ["hashes"] = list.Cast<object>().ToArray(),
                    ["algorithm"] = "sha1",
                }, cancellation).ConfigureAwait(false);
                var latest = await PostAsync(Api + "/update", new Dictionary<string, object>
                {
                    ["hashes"] = list.Cast<object>().ToArray(),
                    ["algorithm"] = "sha1",
                    ["loaders"] = loaders.Cast<object>().ToArray(),
                    ["game_versions"] = new object[] { version.GameVersion },
                }, cancellation).ConfigureAwait(false);

                foreach (string hash in list)
                {
                    if (!(current.TryGetValue(hash, out object a) && a is Dictionary<string, object> now)
                        || !(latest.TryGetValue(hash, out object b) && b is Dictionary<string, object> next))
                        continue;
                    var update = Compare(hashes[hash], hash, now, next, titles);
                    if (update != null)
                        updates.Add(update);
                }
            }
            LinkLog.Info($"Modrinth lists {updates.Count} update(s) for {hashes.Count} mod file(s) of {version.Name}.");
            return updates;
        }

        /// <summary>An update when <paramref name="next"/> is another version of the same project, published later, with an allowed file.</summary>
        private static ModUpdate Compare(string path, string hash, Dictionary<string, object> now, Dictionary<string, object> next,
            IReadOnlyDictionary<string, string> titles)
        {
            if (Text(now, "id") == null || Text(now, "id") == Text(next, "id") || Text(now, "project_id") != Text(next, "project_id"))
                return null;
            if (!DateTime.TryParse(Text(now, "date_published"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nowDate)
                || !DateTime.TryParse(Text(next, "date_published"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nextDate)
                || nextDate <= nowDate)
                return null;
            if (!(next.TryGetValue("files", out object files) && files is object[] fileList))
                return null;

            var all = fileList.OfType<Dictionary<string, object>>().ToList();
            var file = all.FirstOrDefault(f => f.TryGetValue("primary", out object primary) && primary is bool p && p) ?? all.FirstOrDefault();
            if (file == null)
                return null;
            string url = Text(file, "url");
            string name = Text(file, "filename");
            string sha1 = file.TryGetValue("hashes", out object h) && h is Dictionary<string, object> map ? (Text(map, "sha1") ?? "").ToLowerInvariant() : "";
            if (!ModrinthLookup.IsAllowed(url) || !ModInfo.IsSafeFileName(name) || sha1.Length != 40 || sha1 == hash
                || !(file.TryGetValue("size", out object rawSize) && rawSize is double size) || size <= 0)
                return null;

            string title = titles != null && titles.TryGetValue(path, out string known) ? known : System.IO.Path.GetFileNameWithoutExtension(path);
            return new ModUpdate(path, title, Text(now, "version_number"), Text(next, "version_number"), url, name, sha1, (long)size);
        }

        /// <summary>
        /// Downloads <paramref name="update"/> into the mods folder and moves the old jar to Campfire's backups.
        /// Returns the backup folder.
        /// </summary>
        public static async Task<string> ApplyAsync(ModUpdate update, string backupDirectory, CancellationToken cancellation)
        {
            string folder = System.IO.Path.GetDirectoryName(update.Path);
            string target = System.IO.Path.Combine(folder, update.FileName);
            bool sameName = string.Equals(target, update.Path, StringComparison.OrdinalIgnoreCase);
            if (!sameName && File.Exists(target))
                throw new ModSyncException("UpdateExists");

            Directory.CreateDirectory(backupDirectory);
            string backup = System.IO.Path.Combine(backupDirectory, System.IO.Path.GetFileName(update.Path));
            File.Copy(update.Path, backup, true);

            var mod = new ModInfo(update.FileName, update.Sha1, update.Size, Array.Empty<string>(), null, update.NewVersion, ModSide.Both);
            await ModrinthLookup.DownloadAsync(new OfficialFile(update.Url), mod, target, new Progress<long>(), cancellation).ConfigureAwait(false);
            if (!sameName)
                File.Delete(update.Path);
            LinkLog.Info($"Updated {System.IO.Path.GetFileName(update.Path)} to {update.FileName}.");
            return backupDirectory;
        }

        /// <summary>A new folder under Campfire's backups for the jars replaced now.</summary>
        public static string NewBackupDirectory(InstalledVersion version)
        {
            string safe = string.Concat(version.Name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return System.IO.Path.Combine(AppSettings.DataDirectory, "backups", "mods", safe, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        }

        private static async Task<Dictionary<string, object>> PostAsync(string url, Dictionary<string, object> body, CancellationToken cancellation)
        {
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    using (var response = await ModrinthLookup.Client.Value
                               .PostAsync(url, new StringContent(Link.Json.Serialize(body), Encoding.UTF8, "application/json"), timeout.Token)
                               .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            LinkLog.Warn("Modrinth answered the update lookup with " + (int)response.StatusCode + ".");
                            throw new ModSyncException("Lookup");
                        }
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return Link.Json.TryParse(text) as Dictionary<string, object> ?? new Dictionary<string, object>();
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException
                                       || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
            {
                LinkLog.Warn("The update lookup on Modrinth failed: " + ex.Message);
                throw new ModSyncException("Lookup", ex);
            }
        }

        private static string Text(Dictionary<string, object> json, string key)
        {
            return json.TryGetValue(key, out object value) ? value as string : null;
        }
    }
}
