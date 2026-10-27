using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Campfire.Link;

namespace Campfire.Worlds
{
    internal enum VersionLoader
    {
        Vanilla,
        Fabric,
        Quilt,
        Forge,
        NeoForge,
    }

    /// <summary>How sure Campfire is about <see cref="InstalledVersion.SavesDirectory"/>.</summary>
    internal enum SavesCertainty
    {
        /// <summary>The version has already run in its own folder, so its saves are there.</summary>
        Found,

        /// <summary>The launcher's per-version setting says so.</summary>
        LauncherSetting,

        /// <summary>Nothing says either way; the launcher's usual default was assumed. Check again once the game runs.</summary>
        Guessed,
    }

    /// <summary>
    /// One version in a .minecraft folder: the game version it runs, its mod loader and where it keeps its worlds.
    /// </summary>
    internal sealed class InstalledVersion
    {
        private const int MaxInheritance = 8;

        /// <summary>A plain release name such as "1.20.1" or "26.3".</summary>
        private static readonly Regex ReleaseName = new Regex(@"^\d+\.\d+(\.\d+)?$", RegexOptions.CultureInvariant);

        private InstalledVersion()
        {
        }

        /// <summary>The version folder name, which is what launchers show and the user may have renamed.</summary>
        public string Name { get; private set; }

        public GameFolder Folder { get; private set; }

        /// <summary>Full path of versions\&lt;name&gt;.</summary>
        public string Directory { get; private set; }

        /// <summary>The game version such as "1.20.1" or "26.3", or <c>null</c> when it cannot be told.</summary>
        public string GameVersion { get; private set; }

        /// <summary>The world data version this game writes, read from its jar; <c>null</c> before 1.14.</summary>
        public int? DataVersion { get; private set; }

        /// <summary>Whether the game version is a release rather than a snapshot or test build.</summary>
        public bool IsRelease { get; private set; }

        public VersionLoader Loader { get; private set; }

        public string LoaderVersion { get; private set; }

        /// <summary>The saves folder of this version, which depends on whether the launcher isolates it.</summary>
        public string SavesDirectory { get; private set; }

        public SavesCertainty SavesCertainty { get; private set; }

        /// <summary>Lists the versions in <paramref name="folder"/>; broken or partial ones are left out.</summary>
        public static IReadOnlyList<InstalledVersion> Scan(GameFolder folder)
        {
            var result = new List<InstalledVersion>();
            string versions = Path.Combine(folder.Path, "versions");
            IEnumerable<string> directories;
            try
            {
                directories = System.IO.Directory.GetDirectories(versions);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return result;
            }

            var cache = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in directories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var version = Read(folder, Path.GetFileName(directory), cache);
                    if (version != null)
                        result.Add(version);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
                {
                    // One unreadable version should not hide the others.
                }
            }
            return result;
        }

        private static InstalledVersion Read(GameFolder folder, string name, Dictionary<string, Dictionary<string, object>> cache)
        {
            var json = LoadJson(folder, name, cache);
            if (json == null)
                return null;

            // The version itself first, then what it inherits from; a loader version inherits from vanilla.
            var chain = new List<(string Name, Dictionary<string, object> Json)> { (name, json) };
            while (chain.Count < MaxInheritance && chain[chain.Count - 1].Json.TryGetValue("inheritsFrom", out object parent)
                   && parent is string parentName && parentName.Length > 0)
            {
                var parentJson = LoadJson(folder, parentName, cache);
                if (parentJson == null || chain.Any(c => string.Equals(c.Name, parentName, StringComparison.OrdinalIgnoreCase)))
                    break;
                chain.Add((parentName, parentJson));
            }

            var version = new InstalledVersion
            {
                Name = name,
                Folder = folder,
                Directory = Path.Combine(folder.Path, "versions", name),
            };

            var libraries = chain.SelectMany(c => Libraries(c.Json)).ToList();
            var arguments = chain.SelectMany(c => GameArguments(c.Json)).ToList();
            DetectLoader(version, libraries);

            var jarInfo = ReadJarVersion(folder, chain);
            if (jarInfo != null)
            {
                version.GameVersion = jarInfo.Value.Name;
                version.DataVersion = jarInfo.Value.WorldVersion;
                version.IsRelease = jarInfo.Value.Stable ?? ReleaseName.IsMatch(jarInfo.Value.Name ?? "");
            }

            if (version.GameVersion == null)
            {
                // Before 1.14 the jar does not say; fall back on what the version files tell.
                string root = chain[chain.Count - 1].Name;
                version.GameVersion = ValueAfter(arguments, "--fml.mcVersion")
                                      ?? MinecraftFromLoaderLibrary(libraries)
                                      ?? (ReleaseName.IsMatch(root) ? root : null)
                                      ?? ReleaseNameFromLauncher(version.Directory)
                                      ?? root;
                string type = chain.Select(c => c.Json.TryGetValue("type", out object t) ? t as string : null).LastOrDefault(t => t != null);
                version.IsRelease = type != null ? type == "release" : ReleaseName.IsMatch(version.GameVersion);
            }

            ResolveSaves(version);
            return version;
        }

