using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Campfire.Localization;

namespace Campfire.Patching
{
    /// <summary>
    /// A single modification of one method in the launcher assembly.
    /// </summary>
    internal abstract class Patch
    {
        /// <summary>Identifier used for the localized title and description ("Patch.{TextKey}.Title").</summary>
        protected abstract string TextKey { get; }

        /// <summary>Short title shown in the UI.</summary>
        public string Title => Text.Get($"Patch.{TextKey}.Title");

        /// <summary>One-sentence explanation shown in the UI.</summary>
        public string Description => Text.Get($"Patch.{TextKey}.Description");

        /// <summary>State text shown once the patch has been applied by Campfire.</summary>
        public virtual string AppliedText => Text.Get("PatchState.Restored");

        /// <summary>
        /// Whether this patch is required to restore the download feature.
        /// Launchers that miss a required patch point are reported as unsupported.
        /// </summary>
        public virtual bool IsRequired => true;

        /// <summary>Locates the method this patch modifies, or returns <c>null</c>.</summary>
        protected abstract MethodDef FindTarget(ModuleDef module);

        /// <summary>Inspects <paramref name="target"/> and reports whether the patch is still needed.</summary>
        protected abstract PatchState GetState(ModuleDef module, MethodDef target);

        /// <summary>Rewrites <paramref name="target"/>. Branches are in their long form while this runs.</summary>
        protected abstract void Apply(ModuleDef module, MethodDef target);

        public PatchState GetState(ModuleDef module)
        {
            var target = FindTarget(module);
            return target == null ? PatchState.NotFound : GetState(module, target);
        }

        public void Apply(ModuleDef module)
        {
            var target = FindTarget(module)
                ?? throw new PatchException($"patch point of {TextKey} not found");
            if (GetState(module, target) != PatchState.Pending)
                throw new PatchException($"{TextKey} is not pending");

            var body = target.Body;
            body.SimplifyBranches();
            body.SimplifyMacros(target.Parameters);
            Apply(module, target);
            body.OptimizeMacros();
            body.OptimizeBranches();
        }
    }
}
