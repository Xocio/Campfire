using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Campfire.Link.Room;

namespace Campfire.Link
{
    /// <summary>A player the host lets into rooms that only take allowed players.</summary>
    internal sealed class AllowedPlayer
    {
        public AllowedPlayer(string machineId, string name, string gameName, string uuid, DateTime time)
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

        /// <summary>When the player was added, in local time.</summary>
        public DateTime Time { get; }

        /// <summary>Whether a player in the game with this name is the allowed player.</summary>
        public bool HasName(string name)
        {
            return string.Equals(name, Name, StringComparison.OrdinalIgnoreCase)
                   || (GameName != null && string.Equals(name, GameName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Players the host allows, kept in the data folder. While <see cref="LinkSettings.AllowlistOnly"/> is on, the
    /// room turns away every other computer, and the game turns away every other player name.
    /// </summary>
    internal static class Allowlist
    {
        private static readonly object Sync = new object();
        private static List<AllowedPlayer> _players;

        private static string FilePath => Path.Combine(LinkSettings.DataDirectory, "allowed.json");

        /// <summary>Raised after a player was added or removed.</summary>
        public static event EventHandler Changed;

        public static IReadOnlyList<AllowedPlayer> All
        {
            get
            {
                lock (Sync)
                    return Load().ToList();
            }
        }

        /// <summary>Adds the players that are not on the list yet; returns how many were added.</summary>
        public static int Add(IEnumerable<PlayerProfile> players)
        {
            int added = 0;
            lock (Sync)
            {
                var list = Load();
                foreach (var player in players)
                {
                    if (player == null || player.MachineId.Length == 0)
                        continue;
                    int index = list.FindIndex(p => p.MachineId == player.MachineId);
                    string gameName = player.GameName != null && !string.Equals(player.GameName, player.Name, StringComparison.OrdinalIgnoreCase)
                        ? player.GameName
                        : null;
                    var entry = new AllowedPlayer(player.MachineId, player.Name, gameName, player.Uuid, index < 0 ? DateTime.Now : list[index].Time);
                    if (index < 0)
                    {
                        list.Add(entry);
                        added++;
                    }
                    else
                    {
                        list[index] = entry;
                    }
                }
                Save(list);
            }
            Changed?.Invoke(null, EventArgs.Empty);
            return added;
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

        /// <summary>Whether the computer or premium account of <paramref name="player"/> is allowed.</summary>
        public static bool Contains(PlayerProfile player)
        {
            lock (Sync)
            {
                return Load().Any(p => p.MachineId == player.MachineId
                                       || (p.Uuid != null && string.Equals(p.Uuid, player.Uuid, StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <summary>Whether a player name in the game belongs to an allowed player.</summary>
        public static bool ContainsName(string name)
        {
            lock (Sync)
                return Load().Any(p => p.HasName(name));
        }

        private static List<AllowedPlayer> Load()
        {
            if (_players != null)
                return _players;

            _players = new List<AllowedPlayer>();
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
                        _players.Add(new AllowedPlayer(machineId, Get(item, "name"), Get(item, "game_name"), Get(item, "uuid"), time));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Warn("Could not read the allowlist: " + ex.Message);
            }
            return _players;
        }

        private static void Save(List<AllowedPlayer> players)
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
                LinkLog.Warn("Could not save the allowlist: " + ex.Message);
            }
        }

        private static string Get(IDictionary<string, object> item, string key)
        {
            return item.TryGetValue(key, out var value) ? value as string : null;
        }
    }
}
