using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Campfire
{
    /// <summary>
    /// Opens web pages in the default browser.
    /// </summary>
    internal static class Browser
    {
        private const int AnyProcess = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        /// <summary>
        /// Opens <paramref name="url"/>; throws <see cref="System.ComponentModel.Win32Exception"/> or
        /// <see cref="System.InvalidOperationException"/> when no browser can be started.
        /// </summary>
        public static void Open(string url)
        {
            // A browser that is already running opens the page in its existing window. Without this permission
            // Windows keeps that window behind Campfire, so the click looks as if nothing happened.
            AllowSetForegroundWindow(AnyProcess);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
