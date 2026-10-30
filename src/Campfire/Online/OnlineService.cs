using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Campfire.Link;

namespace Campfire.Online
{
    /// <summary>A newer release, as the service describes it.</summary>
    internal sealed class UpdateInfo
    {
        public Version Version { get; set; }

        public string Notes { get; set; }

        public string Date { get; set; }

        public long Size { get; set; }

        /// <summary>Download addresses, tried in order; each has the signature at the same index.</summary>
        public string[] Downloads { get; set; }

        public string[] Signatures { get; set; }
    }

    internal sealed class Notice
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Body { get; set; }

        /// <summary>Shown on the home page instead of in a dialog.</summary>
        public bool IsBanner { get; set; }

        /// <summary>Shown on every start instead of only once.</summary>
        public bool Repeats { get; set; }

        public bool IsWarning { get; set; }

        public Version MinVersion { get; set; }

        public Version MaxVersion { get; set; }

        /// <summary>"zh", "en", or empty for every language.</summary>
        public string Language { get; set; }

        public string ButtonText { get; set; }

        public string ButtonUrl { get; set; }

        /// <summary>Title color chosen by the author, or <c>null</c> for the theme's.</summary>
        public Color? TitleColor { get; set; }

        public Color? BodyColor { get; set; }
    }

    /// <summary>What the service returns at startup.</summary>
    internal sealed class Manifest
    {
        public UpdateInfo Update { get; set; }

        public List<Notice> Notices { get; set; } = new List<Notice>();

        /// <summary>Versions below this one must update before they can be used.</summary>
        public Version MinVersion { get; set; }

        /// <summary>How often a long-running Campfire checks again.</summary>
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);
    }

    /// <summary>
    /// The update, notice and statistics service. Only official builds know its address: it is passed to the
    /// build as the ServiceUrl property.
    /// </summary>
    internal static class OnlineService
    {
        private const string CheckKey = "online.check";

        private static readonly Lazy<HttpClient> LazyClient = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Campfire/" + CurrentVersion.ToString(3));
            return client;
        });

        /// <summary>The service address without a trailing slash, or <c>null</c> in unofficial builds.</summary>
        public static string BaseUrl { get; } = ReadServiceUrl();

        public static bool IsAvailable => BaseUrl != null;

        public static Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version;

        internal static HttpClient Client => LazyClient.Value;

        /// <summary>Whether Campfire looks for updates and notices on its own.</summary>
        public static bool CheckEnabled
        {
            get => AppSettings.Get(CheckKey) != "0";
            set => AppSettings.Set(CheckKey, value ? null : "0");
        }

        private static string ReadServiceUrl()
        {
            string url = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ServiceUrl")?.Value?.Trim().TrimEnd('/');
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.IsLoopback) ? url : null;
        }

        /// <summary>Fetches the manifest; returns <c>null</c> when the service cannot be reached.</summary>
        public static async Task<Manifest> FetchAsync(CancellationToken cancellation = default)
        {
            if (!IsAvailable)
                return null;
            try
            {
                // A slow network must not keep the check hanging; it simply runs again next time.
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var response = await Client.GetAsync(BaseUrl + "/v1/manifest", WithTimeout(timeout)).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return null;
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return Parse(Json.TryParse(text) as Dictionary<string, object>);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is FormatException)
            {
                return null;
            }
        }

        internal static CancellationToken WithTimeout(CancellationTokenSource source, int seconds = 10)
        {
            source.CancelAfter(TimeSpan.FromSeconds(seconds));
            return source.Token;
        }

        private static Manifest Parse(Dictionary<string, object> json)
        {
            if (json == null)
                return null;
            var manifest = new Manifest();

            if (Get(json, "update") is Dictionary<string, object> update && ParseVersion(Get(update, "version")) is Version version)
            {
                var pairs = Strings(Get(update, "downloads")).Zip(Strings(Get(update, "signatures")), (d, s) => (Download: d, Signature: s))
                    .Where(p => IsAllowedUrl(p.Download) && IsAllowedUrl(p.Signature))
                    .ToArray();
                var downloads = pairs.Select(p => p.Download).ToArray();
                var signatures = pairs.Select(p => p.Signature).ToArray();
                if (pairs.Length > 0)
                {
                    manifest.Update = new UpdateInfo
                    {
                        Version = version,
                        Notes = Get(update, "notes") as string ?? "",
                        Date = Get(update, "date") as string ?? "",
                        Size = Get(update, "size") is double size ? (long)size : 0,
                        Downloads = downloads,
                        Signatures = signatures,
                    };
                }
            }

            if (Get(json, "notices") is object[] notices)
            {
                foreach (var item in notices.OfType<Dictionary<string, object>>())
                {
                    string id = Get(item, "id") as string;
                    string title = Get(item, "title") as string;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title))
                        continue;
                    string url = Get(item, "buttonUrl") as string;
                    manifest.Notices.Add(new Notice
                    {
                        Id = id,
                        Title = title,
                        Body = Get(item, "body") as string ?? "",
                        IsBanner = Get(item, "style") as string == "banner",
                        Repeats = Get(item, "repeat") as string == "always",
                        IsWarning = Get(item, "level") as string == "warning",
                        MinVersion = ParseVersion(Get(item, "minVersion")),
                        MaxVersion = ParseVersion(Get(item, "maxVersion")),
                        Language = Get(item, "lang") as string ?? "",
                        ButtonText = Get(item, "buttonText") as string,
                        // Only web pages; a notice must never start a program.
                        ButtonUrl = url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null,
                        TitleColor = ParseColor(Get(item, "titleColor")),
                        BodyColor = ParseColor(Get(item, "bodyColor")),
                    });
                }
            }

            if (Get(json, "config") is Dictionary<string, object> config)
            {
                manifest.MinVersion = ParseVersion(Get(config, "minVersion"));
                if (Get(config, "updateCheckHours") is double hours && hours >= 1 && hours <= 24 * 7)
                    manifest.CheckInterval = TimeSpan.FromHours(hours);
            }
            return manifest;
        }

        /// <summary>Reads "a.b.c" as a three-part version.</summary>
        internal static Version ParseVersion(object value)
        {
            if (!(value is string text) || text.Length == 0)
                return null;
            var parts = text.Split('.');
            if (parts.Length != 3)
                return null;
            var numbers = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            }
            return new Version(numbers[0], numbers[1], numbers[2]);
        }

        /// <summary>Reads "#RRGGBB"; anything else means the theme color.</summary>
        private static Color? ParseColor(object value)
        {
            if (!(value is string text) || text.Length != 7 || text[0] != '#'
                || !int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return null;
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        /// <summary>The running version without its fourth part, for comparing with versions from the service.</summary>
        internal static Version ShortVersion => new Version(CurrentVersion.Major, CurrentVersion.Minor, Math.Max(0, CurrentVersion.Build));

        private static string[] Strings(object value)
        {
            return value is object[] items ? items.Select(i => i as string ?? "").ToArray() : new string[0];
        }

        /// <summary>HTTPS, or a local test service. Downloads are verified by their signature either way.</summary>
        private static bool IsAllowedUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
        }

        private static object Get(Dictionary<string, object> json, string key)
        {
            return json.TryGetValue(key, out var value) ? value : null;
        }
    }
}
