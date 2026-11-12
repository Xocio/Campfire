using System;
using System.Collections.Generic;
using System.Linq;
using Campfire.Worlds;

namespace Campfire.Mods
{
    internal enum ModIssueKind
    {
        /// <summary>The file does not open as a jar.</summary>
        Corrupt,

        /// <summary>The jar only carries metadata for another loader.</summary>
        WrongLoader,

        /// <summary>The mod declares game versions that do not include the installed one.</summary>
        WrongGameVersion,

        /// <summary>The mod declares loader versions that do not include the installed one.</summary>
        WrongLoaderVersion,

        /// <summary>A required mod is not in the folder.</summary>
        MissingDependency,

        /// <summary>A required mod is in the folder in a version outside the declared range.</summary>
        DependencyVersion,

        /// <summary>The mod refuses to load next to a mod that is in the folder.</summary>
        Incompatible,

        /// <summary>The same mod id comes from more than one file.</summary>
        Duplicate,
    }

    internal sealed class ModIssue
    {
        public ModIssue(ModIssueKind kind, ModManifest mod, string other = null, string detail = null)
        {
            Kind = kind;
            Mod = mod;
            Other = other;
            Detail = detail;
        }

        public ModIssueKind Kind { get; }

        public ModManifest Mod { get; }

        /// <summary>The other mod involved: the missing or refused id, or the other file of a duplicate.</summary>
        public string Other { get; }

        /// <summary>The range or version the issue is about, as written in the metadata.</summary>
        public string Detail { get; }

        /// <summary>The version of <see cref="Other"/> in the folder, where it matters.</summary>
        public string Installed { get; set; }

        /// <summary>Whether the issue stops the game from starting.</summary>
        public bool IsError => Kind != ModIssueKind.Duplicate;
    }

    /// <summary>
    /// Checks a mods folder against the version that loads it, from what each jar declares about itself:
    /// the loader it is made for, the game and loader versions it accepts, the mods it needs and the mods it refuses.
    /// </summary>
    internal static class ModAudit
    {
        /// <summary>Ids some loader versions bring along; a dependency on them is never reported missing.</summary>
        private static readonly string[] SometimesBuiltIn = { "mixinextras" };

        /// <summary>The metadata formats <paramref name="version"/>'s loader reads, in order of preference; empty for none.</summary>
        public static IReadOnlyList<ModFormat> FormatsOf(InstalledVersion version)
        {
            switch (version.Loader)
            {
                case VersionLoader.Fabric:
                    return new[] { ModFormat.Fabric };
                case VersionLoader.Quilt:
                    return new[] { ModFormat.Quilt, ModFormat.Fabric };
                case VersionLoader.Forge:
                    return IsLegacyForge(version) ? new[] { ModFormat.LegacyForge } : new[] { ModFormat.Forge };
                case VersionLoader.NeoForge:
                    return ReadsOnlyNeoForgeToml(version) ? new[] { ModFormat.NeoForge } : new[] { ModFormat.NeoForge, ModFormat.Forge };
                default:
                    return Array.Empty<ModFormat>();
            }
        }

