using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Campfire.Patching.Patches
{
    /// <summary>
    /// Lifts the region lock that PCL Community Edition 2.15.1 added to Minecraft downloads.
    /// </summary>
    /// <remarks>
    /// <c>RegionAssessment.IsRestricted</c> is the single decision point behind every guarded entry:
    /// the download list, client install, repair, reset, loader install and modpack install all ask
    /// <c>RestrictedFeaturePolicy</c>, which only reads this property. Replacing its body with
    /// <c>ldc.i4.0; ret</c> keeps every other region-related behaviour (messages, configuration) intact.
    /// </remarks>
    internal sealed class RegionLimitPatch : Patch
    {
        /// <summary>Assembly that owns the region policy inside the bundled launcher.</summary>
        public const string CoreAssemblyName = "PCL.Core";

        private const string AssessmentType = "PCL.Core.App.Region.RegionAssessment";
        private const string RestrictionProperty = "get_IsRestricted";
        private const string ClassificationGetter = "get_Classification";

        protected override string TextKey => "RegionLimit";

        /// <summary>The instructions written over the property body: <c>return false;</c></summary>
        public static byte[] BypassIl => new[]
        {
            (byte)OpCodes.Ldc_I4_0.Value,
            (byte)OpCodes.Ret.Value,
        };

        /// <summary>Locates the patched method for callers that write the body in place.</summary>
        public MethodDef LocateTarget(ModuleDef module)
        {
            return FindTarget(module);
        }

        protected override MethodDef FindTarget(ModuleDef module)
        {
            return ModuleQuery.FindMethod(module, AssessmentType, RestrictionProperty, 0);
        }

        protected override PatchState GetState(ModuleDef module, MethodDef target)
        {
            var instructions = target.Body.Instructions;

            // The fix rewrites the body as "ldc.i4.0; ret"; older in-place attempts padded with nops,
            // so trailing nops are tolerated when reading the state back.
            var meaningful = instructions.Where(i => i.OpCode.Code != Code.Nop).ToList();
            if (meaningful.Count == 2
                && meaningful[0].OpCode.Code == Code.Ldc_I4_0
                && meaningful[1].OpCode.Code == Code.Ret)
            {
                return PatchState.Applied;
            }

            bool computesClassification = instructions.Any(i =>
                i.OpCode.Code == Code.Call
                && i.Operand is IMethod method
                && method.Name == ClassificationGetter
                && method.DeclaringType?.FullName == AssessmentType);

            return computesClassification ? PatchState.Pending : PatchState.NotFound;
        }

        protected override void Apply(ModuleDef module, MethodDef target)
        {
            IlEditor.ReplaceBody(target.Body, new[]
            {
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Ret),
            });
        }
    }
}
