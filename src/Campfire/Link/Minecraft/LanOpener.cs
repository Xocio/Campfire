using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Campfire.Localization;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Asks the in-game agent to open a world to LAN as soon as the host is in it, and reports the outcome.
    /// </summary>
    /// <remarks>
    /// Like the overlay, the request is a state file the hook only obeys while it is fresh: "world" names the
    /// world folder, or is "*" for whichever world the host plays, "id" tells this request from earlier ones and
    /// "time" is rewritten every few seconds. The hook answers with a result file: the port the world was opened
    /// on, "already" when the host opened it first, or 0 when it could not. Disposing removes both.
    /// </remarks>
    internal sealed class LanOpener : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly object _sync = new object();
        private readonly string _path;
        private readonly string _resultPath;
        private readonly string _world;
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly Timer _timer;
        private bool _disposed;

        /// <param name="worldFolder">The folder of the world to open, or <c>null</c> for any world the host enters.</param>
        public LanOpener(int processId, string worldFolder)
        {
            ProcessId = processId;
            _world = worldFolder ?? "*";
            _path = Path.Combine(LinkSettings.DataDirectory, "lan-" + processId + ".txt");
            _resultPath = Path.Combine(LinkSettings.DataDirectory, "lan-" + processId + ".done");
            // A result from an earlier request must not count for this one.
            TryDelete(_resultPath);
            Write();
            _timer = new Timer(_ => Write(), null, Interval, Interval);
        }

        public int ProcessId { get; }

        /// <summary>
        /// The outcome so far: <c>null</c> while waiting, the port once the world is open (0 when the port is
        /// unknown because the host opened it first), or -1 when the game could not open it.
        /// </summary>
        public int? ReadResult()
        {
            try
            {
                if (!File.Exists(_resultPath))
                    return null;
                string text = File.ReadAllText(_resultPath, Utf8).Trim();
                if (!text.StartsWith("port=", StringComparison.Ordinal))
                    return -1;
                string value = text.Substring("port=".Length);
                if (value == "already")
                    return 0;
                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port > 0 ? port : -1;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The hook may be writing it right now.
                return null;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            _timer.Dispose();
            TryDelete(_path);
            TryDelete(_resultPath);
        }

        private void Write()
        {
            var lines = new[]
            {
                "time=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                "world=" + _world,
                "id=" + _id,
                "opened=" + Text.Get("Link.Game.LanOpened"),
            };

            lock (_sync)
            {
                if (_disposed)
                    return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    // Written aside and swapped in.
                    string temporary = _path + ".tmp";
                    File.WriteAllText(temporary, string.Join("\n", lines) + "\n", Utf8);
                    if (File.Exists(_path))
                        File.Replace(temporary, _path, null);
                    else
                        File.Move(temporary, _path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // The hook keeps the last state for a while; the next write usually succeeds.
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
