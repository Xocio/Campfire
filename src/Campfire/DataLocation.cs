using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Campfire
{
    /// <summary>
    /// Where Campfire keeps its data. A chosen folder is recorded in a small file in the default folder, which stays
    /// in place wherever the data lives.
    /// </summary>
    internal static class DataLocation
    {
        private const string PointerName = "data-location.txt";
        private const string PathPrefix = "path=";
        private const string CleanupPrefix = "cleanup=";
        private const string FolderName = "Campfire";
        private const string SettingsName = "settings.ini";
        private const int MaxPathLength = 160;

        /// <summary>The top-level files and folders Campfire creates in its data folder.</summary>
        private static readonly string[] OwnedNames =
        {
            "EasyTier", "Link", "backups", "downloads", "mappings", "update", SettingsName, "stats-id", "agent.log", "crash.log",
        };

        public static string DefaultDirectory { get; } = PrepareDefaultDirectory();

        /// <summary>The chosen folder when it could not be reached at startup; otherwise <c>null</c>.</summary>
        public static string Unavailable { get; private set; }

        private static string PointerPath => Path.Combine(DefaultDirectory, PointerName);

        /// <summary>Whether a location has ever been recorded.</summary>
        public static bool IsRecorded => File.Exists(PointerPath);

        /// <summary>Moves the data folder to its current name if it still has the old one.</summary>
        private static string PrepareDefaultDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string current = Path.Combine(root, FolderName);
            string earlier = Path.Combine(root, "PCLPlus");
            try
            {
                if (!Directory.Exists(current) && Directory.Exists(earlier))
                    Directory.Move(earlier, current);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The old folder is in use; a new one is started.
            }
            return current;
        }

        /// <summary>The folder to use now: the recorded one, or the default one when none is recorded or it is out of reach.</summary>
        public static string Resolve()
        {
            string saved = ReadPointer(out _);
            if (string.IsNullOrEmpty(saved))
                return DefaultDirectory;
            if (Directory.Exists(saved))
                return saved;
            Unavailable = saved;
            return DefaultDirectory;
        }

        /// <summary>Records <paramref name="target"/>; <paramref name="cleanup"/> is the folder to clear at the next start.</summary>
        public static void Record(string target, string cleanup)
        {
            Directory.CreateDirectory(DefaultDirectory);
            string path = SamePath(target, DefaultDirectory) ? "" : target;
            var lines = new List<string> { PathPrefix + path };
            if (cleanup != null)
                lines.Add(CleanupPrefix + cleanup);
            File.WriteAllLines(PointerPath, lines, new UTF8Encoding(false));
        }

        private static string ReadPointer(out string cleanup)
        {
            cleanup = null;
            string path = null;
            try
            {
                if (!File.Exists(PointerPath))
                    return null;
                foreach (string line in File.ReadAllLines(PointerPath, Encoding.UTF8))
                {
                    if (line.StartsWith(PathPrefix, StringComparison.Ordinal))
                        path = line.Substring(PathPrefix.Length).Trim();
                    else if (line.StartsWith(CleanupPrefix, StringComparison.Ordinal))
                        cleanup = line.Substring(CleanupPrefix.Length).Trim();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
            return path;
        }

        /// <summary>The data folder for a folder the user picked: itself when named Campfire, otherwise a Campfire folder inside it.</summary>
        public static string ToDataFolder(string picked)
        {
            string full = Path.GetFullPath(picked).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(Path.GetFileName(full), FolderName, StringComparison.OrdinalIgnoreCase)
                ? full
                : Path.Combine(full + Path.DirectorySeparatorChar, FolderName);
        }

        public static bool SamePath(string first, string second)
        {
            return string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        /// <summary>Whether the folder already holds Campfire data.</summary>
        public static bool HasData(string folder)
        {
            return File.Exists(Path.Combine(folder, SettingsName));
        }

        /// <summary>Free space on the folder's drive, or <c>null</c> when it cannot be read.</summary>
        public static long? FreeSpace(string folder)
        {
            try
            {
                return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The string key of why <paramref name="target"/> cannot hold the data, or <c>null</c> when it can.</summary>
        public static string Check(string target, string current)
        {
            if (target.Length > MaxPathLength)
                return "Storage.Error.TooLong";
            if (!IsLocalDrive(target))
                return "Storage.Error.Network";
            if (IsSystemFolder(target))
                return "Storage.Error.System";

            string normalizedTarget = Normalize(target), normalizedCurrent = Normalize(current);
            if (normalizedTarget != normalizedCurrent
                && (normalizedTarget.StartsWith(normalizedCurrent, StringComparison.OrdinalIgnoreCase)
                    || normalizedCurrent.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase)))
                return "Storage.Error.Nested";

            bool existed = Directory.Exists(target);
            try
            {
                Directory.CreateDirectory(target);
                string probe = Path.Combine(target, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "Storage.Error.NotWritable";
            }

            if (existed && !HasData(target) && !SamePath(target, DefaultDirectory) && Directory.EnumerateFileSystemEntries(target).Any())
                return "Storage.Error.NotEmpty";
            return null;
        }

        private static bool IsLocalDrive(string folder)
        {
            string root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return false;
            try
            {
                var type = new DriveInfo(root).DriveType;
                return type == DriveType.Fixed || type == DriveType.Removable;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException)
            {
                return false;
            }
        }

        private static bool IsSystemFolder(string folder)
        {
            string normalized = Normalize(folder);
            return new[]
            {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            }.Select(Environment.GetFolderPath)
             .Where(p => p.Length > 0)
             .Any(p => normalized.StartsWith(Normalize(p), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The files to carry over, relative to <paramref name="source"/>, with the settings file last.</summary>
        private static List<string> ListFiles(string source)
        {
            string prefix = Normalize(source);
            return Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(prefix.Length))
                .Where(IsCarried)
                .OrderBy(f => string.Equals(f, SettingsName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();
        }

        /// <summary>Leaves out the record of the location and what only matters during a room.</summary>
        private static bool IsCarried(string relative)
        {
            string name = Path.GetFileName(relative);
            string link = "Link" + Path.DirectorySeparatorChar;
            if (string.Equals(relative, PointerName, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!relative.StartsWith(link, StringComparison.OrdinalIgnoreCase))
                return true;
            return !relative.StartsWith(link + "runtime" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("overlay-", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("lan-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The size of everything that would be carried over, in bytes.</summary>
        public static long Measure(string source)
        {
            return ListFiles(source).Sum(f => new FileInfo(Path.Combine(source, f)).Length);
        }

        /// <summary>Copies the data to <paramref name="target"/>. On failure the copies are removed and the error is passed on.</summary>
        public static void Copy(string source, string target, IProgress<int> percent)
        {
            var files = ListFiles(source);
            long total = Math.Max(1, files.Sum(f => new FileInfo(Path.Combine(source, f)).Length));
            long done = 0;
            var copied = new List<string>();
            try
            {
                foreach (string file in files)
                {
                    string from = Path.Combine(source, file), to = Path.Combine(target, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(from, to, true);
                    copied.Add(to);
                    done += new FileInfo(from).Length;
                    percent?.Report((int)(done * 100 / total));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                foreach (string file in copied)
                    TryDelete(file);
                TryDeleteEmptyFolders(target);
                throw;
            }
        }

        /// <summary>Clears the folder the data was moved out of, then forgets it.</summary>
        public static void FinishMove()
        {
            string saved = ReadPointer(out string cleanup);
            if (string.IsNullOrEmpty(cleanup) || Unavailable != null)
                return;

            if (!SamePath(cleanup, Resolve()))
            {
                foreach (string name in OwnedNames)
                {
                    string entry = Path.Combine(cleanup, name);
                    try
                    {
                        if (Directory.Exists(entry))
                            Directory.Delete(entry, true);
                        else
                            File.Delete(entry);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // What is left behind stays unused.
                    }
                }
                if (!SamePath(cleanup, DefaultDirectory))
                    TryDeleteEmptyFolder(cleanup);
            }

            try
            {
                if (string.IsNullOrEmpty(saved))
                    File.Delete(PointerPath);
                else
                    Record(saved, null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Tried again at the next start.
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Removes the empty folders below <paramref name="folder"/>.</summary>
        private static void TryDeleteEmptyFolders(string folder)
        {
            try
            {
                foreach (string child in Directory.GetDirectories(folder))
                {
                    TryDeleteEmptyFolders(child);
                    TryDeleteEmptyFolder(child);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteEmptyFolder(string folder)
        {
            try
            {
                Directory.Delete(folder, false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
