using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Online;

namespace Campfire.Worlds
{
    internal enum MapSort
    {
        Popular,
        Downloads,
        Updated,
        Released,
        Name,
    }

    internal sealed class MapCategory
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Slug { get; set; }
    }

    /// <summary>A map as listed in search results.</summary>
    internal class MapSummary
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Summary { get; set; }

        public IReadOnlyList<string> Authors { get; set; }

        public long Downloads { get; set; }

        public DateTime? Updated { get; set; }

        public string Logo { get; set; }

        /// <summary>Game versions of the latest files, newest first.</summary>
        public IReadOnlyList<string> Versions { get; set; }

        public IReadOnlyList<int> Categories { get; set; }

        public string Website { get; set; }

        /// <summary>Whether the author lets other programs download the files; otherwise only the website can.</summary>
        public bool Distribution { get; set; }
    }

    internal sealed class MapScreenshot
    {
        public string Title { get; set; }

        public string Thumbnail { get; set; }

        public string Url { get; set; }
    }

    internal sealed class MapDetail : MapSummary
    {
        public string Description { get; set; }

        public IReadOnlyList<MapScreenshot> Screenshots { get; set; }

        public IReadOnlyList<MapFile> Files { get; set; }
    }

    internal sealed class MapFile
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string FileName { get; set; }

        public IReadOnlyList<string> Versions { get; set; }

        public IReadOnlyList<string> Loaders { get; set; }

        public long Size { get; set; }

        public DateTime? Date { get; set; }

        /// <summary>"release", "beta" or "alpha".</summary>
        public string Type { get; set; }

        public string Sha1 { get; set; }

        /// <summary>The download address, or <c>null</c> when the author keeps the file to the website.</summary>
        public string Url { get; set; }

        /// <summary>The file's page on the website.</summary>
        public string Page { get; set; }
    }

    internal sealed class MapSearchResult
    {
        public int Total { get; set; }

        public int PageSize { get; set; }

        public IReadOnlyList<MapSummary> Maps { get; set; }
    }

    internal sealed class MapCatalogException : Exception
    {
        public MapCatalogException(bool unavailable, string message, Exception inner = null) : base(message, inner)
        {
            Unavailable = unavailable;
        }

        /// <summary>The service answered that maps cannot be browsed right now, as opposed to a network failure.</summary>
        public bool Unavailable { get; }
    }

    /// <summary>
    /// Maps published on CurseForge, read through the Campfire service, which holds the API key. Only official
    /// builds know the service; others cannot browse maps but still import downloaded ones.
    /// </summary>
    internal static class MapCatalog
    {
        public static bool IsAvailable => OnlineService.IsAvailable;

        public static async Task<IReadOnlyList<MapCategory>> CategoriesAsync(CancellationToken cancellation = default)
        {
            var json = await GetAsync("/v1/maps/categories", cancellation).ConfigureAwait(false);
            return Items(json, "categories")
                .Select(c => new MapCategory { Id = Int(c, "id"), Name = Str(c, "name"), Slug = Str(c, "slug") })
                .Where(c => c.Id > 0 && c.Name.Length > 0)
                .ToList();
        }

        public static async Task<MapSearchResult> SearchAsync(string query, int category, string version, MapSort sort, int page,
            CancellationToken cancellation = default)
        {
            var parameters = new List<string>();
            if (!string.IsNullOrWhiteSpace(query))
                parameters.Add("q=" + Uri.EscapeDataString(query.Trim()));
            if (category > 0)
                parameters.Add("category=" + category.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(version))
                parameters.Add("version=" + Uri.EscapeDataString(version));
            parameters.Add("sort=" + sort.ToString().ToLowerInvariant());
            parameters.Add("page=" + page.ToString(CultureInfo.InvariantCulture));

            var json = await GetAsync("/v1/maps/search?" + string.Join("&", parameters), cancellation).ConfigureAwait(false);
            return new MapSearchResult
            {
                Total = Int(json, "total"),
                PageSize = Math.Max(1, Int(json, "pageSize")),
                Maps = Items(json, "maps").Select(m => FillSummary(new MapSummary(), m)).Where(m => m.Id > 0).ToList(),
            };
        }

        public static async Task<MapDetail> GetAsync(int id, CancellationToken cancellation = default)
        {
            var json = await GetAsync("/v1/maps/" + id.ToString(CultureInfo.InvariantCulture), cancellation).ConfigureAwait(false);
            if (!(Value(json, "map") is Dictionary<string, object> map))
                throw new MapCatalogException(false, "The service sent no map.");

            var detail = FillSummary(new MapDetail(), map);
            detail.Description = Str(map, "description");
            detail.Screenshots = Items(map, "screenshots")
                .Select(s => new MapScreenshot { Title = Str(s, "title"), Thumbnail = Str(s, "thumb"), Url = Str(s, "url") })
                .Where(s => MapImages.IsAllowed(s.Thumbnail))
                .ToList();
            detail.Files = Items(json, "files").Select(f => new MapFile
            {
                Id = Int(f, "id"),
                Name = Str(f, "name"),
                FileName = Str(f, "fileName"),
                Versions = Strings(f, "versions"),
                Loaders = Strings(f, "loaders"),
                Size = Long(f, "size"),
                Date = Date(f, "date"),
                Type = Str(f, "type"),
                Sha1 = Str(f, "sha1"),
                Url = Value(f, "url") as string,
                Page = Str(f, "page"),
            }).Where(f => f.Id > 0).ToList();
            return detail;
        }

        private static T FillSummary<T>(T map, Dictionary<string, object> json) where T : MapSummary
        {
            map.Id = Int(json, "id");
            map.Name = Str(json, "name");
            map.Summary = Str(json, "summary");
            map.Authors = Strings(json, "authors");
            map.Downloads = Long(json, "downloads");
            map.Updated = Date(json, "updated");
            map.Logo = Str(json, "logo");
            map.Versions = Strings(json, "versions");
            map.Categories = (Value(json, "categories") as object[] ?? new object[0]).OfType<double>().Select(d => (int)d).ToList();
            map.Website = Str(json, "website");
            map.Distribution = !(Value(json, "distribution") is bool allowed) || allowed;
            return map;
        }

        private static async Task<Dictionary<string, object>> GetAsync(string path, CancellationToken cancellation)
        {
            if (!IsAvailable)
                throw new MapCatalogException(true, "This build has no service.");
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var response = await OnlineService.Client.GetAsync(OnlineService.BaseUrl + path, OnlineService.WithTimeout(timeout, 20))
                           .ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable || response.StatusCode == HttpStatusCode.NotFound)
                        throw new MapCatalogException(true, "The service answered " + (int)response.StatusCode + ".");
                    if (!response.IsSuccessStatusCode)
                        throw new MapCatalogException(false, "The service answered " + (int)response.StatusCode + ".");
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return Json.TryParse(text) as Dictionary<string, object>
                           ?? throw new MapCatalogException(false, "The service sent no valid answer.");
                }
            }
            catch (TaskCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellation);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new MapCatalogException(false, ex.Message, ex);
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

        private static int Int(Dictionary<string, object> json, string key)
        {
            return Value(json, key) is double d && d >= 0 && d <= int.MaxValue ? (int)d : 0;
        }

        private static long Long(Dictionary<string, object> json, string key)
        {
            return Value(json, key) is double d && d >= 0 && d <= long.MaxValue ? (long)d : 0;
        }

        private static DateTime? Date(Dictionary<string, object> json, string key)
        {
            return DateTime.TryParse(Str(json, key), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var date) ? date : (DateTime?)null;
        }
    }
}
