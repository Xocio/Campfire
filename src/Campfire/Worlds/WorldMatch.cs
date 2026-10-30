using System;
using System.Collections.Generic;
using System.Linq;

namespace Campfire.Worlds
{
    internal enum WorldFit
    {
        /// <summary>The world was saved by this very game version.</summary>
        Same,

        /// <summary>The world is older; the game converts it on first load, and the converted world cannot go back.</summary>
        Upgrade,

        /// <summary>
        /// The world predates 1.13 and the game is 1.13 or later. The conversion changes block ids, which commonly
        /// breaks command blocks and redstone in adventure and puzzle maps.
        /// </summary>
        MajorUpgrade,

        /// <summary>Neither side tells a comparable version.</summary>
        Unknown,

        /// <summary>The world is newer than the game, which then refuses or damages it.</summary>
        TooNew,
    }

    /// <summary>How well one installed version can open one world.</summary>
    internal sealed class WorldMatch
    {
        private WorldMatch(InstalledVersion version, WorldFit fit, bool loaderMissing)
        {
            Version = version;
            Fit = fit;
            LoaderMissing = loaderMissing;
        }

        public InstalledVersion Version { get; }

        public WorldFit Fit { get; }

        /// <summary>The world was played with Forge or NeoForge and this version does not have it.</summary>
        public bool LoaderMissing { get; }

        /// <summary>Whether this version can open the world as it was meant to be played.</summary>
        public bool IsGood => Fit == WorldFit.Same && !LoaderMissing;

        public static WorldMatch Check(WorldInfo world, InstalledVersion version)
        {
            bool loaderMissing = world.Loader == WorldLoader.Forge && version.Loader != VersionLoader.Forge
                                 || world.Loader == WorldLoader.NeoForge && version.Loader != VersionLoader.NeoForge;
            return new WorldMatch(version, Compare(world, version), loaderMissing);
        }

        /// <summary>
        /// Every version that can open the world, best first: same game version, then upgrades, then unknown.
        /// Versions the world is too new for are left out.
        /// </summary>
        public static IReadOnlyList<WorldMatch> Rank(WorldInfo world, IEnumerable<InstalledVersion> versions)
        {
            return versions.Select(v => Check(world, v))
                .Where(m => m.Fit != WorldFit.TooNew)
                .OrderBy(m => m.LoaderMissing)
                .ThenBy(m => m.Fit)
                // The closest newer game first: it converts the least. Releases before 1.14 have no data
                // version in their jar and are older than any that do; other names go last.
                .ThenBy(m => m.Version.DataVersion ?? (ParseRelease(m.Version.GameVersion ?? "") != null ? 0 : int.MaxValue))
                .ThenBy(m => ParseRelease(m.Version.GameVersion ?? "") ?? new Version(int.MaxValue, 0))
                .ThenBy(m => m.Version.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static WorldFit Compare(WorldInfo world, InstalledVersion version)
        {
            if (world.DataVersion != null && version.DataVersion != null)
                return CompareData(world.DataVersion.Value, version.DataVersion.Value);

            if (world.VersionName != null && version.GameVersion != null)
            {
                if (string.Equals(world.VersionName, version.GameVersion, StringComparison.OrdinalIgnoreCase))
                    return WorldFit.Same;
                var worldRelease = ParseRelease(world.VersionName);
                var gameRelease = ParseRelease(version.GameVersion);
                if (worldRelease != null && gameRelease != null)
                {
                    int order = worldRelease.CompareTo(gameRelease);
                    if (order > 0)
                        return WorldFit.TooNew;
                    bool crossesFlattening = worldRelease < new Version(1, 13) && gameRelease >= new Version(1, 13);
                    return order == 0 ? WorldFit.Same : crossesFlattening ? WorldFit.MajorUpgrade : WorldFit.Upgrade;
                }
            }

            // A world without a data version predates 1.9.
            if (world.DataVersion == null && version.DataVersion != null)
                return version.DataVersion >= WorldInfo.FlatteningDataVersion ? WorldFit.MajorUpgrade : WorldFit.Upgrade;
            return WorldFit.Unknown;
        }

        private static WorldFit CompareData(int world, int game)
        {
            if (world == game)
                return WorldFit.Same;
            if (world > game)
                return WorldFit.TooNew;
            return world < WorldInfo.FlatteningDataVersion && game >= WorldInfo.FlatteningDataVersion
                ? WorldFit.MajorUpgrade
                : WorldFit.Upgrade;
        }

        /// <summary>"1.20.1" or "26.3" as a comparable version; <c>null</c> for snapshots and other names.</summary>
        private static Version ParseRelease(string name)
        {
            string[] parts = name.Split('.');
            if (parts.Length < 2 || parts.Length > 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsDigit)))
                return null;
            try
            {
                return parts.Length == 2 ? new Version(int.Parse(parts[0]), int.Parse(parts[1])) : new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            }
            catch (OverflowException)
            {
                return null;
            }
        }
    }
}
