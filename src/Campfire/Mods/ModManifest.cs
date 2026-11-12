using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Campfire.Link;

namespace Campfire.Mods
{
    /// <summary>The metadata files a loader reads from a jar.</summary>
    internal enum ModFormat
    {
        /// <summary>fabric.mod.json, read by Fabric and Quilt.</summary>
        Fabric,

        /// <summary>quilt.mod.json, read by Quilt.</summary>
        Quilt,

        /// <summary>META-INF/mods.toml, read by Forge and by NeoForge up to 1.20.4.</summary>
        Forge,

        /// <summary>META-INF/neoforge.mods.toml, read by NeoForge from 1.20.5 on.</summary>
        NeoForge,

        /// <summary>mcmod.info, read by Forge up to 1.12.2.</summary>
        LegacyForge,
    }

    internal enum DependencyKind
    {
        Required,
        Optional,

        /// <summary>The mod refuses to load next to a matching version of the other mod.</summary>
        Breaks,
    }

    /// <summary>How a dependency writes its versions.</summary>
    internal enum RangeScheme
    {
        /// <summary>Fabric and Quilt predicates such as "&gt;=1.2 &lt;2" or "1.20.x"; several entries mean any of them.</summary>
        Predicate,

        /// <summary>Forge and NeoForge ranges such as "[1.20,1.21)".</summary>
        Maven,
    }

    internal sealed class ModDependency
    {
        public ModDependency(string id, IReadOnlyList<string> ranges, DependencyKind kind, RangeScheme scheme)
        {
            Id = id;
            Ranges = ranges ?? Array.Empty<string>();
            Kind = kind;
            Scheme = scheme;
        }

        public string Id { get; }

        /// <summary>The versions the mod accepts as written in its metadata; empty for any version.</summary>
        public IReadOnlyList<string> Ranges { get; }

        public DependencyKind Kind { get; }

        public RangeScheme Scheme { get; }

        /// <summary>The ranges as one line for display.</summary>
        public string RangeText => Ranges.Count == 0 ? "*" : string.Join(" | ", Ranges);
    }

    /// <summary>What one metadata file of a jar declares.</summary>
    internal sealed class ModMetadata
    {
        public ModMetadata(ModFormat format, IReadOnlyList<string> ids, string name, string version, IReadOnlyList<ModDependency> dependencies,
            IReadOnlyList<ModManifest> nested, IReadOnlyList<string> mixinConfigs)
        {
            Format = format;
            Ids = ids ?? Array.Empty<string>();
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            Dependencies = dependencies ?? Array.Empty<ModDependency>();
            Nested = nested ?? Array.Empty<ModManifest>();
            MixinConfigs = mixinConfigs ?? Array.Empty<string>();
        }

        public ModFormat Format { get; }

        /// <summary>The mod id first, then the ids it also provides.</summary>
        public IReadOnlyList<string> Ids { get; }

        public string Name { get; }

        public string Version { get; }

        public IReadOnlyList<ModDependency> Dependencies { get; }

        /// <summary>Jars packed inside this jar that the loader also loads.</summary>
        public IReadOnlyList<ModManifest> Nested { get; }

        /// <summary>The mixin configuration files the mod names.</summary>
        public IReadOnlyList<string> MixinConfigs { get; }
    }

    /// <summary>
    /// A jar in a mods folder with every metadata file it carries, read for checking the folder: what each loader
    /// sees, what the mods need and what they refuse.
    /// </summary>
    internal sealed class ModManifest
    {
        private const int MaxMetadataSize = 256 * 1024;
        private const long MaxNestedSize = 64L * 1024 * 1024;
        private const int MaxDepth = 3;

        private ModManifest(string fileName, string path, long size, IReadOnlyDictionary<ModFormat, ModMetadata> formats, bool readable)
        {
            FileName = fileName;
            Path = path;
            Size = size;
            Formats = formats;
            IsReadable = readable;
        }

        public string FileName { get; }

        /// <summary>Full path of the jar, or <c>null</c> for a jar packed inside another one.</summary>
        public string Path { get; }

        public long Size { get; }

        /// <summary>Whether the file opens as a zip archive.</summary>
        public bool IsReadable { get; }

        public IReadOnlyDictionary<ModFormat, ModMetadata> Formats { get; }

        /// <summary>The metadata a loader that reads <paramref name="formats"/>, in that order of preference, uses; <c>null</c> when none.</summary>
        public ModMetadata For(IEnumerable<ModFormat> formats)
        {
            foreach (var format in formats)
            {
                if (Formats.TryGetValue(format, out var metadata))
                    return metadata;
            }
            return null;
        }

