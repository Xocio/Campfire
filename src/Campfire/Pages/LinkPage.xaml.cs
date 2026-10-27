using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Campfire.Controls;
using Campfire.Link;
using Campfire.Link.Lobby;
using Campfire.Link.Minecraft;
using Campfire.Link.Room;
using Campfire.Link.Runtime;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// Multiplayer page: create a room for a LAN world or join a friend's room with an invite code.
    /// </summary>
    public partial class LinkPage : UserControl
    {
        private static readonly TimeSpan CopiedFeedback = TimeSpan.FromSeconds(1.6);

        /// <summary>Player names Minecraft accepts: letters, digits and underscores, 3 to 16 characters.</summary>
        private static readonly Regex ValidGameName = new Regex("^[A-Za-z0-9_]{3,16}$");

        private readonly LinkSession _session;
        private readonly List<StepRow> _stepRows = new List<StepRow>();
        private readonly DispatcherTimer _feedbackTimer;
        private readonly HashSet<string> _requestedHeads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private UIElement _shownPanel;
        private LinkRole _stepsRole;
        private LinkStage _lastStage;
        private string _playersSignature;
        private bool _enteringCode;
        private bool? _hasIPv6;

        /// <summary>Tags offered when listing a room, as the service accepts them by default.</summary>
        private static readonly string[] LobbyTagIds =
            { "survival", "creative", "building", "redstone", "minigame", "modded", "adventure", "pvp", "casual" };

        private const int MaxLobbyTags = 3;

        // Listing the room in the lobby.
        private bool _publishOpen;
        private bool _publishing;
        private bool _worldReading;
        private ServerStatus _worldStatus;

        /// <summary>The picture the room is listed with, and the one of the world being played, if found.</summary>
        private byte[] _lobbyIcon;
        private byte[] _mapIcon;

        /// <summary>The lobby room being joined, so a room that cannot be reached is reported once.</summary>
        private string _lobbyRoomId;
        private readonly HashSet<string> _reportedRooms = new HashSet<string>();

        // What to repeat when the user chooses "Retry".
        private LinkRole _lastRole;
        private RoomCode _lastCode;
        private int? _lastManualPort;

        private bool _codeCopied;
        private DateTime _codeCopiedAt = DateTime.MinValue;
        private DateTime _addressCopiedAt = DateTime.MinValue;

        public LinkPage()
        {
            InitializeComponent();

            _session = new LinkSession(new DispatcherSynchronizationContext(Dispatcher));
            _session.Changed += (sender, e) => Render();
            // Player rows carry their colors, so they are rebuilt with a new theme.
            Themes.ThemeManager.Changed += (sender, e) =>
            {
                _playersSignature = null;
                Render();
            };
            _shownPanel = PanSelect;

            _feedbackTimer = new DispatcherTimer { Interval = CopiedFeedback };
            _feedbackTimer.Tick += (sender, e) =>
            {
                _feedbackTimer.Stop();
                Render();
            };

            TxtPlayerName.Text = LinkSettings.PlayerName;

            Text.LanguageChanged += OnLanguageChanged;
            Loaded += OnFirstLoaded;
            IsVisibleChanged += OnVisibleChanged;
            Render();
        }

        /// <summary>Raised whenever the room changes, for status shown outside this page.</summary>
        public event EventHandler StatusChanged;

        /// <summary>Raised when the user asks for the full settings.</summary>
        public event EventHandler SettingsRequested;

        /// <summary>Raised when the user wants to browse the public lobby.</summary>
        public event EventHandler LobbyRequested;

        /// <summary>Whether a room is being set up or is open.</summary>
        public bool IsRoomActive => _session.IsActive;

        internal LinkStage Stage => _session.Stage;

        internal LinkRole Role => _session.Role;

        internal int PlayerCount => _session.Players.Count;

        internal RoomCode Code => _session.Code;

        /// <summary>Whether the room has other players who would be disconnected.</summary>
        public bool HasOtherPlayers => _session.Stage == LinkStage.Ready && _session.Players.Count > 1;

        internal IReadOnlyList<PlayerProfile> Players => _session.Players;

        /// <summary>When the room opened or was joined, in UTC.</summary>
        internal DateTime ReadySince { get; private set; }

        /// <summary>The step being worked on and its progress, while a room is being set up.</summary>
        internal (string Title, string Detail) ActiveStep
        {
            get
            {
                foreach (var step in _session.Steps)
                {
                    var state = _session.GetStepState(step);
                    if (state == StepState.Active)
                        return (StepTitle(step), StepDetail(step, state));
                }
                return (Text.Get(_session.Role == LinkRole.Host ? "Link.Creating" : "Link.Joining"), "");
            }
        }

        /// <summary>Round-trip time to the host in milliseconds, or <c>null</c> when not measured yet.</summary>
        internal double? HostLatency => _session.HostPeer?.LatencyMs;

        internal string LatencyText => HostLatency == null ? "—" : LinkSession.FormatLatency(HostLatency.Value) + " ms";

        internal string HostPathText => PathText(_session.HostPeer);

        internal string ConnectionsText => _session.DirectPeers + _session.RelayedPeers == 0
            ? Text.Get("Link.NoConnections")
            : Text.Format("Link.ConnectionsValue", _session.DirectPeers, _session.RelayedPeers);

        internal string LocalNatText => NatText(_session.LocalNat);

        internal int MinecraftPort => _session.MinecraftPort;

        internal AutoLanState AutoLan => _session.AutoLan;

        internal string GameAddress => AddressText();

        /// <summary>Closes or leaves the room, asking first as the page's own button does.</summary>
        internal void RequestStop()
        {
            if (IsRoomActive)
                BtnStop_Click(this, new RoutedEventArgs());
        }

        /// <summary>Copies the invite code for the host; returns whether the clipboard took it.</summary>
        internal bool CopyCode()
        {
            return _session.Code != null && TryCopy(_session.Code.Code);
        }

        /// <summary>Copies the game address for a guest; returns whether the clipboard took it.</summary>
        internal bool CopyAddress()
        {
            return TryCopy(AddressText());
        }

        /// <summary>Joins <paramref name="code"/> right away, unless a room is already active.</summary>
        /// <param name="lobbyRoomId">The lobby room the code came from, or <c>null</c>.</param>
        internal async void JoinWithCode(RoomCode code, string lobbyRoomId = null)
        {
            if (_session.IsActive)
                return;
            TxtCode.Text = code.Code;
            _lobbyRoomId = lobbyRoomId;
            await StartJoinAsync(code);
        }

        /// <summary>Reloads values that may have been changed on the settings page.</summary>
        internal void ReloadSettings()
        {
            TxtPlayerName.Text = LinkSettings.PlayerName;
            UpdatePlaceholders();
            UpdateSelfHead();
            UpdateNetworkLabel();
            _playersSignature = null;
            Render();
        }

        /// <summary>Ends the room immediately; called when Campfire exits.</summary>
        public void Shutdown()
        {
            _session.Shutdown();
        }

        // ======================== Rendering ========================

        private void Render()
        {
            UIElement panel = _shownPanel;
            if (_session.Stage != LinkStage.Ready || _session.Role != LinkRole.Host)
                _publishOpen = false;
            switch (_session.Stage)
            {
                case LinkStage.Idle:
                    panel = _enteringCode ? PanJoin : PanSelect;
                    break;
                case LinkStage.Working:
                    RenderWorking();
                    panel = PanWorking;
                    break;
                case LinkStage.Ready:
                    if (_lastStage != LinkStage.Ready)
                        ReadySince = DateTime.UtcNow;
                    if (_session.Role == LinkRole.Host)
                    {
                        if (_lastStage != LinkStage.Ready)
                        {
                            _codeCopied = LinkSettings.AutoCopyCode && TryCopy(_session.Code.Code);
                            _codeCopiedAt = DateTime.MinValue;
                        }
                        RenderHost();
                        panel = _publishOpen ? PanPublish : PanHost;
                    }
                    else
                    {
                        RenderGuest();
                        panel = PanGuest;
                    }
                    break;
                case LinkStage.Failed:
                    // The page stays as it was; the error is shown in a dialog over it.
                    if (_lastStage != LinkStage.Failed)
                        ShowFailure();
                    break;
            }

            _lastStage = _session.Stage;
            ShowPanel(panel);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RenderWorking()
        {
            bool host = _session.Role == LinkRole.Host;
            CardWorking.Header = Text.Get(host ? "Link.Creating" : "Link.Joining");

            if (_stepsRole != _session.Role || _stepRows.Count == 0)
            {
                _stepsRole = _session.Role;
                _stepRows.Clear();
                _stepRows.AddRange(_session.Steps.Select(step => new StepRow(step)));
                ListSteps.ItemsSource = null;
                ListSteps.ItemsSource = _stepRows;
            }

            foreach (var row in _stepRows)
            {
                row.State = _session.GetStepState(row.Step);
                row.Title = StepTitle(row.Step);
                row.Detail = StepDetail(row.Step, row.State);
            }

            var download = _session.Download;
            bool downloading = download.HasValue && _session.GetStepState(LinkStep.Component) == StepState.Active;
            BarDownload.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            Motion.Progress(BarDownload, downloading ? download.Value.Fraction : 0);

            Motion.Show(HintWorld, host && _session.GetStepState(LinkStep.World) == StepState.Active);
            LabAutoLan.Visibility = _session.AutoLan == AutoLanState.Off ? Visibility.Collapsed : Visibility.Visible;
            if (_session.AutoLan != AutoLanState.Off)
                LabAutoLan.Text = Text.Get("Link.AutoLan." + _session.AutoLan);
            Motion.Show(HintSearch, !host && _session.GetStepState(LinkStep.FindHost) == StepState.Active
                                    && _session.SearchTime.TotalSeconds >= 12);
        }

        private string StepTitle(LinkStep step)
        {
            switch (step)
            {
                case LinkStep.Component:
                    return Text.Get("Link.Step.Component");
                case LinkStep.World:
                    return Text.Get("Link.Step.World");
                case LinkStep.Network:
                    return Text.Get(_session.Role == LinkRole.Host ? "Link.Step.CreateRoom" : "Link.Step.Network");
                case LinkStep.FindHost:
                    return Text.Get("Link.Step.FindHost");
                default:
                    return Text.Get("Link.Step.Enter");
            }
        }

        private string StepDetail(LinkStep step, StepState state)
        {
            switch (step)
            {
                case LinkStep.Component when state == StepState.Active:
                    var download = _session.Download;
                    return download.HasValue
                        ? Text.Format("Link.Step.Downloading", Megabytes(download.Value.Received), Megabytes(download.Value.Total))
                        : Text.Get("Link.Step.Checking");
                case LinkStep.World when state == StepState.Done:
                    return Text.Format("Link.Step.WorldPort", _session.MinecraftPort);
                case LinkStep.FindHost when state == StepState.Active:
                    return Text.Format("Link.Step.Waited", (int)_session.SearchTime.TotalSeconds);
                case LinkStep.FindHost when state == StepState.Done:
                    return PathText(_session.HostPeer);
                default:
                    return "";
            }
        }

        private void RenderHost()
        {
            LabCode.Text = _session.Code?.Code;
            BtnCopyCode.Content = Text.Get(IsRecent(_codeCopiedAt) ? "Link.Copied" : "Link.CopyCode");
            LabHostTip.Text = Text.Get(_codeCopied ? "Link.HostTipCopied" : "Link.HostTip");
            LabHostPort.Text = _session.MinecraftPort.ToString(CultureInfo.InvariantCulture);
            LabHostLinks.Text = ConnectionsText;
            LabHostNat.Text = LocalNatText;
            Motion.Show(HintHostNodes, _session.ConnectedNodes == 0);
            RenderLobby();

            CardHostPlayers.Header = Text.Format("Link.Players", _session.Players.Count);
            RenderPlayers(ListHostPlayers);
        }

        private void RenderGuest()
        {
            LabGuestGuide.Text = Text.Get("Link.GuestGuide");
            LabAddress.Text = AddressText();
            BtnCopyAddress.Content = Text.Get(IsRecent(_addressCopiedAt) ? "Link.Copied" : "Link.CopyAddress");

            LabLatency.Text = LatencyText;
            LabPath.Text = HostPathText;
            LabGuestNat.Text = LocalNatText;

            CardGuestPlayers.Header = Text.Format("Link.Players", _session.Players.Count);
            RenderPlayers(ListGuestPlayers);
        }

        private void RenderPlayers(ItemsControl list)
        {
            bool skins = LinkSettings.ShowSkins;
            if (skins)
            {
                foreach (var player in _session.Players)
                    RequestHead(player.Uuid);
            }

            string signature = Text.Language + "|" + skins + "|" + string.Join("|", _session.Players.Select(p =>
                p.MachineId + ":" + p.Name + ":" + p.Kind + ":" + (PlayerHeads.TryGetCached(p.Uuid) != null)));
            if (signature == _playersSignature && list.ItemsSource != null)
                return;
            _playersSignature = signature;
            ListHostPlayers.ItemsSource = null;
            ListGuestPlayers.ItemsSource = null;
            list.ItemsSource = _session.Players
                .Select(p => new PlayerRow(p, skins ? PlayerHeads.TryGetCached(p.Uuid) : null,
                    _session.Role == LinkRole.Host && p.Kind == PlayerKind.Guest))
                .ToList();
        }

        private async void BtnRemovePlayer_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is PlayerRow row))
                return;
            if (await Dialogs.Of(this).ConfirmAsync(Text.Get("Link.RemoveTitle"), Text.Format("Link.RemoveMessage", row.Name),
                    Text.Get("Link.Remove")))
                _session.RemovePlayer(row.Profile, false);
        }

        private async void BtnBlockPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is PlayerRow row))
                return;
            if (await Dialogs.Of(this).ConfirmAsync(Text.Get("Link.BlockTitle"), Text.Format("Link.BlockMessage", row.Name),
                    Text.Get("Link.Block")))
                _session.RemovePlayer(row.Profile, true);
        }

        /// <summary>Starts loading a skin face; the page redraws when it arrives.</summary>
        private void RequestHead(string uuid)
        {
            if (uuid == null || !_requestedHeads.Add(uuid))
                return;
            PlayerHeads.GetAsync(uuid).ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _playersSignature = null;
                        Render();
                        UpdateSelfHead();
                    }));
            }, TaskScheduler.Default);
        }

        private void ShowPanel(UIElement panel)
        {
            if (panel == _shownPanel)
                return;

            // Moving on through the steps slides forward; going back to the start slides back.
            int direction = Math.Sign(PanelOrder(panel) - PanelOrder(_shownPanel));
            Motion.Swap((FrameworkElement)_shownPanel, (FrameworkElement)panel, direction == 0 ? 1 : direction);
            _shownPanel = panel;
        }

        private int PanelOrder(UIElement panel)
        {
            return panel == PanSelect ? 0 : panel == PanJoin ? 1 : panel == PanWorking ? 2 : panel == PanPublish ? 4 : 3;
        }

        /// <summary>Shows why the room failed, with the choice to try again.</summary>
        private async void ShowFailure()
        {
            var host = Dialogs.Of(this);
            var error = _session.Error;
            if (host == null || error == null)
                return;

            // A lobby room nobody can reach is reported, so it leaves the lobby once several players fail.
            if (_session.Role == LinkRole.Guest && _lobbyRoomId != null && _reportedRooms.Add(_lobbyRoomId)
                && (error.TextKey == "Link.Error.RoomNotFound" || error.TextKey == "Link.Error.HostNoAnswer"))
                _ = ReportUnreachableAsync(_lobbyRoomId);

            string adviceKey = error.TextKey + ".Advice";
            var choice = await host.ShowDialogAsync(new DialogRequest
            {
                Title = Text.Get(_session.Role == LinkRole.Host ? "Link.CreateFailed" : "Link.JoinFailed"),
                Message = error.LocalizedMessage,
                Detail = Text.Has(adviceKey) ? Text.Get(adviceKey) : null,
                PrimaryText = Text.Get("Link.Retry"),
                SecondaryText = Text.Get("Dialog.Close"),
                LinkText = Text.Get("Link.OpenLog"),
                LinkAction = OpenLog,
                IsError = true,
            });

            // The user may have started something else meanwhile.
            if (_session.Stage != LinkStage.Failed)
                return;
            if (choice == DialogChoice.Primary)
                Retry();
            else
                ReturnToStart();
        }

        // ======================== Choose ========================

        /// <summary>Starts opening a room, as the "Create room" tile does; ignored while a room is active.</summary>
        internal void StartCreate()
        {
            if (!IsRoomActive)
                BtnCreate_Click(this, new RoutedEventArgs());
        }

        /// <summary>Shows the invite code entry, as the "Join room" tile does; ignored while a room is active.</summary>
        internal void StartJoin()
        {
            if (!IsRoomActive)
                BtnJoin_Click(this, new RoutedEventArgs());
        }

        private async void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            if (!await CheckGameNameAsync())
                return;
            _lastRole = LinkRole.Host;
            _lastManualPort = null;
            ResetManualPort();
            _session.StartHost(null);
        }

        private void BtnJoin_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            _enteringCode = true;

            // A code copied from a chat is filled in right away.
            if (LinkSettings.DetectClipboard)
            {
                var copied = RoomCode.Find(TryReadClipboard());
                if (copied != null && RoomCode.Find(TxtCode.Text)?.Code != copied.Code)
                    TxtCode.Text = copied.Code;
            }

            Render();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtCode.Focus();
                TxtCode.CaretIndex = TxtCode.Text.Length;
            }), DispatcherPriority.Input);
        }

        private void BtnMoreSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Settings_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SaveSettings();
        }

        private void SaveSettings()
        {
            if (LinkSettings.PlayerName != TxtPlayerName.Text.Trim())
                LinkSettings.PlayerName = TxtPlayerName.Text;
            UpdatePlaceholders();
        }

        /// <summary>
        /// Warns when the running game uses a player name Minecraft rejects, which makes every world fail with
        /// "Connection lost". Returns whether to go on.
        /// </summary>
        private async Task<bool> CheckGameNameAsync()
        {
            var account = await GameAccount.RefreshAsync();
            if (account == null || ValidGameName.IsMatch(account.Name))
                return true;

            var host = Dialogs.Of(this);
            return host == null || await host.ConfirmAsync(Text.Get("Link.BadGameNameTitle"),
                Text.Format("Link.BadGameNameMessage", account.Name), Text.Get("Link.ContinueAnyway"));
        }

        // ======================== Join ========================

        private void TxtCode_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCodeState();
        }

        private void TxtCode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BtnJoinConfirm.IsEnabled)
                BtnJoinConfirm_Click(sender, e);
        }

        private async void BtnPaste_Click(object sender, RoutedEventArgs e)
        {
            string text = TryReadClipboard();
            var code = RoomCode.Find(text);
            if (code == null)
            {
                await Dialogs.Of(this).AlertAsync(Text.Get("Link.NoCodeOnClipboardTitle"), Text.Get("Link.NoCodeOnClipboard"));
                return;
            }
            TxtCode.Text = code.Code;
            TxtCode.CaretIndex = TxtCode.Text.Length;
            TxtCode.Focus();
        }

        private async void BtnJoinConfirm_Click(object sender, RoutedEventArgs e)
        {
            var code = RoomCode.Find(TxtCode.Text);
            if (code == null)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("Link.InvalidCodeTitle"), Text.Get("Link.InvalidCode"));
                TxtCode.Focus();
                return;
            }
            _lobbyRoomId = null;
            await StartJoinAsync(code);
        }

        private async Task StartJoinAsync(RoomCode code)
        {
            SaveSettings();
            if (!await CheckGameNameAsync())
                return;
            _enteringCode = false;
            _lastRole = LinkRole.Guest;
            _lastCode = code;
            _session.StartJoin(code);
        }

        private void UpdateCodeState()
        {
            string text = TxtCode.Text.Trim();
            var code = RoomCode.Find(text);
            BtnJoinConfirm.IsEnabled = text.Length > 0;

            if (text.Length == 0 || code == null)
            {
                LabCodeState.Text = Text.Get("Link.CodeHint");
                LabCodeState.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray4");
            }
            else
            {
                LabCodeState.Text = string.Equals(text, code.Code, StringComparison.OrdinalIgnoreCase)
                    ? Text.Get("Link.CodeValid")
                    : Text.Format("Link.CodeFound", code.Code);
                LabCodeState.SetResourceReference(TextBlock.ForegroundProperty, "BrushGreen");
            }
        }

        // ======================== In progress ========================

        private void BtnManualPort_Click(object sender, RoutedEventArgs e)
        {
            BtnManualPort.Visibility = Visibility.Collapsed;
            PanPortInput.Visibility = Visibility.Visible;
            TxtPort.Focus();
        }

        private void TxtPort_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnUsePort_Click(sender, e);
        }

        private async void BtnUsePort_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtPort.Text.Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("Link.InvalidPortTitle"),
                    text.Length == 0 ? Text.Get("Link.EmptyPort") : Text.Format("Link.InvalidPort", text));
                TxtPort.Focus();
                TxtPort.SelectAll();
                return;
            }

            _lastManualPort = port;
            _session.StartHost(port);
        }

        private void ResetManualPort()
        {
            BtnManualPort.Visibility = Visibility.Visible;
            PanPortInput.Visibility = Visibility.Collapsed;
        }

        // ======================== Room ========================

        private async void BtnCopyCode_Click(object sender, RoutedEventArgs e)
        {
            if (_session.Code == null)
                return;
            if (TryCopy(_session.Code.Code))
                ShowCopied(ref _codeCopiedAt);
            else
                await Dialogs.Of(this).ErrorAsync(Text.Get("Link.CopyFailedTitle"), Text.Get("Link.CopyFailed"));
        }

        private async void BtnCopyAddress_Click(object sender, RoutedEventArgs e)
        {
            if (TryCopy(AddressText()))
                ShowCopied(ref _addressCopiedAt);
            else
                await Dialogs.Of(this).ErrorAsync(Text.Get("Link.CopyFailedTitle"), Text.Get("Link.CopyFailed"));
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            if (_session.Stage == LinkStage.Ready && dialogs != null)
            {
                bool host = _session.Role == LinkRole.Host;
                bool confirmed = await dialogs.ConfirmAsync(
                    Text.Get(host ? "Link.CloseRoomTitle" : "Link.LeaveRoomTitle"),
                    Text.Get(host ? "Link.CloseRoomMessage" : "Link.LeaveRoomMessage"),
                    Text.Get(host ? "Link.CloseRoom" : "Link.LeaveRoom"));
                if (!confirmed)
                    return;
            }

            ReturnToStart();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            ReturnToStart();
        }

        private void ReturnToStart()
        {
            _enteringCode = false;
            if (_session.Stage == LinkStage.Idle)
                Render();
            else
                _session.Stop();
        }

        private void Retry()
        {
            if (_lastRole == LinkRole.Host)
            {
                ResetManualPort();
                _session.StartHost(_lastManualPort);
            }
            else if (_lastCode != null)
            {
                _session.StartJoin(_lastCode);
            }
            else
            {
                ReturnToStart();
            }
        }

        private void OpenLog()
        {
            try
            {
                if (File.Exists(LinkLog.FilePath))
                    Process.Start(new ProcessStartInfo(LinkLog.FilePath) { UseShellExecute = true });
                else
                    Process.Start("explorer.exe", "\"" + LinkSettings.DataDirectory + "\"");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException)
            {
                Process.Start("explorer.exe", $"/select,\"{LinkLog.FilePath}\"");
            }
        }

        // ======================== Lobby ========================

        private void RenderLobby()
        {
            bool available = LobbyClient.IsAvailable;
            PanLobbyState.Visibility = BtnLobby.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            var lobby = _session.Lobby;
            string text = null;
            string brush = null;
            if (lobby == null)
            {
                text = Text.Get("Lobby.State.None");
            }
            else if (lobby.State == LobbyState.Listed)
            {
                text = Text.Get(lobby.Locked ? "Lobby.State.ListedLocked" : "Lobby.State.Listed");
                brush = "BrushGreen";
            }
            else if (lobby?.State == LobbyState.Publishing)
            {
                text = Text.Get(lobby.Error == null ? "Lobby.State.Publishing" : "Lobby.State.Retrying");
                brush = lobby.Error == null ? "Brush3" : "BrushOrange";
            }
            else if (lobby?.State == LobbyState.Stopped)
            {
                text = lobby.Error != null ? LobbyText.Problem(lobby.Error) : Text.Get("Lobby.Problem.Other");
                brush = "BrushRed";
            }

            LabLobby.Text = text ?? "";
            LabLobby.ToolTip = text;
            DotLobby.Visibility = brush == null ? Visibility.Collapsed : Visibility.Visible;
            if (brush != null)
                DotLobby.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);

            // Listing is the step to take, so it stands out; taking it down is an ordinary button.
            bool listed = lobby != null && lobby.State != LobbyState.Stopped;
            BtnLobby.Content = Text.Get(listed ? "Lobby.Unpublish" : "Lobby.Publish");
            BtnLobby.Style = (Style)FindResource(listed ? "PclButton" : "PclButtonHighlight");
            BtnLobby.FontWeight = listed ? FontWeights.Normal : FontWeights.Bold;
        }

        private void BtnLobby_Click(object sender, RoutedEventArgs e)
        {
            var lobby = _session.Lobby;
            if (lobby != null && lobby.State != LobbyState.Stopped)
                _session.Unpublish();
            else
                OpenPublish();
        }

        /// <summary>Shows the form for listing the open room, filled in from last time and from the world itself.</summary>
        private async void OpenPublish()
        {
            if (_session.Role != LinkRole.Host || _session.Stage != LinkStage.Ready)
                return;
            _publishOpen = true;
            string title = LinkSettings.LobbyTitle;
            TxtLobbyTitle.Text = title.Length > 0 ? title : Text.Format("Lobby.DefaultTitle", LinkSettings.EffectivePlayerName);
            // A password is for this room only, so it is never kept for the next one.
            TxtLobbyPassword.Text = "";
            _lobbyIcon = null;
            _mapIcon = null;
            RenderLobbyIcon();
            FillLobbyTags(LinkSettings.LobbyTags);
            ChkLobbyRegion.IsChecked = LinkSettings.LobbyShowRegion;
            SetPublishing(false);
            _worldStatus = null;
            _worldReading = true;
            RenderWorldStatus();
            Render();
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtLobbyTitle.Focus();
                TxtLobbyTitle.SelectAll();
            }), DispatcherPriority.Input);

            int port = _session.MinecraftPort;
            var status = await MinecraftProbe.GetStatusAsync(port, TimeSpan.FromSeconds(3));
            if (!_publishOpen || _session.MinecraftPort != port)
                return;
            _worldStatus = status;
            _worldReading = false;
            RenderWorldStatus();

            // The world's own picture is offered first.
            var mapIcon = await RoomIcon.FindMapIconAsync(status?.WorldName);
            if (!_publishOpen || _session.MinecraftPort != port)
                return;
            _mapIcon = mapIcon;
            if (_lobbyIcon == null)
                _lobbyIcon = mapIcon;
            RenderLobbyIcon();
        }

        private void RenderLobbyIcon()
        {
            var image = RoomIcon.Decode(_lobbyIcon);
            if (image != null)
            {
                var brush = new System.Windows.Media.ImageBrush(image) { Stretch = System.Windows.Media.Stretch.UniformToFill };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(brush, System.Windows.Media.BitmapScalingMode.HighQuality);
                BoxLobbyIcon.Background = brush;
            }
            else
            {
                // No picture of its own: the room is listed with the default one.
                BoxLobbyIcon.Background = RoomIcon.DefaultBrush();
            }
            GlyphLobbyIcon.Visibility = Visibility.Collapsed;
            BtnLobbyIconMap.Visibility = _mapIcon != null && _lobbyIcon != _mapIcon ? Visibility.Visible : Visibility.Collapsed;
            BtnLobbyIconPick.Content = Text.Get(_lobbyIcon == null ? "Lobby.IconPick" : "Lobby.IconChange");
            BtnLobbyIconRemove.Visibility = _lobbyIcon != null ? Visibility.Visible : Visibility.Collapsed;
            // The first button of the row has no gap in front of it.
            BtnLobbyIconPick.Margin = new Thickness(BtnLobbyIconMap.Visibility == Visibility.Visible ? 14 : 0, 0, 0, 0);
        }

        private async void BtnLobbyIconPick_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Text.Get("Lobby.IconPickTitle"),
                Filter = Text.Get("Lobby.IconFilter") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;
            var icon = RoomIcon.FromFile(dialog.FileName);
            if (icon == null)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("Lobby.IconFailedTitle"), Text.Get("Lobby.IconFailed"));
                return;
            }
            _lobbyIcon = icon;
            RenderLobbyIcon();
        }

        private void BtnLobbyIconMap_Click(object sender, RoutedEventArgs e)
        {
            _lobbyIcon = _mapIcon;
            RenderLobbyIcon();
        }

        private void BtnLobbyIconRemove_Click(object sender, RoutedEventArgs e)
        {
            _lobbyIcon = null;
            RenderLobbyIcon();
        }

        private void RenderWorldStatus()
        {
            var status = _worldStatus;
            if (_worldReading)
                LabLobbyWorld.Text = Text.Get("Lobby.WorldReading");
            else if (status == null || status.Version.Length == 0)
                LabLobbyWorld.Text = Text.Get("Lobby.WorldUnknown");
            else if (status.WorldName.Length > 0)
                LabLobbyWorld.Text = Text.Format("Lobby.WorldInfo", status.Version, status.WorldName);
            else
                LabLobbyWorld.Text = Text.Format("Lobby.WorldVersion", status.Version);
        }

        private void FillLobbyTags(IEnumerable<string> selected)
        {
            var chosen = new HashSet<string>(selected.Take(MaxLobbyTags));
            PanLobbyTags.Children.Clear();
            foreach (string id in LobbyTagIds)
            {
                string name = LobbyText.Tag(id);
                if (name == null)
                    continue;
                var chip = new CheckBox { Style = (Style)FindResource("PclSegment"), Content = name, Tag = id, IsChecked = chosen.Contains(id) };
                chip.Checked += LobbyTag_Checked;
                PanLobbyTags.Children.Add(chip);
            }
            var hint = new TextBlock
            {
                Margin = new Thickness(4, 0, 0, 6),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Text = Text.Get("Lobby.TagsHint"),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray5");
            PanLobbyTags.Children.Add(hint);
        }

        private List<string> SelectedLobbyTags()
        {
            return PanLobbyTags.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
        }

        private void LobbyTag_Checked(object sender, RoutedEventArgs e)
        {
            if (SelectedLobbyTags().Count > MaxLobbyTags)
                ((CheckBox)sender).IsChecked = false;
        }

        private void TxtLobbyTitle_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BtnPublishConfirm.IsEnabled)
                BtnPublishConfirm_Click(sender, e);
        }

        private void BtnPublishBack_Click(object sender, RoutedEventArgs e)
        {
            _publishOpen = false;
            Render();
        }

        private async void BtnPublishConfirm_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            if (_publishing || dialogs == null)
                return;
            string title = TxtLobbyTitle.Text.Trim();
            if (title.Length == 0)
            {
                await dialogs.ErrorAsync(Text.Get("Lobby.PublishFailedTitle"), Text.Get("Lobby.TitleEmpty"));
                TxtLobbyTitle.Focus();
                return;
            }

            var tags = SelectedLobbyTags();
            var status = _worldStatus;
            var listing = new LobbyListing
            {
                Code = _session.Code,
                Title = title,
                Host = LinkSettings.EffectivePlayerName,
                Version = status?.Version ?? "",
                Map = status?.WorldName ?? "",
                Tags = tags,
                MaxPlayers = status?.MaxPlayers ?? 0,
                ShowRegion = ChkLobbyRegion.IsChecked == true,
                Password = TxtLobbyPassword.Text.Trim(),
                Icon = _lobbyIcon,
            };
            LinkSettings.LobbyTitle = title;
            LinkSettings.LobbyTags = tags;
            LinkSettings.LobbyShowRegion = listing.ShowRegion;

            SetPublishing(true);
            try
            {
                var ticket = await LobbyClient.PublishAsync(listing, _session.Players.Count);
                // The room may have closed while the service answered.
                if (!_session.Publish(listing, ticket))
                {
                    _ = LobbyPublisher.CloseQuietlyAsync(ticket);
                    return;
                }
                _publishOpen = false;
                Render();
            }
            catch (LobbyException ex)
            {
                await dialogs.ErrorAsync(Text.Get("Lobby.PublishFailedTitle"), LobbyText.Problem(ex));
                if (ex.Kind == LobbyError.Blocked && _publishOpen)
                {
                    TxtLobbyTitle.Focus();
                    TxtLobbyTitle.SelectAll();
                }
            }
            finally
            {
                SetPublishing(false);
            }
        }

        private void SetPublishing(bool publishing)
        {
            _publishing = publishing;
            BtnPublishConfirm.IsEnabled = !publishing;
            BtnPublishConfirm.Content = Text.Get(publishing ? "Lobby.Publishing" : "Lobby.PublishConfirm");
        }

        private void BtnGoLobby_Click(object sender, RoutedEventArgs e)
        {
            LobbyRequested?.Invoke(this, EventArgs.Empty);
        }

        private static async Task ReportUnreachableAsync(string roomId)
        {
            try
            {
                await LobbyClient.ReportAsync(roomId, "unreachable");
            }
            catch (LobbyException ex)
            {
                LinkLog.Warn("The unreachable lobby room could not be reported: " + ex.Message);
            }
        }

        // ======================== Helpers ========================

        private async void OnFirstLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnFirstLoaded;
            UpdatePlaceholders();
            BtnGoLobby.Visibility = LobbyClient.IsAvailable ? Visibility.Visible : Visibility.Collapsed;

            if (!EasyTierPackage.IsSupportedSystem)
            {
                BtnCreate.IsEnabled = false;
                BtnJoin.IsEnabled = false;
                UpdateNetworkLabel();
                return;
            }

            _hasIPv6 = await Task.Run(() => NetworkCheck.HasPublicIPv6());
            UpdateNetworkLabel();
            Render();
        }

        /// <summary>Each time the page is shown, picks up the name and skin of a game started meanwhile.</summary>
        private async void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
                return;
            var account = await GameAccount.RefreshAsync();
            UpdatePlaceholders();
            UpdateSelfHead();
            UpdateNetworkLabel();
            if (LinkSettings.ShowSkins && account != null && account.IsPremium)
                RequestHead(account.Uuid);
        }

        /// <summary>Shows the skin face inside the name box when the running game uses a premium account.</summary>
        private void UpdateSelfHead()
        {
            var account = GameAccount.Current;
            var head = LinkSettings.ShowSkins && account != null && account.IsPremium ? PlayerHeads.TryGetCached(account.Uuid) : null;
            ImgSelfHead.Source = head;
            ImgSelfHead.Visibility = head == null ? Visibility.Collapsed : Visibility.Visible;
            if (head == null)
                TxtPlayerName.ClearValue(PaddingProperty);
            else
                TxtPlayerName.Padding = new Thickness(32, 0, 8, 0);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_publishOpen)
            {
                FillLobbyTags(SelectedLobbyTags());
                RenderWorldStatus();
                RenderLobbyIcon();
                SetPublishing(_publishing);
            }
            _playersSignature = null;
            UpdatePlaceholders();
            UpdateNetworkLabel();
            UpdateCodeState();
            Render();
        }

        private void UpdatePlaceholders()
        {
            string game = GameAccount.Current?.Name;
            TxtPlayerName.Tag = string.IsNullOrEmpty(game)
                ? Text.Format("Link.PlayerNamePlaceholder", LinkSettings.FallbackPlayerName)
                : Text.Format("Link.PlayerNameFromGame", game);
        }

        private void UpdateNetworkLabel()
        {
            string key;
            string brush;
            if (!EasyTierPackage.IsSupportedSystem)
            {
                key = "Link.SystemTooOld";
                brush = "BrushRed";
            }
            else if (LinkSettings.HideAddress)
            {
                key = "Link.NetworkHidden";
                brush = "Brush3";
            }
            else if (_hasIPv6 == null)
            {
                key = "Link.NetworkChecking";
                brush = "BrushGray5";
            }
            else if (_hasIPv6 == true)
            {
                key = "Link.NetworkIPv6";
                brush = "BrushGreen";
            }
            else
            {
                key = "Link.NetworkNoIPv6";
                brush = "BrushGray5";
            }
            LabNetwork.Text = Text.Get(key);
            DotNetwork.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        }

        private string NatText(NatKind nat)
        {
            string text = Text.Get("Link.Nat." + nat);
            return _hasIPv6 == true && !LinkSettings.HideAddress ? text + " · IPv6" : text;
        }

        private static string PathText(EasyTierPeer peer)
        {
            if (peer == null)
                return "—";
            string path = Text.Get(peer.IsDirect ? "Link.Path.Direct" : "Link.Path.Relay");
            return string.IsNullOrEmpty(peer.Tunnel) || peer.Tunnel == "-"
                ? path
                : path + " · " + peer.Tunnel.ToUpperInvariant();
        }

        private string AddressText()
        {
            return _session.LocalPort == 25565 ? "127.0.0.1" : "127.0.0.1:" + _session.LocalPort;
        }

        private void ShowCopied(ref DateTime copiedAt)
        {
            copiedAt = DateTime.UtcNow;
            _feedbackTimer.Stop();
            _feedbackTimer.Start();
            Render();
        }

        private static bool IsRecent(DateTime time)
        {
            return DateTime.UtcNow - time < CopiedFeedback;
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static bool TryCopy(string text)
        {
            // Another program may hold the clipboard for a moment; retry briefly.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (ExternalException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            return false;
        }

        internal static string TryReadClipboard()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (ExternalException)
            {
                return null;
            }
        }
    }
}
