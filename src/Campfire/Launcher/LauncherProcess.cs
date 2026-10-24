using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Campfire.Launcher
{
    /// <summary>
    /// Detects whether a launcher file is currently running.
    /// </summary>
    internal static class LauncherProcess
    {
        private const int ProcessQueryLimitedInformation = 0x1000;

        public static bool IsRunning(string path)
        {
            string fullPath = Path.GetFullPath(path);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(fullPath)))
            {
                using (process)
                {
                    string imagePath = TryGetImagePath(process);
                    // When the image path cannot be read, assume the worst so the file is never replaced while in use.
                    if (imagePath == null || string.Equals(imagePath, fullPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Full path of the program a process runs, or <c>null</c> when it cannot be read.</summary>
        internal static string TryGetImagePath(Process process)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
                return null;
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            catch (Win32Exception)
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
