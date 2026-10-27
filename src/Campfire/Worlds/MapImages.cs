using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Campfire.Online;

namespace Campfire.Worlds
{
    /// <summary>
    /// Loads map logos and screenshots from CurseForge's image servers, kept in memory while Campfire runs.
    /// </summary>
    internal static class MapImages
    {
        private const int MaxCached = 300;
        private const long MaxImageSize = 8L * 1024 * 1024;

        private static readonly Dictionary<string, Task<ImageSource>> Cache = new Dictionary<string, Task<ImageSource>>();
        private static readonly Queue<string> Order = new Queue<string>();

        /// <summary>Images and files only come from CurseForge's servers, or from a local test service.</summary>
        internal static bool IsAllowed(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            if (uri.IsLoopback)
                return true;
            return uri.Scheme == Uri.UriSchemeHttps
                   && (uri.Host.Equals("forgecdn.net", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith(".forgecdn.net", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The image at <paramref name="url"/> decoded to <paramref name="width"/> pixels, or <c>null</c> when it
        /// cannot be loaded. Call from the UI thread.
        /// </summary>
        public static Task<ImageSource> LoadAsync(string url, int width)
        {
            if (!IsAllowed(url))
                return Task.FromResult<ImageSource>(null);
            string key = width + " " + url;
            // An image that failed to load is tried again the next time it is shown.
            if (Cache.TryGetValue(key, out var cached) && !(cached.IsCompleted && cached.Result == null))
                return cached;

            var task = Task.Run(() => DownloadAsync(url, width));
            if (cached == null)
            {
                Order.Enqueue(key);
                while (Order.Count >= MaxCached)
                    Cache.Remove(Order.Dequeue());
            }
            Cache[key] = task;
            return task;
        }

        private static async Task<ImageSource> DownloadAsync(string url, int width)
        {
            try
            {
                using (var response = await OnlineService.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageSize)
                        return null;
                    byte[] data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (data.Length > MaxImageSize)
                        return null;

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.DecodePixelWidth = width;
                    image.StreamSource = new MemoryStream(data);
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException
                                       || ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException
                                       || ex is System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }
    }
}
