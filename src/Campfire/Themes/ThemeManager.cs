using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Campfire.Controls;

namespace Campfire.Themes
{
    internal enum ThemeMode
    {
        System,
        Light,
        Dark,
    }

    /// <summary>How the palette is derived from the key color.</summary>
    internal enum PaletteStyle
    {
        /// <summary>Balanced colors close to the key color.</summary>
        TonalSpot,
        /// <summary>The most colorful version the screen can show.</summary>
        Vibrant,
        /// <summary>Shifts the hue for a livelier, less literal theme.</summary>
        Expressive,
        /// <summary>Mostly gray with a hint of the key color.</summary>
        Neutral,
        /// <summary>Gray only.</summary>
        Monochrome,
        /// <summary>Keeps the key color itself as the accent.</summary>
        Fidelity,
    }

    /// <summary>
    /// Builds every color of the interface from one key color and puts them into the window resources, where
    /// every themed element picks them up at once.
    /// </summary>
    internal static class ThemeManager
    {
        public static readonly Color DefaultSeed = Color.FromRgb(0xE2, 0x68, 0x0C);

        /// <summary>Ready-made key colors offered next to the default.</summary>
        public static readonly Color[] Presets =
        {
            Color.FromRgb(0xE2, 0x68, 0x0C), // campfire orange
            Color.FromRgb(0xD9, 0x3A, 0x3A), // red
            Color.FromRgb(0xD9, 0x4A, 0x8C), // pink
            Color.FromRgb(0x8E, 0x5B, 0xD6), // purple
            Color.FromRgb(0x3D, 0x6F, 0xE0), // blue
            Color.FromRgb(0x14, 0x9C, 0xB5), // teal
            Color.FromRgb(0x2E, 0x9E, 0x5B), // green
            Color.FromRgb(0xC9, 0x9A, 0x06), // gold
            Color.FromRgb(0x8D, 0x6E, 0x5A), // brown
        };

        private static ResourceDictionary _resources;
        private static DispatcherTimer _systemChange;

        /// <summary>Raised on the UI thread after the colors changed.</summary>
        public static event EventHandler Changed;

        // ======================== Settings ========================

        public static ThemeMode Mode
        {
            get
            {
                switch (AppSettings.Get("theme.mode"))
                {
                    case "light": return ThemeMode.Light;
                    case "dark": return ThemeMode.Dark;
                    default: return ThemeMode.System;
                }
            }
            set => AppSettings.Set("theme.mode", value == ThemeMode.Light ? "light" : value == ThemeMode.Dark ? "dark" : null);
        }

        /// <summary>Whether the key color comes from the desktop wallpaper.</summary>
        public static bool UseWallpaper
        {
            get => AppSettings.Get("theme.monet") == "1";
            set => AppSettings.Set("theme.monet", value ? "1" : null);
        }

        /// <summary>Which of the wallpaper's colors is used.</summary>
        public static int WallpaperChoice
        {
            get => int.TryParse(AppSettings.Get("theme.monet.index"), out int index) && index >= 0 ? index : 0;
            set => AppSettings.Set("theme.monet.index", value > 0 ? value.ToString() : null);
        }

        /// <summary>The chosen key color as "#RRGGBB", or "system" for the Windows accent color.</summary>
        public static string ChosenColor
        {
            get => AppSettings.Get("theme.color") ?? ColorMath.ToHex(DefaultSeed);
            set => AppSettings.Set("theme.color", value);
        }

        public static PaletteStyle Style
        {
            get => Enum.TryParse(AppSettings.Get("theme.style"), out PaletteStyle style) ? style : PaletteStyle.TonalSpot;
            set => AppSettings.Set("theme.style", value == PaletteStyle.TonalSpot ? null : value.ToString());
        }

        // ======================== State ========================

        public static bool IsDark { get; private set; }

        /// <summary>The key color in effect.</summary>
        public static Color Seed { get; private set; } = DefaultSeed;

        /// <summary>Colors found in the wallpaper, best first; may be empty.</summary>
        public static IReadOnlyList<Color> WallpaperCandidates { get; private set; } = Array.Empty<Color>();

