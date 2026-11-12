using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Campfire.Crash
{
    internal enum CrashFileKind
    {
        /// <summary>crash-reports\crash-*.txt, written by the game when it stops on an error.</summary>
        CrashReport,

        /// <summary>logs\latest.log, the log of the last run.</summary>
        GameLog,

        /// <summary>hs_err_pid*.log, written by Java when the virtual machine itself fails.</summary>
        JvmReport,

        /// <summary>A file the user picked.</summary>
        Other,
    }

    internal sealed class CrashFile
    {
        public CrashFile(CrashFileKind kind, string path, DateTime time, string text)
        {
            Kind = kind;
            Path = path;
            Time = time;
            Text = text;
        }

        public CrashFileKind Kind { get; }

        public string Path { get; }

        /// <summary>When the file was last written, in local time.</summary>
        public DateTime Time { get; }

        public string Text { get; }

        public string Name => System.IO.Path.GetFileName(Path);
    }

    /// <summary>Finds and reads the files that tell why a game stopped.</summary>
    internal static class CrashSources
    {
        /// <summary>Larger files are read as their first and last parts.</summary>
        private const int HeadBytes = 2 * 1024 * 1024;
        private const int TailBytes = 6 * 1024 * 1024;

        /// <summary>
        /// The newest crash report and Java error report and the last game log of the game that runs in
        /// <paramref name="gameDirectory"/>. Java error reports are also looked for in <paramref name="otherDirectories"/>.
        /// </summary>
        public static IReadOnlyList<CrashFile> Find(string gameDirectory, IEnumerable<string> otherDirectories)
        {
            var files = new List<CrashFile>();
            var report = Newest(Path.Combine(gameDirectory, "crash-reports"), "crash-*.txt");
            if (report != null)
                files.Add(Load(CrashFileKind.CrashReport, report));

            string log = Path.Combine(gameDirectory, "logs", "latest.log");
            if (File.Exists(log))
                files.Add(Load(CrashFileKind.GameLog, log));

            var jvm = new[] { gameDirectory }.Concat(otherDirectories ?? Array.Empty<string>())
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(d => Newest(d, "hs_err_pid*.log"))
                .Where(f => f != null)
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
            if (jvm != null)
                files.Add(Load(CrashFileKind.JvmReport, jvm));
            return files.Where(f => f != null).ToList();
        }

        /// <summary>When the newest of these files was written, or <c>null</c> when there is none.</summary>
        public static DateTime? LatestTime(string gameDirectory)
        {
            try
            {
                var times = new List<DateTime>();
                var report = Newest(Path.Combine(gameDirectory, "crash-reports"), "crash-*.txt");
                if (report != null)
                    times.Add(report.LastWriteTime);
                string log = Path.Combine(gameDirectory, "logs", "latest.log");
                if (File.Exists(log))
                    times.Add(File.GetLastWriteTime(log));
                return times.Count == 0 ? (DateTime?)null : times.Max();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Reads a file the user picked, or returns <c>null</c> when it cannot be read.</summary>
        public static CrashFile Open(string path)
        {
            string name = Path.GetFileName(path);
            var kind = name.StartsWith("crash-", StringComparison.OrdinalIgnoreCase) ? CrashFileKind.CrashReport
                : name.StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase) ? CrashFileKind.JvmReport
                : name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ? CrashFileKind.GameLog
                : CrashFileKind.Other;
            return Load(kind, new FileInfo(path));
        }

        private static CrashFile Load(CrashFileKind kind, string path)
        {
            return Load(kind, new FileInfo(path));
        }

        private static CrashFile Load(CrashFileKind kind, FileInfo file)
        {
            try
            {
                return new CrashFile(kind, file.FullName, file.LastWriteTime, ReadText(file.FullName));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Link.LinkLog.Warn("A crash file could not be read: " + file.Name + " (" + ex.Message + ")");
                return null;
            }
        }

        private static FileInfo Newest(string directory, string pattern)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return null;
                return new DirectoryInfo(directory).GetFiles(pattern).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The file as text with "\n" line breaks; see <see cref="Decode"/>.</summary>
        private static string ReadText(string path)
        {
            return Decode(path).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>The file as text: UTF-8 when it is valid UTF-8, else the system code page. Large files keep their start and end.</summary>
        private static string Decode(string path)
        {
            byte[] bytes;
            bool cut;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                cut = stream.Length > HeadBytes + TailBytes;
                if (!cut)
                {
                    bytes = new byte[stream.Length];
                    ReadFully(stream, bytes, 0, bytes.Length);
                }
                else
                {
                    bytes = new byte[HeadBytes + TailBytes];
                    ReadFully(stream, bytes, 0, HeadBytes);
                    stream.Position = stream.Length - TailBytes;
                    ReadFully(stream, bytes, HeadBytes, TailBytes);
                }
            }
            try
            {
                // Whole files are decoded strictly; in shortened files broken characters are replaced.
                return new UTF8Encoding(false, !cut).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        private static void ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    break;
                offset += read;
                count -= read;
            }
        }
    }
}
