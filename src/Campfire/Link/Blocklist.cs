using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Campfire.Link.Room;

namespace Campfire.Link
{
    /// <summary>A player the host never lets into their rooms again.</summary>
    internal sealed class BlockedPlayer
    {
        public BlockedPlayer(string machineId, string name, string gameName, string uuid, DateTime time)
        {
            MachineId = machineId ?? "";
            Name = name ?? "";
            GameName = gameName;
            Uuid = uuid;
            Time = time;
        }

        public string MachineId { get; }

        public string Name { get; }

        /// <summary>The name in the game, when it differed from <see cref="Name"/>.</summary>
        public string GameName { get; }

        public string Uuid { get; }

        public DateTime Time { get; }

        /// <summary>Whether a player in the game with this name is the blocked player.</summary>
        public bool HasName(string name)
        {
            return string.Equals(name, Name, StringComparison.OrdinalIgnoreCase)
                   || (GameName != null && string.Equals(name, GameName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Players blocked by this host, kept in the data folder. A blocked computer is turned away by the room, and
    /// its player names are turned away by the game.
    /// </summary>
    internal static class Blocklist
    {
        private static readonly object Sync = new object();
        private static List<BlockedPlayer> _players;

        private static string FilePath => Path.Combine(LinkSettings.DataDirectory, "blocked.json");

        /// <summary>Raised after a player was blocked or unblocked.</summary>
        public static event EventHandler Changed;

        public static IReadOnlyList<BlockedPlayer> All
        {
            get
            {
                lock (Sync)
                    return Load().ToList();
            }
        }

        public static void Add(PlayerProfile player)
        {
            lock (Sync)
            {
                var players = Load();
                players.RemoveAll(p => p.MachineId == player.MachineId);
                string gameName = player.GameName != null && !string.Equals(player.GameName, player.Name, StringComparison.OrdinalIgnoreCase)
                    ? player.GameName
                    : null;
                players.Add(new BlockedPlayer(player.MachineId, player.Name, gameName, player.Uuid, DateTime.Now));
                Save(players);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Remove(string machineId)
        {
            lock (Sync)
            {
                var players = Load();
                if (players.RemoveAll(p => p.MachineId == machineId) == 0)
                    return;
                Save(players);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Whether the computer or premium account of <paramref name="player"/> is blocked.</summary>
        public static bool Contains(PlayerProfile player)
        {
            lock (Sync)
            {
                return Load().Any(p => p.MachineId == player.MachineId
                                       || (p.Uuid != null && string.Equals(p.Uuid, player.Uuid, StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <summary>Whether a player name in the game belongs to a blocked player.</summary>
        public static bool ContainsName(string name)
        {
            lock (Sync)
                return Load().Any(p => p.HasName(name));
        }

        private static List<BlockedPlayer> Load()
        {
            if (_players != null)
                return _players;

            _players = new List<BlockedPlayer>();
            try
            {
                if (File.Exists(FilePath) && Json.TryParse(File.ReadAllText(FilePath)) is object[] items)
                {
                    foreach (var item in items.OfType<IDictionary<string, object>>())
                    {
                        string machineId = Get(item, "machine_id");
                        if (string.IsNullOrEmpty(machineId))
                            continue;
                        DateTime.TryParse(Get(item, "time"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time);
                        _players.Add(new BlockedPlayer(machineId, Get(item, "name"), Get(item, "game_name"), Get(item, "uuid"), time));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Warn("Could not read the blocklist: " + ex.Message);
            }
            return _players;
        }

        private static void Save(List<BlockedPlayer> players)
        {
            _players = players;
            var items = players.Select(p =>
            {
                var item = new Dictionary<string, object>
                {
                    ["machine_id"] = p.MachineId,
                    ["name"] = p.Name,
                    ["time"] = p.Time.ToString("o", CultureInfo.InvariantCulture),
                };
                if (p.GameName != null)
                    item["game_name"] = p.GameName;
                if (p.Uuid != null)
                    item["uuid"] = p.Uuid;
                return (object)item;
            }).ToList();
            try
            {
                Directory.CreateDirectory(LinkSettings.DataDirectory);
                File.WriteAllText(FilePath, Json.Serialize(items));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Warn("Could not save the blocklist: " + ex.Message);
            }
        }

        private static string Get(IDictionary<string, object> item, string key)
        {
            return item.TryGetValue(key, out var value) ? value as string : null;
        }
    }
}