        public static void Initialize(ResourceDictionary resources)
        {
            _resources = resources;
            if (UseWallpaper)
                WallpaperCandidates = WallpaperColors.Extract();
            Apply(false);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public static void Shutdown()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        /// <summary>Reads the wallpaper again; the work happens in the background.</summary>
        public static async Task RefreshWallpaperAsync()
        {
            WallpaperCandidates = await Task.Run(() => WallpaperColors.Extract());
            Apply(true);
        }

        /// <summary>Recomputes the palette from the current settings.</summary>
        /// <param name="animate">Whether every color fades from its old value to the new one.</param>
        public static void Apply(bool animate)
        {
            if (_resources == null)
                return;

            IsDark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && SystemPrefersDark());
            Seed = ResolveSeed();
            var palette = Palette.Create(Seed, Style, IsDark);
            bool fade = animate && Motion.Enabled;
            foreach (var entry in palette.Solids)
            {
                var from = fade && _resources[entry.Key] is SolidColorBrush old ? old.Color : (Color?)null;
                _resources[entry.Key] = CreateSolid(from, entry.Value);
            }
            foreach (var entry in palette.Gradients)
            {
                var from = fade && _resources[entry.Key] is GradientBrush old && old.GradientStops.Count == entry.Value.Length
                    ? old.GradientStops.Select(s => s.Color).ToArray()
                    : null;
                _resources[entry.Key] = CreateGradient(entry.Key, from, entry.Value);
            }
            foreach (var entry in palette.Colors)
                _resources[entry.Key] = entry.Value;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static Color ResolveSeed()
        {
            if (UseWallpaper)
            {
                if (WallpaperCandidates.Count > 0)
                    return WallpaperCandidates[Math.Min(WallpaperChoice, WallpaperCandidates.Count - 1)];
                return WallpaperColors.SystemAccent() ?? DefaultSeed;
            }
            string chosen = ChosenColor;
            if (chosen == "system")
                return WallpaperColors.SystemAccent() ?? DefaultSeed;
            return ColorMath.Parse(chosen, DefaultSeed);
        }

        /// <summary>Whether Windows is set to dark mode for apps; always light where the setting does not exist.</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Desktop
                && e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.VisualStyle)
                return;
            bool wallpaper = e.Category == UserPreferenceCategory.Desktop;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => ScheduleSystemChange(wallpaper)));
        }

        /// <summary>Windows sends several notices for one change; react once they settle.</summary>
        private static void ScheduleSystemChange(bool wallpaper)
        {
            if (_systemChange == null)
            {
                _systemChange = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _systemChange.Tick += async (sender, args) =>
                {
                    _systemChange.Stop();
                    bool reread = (bool)_systemChange.Tag;
                    _systemChange.Tag = false;
                    if (reread && UseWallpaper)
                        await RefreshWallpaperAsync();
                    else
                        Apply(true);
                };
                _systemChange.Tag = false;
            }
            _systemChange.Tag = (bool)_systemChange.Tag || wallpaper;
            _systemChange.Stop();
            _systemChange.Start();
        }

        // ======================== Resources ========================

        // Every change hands out new brushes. A brush that fades starts its animation before it goes into the
        // resources: the window may seal what it holds, but a brush that is still animating cannot be sealed.
        private static readonly Duration TransitionTime = new Duration(TimeSpan.FromMilliseconds(400));
        private static readonly IEasingFunction TransitionEase = CreateEase();

        private static SolidColorBrush CreateSolid(Color? from, Color to)
        {
            var brush = new SolidColorBrush(from ?? to);
            if (from.HasValue && from.Value != to)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, Fade(to));
            else
                brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush CreateGradient(string key, Color[] from, Color[] to)
        {
            var brush = key == "BrushTitleBar"
                ? new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) }
                : new LinearGradientBrush { StartPoint = new Point(0.1, 0), EndPoint = new Point(0.9, 1) };
            bool fades = false;
            for (int i = 0; i < to.Length; i++)
            {
                var stop = new GradientStop(from?[i] ?? to[i], i / (double)(to.Length - 1));
                if (from != null && from[i] != to[i])
                {
                    stop.BeginAnimation(GradientStop.ColorProperty, Fade(to[i]));
                    fades = true;
                }
                brush.GradientStops.Add(stop);
            }
            if (!fades)
                brush.Freeze();
            return brush;
        }

        private static ColorAnimation Fade(Color to)
        {
            return new ColorAnimation(to, TransitionTime) { EasingFunction = TransitionEase };
        }

        private static IEasingFunction CreateEase()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            ease.Freeze();
            return ease;
        }

        // ======================== Palette ========================

        /// <summary>Every themed color of the interface, worked out from a key color.</summary>
        private sealed class Palette
        {
            public Dictionary<string, Color> Solids { get; } = new Dictionary<string, Color>();

            public Dictionary<string, Color[]> Gradients { get; } = new Dictionary<string, Color[]>();

            public Dictionary<string, Color> Colors { get; } = new Dictionary<string, Color>();

            public static Palette Create(Color seedColor, PaletteStyle style, bool dark)
            {
                var seed = Oklch.FromColor(seedColor);
                double hue = seed.H;
                double accent, neutral;
                switch (style)
                {
                    case PaletteStyle.Vibrant:
                        accent = 0.22;
                        neutral = 0.02;
                        break;
                    case PaletteStyle.Expressive:
                        hue -= 60;
                        accent = 0.15;
                        neutral = 0.018;
                        break;
                    case PaletteStyle.Neutral:
                        accent = 0.045;
                        neutral = 0.008;
                        break;
                    case PaletteStyle.Monochrome:
                        accent = 0;
                        neutral = 0;
                        break;
                    case PaletteStyle.Fidelity:
                        accent = Math.Min(seed.C, 0.26);
                        neutral = Math.Min(seed.C * 0.1, 0.016);
                        break;
                    default:
                        accent = Math.Max(0.10, Math.Min(seed.C, 0.16));
                        neutral = 0.014;
                        break;
                }
                // A gray key color has no meaningful hue; keep it gray rather than inventing one.
                if (seed.C < 0.02 && style != PaletteStyle.Vibrant && style != PaletteStyle.Expressive)
                {
                    accent = Math.Min(accent, 0.02);
                    neutral = Math.Min(neutral, 0.004);
                }

                var palette = new Palette();
                if (dark)
                    palette.FillDark(hue, accent, neutral);
                else
                    palette.FillLight(hue, accent, neutral, style == PaletteStyle.Fidelity ? seed : (Oklch?)null);
                return palette;
            }

            private void FillLight(double hue, double accent, double neutral, Oklch? exact)
            {
                Color A(double l, double c, byte alpha = 255) => new Oklch(l, c, hue).ToColor(alpha);
                Color N(double l, double factor, byte alpha = 255) => new Oklch(l, neutral * factor, hue).ToColor(alpha);

                var primary = exact.HasValue
                    ? new Oklch(Math.Max(0.5, Math.Min(0.68, exact.Value.L)), accent, hue)
                    : new Oklch(0.645, accent, hue);
                SetRamp(
                    N(0.34, 2.2),
                    A(primary.L - 0.095, accent),
                    primary.ToColor(),
                    A(0.73, accent * 0.8),
                    A(0.84, accent * 0.45),
                    A(0.925, Math.Min(0.045, accent * 0.3)),
                    A(0.950, Math.Min(0.032, accent * 0.22)),
                    A(0.972, Math.Min(0.020, accent * 0.14)));
                SetGrays(N(0.38, 0.6), N(0.52, 0.6), N(0.60, 0.6), N(0.72, 0.6), N(0.89, 0.6));

                Solids["BrushOnAccent"] = System.Windows.Media.Colors.White;
                Solids["BrushTitleAccent"] = primary.ToColor();
                Solids["BrushSurface"] = System.Windows.Media.Colors.White;
                Solids["BrushCard"] = N(0.995, 0.3, 0xCC);
                Solids["BrushCardHover"] = N(0.995, 0.3, 0xEB);
                Solids["BrushTile"] = N(0.995, 0.3, 0x9E);
                Solids["BrushInput"] = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
                Solids["BrushInputFocus"] = System.Windows.Media.Colors.White;
                Solids["BrushGreen"] = Color.FromRgb(0x1E, 0x9E, 0x58);
                Solids["BrushGreenLight"] = Color.FromRgb(0xE3, 0xF5, 0xEA);
                Solids["BrushRed"] = Color.FromRgb(0xD8, 0x29, 0x29);
                Solids["BrushRedLight"] = Color.FromRgb(0xFD, 0xE8, 0xE8);
                Solids["BrushOrange"] = Color.FromRgb(0xB8, 0x62, 0x00);
                Solids["BrushOrangeLight"] = Color.FromRgb(0xFD, 0xF1, 0xE0);
                SetLampColors();
                Solids["BrushDialogMask"] = N(0.18, 2, 0x66);

                Gradients["BrushTitleBar"] = new[] { Solids["Brush2"], Solids["Brush3"], Solids["Brush2"] };
                Gradients["BrushBackground"] = new[] { N(0.972, 1.1), N(0.952, 1.7), N(0.928, 2.5) };
                Colors["ColorShadow"] = A(0.32, accent * 0.5);
                Solids["BrushShadowRing"] = A(0.32, accent * 0.5, 0x30);
            }

            private void FillDark(double hue, double accent, double neutral)
            {
                Color A(double l, double c, byte alpha = 255) => new Oklch(l, c, hue).ToColor(alpha);
                Color N(double l, double factor, byte alpha = 255) => new Oklch(l, neutral * factor, hue).ToColor(alpha);

                SetRamp(
                    N(0.93, 1.2),
                    A(0.86, accent * 0.75),
                    A(0.78, accent * 0.85),
                    A(0.70, accent * 0.8),
                    A(0.52, accent * 0.55),
                    A(0.37, Math.Min(0.06, accent * 0.4)),
                    A(0.32, Math.Min(0.05, accent * 0.33)),
                    A(0.285, Math.Min(0.04, accent * 0.27)));
                SetGrays(N(0.88, 0.6), N(0.76, 0.6), N(0.67, 0.6), N(0.55, 0.6), N(0.40, 0.6));

                Solids["BrushOnAccent"] = A(0.22, accent * 0.3);
                Solids["BrushTitleAccent"] = A(0.50, accent);
                Solids["BrushSurface"] = N(0.275, 1.2);
                Solids["BrushCard"] = N(0.255, 1.3, 0xE0);
                Solids["BrushCardHover"] = N(0.28, 1.3, 0xEB);
                Solids["BrushTile"] = N(0.28, 1.2, 0xB3);
                Solids["BrushInput"] = N(0.21, 1.2, 0xE6);
                Solids["BrushInputFocus"] = N(0.19, 1.2);
                Solids["BrushGreen"] = new Oklch(0.76, 0.14, 150).ToColor();
                Solids["BrushGreenLight"] = new Oklch(0.30, 0.045, 150).ToColor();
                Solids["BrushRed"] = new Oklch(0.71, 0.16, 25).ToColor();
                Solids["BrushRedLight"] = new Oklch(0.30, 0.055, 25).ToColor();
                Solids["BrushOrange"] = new Oklch(0.79, 0.14, 70).ToColor();
                Solids["BrushOrangeLight"] = new Oklch(0.31, 0.05, 70).ToColor();
                SetLampColors();
                Solids["BrushDialogMask"] = Color.FromArgb(0x8C, 0x05, 0x05, 0x08);

                Gradients["BrushTitleBar"] = new[] { A(0.43, accent * 0.85), A(0.51, accent * 0.9), A(0.43, accent * 0.85) };
                Gradients["BrushBackground"] = new[] { N(0.205, 1.5), N(0.185, 1.8), N(0.165, 2.0) };
                Colors["ColorShadow"] = Color.FromRgb(0, 0, 0);
                Solids["BrushShadowRing"] = Color.FromArgb(0x90, 0, 0, 0);
            }

            /// <summary>Status lamps on the title bar: bright enough to read on any accent in either theme.</summary>
            private void SetLampColors()
            {
                Solids["BrushLampGreen"] = Color.FromRgb(0x3C, 0xDC, 0x7C);
                Solids["BrushLampYellow"] = Color.FromRgb(0xFF, 0xD4, 0x3B);
                Solids["BrushLampRed"] = Color.FromRgb(0xFF, 0x4D, 0x4F);
            }

            private void SetRamp(params Color[] ramp)
            {
                for (int i = 0; i < ramp.Length; i++)
                {
                    Solids["Brush" + (i + 1)] = ramp[i];
                    Colors["Color" + (i + 1)] = ramp[i];
                }
            }

            private void SetGrays(Color gray1, Color gray3, Color gray4, Color gray5, Color gray7)
            {
                Solids["BrushGray1"] = gray1;
                Solids["BrushGray3"] = gray3;
                Solids["BrushGray4"] = gray4;
                Solids["BrushGray5"] = gray5;
                Solids["BrushGray7"] = gray7;
            }
        }
    }
}
