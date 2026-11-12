using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Campfire.Mods
{
    /// <summary>
    /// Tells whether a version falls in a dependency's range. Only plain numeric versions such as "1.20.1" or
    /// "0.92.0+1.20.1" are compared; for anything else the answer is unknown.
    /// </summary>
    internal static class VersionRange
    {
        /// <summary>
        /// <c>true</c> or <c>false</c> when <paramref name="version"/> is clearly inside or outside every reading of
        /// <paramref name="dependency"/>'s ranges, <c>null</c> when that cannot be told.
        /// </summary>
        public static bool? Matches(ModDependency dependency, string version)
        {
            if (dependency.Ranges.Count == 0)
                return true;
            var parsed = Parse(version);
            if (parsed == null)
                return null;

            bool unknown = false;
            foreach (string range in dependency.Ranges)
            {
                bool? result = dependency.Scheme == RangeScheme.Maven ? MatchesMaven(range, parsed) : MatchesPredicate(range, parsed);
                if (result == true)
                    return true;
                if (result == null)
                    unknown = true;
            }
            return unknown ? (bool?)null : false;
        }

        // ======================== Versions ========================

        /// <summary>
        /// The numeric parts of a version, with build metadata after "+" dropped; <c>null</c> for versions with letters,
        /// pre-release parts or wildcards.
        /// </summary>
        internal static int[] Parse(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            string text = version.Trim();
            int plus = text.IndexOf('+');
            if (plus >= 0)
                text = text.Substring(0, plus);
            if (text.Length == 0)
                return null;
            var parts = text.Split('.');
            var numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 9 || !parts[i].All(c => c >= '0' && c <= '9'))
                    return null;
                numbers[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            }
            return numbers;
        }

        /// <summary>Compares part by part; missing parts count as 0.</summary>
        internal static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y)
                    return x.CompareTo(y);
            }
            return 0;
        }

        // ======================== Fabric and Quilt ========================

        /// <summary>
        /// A predicate is one or more terms separated by spaces, all of which must hold. A term is a version with an
        /// optional operator: =, &gt;, &gt;=, &lt;, &lt;=, ~ (same minor version) or ^ (same major version); a version
        /// may end in .x or .* for any value there.
        /// </summary>
        private static bool? MatchesPredicate(string predicate, int[] version)
        {
            var terms = predicate.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return true;
            bool unknown = false;
            foreach (string term in terms)
            {
                bool? result = MatchesTerm(term, version);
                if (result == false)
                    return false;
                if (result == null)
                    unknown = true;
            }
            return unknown ? (bool?)null : true;
        }

        private static bool? MatchesTerm(string term, int[] version)
        {
            if (term == "*")
                return true;
            string op = "";
            foreach (string candidate in new[] { ">=", "<=", ">", "<", "=", "~", "^" })
            {
                if (term.StartsWith(candidate, StringComparison.Ordinal))
                {
                    op = candidate;
                    break;
                }
            }
            string rest = term.Substring(op.Length);

            // A trailing wildcard: "1.20.x" is any 1.20 version.
            var pieces = rest.Split('.');
            int wildcard = Array.FindIndex(pieces, p => p == "x" || p == "X" || p == "*");
            if (wildcard >= 0)
            {
                if (pieces.Skip(wildcard).Any(p => p != "x" && p != "X" && p != "*") || (op != "" && op != "="))
                    return null;
                var prefix = Parse(string.Join(".", pieces.Take(wildcard)));
                if (wildcard == 0)
                    return true;
                if (prefix == null)
                    return null;
                for (int i = 0; i < prefix.Length; i++)
                {
                    if ((i < version.Length ? version[i] : 0) != prefix[i])
                        return false;
                }
                return true;
            }

            var bound = Parse(rest);
            if (bound == null)
                return null;
            int compare = Compare(version, bound);
            switch (op)
            {
                case "":
                case "=":
                    return compare == 0;
                case ">=":
                    return compare >= 0;
                case "<=":
                    return compare <= 0;
                case ">":
                    return compare > 0;
                case "<":
                    return compare < 0;
                case "~":
                    // Same major and minor version, at least the given one.
                    return compare >= 0 && Part(version, 0) == Part(bound, 0) && Part(version, 1) == Part(bound, 1);
                case "^":
                    if (compare < 0)
                        return false;
                    if (Part(version, 0) != Part(bound, 0))
                        return false;
                    // With major version 0 the minor version may also have to match; both readings must agree.
                    if (Part(bound, 0) == 0 && Part(version, 1) != Part(bound, 1))
                        return null;
                    return true;
                default:
                    return null;
            }
        }

        private static int Part(int[] version, int index) => index < version.Length ? version[index] : 0;

        // ======================== Forge and NeoForge ========================

        /// <summary>
        /// A range such as "[1.20,1.21)", "[47,)", "(,2.0]" or "[1.0]"; several ranges joined by commas mean any of
        /// them. A bare version only recommends that version and accepts any.
        /// </summary>
        private static bool? MatchesMaven(string range, int[] version)
        {
            string text = range.Replace(" ", "");
            if (text.Length == 0 || text == "*")
                return true;
            if (text[0] != '[' && text[0] != '(')
                return true;

            var sets = new List<string>();
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '[' || c == '(')
                    start = i;
                else if ((c == ']' || c == ')') && start >= 0)
                {
                    sets.Add(text.Substring(start, i - start + 1));
                    start = -1;
                }
            }
            if (sets.Count == 0)
                return null;

            bool unknown = false;
            foreach (string set in sets)
            {
                bool? result = MatchesMavenSet(set, version);
                if (result == true)
                    return true;
                if (result == null)
                    unknown = true;
            }
            return unknown ? (bool?)null : false;
        }

        private static bool? MatchesMavenSet(string set, int[] version)
        {
            bool lowInclusive = set[0] == '[';
            bool highInclusive = set[set.Length - 1] == ']';
            string inner = set.Substring(1, set.Length - 2);
            int comma = inner.IndexOf(',');
            if (comma < 0)
            {
                var exact = Parse(inner);
                return exact == null ? (bool?)null : Compare(version, exact) == 0;
            }

            string low = inner.Substring(0, comma);
            string high = inner.Substring(comma + 1);
            if (low.Length > 0)
            {
                var bound = Parse(low);
                if (bound == null)
                    return null;
                int compare = Compare(version, bound);
                if (compare < 0 || (compare == 0 && !lowInclusive))
                    return false;
            }
            if (high.Length > 0)
            {
                var bound = Parse(high);
                if (bound == null)
                    return null;
                int compare = Compare(version, bound);
                if (compare > 0 || (compare == 0 && !highInclusive))
                    return false;
            }
            return true;
        }
    }
}
