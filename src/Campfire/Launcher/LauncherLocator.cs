using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Campfire.Patching;

namespace Campfire.Launcher
{
    /// <summary>
    /// Finds the launcher next to Campfire.
    /// </summary>
    internal static class LauncherLocator
    {
        /// <summary>Upper bound for candidate files; the official launcher is far smaller.</summary>
        private const long MaxCandidateSize = 64L * 1024 * 1024;

        public static string FindIn(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;

            string preferred = Path.Combine(directory, LauncherSignature.DefaultFileName);
            if (File.Exists(preferred))
                return preferred;

            string self = Assembly.GetExecutingAssembly().Location;
            try
            {
                return Directory.EnumerateFiles(directory, "*.exe")
                    .Where(f => !string.Equals(f, self, StringComparison.OrdinalIgnoreCase))
                    .Where(f => new FileInfo(f).Length <= MaxCandidateSize)
                    .FirstOrDefault(IsLauncherFile);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool IsLauncherFile(string path)
        {
            return IsLauncherAssembly(path) || CommunityLauncher.IsCommunityLauncher(path);
        }

        private static bool IsLauncherAssembly(string path)
        {
            try
            {
                return AssemblyName.GetAssemblyName(path).Name == LauncherSignature.AssemblyName;
            }
            catch (Exception ex) when (ex is BadImageFormatException || ex is IOException
                || ex is UnauthorizedAccessException || ex is FileLoadException)
            {
                return false;
            }
        }
    }
}
