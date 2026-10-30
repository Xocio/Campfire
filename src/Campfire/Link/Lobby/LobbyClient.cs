using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Online;

namespace Campfire.Link.Lobby
{
    /// <summary>A room in the lobby, as everyone sees it. The invite code is only handed out on joining.</summary>
    internal sealed class LobbyRoom
    {
        public string Id { get; set; }

        public string Title { get; set; }

        /// <summary>The host's player name; may be empty.</summary>
        public string Host { get; set; }

        /// <summary>Game version of the host's world; may be empty.</summary>
        public string Version { get; set; }

        /// <summary>Name of the host's world; may be empty.</summary>
        public string Map { get; set; }

        public IReadOnlyList<string> Tags { get; set; }

        public int Players { get; set; }

        /// <summary>The host's player limit, or 0 when unknown.</summary>
        public int MaxPlayers { get; set; }

        /// <summary>Two-letter country code and region name; both empty when the host keeps them private.</summary>
        public string Country { get; set; }

        public string Region { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>Whether joining asks for the password the host set.</summary>
        public bool Locked { get; set; }

        /// <summary>The room picture as PNG or JPEG bytes, or <c>null</c>.</summary>
        public byte[] Icon { get; set; }

        /// <summary>What the host wrote about the room; may be empty.</summary>
        public string Description { get; set; }
    }

    internal sealed class LobbyList
    {
        public bool Enabled { get; set; }

        /// <summary>A message from the author shown above the rooms; may be empty.</summary>
        public string Notice { get; set; }

        /// <summary>Versions below this one can browse but not list rooms; <c>null</c> for none.</summary>
        public Version MinAppVersion { get; set; }

        /// <summary>How long to wait before loading the list again.</summary>
        public TimeSpan RefreshInterval { get; set; }

        /// <summary>Tags a host may pick, in the order to offer them.</summary>
        public IReadOnlyList<string> Tags { get; set; }

        public IReadOnlyList<LobbyRoom> Rooms { get; set; }
    }

    /// <summary>What a host lists about its room.</summary>
    internal sealed class LobbyListing
    {
        public RoomCode Code { get; set; }

        public string Title { get; set; }

        public string Host { get; set; }

        public string Version { get; set; }

        public string Map { get; set; }

        public IReadOnlyList<string> Tags { get; set; }

        public int MaxPlayers { get; set; }

        /// <summary>Whether the lobby shows the rough region the service derives from the connection.</summary>
        public bool ShowRegion { get; set; }

        /// <summary>Asked for before the invite code is handed out; empty for none.</summary>
        public string Password { get; set; }

        /// <summary>A small PNG or JPEG shown with the room, or <c>null</c>.</summary>
        public byte[] Icon { get; set; }

        /// <summary>A few words about the room, typed by the host; empty for none.</summary>
        public string Description { get; set; }
    }

    /// <summary>A listed room as its host holds it: the token proves the listing is theirs.</summary>
    internal sealed class LobbyTicket
    {
        public string Id { get; set; }

        public string Token { get; set; }

        /// <summary>The title as listed.</summary>
        public string Title { get; set; }

        public TimeSpan HeartbeatInterval { get; set; }
    }

    internal enum LobbyError
    {
        /// <summary>This build has no service.</summary>
        NoService,
        Network,
        /// <summary>The lobby is turned off.</summary>
        Closed,
        /// <summary>The lobby holds as many rooms as it may.</summary>
        Full,
        NotFound,
        /// <summary>The listing was taken down by the author.</summary>
        Removed,
        /// <summary>The listing was hidden after several players reported it.</summary>
        Reported,
        /// <summary>The title cannot be shown; see <see cref="LobbyException.Field"/>.</summary>
        Blocked,
        Banned,
        UpdateRequired,
        /// <summary>Too many requests or rooms from this network.</summary>
        TooMany,
        /// <summary>Somebody else already listed this invite code.</summary>
        AlreadyListed,
        /// <summary>The room asks for a password and none or a wrong one was given.</summary>
        WrongPassword,
        Unexpected,
    }

    internal sealed class LobbyException : Exception
    {
        public LobbyException(LobbyError kind, string message, Exception inner = null, string field = null) : base(message, inner)
        {
            Kind = kind;
            Field = field;
        }

        public LobbyError Kind { get; }

        /// <summary>For <see cref="LobbyError.Blocked"/>: the refused field, such as "title".</summary>
        public string Field { get; }
    }

    /// <summary>
    /// The public room lobby on the Campfire service. Only official builds know the service; others can still
    /// play together with invite codes.
    /// </summary>
    internal static class LobbyClient
    {
        public static bool IsAvailable => OnlineService.IsAvailable;

