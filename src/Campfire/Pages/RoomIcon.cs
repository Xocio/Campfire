using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// Room pictures for the lobby: a square of <see cref="Size"/> pixels, small enough to travel with the room list.
    /// </summary>
    internal static class RoomIcon
    {
        public const int Size = 64;

        private static BitmapSource _default;

        /// <summary>
        /// The picture of a room that has none: a 16 by 16 grass block side drawn here in code, in the blocky style
        /// of the game. It is shown without smoothing.
        /// </summary>
        public static BitmapSource Default => _default ?? (_default = BuildDefault());

        /// <summary>An image brush for <see cref="Default"/> that keeps the pixels sharp when scaled.</summary>
        public static ImageBrush DefaultBrush()
        {
            var brush = new ImageBrush(Default) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            return brush;
        }

        private static BitmapSource BuildDefault()
        {
            const int n = 16;
            var grass = new[] { Color.FromRgb(0x5E, 0xA3, 0x32), Color.FromRgb(0x6B, 0xB3, 0x3A), Color.FromRgb(0x52, 0x92, 0x2A), Color.FromRgb(0x78, 0xC0, 0x45) };
            var dirt = new[] { Color.FromRgb(0x86, 0x60, 0x43), Color.FromRgb(0x96, 0x6C, 0x4B), Color.FromRgb(0x73, 0x52, 0x38), Color.FromRgb(0x7D, 0x59, 0x3D) };
            var pixels = new byte[n * n * 4];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // The grass edge hangs a little lower in some columns, like the game's texture.
                    int edge = 3 + (Hash(x, 99) % 3 == 0 ? 2 : Hash(x, 98) % 2 == 0 ? 1 : 0);
                    var palette = y < edge ? grass : dirt;
                    var c = palette[Hash(x, y) % palette.Length];
                    int i = (y * n + x) * 4;
                    pixels[i] = c.B;
                    pixels[i + 1] = c.G;
                    pixels[i + 2] = c.R;
                    pixels[i + 3] = 255;
                }
            }
            var image = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, pixels, n * 4);
            image.Freeze();
            return image;
        }

        /// <summary>A fixed scatter of small numbers.</summary>
        private static int Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (int)(h % 1000);
            }
        }

        /// <summary>The largest picture the service accepts.</summary>
        private const int MaxBytes = 8192;

        /// <summary>Pictures larger than this are not opened at all.</summary>
        private const long MaxFileBytes = 20L * 1024 * 1024;

        /// <summary>
        /// The picture of the world the host plays, from its icon.png. The world is found by its name among the saves
        /// of running games; the one played last wins when several share the name.
        /// </summary>
        /// <remarks>Searching runs in the background; the picture is made on the calling thread, which must be the UI thread.</remarks>
        public static async Task<byte[]> FindMapIconAsync(string worldName)
        {
            if (string.IsNullOrWhiteSpace(worldName))
                return null;
            string icon = await Task.Run(() =>
            {
                try
                {
                    return RunningGame.Find()
                        .Select(game => game.SavesDirectory)
                        .Where(Directory.Exists)
                        .SelectMany(Directory.EnumerateDirectories)
                        .Select(world => (Icon: Path.Combine(world, "icon.png"), Level: Path.Combine(world, "level.dat")))
                        .Where(w => File.Exists(w.Icon) && File.Exists(w.Level) && LevelName(w.Level) == worldName.Trim())
                        .OrderByDescending(w => File.GetLastWriteTimeUtc(w.Level))
                        .Select(w => w.Icon)
                        .FirstOrDefault();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return null;
                }
            });
            return icon == null ? null : FromFile(icon);
        }

        /// <summary>A picture the user picked, made into a room picture; <c>null</c> when it cannot be read. UI thread only.</summary>
        public static byte[] FromFile(string path)
        {
            try
            {
                return new FileInfo(path).Length > MaxFileBytes ? null : Encode(Load(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException
                                       || ex is FileFormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>Shows room picture bytes; <c>null</c> when they are not a picture.</summary>
        public static BitmapSource Decode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(data);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is FileFormatException || ex is ArgumentException
                                       || ex is InvalidOperationException)
            {
                return null;
            }
        }

        private static string LevelName(string levelDat)
        {
            try
            {
                return Nbt.GetString(Nbt.Read(File.ReadAllBytes(levelDat)), "Data", "LevelName")?.Trim();
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        private static BitmapSource Load(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }

        /// <summary>
        /// Crops the middle square, scales it to <see cref="Size"/> and encodes it: PNG keeps transparency and
        /// pixel art sharp; a photo too large for PNG becomes JPEG on a white background.
        /// </summary>
        private static byte[] Encode(BitmapSource source)
        {
            if (source == null || source.PixelWidth == 0 || source.PixelHeight == 0)
                return null;
            int side = Math.Min(source.PixelWidth, source.PixelHeight);
            var square = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));

            // Pictures already this size or smaller, such as map icons, are scaled without blurring their pixels.
            bool pixelArt = side <= Size;
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, pixelArt ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            using (var context = visual.RenderOpen())
                context.DrawImage(square, new Rect(0, 0, Size, Size));
            var scaled = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            scaled.Render(visual);

            byte[] png = Save(new PngBitmapEncoder(), scaled);
            if (png.Length <= MaxBytes)
                return png;

            var flat = new DrawingVisual();
            using (var context = flat.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, Size, Size));
                context.DrawImage(scaled, new Rect(0, 0, Size, Size));
            }
            var opaque = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            opaque.Render(flat);
            foreach (int quality in new[] { 90, 75, 60 })
            {
                byte[] jpeg = Save(new JpegBitmapEncoder { QualityLevel = quality }, opaque);
                if (jpeg.Length <= MaxBytes)
                    return jpeg;
            }
            return null;
        }

        private static byte[] Save(BitmapEncoder encoder, BitmapSource image)
        {
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }
    }
}
