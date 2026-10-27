using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Campfire.Link;
using Campfire.Link.Lobby;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>A room picked in the lobby, with the invite code the service handed out for it.</summary>
    internal sealed class LobbyJoinEventArgs : EventArgs
    {
        public LobbyJoinEventArgs(RoomCode code, string roomId)
        {
            Code = code;
            RoomId = roomId;
        }

        public RoomCode Code { get; }

        public string RoomId { get; }
    }

    /// <summary>
    /// One room of the lobby in full: what it is about, and joining it, with its password when it has one.
    /// </summary>
    public partial class LobbyRoomPage : UserControl
    {
        private LobbyRoom _room;
        private bool _busy;

        public LobbyRoomPage()
        {
            InitializeComponent();
            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Whether a room is open or being set up, which joining another one would end.</summary>
        internal Func<bool> IsRoomActive { get; set; }

        /// <summary>Raised when the invite code of the room arrived.</summary>
        internal event EventHandler<LobbyJoinEventArgs> JoinRequested;

        /// <summary>Raised when the room turned out to be gone, so the list should be loaded again.</summary>
        internal event EventHandler RoomGone;

        internal void Show(LobbyRoom room)
        {
            _room = room;
            TxtPassword.Text = "";
            Render();
        }

        private void Render()
        {
            var room = _room;
            if (room == null)
                return;

            BoxPicture.Content = LobbyText.Picture(room, 64);
            PanTitle.Children.Clear();
            if (room.Locked)
                PanTitle.Children.Add(LobbyText.Lock(18));
            PanTitle.Children.Add(new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, Text = room.Title, TextTrimming = TextTrimming.CharacterEllipsis });
            PanPills.Children.Clear();
            LobbyText.AddPills(PanPills, room, false);
            PanPills.Visibility = PanPills.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            LabHost.Text = LobbyText.HostName(room);
            LabVersion.Text = LobbyText.VersionName(room);
            LabPlayers.Text = LobbyText.Players(room);
            LabMap.Text = room.Map.Length > 0 ? room.Map : "—";
            LabPlace.Text = LobbyText.Place(room) ?? "—";
            LabOpened.Text = LobbyText.Ago(room.CreatedAt);
            CardPassword.Visibility = room.Locked ? Visibility.Visible : Visibility.Collapsed;
            LabTip.Text = room.Version.Length > 0 ? Text.Format("LobbyRoom.Tip", room.Version) : Text.Get("LobbyRoom.TipNoVersion");
        }

        private async void BtnJoin_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            var room = _room;
            if (dialogs == null || room == null || _busy)
                return;
            if (IsRoomActive?.Invoke() == true)
            {
                await dialogs.AlertAsync(Text.Get("Lobby.InRoomTitle"), Text.Get("Lobby.InRoom"));
                return;
            }
            string password = TxtPassword.Text.Trim();
            if (room.Locked && password.Length == 0)
            {
                await dialogs.ErrorAsync(Text.Get("Lobby.JoinFailedTitle"), Text.Get("Lobby.PasswordEmpty"));
                TxtPassword.Focus();
                return;
            }

            _busy = true;
            BtnJoin.IsEnabled = false;
            try
            {
                var code = await LobbyClient.GetCodeAsync(room.Id, room.Locked ? password : null);
                if (room == _room)
                    JoinRequested?.Invoke(this, new LobbyJoinEventArgs(code, room.Id));
            }
            catch (LobbyException ex)
            {
                await dialogs.ErrorAsync(Text.Get("Lobby.JoinFailedTitle"), JoinProblem(ex));
                if (ex.Kind == LobbyError.NotFound)
                    RoomGone?.Invoke(this, EventArgs.Empty);
                else if (ex.Kind == LobbyError.WrongPassword)
                {
                    TxtPassword.Focus();
                    TxtPassword.SelectAll();
                }
            }
            finally
            {
                _busy = false;
                BtnJoin.IsEnabled = true;
            }
        }

        private static string JoinProblem(LobbyException error)
        {
            switch (error.Kind)
            {
                case LobbyError.NotFound:
                    return Text.Get("Lobby.Gone");
                case LobbyError.WrongPassword:
                case LobbyError.TooMany:
                case LobbyError.Closed:
                    return Text.Get("Lobby.Problem." + error.Kind);
                default:
                    return Text.Get("Lobby.NetworkError");
            }
        }

        private async void BtnReport_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            var room = _room;
            if (dialogs == null || room == null || !await dialogs.ConfirmAsync(Text.Get("Lobby.ReportTitle"),
                    Text.Format("Lobby.ReportMessage", room.Title), Text.Get("Lobby.ReportConfirm")))
                return;
            try
            {
                await LobbyClient.ReportAsync(room.Id, "content");
                await dialogs.AlertAsync(Text.Get("Lobby.ReportedTitle"), Text.Get("Lobby.Reported"));
            }
            catch (LobbyException ex)
            {
                await dialogs.ErrorAsync(Text.Get("Lobby.ReportTitle"),
                    ex.Kind == LobbyError.TooMany ? Text.Get("Lobby.Problem.TooMany") : Text.Get("Lobby.NetworkError"));
            }
        }

        private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnJoin_Click(sender, e);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Render();
        }
    }
}
