using System;
using System.Collections.Generic;
using System.Linq;

namespace Campfire.Mods
{
    internal enum ModDifference
    {
        /// <summary>The host has it and this computer does not.</summary>
        Missing,

        /// <summary>Both have it in different versions.</summary>
        Different,

        /// <summary>This computer has it and the host does not.</summary>
        Extra,
    }

    internal sealed class ModChange
    {
        public ModChange(ModDifference kind, ModInfo host, ModInfo local)
        {
            Kind = kind;
            Host = host;
            Local = local;
        }

        public ModDifference Kind { get; }

        /// <summary>The host's jar; <c>null</c> for <see cref="ModDifference.Extra"/>.</summary>
        public ModInfo Host { get; }

        /// <summary>The jar on this computer; <c>null</c> for <see cref="ModDifference.Missing"/>.</summary>
        public ModInfo Local { get; }

        public string Title => (Host ?? Local).Title;
    }

    /// <summary>
    /// Which shared mods differ between the host and a version on this computer. Mods that only one side needs,
    /// such as minimaps or shader loaders, are left out: they never keep a player from entering.
    /// </summary>
    internal sealed class ModComparison
    {
        private ModComparison(IReadOnlyList<ModChange> changes, int ignored)
        {
            Changes = changes;
            Ignored = ignored;
        }

        /// <summary>Missing mods first, then different versions, then extra ones, each by name.</summary>
        public IReadOnlyList<ModChange> Changes { get; }

        /// <summary>How many mods on either side were left out; only one side needs them.</summary>
        public int Ignored { get; }

        public bool IsMatch => Changes.Count == 0;

        public int Count(ModDifference kind) => Changes.Count(c => c.Kind == kind);

        public static ModComparison Compare(IReadOnlyList<ModInfo> host, IReadOnlyList<ModInfo> local)
        {
            var changes = new List<ModChange>();
            var matched = new HashSet<ModInfo>();
            int ignored = 0;

            foreach (var mod in host)
            {
                var mine = local.FirstOrDefault(l => !matched.Contains(l) && SameMod(l, mod));
                if (mine != null)
                    matched.Add(mine);
                if (!mod.IsShared)
                {
                    ignored++;
                    continue;
                }
                if (mine == null)
                    changes.Add(new ModChange(ModDifference.Missing, mod, null));
                else if (mine.Sha1 != mod.Sha1 && !SameVersion(mine, mod))
                    changes.Add(new ModChange(ModDifference.Different, mod, mine));
            }

            foreach (var mine in local.Where(l => !matched.Contains(l)))
            {
                if (mine.IsShared)
                    changes.Add(new ModChange(ModDifference.Extra, null, mine));
                else
                    ignored++;
            }

            var ordered = changes
                .OrderBy(c => c.Kind)
                .ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new ModComparison(ordered, ignored);
        }

        /// <summary>The same mod: one id in common, or for jars without metadata the same file.</summary>
        private static bool SameMod(ModInfo a, ModInfo b)
        {
            if (a.Sha1 == b.Sha1)
                return true;
            return a.Ids.Count > 0 && b.Ids.Count > 0 && a.Ids[0].Equals(b.Ids[0], StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Rebuilt jars of one release differ in their hash only; the version they declare is what the loaders compare.
        /// </summary>
        private static bool SameVersion(ModInfo a, ModInfo b)
        {
            return a.Version != null && b.Version != null && a.Version == b.Version && a.Ids.Count > 0;
        }
    }
}
