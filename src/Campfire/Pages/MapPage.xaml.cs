using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Campfire.Localization;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// A map from CurseForge: its files, pictures and description. The chosen file is downloaded and handed to
    /// the import page; files the author keeps to the website are opened there instead.
    /// </summary>
    public partial class MapPage : UserControl
    {
        private MapSummary _summary;
        private MapDetail _detail;
        private MapFile _selected;
        private IReadOnlyCollection<string> _installed = new string[0];
        private int _generation;
        private bool _loading;
        private bool _failed;

        private CancellationTokenSource _download;

        public MapPage()
        {
            InitializeComponent();
            Render();
            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Raised with the downloaded file, for the import page to take over.</summary>
        internal event EventHandler<string> ImportRequested;

        public bool IsBusy => _download != null;

        /// <summary>Shows <paramref name="map"/> at once from the list and loads the rest.</summary>
        internal async Task ShowAsync(MapSummary map, IReadOnlyCollection<string> installed)
        {
            if (_summary?.Id == map.Id && _detail != null)
                return;
            CancelDownload();
            _generation++;
            int generation = _generation;
            _summary = map;
            _installed = installed ?? new string[0];
            _detail = null;
            _selected = null;
            _failed = false;
            _loading = true;
            PanLogo.SetResourceReference(Border.BackgroundProperty, "Brush7");
            LoadHeaderLogo(map.Logo, generation);
            ScrollDetail.ScrollToTop();
            Render();

            try
            {
                var detail = await MapCatalog.GetAsync(map.Id);
                if (generation != _generation)
                    return;
                _detail = detail;
                _selected = DefaultFile(detail.Files);
            }
            catch (MapCatalogException)
            {
                if (generation != _generation)
                    return;
                _failed = true;
            }
            finally
            {
                if (generation == _generation)
                    _loading = false;
            }
            Render();
        }

        private async void LoadHeaderLogo(string url, int generation)
        {
            var image = await MapImages.LoadAsync(url, 112);
            // A slow logo must not land on the next map.
            if (image != null && generation == _generation)
                PanLogo.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }

        /// <summary>The newest file for a version installed here, else the newest release, else the newest file.</summary>
        private MapFile DefaultFile(IReadOnlyList<MapFile> files)
        {
            return files.FirstOrDefault(f => f.Versions.Any(_installed.Contains) && f.Type == "release")
                   ?? files.FirstOrDefault(f => f.Versions.Any(_installed.Contains))
                   ?? files.FirstOrDefault(f => f.Type == "release")
                   ?? files.FirstOrDefault();
        }

        // ======================== Rendering ========================

        private void Render()
        {
            MapSummary map = (MapSummary)_detail ?? _summary;
            LabName.Text = map?.Name ?? "";
            LabName.ToolTip = map?.Name;
            LabSummary.Text = map?.Summary ?? "";
            LabSummary.ToolTip = string.IsNullOrEmpty(map?.Summary) ? null : map.Summary;
            LabMeta.Text = map == null ? "" : MapText.Meta(map);
            LabMeta.ToolTip = map == null ? null : LabMeta.Text;
            BtnWebsite.Visibility = string.IsNullOrEmpty(map?.Website) ? Visibility.Collapsed : Visibility.Visible;

            RenderFiles();
            RenderAbout();
            UpdateButtons();
        }

        private void RenderFiles()
        {
            ListFiles.Children.Clear();
            var files = _detail?.Files ?? new MapFile[0];
            foreach (var file in files)
            {
                var option = new RadioButton
                {
                    GroupName = "MapFile",
                    Tag = file,
                    Margin = new Thickness(0, ListFiles.Children.Count == 0 ? 0 : 10, 0, 0),
                    Style = (Style)FindResource("PclRadioButton"),
                    IsChecked = file == _selected,
                    IsEnabled = _download == null,
                    Content = FileRow(file),
                };
                option.Checked += File_Checked;
                ListFiles.Children.Add(option);
            }

            string key = _loading ? "Map.Loading" : _failed ? "Map.Error" : _detail != null && files.Count == 0 ? "Map.NoFiles" : null;
            PanState.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
            LabState.Text = key == null ? "" : Text.Get(key);
            SpinState.Visibility = _loading ? Visibility.Visible : Visibility.Collapsed;
            BtnRetry.Visibility = _failed ? Visibility.Visible : Visibility.Collapsed;
            HintWebsite.Visibility = _selected != null && _selected.Url == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement FileRow(MapFile file)
        {
            var parts = new List<string> { MapText.Versions(file.Versions, 3) ?? Text.Get("Map.NoVersion") };
            if (file.Loaders.Count > 0)
                parts.Add(Text.Format("Map.NeedsLoader", string.Join(", ", file.Loaders)));
            if (file.Size > 0)
                parts.Add(MapText.Size(file.Size));
            if (MapText.Date(file.Date) is string date)
                parts.Add(date);
            if (file.Type == "beta" || file.Type == "alpha")
                parts.Add(Text.Get("Map.Type." + file.Type));

            var panel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            panel.Children.Add(new TextBlock { Text = file.Name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.FileName });
            var detail = new TextBlock
            {
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 12,
                Text = string.Join("  ·  ", parts),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray4");
            panel.Children.Add(detail);

            if (file.Versions.FirstOrDefault(_installed.Contains) is string installed)
            {
                var note = new TextBlock { Margin = new Thickness(0, 2, 0, 0), FontSize = 12, Text = Text.Format("Map.Installed", installed) };
                note.SetResourceReference(TextBlock.ForegroundProperty, "BrushGreen");
                panel.Children.Add(note);
            }
            return panel;
        }

        private void RenderAbout()
        {
            PanScreenshots.Children.Clear();
            var screenshots = _detail?.Screenshots ?? new MapScreenshot[0];
            foreach (var screenshot in screenshots)
                PanScreenshots.Children.Add(ScreenshotTile(screenshot));
            PanScreenshots.Visibility = screenshots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            string description = _detail?.Description;
            LabDescription.Text = string.IsNullOrWhiteSpace(description) ? Text.Get("Map.NoDescription") : description;
            LabDescription.Margin = new Thickness(0, screenshots.Count > 0 ? 8 : 0, 0, 0);
            CardAbout.Visibility = _detail != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement ScreenshotTile(MapScreenshot screenshot)
        {
            var picture = new Border { Width = 130, Height = 73, CornerRadius = new CornerRadius(4) };
            picture.SetResourceReference(Border.BackgroundProperty, "Brush7");
            LoadPicture(picture, screenshot.Thumbnail);

            var tile = new Button
            {
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                ToolTip = string.IsNullOrEmpty(screenshot.Title) ? null : screenshot.Title,
                Template = TileTemplate,
                Content = picture,
            };
            tile.Click += (sender, e) => OpenPage(screenshot.Url);
            return tile;
        }

        /// <summary>A button that is only its content.</summary>
        private static readonly ControlTemplate TileTemplate = CreateTileTemplate();

        private static ControlTemplate CreateTileTemplate()
        {
            var template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            template.Seal();
            return template;
        }

        private static async void LoadPicture(Border target, string url)
        {
            var image = await MapImages.LoadAsync(url, 260);
            if (image != null)
                target.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }

        private void UpdateButtons()
        {
            Cursor = _loading ? Cursors.AppStarting : null;
            BtnCancel.Visibility = _download != null ? Visibility.Visible : Visibility.Collapsed;
            if (_download != null)
                return;
            BarDownload.Visibility = Visibility.Hidden;
            BtnDownload.IsEnabled = _selected != null;
            BtnDownload.Content = Text.Get(_selected != null && _selected.Url == null ? "Map.OpenWebsite" : "Map.Download");
        }

        // ======================== Downloading ========================

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            var file = _selected;
            if (file == null || _download != null)
                return;
            if (file.Url == null)
            {
                OpenPage(string.IsNullOrEmpty(file.Page) ? _detail?.Website : file.Page);
                return;
            }

            var cancellation = new CancellationTokenSource();
            _download = cancellation;
            BarDownload.Value = 0;
            BarDownload.Visibility = Visibility.Visible;
            BtnDownload.IsEnabled = false;
            BtnDownload.Content = Text.Format("Map.Downloading", 0);
            RenderFiles();
            UpdateButtons();

            var progress = new Progress<double>(value =>
            {
                if (_download != cancellation)
                    return;
                BarDownload.Value = value;
                BtnDownload.Content = Text.Format("Map.Downloading", (int)(value * 100));
            });
            string path = null;
            try
            {
                path = await MapDownloader.DownloadAsync(file, progress, cancellation.Token);
                Online.Telemetry.Count("maps.download");
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the user, or another map was opened.
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                if (_download == cancellation)
                    _ = Dialogs.Of(this)?.ErrorAsync(Text.Get("Map.DownloadFailedTitle"), Text.Get("Map.DownloadFailed"), ex.Message);
            }
            finally
            {
                if (_download == cancellation)
                    _download = null;
                cancellation.Dispose();
            }

            if (_download == null)
            {
                RenderFiles();
                UpdateButtons();
            }
            if (path != null && file == _selected && !cancellation.IsCancellationRequested)
                ImportRequested?.Invoke(this, path);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CancelDownload();
            RenderFiles();
            UpdateButtons();
        }

        private void CancelDownload()
        {
            var download = _download;
            _download = null;
            download?.Cancel();
        }

        // ======================== Other actions ========================

        private void File_Checked(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton option) || !(option.Tag is MapFile file) || file == _selected)
                return;
            _selected = file;
            HintWebsite.Visibility = file.Url == null ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtons();
        }

        private async void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            var map = _summary;
            if (map == null)
                return;
            _detail = null;
            await ShowAsync(map, _installed);
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            OpenPage(_detail?.Website ?? _summary?.Website);
        }

        private void OpenPage(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                Browser.Open(url);
            }
            catch (Exception ex)
            {
                _ = Dialogs.Of(this)?.ErrorAsync(Text.Get("Dialog.BrowserFailedTitle"), Text.Format("Dialog.BrowserFailed", url, ex.Message));
            }
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Render();
            if (_download != null)
                BtnDownload.Content = Text.Format("Map.Downloading", (int)(BarDownload.Value * 100));
        }
    }
}
