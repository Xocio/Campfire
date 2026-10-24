using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Faces of premium players, cut from their skins (face plus hat layer). Skins come from Mojang's public
    /// profile service and are cached on disk for a day.
    /// </summary>
    internal static class PlayerHeads
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(1);
        private static readonly string CacheDirectory = Path.Combine(LinkSettings.DataDirectory, "heads");

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Task<BitmapSource>> Requests =
            new Dictionary<string, Task<BitmapSource>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Lazy<HttpClient> Client =
            new Lazy<HttpClient>(() => new HttpClient { Timeout = TimeSpan.FromSeconds(8) });

        /// <summary>The face of the account <paramref name="uuid"/> if it has been loaded, otherwise <c>null</c>.</summary>
        public static BitmapSource TryGetCached(string uuid)
        {
            if (uuid == null)
                return null;
            lock (Sync)
            {
                return Requests.TryGetValue(uuid, out var request) && request.Status == TaskStatus.RanToCompletion
                    ? request.Result
                    : null;
            }
        }

        /// <summary>
        /// Loads the face of a premium account. Resolves to <c>null</c> when the skin cannot be fetched. Each
        /// account is requested at most once per run.
        /// </summary>
        /// <param name="uuid">Account UUID without dashes.</param>
        public static Task<BitmapSource> GetAsync(string uuid)
        {
            if (uuid == null || uuid.Length != 32)
                return Task.FromResult<BitmapSource>(null);

            lock (Sync)
            {
                if (!Requests.TryGetValue(uuid, out var request))
                {
                    request = Task.Run(() => LoadAsync(uuid.ToLowerInvariant()));
                    Requests[uuid] = request;
                }
                return request;
            }
        }

        private static async Task<BitmapSource> LoadAsync(string uuid)
        {
            string cacheFile = Path.Combine(CacheDirectory, uuid + ".png");
            try
            {
                if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < CacheLifetime)
                    return Decode(File.ReadAllBytes(cacheFile));

                string skinUrl = await LookUpSkinAsync(uuid).ConfigureAwait(false);
                if (skinUrl == null)
                    return null;

                byte[] face = CutFace(await Client.Value.GetByteArrayAsync(skinUrl).ConfigureAwait(false));
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllBytes(cacheFile, face);
                return Decode(face);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TaskCanceledException
                                       || ex is UnauthorizedAccessException || ex is NotSupportedException
                                       || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                LinkLog.Info($"No skin for {uuid}: {ex.Message}");
                // A stale copy is better than nothing when the service cannot be reached.
                return File.Exists(cacheFile) ? TryDecode(cacheFile) : null;
            }
        }

        private static async Task<string> LookUpSkinAsync(string uuid)
        {
            using (var response = await Client.Value
                .GetAsync("https://sessionserver.mojang.com/session/minecraft/profile/" + uuid)
                .ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    return null;
                var profile = Json.TryParse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (!(Get(profile, "properties") is object[] properties))
                    return null;

                foreach (var property in properties)
                {
                    if (!(Get(property, "name") is string kind) || kind != "textures" || !(Get(property, "value") is string encoded))
                        continue;

                    // The value is Base64 JSON: {"textures":{"SKIN":{"url":"http://textures.minecraft.net/..."}}}
                    var textures = Json.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                    if (Get(Get(Get(textures, "textures"), "SKIN"), "url") is string url)
                        return url.Replace("http://", "https://");
                }
                return null;
            }
        }

        private static object Get(object json, string key)
        {
            return json is IDictionary<string, object> map && map.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>Returns an 8×8 PNG of the face with the hat layer drawn over it.</summary>
        private static byte[] CutFace(byte[] skinPng)
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(skinPng), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var skin = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            int scale = Math.Max(1, skin.PixelWidth / 64);
            int size = 8 * scale;

            var face = new byte[size * size * 4];
            var hat = new byte[size * size * 4];
            skin.CopyPixels(new System.Windows.Int32Rect(8 * scale, 8 * scale, size, size), face, size * 4, 0);
            skin.CopyPixels(new System.Windows.Int32Rect(40 * scale, 8 * scale, size, size), hat, size * 4, 0);

            for (int i = 0; i < face.Length; i += 4)
            {
                int alpha = hat[i + 3];
                for (int c = 0; c < 3; c++)
                    face[i + c] = (byte)((hat[i + c] * alpha + face[i + c] * (255 - alpha)) / 255);
                face[i + 3] = 255;
            }

            var result = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, face, size * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            using (var output = new MemoryStream())
            {
                encoder.Save(output);
                return output.ToArray();
            }
        }

        private static BitmapSource TryDecode(string file)
        {
            try
            {
                return Decode(File.ReadAllBytes(file));
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static BitmapSource Decode(byte[] png)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
