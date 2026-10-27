using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Campfire.Worlds
{
    /// <summary>The mod loader a world was last saved with, as far as level.dat tells.</summary>
    /// <remarks>Fabric and Quilt leave no trace in level.dat, so their worlds read as <see cref="None"/>.</remarks>
    internal enum WorldLoader
    {
        None,
        Forge,
        NeoForge,
    }

    internal sealed class WorldMod
    {
        public WorldMod(string id, string version)
        {
            Id = id;
            Version = version;
        }

        public string Id { get; }

        /// <summary>The version, or <c>null</c> when only the id is known.</summary>
        public string Version { get; }
    }

    /// <summary>
    /// What one level.dat says about its world: name, game version and the mods it was played with.
    /// </summary>
    internal sealed class WorldInfo
    {
        /// <summary>The first world format with the 1.13 block changes; older worlds are converted when opened.</summary>
        public const int FlatteningDataVersion = 1519;

        private static readonly Regex FormattingCodes = new Regex("§.?", RegexOptions.CultureInvariant);

        /// <summary>Mod list entries that are the game or the loader itself.</summary>
        private static readonly HashSet<string> BuiltInMods =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "minecraft", "mcp", "FML", "forge", "neoforge" };

        private WorldInfo()
        {
        }

        /// <summary>Folder of the world inside the source, with '/' separators; empty when the source is the world itself.</summary>
        public string Directory { get; private set; }

        /// <summary>The name shown in the game's world list, without formatting codes.</summary>
        public string LevelName { get; private set; }

        /// <summary>World data version, written since 1.9. The game's own version.json uses the same numbers.</summary>
        public int? DataVersion { get; private set; }

        /// <summary>Game version such as "26.3", or <c>null</c> when it cannot be told.</summary>
        public string VersionName { get; private set; }

        /// <summary>The version was worked out from the mod list rather than read, which is the case before 1.9.</summary>
        public bool VersionInferred { get; private set; }

        public bool IsSnapshot { get; private set; }

        public WorldLoader Loader { get; private set; }

        public string LoaderVersion { get; private set; }

        /// <summary>Mods the world was saved with, without the game and the loader.</summary>
        public IReadOnlyList<WorldMod> Mods { get; private set; }

        /// <summary>level.dat was damaged and the game's backup level.dat_old was read instead.</summary>
        public bool FromBackup { get; private set; }

        /// <summary>0 survival, 1 creative, 2 adventure, 3 spectator.</summary>
        public int GameType { get; private set; }

        public bool Hardcore { get; private set; }

        public bool AllowCommands { get; private set; }

        /// <summary>Reads the root compound of a Java Edition level.dat.</summary>
        public static WorldInfo FromLevelData(Dictionary<string, object> root, string directory, bool fromBackup)
        {
            var info = new WorldInfo
            {
                Directory = directory,
                FromBackup = fromBackup,
                LevelName = StripFormatting(Nbt.GetString(root, "Data", "LevelName")),
                GameType = (int)(Nbt.GetNumber(root, "Data", "GameType") ?? 0),
                Hardcore = Nbt.GetNumber(root, "Data", "hardcore") == 1,
                AllowCommands = Nbt.GetNumber(root, "Data", "allowCommands") == 1,
            };

            long? dataVersion = Nbt.GetNumber(root, "Data", "DataVersion");
            if (dataVersion > 0 && dataVersion <= int.MaxValue)
                info.DataVersion = (int)dataVersion.Value;
            info.VersionName = Nbt.GetString(root, "Data", "Version", "Name");
            info.IsSnapshot = Nbt.GetNumber(root, "Data", "Version", "Snapshot") == 1;

            ReadMods(root, info);

            if (info.VersionName == null)
            {
                info.VersionName = InferVersion(root, info);
                info.VersionInferred = info.VersionName != null;
            }
            return info;
        }

        /// <summary>Removes the section-sign colour and style codes some map makers put in the name.</summary>
        internal static string StripFormatting(string name)
        {
            return name == null ? null : FormattingCodes.Replace(name, "").Trim();
        }

        /// <summary>
        /// Forge writes its mod list into level.dat: "fml" → "LoadingModList" since 1.13 and "FML" → "ModList"
        /// before. Both list ModId and ModVersion, including entries for the game and the loader.
        /// </summary>
        private static void ReadMods(Dictionary<string, object> root, WorldInfo info)
        {
            var mods = new List<WorldMod>();
            foreach (var pair in root)
            {
                if (pair.Key == "Data")
                    continue;
                var list = Nbt.Get(pair.Value, "LoadingModList") as object[] ?? Nbt.Get(pair.Value, "ModList") as object[];
                if (list == null)
                    continue;
                foreach (object entry in list)
                {
                    string id = Nbt.GetString(entry, "ModId");
                    if (!string.IsNullOrEmpty(id))
                        mods.Add(new WorldMod(id, Nbt.GetString(entry, "ModVersion")));
                }
            }

            // Without a mod list, data packs that mods register still show the world needs them.
            if (mods.Count == 0 && Nbt.Get(root, "Data", "DataPacks", "Enabled") is object[] packs)
            {
                foreach (string pack in packs.OfType<string>())
                {
                    if (pack.StartsWith("mod:", StringComparison.Ordinal) && pack.Length > 4)
                        mods.Add(new WorldMod(pack.Substring(4), null));
                }
            }

            var neoForge = mods.FirstOrDefault(m => m.Id.Equals("neoforge", StringComparison.OrdinalIgnoreCase));
            var forge = mods.FirstOrDefault(m => m.Id.Equals("forge", StringComparison.OrdinalIgnoreCase));
            if (neoForge != null)
            {
                info.Loader = WorldLoader.NeoForge;
                info.LoaderVersion = neoForge.Version;
            }
            else if (forge != null)
            {
                info.Loader = WorldLoader.Forge;
                info.LoaderVersion = forge.Version;
            }
            else if (mods.Count > 0)
            {
                var brands = Nbt.Get(root, "Data", "ServerBrands") as object[];
                info.Loader = brands != null && brands.Contains("neoforge") ? WorldLoader.NeoForge : WorldLoader.Forge;
            }

            info.Mods = mods.Where(m => !BuiltInMods.Contains(m.Id))
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>Forge build numbers before 1.9, which is when level.dat started to name the game version.</summary>
        private static readonly (string Prefix, string Game)[] ForgeBuilds =
        {
            ("11.15.1.", "1.8.9"),
            ("11.15.0.", "1.8.8"),
            ("11.14.", "1.8"),
            ("10.13.", "1.7.10"),
            ("10.12.", "1.7.2"),
            ("9.11.", "1.6.4"),
            ("9.10.", "1.6.2"),
        };

        private static readonly (string Mcp, string Game)[] McpBuilds =
        {
            ("9.19", "1.8.9"),
            ("9.05", "1.7.10"),
        };

        private static string InferVersion(Dictionary<string, object> root, WorldInfo info)
        {
            string minecraft = ModVersion(root, "minecraft");
            if (!string.IsNullOrEmpty(minecraft))
                return minecraft;

            if (info.Loader == WorldLoader.Forge && info.LoaderVersion != null)
            {
                foreach (var (prefix, game) in ForgeBuilds)
                {
                    if (info.LoaderVersion.StartsWith(prefix, StringComparison.Ordinal))
                        return game;
                }
            }

            string mcp = ModVersion(root, "mcp");
            if (mcp != null)
            {
                foreach (var (prefix, game) in McpBuilds)
                {
                    if (mcp == prefix)
                        return game;
                }
            }
            return null;
        }

        /// <summary>The version of a built-in entry such as "minecraft" or "mcp", which <see cref="Mods"/> leaves out.</summary>
        private static string ModVersion(Dictionary<string, object> root, string id)
        {
            foreach (var pair in root)
            {
                var list = Nbt.Get(pair.Value, "LoadingModList") as object[] ?? Nbt.Get(pair.Value, "ModList") as object[];
                var entry = list?.FirstOrDefault(e => string.Equals(Nbt.GetString(e, "ModId"), id, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                    return Nbt.GetString(entry, "ModVersion");
            }
            return null;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1}, data {2})", LevelName, VersionName ?? "?", DataVersion?.ToString(CultureInfo.InvariantCulture) ?? "-");
        }
    }
}