        public static async Task<LobbyList> ListAsync(CancellationToken cancellation = default)
        {
            var json = await SendAsync(HttpMethod.Get, "/v1/lobby", null, null, cancellation).ConfigureAwait(false);
            return new LobbyList
            {
                Enabled = !(Value(json, "enabled") is bool enabled) || enabled,
                Notice = Str(json, "notice"),
                MinAppVersion = OnlineService.ParseVersion(Value(json, "minAppVersion")),
                RefreshInterval = TimeSpan.FromSeconds(Math.Max(5, Int(json, "refreshSeconds", 30))),
                Tags = Strings(json, "tags"),
                Rooms = Items(json, "rooms").Select(r => new LobbyRoom
                {
                    Id = Str(r, "id"),
                    Title = Str(r, "title"),
                    Host = Str(r, "host"),
                    Version = Str(r, "version"),
                    Map = Str(r, "map"),
                    Tags = Strings(r, "tags"),
                    Players = Math.Max(1, Int(r, "players", 1)),
                    MaxPlayers = Int(r, "maxPlayers", 0),
                    Country = Str(r, "country"),
                    Region = Str(r, "region"),
                    Locked = Value(r, "locked") is bool locked && locked,
                    Icon = Base64(Str(r, "icon")),
                    Description = Str(r, "description"),
                    CreatedAt = Value(r, "createdAt") is double ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime : DateTime.UtcNow,
                }).Where(r => r.Id.Length > 0 && r.Title.Length > 0).ToList(),
            };
        }

        /// <summary>Lists a room; the ticket is needed for every later heartbeat and for taking it down.</summary>
        public static async Task<LobbyTicket> PublishAsync(LobbyListing listing, int players, CancellationToken cancellation = default)
        {
            var body = new Dictionary<string, object>
            {
                ["owner"] = LinkSettings.LobbyId,
                ["app"] = OnlineService.ShortVersion.ToString(3),
                ["code"] = listing.Code.Code,
                ["title"] = listing.Title ?? "",
                ["host"] = listing.Host ?? "",
                ["version"] = listing.Version ?? "",
                ["map"] = listing.Map ?? "",
                ["tags"] = (listing.Tags ?? Array.Empty<string>()).ToArray<object>(),
                ["players"] = players,
                ["maxPlayers"] = listing.MaxPlayers,
                ["showRegion"] = listing.ShowRegion,
                ["description"] = listing.Description ?? "",
            };
            if (!string.IsNullOrEmpty(listing.Password))
                body["password"] = listing.Password;
            if (listing.Icon != null)
                body["icon"] = Convert.ToBase64String(listing.Icon);
            var json = await SendAsync(HttpMethod.Post, "/v1/lobby/rooms", body, null, cancellation).ConfigureAwait(false);
            string id = Str(json, "id");
            string token = Str(json, "token");
            if (id.Length == 0 || token.Length == 0)
                throw new LobbyException(LobbyError.Unexpected, "The service sent no ticket.");
            return new LobbyTicket
            {
                Id = id,
                Token = token,
                Title = Str(json, "title"),
                HeartbeatInterval = Heartbeat(json),
            };
        }

        /// <summary>Keeps the room listed; returns when to send the next heartbeat.</summary>
        public static async Task<TimeSpan> HeartbeatAsync(LobbyTicket ticket, int players, int maxPlayers,
            CancellationToken cancellation = default)
        {
            var body = new Dictionary<string, object> { ["players"] = players, ["maxPlayers"] = maxPlayers };
            var json = await SendAsync(HttpMethod.Put, "/v1/lobby/rooms/" + ticket.Id, body, ticket.Token, cancellation).ConfigureAwait(false);
            return Heartbeat(json);
        }

        /// <summary>Takes the room down. A listing that is already gone counts as done.</summary>
        public static async Task CloseAsync(LobbyTicket ticket, CancellationToken cancellation = default)
        {
            await SendAsync(HttpMethod.Delete, "/v1/lobby/rooms/" + ticket.Id, null, ticket.Token, cancellation).ConfigureAwait(false);
        }

        /// <summary>The invite code of a listed room; <paramref name="password"/> is needed when the room is locked.</summary>
        public static async Task<RoomCode> GetCodeAsync(string roomId, string password = null, CancellationToken cancellation = default)
        {
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(password))
                body["password"] = password;
            var json = await SendAsync(HttpMethod.Post, "/v1/lobby/rooms/" + roomId + "/join", body, null, cancellation).ConfigureAwait(false);
            return RoomCode.Find(Str(json, "code")) ?? throw new LobbyException(LobbyError.Unexpected, "The service sent no invite code.");
        }

