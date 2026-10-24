using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Campfire.Link
{
    /// <summary>
    /// Plain-text log of multiplayer sessions in %LocalAppData%\Campfire\Link\link.log, kept for troubleshooting.
    /// </summary>
    internal static class LinkLog
    {
        private const long MaxSize = 2 * 1024 * 1024;

        private static readonly object Sync = new object();

        public static string FilePath { get; } = Path.Combine(LinkSettings.DataDirectory, "link.log");

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);
        }

        private static void Write(string level, string message)
        {
            string line = string.Format(CultureInfo.InvariantCulture, "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}{3}",
                DateTime.Now, level, message, Environment.NewLine);
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(LinkSettings.DataDirectory);
                    var file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > MaxSize)
                        file.Delete();
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Logging must never break multiplayer.
                }
            }
        }
    }
}
