using System;
using System.Windows.Media;

namespace Campfire.Themes
{
    /// <summary>
    /// A color in the OKLCH space: perceived lightness (0 to 1), chroma (0 to about 0.37) and hue in degrees.
    /// Equal steps in lightness look equally far apart, which keeps generated palettes even.
    /// </summary>
    internal struct Oklch
    {
        public Oklch(double lightness, double chroma, double hue)
        {
            L = lightness;
            C = chroma;
            H = (hue % 360 + 360) % 360;
        }

        public double L { get; }

        public double C { get; }

        public double H { get; }

        public static Oklch FromColor(Color color)
        {
            ColorMath.ToOklab(color, out double l, out double a, out double b);
            double chroma = Math.Sqrt(a * a + b * b);
            double hue = Math.Atan2(b, a) * 180 / Math.PI;
            return new Oklch(l, chroma, hue);
        }

        /// <summary>The nearest displayable color, keeping lightness and hue and giving up chroma where needed.</summary>
        public Color ToColor(byte alpha = 255)
        {
            double low = 0, high = C;
            if (ColorMath.InGamut(L, high, H))
                return ColorMath.FromOklch(L, high, H, alpha);
            for (int i = 0; i < 18; i++)
            {
                double middle = (low + high) / 2;
                if (ColorMath.InGamut(L, middle, H))
                    low = middle;
                else
                    high = middle;
            }
            return ColorMath.FromOklch(L, low, H, alpha);
        }
    }

    internal static class ColorMath
    {
        public static void ToOklab(Color color, out double l, out double a, out double b)
        {
            double r = ToLinear(color.R / 255.0), g = ToLinear(color.G / 255.0), bl = ToLinear(color.B / 255.0);
            double lc = Math.Pow(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * bl, 1.0 / 3);
            double mc = Math.Pow(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * bl, 1.0 / 3);
            double sc = Math.Pow(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * bl, 1.0 / 3);
            l = 0.2104542553 * lc + 0.7936177850 * mc - 0.0040720468 * sc;
            a = 1.9779984951 * lc - 2.4285922050 * mc + 0.4505937099 * sc;
            b = 0.0259040371 * lc + 0.7827717662 * mc - 0.8086757660 * sc;
        }

        public static bool InGamut(double lightness, double chroma, double hue)
        {
            ToLinearRgb(lightness, chroma, hue, out double r, out double g, out double b);
            const double e = 1e-4;
            return r >= -e && r <= 1 + e && g >= -e && g <= 1 + e && b >= -e && b <= 1 + e;
        }

        public static Color FromOklch(double lightness, double chroma, double hue, byte alpha)
        {
            ToLinearRgb(lightness, chroma, hue, out double r, out double g, out double b);
            return Color.FromArgb(alpha, ToByte(r), ToByte(g), ToByte(b));
        }

        /// <summary>Contrast ratio between two colors as defined for text legibility (1 to 21).</summary>
        public static double Contrast(Color first, Color second)
        {
            double one = Luminance(first), two = Luminance(second);
            return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
        }

        public static Color Parse(string text, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;
            string hex = text.Trim().TrimStart('#');
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint value))
                return fallback;
            return Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        public static string ToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static double Luminance(Color color)
        {
            return 0.2126 * ToLinear(color.R / 255.0) + 0.7152 * ToLinear(color.G / 255.0) + 0.0722 * ToLinear(color.B / 255.0);
        }

        private static void ToLinearRgb(double lightness, double chroma, double hue, out double r, out double g, out double b)
        {
            double radians = hue * Math.PI / 180;
            double a = chroma * Math.Cos(radians), bb = chroma * Math.Sin(radians);
            double lc = lightness + 0.3963377774 * a + 0.2158037573 * bb;
            double mc = lightness - 0.1055613458 * a - 0.0638541728 * bb;
            double sc = lightness - 0.0894841775 * a - 1.2914855480 * bb;
            lc = lc * lc * lc;
            mc = mc * mc * mc;
            sc = sc * sc * sc;
            r = 4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc;
            g = -1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc;
            b = -0.0041960863 * lc - 0.7034186147 * mc + 1.7076147010 * sc;
        }

        private static double ToLinear(double channel)
        {
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        private static byte ToByte(double linear)
        {
            linear = Math.Max(0, Math.Min(1, linear));
            double srgb = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            return (byte)Math.Round(Math.Max(0, Math.Min(1, srgb)) * 255);
        }
    }
}
