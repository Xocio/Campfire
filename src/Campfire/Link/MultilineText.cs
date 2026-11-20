using System.Linq;
using System.Text;

namespace Campfire.Link
{
    /// <summary>Short texts written by the host that may span a few lines, such as the announcement and the room description.</summary>
    internal static class MultilineText
    {
        /// <summary>The most lines such a text keeps.</summary>
        public const int MaxLines = 4;

        /// <summary>
        /// Line breaks as "\n", other control characters as spaces, each line trimmed, empty lines dropped,
        /// at most <see cref="MaxLines"/> lines, cut to <paramref name="limit"/> characters.
        /// </summary>
        public static string Clean(string text, int limit = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(OneLine)
                .Where(line => line.Length > 0)
                .Take(MaxLines);
            string result = string.Join("\n", lines);
            return result.Length > limit ? result.Substring(0, limit).TrimEnd() : result;
        }

        /// <summary>The text on one line, its lines joined by spaces.</summary>
        public static string Flatten(string text)
        {
            return Clean(text).Replace('\n', ' ');
        }

        private static string OneLine(string line)
        {
            var clean = new StringBuilder(line.Length);
            foreach (char c in line)
                clean.Append(char.IsControl(c) ? ' ' : c);
            return clean.ToString().Trim();
        }
    }
}
