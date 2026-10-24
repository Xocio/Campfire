using System;
using System.Collections.Generic;
using System.Globalization;

namespace Campfire.Localization
{
    /// <summary>
    /// Localized user-facing strings. Tables live in <c>Text.Chinese.cs</c> and <c>Text.English.cs</c>.
    /// </summary>
    public static partial class Text
    {
        private static AppLanguage _language = DetectSystemLanguage();

        /// <summary>Raised after <see cref="Language"/> changes.</summary>
        public static event EventHandler LanguageChanged;

        public static AppLanguage Language
        {
            get => _language;
            set
            {
                if (_language == value)
                    return;
                _language = value;
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>Chinese for Chinese system UI, English otherwise.</summary>
        public static AppLanguage DetectSystemLanguage()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
                ? AppLanguage.Chinese
                : AppLanguage.English;
        }

        /// <summary>Returns the string for <paramref name="key"/>, falling back to Chinese, then to the key itself.</summary>
        public static string Get(string key)
        {
            var table = _language == AppLanguage.English ? English : Chinese;
            if (table.TryGetValue(key, out var value) || Chinese.TryGetValue(key, out value))
                return value;
            return key;
        }

        /// <summary>Whether a string exists for <paramref name="key"/>.</summary>
        public static bool Has(string key)
        {
            return Chinese.ContainsKey(key);
        }

        public static string Format(string key, params object[] args)
        {
            return args == null || args.Length == 0
                ? Get(key)
                : string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }

        private static Dictionary<string, string> Table(params string[] pairs)
        {
            var table = new Dictionary<string, string>(pairs.Length / 2, StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                table.Add(pairs[i], pairs[i + 1]);
            return table;
        }
    }
}
