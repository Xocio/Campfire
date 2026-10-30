using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Campfire.Controls;
using Campfire.Link;
using Campfire.Localization;
using Campfire.Online;

namespace Campfire.Pages
{
    /// <summary>
    /// "Home" tab, built around multiplayer: start, join or find a room right here, see the room once it is open,
    /// and a short guide for playing together.
    /// </summary>
    public partial class HomePage : UserControl
    {
        private RoomCode _quickJoinCode;
        private Notice _notice;

        public HomePage()
        {
            InitializeComponent();
            Text.LanguageChanged += (sender, e) => RenderQuickJoin();
            // Without the service there is no lobby; the other two tiles share the row.
            if (!Link.Lobby.LobbyClient.IsAvailable)
            {
                TileLobby.Visibility = Visibility.Collapsed;
                PanStart.ColumnDefinitions[3].Width = new GridLength(0);
                PanStart.ColumnDefinitions[4].Width = new GridLength(0);
            }
        }

        /// <summary>Raised when the user wants to browse the public lobby.</summary>
        public event EventHandler LobbyRequested;

        /// <summary>Raised when the multiplayer page should open as it is.</summary>
        public event EventHandler LinkRequested;

        /// <summary>Raised when the user wants to open a room.</summary>
        public event EventHandler CreateRequested;

        /// <summary>Raised when the user wants to enter an invite code.</summary>
        public event EventHandler JoinRequested;

        /// <summary>Raised when the user wants the rooms used before.</summary>
        public event EventHandler RecentRequested;

        /// <summary>Raised when the user wants the multiplayer settings.</summary>
        public event EventHandler SettingsRequested;

        /// <summary>Raised when the user wants to join the code found on the clipboard.</summary>
        internal event EventHandler<RoomCode> QuickJoinRequested;

        /// <summary>
        /// Shows the room in place of the start tiles; a <c>null</c> title means there is no room and the tiles return.
        /// </summary>
        internal void SetRoom(string title, string detail, string brushKey)
        {
            bool active = title != null;
            if (active)
            {
                LabRoomTitle.Text = title;
                LabRoomDetail.Text = detail ?? "";
                LabRoomTitle.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
                DotRoom.SetResourceReference(Shape.FillProperty, brushKey);
            }
            if (active == (PanRoom.Visibility == Visibility.Visible))
                return;
            PanStart.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            PanRoom.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (IsVisible)
                Motion.Pop(active ? PanRoom : (FrameworkElement)PanStart, 0.94);
        }

        /// <summary>Offers to join <paramref name="code"/>; <c>null</c> hides the offer.</summary>
        internal void SetQuickJoin(RoomCode code)
        {
            _quickJoinCode = code;
            RenderQuickJoin();
        }

        private void RenderQuickJoin()
        {
            Motion.Show(HintQuickJoin, _quickJoinCode != null);
            if (_quickJoinCode != null)
                LabQuickJoin.Text = Text.Format("Home.QuickJoin", _quickJoinCode.Code);
        }

        /// <summary>Shows a notice above the guide; <c>null</c> hides it.</summary>
        internal void ShowNotice(Notice notice)
        {
            _notice = notice;
            if (notice != null)
            {
                HintNotice.Style = (Style)FindResource(notice.IsWarning ? "PclHintOrange" : "PclHint");
                LabNoticeTitle.Text = notice.Title;
                LabNoticeBody.Text = notice.Body;
                SetColor(LabNoticeTitle, notice.TitleColor);
                SetColor(LabNoticeBody, notice.BodyColor);
                LabNoticeBody.Visibility = string.IsNullOrEmpty(notice.Body) ? Visibility.Collapsed : Visibility.Visible;
                bool hasButton = notice.ButtonUrl != null && !string.IsNullOrEmpty(notice.ButtonText);
                BtnNoticeAction.Content = notice.ButtonText;
                BtnNoticeAction.Visibility = hasButton ? Visibility.Visible : Visibility.Collapsed;
            }
            Motion.Show(HintNotice, notice != null);
        }

        /// <summary>The author's color, or the hint's own color when none was chosen.</summary>
        private static void SetColor(TextBlock text, Color? color)
        {
            if (color.HasValue)
                text.Foreground = new SolidColorBrush(color.Value);
            else
                text.ClearValue(TextBlock.ForegroundProperty);
        }

        private void BtnNoticeAction_Click(object sender, RoutedEventArgs e)
        {
            if (_notice?.ButtonUrl != null)
                OnlineFlow.Current?.OpenUrl(_notice.ButtonUrl);
        }

        private void BtnNoticeClose_Click(object sender, RoutedEventArgs e)
        {
            var notice = _notice;
            ShowNotice(null);
            if (notice != null)
                OnlineFlow.Current?.BannerClosed(notice);
        }

        private void TileCreate_Click(object sender, RoutedEventArgs e)
        {
            CreateRequested?.Invoke(this, EventArgs.Empty);
        }

        private void TileJoin_Click(object sender, RoutedEventArgs e)
        {
            JoinRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnViewRoom_Click(object sender, RoutedEventArgs e)
        {
            LinkRequested?.Invoke(this, EventArgs.Empty);
        }

        private void TileLobby_Click(object sender, RoutedEventArgs e)
        {
            LobbyRequested?.Invoke(this, EventArgs.Empty);
        }

        private void TileRecent_Click(object sender, RoutedEventArgs e)
        {
            RecentRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnQuickJoin_Click(object sender, RoutedEventArgs e)
        {
            var code = _quickJoinCode;
            SetQuickJoin(null);
            if (code != null)
                QuickJoinRequested?.Invoke(this, code);
        }
    }
}
