using System.Collections.Generic;
using Campfire.Patching.Patches;

namespace Campfire.Patching
{
    /// <summary>
    /// All patches Campfire applies, in application order.
    /// </summary>
    internal static class PatchCatalog
    {
        public static IReadOnlyList<Patch> All { get; } = new Patch[]
        {
            new DownloadEntryPatch(),
            new VersionListPatch(),
            new ModpackPickerPatch(),
            new ModpackDispatchPatch(),
            new UpdateBlockPatch(),
        };
    }
}
