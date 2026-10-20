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
                return NotLauncher(path, "文件不存在。");

            ModuleDefMD module;
            try
            {
                module = LoadModule(File.ReadAllBytes(path));
            }
            catch (BadImageFormatException)
            {
                return NotLauncher(path, "这不是 PCL 正式版。社区版等其他启动器的下载功能可以正常使用，无需修复。");
            }
            catch (IOException ex)
            {
                return NotLauncher(path, "无法读取文件：" + ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return NotLauncher(path, "没有读取该文件的权限。");
            }

            using (module)
                return Inspect(path, module);
        }

        public static LauncherReport Inspect(string path, ModuleDefMD module)
        {
            if (module.Assembly == null || module.Assembly.Name != LauncherSignature.AssemblyName)
                return NotLauncher(path, "这不是 PCL 正式版。社区版等其他启动器的下载功能可以正常使用，无需修复。");

            string version = module.Assembly.Version.ToString();
            var patches = PatchCatalog.All.Select(p => new PatchReport(p, p.GetState(module))).ToList();
            var required = patches.Where(p => p.IsRequired).ToList();
            bool hasMarker = module.Resources.Any(r => r.Name == LauncherSignature.PatchMarkerResource);

            // Older releases differ in layout, so a patch point that is missing only matters when something is disabled.
            LauncherStatus status;
            string message;
            if (required.All(p => p.State != PatchState.Pending))
            {
                status = hasMarker ? LauncherStatus.Patched : LauncherStatus.NotBlocked;
                message = hasMarker
                    ? "下载功能已由 PCL++ 恢复，可以正常使用。"
                    : "此版本的下载功能正常，无需修复。";
            }
            else if (required.All(p => p.State != PatchState.NotFound))
            {
                status = LauncherStatus.Blocked;
                message = "此版本已关闭下载功能，可以一键修复。";
            }
            else
            {
                status = LauncherStatus.Unsupported;
                message = "此版本已关闭下载功能，但代码结构与已知版本不同，暂不支持修复。";
            }

            return new LauncherReport(path, version, status, message, patches);
        }

        internal static ModuleDefMD LoadModule(byte[] data)
        {
            return ModuleDefMD.Load(data, new ModuleCreationOptions { TryToLoadPdbFromDisk = false });
        }

        private static LauncherReport NotLauncher(string path, string message)
        {
            return new LauncherReport(path, null, LauncherStatus.NotLauncher, message, Array.Empty<PatchReport>());
        }
    }
}
