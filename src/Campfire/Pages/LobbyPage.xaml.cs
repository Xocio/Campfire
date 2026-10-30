using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Campfire.Link;
using Campfire.Link.Lobby;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// The public lobby: rooms other players listed, to join without asking for an invite code.
    /// </summary>
    public partial class LobbyPage : UserControl
    {
        private enum ListState
        {
            None,
            Loading,
            Empty,
            NoMatch,
            Error,
            Closed,
            NoService,
        }

        /// <summary>A reload asked for with the button can be repeated only after this long.</summary>
        private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(10);

        private readonly DispatcherTimer _cooldown;
        private DateTime _cooldownUntil = DateTime.MinValue;
        private readonly DispatcherTimer _typing;
        private LobbyList _list;
        private List<string> _versionChoices = new List<string>();
        private DateTime _loadedAt = DateTime.MinValue;
        private TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
        private ListState _state;
        private bool _loading;
        private bool _filling;

        public LobbyPage()
        {
            InitializeComponent();
            // Counts the seconds left on the button.
            _cooldown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cooldown.Tick += (sender, e) =>
            {
                if (DateTime.UtcNow >= _cooldownUntil)
                    _cooldown.Stop();
                UpdateRefreshButton();
            };
            _typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _typing.Tick += (sender, e) =>
            {
                _typing.Stop();
                RenderRooms();
            };
            FillVersions();
            RenderState();

            // Campfire draws every page once while starting; that alone must not contact the service.
            IsVisibleChanged += (sender, e) =>
            {
                if (IsVisible && IsHitTestVisible)
                    RefreshIfStale();
            };
            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Raised when the user wants to open a room of their own.</summary>
        internal event EventHandler CreateRequested;

        /// <summary>Raised when a room is clicked, to show its details.</summary>
        internal event EventHandler<LobbyRoom> RoomRequested;

        /// <summary>Loads the list again soon, for example after a room turned out to be gone.</summary>
        internal void Invalidate()
        {
            _loadedAt = DateTime.MinValue;
        }

        // ======================== Loading ========================

        private void RefreshIfStale()
        {
            if (DateTime.UtcNow - _loadedAt >= _refreshInterval)
                _ = RefreshAsync();
        }

        private async Task RefreshAsync(bool asked = false)
        {
            if (_loading)
                return;
            if (!LobbyClient.IsAvailable)
            {
                TxtSearch.IsEnabled = CmbVersion.IsEnabled = BtnRefresh.IsEnabled = false;
                SetState(ListState.NoService);
                return;
            }

            _loading = true;
            UpdateRefreshButton();
            if (_list == null || _state != ListState.None)
                SetState(ListState.Loading);
            try
            {
                _list = await LobbyClient.ListAsync();
                _refreshInterval = _list.RefreshInterval;
                _loadedAt = DateTime.UtcNow;
                HintNotice.Visibility = string.IsNullOrWhiteSpace(_list.Notice) ? Visibility.Collapsed : Visibility.Visible;
                LabNotice.Text = _list.Notice;
                FillVersions();
                RenderRooms();
            }
            catch (LobbyException ex)
            {
                SetState(ex.Kind == LobbyError.NoService ? ListState.NoService : ListState.Error);
            }
            finally
            {
                _loading = false;
                // Reloading at once costs the service a request and shows nothing new.
                if (asked)
                {
                    _cooldownUntil = DateTime.UtcNow + RefreshCooldown;
                    _cooldown.Stop();
                    _cooldown.Start();
                }
                UpdateRefreshButton();
            }
        }

        private void UpdateRefreshButton()
        {
            double wait = (_cooldownUntil - DateTime.UtcNow).TotalSeconds;
            BtnRefresh.IsEnabled = LobbyClient.IsAvailable && !_loading && wait <= 0;
            BtnRefresh.Content = Text.Get("Lobby.Refresh");
            if (wait > 0)
                BtnRefresh.Content = Text.Format("Lobby.RefreshWait", (int)Math.Ceiling(wait));
        }

        // ======================== Rendering ========================

        private void RenderRooms()
        {
            ListRooms.Children.Clear();
            if (_list == null)
                return;
            if (!_list.Enabled)
            {
                SetState(ListState.Closed);
                return;
            }

            string version = CmbVersion.SelectedIndex > 0 ? _versionChoices[CmbVersion.SelectedIndex - 1] : null;
            string query = TxtSearch.Text.Trim();
            var shown = _list.Rooms
                .Where(r => version == null || string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase))
                .Where(r => query.Length == 0 || Contains(r.Title, query) || Contains(r.Host, query) || Contains(r.Map, query))
                .ToList();
            foreach (var room in shown)
                ListRooms.Children.Add(RoomRow(room));
            SetState(_list.Rooms.Count == 0 ? ListState.Empty : shown.Count == 0 ? ListState.NoMatch : ListState.None);
        }

        private static bool Contains(string text, string query)
        {
            return text != null && CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth) >= 0;
        }

        /// <summary>
        /// One room: its picture, then the title, host and map, and the version and tags as pills; the player count
        /// and how long it has been open sit on the right.
        /// </summary>
        private Button RoomRow(LobbyRoom room)
        {
            var title = new DockPanel();
            if (room.Locked)
                title.Children.Add(LobbyText.Lock(14));
            title.Children.Add(new TextBlock { FontSize = 14, Text = room.Title, TextTrimming = TextTrimming.CharacterEllipsis });

            var people = new StackPanel { Margin = new Thickness(0, 3, 0, 0), Orientation = Orientation.Horizontal };
            people.Children.Add(LobbyText.Field("Lobby.HostLabel", LobbyText.HostName(room)));
            if (room.Map.Length > 0)
            {
                var map = LobbyText.Field("Lobby.MapLabel", room.Map);
                map.Margin = new Thickness(16, 0, 0, 0);
                people.Children.Add(map);
            }

            var pills = LobbyText.Pills(room);
            pills.Margin = new Thickness(0, 5, 0, 0);

            var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(people);
            text.Children.Add(pills);

            var count = new TextBlock { Text = LobbyText.Players(room), FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            count.SetResourceReference(TextBlock.ForegroundProperty, "Brush3");
            var countLabel = new TextBlock { Text = Text.Get("Lobby.PlayersLabel"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            countLabel.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray5");
            var age = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Text = LobbyText.Ago(room.CreatedAt), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            age.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray5");
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            side.Children.Add(count);
            side.Children.Add(countLabel);
            side.Children.Add(age);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(LobbyText.Picture(room, 52));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(side, 2);
            grid.Children.Add(side);

            var row = new Button { Style = (Style)FindResource("PclEntry"), Content = grid, Tag = room, ToolTip = room.Title };
            row.Click += (sender, e) => RoomRequested?.Invoke(this, room);
            return row;
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
                    key = "Lobby.Loading";
                    break;
                case ListState.Empty:
                    key = "Lobby.Empty";
                    break;
                case ListState.NoMatch:
                    key = "Lobby.NoMatch";
                    break;
                case ListState.Error:
                    key = "Lobby.Error";
                    break;
                case ListState.Closed:
                    key = "Lobby.Closed";
                    break;
                case ListState.NoService:
                    key = "Lobby.NoService";
                    break;
            }
            PanState.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
            LabState.Text = key == null ? "" : Text.Get(key);
            SpinState.Visibility = _state == ListState.Loading ? Visibility.Visible : Visibility.Collapsed;
            BtnRetry.Visibility = _state == ListState.Error ? Visibility.Visible : Visibility.Collapsed;
            PanState.Margin = new Thickness(0, ListRooms.Children.Count > 0 ? 10 : 4, 0, 2);
            int total = _list != null && _list.Enabled ? _list.Rooms.Count : 0;
            CardRooms.Header = total > 0 ? Text.Format("Lobby.HeaderCount", total) : Text.Get("Lobby.Header");
        }

        /// <summary>Offers the versions found in the list, keeping what is chosen.</summary>
        private void FillVersions()
        {
            _filling = true;
            try
            {
                string version = CmbVersion.SelectedIndex > 0 ? _versionChoices[CmbVersion.SelectedIndex - 1] : null;
                _versionChoices = (_list?.Rooms ?? new LobbyRoom[0])
                    .Select(r => r.Version)
                    .Where(v => v.Length > 0)
                    .Concat(version == null ? Enumerable.Empty<string>() : new[] { version })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(v => Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0))
                    .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                CmbVersion.ItemsSource = new[] { Text.Get("Lobby.AllVersions") }.Concat(_versionChoices).ToList();
                CmbVersion.SelectedIndex = version == null ? 0 : Math.Max(0, _versionChoices.IndexOf(version) + 1);
            }
            finally
            {
                _filling = false;
            }
        }

        // ======================== Events ========================

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            CreateRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (DateTime.UtcNow < _cooldownUntil)
                return;
            _ = RefreshAsync(true);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _typing.Stop();
            _typing.Start();
        }

        private void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_filling)
                RenderRooms();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            FillVersions();
            RenderRooms();
            RenderState();
        }
    }
}