        /// <summary>A name to show: the display name or id of any metadata, else the file name.</summary>
        public string Title
        {
            get
            {
                var any = Formats.Values.FirstOrDefault();
                return any?.Name ?? (any != null && any.Ids.Count > 0 ? any.Ids[0] : System.IO.Path.GetFileNameWithoutExtension(FileName));
            }
        }

        /// <summary>
        /// Reads the jar at <paramref name="path"/>. A file that is not a zip archive comes back unreadable; a file
        /// that cannot be opened at all, such as one another program holds, comes back as <c>null</c>.
        /// </summary>
        public static ModManifest Read(string path)
        {
            try
            {
                var file = new FileInfo(path);
                try
                {
                    using (var zip = ZipFile.OpenRead(path))
                        return new ModManifest(file.Name, path, file.Length, ReadFormats(zip, 0), true);
                }
                catch (InvalidDataException)
                {
                    return new ModManifest(file.Name, path, file.Length, new Dictionary<ModFormat, ModMetadata>(), false);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Warn("A mod file could not be opened: " + System.IO.Path.GetFileName(path) + " (" + ex.Message + ")");
                return null;
            }
        }

        private static ModManifest ReadNested(ZipArchive outer, string entryName, int depth)
        {
            var entry = outer.GetEntry(entryName);
            if (entry == null || entry.Length > MaxNestedSize || depth > MaxDepth)
                return null;
            try
            {
                var buffer = new MemoryStream();
                using (var input = entry.Open())
                    input.CopyTo(buffer);
                buffer.Position = 0;
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read))
                    return new ModManifest(System.IO.Path.GetFileName(entryName), null, entry.Length, ReadFormats(zip, depth), true);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        private static Dictionary<ModFormat, ModMetadata> ReadFormats(ZipArchive zip, int depth)
        {
            var formats = new Dictionary<ModFormat, ModMetadata>();
            void Add(ModMetadata metadata)
            {
                if (metadata != null && metadata.Ids.Count > 0)
                    formats[metadata.Format] = metadata;
            }
            Add(ReadFabric(zip, depth));
            Add(ReadQuilt(zip, depth));
            var jarJar = ReadJarJar(zip, depth);
            string manifestMixins = ManifestValue(zip, "MixinConfigs");
            Add(ReadForge(zip, "META-INF/mods.toml", ModFormat.Forge, jarJar, manifestMixins));
            Add(ReadForge(zip, "META-INF/neoforge.mods.toml", ModFormat.NeoForge, jarJar, manifestMixins));
            Add(ReadLegacyForge(zip));
            return formats;
        }

        // ======================== Fabric and Quilt ========================

        private static ModMetadata ReadFabric(ZipArchive zip, int depth)
        {
            if (!(Json.TryParse(ReadEntry(zip, "fabric.mod.json")) is Dictionary<string, object> json))
                return null;
            var ids = new List<string>();
            AddId(ids, Get(json, "id"));
            if (json.TryGetValue("provides", out object provides) && provides is object[] list)
            {
                foreach (string id in list.OfType<string>())
                    AddId(ids, id);
            }

            var dependencies = new List<ModDependency>();
            ReadFabricDependencies(json, "depends", DependencyKind.Required, dependencies);
            ReadFabricDependencies(json, "breaks", DependencyKind.Breaks, dependencies);

            var nested = new List<ModManifest>();
            if (json.TryGetValue("jars", out object jars) && jars is object[] jarList)
            {
                foreach (var jar in jarList.OfType<Dictionary<string, object>>())
                {
                    var inner = Get(jar, "file") is string file ? ReadNested(zip, file, depth + 1) : null;
                    if (inner != null)
                        nested.Add(inner);
                }
            }

            var mixins = new List<string>();
            if (json.TryGetValue("mixins", out object mixinValue) && mixinValue is object[] mixinList)
            {
                foreach (object item in mixinList)
                {
                    string config = item as string ?? (item is Dictionary<string, object> entry ? Get(entry, "config") : null);
                    if (!string.IsNullOrEmpty(config))
                        mixins.Add(config);
                }
            }
            return new ModMetadata(ModFormat.Fabric, ids, Get(json, "name"), Get(json, "version"), dependencies, nested, mixins);
        }

        /// <summary>Fabric writes each dependency as "id": "range" or "id": ["range", ...].</summary>
        private static void ReadFabricDependencies(Dictionary<string, object> json, string key, DependencyKind kind, List<ModDependency> into)
        {
            if (!(json.TryGetValue(key, out object value) && value is Dictionary<string, object> map))
                return;
            foreach (var pair in map)
            {
                if (!ModInfo.IsModId(pair.Key))
                    continue;
                var ranges = pair.Value is string single ? new[] { single }
                    : pair.Value is object[] many ? many.OfType<string>().ToArray()
                    : null;
                if (ranges == null)
                    continue;
                into.Add(new ModDependency(pair.Key, ranges.Where(r => r.Trim() != "*").ToList(), kind, RangeScheme.Predicate));
            }
        }

        private static ModMetadata ReadQuilt(ZipArchive zip, int depth)
        {
            if (!(Json.TryParse(ReadEntry(zip, "quilt.mod.json")) is Dictionary<string, object> json)
                || !(json.TryGetValue("quilt_loader", out object loaderValue) && loaderValue is Dictionary<string, object> loader))
                return null;
            var ids = new List<string>();
            AddId(ids, Get(loader, "id"));
            if (loader.TryGetValue("provides", out object provides) && provides is object[] list)
            {
                foreach (object item in list)
                    AddId(ids, item as string ?? (item is Dictionary<string, object> entry ? Get(entry, "id") : null));
            }

            var dependencies = new List<ModDependency>();
            ReadQuiltDependencies(loader, "depends", false, dependencies);
            ReadQuiltDependencies(loader, "breaks", true, dependencies);

            var nested = new List<ModManifest>();
            if (loader.TryGetValue("jars", out object jars) && jars is object[] jarList)
            {
                foreach (string file in jarList.OfType<string>())
                {
                    var inner = ReadNested(zip, file, depth + 1);
                    if (inner != null)
                        nested.Add(inner);
                }
            }

            var mixins = new List<string>();
            if (json.TryGetValue("mixin", out object mixinValue))
            {
                if (mixinValue is string one)
                    mixins.Add(one);
                else if (mixinValue is object[] many)
                    mixins.AddRange(many.OfType<string>());
            }

            string name = loader.TryGetValue("metadata", out object metadataValue) && metadataValue is Dictionary<string, object> metadata
                ? Get(metadata, "name")
                : null;
            return new ModMetadata(ModFormat.Quilt, ids, name, Get(loader, "version"), dependencies, nested, mixins);
        }

        /// <summary>
        /// Quilt writes each dependency as "id" or as an object with "id", "versions" and "optional". Objects with
        /// conditions the check does not follow, such as "unless", are left out.
        /// </summary>
        private static void ReadQuiltDependencies(Dictionary<string, object> loader, string key, bool breaks, List<ModDependency> into)
        {
            if (!(loader.TryGetValue(key, out object value) && value is object[] list))
                return;
            foreach (object item in list)
            {
                if (item is string id)
                {
                    if (ModInfo.IsModId(id))
                        into.Add(new ModDependency(id, null, breaks ? DependencyKind.Breaks : DependencyKind.Required, RangeScheme.Predicate));
                    continue;
                }
                if (!(item is Dictionary<string, object> entry) || entry.ContainsKey("unless") || !(Get(entry, "id") is string entryId))
                    continue;
                // "group:id" names a Maven artifact; only the part after the colon is the mod id.
                int colon = entryId.IndexOf(':');
                if (colon >= 0)
                    entryId = entryId.Substring(colon + 1);
                if (!ModInfo.IsModId(entryId))
                    continue;

                IReadOnlyList<string> ranges;
                if (!entry.TryGetValue("versions", out object versions))
                    ranges = null;
                else if (versions is string text)
                    ranges = text.Trim() == "*" ? null : new[] { text };
                else if (versions is object[] any)
                    ranges = any.OfType<string>().ToList();
                else
                    continue;
                bool optional = entry.TryGetValue("optional", out object optionalValue) && optionalValue is bool flag && flag;
                var kind = breaks ? DependencyKind.Breaks : optional ? DependencyKind.Optional : DependencyKind.Required;
                into.Add(new ModDependency(entryId, ranges, kind, RangeScheme.Predicate));
            }
        }

        // ======================== Forge and NeoForge ========================

        /// <summary>
        /// mods.toml lists [[mods]] tables and one [[dependencies.modid]] table per dependency. Forge marks required
        /// ones with mandatory = true; NeoForge writes type = "required", "optional", "incompatible" or "discouraged".
        /// Dependencies that only apply on a server are left out.
        /// </summary>
        private static ModMetadata ReadForge(ZipArchive zip, string entryName, ModFormat format, IReadOnlyList<ModManifest> jarJar, string manifestMixins)
        {
            string text = ReadEntry(zip, entryName);
            if (text == null)
                return null;
            var tables = MiniToml.Parse(text);
            var mods = tables.Where(t => t.Name == "mods").ToList();
            var ids = new List<string>();
            foreach (var mod in mods)
                AddId(ids, mod.Get("modId"));
            if (ids.Count == 0)
                return null;

            var first = mods.First(m => m.Get("modId") != null);
            string version = first.Get("version");
            if (version != null && version.Contains("${"))
                version = ManifestValue(zip, "Implementation-Version");

            var dependencies = new List<ModDependency>();
            foreach (var table in tables.Where(t => t.Name.StartsWith("dependencies.", StringComparison.Ordinal)))
            {
                string id = table.Get("modId");
                if (!ModInfo.IsModId(id) || string.Equals(table.Get("side"), "SERVER", StringComparison.OrdinalIgnoreCase))
                    continue;
                string type = table.Get("type")?.ToLowerInvariant();
                string mandatory = table.Get("mandatory")?.ToLowerInvariant();
                DependencyKind kind;
                if (type == "incompatible")
                    kind = DependencyKind.Breaks;
                else if (type == "optional" || type == "discouraged" || (type == null && mandatory == "false"))
                    kind = DependencyKind.Optional;
                else
                    kind = DependencyKind.Required;
                string range = table.Get("versionRange");
                var ranges = string.IsNullOrWhiteSpace(range) || range.Trim() == "*" ? null : new[] { range.Trim() };
                dependencies.Add(new ModDependency(id, ranges, kind, RangeScheme.Maven));
            }

            var mixins = tables.Where(t => t.Name == "mixins").Select(t => t.Get("config")).Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (!string.IsNullOrEmpty(manifestMixins))
                mixins.AddRange(manifestMixins.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0));
            return new ModMetadata(format, ids, first.Get("displayName"), version, dependencies, jarJar, mixins.Distinct().ToList());
        }