        private static readonly HashSet<string> AllowedNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Asks the service whether a player name passes its word filter. When the service cannot be reached, or
        /// this build has none, the name is let through; the answer for a name is remembered until Campfire closes.
        /// </summary>
        public static async Task<bool> IsNameAllowedAsync(string name, CancellationToken cancellation = default)
        {
            if (!IsAvailable || string.IsNullOrEmpty(name))
                return true;
            lock (AllowedNames)
            {
                if (AllowedNames.Contains(name))
                    return true;
            }

            try
            {
                var json = await SendAsync(HttpMethod.Post, "/v1/lobby/check",
                    new Dictionary<string, object> { ["text"] = name }, null, cancellation).ConfigureAwait(false);
                if (Value(json, "ok") is bool ok && !ok)
                    return false;
            }
            catch (LobbyException)
            {
                return true;
            }
            lock (AllowedNames)
                AllowedNames.Add(name);
            return true;
        }

        /// <summary>Reports a room: "content" when it is not fit to show, "unreachable" when it could not be joined.</summary>
        public static async Task ReportAsync(string roomId, string reason, CancellationToken cancellation = default)
        {
            await SendAsync(HttpMethod.Post, "/v1/lobby/rooms/" + roomId + "/report",
                new Dictionary<string, object> { ["reason"] = reason }, null, cancellation).ConfigureAwait(false);
        }

        private static TimeSpan Heartbeat(Dictionary<string, object> json)
        {
            // The service decides; a broken answer still keeps the room alive with the default.
            return TimeSpan.FromSeconds(Math.Min(Math.Max(Int(json, "heartbeatSeconds", 240), 30), 3600));
        }

        private static async Task<Dictionary<string, object>> SendAsync(HttpMethod method, string path, Dictionary<string, object> body,
            string token, CancellationToken cancellation)
        {
            if (!IsAvailable)
                throw new LobbyException(LobbyError.NoService, "This build has no service.");
            try
            {
                using (var request = new HttpRequestMessage(method, OnlineService.BaseUrl + path))
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    if (body != null)
                        request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                    if (token != null)
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                    using (var response = await OnlineService.Client.SendAsync(request, OnlineService.WithTimeout(timeout, 15)).ConfigureAwait(false))
                    {
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var json = Json.TryParse(text) as Dictionary<string, object>;
                        if (response.IsSuccessStatusCode)
                            return json ?? new Dictionary<string, object>();
                        throw Failure(response.StatusCode, Str(json, "error"), Str(json, "field"));
                    }
                }
            }
            catch (TaskCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellation);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new LobbyException(LobbyError.Network, ex.Message, ex);
            }
        }

        private static LobbyException Failure(HttpStatusCode status, string error, string field)
        {
            string message = "The service answered " + (int)status + (error.Length > 0 ? " (" + error + ")." : ".");
            switch ((int)status)
            {
                case 401 when error == "wrong password" || error == "password required":
                    return new LobbyException(LobbyError.WrongPassword, message);
                case 403:
                    return new LobbyException(LobbyError.Banned, message);
                case 404:
                    return new LobbyException(LobbyError.NotFound, message);
                case 409:
                    return new LobbyException(LobbyError.AlreadyListed, message);
                case 410:
                    return new LobbyException(error == "removed" ? LobbyError.Removed : LobbyError.Reported, message);
                case 422:
                    return new LobbyException(LobbyError.Blocked, message, field: field);
                case 426:
                    return new LobbyException(LobbyError.UpdateRequired, message);
                case 429:
                    return new LobbyException(LobbyError.TooMany, message);
                case 503:
                    return new LobbyException(error == "lobby full" ? LobbyError.Full : LobbyError.Closed, message);
                default:
                    return new LobbyException(LobbyError.Unexpected, message);
            }
        }

        private static object Value(Dictionary<string, object> json, string key)
        {
            return json != null && json.TryGetValue(key, out var value) ? value : null;
        }

        private static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> json, string key)
        {
            return (Value(json, key) as object[] ?? new object[0]).OfType<Dictionary<string, object>>();
        }

        private static string Str(Dictionary<string, object> json, string key)
        {
            return Value(json, key) as string ?? "";
        }

        private static IReadOnlyList<string> Strings(Dictionary<string, object> json, string key)
        {
            return (Value(json, key) as object[] ?? new object[0]).OfType<string>().Where(s => s.Length > 0).ToList();
        }

        private static byte[] Base64(string text)
        {
            if (text.Length == 0)
                return null;
            try
            {
                return Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static int Int(Dictionary<string, object> json, string key, int fallback)
        {
            return Value(json, key) is double d && d >= 0 && d <= int.MaxValue ? (int)d : fallback;
        }
    }
}
