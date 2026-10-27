using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Provides the official mapping file of an obfuscated release, which the in-game agent needs to find
    /// Minecraft's classes up to 1.21.11. Releases since 26.1 are not obfuscated and need none.
    /// </summary>
    /// <remarks>
    /// The game's own version file names the mapping file and its SHA-1. It is downloaded once from Mojang,
    /// or from a mirror when Mojang cannot be reached, checked against that SHA-1 and kept in the data folder.
    /// A few older releases have no mappings at all; for those Campfire carries its own name table, in the
    /// same format and keyed by today's official names.
    /// </remarks>
    internal static class GameMappings
    {
        private static readonly Lazy<HttpClient> Client =
            new Lazy<HttpClient>(() => new HttpClient { Timeout = TimeSpan.FromSeconds(90) });
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private const string MojangHost = "https://piston-data.mojang.com/";
        private const string MirrorHost = "https://bmclapi2.bangbang93.com/";

        private static string Directory => Path.Combine(AppSettings.DataDirectory, "mappings");

        private const string EmbeddedPrefix = "embedded:";

        private const string LegacyPrefix = "Campfire.Mappings.";

        /// <summary>
        /// Releases Mojang published no mappings for, by the SHA-1 of their client jar, and the name table Campfire
        /// carries for each (1.7.2 to 1.14.3). The tables were made by reading the jars themselves; each one names
        /// its jar in a "# client &lt;SHA-1&gt;" line.
        /// </summary>
        private static readonly Lazy<Dictionary<string, string>> Legacy = new Lazy<Dictionary<string, string>>(() =>
        {
            var tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var assembly = typeof(GameMappings).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith(LegacyPrefix, StringComparison.Ordinal)))
            {
                using (var stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    for (string line = reader.ReadLine(); line != null && line.StartsWith("#", StringComparison.Ordinal); line = reader.ReadLine())
                    {
                        if (line.StartsWith("# client ", StringComparison.Ordinal))
                            tables[line.Substring("# client ".Length).Trim()] = resource;
                    }
                }
            }
            return tables;
        });

        /// <summary>Releases before 1.14.4 that the agent still knows, through the tables above.</summary>
        internal static IReadOnlyCollection<string> LegacyVersions => Legacy.Value.Values
            .Select(r => r.Substring(LegacyPrefix.Length, r.Length - LegacyPrefix.Length - ".txt".Length))
            .ToList();

        /// <summary>
        /// Returns the mapping file for the game running as <paramref name="processId"/>, downloading it when
        /// needed, or <c>null</c> when the release has none: it is not obfuscated, or older than 1.14.4.
        /// </summary>
        public static async Task<string> PrepareAsync(int processId, CancellationToken cancellation)
        {
            string commandLine = await Task.Run(() => GameAccount.ReadCommandLine(processId), cancellation).ConfigureAwait(false);
            var source = commandLine == null ? null : await Task.Run(() => FindSource(commandLine), cancellation).ConfigureAwait(false);
            if (source == null)
                return null;

            if (source.Value.Url.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
                return await Task.Run(() => ExtractEmbedded(source.Value.Url.Substring(EmbeddedPrefix.Length)), cancellation).ConfigureAwait(false);

            string path = Path.Combine(Directory, source.Value.Sha1 + ".txt");
            await Gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (File.Exists(path))
                    return path;

                System.IO.Directory.CreateDirectory(Directory);
                Exception last = null;
                foreach (string url in Urls(source.Value.Url))
                {
                    try
                    {
                        byte[] data = await DownloadAsync(url, cancellation).ConfigureAwait(false);
                        if (!string.Equals(Sha1(data), source.Value.Sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            LinkLog.Warn("Mapping file from " + url + " failed its checksum.");
                            continue;
                        }
                        string temporary = path + ".tmp";
                        File.WriteAllBytes(temporary, data);
                        if (File.Exists(path))
                            File.Delete(temporary);
                        else
                            File.Move(temporary, path);
                        LinkLog.Info("Mapping file downloaded from " + url + ".");
                        return path;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested
                                               || ex is IOException)
                    {
                        last = ex;
                    }
                }
                LinkLog.Warn("The mapping file could not be downloaded: " + last?.Message);
                return null;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Finds the version file of the running game and, following "inheritsFrom" to the release it is
        /// based on, the URL and SHA-1 of its client mappings.
        /// </summary>
        internal static (string Url, string Sha1)? FindSource(string commandLine)
        {
            var arguments = GameAccount.Split(commandLine);
            string versionFile = FindVersionFile(arguments);
            if (versionFile == null)
                return null;

            string versions = Path.GetDirectoryName(Path.GetDirectoryName(versionFile));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (string file = versionFile; file != null && seen.Add(file);)
            {
                if (!(ReadJson(file) is Dictionary<string, object> json))
                    return null;
                if (Get(json, "downloads", "client_mappings") is Dictionary<string, object> mappings
                    && mappings.TryGetValue("url", out object url) && url is string link
                    && mappings.TryGetValue("sha1", out object sha) && sha is string sha1)
                    return (link, sha1);
                if (Get(json, "downloads", "client", "sha1") is string client && Legacy.Value.TryGetValue(client, out string resource))
                    return (EmbeddedPrefix + resource, client);

                file = json.TryGetValue("inheritsFrom", out object parent) && parent is string name && name.Length > 0
                    ? Path.Combine(versions, name, name + ".json")
                    : null;
            }
            return null;
        }

        /// <summary>
        /// The version file next to the game jar on the class path, or, when the jar is not there as with newer
        /// Forge, the one named by --version in the folder the libraries come from.
        /// </summary>
        private static string FindVersionFile(IReadOnlyList<string> arguments)
        {
            string classPath = ValueAfter(arguments, "-cp") ?? ValueAfter(arguments, "-classpath") ?? ValueAfter(arguments, "--class-path");
            var entries = (classPath ?? "").Split(';').Where(e => e.Length > 0).ToList();
            foreach (string entry in entries)
            {
                string directory = Path.GetDirectoryName(entry);
                string name = Path.GetFileNameWithoutExtension(entry);
                if (directory == null || !string.Equals(Path.GetFileName(directory), name, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetFileName(Path.GetDirectoryName(directory)), "versions", StringComparison.OrdinalIgnoreCase))
                    continue;
                string json = Path.Combine(directory, name + ".json");
                if (File.Exists(json))
                    return json;
            }

            string version = ValueAfter(arguments, "--version");
            string library = entries.Concat(arguments).FirstOrDefault(e => e.IndexOf("\\libraries\\", StringComparison.OrdinalIgnoreCase) > 0);
            if (version == null || library == null)
                return null;
            string root = library.Substring(0, library.IndexOf("\\libraries\\", StringComparison.OrdinalIgnoreCase));
            int start = root.LastIndexOfAny(new[] { ' ', '=', '"' });
            root = root.Substring(start + 1);
            string candidate = Path.Combine(root, "versions", version, version + ".json");
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>Writes a name table Campfire carries to the data folder, where the agent can read it.</summary>
        private static string ExtractEmbedded(string resource)
        {
            using (var stream = typeof(GameMappings).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                    return null;
                byte[] data;
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    data = memory.ToArray();
                }

                // Named by content, so a newer table from a newer Campfire never meets an old copy.
                string path = Path.Combine(Directory, Path.GetFileNameWithoutExtension(resource) + "-" + Sha1(data).Substring(0, 12) + ".txt");
                if (!File.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    string temporary = path + ".tmp";
                    File.WriteAllBytes(temporary, data);
                    if (File.Exists(path))
                        File.Delete(temporary);
                    else
                        File.Move(temporary, path);
                }
                return path;
            }
        }

        private static IEnumerable<string> Urls(string url)
        {
            yield return url;
            if (url.StartsWith(MojangHost, StringComparison.OrdinalIgnoreCase))
                yield return MirrorHost + url.Substring(MojangHost.Length);
        }

        private static async Task<byte[]> DownloadAsync(string url, CancellationToken cancellation)
        {
            using (var response = await Client.Value.GetAsync(url, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private static string Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        private static object ReadJson(string path)
        {
            try
            {
                return Json.TryParse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static object Get(object value, params string[] path)
        {
            foreach (string key in path)
            {
                if (!(value is Dictionary<string, object> map) || !map.TryGetValue(key, out value))
                    return null;
            }
            return value;
        }

        private static string ValueAfter(IReadOnlyList<string> arguments, string option)
        {
            for (int i = 0; i + 1 < arguments.Count; i++)
            {
                if (arguments[i] == option)
                    return arguments[i + 1];
            }
            return null;
        }
    }
}
