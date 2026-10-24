using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Campfire.Controls;
using Campfire.Link.Runtime;
using Campfire.Localization;
using Campfire.Online;
using Campfire.Themes;

namespace Campfire.Pages
{
    /// <summary>
    /// Settings of Campfire itself, with a side navigation: personalization, general options, about and the
    /// open source licenses. Every change applies at once.
    /// </summary>
    public partial class PreferencesPage : UserControl
    {
        private static readonly (PaletteStyle Style, string Key)[] Styles =
        {
            (PaletteStyle.TonalSpot, "Prefs.Style.TonalSpot"),
            (PaletteStyle.Vibrant, "Prefs.Style.Vibrant"),
            (PaletteStyle.Expressive, "Prefs.Style.Expressive"),
            (PaletteStyle.Neutral, "Prefs.Style.Neutral"),
            (PaletteStyle.Monochrome, "Prefs.Style.Monochrome"),
            (PaletteStyle.Fidelity, "Prefs.Style.Fidelity"),
        };

        private const string SystemColor = "system";

        /// <summary>Where the key color comes from, in the order of the drop-down.</summary>
        private enum ColorSource
        {
            Chosen,
            Wallpaper,
            Windows,
        }

        private FrameworkElement _shownSection;
        private bool _loading;
        private AppLanguage? _licensesLanguage;

        public PreferencesPage()
        {
            InitializeComponent();
            _shownSection = PanPersonalize;

            foreach (var color in ThemeManager.Presets)
                PanSwatches.Children.Add(CreateSwatch("Color", ColorMath.ToHex(color), color));

            Text.LanguageChanged += (sender, e) => Load();
            ThemeManager.Changed += (sender, e) =>
            {
                if (!_loading)
                    LoadColors();
            };
            IsVisibleChanged += (sender, e) =>
            {
                if (!IsVisible)
                    return;
                Load();
                Motion.EnterNavigation(PanNav);
            };
            Load();
        }

        /// <summary>Raised when the notification area settings changed.</summary>
        public event EventHandler TraySettingsChanged;

