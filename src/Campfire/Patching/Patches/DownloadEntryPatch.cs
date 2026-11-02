using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Campfire.Patching.Patches
{
    /// <summary>
    /// Restores the "Install" entry on the "Download" page's left navigation.
    /// </summary>
    /// <remarks>
    /// <c>PageDownloadLeft..ctor</c> ends with a block that hides the "Install" item and opens the Mod sub-page.
    /// The patch cuts the constructor back to the <c>InitializeComponent</c> call followed by <c>ret</c>.
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
