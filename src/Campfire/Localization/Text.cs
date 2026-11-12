using System;
using System.Collections.Generic;
using System.Globalization;

namespace Campfire.Localization
{
    /// <summary>
    /// Localized user-facing strings. Tables live in <c>Text.Chinese.cs</c>, <c>Text.TraditionalChinese.cs</c>, <c>Text.Cantonese.cs</c> and <c>Text.English.cs</c>.
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

        /// <summary>Whether the display language is written in Chinese characters.</summary>
        public static bool IsChinese => _language != AppLanguage.English;

        /// <summary>Whether the display language uses Traditional characters.</summary>
        public static bool IsTraditional => _language == AppLanguage.TraditionalChinese || _language == AppLanguage.Cantonese;

        /// <summary>Traditional Chinese for Traditional-script system UI, Simplified Chinese for other Chinese system UI, English otherwise.</summary>
        public static AppLanguage DetectSystemLanguage()
        {
            var culture = CultureInfo.CurrentUICulture;
            if (culture.TwoLetterISOLanguageName == "yue")
                return AppLanguage.Cantonese;
            if (culture.TwoLetterISOLanguageName != "zh")
                return AppLanguage.English;
            string name = culture.Name;
            return name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0
                || name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.TraditionalChinese
                : AppLanguage.Chinese;
        }

        /// <summary>Returns the string for <paramref name="key"/>, falling back to Traditional Chinese, then Simplified Chinese, then the key itself.</summary>
        public static string Get(string key)
        {
            string value;
            switch (_language)
            {
                case AppLanguage.English:
                    if (English.TryGetValue(key, out value))
                        return value;
                    break;
                case AppLanguage.Cantonese:
                    if (Cantonese.TryGetValue(key, out value) || TraditionalChinese.TryGetValue(key, out value))
                        return value;
                    break;
                case AppLanguage.TraditionalChinese:
                    if (TraditionalChinese.TryGetValue(key, out value))
                        return value;
                    break;
            }
            return Chinese.TryGetValue(key, out value) ? value : key;
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
