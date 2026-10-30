using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Campfire.Link
{
    /// <summary>A room this computer opened or joined.</summary>
    internal sealed class RoomRecord
    {
        public RoomRecord(string code, bool host, string title, DateTime lastUsed, bool favorite)
        {
            Code = code;
            IsHost = host;
            Title = title;
            LastUsed = lastUsed;
            IsFavorite = favorite;
        }

        public string Code { get; }

        /// <summary>The room was opened here; opening it again reuses the same invite code.</summary>
        public bool IsHost { get; }

        /// <summary>The name of the host, as the players saw it.</summary>
        public string Title { get; }

        public DateTime LastUsed { get; }

        public bool IsFavorite { get; }
    }

    /// <summary>
    /// The rooms used before. Recent ones are trimmed to the latest few; favorites stay until removed.
    /// </summary>
    internal static class RoomHistory
    {
        private const int MaxRecent = 10;
        private const int MaxFavorites = 30;

        private static readonly object Sync = new object();
        private static List<RoomRecord> _items;

        private static string FilePath => Path.Combine(LinkSettings.DataDirectory, "rooms.txt");

        public static event EventHandler Changed;

        /// <summary>Favorites first, then the others; each group with the latest on top.</summary>
        public static IReadOnlyList<RoomRecord> GetAll()
        {
            lock (Sync)
            {
                EnsureLoaded();
                return _items.OrderByDescending(r => r.IsFavorite).ThenByDescending(r => r.LastUsed).ToList();
            }
        }

        /// <summary>Notes that a room was opened or joined just now.</summary>
        public static void Record(bool host, RoomCode code, string title)
        {
            if (code == null)
                return;
            lock (Sync)
            {
                EnsureLoaded();
                var known = Find(code.Code, host);
                bool favorite = known?.IsFavorite ?? false;
                _items.Remove(known);
                _items.Add(new RoomRecord(code.Code, host, Clean(title), DateTime.UtcNow, favorite));
                Trim();
                Save();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void SetFavorite(RoomRecord record, bool favorite)
        {
            lock (Sync)
            {
                EnsureLoaded();
                var known = Find(record.Code, record.IsHost);
                if (known == null || known.IsFavorite == favorite)
                    return;
                _items.Remove(known);
                _items.Add(new RoomRecord(known.Code, known.IsHost, known.Title, known.LastUsed, favorite));
                Trim();
                Save();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Remove(RoomRecord record)
        {
            lock (Sync)
            {
                EnsureLoaded();
                if (_items.RemoveAll(r => r.Code == record.Code && r.IsHost == record.IsHost) == 0)
                    return;
                Save();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Clear()
        {
            lock (Sync)
            {
                EnsureLoaded();
                if (_items.Count == 0)
                    return;
                _items.Clear();
                Save();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static RoomRecord Find(string code, bool host)
        {
            return _items.FirstOrDefault(r => r.Code == code && r.IsHost == host);
        }

        private static void Trim()
        {
            var favorites = _items.Where(r => r.IsFavorite).OrderByDescending(r => r.LastUsed).Take(MaxFavorites);
            var recent = _items.Where(r => !r.IsFavorite).OrderByDescending(r => r.LastUsed).Take(MaxRecent);
            _items = favorites.Concat(recent).ToList();
        }

        private static string Clean(string text)
        {
            return (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static void EnsureLoaded()
        {
            if (_items != null)
                return;

            _items = new List<RoomRecord>();
            try
            {
                if (!File.Exists(FilePath))
                    return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    // host, favorite, ticks, code, title
                    var parts = line.Split('\t');
                    if (parts.Length < 5 || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                        || ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
                        continue;
                    var code = RoomCode.Find(parts[3]);
                    if (code == null)
                        continue;
                    _items.Add(new RoomRecord(code.Code, parts[0] == "1", parts[4], new DateTime(ticks, DateTimeKind.Utc), parts[1] == "1"));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(LinkSettings.DataDirectory);
                var lines = _items.Select(r => string.Join("\t", r.IsHost ? "1" : "0", r.IsFavorite ? "1" : "0",
                    r.LastUsed.Ticks.ToString(CultureInfo.InvariantCulture), r.Code, r.Title));
                File.WriteAllText(FilePath, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The list is a convenience; failing to save it is not an error.
            }
        }
    }
}