        private static Dictionary<string, object> LoadJson(GameFolder folder, string name, Dictionary<string, Dictionary<string, object>> cache)
        {
            if (cache.TryGetValue(name, out var cached))
                return cached;

            string path = Path.Combine(folder.Path, "versions", name, name + ".json");
            Dictionary<string, object> json = null;
            if (File.Exists(path))
                json = Json.TryParse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            cache[name] = json;
            return json;
        }

        private static IEnumerable<string> Libraries(Dictionary<string, object> json)
        {
            if (json.TryGetValue("libraries", out object value) && value is object[] list)
            {
                foreach (var library in list.OfType<Dictionary<string, object>>())
                {
                    if (library.TryGetValue("name", out object name) && name is string text)
                        yield return text;
                }
            }
        }

        /// <summary>Plain string game arguments, from "arguments.game" or the older "minecraftArguments".</summary>
        private static IEnumerable<string> GameArguments(Dictionary<string, object> json)
        {
            if (json.TryGetValue("arguments", out object arguments) && arguments is Dictionary<string, object> map
                && map.TryGetValue("game", out object game) && game is object[] list)
            {
                foreach (string argument in list.OfType<string>())
                    yield return argument;
            }
            if (json.TryGetValue("minecraftArguments", out object legacy) && legacy is string text)
            {
                foreach (string argument in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    yield return argument;
            }
        }

        private static string ValueAfter(IReadOnlyList<string> arguments, string option)
        {
            for (int i = 0; i + 1 < arguments.Count; i++)
            {
                if (arguments[i] == option && !arguments[i + 1].StartsWith("$", StringComparison.Ordinal))
                    return arguments[i + 1];
            }
            return null;
        }

        /// <summary>Recognizes the loader from its library: "group:artifact:version".</summary>
        private static void DetectLoader(InstalledVersion version, IEnumerable<string> libraries)
        {
            foreach (string library in libraries)
            {
                string[] parts = library.Split(':');
                if (parts.Length < 3)
                    continue;
                string id = parts[0] + ":" + parts[1];
                VersionLoader loader;
                switch (id)
                {
                    case "net.fabricmc:fabric-loader": loader = VersionLoader.Fabric; break;
                    case "org.quiltmc:quilt-loader": loader = VersionLoader.Quilt; break;
                    case "net.neoforged:neoforge":
                    case "net.neoforged:forge": loader = VersionLoader.NeoForge; break;
                    case "net.minecraftforge:forge":
                    case "net.minecraftforge:fmlloader": loader = VersionLoader.Forge; break;
                    default: continue;
                }
                version.Loader = loader;
                version.LoaderVersion = StripGamePrefix(parts[2]);
                return;
            }
        }

        /// <summary>Forge versions are written "1.20.1-47.2.0"; the part after the game version is the loader's.</summary>
        private static string StripGamePrefix(string version)
        {
            int dash = version.IndexOf('-');
            return dash > 0 && ReleaseName.IsMatch(version.Substring(0, dash)) ? version.Substring(dash + 1) : version;
        }

        private static string MinecraftFromLoaderLibrary(IEnumerable<string> libraries)
        {
            foreach (string library in libraries)
            {
                string[] parts = library.Split(':');
                if (parts.Length >= 3 && (parts[0] == "net.minecraftforge" || parts[0] == "net.neoforged") && parts[1] == "forge")
                {
                    int dash = parts[2].IndexOf('-');
                    if (dash > 0 && ReleaseName.IsMatch(parts[2].Substring(0, dash)))
                        return parts[2].Substring(0, dash);
                }
            }
            return null;
        }

        /// <summary>
        /// The game jar names itself in version.json since 1.14, including the world data version. A version
        /// without its own jar uses the jar of the version it inherits from.
        /// </summary>
        private static (string Name, int? WorldVersion, bool? Stable)? ReadJarVersion(GameFolder folder, List<(string Name, Dictionary<string, object> Json)> chain)
        {
            foreach (var (name, json) in chain)
            {
                string jarName = json.TryGetValue("jar", out object jar) && jar is string text && text.Length > 0 ? text : name;
                string path = Path.Combine(folder.Path, "versions", jarName, jarName + ".jar");
                if (!File.Exists(path))
                    continue;

                try
                {
                    using (var zip = ZipFile.OpenRead(path))
                    {
                        var entry = zip.GetEntry("version.json");
                        if (entry == null || entry.Length > 64 * 1024)
                            return null;
                        string content;
                        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                            content = reader.ReadToEnd();
                        if (!(Json.TryParse(content) is Dictionary<string, object> info))
                            return null;

                        string id = (info.TryGetValue("name", out object n) ? n as string : null)
                                    ?? (info.TryGetValue("id", out object i) ? i as string : null);
                        int? world = info.TryGetValue("world_version", out object w) && w is double d ? (int)d : (int?)null;
                        bool? stable = info.TryGetValue("stable", out object s) && s is bool b ? b : (bool?)null;
                        return (id, world, stable);
                    }
                }
                catch (InvalidDataException)
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>The release name the launcher noted in versions\&lt;name&gt;\PCL\Setup.ini; snapshots get no usable name there.</summary>
        private static string ReleaseNameFromLauncher(string directory)
        {
            string value = SetupValue(directory, "VersionOriginal");
            return value != null && ReleaseName.IsMatch(value) ? value : null;
        }

        /// <summary>
        /// Works out which saves folder the version uses. A version that already ran in its own folder has
        /// options.txt or saves there. Otherwise the launcher's per-version switch decides, and without one the
        /// usual default: versions with a mod loader and non-release versions get their own folder.
        /// </summary>
        private static void ResolveSaves(InstalledVersion version)
        {
            string own = Path.Combine(version.Directory, "saves");
            string shared = Path.Combine(version.Folder.Path, "saves");

            if (System.IO.Directory.Exists(own) || File.Exists(Path.Combine(version.Directory, "options.txt")))
            {
                version.SavesDirectory = own;
                version.SavesCertainty = SavesCertainty.Found;
                return;
            }

            string setting = SetupValue(version.Directory, "VersionArgumentIndieV2");
            if (string.Equals(setting, "True", StringComparison.OrdinalIgnoreCase) || string.Equals(setting, "False", StringComparison.OrdinalIgnoreCase))
            {
                bool isolated = string.Equals(setting, "True", StringComparison.OrdinalIgnoreCase);
                version.SavesDirectory = isolated ? own : shared;
                version.SavesCertainty = SavesCertainty.LauncherSetting;
                return;
            }

            bool managedByLauncher = System.IO.Directory.Exists(Path.Combine(version.Directory, "PCL"));
            bool isolate = managedByLauncher && (version.Loader != VersionLoader.Vanilla || !version.IsRelease);
            version.SavesDirectory = isolate ? own : shared;
            version.SavesCertainty = SavesCertainty.Guessed;
        }

        private static string SetupValue(string directory, string key)
        {
            string path = Path.Combine(directory, "PCL", "Setup.ini");
            try
            {
                if (!File.Exists(path))
                    return null;
                foreach (string line in File.ReadLines(path))
                {
                    if (line.StartsWith(key + ":", StringComparison.Ordinal))
                        return line.Substring(key.Length + 1).Trim();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return null;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1} {2}{3})", Name, GameVersion ?? "?", Loader,
                LoaderVersion == null ? "" : " " + LoaderVersion);
        }
    }
}
