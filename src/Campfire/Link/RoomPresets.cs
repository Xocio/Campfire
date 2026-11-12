using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Campfire.Link
{
    /// <summary>A saved set of room settings the host can apply in one step.</summary>
    internal sealed class RoomPreset
    {
        public const int MaxName = 24;

        public RoomPreset(string name, string title, int maxPlayers, string notice, string description, IReadOnlyList<string> tags,
            bool showRegion, bool voice, bool shareMods, bool allowlistOnly)
        {
            Name = Clip(name, MaxName);
            Title = title ?? "";
            MaxPlayers = maxPlayers >= 2 && maxPlayers <= 100 ? maxPlayers : 0;
            Notice = notice ?? "";
            Description = description ?? "";
            Tags = tags ?? Array.Empty<string>();
            ShowRegion = showRegion;
            Voice = voice;
            ShareMods = shareMods;
            AllowlistOnly = allowlistOnly;
        }

        /// <summary>The name the host gave the preset.</summary>
        public string Name { get; }

        /// <summary>The room name.</summary>
        public string Title { get; }

        public int MaxPlayers { get; }

        public string Notice { get; }

        public string Description { get; }

        public IReadOnlyList<string> Tags { get; }

        public bool ShowRegion { get; }

        public bool Voice { get; }

        public bool ShareMods { get; }

        public bool AllowlistOnly { get; }

        private static string Clip(string text, int limit)
        {
            text = (text ?? "").Trim();
            return text.Length > limit ? text.Substring(0, limit) : text;
        }
    }

    /// <summary>The host's room presets, kept in the data folder in the order they were saved.</summary>
    internal static class RoomPresets
    {
        public const int MaxCount = 20;

        private static readonly object Sync = new object();
        private static List<RoomPreset> _presets;

        private static string FilePath => Path.Combine(LinkSettings.DataDirectory, "presets.json");

        public static event EventHandler Changed;

        public static IReadOnlyList<RoomPreset> All
        {
            get
            {
                lock (Sync)
                    return Load().ToList();
            }
        }

        /// <summary>Saves <paramref name="preset"/>, replacing one with the same name.</summary>
        public static void Save(RoomPreset preset)
        {
            lock (Sync)
            {
                var presets = Load();
                int index = presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    presets[index] = preset;
                else
                    presets.Add(preset);
                while (presets.Count > MaxCount)
                    presets.RemoveAt(0);
                Write(presets);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Remove(string name)
        {
            lock (Sync)
            {
                var presets = Load();
                if (presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
                    return;
                Write(presets);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>The settings in use now, as a preset called <paramref name="name"/>.</summary>
        public static RoomPreset FromSettings(string name)
        {
            return new RoomPreset(name, LinkSettings.LobbyTitle, LinkSettings.MaxPlayers, LinkSettings.RoomNotice,
                LinkSettings.LobbyDescription, LinkSettings.LobbyTags, LinkSettings.LobbyShowRegion, LinkSettings.Voice,
                LinkSettings.ShareMods, LinkSettings.AllowlistOnly);
        }

        private static List<RoomPreset> Load()
        {
            if (_presets != null)
                return _presets;
            _presets = new List<RoomPreset>();
            try
            {
                if (File.Exists(FilePath) && Json.TryParse(File.ReadAllText(FilePath)) is object[] items)
                {
                    foreach (var item in items.OfType<IDictionary<string, object>>())
                    {
                        string name = Text(item, "name");
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        int max = item.TryGetValue("max", out object m) && m is double d ? (int)d : 0;
                        var tags = item.TryGetValue("tags", out object t) && t is object[] list
                            ? list.OfType<string>().ToList()
                            : new List<string>();
                        _presets.Add(new RoomPreset(name, Text(item, "title"), max, Text(item, "notice"), Text(item, "description"), tags,
                            Flag(item, "region", true), Flag(item, "voice", false), Flag(item, "mods", true), Flag(item, "allowlist", false)));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Warn("Could not read the room presets: " + ex.Message);
            }
            return _presets;
        }

        private static void Write(List<RoomPreset> presets)
        {
            _presets = presets;
            var items = presets.Select(p => (object)new Dictionary<string, object>
            {
                ["name"] = p.Name,
                ["title"] = p.Title,
                ["max"] = (double)p.MaxPlayers,
                ["notice"] = p.Notice,
                ["description"] = p.Description,
                ["tags"] = p.Tags.Cast<object>().ToArray(),
                ["region"] = p.ShowRegion,
                ["voice"] = p.Voice,
                ["mods"] = p.ShareMods,
                ["allowlist"] = p.AllowlistOnly,
            }).ToList();
            try
            {
                Directory.CreateDirectory(LinkSettings.DataDirectory);
                File.WriteAllText(FilePath, Json.Serialize(items));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Warn("Could not save the room presets: " + ex.Message);
            }
        }

        private static string Text(IDictionary<string, object> item, string key)
        {
            return item.TryGetValue(key, out object value) ? value as string : null;
        }

        private static bool Flag(IDictionary<string, object> item, string key, bool fallback)
        {
            return item.TryGetValue(key, out object value) && value is bool flag ? flag : fallback;
        }
    }
}
