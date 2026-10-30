using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Campfire.Localization;

namespace Campfire.Patching.Patches
{
    /// <summary>
    /// Stops the launcher from replacing itself with an update that disables the download feature again.
    /// </summary>
    /// <remarks>
    /// Every update path, including updates pushed through server notices, ends in <c>ModSecret.UpdateStart</c>.
    /// The patch prepends:
    /// <code>
    /// if (!silent) ModMain.Hint(Message, HintType.Blue, true);
    /// return;
    /// </code>
    /// </remarks>
    internal sealed class UpdateBlockPatch : Patch
    {
        private const string SecretType = "PCL.ModSecret";
        private const string MethodName = "UpdateStart";
        private const int HintTypeBlue = 0;

        /// <summary>Shown inside PCL; not localized.</summary>
        internal const string Message = "Campfire 已停用启动器自动更新。如需更新，请手动下载新版本后重新运行 Campfire。";

        /// <summary>Notices from earlier builds; launchers carrying one count as repaired.</summary>
        private static readonly string[] EarlierMessages =
        {
            "PCL++ 已停用启动器自动更新。如需更新，请手动下载新版本后重新运行 PCL++。",
        };

        protected override string TextKey => "UpdateBlock";

        public override string AppliedText => Text.Get("PatchState.Disabled");

        public override bool IsRequired => false;

        protected override MethodDef FindTarget(ModuleDef module)
        {
            return ModuleQuery.FindMethod(module, SecretType, MethodName, 2);
        }

        protected override PatchState GetState(ModuleDef module, MethodDef target)
        {
            bool applied = target.Body.Instructions.Any(i => ModuleQuery.IsLdstr(i, Message)
                                                             || EarlierMessages.Any(m => ModuleQuery.IsLdstr(i, m)));
            return applied ? PatchState.Applied : PatchState.Pending;
        }

        protected override void Apply(ModuleDef module, MethodDef target)
        {
            var hint = ModuleQuery.RequireMethod(module, "PCL.ModMain", "Hint", 3);
            var silent = target.Parameters[0];

            var ret = Instruction.Create(OpCodes.Ret);
            IlEditor.Prepend(target.Body, new[]
            {
                Instruction.Create(OpCodes.Ldarg, silent),
                Instruction.Create(OpCodes.Brtrue, ret),
                Instruction.Create(OpCodes.Ldstr, Message),
                Instruction.Create(OpCodes.Ldc_I4, HintTypeBlue),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Call, hint),
                ret,
            });
        }
    }
}
