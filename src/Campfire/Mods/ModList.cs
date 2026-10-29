using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Campfire.Link;
using Campfire.Worlds;

namespace Campfire.Mods
{
    /// <summary>
    /// The game version, loader and mods of one game, as the host shares it with the room.
    /// </summary>
    internal sealed class ModList
    {
        private const int MaxMods = 1000;

        /// <summary>Jars already hashed, by path, size and time written; a folder of a few hundred mods is read once.</summary>
        private static readonly Dictionary<string, (long Size, DateTime Written, ModInfo Mod)> Cache =
            new Dictionary<string, (long, DateTime, ModInfo)>(StringComparer.OrdinalIgnoreCase);

        public ModList(string gameVersion, VersionLoader? loader, string loaderVersion, IReadOnlyList<ModInfo> mods)
        {
            GameVersion = gameVersion;
            Loader = loader;
            LoaderVersion = loaderVersion;
            Mods = mods ?? Array.Empty<ModInfo>();
        }

        /// <summary>The game version such as "1.20.1", or <c>null</c> when the host's game did not tell.</summary>
        public string GameVersion { get; }

        /// <summary>The mod loader, or <c>null</c> when the host's game did not tell.</summary>
        public VersionLoader? Loader { get; }

        public string LoaderVersion { get; }

        public IReadOnlyList<ModInfo> Mods { get; }

        /// <summary>Tells two lists apart: equal when the game, loader, file offer and jars are all the same.</summary>
        public string Signature => string.Join("|", GameVersion, Loader, LoaderVersion, FilesOffered,
            string.Join(",", Mods.Select(m => m.Sha1).OrderBy(s => s, StringComparer.Ordinal)));

        /// <summary>Whether the host lets players download these jars from its computer.</summary>
        public bool FilesOffered { get; private set; }

        /// <summary>Reads every enabled jar in <paramref name="directory"/>; a missing folder means no mods.</summary>
        public static IReadOnlyList<ModInfo> ReadFolder(string directory)
        {
            var result = new List<ModInfo>();
            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(directory).GetFiles("*.jar", SearchOption.TopDirectoryOnly)
                    .Where(f => f.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxMods)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return result;
            }

            foreach (var file in files)
            {
                try
                {
                    result.Add(ReadCached(file));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    LinkLog.Warn("A mod could not be read: " + file.Name + ": " + ex.Message);
                }
            }
            return result;
        }

        private static ModInfo ReadCached(FileInfo file)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(file.FullName, out var cached) && cached.Size == file.Length && cached.Written == file.LastWriteTimeUtc)
                    return cached.Mod;
            }
            var mod = ModReader.Read(file.FullName);
            lock (Cache)
                Cache[file.FullName] = (file.Length, file.LastWriteTimeUtc, mod);
            return mod;
        }

        public Dictionary<string, object> ToJson()
        {
            var json = new Dictionary<string, object>
            {
                                ["mods"] = Mods.Select(m => (object)m.ToJson()).ToArray(),
            };
            if (GameVersion != null)
                json["game"] = GameVersion;
            if (Loader != null)
                json["loader"] = Loader.Value.ToString().ToLowerInvariant();
            if (LoaderVersion != null)
                json["loaderVersion"] = LoaderVersion;
            return json;
        }

        public static ModList FromJson(object value)
        {
            if (!(value is Dictionary<string, object> json))
                return null;
            string game = json.TryGetValue("game", out object g) ? Short(g as string) : null;
            VersionLoader? loader = json.TryGetValue("loader", out object l) && Enum.TryParse(l as string, true, out VersionLoader parsed)
                                   && Enum.IsDefined(typeof(VersionLoader), parsed)
                ? parsed
                : (VersionLoader?)null;
            string loaderVersion = json.TryGetValue("loaderVersion", out object v) ? Short(v as string) : null;
            var mods = json.TryGetValue("mods", out object m) && m is object[] list
                ? list.Take(MaxMods).Select(ModInfo.FromJson).Where(x => x != null).ToList()
                : new List<ModInfo>();
            return new ModList(game, loader, loaderVersion, mods) { FilesOffered = json.TryGetValue("files", out object f) && f is bool files && files };
        }

        private static string Short(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Length > 64 ? text.Substring(0, 64) : text;
        }
    }
}
