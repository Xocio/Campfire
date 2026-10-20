using System.Collections.Generic;
using PCLPlus.Patching.Patches;

namespace PCLPlus.Patching
{
    /// <summary>
    /// All patches PCL++ applies, in application order.
    /// </summary>
    internal static class PatchCatalog
    {
        public static IReadOnlyList<Patch> All { get; } = new Patch[]
        {
            new VersionListPatch(),
            new ModpackPickerPatch(),
            new ModpackDispatchPatch(),
            new UpdateBlockPatch(),
        };
    }
}
