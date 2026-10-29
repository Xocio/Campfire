using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Campfire.Link
{
    /// <summary>
    /// Multiplayer preferences and the identity of this computer. Changes apply to the next room.
    /// </summary>
    internal static class LinkSettings
    {
        /// <summary>Choices offered for <see cref="SearchTimeoutSeconds"/>.</summary>
        public static readonly int[] SearchTimeoutChoices = { 30, 60, 120 };

        private static string _machineId;

        public static string DataDirectory { get; } = Path.Combine(AppSettings.DataDirectory, "Link");

        // ======================== Room ========================

        /// <summary>The name shown to other players, as typed by the user (may be empty).</summary>
        public static string PlayerName
        {
            get => GetText("link.player");
            set => AppSettings.Set("link.player", (value ?? "").Trim());
        }

        /// <summary>The name actually sent to other players: the typed name, else the name in the running game.</summary>
        public static string EffectivePlayerName
        {
            get
            {
                string name = PlayerName;
                if (name.Length > 0)
                    return name;
                string game = Minecraft.GameAccount.Current?.Name;
                return !string.IsNullOrEmpty(game) ? game : FallbackPlayerName;
            }
        }

        /// <summary>Used when no name is typed and no game is running.</summary>
        public static string FallbackPlayerName => "Campfire #" + MachineId.Substring(0, 4).ToUpperInvariant();

        /// <summary>Name of the room entry in the multiplayer list; empty means the default text.</summary>
        public static string LanEntryName
        {
            get => GetText("link.lanname");
            set => AppSettings.Set("link.lanname", (value ?? "").Trim());
        }

        /// <summary>How long a joining player looks for the host before giving up.</summary>
        public static int SearchTimeoutSeconds
        {
            get => int.TryParse(AppSettings.Get("link.timeout"), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                   && SearchTimeoutChoices.Contains(value)
                ? value
                : 60;
            set => AppSettings.Set("link.timeout", value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Local port for the host's game when joining, or 0 to choose one automatically.</summary>
        public static int LocalPort
        {
            get => int.TryParse(AppSettings.Get("link.localport"), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                   && value > 0 && value < 65536
                ? value
                : 0;
            set => AppSettings.Set("link.localport", value > 0 ? value.ToString(CultureInfo.InvariantCulture) : null);
        }

        /// <summary>Whether the invite code is copied as soon as a room is open.</summary>
        public static bool AutoCopyCode
        {
            get => GetFlag("link.autocopy", true);
            set => SetFlag("link.autocopy", value);
        }

        /// <summary>Whether creating a room opens the host's single-player world to LAN through the game agent.</summary>
        public static bool AutoOpenLan
        {
            get => GetFlag("link.autolan", true);
            set => SetFlag("link.autolan", value);
        }

        /// <summary>Whether an invite code on the clipboard is offered for joining.</summary>
        public static bool DetectClipboard
        {
            get => GetFlag("link.clipboard", true);
            set => SetFlag("link.clipboard", value);
        }

        // ======================== Mods ========================

        /// <summary>Host: whether players in the room see the game version and mods of the host's game.</summary>
        public static bool ShareMods
        {
            get => GetFlag("link.sharemods", true);
            set => SetFlag("link.sharemods", value);
        }

        /// <summary>Host: whether players may download the host's mods from this computer.</summary>
        public static bool ServeMods
        {
            get => GetFlag("link.servemods", true);
            set => SetFlag("link.servemods", value);
        }

        /// <summary>Guest: where mod sync gets files from, as last chosen.</summary>
        public static Mods.ModSource ModSource
        {
            get => Enum.TryParse(AppSettings.Get("link.modsource"), out Mods.ModSource value) && Enum.IsDefined(typeof(Mods.ModSource), value)
                ? value
                : Mods.ModSource.OfficialFirst;
            set => AppSettings.Set("link.modsource", value.ToString());
        }

        // ======================== Network ========================

        /// <summary>Whether traffic may go through a relay node when a direct connection cannot be made.</summary>
        public static bool AllowRelay
        {
            get => GetFlag("link.relay", true);
            set => SetFlag("link.relay", value);
        }

        /// <summary>Whether the built-in public nodes are used in addition to custom nodes.</summary>
        public static bool UsePublicNodes
        {
            get => GetFlag("link.publicnodes", true);
            set => SetFlag("link.publicnodes", value);
        }

        /// <summary>Extra nodes as typed by the user.</summary>
        public static string CustomNodes
        {
            get => GetText("link.nodes");
            set => AppSettings.Set("link.nodes", (value ?? "").Trim());
        }

        /// <summary>
        /// Whether other devices in this computer's local network may join the host's world through it. When
        /// off, the room is only reachable from this computer and is not announced in the LAN list.
        /// </summary>
        public static bool ShareWithLan
        {
            get => GetFlag("link.lanshare", true);
            set => SetFlag("link.lanshare", value);
        }

        // ======================== Privacy ========================

        /// <summary>
        /// Never connect directly to other players, so they cannot learn this computer's IP address. All
        /// traffic then goes through a relay node.
        /// </summary>
        public static bool HideAddress
        {
            get => GetFlag("link.hideip", false);
            set => SetFlag("link.hideip", value);
        }

        /// <summary>Whether the player name and account type are read from a running game.</summary>
        public static bool ReadGameAccount
        {
            get => GetFlag("link.gameaccount", true);
            set => SetFlag("link.gameaccount", value);
        }

        /// <summary>Whether skin faces are shown, and whether this player's account id is shared for that.</summary>
        public static bool ShowSkins
        {
            get => GetFlag("link.heads", true);
            set => SetFlag("link.heads", value);
        }

        /// <summary>Whether other players see the program name and version.</summary>
        public static bool ShareVersion
        {
            get => GetFlag("link.vendor", true);
            set => SetFlag("link.vendor", value);
        }

        // ======================== Lobby ========================

        /// <summary>The room name last used in the lobby, offered again next time.</summary>
        public static string LobbyTitle
        {
            get => GetText("lobby.title");
            set => AppSettings.Set("lobby.title", (value ?? "").Trim());
        }

        /// <summary>The tags last picked for the lobby.</summary>
        public static IReadOnlyList<string> LobbyTags
        {
            get => GetText("lobby.tags").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            set => AppSettings.Set("lobby.tags", string.Join(",", value ?? Array.Empty<string>()));
        }

        /// <summary>Whether listed rooms show the rough region the service derives from the connection.</summary>
        public static bool LobbyShowRegion
        {
            get => GetFlag("lobby.region", true);
            set => SetFlag("lobby.region", value);
        }

        /// <summary>
        /// A random identifier used only by the lobby, so the service can keep one listing per computer. It is
        /// unrelated to <see cref="MachineId"/> and to the statistics identifier.
        /// </summary>
        public static string LobbyId
        {
            get
            {
                string id = AppSettings.Get("lobby.id");
                if (id != null && id.Length == 32 && id.All(Uri.IsHexDigit))
                    return id.ToLowerInvariant();
                id = Hex.Random(16);
                AppSettings.Set("lobby.id", id);
                return id;
            }
        }

        // ======================== Nodes ========================

        /// <summary>Custom nodes first, then the enabled built-in nodes, without duplicates.</summary>
        public static IReadOnlyList<string> GetNodes()
        {
            return ParseNodes(CustomNodes, out _)
                .Concat(PublicNodes.All.Where(IsNodeEnabled).Select(n => n.Url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Whether a built-in node is used. Nodes added by later versions start enabled.</summary>
        public static bool IsNodeEnabled(PublicNode node)
        {
            return UsePublicNodes && !GetDisabledNodes().Contains(node.Id);
        }

        /// <summary>Saves which built-in nodes are used; every other one is turned off.</summary>
        public static void SetEnabledNodes(IEnumerable<PublicNode> enabled)
        {
            var ids = new HashSet<string>(enabled.Select(n => n.Id), StringComparer.Ordinal);
            AppSettings.Set("link.nodes.off", string.Join(",", PublicNodes.All.Where(n => !ids.Contains(n.Id)).Select(n => n.Id)));
            UsePublicNodes = ids.Count > 0;
        }

        private static HashSet<string> GetDisabledNodes()
        {
            return new HashSet<string>(GetText("link.nodes.off").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Splits user input into node addresses. Accepts spaces, commas, semicolons or line breaks as separators;
        /// a bare "host:port" is read as TCP. Entries that are not addresses are returned in <paramref name="invalid"/>.
        /// </summary>
        public static IReadOnlyList<string> ParseNodes(string text, out IReadOnlyList<string> invalid)
        {
            var nodes = new List<string>();
            var rejected = new List<string>();
            invalid = rejected;
            if (string.IsNullOrWhiteSpace(text))
                return nodes;

            var separators = new[] { ' ', '\t', '\r', '\n', ',', ';', '，', '；', '、' };
            foreach (string part in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string node = part.Contains("://") ? part : "tcp://" + part;
                if (Uri.TryCreate(node, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                    && (uri.Scheme == "tcp" || uri.Scheme == "udp" || uri.Scheme == "ws" || uri.Scheme == "wss"
                        || uri.Scheme == "quic" || uri.Scheme == "http" || uri.Scheme == "https"
                        || uri.Scheme == "txt" || uri.Scheme == "srv"))
                    nodes.Add(node);
                else
                    rejected.Add(part);
            }
            return nodes;
        }

        // ======================== Identity ========================

        /// <summary>A random identifier of this computer, created once and kept in the data folder.</summary>
        public static string MachineId
        {
            get
            {
                if (_machineId == null)
                    _machineId = LoadOrCreateMachineId(false);
                return _machineId;
            }
        }

        /// <summary>Replaces the identifier, so other players see this computer as a new one.</summary>
        public static void ResetMachineId()
        {
            _machineId = LoadOrCreateMachineId(true);
        }

        private static string LoadOrCreateMachineId(bool replace)
        {
            string path = Path.Combine(DataDirectory, "machine-id");
            try
            {
                if (!replace && File.Exists(path))
                {
                    string stored = File.ReadAllText(path).Trim();
                    if (stored.Length == 32 && stored.All(Uri.IsHexDigit))
                        return stored.ToLowerInvariant();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            string id = Hex.Random(16);

            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(path, id);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // An unsaved id only means this computer gets a new identity next time.
            }
            return id;
        }

        // ======================== Helpers ========================

        private static string GetText(string key)
        {
            return AppSettings.Get(key) ?? "";
        }

        private static bool GetFlag(string key, bool fallback)
        {
            string value = AppSettings.Get(key);
            return value == null ? fallback : value != "0";
        }

        private static void SetFlag(string key, bool value)
        {
            AppSettings.Set(key, value ? "1" : "0");
        }
    }
}
