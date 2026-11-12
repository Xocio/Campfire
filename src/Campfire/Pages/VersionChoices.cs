using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Campfire.Localization;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>The installed versions offered on the tool pages, and how each is named in their lists.</summary>
    internal static class VersionChoices
    {
        /// <summary>Every installed version in the folders Campfire knows, also those next to <paramref name="launcherDirectories"/>.</summary>
        public static Task<List<InstalledVersion>> ScanAsync(IEnumerable<string> launcherDirectories)
        {
            var directories = (launcherDirectories ?? Array.Empty<string>()).ToList();
            return Task.Run(() => GameFolders.Find(directories).SelectMany(InstalledVersion.Scan).ToList());
        }

        /// <summary>"Name · 1.20.1 Fabric", or the name with the game version alone for vanilla.</summary>
        public static string Label(InstalledVersion version)
        {
            string game = version.GameVersion ?? "?";
            string loader = version.Loader == VersionLoader.Vanilla ? Text.Get("Mods.Vanilla") : version.Loader.ToString();
            string detail = game + " " + loader;
            return string.Equals(version.Name, game, StringComparison.OrdinalIgnoreCase) && version.Loader == VersionLoader.Vanilla
                ? version.Name
                : version.Name + " · " + detail;
        }

        /// <summary>The folder the version's game runs in: next to its saves and mods.</summary>
        public static string GameDirectory(InstalledVersion version)
        {
            return Path.GetDirectoryName(version.SavesDirectory);
        }

        /// <summary>The version stored under <paramref name="key"/>, else <paramref name="fallback"/>'s pick, else the first.</summary>
        public static int Remembered(IReadOnlyList<InstalledVersion> versions, string key, Func<InstalledVersion, bool> fallback = null)
        {
            string saved = AppSettings.Get(key);
            int index = saved == null ? -1 : versions.ToList().FindIndex(v => string.Equals(v.Directory, saved, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && fallback != null)
                index = versions.ToList().FindIndex(v => fallback(v));
            return index < 0 && versions.Count > 0 ? 0 : index;
        }
    }
}
