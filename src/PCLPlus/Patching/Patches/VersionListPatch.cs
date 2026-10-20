using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLPlus.Patching.Patches
{
    /// <summary>
    /// Restores the Minecraft version list on the "Download" page.
    /// </summary>
    /// <remarks>
    /// <c>PageDownloadInstall.LoaderInit</c> binds the page to a placeholder loader task that only throws.
    /// The patch binds it back to <c>ModDownload.DlClientListLoader</c>, the loader that fetches the version manifest.
    /// </remarks>
    internal sealed class VersionListPatch : Patch
    {
        private const string PageType = "PCL.PageDownloadInstall";
        private const string DownloadModuleType = "PCL.ModDownload";
        private const string VersionListLoaderField = "DlClientListLoader";
        private const string LoaderTaskType = "PCL.ModLoader/LoaderTask`2";

        public override string Title => "Minecraft 版本列表";

        public override string Description => "恢复“下载”页的游戏版本列表与安装功能";

        protected override MethodDef FindTarget(ModuleDef module)
        {
            return ModuleQuery.FindMethod(module, PageType, "LoaderInit", 0);
        }

        protected override PatchState GetState(ModuleDef module, MethodDef target)
        {
            var instructions = target.Body.Instructions;
            if (instructions.Any(i => ModuleQuery.IsLdstr(i, LauncherSignature.DisabledLoaderName)))
                return PatchState.Pending;
            if (instructions.Any(IsVersionListLoad))
                return PatchState.Applied;
            return PatchState.NotFound;
        }

        protected override void Apply(ModuleDef module, MethodDef target)
        {
            var instructions = target.Body.Instructions;

            // ldstr "关闭下载" ... newobj LoaderTask`2::.ctor  →  ldsfld ModDownload::DlClientListLoader
            int start = ModuleQuery.RequireIndex(instructions,
                i => ModuleQuery.IsLdstr(i, LauncherSignature.DisabledLoaderName),
                "占位加载器的名称");
            int end = ModuleQuery.RequireIndex(instructions, IsLoaderTaskConstruction,
                "占位加载器的构造", start);

            var loaderField = ModuleQuery.RequireType(module, DownloadModuleType).FindField(VersionListLoaderField)
                ?? throw new PatchException($"找不到字段 {DownloadModuleType}.{VersionListLoaderField}");

            IlEditor.ReplaceRange(target.Body, start, end, new[]
            {
                Instruction.Create(OpCodes.Ldsfld, loaderField),
            });
        }

        private static bool IsLoaderTaskConstruction(Instruction instruction)
        {
            return instruction.OpCode.Code == Code.Newobj
                && instruction.Operand is IMethod ctor
                && ctor.DeclaringType?.ScopeType?.FullName == LoaderTaskType;
        }

        private static bool IsVersionListLoad(Instruction instruction)
        {
            return instruction.OpCode.Code == Code.Ldsfld
                && instruction.Operand is IField field
                && field.Name == VersionListLoaderField
                && field.DeclaringType?.FullName == DownloadModuleType;
        }
    }
}
