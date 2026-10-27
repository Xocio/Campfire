using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace Campfire.Worlds
{
    /// <summary>
    /// Copies a world from a <see cref="WorldSource"/> into a game's saves folder.
    /// </summary>
    /// <remarks>
    /// The world is written to a temporary folder first and renamed when complete, so the game's world list never
    /// shows half a world, and an existing world is never overwritten: a free name is picked instead. Another
    /// world nested inside the world, usually an old copy the map maker forgot, is left out; the game never reads it.
    /// </remarks>
    internal static class WorldImporter
    {
        private const string TemporarySuffix = ".campfire.tmp";

        /// <summary>Longest folder name to create; the game itself keeps world folder names short as well.</summary>
        private const int MaxNameLength = 64;

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>
        /// Copies <paramref name="world"/> into <paramref name="savesDirectory"/> and returns the new world folder.
        /// </summary>
        /// <param name="progress">Receives the share of bytes copied, from 0 to 1.</param>
        public static string Import(WorldSource source, WorldInfo world, string savesDirectory,
            IProgress<double> progress = null, CancellationToken cancellation = default)
        {
            Directory.CreateDirectory(savesDirectory);
            string name = FolderName(source, world);
            string temporary = Path.Combine(savesDirectory, name + TemporarySuffix);
            DeleteQuietly(temporary);
            Directory.CreateDirectory(temporary);

            try
            {
                if (source.IsArchive)
                    CopyFromArchive(source.Path, world.Directory, temporary, progress, cancellation);
                else
                    CopyFromFolder(Path.Combine(source.Path, world.Directory.Replace('/', Path.DirectorySeparatorChar)), temporary, progress, cancellation);

                string target = FreePath(savesDirectory, name);
                Directory.Move(temporary, target);
                progress?.Report(1);
                return target;
            }
            catch
            {
                DeleteQuietly(temporary);
                throw;
            }
        }

        /// <summary>
        /// Moves an imported world into another saves folder, keeping its name when free, and returns the new
        /// folder. Used when the running game turns out to keep its worlds elsewhere than assumed.
        /// </summary>
        public static string Move(string worldDirectory, string savesDirectory)
        {
            Directory.CreateDirectory(savesDirectory);
            string target = FreePath(savesDirectory, Path.GetFileName(worldDirectory));
            if (string.Equals(Path.GetPathRoot(Path.GetFullPath(worldDirectory)), Path.GetPathRoot(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(worldDirectory, target);
                return target;
            }

            // Another drive: copy, then remove the original once the copy is complete.
            string temporary = target + TemporarySuffix;
            DeleteQuietly(temporary);
            try
            {
                CopyFromFolder(worldDirectory, temporary, null, CancellationToken.None);
                Directory.Move(temporary, target);
            }
            catch
            {
                DeleteQuietly(temporary);
                throw;
            }
            DeleteQuietly(worldDirectory);
            return target;
        }

        /// <summary>The world folder name: the folder it came in, else the archive name, else the world's own name.</summary>
        internal static string FolderName(WorldSource source, WorldInfo world)
        {
            string name = world.Directory.Length > 0
                ? world.Directory.Substring(world.Directory.LastIndexOf('/') + 1)
                : source.SourceName;
            name = Sanitize(name);
            if (name.Length == 0)
                name = Sanitize(world.LevelName ?? "");
            return name.Length == 0 ? "World" : name;
        }

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
            string result = new string(chars).Trim().TrimEnd('.').Trim();
            if (result.Length > MaxNameLength)
                result = result.Substring(0, MaxNameLength).TrimEnd('.', ' ');
            if (ReservedNames.Contains(result))
                result += "_";
            return result;
        }

        /// <summary>"Name", or "Name (2)", "Name (3)" … when taken.</summary>
        private static string FreePath(string directory, string name)
        {
            string path = Path.Combine(directory, name);
            for (int i = 2; Directory.Exists(path) || File.Exists(path); i++)
                path = Path.Combine(directory, name + " (" + i + ")");
            return path;
        }

        private static void CopyFromArchive(string archive, string worldDirectory, string target,
            IProgress<double> progress, CancellationToken cancellation)
        {
            string prefix = worldDirectory.Length == 0 ? "" : worldDirectory + "/";
            string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;

            using (var zip = ZipFile.OpenRead(archive))
            {
                var names = zip.Entries.Select(e => WorldSource.Normalize(e.FullName)).ToList();
                var nested = names
                    .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.EndsWith("/level.dat", StringComparison.OrdinalIgnoreCase))
                    .Select(n => n.Substring(prefix.Length, n.Length - prefix.Length - "level.dat".Length))
                    .Where(n => n.Length > 0)
                    .ToList();

                var entries = new List<(ZipArchiveEntry Entry, string Path)>();
                for (int i = 0; i < names.Count; i++)
                {
                    var entry = zip.Entries[i];
                    string name = names[i];
                    if (name.EndsWith("/", StringComparison.Ordinal) || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string relative = name.Substring(prefix.Length);
                    if (Skip(relative) || nested.Any(n => relative.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Entry names come from the archive; refuse any that would land outside the world folder.
                    string path;
                    try
                    {
                        path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                    {
                        continue;
                    }
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    entries.Add((entry, path));
                }

                long total = Math.Max(1, entries.Sum(e => e.Entry.Length));
                EnsureSpace(target, total);
                long done = 0;
                var buffer = new byte[81920];
                foreach (var (entry, path) in entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open())
                    using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        int read;
                        long written = 0;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            // The free space was checked against the sizes the archive declares; hold it to them.
                            written += read;
                            if (written > entry.Length)
                                throw new InvalidDataException("An entry of the archive holds more than it declares.");
                            output.Write(buffer, 0, read);
                        }
                    }
                    File.SetLastWriteTime(path, entry.LastWriteTime.LocalDateTime);
                    done += entry.Length;
                    progress?.Report((double)done / total);
                }
            }
        }

        private static void CopyFromFolder(string source, string target, IProgress<double> progress, CancellationToken cancellation)
        {
            var files = new List<FileInfo>();
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(source));
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var child in directory.EnumerateDirectories())
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) == 0 && !File.Exists(Path.Combine(child.FullName, "level.dat")))
                        pending.Push(child);
                }
                files.AddRange(directory.EnumerateFiles());
            }

            int rootLength = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar).Length + 1;
            files.RemoveAll(f => Skip(f.FullName.Substring(rootLength).Replace(Path.DirectorySeparatorChar, '/')));
            long total = Math.Max(1, files.Sum(f => f.Length));
            EnsureSpace(target, total);
            long done = 0;
            foreach (var file in files)
            {
                cancellation.ThrowIfCancellationRequested();
                string relative = file.FullName.Substring(rootLength);
                string path = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                file.CopyTo(path, false);
                done += file.Length;
                progress?.Report((double)done / total);
            }
        }

        /// <summary>Refuses to start a copy that would fill the drive; the game needs room to save the world as well.</summary>
        private static void EnsureSpace(string target, long needed)
        {
            const long Reserve = 256L * 1024 * 1024;
            DriveInfo drive;
            try
            {
                drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target)));
                if (!drive.IsReady)
                    return;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // A network share; Windows reports a full share when it happens.
                return;
            }
            if (drive.AvailableFreeSpace < needed + Reserve)
                throw new DiskFullException(needed + Reserve);
        }

        /// <summary>
        /// session.lock belongs to the game that last had the world open, and the game writes a fresh one.
        /// level*.dat with digits are temporary files the game failed to clean up.
        /// </summary>
        private static bool Skip(string relative)
        {
            if (relative.Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                return true;
            if (relative.IndexOf('/') < 0 && relative.StartsWith("level", StringComparison.OrdinalIgnoreCase)
                && relative.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                string middle = relative.Substring(5, relative.Length - 9);
                return middle.Length > 0 && middle.All(char.IsDigit);
            }
            return false;
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The drive has too little free space for the world.</summary>
    internal sealed class DiskFullException : IOException
    {
        public DiskFullException(long needed) : base("Not enough free space.")
        {
            Needed = needed;
        }

        /// <summary>Bytes the copy needs, including room left for the game.</summary>
        public long Needed { get; }
    }
}
