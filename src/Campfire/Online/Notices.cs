using System.Collections.Generic;
using System.Linq;
using Campfire.Localization;

namespace Campfire.Online
{
    /// <summary>
    /// Picks the notices meant for this Campfire and remembers which one-time notices were already shown.
    /// </summary>
    internal static class Notices
    {
        private const string SeenKey = "notices.seen";
        private const int MaxRemembered = 100;
        private const string PageScheme = "campfire://";

        /// <summary>Pages a notice button can open, written as campfire://name.</summary>
        public static readonly IReadOnlyCollection<string> Pages = new HashSet<string>
        {
            "home", "maps", "tools", "settings", "linksettings", "support", "about", "licenses", "lobby", "recent",
        };

        /// <summary>The page named by a campfire:// link, or <c>null</c> for anything else.</summary>
        public static string ParsePage(string url)
        {
            if (url == null || !url.StartsWith(PageScheme, System.StringComparison.OrdinalIgnoreCase))
                return null;
            string name = url.Substring(PageScheme.Length).Trim().TrimEnd('/').ToLowerInvariant();
            return Pages.Contains(name) ? name : null;
        }

        public static IEnumerable<Notice> Applicable(Manifest manifest)
        {
            if (manifest == null)
                return Enumerable.Empty<Notice>();
            var version = OnlineService.ShortVersion;
            string language = Text.IsChinese ? "zh" : "en";
            var seen = Seen();
            return manifest.Notices.Where(n =>
                (n.MinVersion == null || version >= n.MinVersion)
                && (n.MaxVersion == null || version <= n.MaxVersion)
                && (n.Language.Length == 0 || n.Language == language)
                && (n.Repeats || !seen.Contains(n.Id)));
        }

        public static void MarkSeen(Notice notice)
        {
            if (notice.Repeats)
                return;
            var seen = Seen();
            if (seen.Contains(notice.Id))
                return;
            seen.Add(notice.Id);
            AppSettings.Set(SeenKey, string.Join(",", seen.Skip(seen.Count - MaxRemembered)));
        }

        private static List<string> Seen()
        {
            return (AppSettings.Get(SeenKey) ?? "").Split(',').Where(s => s.Length > 0).ToList();
        }
    }
}
