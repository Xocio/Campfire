using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Campfire.Localization;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// "Maps" tab: the entry to importing a downloaded map, and maps from CurseForge to search and browse.
    /// </summary>
    public partial class MapsPage : UserControl
    {
        private enum ListState
        {
            None,
            Loading,
            LoadingMore,
            Empty,
            Error,
            Unavailable,
            NoService,
        }

        private static readonly MapSort[] Sorts = { MapSort.Popular, MapSort.Downloads, MapSort.Updated, MapSort.Released, MapSort.Name };

        /// <summary>Releases offered in the version filter besides the installed ones: those most maps are made for.</summary>
        private static readonly string[] CommonVersions =
        {
            "1.21.1", "1.20.1", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.15.2", "1.14.4", "1.13.2", "1.12.2", "1.11.2",
            "1.10.2", "1.9.4", "1.8.9", "1.7.10",
        };

        /// <summary>Starts loading the next page when the list is scrolled this close to its end.</summary>
        private const double LoadAhead = 240;

        private readonly DispatcherTimer _typing;
        private readonly List<MapSummary> _maps = new List<MapSummary>();
        private IReadOnlyList<MapCategory> _categories = new MapCategory[0];
        /// <summary>The category ids and versions behind the filter choices, after the first "all" choice.</summary>
        private List<int> _categoriesShown = new List<int>();
        private List<string> _versionChoices = new List<string>();
        private HashSet<string> _installed = new HashSet<string>();

        private bool _started;
        private bool _loadingCategories;
        private bool _filling;
        private int _generation;
        private bool _loading;
        private bool _hasMore;
        private int _page;
        private int _total;
        private ListState _state;
        private string _query = "";

        public MapsPage()
        {
            InitializeComponent();
            _typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _typing.Tick += (sender, e) =>
            {
                _typing.Stop();
                SearchIfChanged();
            };
            FillFilters();
            RenderState();

            // Campfire draws every page once while starting; that alone must not contact the service.
            IsVisibleChanged += (sender, e) =>
            {
                if (IsVisible && IsHitTestVisible)
                    Start();
            };
            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Raised when the user wants to import a map they already have.</summary>
        public event EventHandler ImportRequested;

        /// <summary>Raised when a map is clicked.</summary>
        internal event EventHandler<MapSummary> MapRequested;

        /// <summary>Game versions installed on this computer, as far as found.</summary>
        internal IReadOnlyCollection<string> InstalledVersions => _installed;

        // ======================== Loading ========================

        /// <summary>Loads the list the first time the tab is shown.</summary>
        private void Start()
        {
            if (_started)
                return;
            _started = true;
            _ = ScanVersionsAsync();
            if (!MapCatalog.IsAvailable)
            {
                TxtSearch.IsEnabled = CmbCategory.IsEnabled = CmbVersion.IsEnabled = CmbSort.IsEnabled = false;
                SetState(ListState.NoService);
                return;
            }
            _ = LoadCategoriesAsync();
            Search();
        }

        private async Task LoadCategoriesAsync()
        {
            if (_loadingCategories)
                return;
            _loadingCategories = true;
            try
            {
                _categories = await MapCatalog.CategoriesAsync();
                FillFilters();
            }
            catch (MapCatalogException)
            {
                // The list still works without the category filter; it is tried again with the next search.
            }
            finally
            {
                _loadingCategories = false;
            }
        }

        private async Task ScanVersionsAsync()
        {
            var directories = new[] { AppDomain.CurrentDomain.BaseDirectory };
            var versions = await Task.Run(() => GameFolders.Find(directories).SelectMany(InstalledVersion.Scan)
                .Where(v => v.IsRelease && !string.IsNullOrEmpty(v.GameVersion))
                .Select(v => v.GameVersion)
                .ToList());
            _installed = new HashSet<string>(versions, StringComparer.OrdinalIgnoreCase);
            FillFilters();
        }

        /// <summary>Starts the list over with the current search and filters.</summary>
        private void Search()
        {
            if (!MapCatalog.IsAvailable)
                return;
            _typing.Stop();
            _generation++;
            _query = TxtSearch.Text.Trim();
            _maps.Clear();
            ListMaps.Children.Clear();
            _page = 0;
            _total = 0;
            _hasMore = true;
            _loading = false;
            ScrollMaps.ScrollToTop();
            if (_categories.Count == 0)
                _ = LoadCategoriesAsync();
            _ = LoadPageAsync();
        }

        private void SearchIfChanged()
        {
            if (TxtSearch.Text.Trim() != _query)
                Search();
        }

        private async Task LoadPageAsync()
        {
            if (_loading || !_hasMore)
                return;
            int generation = _generation;
            _loading = true;
            SetState(_page == 0 ? ListState.Loading : ListState.LoadingMore);

            int category = CmbCategory.SelectedIndex > 0 ? _categories[CmbCategory.SelectedIndex - 1].Id : 0;
            string version = CmbVersion.SelectedIndex > 0 ? _versionChoices[CmbVersion.SelectedIndex - 1] : null;
            var sort = Sorts[Math.Max(0, CmbSort.SelectedIndex)];
            try
            {
                var result = await MapCatalog.SearchAsync(_query, category, version, sort, _page);
                if (generation != _generation)
                    return;

                // Popularity shifts while the list is read; a map may come round again on the next page.
                var known = new HashSet<int>(_maps.Select(m => m.Id));
                foreach (var map in result.Maps.Where(m => known.Add(m.Id)))
                {
                    _maps.Add(map);
                    ListMaps.Children.Add(MapRow(map));
                }
                _page++;
                _total = result.Total;
                _hasMore = result.Maps.Count > 0 && _page * result.PageSize < result.Total;
                SetState(_maps.Count == 0 ? ListState.Empty : ListState.None);
            }
            catch (MapCatalogException ex)
            {
                if (generation == _generation)
                    SetState(ex.Unavailable ? ListState.Unavailable : ListState.Error);
            }
            finally
            {
                if (generation == _generation)
                    _loading = false;
            }
        }

        // ======================== Rendering ========================

        private Button MapRow(MapSummary map)
        {
            var logo = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(4) };
            logo.SetResourceReference(Border.BackgroundProperty, "Brush7");
            LoadLogo(logo, map.Logo);

            var name = new TextBlock { FontSize = 14, Text = map.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            var summary = new TextBlock { Margin = new Thickness(0, 2, 0, 0), FontSize = 12, Text = map.Summary, TextTrimming = TextTrimming.CharacterEllipsis };
            summary.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray3");
            var meta = new TextBlock { Margin = new Thickness(0, 2, 0, 0), FontSize = 12, Text = MapText.Meta(map), TextTrimming = TextTrimming.CharacterEllipsis };
            meta.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray4");

            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(name);
            text.Children.Add(summary);
            text.Children.Add(meta);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(logo);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var row = new Button { Style = (Style)FindResource("PclEntry"), Content = grid, Tag = map, ToolTip = map.Name };
            row.Click += (sender, e) => MapRequested?.Invoke(this, map);
            return row;
        }

        private static async void LoadLogo(Border target, string url)
        {
            var image = await MapImages.LoadAsync(url, 96);
            if (image != null)
                target.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }

        private void SetState(ListState state)
        {
            _state = state;
            RenderState();
        }

        private void RenderState()
        {
            string key = null;
            switch (_state)
            {
                case ListState.Loading:
                    key = "Maps.Loading";
                    break;
                case ListState.LoadingMore:
                    key = "Maps.LoadingMore";
                    break;
                case ListState.Empty:
                    key = "Maps.Empty";
                    break;
                case ListState.Error:
                    key = "Maps.Error";
                    break;
                case ListState.Unavailable:
                    key = "Maps.Unavailable";
                    break;
                case ListState.NoService:
                    key = "Maps.NoService";
                    break;
            }
            PanState.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
            LabState.Text = key == null ? "" : Text.Get(key);
            SpinState.Visibility = _state == ListState.Loading || _state == ListState.LoadingMore ? Visibility.Visible : Visibility.Collapsed;
            BtnRetry.Visibility = _state == ListState.Error || _state == ListState.Unavailable ? Visibility.Visible : Visibility.Collapsed;
            PanState.Margin = new Thickness(0, _maps.Count > 0 ? 10 : 4, 0, 2);
            CardMaps.Header = _total > 0 ? Text.Format("Maps.HeaderCount", _total.ToString("N0", CultureInfo.InvariantCulture)) : Text.Get("Maps.Header");
        }

        /// <summary>Fills the filters, keeping what is chosen; called again when categories or versions arrive.</summary>
        private void FillFilters()
        {
            _filling = true;
            try
            {
                int category = CmbCategory.SelectedIndex > 0 ? _categoriesShown[CmbCategory.SelectedIndex - 1] : 0;
                string version = CmbVersion.SelectedIndex > 0 ? _versionChoices[CmbVersion.SelectedIndex - 1] : null;
                int sort = Math.Max(0, CmbSort.SelectedIndex);

                _categoriesShown = _categories.Select(c => c.Id).ToList();
                CmbCategory.ItemsSource = new[] { Text.Get("Maps.AllCategories") }.Concat(_categories.Select(MapText.Category)).ToList();
                CmbCategory.SelectedIndex = Math.Max(0, _categoriesShown.IndexOf(category) + 1);

                _versionChoices = _installed.Concat(CommonVersions).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(v => Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0)).ToList();
                CmbVersion.ItemsSource = new[] { Text.Get("Maps.AllVersions") }.Concat(_versionChoices).ToList();
                CmbVersion.SelectedIndex = version == null ? 0 : Math.Max(0, _versionChoices.IndexOf(version) + 1);

                CmbSort.ItemsSource = Sorts.Select(s => Text.Get("Maps.Sort." + s)).ToList();
                CmbSort.SelectedIndex = sort;
            }
            finally
            {
                _filling = false;
            }
        }

        // ======================== Events ========================

        private void EntryImport_Click(object sender, RoutedEventArgs e)
        {
            ImportRequested?.Invoke(this, EventArgs.Empty);
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            Search();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_started)
                return;
            _typing.Stop();
            _typing.Start();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_filling && _started)
                Search();
        }

        private void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            if (_maps.Count == 0)
            {
                Search();
                return;
            }
            _ = LoadPageAsync();
        }

        private void ScrollMaps_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_state == ListState.None && _hasMore && !_loading
                && ScrollMaps.ExtentHeight - ScrollMaps.VerticalOffset - ScrollMaps.ViewportHeight < LoadAhead)
                _ = LoadPageAsync();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            FillFilters();
            ListMaps.Children.Clear();
            foreach (var map in _maps)
                ListMaps.Children.Add(MapRow(map));
            RenderState();
        }
    }
}
