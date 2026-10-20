using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using PCLPlus.Patching;

namespace PCLPlus.Launcher
{
    /// <summary>
    /// Determines whether a file is the official launcher and whether its download feature is disabled.
    /// </summary>
    internal static class LauncherInspector
    {
        public static LauncherReport Inspect(string path)
        {
            if (!File.Exists(path))
                return NotLauncher(path, "Inspect.FileMissing");

            ModuleDefMD module;
            try
            {
                module = LoadModule(File.ReadAllBytes(path));
            }
            catch (BadImageFormatException)
            {
                return NotLauncher(path, "Inspect.NotOfficial");
            }
            catch (IOException ex)
            {
                return NotLauncher(path, "Inspect.ReadFailed", ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return NotLauncher(path, "Inspect.AccessDenied");
            }

            using (module)
                return Inspect(path, module);
        }

        public static LauncherReport Inspect(string path, ModuleDefMD module)
        {
            if (module.Assembly == null || module.Assembly.Name != LauncherSignature.AssemblyName)
                return NotLauncher(path, "Inspect.NotOfficial");

            string version = module.Assembly.Version.ToString();
            var patches = PatchCatalog.All.Select(p => new PatchReport(p, p.GetState(module))).ToList();
            var required = patches.Where(p => p.IsRequired).ToList();
            bool hasMarker = module.Resources.Any(r => r.Name == LauncherSignature.PatchMarkerResource);

            // Older releases differ in layout, so a patch point that is missing only matters when something is disabled.
            LauncherStatus status;
            string messageKey;
            if (required.All(p => p.State != PatchState.Pending))
            {
                status = hasMarker ? LauncherStatus.Patched : LauncherStatus.NotBlocked;
                messageKey = hasMarker ? "Inspect.Patched" : "Inspect.NotBlocked";
            }
            else if (required.All(p => p.State != PatchState.NotFound))
            {
                status = LauncherStatus.Blocked;
                messageKey = "Inspect.Blocked";
            }
            else
            {
                status = LauncherStatus.Unsupported;
                messageKey = "Inspect.Unsupported";
            }

            return new LauncherReport(path, version, status, patches, messageKey);
        }

        internal static ModuleDefMD LoadModule(byte[] data)
        {
            return ModuleDefMD.Load(data, new ModuleCreationOptions { TryToLoadPdbFromDisk = false });
        }

        private static LauncherReport NotLauncher(string path, string messageKey, params object[] args)
        {
            return new LauncherReport(path, null, LauncherStatus.NotLauncher, Array.Empty<PatchReport>(), messageKey, args);
        }
    }
}
