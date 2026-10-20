using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using PCLPlus.Patching;

namespace PCLPlus.Launcher
{
    /// <summary>
    /// Applies all patches to a launcher file, and restores it from its backup.
    /// </summary>
    internal static class LauncherFixer
    {
        private const string BackupExtension = ".bak";
        private const string TempExtension = ".pclpp.tmp";

        public static string GetBackupPath(string launcherPath)
        {
            return launcherPath + BackupExtension;
        }

        public static bool HasBackup(string launcherPath)
        {
            return File.Exists(GetBackupPath(launcherPath));
        }

        /// <summary>
        /// Patches the launcher in place. The original file is kept next to it as a backup.
        /// </summary>
        /// <returns>The report of the patched file.</returns>
        public static LauncherReport Fix(string launcherPath)
        {
            EnsureNotRunning(launcherPath);

            byte[] patched;
            using (var module = LoadLauncher(launcherPath))
            {
                var report = LauncherInspector.Inspect(launcherPath, module);
                if (!report.CanFix)
                    throw new LauncherException(report.Message);

                foreach (var patch in PatchCatalog.All.Where(p => p.GetState(module) == PatchState.Pending))
                {
                    try
                    {
                        patch.Apply(module);
                    }
                    catch (PatchException ex)
                    {
                        throw new LauncherException($"修复“{patch.Title}”失败：{ex.Message}", ex);
                    }
                }

                AddPatchMarker(module, report.Version);
                patched = Write(module);
            }

            var verified = Verify(launcherPath, patched);

            string backupPath = GetBackupPath(launcherPath);
            string tempPath = launcherPath + TempExtension;
            try
            {
                File.Copy(launcherPath, backupPath, overwrite: true);
                File.WriteAllBytes(tempPath, patched);
                File.Copy(tempPath, launcherPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new LauncherException("写入文件失败：" + ex.Message
                    + "\n如果启动器位于 Program Files 等受保护的文件夹，请以管理员身份运行 PCL++。", ex);
            }
            finally
            {
                TryDelete(tempPath);
            }

            return verified;
        }

        /// <summary>
        /// Replaces the launcher with the backup made by <see cref="Fix"/>.
        /// </summary>
        public static LauncherReport Restore(string launcherPath)
        {
            string backupPath = GetBackupPath(launcherPath);
            if (!File.Exists(backupPath))
                throw new LauncherException("没有找到备份文件，无法还原。");

            var backup = LauncherInspector.Inspect(backupPath);
            if (backup.Status == LauncherStatus.NotLauncher)
                throw new LauncherException("备份文件不是 PCL 启动器，已取消还原。");

            EnsureNotRunning(launcherPath);
            try
            {
                File.Copy(backupPath, launcherPath, overwrite: true);
                File.Delete(backupPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new LauncherException("还原失败：" + ex.Message, ex);
            }

            return LauncherInspector.Inspect(launcherPath);
        }

        private static ModuleDefMD LoadLauncher(string path)
        {
            try
            {
                return LauncherInspector.LoadModule(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException)
            {
                throw new LauncherException("无法读取启动器：" + ex.Message, ex);
            }
        }

        private static void EnsureNotRunning(string launcherPath)
        {
            if (LauncherProcess.IsRunning(launcherPath))
                throw new LauncherException("PCL 正在运行，请先关闭 PCL 再试。");
        }

        private static void AddPatchMarker(ModuleDef module, string launcherVersion)
        {
            string toolVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            string content = $"PCL++ {toolVersion}\nLauncher {launcherVersion}\nPatched {DateTime.UtcNow:O}\n";

            var existing = module.Resources.FindEmbeddedResource(LauncherSignature.PatchMarkerResource);
            if (existing != null)
                module.Resources.Remove(existing);
            module.Resources.Add(new EmbeddedResource(LauncherSignature.PatchMarkerResource,
                Encoding.UTF8.GetBytes(content), ManifestResourceAttributes.Private));
        }

        private static byte[] Write(ModuleDefMD module)
        {
            var options = new ModuleWriterOptions(module)
            {
                Logger = DummyLogger.ThrowModuleWriterExceptionOnErrorInstance,
            };
            options.MetadataOptions.Flags |= MetadataFlags.PreserveAll;

            try
            {
                using (var stream = new MemoryStream())
                {
                    module.Write(stream, options);
                    return stream.ToArray();
                }
            }
            catch (ModuleWriterException ex)
            {
                throw new LauncherException("生成修复后的启动器失败：" + ex.Message, ex);
            }
        }

        /// <summary>Reloads the patched image and checks every required patch point.</summary>
        private static LauncherReport Verify(string launcherPath, byte[] patched)
        {
            using (var module = LauncherInspector.LoadModule(patched))
            {
                var report = LauncherInspector.Inspect(launcherPath, module);
                bool complete = report.Status == LauncherStatus.Patched
                    && report.Patches.All(p => p.State == PatchState.Applied);
                if (!complete)
                    throw new LauncherException("修复结果校验失败，原文件未做任何改动。");
                return report;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
