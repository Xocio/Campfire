using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Campfire.Worlds;

namespace Campfire.Mods
{
    /// <summary>
    /// The host's mods against one installed version on this computer. The versions offered are those with the
    /// host's game version and loader; the one picked last time comes first, then the one with the fewest changes.
    /// </summary>
    internal sealed class ModCheck
    {
        private const string ChosenKey = "link.modversion";

        private ModCheck(ModList host, IReadOnlyList<(InstalledVersion Version, ModComparison Comparison)> candidates, int selected)
        {
            Host = host;
            Candidates = candidates.Select(c => c.Version).ToList();
            _comparisons = candidates.Select(c => c.Comparison).ToList();
            _selected = selected;
        }

        private readonly IReadOnlyList<ModComparison> _comparisons;
        private readonly int _selected;

        public ModList Host { get; }

        /// <summary>Installed versions that run the host's game version with the host's loader.</summary>
        public IReadOnlyList<InstalledVersion> Candidates { get; }

        /// <summary>The version compared, or <c>null</c> when none fits.</summary>
        public InstalledVersion Version => _selected < 0 ? null : Candidates[_selected];

        public ModComparison Comparison => _selected < 0 ? null : _comparisons[_selected];

        /// <summary>Whether the host plays with mods that players need too; without any there is nothing to check.</summary>
        public bool HostHasMods => Host.Mods.Any(m => m.IsShared) || (Host.Loader != null && Host.Loader != VersionLoader.Vanilla);

        public static Task<ModCheck> RunAsync(ModList host, IEnumerable<string> launcherDirectories)
        {
            var directories = launcherDirectories.ToList();
            return Task.Run(() =>
            {
                var versions = GameFolders.Find(directories).SelectMany(InstalledVersion.Scan).ToList();
                var candidates = versions
                    .Where(v => host.GameVersion == null || string.Equals(v.GameVersion, host.GameVersion, StringComparison.OrdinalIgnoreCase))
                    .Where(v => host.Loader == null || v.Loader == host.Loader)
                    .Select(v => (Version: v, Comparison: ModComparison.Compare(host.Mods, ModList.ReadFolder(v.ModsDirectory))))
                    .ToList();

                string chosen = AppSettings.Get(ChosenKey);
                var ordered = candidates
                    .OrderByDescending(c => string.Equals(c.Version.Directory, chosen, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(c => c.Comparison.Changes.Count)
                    .ThenByDescending(c => c.Version.LoaderVersion == host.LoaderVersion)
                    .ThenBy(c => c.Version.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new ModCheck(host, ordered, ordered.Count > 0 ? 0 : -1);
            });
        }

        /// <summary>Compares <paramref name="version"/> instead, and remembers it for next time.</summary>
        public Task<ModCheck> SelectAsync(InstalledVersion version)
        {
            AppSettings.Set(ChosenKey, version.Directory);
            return RefreshAsync(version);
        }

        /// <summary>Reads the mods folder of the selected version again, as after a sync.</summary>
        public Task<ModCheck> RefreshAsync(InstalledVersion version = null)
        {
            var target = version ?? Version;
            return Task.Run(() =>
            {
                var list = Candidates
                    .Select((v, i) => (Version: v, Comparison: v == target
                        ? ModComparison.Compare(Host.Mods, ModList.ReadFolder(v.ModsDirectory))
                        : _comparisons[i]))
                    .ToList();
                int index = Candidates.ToList().IndexOf(target);
                return new ModCheck(Host, list, index);
            });
        }

        /// <summary>The mods folder of the selected version, which may not exist yet.</summary>
        public string ModsDirectory => Version?.ModsDirectory;

        public bool ModsDirectoryExists => ModsDirectory != null && Directory.Exists(ModsDirectory);
    }
}