        // ======================== Navigation ========================

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PanPersonalize == null)
                return;
            var target = sender == NavGeneral ? PanGeneral
                : sender == NavAbout ? PanAbout
                : sender == NavLicenses ? PanLicenses
                : (FrameworkElement)PanPersonalize;
            if (target == _shownSection)
                return;
            int direction = Math.Sign(SectionOrder(target) - SectionOrder(_shownSection));
            Motion.Swap(_shownSection, target, direction);
            _shownSection = target;
        }

        private int SectionOrder(FrameworkElement section)
        {
            return section == PanGeneral ? 1 : section == PanAbout ? 2 : section == PanLicenses ? 3 : 0;
        }

        // ======================== Loading ========================

        private void Load()
        {
            _loading = true;
            try
            {
                Fill(CmbMode, new[] { "Prefs.ModeSystem", "Prefs.ModeLight", "Prefs.ModeDark" }, (int)ThemeManager.Mode);
                Fill(CmbStyle, Styles.Select(s => s.Key).ToArray(), Array.FindIndex(Styles, s => s.Style == ThemeManager.Style));
                Fill(CmbSource, new[] { "Prefs.Source.Chosen", "Prefs.Source.Wallpaper", "Prefs.Source.Windows" }, (int)CurrentSource());
                CmbLanguage.SelectedIndex = Text.Language == AppLanguage.English ? 1 : 0;

                ChkMotion.IsChecked = Motion.Enabled;
                ChkTray.IsChecked = TrayIcon.ShowIcon;
                ChkCloseToTray.IsChecked = TrayIcon.ShowIcon && TrayIcon.CloseToTray;
                ChkCloseToTray.IsEnabled = TrayIcon.ShowIcon;
                CardUpdate.Visibility = CardPrivacy.Visibility = OnlineService.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
                ChkAutoCheck.IsChecked = OnlineService.CheckEnabled;
                ChkStats.IsChecked = Telemetry.Consent == true;
                if (_licensesLanguage != Text.Language)
                {
                    _licensesLanguage = Text.Language;
                    ListLicenses.ItemsSource = LicenseRow.All();
                }
                LoadColors();
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>Fills a drop-down with translated choices, keeping the selection.</summary>
        private static void Fill(ComboBox box, string[] keys, int selected)
        {
            if (box.Items.Count != keys.Length)
            {
                box.Items.Clear();
                foreach (string key in keys)
                    box.Items.Add(new ComboBoxItem());
            }
            for (int i = 0; i < keys.Length; i++)
                ((ComboBoxItem)box.Items[i]).Content = Text.Get(keys[i]);
            box.SelectedIndex = Math.Max(0, selected);
        }

        private static ColorSource CurrentSource()
        {
            if (ThemeManager.UseWallpaper)
                return ColorSource.Wallpaper;
            return ThemeManager.ChosenColor == SystemColor ? ColorSource.Windows : ColorSource.Chosen;
        }

        /// <summary>Shows the color choices that belong to the current source.</summary>
        private void LoadColors()
        {
            bool loading = _loading;
            _loading = true;
            try
            {
                var source = CurrentSource();
                CmbSource.SelectedIndex = (int)source;
                LabSourceNote.Text = Text.Get("Prefs.Source." + source + "Note");
                LabStyleNote.Text = Text.Get(Styles.First(s => s.Style == ThemeManager.Style).Key + "Note");

                // Fixed colors.
                bool chosen = source == ColorSource.Chosen;
                PanSwatches.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
                PanCustomColor.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
                string color = ThemeManager.ChosenColor;
                bool preset = false;
                foreach (RadioButton swatch in PanSwatches.Children)
                {
                    bool match = string.Equals((string)swatch.Tag, color, StringComparison.OrdinalIgnoreCase);
                    swatch.IsChecked = match;
                    preset |= match;
                }
                if (!TxtCustomColor.IsKeyboardFocused)
                    TxtCustomColor.Text = chosen && !preset ? color : "";

                // Colors of the wallpaper, or the single Windows accent color.
                PanWallpaperSwatches.Children.Clear();
                if (source == ColorSource.Wallpaper)
                {
                    var candidates = ThemeManager.WallpaperCandidates;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var swatch = CreateSwatch("Wallpaper", i, candidates[i]);
                        swatch.IsChecked = i == Math.Min(ThemeManager.WallpaperChoice, candidates.Count - 1);
                        PanWallpaperSwatches.Children.Add(swatch);
                    }
                    LabNoWallpaperColor.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    LabNoWallpaperColor.Visibility = Visibility.Collapsed;
                }
                if (source == ColorSource.Windows)
                {
                    var swatch = CreateSwatch("Windows", SystemColor, ThemeManager.Seed);
                    swatch.IsChecked = true;
                    PanWallpaperSwatches.Children.Add(swatch);
                }
                PanWallpaperSwatches.Visibility = chosen ? Visibility.Collapsed : Visibility.Visible;
                BtnRefreshWallpaper.Visibility = source == ColorSource.Wallpaper ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _loading = loading;
            }
        }

        private RadioButton CreateSwatch(string group, object tag, Color color)
        {
            var swatch = new RadioButton
            {
                GroupName = group,
                Tag = tag,
                Background = new SolidColorBrush(color),
                ToolTip = ColorMath.ToHex(color),
                Style = (Style)FindResource("PclSwatch"),
            };
            int index = group == "Color" ? PanSwatches.Children.Count : PanWallpaperSwatches.Children.Count;
            System.Windows.Automation.AutomationProperties.SetAutomationId(swatch, group + index);
            swatch.Checked += Swatch_Checked;
            return swatch;
        }

        // ======================== Theme ========================

        private void CmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CmbMode.SelectedIndex < 0)
                return;
            ThemeManager.Mode = (ThemeMode)CmbMode.SelectedIndex;
            ThemeManager.Apply(true);
        }

        private void CmbStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CmbStyle.SelectedIndex < 0)
                return;
            ThemeManager.Style = Styles[CmbStyle.SelectedIndex].Style;
            ThemeManager.Apply(true);
        }

        private async void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CmbSource.SelectedIndex < 0)
                return;
            var source = (ColorSource)CmbSource.SelectedIndex;
            ThemeManager.UseWallpaper = source == ColorSource.Wallpaper;
            if (source == ColorSource.Windows)
                ThemeManager.ChosenColor = SystemColor;
            else if (source == ColorSource.Chosen && ThemeManager.ChosenColor == SystemColor)
                ThemeManager.ChosenColor = null;

            if (source == ColorSource.Wallpaper)
                await ThemeManager.RefreshWallpaperAsync();
            else
                ThemeManager.Apply(true);
        }

        private async void BtnRefreshWallpaper_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshWallpaper.IsEnabled = false;
            try
            {
                await ThemeManager.RefreshWallpaperAsync();
            }
            finally
            {
                BtnRefreshWallpaper.IsEnabled = true;
            }
        }

        private void Swatch_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;
            var swatch = (RadioButton)sender;
            if (swatch.GroupName == "Wallpaper")
                ThemeManager.WallpaperChoice = (int)swatch.Tag;
            else if (swatch.GroupName == "Color")
                ThemeManager.ChosenColor = (string)swatch.Tag;
            else
                return;
            ThemeManager.Apply(true);
        }

        private void TxtCustomColor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                _ = ApplyCustomColorAsync();
        }

        private void TxtCustomColor_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _ = ApplyCustomColorAsync();
        }

        private async System.Threading.Tasks.Task ApplyCustomColorAsync()
        {
            string text = TxtCustomColor.Text.Trim();
            if (text.Length == 0)
                return;
            if (!text.StartsWith("#", StringComparison.Ordinal))
                text = "#" + text;

            var invalid = Color.FromArgb(0, 0, 0, 0);
            var color = ColorMath.Parse(text, invalid);
            if (color == invalid)
            {
                await Dialogs.Of(this).AlertAsync(Text.Get("Prefs.InvalidColorTitle"), Text.Format("Prefs.InvalidColor", text));
                TxtCustomColor.Focus();
                TxtCustomColor.SelectAll();
                return;
            }

            string hex = ColorMath.ToHex(color);
            if (string.Equals(hex, ThemeManager.ChosenColor, StringComparison.OrdinalIgnoreCase))
                return;
            ThemeManager.ChosenColor = hex;
            ThemeManager.Apply(true);
        }

        // ======================== General ========================

        private void ChkMotion_Click(object sender, RoutedEventArgs e)
        {
            Motion.Enabled = ChkMotion.IsChecked == true;
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CmbLanguage.SelectedIndex < 0)
                return;
            var language = CmbLanguage.SelectedIndex == 1 ? AppLanguage.English : AppLanguage.Chinese;
            if (language == Text.Language)
                return;
            Text.Language = language;
            AppSettings.SaveLanguage(language);
        }

        private void ChkTray_Click(object sender, RoutedEventArgs e)
        {
            TrayIcon.ShowIcon = ChkTray.IsChecked == true;
            Load();
            TraySettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ChkCloseToTray_Click(object sender, RoutedEventArgs e)
        {
            TrayIcon.CloseToTray = ChkCloseToTray.IsChecked == true;
            TraySettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ChkAutoCheck_Click(object sender, RoutedEventArgs e)
        {
            OnlineService.CheckEnabled = ChkAutoCheck.IsChecked == true;
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            var flow = OnlineFlow.Current;
            if (flow == null)
                return;
            BtnCheckUpdate.IsEnabled = false;
            LabUpdateStatus.Text = Text.Get("Prefs.Checking");
            try
            {
                var result = await flow.CheckAsync(manual: true);
                LabUpdateStatus.Text = Text.Get(result == CheckResult.UpToDate ? "Prefs.UpToDate"
                    : result == CheckResult.Failed ? "Prefs.CheckFailed" : "Prefs.UpdateFound");
            }
            finally
            {
                BtnCheckUpdate.IsEnabled = true;
            }
        }

        private void ChkStats_Click(object sender, RoutedEventArgs e)
        {
            Telemetry.Consent = ChkStats.IsChecked == true;
        }

        private void BtnPrivacy_Click(object sender, RoutedEventArgs e)
        {
            var host = Dialogs.Of(this);
            if (host != null)
                _ = OnlineFlow.ShowPrivacyAsync(host);
        }

        private void BtnStatsPreview_Click(object sender, RoutedEventArgs e)
        {
            _ = Dialogs.Of(this)?.AlertAsync(Text.Get("Prefs.StatsPreviewTitle"), Telemetry.Preview(), Text.Get("Prefs.StatsPreviewNote"));
        }

        private async void BtnStatsReset_Click(object sender, RoutedEventArgs e)
        {
            var host = Dialogs.Of(this);
            if (host == null || !await host.ConfirmAsync(Text.Get("Prefs.StatsResetTitle"), Text.Get("Prefs.StatsResetMessage"),
                    Text.Get("Prefs.StatsResetConfirm")))
                return;
            Telemetry.ResetId();
            await host.AlertAsync(Text.Get("Prefs.StatsResetTitle"), Text.Get("Prefs.StatsResetDone"));
        }

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.Tag is string url))
                return;
            try
            {
                Browser.Open(url);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                _ = Dialogs.Of(this)?.ErrorAsync(Text.Get("Dialog.BrowserFailedTitle"), Text.Format("Dialog.BrowserFailed", url, ex.Message));
            }
        }

        /// <summary>A third-party component Campfire uses, with its license.</summary>
        private sealed class LicenseRow
        {
            public string Name { get; private set; }

            public string License { get; private set; }

            public string Description { get; private set; }

            public string Url { get; private set; }

            public string LinkText => Text.Get("Licenses.View");

            public static List<LicenseRow> All()
            {
                return new List<LicenseRow>
                {
                    new LicenseRow
                    {
                        Name = "EasyTier " + EasyTierPackage.Version,
                        License = "LGPL-3.0",
                        Description = Text.Get("Licenses.EasyTier"),
                        Url = "https://github.com/EasyTier/EasyTier/blob/main/LICENSE",
                    },
                    new LicenseRow
                    {
                        Name = "dnlib 4.4.0",
                        License = "MIT",
                        Description = Text.Get("Licenses.Dnlib"),
                        Url = "https://github.com/0xd4d/dnlib/blob/master/LICENSE.txt",
                    },
                    new LicenseRow
                    {
                        Name = "ASM 9.11.1",
                        License = "BSD-3-Clause",
                        Description = Text.Get("Licenses.Asm"),
                        Url = "https://gitlab.ow2.org/asm/asm/-/blob/master/LICENSE.txt",
                    },
                };
            }
        }
    }
}
