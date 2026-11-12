using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Campfire.Localization;

namespace Campfire
{
    /// <summary>
    /// User preferences, stored as "key=value" lines in settings.ini in the data folder.
    /// </summary>
    internal static class AppSettings
    {
        private const string LanguageKey = "language";

        private static readonly object Sync = new object();
        private static Dictionary<string, string> _values;

        /// <summary>Folder for everything Campfire keeps on disk.</summary>
        public static string DataDirectory { get; private set; } = DataLocation.Resolve();

        /// <summary>Whether Campfire has never saved anything before.</summary>
        public static bool IsFirstRun { get; private set; } = !DataLocation.IsRecorded && !DataLocation.HasData(DataDirectory);

        private static string FilePath =>Path.Combine(DataDirectory, "settings.ini");

        /// <summary>Switches to the folder chosen on the first page, before anything else reads the data.</summary>
        public static void UseDataDirectory(string folder)
        {
            lock (Sync)
            {
                DataDirectory = folder;
                IsFirstRun = !DataLocation.HasData(folder);
                _values = null;
            }
        }

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
                // Reads the file again before writing.
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

        /// <summary>Parses "zh" / "zh-Hant" / "yue" / "en" (also accepts "zh-CN", "zh-TW", "zh-HK", "en-US" and similar).</summary>
        public static AppLanguage? ParseLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;
            if (code.IndexOf("yue", StringComparison.OrdinalIgnoreCase) >= 0)
                return AppLanguage.Cantonese;
            if (code.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0
                || code.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase))
                return AppLanguage.TraditionalChinese;
            if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return AppLanguage.Chinese;
            if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return AppLanguage.English;
            return null;
        }

        private static string ToCode(AppLanguage language)
        {
            switch (language)
            {
                case AppLanguage.English:
                    return "en";
                case AppLanguage.TraditionalChinese:
                    return "zh-Hant";
                case AppLanguage.Cantonese:
                    return "yue";
                default:
                    return "zh";
            }
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
                // A failed save is ignored.
            }
        }
    }
}
