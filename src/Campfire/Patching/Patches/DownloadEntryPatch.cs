using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Campfire.Patching.Patches
{
    /// <summary>
    /// Restores the "Install" entry on the "Download" page's left navigation.
    /// </summary>
    /// <remarks>
    /// <c>PageDownloadLeft..ctor</c> gained a tail block, added after 2.99.0.0, that hides the
    /// "Install" item, forces the page to the Mod sub-page and checks the Mod item instead. The
    /// version list and installer this uncovers still work once <see cref="VersionListPatch"/> is
    /// applied, but the entry that leads to them is hidden, so downloads stay unreachable even on a
    /// fully patched assembly. The patch truncates the constructor back to just the call to
    /// <c>InitializeComponent</c> followed by <c>ret</c>, as it was before the tail block existed.
    /// </remarks>
    internal sealed class DownloadEntryPatch : Patch
    {
        private const string PageType = "PCL.PageDownloadLeft";
        private const string InitializeComponentMethod = "InitializeComponent";
        private const string HiddenEntryProperty = "get_ItemInstall";

        protected override string TextKey => "DownloadEntry";

        protected override MethodDef FindTarget(ModuleDef module)
        {
            return ModuleQuery.FindMethod(module, PageType, ".ctor", 0);
        }

        protected override PatchState GetState(ModuleDef module, MethodDef target)
        {
            var instructions = target.Body.Instructions;
            if (instructions.Any(i => ModuleQuery.IsCallTo(i, PageType, HiddenEntryProperty)))
                return PatchState.Pending;

            int initIndex = ModuleQuery.IndexOf(instructions, i => ModuleQuery.IsCallTo(i, PageType, InitializeComponentMethod));
            bool endsRightAfterInit = initIndex >= 0
                && initIndex == instructions.Count - 2
                && instructions[instructions.Count - 1].OpCode.Code == Code.Ret;
            return endsRightAfterInit ? PatchState.Applied : PatchState.NotFound;
        }

        protected override void Apply(ModuleDef module, MethodDef target)
        {
            var instructions = target.Body.Instructions;

            int initIndex = ModuleQuery.RequireIndex(instructions,
                i => ModuleQuery.IsCallTo(i, PageType, InitializeComponentMethod),
                "call to InitializeComponent");
            int lastIndex = instructions.Count - 1;
            if (instructions[lastIndex].OpCode.Code != Code.Ret)
                throw new PatchException("constructor does not end in ret");

            // Drop every instruction after the InitializeComponent call, keeping a single ret.
            IlEditor.ReplaceRange(target.Body, initIndex + 1, lastIndex, new[]
            {
                Instruction.Create(OpCodes.Ret),
            });
        }
    }
}
