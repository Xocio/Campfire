using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Campfire.Link;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// Rooms used before: favorites and the latest few. A joined room opens again while its host keeps it open; a room
    /// opened here comes back under the same invite code. Clicking a room enters it.
    /// </summary>
    public partial class RecentPage : UserControl
    {
        public RecentPage()
        {
            InitializeComponent();
            RoomHistory.Changed += (sender, e) => Dispatcher.BeginInvoke(new Action(Render));
            Text.LanguageChanged += (sender, e) => Render();
            IsVisibleChanged += (sender, e) =>
            {
                if (IsVisible)
                    Render();
            };
            Render();
        }

        /// <summary>Whether a room is open or being set up.</summary>
        internal Func<bool> IsRoomActive { get; set; }

        /// <summary>Raised when a room joined before should be joined again.</summary>
        internal event EventHandler<RoomCode> JoinRequested;

        /// <summary>Raised when a room opened here before should be opened again with its code.</summary>
        internal event EventHandler<RoomCode> HostRequested;

        private void Render()
        {
            var all = RoomHistory.GetAll();
            var favorites = all.Where(r => r.IsFavorite).ToList();
            var recent = all.Where(r => !r.IsFavorite).ToList();

            Fill(PanFavorites, favorites, "Recent.NoFavorites");
            Fill(PanRecent, recent, "Recent.NoRecent");
            LabFavoriteCount.Text = favorites.Count > 0 ? favorites.Count.ToString(CultureInfo.InvariantCulture) : "";
            LabRecentCount.Text = recent.Count > 0 ? recent.Count.ToString(CultureInfo.InvariantCulture) : "";
            BtnClear.Visibility = all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Fill(Panel panel, IReadOnlyList<RoomRecord> records, string emptyKey)
        {
            panel.Children.Clear();
            foreach (var record in records)
                panel.Children.Add(CreateRow(record));
            if (records.Count == 0)
                panel.Children.Add(CreateEmpty(Text.Get(emptyKey)));
        }

        private UIElement CreateEmpty(string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray4");
            var box = new Border { Padding = new Thickness(16, 18, 16, 18), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = label };
            box.SetResourceReference(Border.BorderBrushProperty, "Brush6");
            return box;
        }

        private UIElement CreateRow(RoomRecord record)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string name = record.Title.Length > 0 ? record.Title : Text.Get("Recent.Unnamed");
            grid.Children.Add(CreateAvatar(record.Title.Length > 0 ? record.Title : "?"));

            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.Bold, MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis });
            heading.Children.Add(CreateTag(Text.Get(record.IsHost ? "Recent.Hosted" : "Recent.Joined")));
            var meta = new TextBlock
            {
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = Ago(record.LastUsed) + " · " + LinkSession.Mask(RoomCode.Find(record.Code)),
            };
            meta.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray4");
            var middle = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            middle.Children.Add(heading);
            middle.Children.Add(meta);
            Grid.SetColumn(middle, 1);
            grid.Children.Add(middle);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(CreateIconButton(StarPath(record.IsFavorite), record.IsFavorite,
                Text.Get(record.IsFavorite ? "Recent.Unfavorite" : "Recent.Favorite"), () => RoomHistory.SetFavorite(record, !record.IsFavorite)));
            actions.Children.Add(CreateIconButton("M4.5,4.5 L11.5,11.5 M11.5,4.5 L4.5,11.5", false, Text.Get("Recent.Remove"), () => RoomHistory.Remove(record)));
            actions.Children.Add(CreateAction(Text.Get(record.IsHost ? "Recent.OpenAgain" : "Recent.JoinAgain")));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            var tile = new Button
            {
                Style = (Style)FindResource("PclTile"),
                Padding = new Thickness(12, 9, 10, 9),
                Margin = new Thickness(0, 0, 0, 8),
                Content = grid,
            };
            tile.Click += (sender, e) => Open(record);
            return tile;
        }

        /// <summary>A round badge with the first letter of the room's name.</summary>
        private UIElement CreateAvatar(string title)
        {
            var initial = new TextBlock
            {
                Text = char.ToUpperInvariant(title[0]).ToString(),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            initial.SetResourceReference(TextBlock.ForegroundProperty, "Brush3");
            var badge = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), VerticalAlignment = VerticalAlignment.Center, Child = initial };
            badge.SetResourceReference(Border.BackgroundProperty, "Brush7");
            return badge;
        }

        private Border CreateTag(string text)
        {
            var label = new TextBlock { FontSize = 11, Text = text };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush3");
            var tag = new Border
            {
                Margin = new Thickness(8, 1, 0, 0),
                Padding = new Thickness(5, 0, 5, 0),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label,
            };
            tag.SetResourceReference(Border.BackgroundProperty, "Brush7");
            return tag;
        }

        /// <summary>What a click on the room does, drawn as a filled label; the whole row is the button.</summary>
        private UIElement CreateAction(string text)
        {
            var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrushOnAccent");
            var pill = new Border
            {
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(14),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label,
            };
            pill.SetResourceReference(Border.BackgroundProperty, "Brush3");
            return pill;
        }

        private static string StarPath(bool filled)
        {
            return "M8,1.8 L9.9,5.9 L14.3,6.4 L11,9.4 L11.9,13.8 L8,11.6 L4.1,13.8 L5,9.4 L1.7,6.4 L6.1,5.9 Z";
        }

        private Button CreateIconButton(string data, bool filled, string tooltip, Action action)
        {
            var glyph = new Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.None,
                Width = 16,
                Height = 16,
            };
            glyph.SetResourceReference(Shape.StrokeProperty, filled ? "Brush3" : "BrushGray4");
            if (filled)
                glyph.SetResourceReference(Shape.FillProperty, "Brush3");

            var button = new Button { Style = (Style)FindResource("RoomIconButton"), Content = glyph, ToolTip = tooltip };
            button.Click += (sender, e) =>
            {
                // The click does not reach the row.
                e.Handled = true;
                action();
            };
            return button;
        }

        private static string Ago(DateTime utc)
        {
            var age = DateTime.UtcNow - utc;
            if (age.TotalDays >= 2)
                return Text.Format("Recent.DaysAgo", (int)age.TotalDays);
            return LobbyText.Ago(utc);
        }

        private async void Open(RoomRecord record)
        {
            var dialogs = Dialogs.Of(this);
            if (IsRoomActive?.Invoke() == true)
            {
                if (dialogs != null)
                    await dialogs.AlertAsync(Text.Get("Lobby.InRoomTitle"), Text.Get("Lobby.InRoom"));
                return;
            }
            var code = RoomCode.Find(record.Code);
            if (code == null)
                return;
            (record.IsHost ? HostRequested : JoinRequested)?.Invoke(this, code);
        }

        private async void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            if (dialogs == null || await dialogs.ConfirmAsync(Text.Get("Recent.ClearTitle"), Text.Get("Recent.ClearMessage"), Text.Get("Recent.Clear")))
                RoomHistory.Clear();
        }
    }
}
