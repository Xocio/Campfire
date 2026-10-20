using System;
using System.IO;
using PCLPlus.Localization;

namespace PCLPlus
{
    /// <summary>
    /// User preferences, stored in %LocalAppData%\PCLPlus\settings.ini.
    /// </summary>
    internal static class AppSettings
    {
        private const string LanguageKey = "language";

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCLPlus", "settings.ini");

        /// <summary>The saved language, or <c>null</c> when the user has never chosen one.</summary>
        public static AppLanguage? LoadLanguage()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length == 2 && parts[0].Trim() == LanguageKey)
                        return ParseLanguage(parts[1].Trim());
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return null;
        }

        public static void SaveLanguage(AppLanguage language)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, $"{LanguageKey}={ToCode(language)}\r\n");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Preferences are a convenience; failing to save them is not an error.
            }
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
    }
}
