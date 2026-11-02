using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Campfire.Link.Minecraft;
using Campfire.Worlds;

namespace Campfire.Link
{
    internal enum BackupState
    {
        /// <summary>No backup was made or asked for.</summary>
        None,
        Working,
        Done,

        /// <summary>The world could not be found, or it is too large.</summary>
        Skipped,
        Failed,
    }

    internal sealed class BackupResult
    {
        public BackupResult(BackupState state, string worldName = null, string path = null)
        {
            State = state;
            WorldName = worldName;
            Path = path;
        }

        public BackupState State { get; }

        /// <summary>Folder name of the world that was copied.</summary>
        public string WorldName { get; }

        public string Path { get; }
    }

    /// <summary>One backup file of a world.</summary>
    internal sealed class BackupEntry
    {
        public BackupEntry(string path, DateTime time, long size)
        {
            Path = path;
            Time = time;
            Size = size;
        }

        public string Path { get; }

        /// <summary>When the backup was made, in local time.</summary>
        public DateTime Time { get; }

        public long Size { get; }
    }

    /// <summary>
    /// Copies the world the host plays into a zip file before guests come in.
    /// </summary>
    internal static class WorldBackup
    {
        /// <summary>Worlds above this size are not backed up.</summary>
        private const long MaxBytes = 2L * 1024 * 1024 * 1024;

        /// <summary>Backups kept for each world; older ones are deleted.</summary>
        public const int KeepPerWorld = 5;

        private const string TimeFormat = "yyyyMMdd-HHmmss";

        /// <summary>The backups of the world in folder <paramref name="worldName"/>, newest first.</summary>
        public static IReadOnlyList<BackupEntry> List(string worldName)
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    return Array.Empty<BackupEntry>();
                string prefix = SafeName(worldName) + "_";
                return new DirectoryInfo(Directory).GetFiles(prefix + "*.zip")
                    .Where(f => f.Name.Length == prefix.Length + TimeFormat.Length + 4)
                    .Select(f => new BackupEntry(f.FullName, ParseTime(f, prefix), f.Length))
                    .OrderByDescending(b => b.Time)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<BackupEntry>();
            }
        }

        /// <summary>The time in the file name, or the time the file was written when the name does not tell.</summary>
        private static DateTime ParseTime(FileInfo file, string prefix)
        {
            string stamp = file.Name.Substring(prefix.Length, TimeFormat.Length);
            return DateTime.TryParseExact(stamp, TimeFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var time)
                ? time
                : file.LastWriteTime;
        }

        public static string Directory => System.IO.Path.Combine(AppSettings.DataDirectory, "backups", "worlds");

        /// <summary>Backs up the world that the game listening on <paramref name="gamePort"/> has open.</summary>
        public static BackupResult Run(int gamePort, CancellationToken cancellation)
        {
            var game = RunningGame.FromProcess(LocalWorldFinder.FindProcess(gamePort));
            // A dedicated server has no --gameDir and is not backed up.
            if (game == null || !System.IO.Directory.Exists(game.SavesDirectory))
                return new BackupResult(BackupState.Skipped);

            string world = FindOpenWorld(game.SavesDirectory);
            if (world == null)
                return new BackupResult(BackupState.Skipped);
            string name = System.IO.Path.GetFileName(world);

            var files = new List<string>();
            long size = 0;
            foreach (string file in System.IO.Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories))
            {
                cancellation.ThrowIfCancellationRequested();
                if (IsLock(file))
                    continue;
                files.Add(file);
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
                if (size > MaxBytes)
                {
                    LinkLog.Info($"The world {name} is larger than the backup limit; no backup was made.");
                    return new BackupResult(BackupState.Skipped, name);
                }
            }

            System.IO.Directory.CreateDirectory(Directory);
            string target = System.IO.Path.Combine(Directory, SafeName(name) + "_" + DateTime.Now.ToString(TimeFormat) + ".zip");
            string partial = target + ".tmp";
            try
            {
                using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (string file in files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        Add(archive, world, name, file);
                    }
                }
                File.Move(partial, target);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }

            Prune(name);
            LinkLog.Info($"Backed up the world {name} ({files.Count} files).");
            return new BackupResult(BackupState.Done, name, target);
        }

        /// <summary>The folder name of the world the game listening on <paramref name="gamePort"/> has open, or <c>null</c>.</summary>
        public static string FindWorldName(int gamePort)
        {
            try
            {
                var game = RunningGame.FromProcess(LocalWorldFinder.FindProcess(gamePort));
                if (game == null || !System.IO.Directory.Exists(game.SavesDirectory))
                    return null;
                string world = FindOpenWorld(game.SavesDirectory);
                return world == null ? null : System.IO.Path.GetFileName(world);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The world folder whose session.lock was written last: the one the running game holds.</summary>
        private static string FindOpenWorld(string saves)
        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string folder in System.IO.Directory.EnumerateDirectories(saves))
            {
                string level = System.IO.Path.Combine(folder, "level.dat");
                string session = System.IO.Path.Combine(folder, "session.lock");
                if (!File.Exists(level) || !File.Exists(session))
                    continue;
                DateTime time = File.GetLastWriteTimeUtc(session);
                if (time > bestTime)
                {
                    bestTime = time;
                    best = folder;
                }
            }
            return best;
        }

        private static void Add(ZipArchive archive, string world, string name, string file)
        {
            string relative = file.Substring(world.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            try
            {
                // Opened with shared access.
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var entry = archive.CreateEntry(name + "/" + relative.Replace('\\', '/'), CompressionLevel.Fastest);
                    var written = File.GetLastWriteTime(file);
                    // Zip dates start in 1980.
                    if (written.Year >= 1980 && written.Year <= 2107)
                        entry.LastWriteTime = written;
                    using (var output = entry.Open())
                        input.CopyTo(output);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // A file the game is rewriting is skipped.
                LinkLog.Warn($"A file was skipped in the world backup: {relative} ({ex.Message})");
            }
        }

        private static bool IsLock(string file)
        {
            return string.Equals(System.IO.Path.GetFileName(file), "session.lock", StringComparison.OrdinalIgnoreCase);
        }

        private static void Prune(string name)
        {
            try
            {
                var old = new DirectoryInfo(Directory).GetFiles(SafeName(name) + "_*.zip")
                    .OrderByDescending(f => f.Name)
                    .Skip(KeepPerWorld);
                foreach (var file in old)
                    TryDelete(file.FullName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static string SafeName(string name)
        {
            foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            return name;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
