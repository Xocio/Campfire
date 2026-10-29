using System;
using System.Collections.Generic;
using System.Text;

namespace Campfire.Mods
{
    /// <summary>
    /// Just enough TOML for mods.toml: tables, arrays of tables and "key = value" lines with strings or bare values.
    /// Multi-line strings (descriptions) are skipped; values that are not strings are kept as their raw text.
    /// </summary>
    internal static class MiniToml
    {
        public sealed class Table
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

            public Table(string name)
            {
                Name = name;
            }

            /// <summary>The header without brackets, such as "mods"; "" for the keys before the first header.</summary>
            public string Name { get; }

            public string Get(string key)
            {
                return _values.TryGetValue(key, out string value) ? value : null;
            }

            internal void Set(string key, string value)
            {
                _values[key] = value;
            }
        }

        public static List<Table> Parse(string text)
        {
            var tables = new List<Table> { new Table("") };
            var current = tables[0];
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line[0] == '[')
                {
                    string header = StripComment(line).Trim().Trim('[', ']').Trim();
                    current = new Table(header);
                    tables.Add(current);
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;
                string key = line.Substring(0, equals).Trim().Trim('"', '\'');
                string rest = line.Substring(equals + 1).Trim();

                if (rest.StartsWith("\"\"\"", StringComparison.Ordinal) || rest.StartsWith("'''", StringComparison.Ordinal))
                {
                    // Skip to the closing quotes, which may be on this line or a later one.
                    string quotes = rest.Substring(0, 3);
                    string after = rest.Substring(3);
                    while (after.IndexOf(quotes, StringComparison.Ordinal) < 0 && i + 1 < lines.Length)
                        after = lines[++i];
                    continue;
                }

                string value = ReadValue(rest);
                if (value != null)
                    current.Set(key, value);
            }
            return tables;
        }

        private static string ReadValue(string text)
        {
            if (text.Length == 0)
                return null;
            if (text[0] == '\'')
            {
                int end = text.IndexOf('\'', 1);
                return end < 0 ? null : text.Substring(1, end - 1);
            }
            if (text[0] != '"')
                return StripComment(text).Trim();

            var builder = new StringBuilder();
            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                    return builder.ToString();
                if (c == '\\' && i + 1 < text.Length)
                {
                    char next = text[++i];
                    switch (next)
                    {
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u' when i + 4 < text.Length && TryHex(text.Substring(i + 1, 4), out char unicode):
                            builder.Append(unicode);
                            i += 4;
                            break;
                        default: builder.Append(next); break;
                    }
                    continue;
                }
                builder.Append(c);
            }
            return null;
        }

        private static bool TryHex(string text, out char value)
        {
            bool ok = int.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code);
            value = ok ? (char)code : '\0';
            return ok;
        }

        private static string StripComment(string text)
        {
            int hash = text.IndexOf('#');
            return hash < 0 ? text : text.Substring(0, hash);
        }
    }
}