        /// <summary>The jars listed in META-INF/jarjar/metadata.json, which Forge and NeoForge load from inside the jar.</summary>
        private static IReadOnlyList<ModManifest> ReadJarJar(ZipArchive zip, int depth)
        {
            if (!(Json.TryParse(ReadEntry(zip, "META-INF/jarjar/metadata.json")) is Dictionary<string, object> json)
                || !(json.TryGetValue("jars", out object jars) && jars is object[] list))
                return Array.Empty<ModManifest>();
            var nested = new List<ModManifest>();
            foreach (var jar in list.OfType<Dictionary<string, object>>())
            {
                var inner = Get(jar, "path") is string path ? ReadNested(zip, path, depth + 1) : null;
                if (inner != null)
                    nested.Add(inner);
            }
            return nested;
        }

        private static ModMetadata ReadLegacyForge(ZipArchive zip)
        {
            object json = Json.TryParse(ReadEntry(zip, "mcmod.info"));
            object[] list = json as object[]
                            ?? (json is Dictionary<string, object> wrapper && wrapper.TryGetValue("modList", out object inner) ? inner as object[] : null);
            var mods = list?.OfType<Dictionary<string, object>>().ToList();
            if (mods == null || mods.Count == 0)
                return null;
            var ids = new List<string>();
            foreach (var mod in mods)
                AddId(ids, Get(mod, "modid"));
            if (ids.Count == 0)
                return null;
            var first = mods.First(m => Get(m, "modid") != null);
            string version = Get(first, "version");
            if (version != null && version.Contains("${"))
                version = ManifestValue(zip, "Implementation-Version");
            return new ModMetadata(ModFormat.LegacyForge, ids, Get(first, "name"), version, null, null,
                ManifestValue(zip, "MixinConfigs")?.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList());
        }

        // ======================== Helpers ========================

        /// <summary>A main attribute of META-INF/MANIFEST.MF, such as "Implementation-Version", or <c>null</c>.</summary>
        private static string ManifestValue(ZipArchive zip, string key)
        {
            string manifest = ReadEntry(zip, "META-INF/MANIFEST.MF");
            if (manifest == null)
                return null;
            string prefix = key + ":";
            foreach (string line in manifest.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(prefix.Length).Trim();
            }
            return null;
        }

        private static string ReadEntry(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            if (entry == null || entry.Length > MaxMetadataSize)
                return null;
            try
            {
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        private static string Get(Dictionary<string, object> json, string key)
        {
            return json.TryGetValue(key, out object value) ? value as string : null;
        }

        private static void AddId(List<string> ids, string id)
        {
            if (ModInfo.IsModId(id) && !ids.Contains(id, StringComparer.OrdinalIgnoreCase) && ids.Count < 16)
                ids.Add(id);
        }
    }
}
