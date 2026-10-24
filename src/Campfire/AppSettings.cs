using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Campfire.Localization;

namespace Campfire
{
    /// <summary>
    /// User preferences, stored as "key=value" lines in %LocalAppData%\Campfire\settings.ini.
    /// </summary>
    internal static class AppSettings
    {
        private const string LanguageKey = "language";

        private static readonly object Sync = new object();
        private static Dictionary<string, string> _values;

        /// <summary>Folder for everything Campfire keeps on disk.</summary>
        public static string DataDirectory { get; } = PrepareDataDirectory();

        /// <summary>Moves the data folder to its current name if it still has the old one.</summary>
        private static string PrepareDataDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string current = Path.Combine(root, "Campfire");
            string earlier = Path.Combine(root, "PCLPlus");
            try
            {
                if (!Directory.Exists(current) && Directory.Exists(earlier))
                    Directory.Move(earlier, current);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The old folder is in use; start fresh rather than fail.
            }
            return current;
        }

        /// <summary>Whether Campfire has never saved anything before, so this is the first time it runs.</summary>
        public static bool IsFirstRun { get; } = !File.Exists(Path.Combine(DataDirectory, "settings.ini"));

        private static string FilePath =>Path.Combine(DataDirectory, "settings.ini");

        /// <summary>The stored value, or <c>null</c> when the key has never been saved.</summary>
        public static string Get(string key)
        {
            lock (Sync)
            {
                EnsureLoaded();
                return _values.TryGetValue(key, out var value) ? value : null;
            }
        }

        /// <summary>Stores a value; <c>null</c> removes the key. Values are kept on a single line.</summary>
        public static void Set(string key, string value)
        {
            lock (Sync)
            {
                // Another Campfire may have saved since this one loaded, for example one left in the notification
                // area. Reading the file again keeps its changes instead of writing back an old copy.
                _values = null;
                EnsureLoaded();
                if (value == null)
                    _values.Remove(key);
                else
                    _values[key] = value.Replace("\r", " ").Replace("\n", " ");
                Save();
            }
        }

        /// <summary>The saved language, or <c>null</c> when the user has never chosen one.</summary>
        public static AppLanguage? LoadLanguage()
        {
            return ParseLanguage(Get(LanguageKey));
        }

        public static void SaveLanguage(AppLanguage language)
        {
            Set(LanguageKey, ToCode(language));
        }

        /// <summary>Parses "zh" / "en" (also accepts "zh-CN", "en-US" and similar).</summary>
        public static AppLanguage? ParseLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;
            if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return AppLanguage.Chinese;
            if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return AppLanguage.English;
            return null;
        }

        private static string ToCode(AppLanguage language)
        {
            return language == AppLanguage.English ? "en" : "zh";
        }

        private static void EnsureLoaded()
        {
            if (_values != null)
                return;

            _values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(FilePath))
                    return;

                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length == 2 && parts[0].Trim().Length > 0)
                        _values[parts[0].Trim()] = parts[1].Trim();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var lines = _values.Select(pair => pair.Key + "=" + pair.Value);
                File.WriteAllText(FilePath, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Preferences are a convenience; failing to save them is not an error.
            }
        }
    }
}
