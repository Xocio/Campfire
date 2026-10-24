using System;
using System.Diagnostics;
using System.IO;
using Campfire.Patching;

namespace Campfire.Launcher
{
    /// <summary>
    /// File steps shared by the launcher repair and the JRE tool: reading the launcher, replacing it while keeping a
    /// backup, and putting the backup back.
    /// </summary>
    internal static class LauncherFile
    {
        /// <summary>Largest community bundle considered; real ones are far smaller.</summary>
        public const long MaxBundleSize = 64L * 1024 * 1024;

        private const string TempExtension = ".campfire.tmp";

        public static byte[] Read(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new LauncherException(ex, "Fix.ReadFailed", ex.Message);
            }
        }

        public static void EnsureNotRunning(string path)
        {
            if (LauncherProcess.IsRunning(path))
                throw new LauncherException("Fix.Running");
        }

        /// <summary>Copies the launcher to <paramref name="backupPath"/>, then writes <paramref name="patched"/> over it.</summary>
        public static void Replace(string path, string backupPath, byte[] patched)
        {
            string tempPath = path + TempExtension;
            try
            {
                File.Copy(path, backupPath, overwrite: true);
                File.WriteAllBytes(tempPath, patched);
                File.Copy(tempPath, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new LauncherException(ex, "Fix.WriteFailed", ex.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>Puts the backup back in place of the launcher and deletes it.</summary>
        public static void Restore(string path, string backupPath)
        {
            EnsureNotRunning(path);
            try
            {
                File.Copy(backupPath, path, overwrite: true);
                File.Delete(backupPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new LauncherException(ex, "Fix.RestoreFailed", ex.Message);
            }
        }

        /// <summary>
        /// Version of a community launcher. The bundled <c>PCL.Core</c> assembly is a library, so its version says
        /// nothing; the executable's version resource is the release number.
        /// </summary>
        public static string ReadBundleVersion(string path, BundleImage image)
        {
            try
            {
                string version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return image.Version?.ToString();
        }
    }
}