        public static IReadOnlyList<ModIssue> Check(InstalledVersion version, IReadOnlyList<ModManifest> mods)
        {
            var issues = new List<ModIssue>();
            var formats = FormatsOf(version);
            if (formats.Count == 0)
                return issues;
            bool legacy = formats.Contains(ModFormat.LegacyForge);

            foreach (var mod in mods.Where(m => !m.IsReadable))
                issues.Add(new ModIssue(ModIssueKind.Corrupt, mod));

            // What the loader loads: each readable jar's metadata for it, and the jars packed inside.
            var loaded = new List<(ModManifest Mod, ModMetadata Metadata)>();
            foreach (var mod in mods.Where(m => m.IsReadable))
            {
                var metadata = mod.For(formats);
                if (metadata == null)
                {
                    if (!legacy && mod.Formats.Count > 0 && !HasLoaderBridge(mods, formats))
                    {
                        var made = new[] { ModFormat.Forge, ModFormat.NeoForge, ModFormat.Quilt, ModFormat.Fabric, ModFormat.LegacyForge }
                            .First(f => mod.Formats.ContainsKey(f));
                        issues.Add(new ModIssue(ModIssueKind.WrongLoader, mod, detail: FormatName(made)));
                    }
                    continue;
                }
                loaded.Add((mod, metadata));
            }
            if (legacy)
                return AddDuplicates(issues, loaded);

            var provided = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Provide(ModMetadata metadata)
            {
                foreach (string id in metadata.Ids)
                {
                    if (!provided.ContainsKey(id))
                        provided[id] = metadata.Version;
                }
                foreach (var nested in metadata.Nested)
                {
                    var inner = nested.For(formats);
                    if (inner != null)
                        Provide(inner);
                }
            }
            foreach (var (_, metadata) in loaded)
                Provide(metadata);
            var builtIn = BuiltIns(version);

            foreach (var (mod, metadata) in loaded)
            {
                foreach (var dependency in metadata.Dependencies)
                {
                    string id = dependency.Id;
                    if (string.Equals(id, "java", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (builtIn.TryGetValue(id, out string builtInVersion))
                    {
                        if (dependency.Kind == DependencyKind.Breaks || builtInVersion == null)
                            continue;
                        if (VersionRange.Matches(dependency, builtInVersion) == false)
                        {
                            var kind = IsGame(id) ? ModIssueKind.WrongGameVersion : ModIssueKind.WrongLoaderVersion;
                            issues.Add(new ModIssue(kind, mod, id, dependency.RangeText));
                        }
                        continue;
                    }

                    bool present = provided.TryGetValue(id, out string presentVersion);
                    switch (dependency.Kind)
                    {
                        case DependencyKind.Required when !present:
                            if (!SometimesBuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase))
                                issues.Add(new ModIssue(ModIssueKind.MissingDependency, mod, id, dependency.RangeText));
                            break;
                        case DependencyKind.Required:
                            if (VersionRange.Matches(dependency, presentVersion) == false)
                                issues.Add(new ModIssue(ModIssueKind.DependencyVersion, mod, id, dependency.RangeText) { Installed = presentVersion });
                            break;
                        case DependencyKind.Breaks when present:
                            // Any version is refused when no range is given.
                            bool? refused = dependency.Ranges.Count == 0 ? true : VersionRange.Matches(dependency, presentVersion);
                            if (refused == true)
                                issues.Add(new ModIssue(ModIssueKind.Incompatible, mod, id, presentVersion));
                            break;
                    }
                }
            }
            return AddDuplicates(issues, loaded);
        }

        /// <summary>Adds one issue for each mod id that more than one top-level jar declares.</summary>
        private static IReadOnlyList<ModIssue> AddDuplicates(List<ModIssue> issues, List<(ModManifest Mod, ModMetadata Metadata)> loaded)
        {
            foreach (var group in loaded.Where(l => l.Metadata.Ids.Count > 0).GroupBy(l => l.Metadata.Ids[0], StringComparer.OrdinalIgnoreCase))
            {
                var files = group.ToList();
                if (files.Count < 2)
                    continue;
                foreach (var file in files)
                    issues.Add(new ModIssue(ModIssueKind.Duplicate, file.Mod, string.Join(", ", files.Where(f => f.Mod != file.Mod).Select(f => f.Mod.FileName))));
            }
            return issues;
        }

        /// <summary>The ids the loader itself provides, with their versions where known.</summary>
        private static Dictionary<string, string> BuiltIns(InstalledVersion version)
        {
            string game = version.IsRelease ? version.GameVersion : null;
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["minecraft"] = game };
            switch (version.Loader)
            {
                case VersionLoader.Fabric:
                    ids["fabricloader"] = version.LoaderVersion;
                    break;
                case VersionLoader.Quilt:
                    ids["quilt_loader"] = version.LoaderVersion;
                    ids["fabricloader"] = null;
                    break;
                case VersionLoader.Forge:
                    ids["forge"] = version.LoaderVersion;
                    break;
                case VersionLoader.NeoForge:
                    ids["neoforge"] = version.LoaderVersion;
                    // NeoForge for 1.20.1 still calls itself forge.
                    if (version.GameVersion == "1.20.1")
                        ids["forge"] = null;
                    break;
            }
            return ids;
        }

        /// <summary>Whether a mod that lets this loader run mods made for another one is in the folder.</summary>
        private static bool HasLoaderBridge(IReadOnlyList<ModManifest> mods, IReadOnlyList<ModFormat> formats)
        {
            return mods.Any(m => m.For(formats)?.Ids.Contains("connector", StringComparer.OrdinalIgnoreCase) == true);
        }

        private static bool IsGame(string id) => string.Equals(id, "minecraft", StringComparison.OrdinalIgnoreCase);

        /// <summary>Forge up to 1.12.2, which reads mcmod.info.</summary>
        private static bool IsLegacyForge(InstalledVersion version)
        {
            var parsed = VersionRange.Parse(version.GameVersion);
            return parsed != null && parsed.Length >= 2 && parsed[0] == 1 && parsed[1] <= 12;
        }

        /// <summary>NeoForge from 1.20.5 on, which reads neoforge.mods.toml only.</summary>
        private static bool ReadsOnlyNeoForgeToml(InstalledVersion version)
        {
            var parsed = VersionRange.Parse(version.GameVersion);
            if (parsed == null)
                return false;
            return VersionRange.Compare(parsed, new[] { 1, 20, 5 }) >= 0;
        }

        /// <summary>The name of the loader a metadata file is made for.</summary>
        public static string FormatName(ModFormat format)
        {
            switch (format)
            {
                case ModFormat.Fabric:
                    return "Fabric";
                case ModFormat.Quilt:
                    return "Quilt";
                case ModFormat.NeoForge:
                    return "NeoForge";
                default:
                    return "Forge";
            }
        }
    }
}
