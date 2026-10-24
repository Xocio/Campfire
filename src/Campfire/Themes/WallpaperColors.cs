using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;

namespace Campfire.Themes
{
    /// <summary>
    /// Picks theme colors out of the desktop wallpaper: the hues that cover a fair part of the picture and are
    /// colorful enough to carry a theme, most suitable first and clearly different from each other.
    /// </summary>
    internal static class WallpaperColors
    {
        private const int SampleSize = 128;
        private const int MaxColors = 4;

        /// <summary>Up to four candidate colors; empty when the wallpaper is missing or has no usable color.</summary>
        public static IReadOnlyList<MediaColor> Extract()
        {
            try
            {
                using (var bitmap = LoadWallpaper())
                {
                    if (bitmap != null)
                        return Score(Sample(bitmap));
                }
                var solid = SolidBackground();
                return solid.HasValue && Oklch.FromColor(solid.Value).C >= 0.04
                    ? new[] { solid.Value }
                    : Array.Empty<MediaColor>();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                                       || ex is ExternalException || ex is OutOfMemoryException)
            {
                return Array.Empty<MediaColor>();
            }
        }

        /// <summary>The color Windows uses for window borders and the Start menu.</summary>
        public static MediaColor? SystemAccent()
        {
            try
            {
                if (DwmGetColorizationColor(out uint argb, out _) == 0)
                    return MediaColor.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
            return null;
        }

        // ======================== Loading ========================

        private static Bitmap LoadWallpaper()
        {
            foreach (string path in WallpaperPaths())
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;
                // Read through a copy in memory so the file is never locked.
                var bytes = File.ReadAllBytes(path);
                using (var stream = new MemoryStream(bytes))
                using (var image = Image.FromStream(stream, false, false))
                {
                    var bitmap = new Bitmap(SampleSize, SampleSize, PixelFormat.Format32bppArgb);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        graphics.DrawImage(image, 0, 0, SampleSize, SampleSize);
                    }
                    return bitmap;
                }
            }
            return null;
        }

        private static IEnumerable<string> WallpaperPaths()
        {
            var buffer = new StringBuilder(520);
            if (SystemParametersInfo(GetDeskWallpaper, buffer.Capacity, buffer, 0))
                yield return buffer.ToString();

            // A slideshow or a picture from the Settings app is kept as a converted copy here.
            string themes = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes");
            yield return Path.Combine(themes, "TranscodedWallpaper");
            yield return Path.Combine(themes, "TranscodedWallpaper.jpg");
        }

        private static MediaColor? SolidBackground()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
            {
                var parts = (key?.GetValue("Background") as string)?.Split(' ');
                if (parts == null || parts.Length != 3)
                    return null;
                if (byte.TryParse(parts[0], out byte r) && byte.TryParse(parts[1], out byte g) && byte.TryParse(parts[2], out byte b))
                    return MediaColor.FromRgb(r, g, b);
                return null;
            }
        }

        private static List<MediaColor> Sample(Bitmap bitmap)
        {
            var colors = new List<MediaColor>(SampleSize * SampleSize);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[data.Stride];
                for (int y = 0; y < data.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    for (int x = 0; x < data.Width; x++)
                    {
                        int i = x * 4;
                        if (row[i + 3] == 255)
                            colors.Add(MediaColor.FromRgb(row[i + 2], row[i + 1], row[i]));
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return colors;
        }

        // ======================== Scoring ========================

        /// <summary>
        /// Groups pixels by hue, then rates each hue by how much of the picture lies near it and how colorful it is.
        /// Gray, very dark and very bright pixels never decide the theme.
        /// </summary>
        private static IReadOnlyList<MediaColor> Score(List<MediaColor> pixels)
        {
            var counts = new double[360];
            var sumL = new double[360];
            var sumC = new double[360];
            int usable = 0;
            foreach (var pixel in pixels)
            {
                var color = Oklch.FromColor(pixel);
                if (color.C < 0.03 || color.L < 0.2 || color.L > 0.96)
                    continue;
                int bin = (int)color.H % 360;
                counts[bin]++;
                sumL[bin] += color.L;
                sumC[bin] += color.C;
                usable++;
            }
            if (usable < pixels.Count * 0.01 || usable == 0)
                return Array.Empty<MediaColor>();

            var candidates = new List<(double Score, Oklch Color)>();
            for (int hue = 0; hue < 360; hue++)
            {
                if (counts[hue] == 0)
                    continue;

                // The share of colorful pixels within 15 degrees of this hue.
                double near = 0;
                for (int offset = -15; offset <= 15; offset++)
                    near += counts[(hue + offset + 360) % 360];
                double share = near / usable;
                double chroma = sumC[hue] / counts[hue];
                if (share < 0.01 || chroma < 0.04)
                    continue;

                double lightness = sumL[hue] / counts[hue];
                double score = share * 70 + (chroma - 0.12) * (chroma < 0.12 ? 25 : 75);
                candidates.Add((score, new Oklch(lightness, chroma, hue)));
            }

            // Prefer hues far apart, and accept closer ones only when nothing else is left.
            var sorted = candidates.OrderByDescending(c => c.Score).ToList();
            for (int spacing = 90; spacing >= 15; spacing -= 15)
            {
                var chosen = new List<Oklch>();
                foreach (var candidate in sorted)
                {
                    if (chosen.All(c => HueDistance(c.H, candidate.Color.H) >= spacing))
                        chosen.Add(candidate.Color);
                    if (chosen.Count == MaxColors)
                        break;
                }
                if (chosen.Count == MaxColors || spacing == 15)
                    return chosen.Select(c => c.ToColor()).ToList();
            }
            return Array.Empty<MediaColor>();
        }

        private static double HueDistance(double first, double second)
        {
            double distance = Math.Abs(first - second) % 360;
            return distance > 180 ? 360 - distance : distance;
        }

        private const int GetDeskWallpaper = 0x0073;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(int action, int param, StringBuilder value, int winIni);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint color, out bool opaqueBlend);
    }
}
