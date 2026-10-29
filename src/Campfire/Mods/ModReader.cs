using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Campfire.Link;

namespace Campfire.Mods
{
    /// <summary>
    /// Reads what a mod jar says about itself. Each loader keeps its own metadata file; a jar may carry several
    /// when it supports more than one loader, and the first one found wins.
    /// </summary>
    internal static class ModReader
    {
        private const int MaxMetadataSize = 256 * 1024;

        /// <summary>Hashes and reads the jar at <paramref name="path"/>. A jar that cannot be opened as a zip still gets its hash.</summary>
        public static ModInfo Read(string path)
        {
            var file = new FileInfo(path);
            string sha1 = Sha1(path);

            Metadata metadata = null;
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    metadata = ReadFabric(zip, "fabric.mod.json") ?? ReadQuilt(zip) ?? ReadForge(zip, "META-INF/neoforge.mods.toml")
                               ?? ReadForge(zip, "META-INF/mods.toml") ?? ReadLegacyForge(zip);
                }
            }
            catch (InvalidDataException)
            {
            }

            return new ModInfo(file.Name, sha1, file.Length, metadata?.Ids, metadata?.Name, metadata?.Version, metadata?.Side ?? ModSide.Both,
                CurseForgeFingerprint(path));
        }

        /// <summary>
        /// The fingerprint CurseForge files a jar under: 32-bit MurmurHash2 with seed 1 over the file with every
        /// tab, line feed, carriage return and space byte left out.
        /// </summary>
        public static uint CurseForgeFingerprint(string path)
        {
            const uint m = 0x5bd1e995;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            {
                // The hash starts from the length of what it covers, so the file is read twice.
                uint length = 0;
                var buffer = new byte[1 << 16];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        if (!IsWhitespace(buffer[i]))
                            length++;
                    }
                }

                uint h = 1 ^ length;
                uint word = 0;
                int shift = 0;
                stream.Position = 0;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        byte b = buffer[i];
                        if (IsWhitespace(b))
                            continue;
                        word |= (uint)b << shift;
                        shift += 8;
                        if (shift < 32)
                            continue;
                        unchecked
                        {
                            word *= m;
                            word ^= word >> 24;
                            word *= m;
                            h = h * m ^ word;
                        }
                        word = 0;
                        shift = 0;
                    }
                }

                unchecked
                {
                    if (shift > 0)
                        h = (h ^ word) * m;
                    h ^= h >> 13;
                    h *= m;
                    h ^= h >> 15;
                }
                return h;
            }
        }

        private static bool IsWhitespace(byte b) => b == 9 || b == 10 || b == 13 || b == 32;

        public static string Sha1(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            using (var sha1 = SHA1.Create())
                return Hex.Of(sha1.ComputeHash(stream));
        }

        // ======================== Fabric and Quilt ========================

        private static Metadata ReadFabric(ZipArchive zip, string entryName)
        {
            if (!(Json.TryParse(ReadEntry(zip, entryName)) is Dictionary<string, object> json))
                return null;
            var ids = new List<string>();
            AddId(ids, Get(json, "id"));
            if (json.TryGetValue("provides", out object provides) && provides is object[] list)
            {
                foreach (string id in list.OfType<string>())
                    AddId(ids, id);
            }
            if (ids.Count == 0)
                return null;

            ModSide side;
            switch (Get(json, "environment"))
            {
                case "client": side = ModSide.Client; break;
                case "server": side = ModSide.Server; break;
                default: side = ModSide.Both; break;
            }
            return new Metadata(ids, Get(json, "name"), Get(json, "version"), side);
        }

        private static Metadata ReadQuilt(ZipArchive zip)
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
            if (ids.Count == 0)
                return null;

            string name = loader.TryGetValue("metadata", out object metadataValue) && metadataValue is Dictionary<string, object> metadata
                ? Get(metadata, "name")
                : null;
            string environment = json.TryGetValue("minecraft", out object minecraftValue) && minecraftValue is Dictionary<string, object> minecraft
                ? Get(minecraft, "environment")
                : null;
            var side = environment == "client" ? ModSide.Client : environment == "dedicated_server" ? ModSide.Server : ModSide.Both;
            return new Metadata(ids, name, Get(loader, "version"), side);
        }

        // ======================== Forge and NeoForge ========================

        /// <summary>
        /// mods.toml lists one or more [[mods]] tables. A mod that sets displayTest to ignore the other side's
        /// version, or clientSideOnly, does not need to be on both sides.
        /// </summary>
        private static Metadata ReadForge(ZipArchive zip, string entryName)
        {
            string text = ReadEntry(zip, entryName);
            if (text == null)
                return null;
            var tables = MiniToml.Parse(text);
            var root = tables.FirstOrDefault(t => t.Name == "");
            var mods = tables.Where(t => t.Name == "mods").ToList();
            var ids = new List<string>();
            foreach (var mod in mods)
                AddId(ids, mod.Get("modId"));
            if (ids.Count == 0)
                return null;

            var first = mods.First(m => m.Get("modId") != null);
            string version = first.Get("version");
            if (version != null && version.Contains("${"))
                version = ManifestVersion(zip);

            string displayTest = first.Get("displayTest") ?? root?.Get("displayTest");
            bool clientOnly = string.Equals(root?.Get("clientSideOnly"), "true", StringComparison.OrdinalIgnoreCase);
            var side = clientOnly ? ModSide.Client
                : displayTest == "IGNORE_ALL_VERSION" || displayTest == "IGNORE_SERVER_VERSION" ? ModSide.Optional
                : ModSide.Both;
            return new Metadata(ids, first.Get("displayName"), version, side);
        }

        /// <summary>Forge before 1.13 kept a JSON list in mcmod.info, either bare or under "modList".</summary>
        private static Metadata ReadLegacyForge(ZipArchive zip)
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
                version = ManifestVersion(zip);
            return new Metadata(ids, Get(first, "name"), version, ModSide.Both);
        }

        private static string ManifestVersion(ZipArchive zip)
        {
            string manifest = ReadEntry(zip, "META-INF/MANIFEST.MF");
            if (manifest == null)
                return null;
            foreach (string line in manifest.Split('\n'))
            {
                const string key = "Implementation-Version:";
                if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(key.Length).Trim();
            }
            return null;
        }

        // ======================== Helpers ========================

        private static string ReadEntry(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            if (entry == null || entry.Length > MaxMetadataSize)
                return null;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                return reader.ReadToEnd();
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

        private sealed class Metadata
        {
            public Metadata(IReadOnlyList<string> ids, string name, string version, ModSide side)
            {
                Ids = ids;
                Name = name;
                Version = version;
                Side = side;
            }

            public IReadOnlyList<string> Ids { get; }

            public string Name { get; }

            public string Version { get; }

            public ModSide Side { get; }
        }
    }
}
