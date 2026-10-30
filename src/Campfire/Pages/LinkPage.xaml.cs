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
        private bool _open;
        private bool _preparing;
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

        /// <summary>The lobby room being joined, and the rooms already reported.</summary>
        private string _lobbyRoomId;
        private readonly HashSet<string> _reportedRooms = new HashSet<string>();

        // What to repeat when the user chooses "Retry".
        private LinkRole _lastRole;
        private RoomCode _lastCode;
        private int? _lastManualPort;

        /// <summary>The invite code of an earlier room that the current room runs with again, if any.</summary>
        private RoomCode _lastHostCode;

        // Comparing the host's mods with this computer's, as a guest.
        private Campfire.Mods.ModCheck _modCheck;
        private Campfire.Mods.ModList _checkedHost;

        private bool _codeCopied;
        private DateTime _codeCopiedAt = DateTime.MinValue;
        private DateTime _addressCopiedAt = DateTime.MinValue;

        public LinkPage()
        {
            InitializeComponent();

            _session = new LinkSession(new DispatcherSynchronizationContext(Dispatcher));
            _session.Changed += (sender, e) => Render();
            // Player rows are rebuilt with a new theme.
            Themes.ThemeManager.Changed += (sender, e) =>
            {
                _playersSignature = null;
                Render();
            };
            _shownPanel = PanJoin;
            RoomHistory.Changed += (sender, e) => Dispatcher.BeginInvoke(new Action(Render));

            _feedbackTimer = new DispatcherTimer { Interval = CopiedFeedback };
            _feedbackTimer.Tick += (sender, e) =>
            {
                _feedbackTimer.Stop();
                Render();
            };

            Text.LanguageChanged += OnLanguageChanged;
            Loaded += OnFirstLoaded;
            Render();
        }

        /// <summary>Raised whenever the room changes, for status shown outside this page.</summary>
        public event EventHandler StatusChanged;

        /// <summary>
        /// Raised when the page has nothing left to show: the room ended, or the user left the invite code entry.
        /// The window then goes back to where the page was opened from.
        /// </summary>
        public event EventHandler Left;

        /// <summary>Raised when the user asks for the connection diagnostics.</summary>
        public event EventHandler DiagnosticsRequested;

        /// <summary>Raised when the user wants to browse the public lobby.</summary>
        public event EventHandler LobbyRequested;

        /// <summary>Raised when the user wants to compare or sync mods with the host.</summary>
        public event EventHandler ModsRequested;

        /// <summary>Folders that hold a launcher, also searched for installed versions; set by the window.</summary>
        internal Func<IEnumerable<string>> LauncherDirectories { get; set; }

        internal LinkSession Session => _session;

        /// <summary>Whether a room is being set up or is open.</summary>
        public bool IsRoomActive => _session.IsActive;

        internal LinkStage Stage => _session.Stage;

        internal LinkRole Role => _session.Role;

        internal int PlayerCount => _session.Players.Count;

        internal RoomCode Code => _session.Code;

        /// <summary>Whether the room has other players.</summary>
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

        // ======================== Entry ========================
        // The window opens this page once one of these returns true.

        /// <summary>
        /// Starts opening a room, reusing the invite code of an earlier one when <paramref name="code"/> is given.
        /// Returns whether the page has something to show: the new room, or the one already active.
        /// </summary>
        internal async Task<bool> CreateAsync(RoomCode code = null)
        {
            if (_session.IsActive)
                return true;
            if (_preparing || !await PrepareAsync())
                return false;
            _enteringCode = false;
            _lastRole = LinkRole.Host;
            _lastManualPort = null;
            _lastHostCode = code;
            ResetManualPort();
            _session.StartHost(null, code);
            return true;
        }

        /// <summary>Shows the invite code entry, or the room already active.</summary>
        internal void ShowJoin()
        {
            if (_session.IsActive)
                return;
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

        /// <summary>Joins <paramref name="code"/> right away. Returns whether the page has something to show.</summary>
        /// <param name="lobbyRoomId">The lobby room the code came from, or <c>null</c>.</param>
        internal async Task<bool> JoinAsync(RoomCode code, string lobbyRoomId = null)
        {
            if (_session.IsActive)
                return true;
            if (_preparing || !await PrepareAsync())
                return false;
            TxtCode.Text = code.Code;
            _lobbyRoomId = lobbyRoomId;
            StartJoin(code);
            return true;
        }

        /// <summary>The page was closed by the user; a half-typed invite code is not kept open.</summary>
        internal void Dismiss()
        {
            if (_session.Stage == LinkStage.Idle)
                _enteringCode = false;
            _open = _session.Stage != LinkStage.Idle || _enteringCode;
        }

        /// <summary>The questions asked before entering any room. Returns whether to go on.</summary>
        private async Task<bool> PrepareAsync()
        {
            _preparing = true;
            try
            {
                return await RequirePlayerNameAsync() && await CheckGameNameAsync();
            }
            finally
            {
                _preparing = false;
            }
        }

        /// <summary>Reloads values that may have been changed on the settings page.</summary>
        internal void ReloadSettings()
        {
            WarmUpNameCheck();
            _playersSignature = null;
            Render();
        }

        /// <summary>Checks the player name in the background.</summary>
        private static void WarmUpNameCheck()
        {
            _ = LobbyClient.IsNameAllowedAsync(LinkSettings.PlayerName);
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
                    // Without a room the page keeps its last look while the window leaves it.
                    if (_enteringCode)
                        panel = PanJoin;
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

            bool wasOpen = _open;
            _open = _session.Stage != LinkStage.Idle || _enteringCode;
            if (wasOpen && !_open)
                Left?.Invoke(this, EventArgs.Empty);
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
            RenderBackup();
            RenderFavorite();
            RenderLobby();
            RenderModSync();

            CardHostPlayers.Header = Text.Format("Link.Players", _session.Players.Count);
            RenderPlayers(ListHostPlayers);
        }

        /// <summary>The history entry of the room that is open now; it exists once the room is ready.</summary>
        private RoomRecord CurrentRecord()
        {
            string code = _session.Code?.Code;
            bool host = _session.Role == LinkRole.Host;
            return code == null ? null : RoomHistory.GetAll().FirstOrDefault(r => r.Code == code && r.IsHost == host);
        }

        private void RenderFavorite()
        {
            var record = CurrentRecord();
            var button = _session.Role == LinkRole.Host ? BtnFavoriteHost : BtnFavoriteGuest;
            button.IsEnabled = record != null;
            button.Content = Text.Get(record != null && record.IsFavorite ? "Recent.Favorited" : "Recent.FavoriteRoom");
            button.Style = (Style)FindResource(record != null && record.IsFavorite ? "PclButtonHighlight" : "PclButton");
        }

        private void BtnFavorite_Click(object sender, RoutedEventArgs e)
        {
            var record = CurrentRecord();
            if (record != null)
                RoomHistory.SetFavorite(record, !record.IsFavorite);
        }

        private void RenderBackup()
        {
            var backup = _session.Backup;
            bool shown = backup.State == BackupState.Working || backup.State == BackupState.Done || backup.State == BackupState.Failed;
            PanBackup.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown)
                return;
            LabBackup.Text = backup.State == BackupState.Working ? Text.Get("Backup.Working")
                : backup.State == BackupState.Done ? Text.Format("Backup.Done", backup.WorldName)
                : Text.Get("Backup.Failed");
            BtnOpenBackup.Visibility = backup.State == BackupState.Done ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnOpenBackup_Click(object sender, RoutedEventArgs e)
        {
            string path = _session.Backup.Path;
            try
            {
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + WorldBackup.Directory + "\"");
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.ComponentModel.Win32Exception)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("Backup.Open"), Text.Format("Settings.ActionFailed", ex.Message));
            }
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
            RenderFavorite();
            RenderMods();
        }

        // ======================== Mods ========================

        /// <summary>Shows how the mods compare once the host's list arrives; the check runs once per list.</summary>
        private void RenderMods()
        {
            var host = _session.HostMods;
            if (host == null)
            {
                _modCheck = null;
                _checkedHost = null;
            }
            else if (host != _checkedHost)
            {
                StartModCheck(host);
            }

            // The button appears once the comparison is ready; its tooltip tells the result.
            var check = _session.ModsState == HostModsState.Ready && _modCheck?.Host == host ? _modCheck : null;
            bool shown = check != null && check.HostHasMods;
            bool differs = shown && (check.Version == null || !check.Comparison.IsMatch);
            BtnMods.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown)
                return;

            BtnMods.ToolTip = check.Version == null ? Text.Get("Mods.State.NoVersion")
                : check.Comparison.IsMatch ? Text.Get("Mods.State.Match")
                : Text.Format("Mods.State.Differs", check.Comparison.Changes.Count);
            // The button is highlighted while the mods differ.
            BtnMods.Style = (Style)FindResource(differs ? "PclButtonHighlight" : "PclButton");
            BtnMods.FontWeight = differs ? FontWeights.Bold : FontWeights.Normal;
        }

        /// <summary>Host: the button that lets players sync mods, once the host's own mods are read.</summary>
        private void RenderModSync()
        {
            var own = _session.OwnMods;
            bool shown = own != null && (own.Mods.Any(m => m.IsShared) || (own.Loader != null && own.Loader != Campfire.Worlds.VersionLoader.Vanilla));
            BtnModSync.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown)
                return;
            bool allowed = _session.ModSyncAllowed;
            BtnModSync.Content = Text.Get(allowed ? "Mods.HostStop" : "Mods.HostAllow");
            BtnModSync.ToolTip = Text.Get(allowed ? "Mods.HostStopTip" : "Mods.HostAllowTip");
        }

        private void BtnModSync_Click(object sender, RoutedEventArgs e)
        {
            _session.SetModSync(!_session.ModSyncAllowed);
        }

        private async void StartModCheck(Campfire.Mods.ModList host)
        {
            _checkedHost = host;
            try
            {
                var check = await Campfire.Mods.ModCheck.RunAsync(host, LauncherDirectories?.Invoke() ?? Array.Empty<string>());
                if (_session.HostMods == host)
                    _modCheck = check;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                LinkLog.Warn("The mods could not be compared: " + ex.Message);
            }
            Render();
        }

        /// <summary>Takes the comparison made on the sync page.</summary>
        internal void SetModCheck(Campfire.Mods.ModCheck check)
        {
            if (check == null || check.Host != _session.HostMods)
                return;
            _modCheck = check;
            Render();
        }

        private void BtnMods_Click(object sender, RoutedEventArgs e)
        {
            ModsRequested?.Invoke(this, EventArgs.Empty);
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
                    }));
            }, TaskScheduler.Default);
        }

        private void ShowPanel(UIElement panel)
        {
            if (panel == _shownPanel)
                return;

            // Moving on through the steps slides forward; going back slides back. Out of sight, the part changes at once.
            int direction = Math.Sign(PanelOrder(panel) - PanelOrder(_shownPanel));
            Motion.Swap((FrameworkElement)_shownPanel, (FrameworkElement)panel, direction == 0 ? 1 : direction, IsVisible);
            _shownPanel = panel;
        }

        private int PanelOrder(UIElement panel)
        {
            return panel == PanJoin ? 0 : panel == PanWorking ? 1 : panel == PanPublish ? 3 : 2;
        }

        /// <summary>Shows the failure, with the choice to try again.</summary>
        private async void ShowFailure()
        {
            var host = Dialogs.Of(this);
            var error = _session.Error;
            if (host == null || error == null)
                return;

            // A lobby room nobody can reach is reported.
            if (_session.Role == LinkRole.Guest && _lobbyRoomId != null && _reportedRooms.Add(_lobbyRoomId)
                && (error.TextKey == "Link.Error.RoomNotFound" || error.TextKey == "Link.Error.HostNoAnswer"))
                _ = ReportUnreachableAsync(_lobbyRoomId);

            // When nobody answered in time, the diagnostics take the place of the log link.
            bool timedOut = error.TextKey == "Link.Error.RoomNotFound" || error.TextKey == "Link.Error.NoNodes"
                            || error.TextKey == "Link.Error.NoDirectPath" || error.TextKey == "Link.Error.HostNoAnswer";
            string adviceKey = error.TextKey + ".Advice";
            var choice = await host.ShowDialogAsync(new DialogRequest
            {
                Title = Text.Get(_session.Role == LinkRole.Host ? "Link.CreateFailed" : "Link.JoinFailed"),
                Message = error.LocalizedMessage,
                Detail = Text.Has(adviceKey) ? Text.Get(adviceKey) : null,
                PrimaryText = Text.Get("Link.Retry"),
                SecondaryText = Text.Get("Dialog.Close"),
                LinkText = Text.Get(timedOut ? "Link.OpenDiagnostics" : "Link.OpenLog"),
                LinkAction = timedOut ? null : (Action)OpenLog,
                LinkCloses = timedOut,
                IsError = true,
            });

            // The user may have started something else meanwhile.
            if (_session.Stage != LinkStage.Failed)
                return;
            if (choice == DialogChoice.Primary)
            {
                Retry();
            }
            else
            {
                ReturnToStart();
                if (choice == DialogChoice.Link)
                    DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        // ======================== Checks ========================

        /// <summary>Asks the user to type a player name before entering a room. Returns whether one is set.</summary>
        private async Task<bool> RequirePlayerNameAsync()
        {
            string current = LinkSettings.PlayerName;
            string message = Text.Get("Link.NameRequired");
            bool problem = false;
            if (current.Length > 0)
            {
                if (await LobbyClient.IsNameAllowedAsync(current))
                    return true;
                message = Text.Format("Link.NameBlocked", current);
                problem = true;
            }

            var host = Dialogs.Of(this);
            if (host == null)
                return false;

            // Asked again until an allowed name is typed or the user gives up.
            while (true)
            {
                var request = new DialogRequest
                {
                    Title = Text.Get("Link.NameRequiredTitle"),
                    Message = message,
                    IsError = problem,
                    InputHint = Text.Get("Link.PlayerName"),
                    InputMaxLength = 16,
                    PrimaryText = Text.Get("Dialog.OK"),
                    SecondaryText = Text.Get("Dialog.Cancel"),
                };
                if (await host.ShowDialogAsync(request) != DialogChoice.Primary)
                    return false;

                string name = (request.InputText ?? "").Trim();
                if (name.Length == 0)
                    continue;
                if (!await LobbyClient.IsNameAllowedAsync(name))
                {
                    message = Text.Format("Link.NameBlocked", name);
                    problem = true;
                    continue;
                }
                LinkSettings.PlayerName = name;
                return true;
            }
        }

        /// <summary>
        /// Warns when the running game uses a player name Minecraft rejects. Returns whether to go on.
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
            if (_preparing || !await PrepareAsync())
                return;
            _lobbyRoomId = null;
            StartJoin(code);
        }

        private void StartJoin(RoomCode code)
        {
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
            _session.StartHost(port, _lastHostCode);
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
                _session.StartHost(_lastManualPort, _lastHostCode);
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
            // Before listing, the button says it all; a status line only appears once the room is in the lobby.
            string text = null;
            string brush = null;
            if (lobby?.State == LobbyState.Listed)
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

            // The listing button is highlighted; taking it down is an ordinary button.
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
            // The password is not kept.
            TxtLobbyDescription.Text = LinkSettings.LobbyDescription;
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
                Description = TxtLobbyDescription.Text.Trim(),
                Password = TxtLobbyPassword.Text.Trim(),
                Icon = _lobbyIcon,
            };
            LinkSettings.LobbyTitle = title;
            LinkSettings.LobbyDescription = listing.Description;
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
                    var box = ex.Field == "description" ? TxtLobbyDescription : TxtLobbyTitle;
                    box.Focus();
                    box.SelectAll();
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
            BtnGoLobby.Visibility = LobbyClient.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
            WarmUpNameCheck();
            if (!EasyTierPackage.IsSupportedSystem)
                return;

            _hasIPv6 = await Task.Run(() => NetworkCheck.HasPublicIPv6());
            Render();
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
            UpdateCodeState();
            Render();
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
