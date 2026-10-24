using System;
using System.IO;
using dnlib.DotNet;
using Campfire.Patching;
using Campfire.Patching.Patches;

namespace Campfire.Launcher
{
    /// <summary>
    /// Inspects and fixes PCL Community Edition, which ships as a .NET single-file bundle.
    /// </summary>
    /// <remarks>
    /// The community edition keeps its download code; version 2.15.1 gates it behind a region check
    /// in the bundled <c>PCL.Core</c> assembly. The fix rewrites that method body in place, so the
    /// file keeps its size and layout.
    /// </remarks>
    internal static class CommunityLauncher
    {
        /// <summary>Returns whether <paramref name="path"/> is a community launcher bundle.</summary>
        public static bool IsCommunityLauncher(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > LauncherFile.MaxBundleSize)
                    return false;
                return BundleScanner.ContainsAssembly(File.ReadAllBytes(path), RegionLimitPatch.CoreAssemblyName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Inspects a community launcher, reporting <c>false</c> when the file is not one.</summary>
        public static bool TryInspect(string path, out LauncherReport report)
        {
            report = null;
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }

            return TryInspect(path, data, out report);
        }

        /// <summary>Applies the region patch to the bundled <c>PCL.Core</c> assembly.</summary>
        public static LauncherReport Fix(string path)
        {
            LauncherFile.EnsureNotRunning(path);
            byte[] data = LauncherFile.Read(path);

            byte[] patched;
            using (var image = BundleScanner.FindAssembly(data, RegionLimitPatch.CoreAssemblyName))
            {
                if (image == null)
                    throw new LauncherException("Inspect.NotOfficial");

                var patch = new RegionLimitPatch();
                var report = BuildReport(path, image);
                if (report.Status != LauncherStatus.Blocked)
                    return report; // already patched, or a release that never had the region gate

                patched = ApplyRegionPatch(data, image, patch);
            }

            // Re-read the patched bytes before touching the file, so a failed patch never replaces the launcher.
            LauncherReport verified;
            if (!TryInspect(path, patched, out verified) || verified.Status != LauncherStatus.Patched)
                throw new LauncherException("Fix.VerifyFailed");

            LauncherFile.Replace(path, LauncherFixer.GetBackupPath(path), patched);
            return verified;
        }

        private static byte[] ApplyRegionPatch(byte[] data, BundleImage image, RegionLimitPatch patch)
        {
            MethodDef target = patch.LocateTarget(image.Module);
            int bodyOffset = BundleScanner.MapRvaToOffset(data, image.Start, (int)target.RVA);
            var body = BundleScanner.LocateBody(data, bodyOffset);

            byte[] il = RegionLimitPatch.BypassIl;
            if (il.Length > body.CodeSize)
                throw new LauncherException("Fix.PatchFailed", patch.Title, "the method body is too small to patch");

            var patched = (byte[])data.Clone();
            int bodySize = body.HeaderSize + body.CodeSize;

            // Rewrite the body as a tiny header + "ldc.i4.0; ret". The original fat header declared
            // locals that the new code no longer uses, and the runtime rejects that mismatch, so the
            // header has to shrink as well. The bytes left over from the old body stay as padding and
            // are no longer part of any method; every other metadata offset is unaffected.
            patched[bodyOffset] = (byte)((il.Length << 2) | 0x02);
            Buffer.BlockCopy(il, 0, patched, bodyOffset + 1, il.Length);
            for (int i = 1 + il.Length; i < bodySize; i++)
                patched[bodyOffset + i] = 0x00;

            return patched;
        }

        private static bool TryInspect(string path, byte[] data, out LauncherReport report)
        {
            report = null;
            using (var image = BundleScanner.FindAssembly(data, RegionLimitPatch.CoreAssemblyName))
            {
                if (image == null)
                    return false;

                report = BuildReport(path, image);
                return true;
            }
        }

        private static LauncherReport BuildReport(string path, BundleImage image)
        {
            var patch = new RegionLimitPatch();
            var state = patch.GetState(image.Module);

            LauncherStatus status;
            string messageKey;
            switch (state)
            {
                case PatchState.Applied:
                    status = LauncherStatus.Patched;
                    messageKey = "Inspect.CommunityPatched";
                    break;
                case PatchState.Pending:
                    status = LauncherStatus.Blocked;
                    messageKey = "Inspect.CommunityBlocked";
                    break;
                default:
                    // Releases before 2.15.1 never added the region gate, so their downloads already work.
                    bool hasRegionGate = image.Module.Find("PCL.Core.App.Region.RegionAssessment", false) != null;
                    status = hasRegionGate ? LauncherStatus.Unsupported : LauncherStatus.NotBlocked;
                    messageKey = hasRegionGate ? "Inspect.Unsupported" : "Inspect.NotBlocked";
                    break;
            }

            var report = new LauncherReport(path, LauncherFile.ReadBundleVersion(path, image), status,
                new[] { new PatchReport(patch, state) }, messageKey);
            report.Kind = LauncherKind.Community;
            return report;
        }
    }
}
