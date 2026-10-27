using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.Win32;

namespace Campfire.Worlds
{
    internal enum GameFolderSource
    {
        /// <summary>%AppData%\.minecraft, which most launchers use unless told otherwise.</summary>
        Default,

        /// <summary>The .minecraft folder next to a launcher.</summary>
        NextToLauncher,

        /// <summary>A folder listed in a launcher's settings.</summary>
        LauncherSettings,

        /// <summary>A folder the user added in Campfire.</summary>
        Added,
    }

    internal sealed class GameFolder
    {
        public GameFolder(string path, GameFolderSource source)
        {
            Path = path;
            Source = source;
        }

        /// <summary>Full path of the .minecraft folder, without a trailing separator.</summary>
        public string Path { get; }

        public GameFolderSource Source { get; }

        public override string ToString() => Path;
    }

    /// <summary>
    /// Finds the .minecraft folders on this computer while no game is running, so a map can be imported before
    /// the game starts.
    /// </summary>
    internal static class GameFolders
    {
        private const string AddedKey = "worlds.folders";

        /// <summary>
        /// Every folder with installed versions: the default one, those next to the given launchers, those the
        /// launchers' settings list and those the user added. Folders without a versions folder are left out.
        /// </summary>
        /// <param name="launcherDirectories">Folders that hold a launcher, such as the one Campfire found to repair.</param>
        public static IReadOnlyList<GameFolder> Find(IEnumerable<string> launcherDirectories)
        {
            var found = new List<GameFolder>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string path, GameFolderSource source)
            {
                string full = Clean(path);
                if (full != null && seen.Add(full) && Directory.Exists(System.IO.Path.Combine(full, "versions")))
                    found.Add(new GameFolder(full, source));
            }

            Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"),
                GameFolderSource.Default);

            foreach (string directory in (launcherDirectories ?? Enumerable.Empty<string>()).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Add(System.IO.Path.Combine(directory, ".minecraft"), GameFolderSource.NextToLauncher);
                foreach (string folder in ReadLocalLauncherSettings(directory))
                    Add(folder, GameFolderSource.LauncherSettings);
            }

            foreach (string folder in ReadRegistryFolders())
                Add(folder, GameFolderSource.LauncherSettings);

            foreach (string folder in Added)
                Add(folder, GameFolderSource.Added);

            return found;
        }

        /// <summary>Folders the user added by hand, kept across runs.</summary>
        public static IReadOnlyList<string> Added =>
            (AppSettings.Get(AddedKey) ?? "").Split('|').Where(p => p.Length > 0).ToList();

        public static void Add(string path)
        {
            string full = Clean(path);
            if (full == null || Added.Contains(full, StringComparer.OrdinalIgnoreCase))
                return;
            AppSettings.Set(AddedKey, string.Join("|", Added.Concat(new[] { full })));
        }

        public static void Remove(string path)
        {
            string full = Clean(path);
            var rest = Added.Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase)).ToList();
            AppSettings.Set(AddedKey, rest.Count == 0 ? null : string.Join("|", rest));
        }

        /// <summary>
        /// A folder the user picked may be .minecraft itself, the launcher folder next to it or a version folder
        /// inside it; returns the .minecraft folder in each case, or <c>null</c>.
        /// </summary>
        public static string Resolve(string picked)
        {
            string full = Clean(picked);
            if (full == null)
                return null;
            if (Directory.Exists(System.IO.Path.Combine(full, "versions")))
                return full;
            string inside = System.IO.Path.Combine(full, ".minecraft");
            if (Directory.Exists(System.IO.Path.Combine(inside, "versions")))
                return inside;
            // ...\.minecraft\versions or ...\.minecraft\versions\<name>
            string current = full;
            for (int level = 0; level < 2 && current != null; level++, current = System.IO.Path.GetDirectoryName(current))
            {
                string parent = System.IO.Path.GetDirectoryName(current);
                if (parent != null && string.Equals(System.IO.Path.GetFileName(current), "versions", StringComparison.OrdinalIgnoreCase))
                    return parent;
            }
            return null;
        }

        /// <summary>
        /// The launcher keeps the selected folder in a text file beside it: "PCL\config.v1.yml" with
        /// "Key: value" lines, or "PCL\Setup.ini" with "Key:value" lines, depending on the build.
        /// </summary>
        private static IEnumerable<string> ReadLocalLauncherSettings(string directory)
        {
            foreach (string file in new[] { "config.v1.yml", "Setup.ini" })
            {
                string path = System.IO.Path.Combine(directory, "PCL", file);
                foreach (string line in ReadLines(path))
                {
                    string value = ValueOf(line, "LaunchFolderSelect");
                    if (value != null)
                        yield return value;
                    value = ValueOf(line, "LaunchFolders");
                    if (value != null)
                    {
                        foreach (string folder in SplitFolderList(value))
                            yield return folder;
                    }
                }
            }
        }

        /// <summary>The folder list the launcher keeps in the registry.</summary>
        private static IEnumerable<string> ReadRegistryFolders()
        {
            string value;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\PCL"))
                    value = key?.GetValue("LaunchFolders") as string;
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                value = null;
            }
            return SplitFolderList(value);
        }

        /// <summary>
        /// Folder lists are "name>path" items separated by '|'. Items without a name are plain paths; anything
        /// that is not an existing folder is ignored later.
        /// </summary>
        private static IEnumerable<string> SplitFolderList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield break;
            foreach (string item in value.Split('|'))
            {
                string path = item.Contains(">") ? item.Substring(item.IndexOf('>') + 1) : item;
                if (path.Trim().Length > 0)
                    yield return path.Trim();
            }
        }

        private static string ValueOf(string line, string key)
        {
            string trimmed = line.TrimStart('﻿', ' ');
            if (!trimmed.StartsWith(key + ":", StringComparison.Ordinal))
                return null;
            string value = trimmed.Substring(key.Length + 1).Trim().Trim('"', '\'');
            return value.Length == 0 ? null : value;
        }

        private static IEnumerable<string> ReadLines(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        private static string Clean(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
                if (!System.IO.Path.IsPathRooted(expanded))
                    return null;
                string full = System.IO.Path.GetFullPath(expanded);
                // "C:\" must keep its separator; "C:" alone means the current folder of drive C.
                return full.Length > System.IO.Path.GetPathRoot(full).Length
                    ? full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    : full;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is SecurityException)
            {
                return null;
            }
        }
    }
}
