using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Campfire.Localization;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>How the map pages write numbers, dates and lists.</summary>
    internal static class MapText
    {
        public static string Count(long value)
        {
            var culture = CultureInfo.InvariantCulture;
            if (Text.Language == AppLanguage.Chinese)
            {
                if (value >= 100_000_000)
                    return (value / 100_000_000d).ToString("0.#", culture) + " 亿";
                if (value >= 10_000)
                    return (value / 10_000d).ToString("0.#", culture) + " 万";
                return value.ToString(culture);
            }
            if (value >= 1_000_000)
                return (value / 1_000_000d).ToString("0.#", culture) + "M";
            if (value >= 1_000)
                return (value / 1_000d).ToString("0.#", culture) + "K";
            return value.ToString(culture);
        }

        public static string Downloads(long value) => Text.Format("Maps.Downloads", Count(value));

        public static string Date(DateTime? date) => date?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static string Size(long bytes)
        {
            var culture = CultureInfo.InvariantCulture;
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / (1024d * 1024 * 1024)).ToString("0.##", culture) + " GB";
            if (bytes >= 1024 * 1024)
                return (bytes / (1024d * 1024)).ToString("0.#", culture) + " MB";
            return Math.Max(1, bytes / 1024).ToString(culture) + " KB";
        }

        /// <summary>The first few versions; maps often list many patch releases of the same version.</summary>
        public static string Versions(IReadOnlyList<string> versions, int shown)
        {
            if (versions == null || versions.Count == 0)
                return null;
            string text = string.Join(", ", versions.Take(shown));
            return versions.Count > shown ? Text.Format("Maps.VersionsMore", text) : text;
        }

        /// <summary>Categories carry English names; the known ones have their own translation.</summary>
        public static string Category(MapCategory category)
        {
            string key = "Maps.Category." + category.Slug;
            return Text.Has(key) ? Text.Get(key) : category.Name;
        }

        public static string Meta(MapSummary map)
        {
            var parts = new List<string>();
            if (map.Authors.Count > 0)
                parts.Add(map.Authors[0]);
            parts.Add(Downloads(map.Downloads));
            if (Versions(map.Versions, 2) is string versions)
                parts.Add(versions);
            if (Date(map.Updated) is string updated)
                parts.Add(Text.Format("Maps.UpdatedOn", updated));
            return string.Join("  ·  ", parts);
        }
    }
}
