using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Campfire.Worlds
{
    internal enum WorldSourceProblem
    {
        None,

        /// <summary>The file or folder does not exist or cannot be opened.</summary>
        Unreadable,

        /// <summary>Not a zip file and not a folder.</summary>
        NotSupported,

        /// <summary>There is no level.dat anywhere inside.</summary>
        NoWorld,

        /// <summary>Region files are there but level.dat is missing.</summary>
        Incomplete,

        /// <summary>A Bedrock Edition world, which Java Edition cannot open.</summary>
        Bedrock,

        /// <summary>level.dat exists but neither it nor its backup can be read.</summary>
        Damaged,
    }

    /// <summary>
    /// A map the user picked: a zip file as map sites hand them out, or a folder. Finds the worlds inside and
    /// reads their level.dat without unpacking anything.
    /// </summary>
    /// <remarks>
    /// Map archives usually wrap the world in a folder named after the map, and some carry leftovers such as an
    /// older copy of the world nested inside the real one. Every folder with a level.dat counts as a world unless
    /// it lies inside another world.
    /// </remarks>
    internal sealed class WorldSource
    {
        private const string LevelFile = "level.dat";
        private const string BackupFile = "level.dat_old";

        /// <summary>A level.dat is a few kilobytes; anything far bigger is not one.</summary>
        private const long MaxLevelFileSize = 8L * 1024 * 1024;

        /// <summary>How deep to look for worlds in a picked folder, which may be a saves folder or a whole download folder.</summary>
        private const int MaxFolderDepth = 4;

        private WorldSource(string path, bool isArchive)
        {
            Path = path;
            IsArchive = isArchive;
        }

        public string Path { get; }

        /// <summary>Whether <see cref="Path"/> is a zip file rather than a folder.</summary>
        public bool IsArchive { get; }

        public IReadOnlyList<WorldInfo> Worlds { get; private set; } = new WorldInfo[0];

        public WorldSourceProblem Problem { get; private set; }

        /// <summary>A name for a world when it sits at the root of the source, taken from the file or folder name.</summary>
        public string SourceName => IsArchive
            ? System.IO.Path.GetFileNameWithoutExtension(Path)
            : System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

        /// <summary>Opens and inspects <paramref name="path"/>. Never throws for bad input; see <see cref="Problem"/>.</summary>
        public static WorldSource Open(string path)
        {
            if (Directory.Exists(path))
            {
                var folder = new WorldSource(System.IO.Path.GetFullPath(path), false);
                try
                {
                    folder.Inspect(EnumerateFolder(folder.Path), name => File.ReadAllBytes(System.IO.Path.Combine(folder.Path, name)));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    folder.Problem = WorldSourceProblem.Unreadable;
                }
                return folder;
            }

            var source = new WorldSource(path, true);
            if (!File.Exists(path))
            {
                source.Problem = WorldSourceProblem.Unreadable;
                return source;
            }

            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in zip.Entries)
                    {
                        string name = Normalize(entry.FullName);
                        if (name.Length > 0 && !name.EndsWith("/", StringComparison.Ordinal))
                            entries[name] = entry;
                    }
                    source.Inspect(entries.Select(e => (e.Key, e.Value.Length)).ToList(), name => ReadEntry(entries[name]));
                }
            }
            catch (InvalidDataException)
            {
                // Not a zip.
                source.Problem = WorldSourceProblem.NotSupported;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                source.Problem = WorldSourceProblem.Unreadable;
            }
            return source;
        }

        /// <param name="files">Every file in the source as ('/'-separated path, size).</param>
        /// <param name="read">Reads one of those files.</param>
        private void Inspect(IReadOnlyCollection<(string Name, long Size)> files, Func<string, byte[]> read)
        {
            var sizes = files.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Size, StringComparer.OrdinalIgnoreCase);

            // Folders holding level.dat, or only its backup when the main file was lost.
            var folders = files.Where(f => IsFile(f.Name, LevelFile) || IsFile(f.Name, BackupFile))
                .Select(f => Parent(f.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f.Length)
                .ToList();
            var worlds = new List<string>();
            foreach (string folder in folders)
            {
                if (!worlds.Any(outer => IsInside(folder, outer)))
                    worlds.Add(folder);
            }

            if (worlds.Count == 0)
            {
                Problem = files.Any(f => f.Name.IndexOf("region/", StringComparison.OrdinalIgnoreCase) >= 0
                                         && f.Name.EndsWith(".mca", StringComparison.OrdinalIgnoreCase))
                    ? WorldSourceProblem.Incomplete
                    : WorldSourceProblem.NoWorld;
                return;
            }

            var result = new List<WorldInfo>();
            bool bedrock = false;
            foreach (string folder in worlds)
            {
                var info = ReadWorld(folder, sizes, read, ref bedrock);
                if (info != null)
                    result.Add(info);
            }

            Worlds = result;
            if (result.Count == 0)
                Problem = bedrock ? WorldSourceProblem.Bedrock : WorldSourceProblem.Damaged;
        }

        private static WorldInfo ReadWorld(string folder, Dictionary<string, long> sizes, Func<string, byte[]> read, ref bool bedrock)
        {
            string prefix = folder.Length == 0 ? "" : folder + "/";
            foreach (string file in new[] { LevelFile, BackupFile })
            {
                string name = prefix + file;
                if (!sizes.TryGetValue(name, out long size) || size <= 0 || size > MaxLevelFileSize)
                    continue;

                byte[] data;
                try
                {
                    data = read(name);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                if (IsBedrockLevel(data) || sizes.Keys.Any(k => k.StartsWith(prefix + "db/", StringComparison.OrdinalIgnoreCase)))
                {
                    bedrock = true;
                    return null;
                }

                try
                {
                    var root = Nbt.Read(data);
                    if (root.TryGetValue("Data", out object levelData) && levelData is Dictionary<string, object>)
                        return WorldInfo.FromLevelData(root, folder, file == BackupFile);
                }
                catch (InvalidDataException)
                {
                    // Damaged or cut short; try the backup next.
                }
            }
            return null;
        }

        /// <summary>
        /// Bedrock's level.dat is uncompressed little-endian NBT behind an 8-byte header: a format version and
        /// the length of the rest.
        /// </summary>
        private static bool IsBedrockLevel(byte[] data)
        {
            if (data.Length < 9 || (data[0] == 0x1F && data[1] == 0x8B))
                return false;
            int length = BitConverter.ToInt32(data, 4);
            return length == data.Length - 8 && data[8] == 10;
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var memory = new MemoryStream((int)Math.Min(entry.Length, MaxLevelFileSize)))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // The size in the zip directory is only a claim; stop if the data runs past it.
                    if (memory.Length + read > MaxLevelFileSize)
                        throw new InvalidDataException("Entry is larger than a level.dat can be.");
                    memory.Write(buffer, 0, read);
                }
                return memory.ToArray();
            }
        }

        private static List<(string Name, long Size)> EnumerateFolder(string root)
        {
            var files = new List<(string, long)>();
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0)
            {
                var (directory, depth) = pending.Pop();
                var info = new DirectoryInfo(directory);
                foreach (var file in info.EnumerateFiles())
                {
                    if (file.Name.Equals(LevelFile, StringComparison.OrdinalIgnoreCase)
                        || file.Name.Equals(BackupFile, StringComparison.OrdinalIgnoreCase)
                        || file.Extension.Equals(".mca", StringComparison.OrdinalIgnoreCase))
                        files.Add((Relative(root, file.FullName), file.Length));
                }

                // A world's own folders never hold another world worth importing, and they can be huge.
                bool isWorld = File.Exists(System.IO.Path.Combine(directory, LevelFile));
                foreach (var child in info.EnumerateDirectories())
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if (isWorld && !child.Name.Equals("region", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (depth < MaxFolderDepth || child.Name.Equals("region", StringComparison.OrdinalIgnoreCase))
                        pending.Push((child.FullName, depth + 1));
                }

                // A db folder marks a Bedrock world.
                if (Directory.Exists(System.IO.Path.Combine(directory, "db")))
                    files.Add((Relative(root, System.IO.Path.Combine(directory, "db", "CURRENT")), 0));
            }
            return files;
        }

        private static string Relative(string root, string path)
        {
            return Normalize(path.Substring(root.Length));
        }

        /// <summary>'/' separators, no leading or doubled slashes.</summary>
        internal static string Normalize(string name)
        {
            string normalized = name.Replace('\\', '/');
            while (normalized.Contains("//"))
                normalized = normalized.Replace("//", "/");
            return normalized.TrimStart('/');
        }

        private static bool IsFile(string name, string file)
        {
            return name.Equals(file, StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase);
        }

        private static string Parent(string name)
        {
            int slash = name.LastIndexOf('/');
            return slash < 0 ? "" : name.Substring(0, slash);
        }

        private static bool IsInside(string folder, string outer)
        {
            return outer.Length == 0 || folder.StartsWith(outer + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
