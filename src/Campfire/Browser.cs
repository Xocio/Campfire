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
            // Lets the browser bring its window to the front.
            AllowSetForegroundWindow(AnyProcess);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
