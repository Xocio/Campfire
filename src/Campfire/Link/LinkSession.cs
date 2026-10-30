using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link.Lobby;
using Campfire.Link.Minecraft;
using Campfire.Link.Room;
using Campfire.Link.Runtime;
using Campfire.Localization;

namespace Campfire.Link
{
    internal enum LinkRole
    {
        None,
        Host,
        Guest,
    }

    internal enum LinkStage
    {
        Idle,
        Working,
        Ready,
        Failed,
    }

    internal enum LinkStep
    {
        Component,
        World,
        Network,
        FindHost,
        Enter,
    }

    internal enum StepState
    {
        Pending,
        Active,
        Done,
    }

    /// <summary>Guest: whether the host's mod list is known.</summary>
    internal enum HostModsState
    {
        /// <summary>The host does not share it: another program, an older Campfire or a dedicated server.</summary>
        None,
        Loading,
        Ready,

        /// <summary>The host has turned mod sync off; it may turn it on again while the room is open.</summary>
        Off,
    }

    /// <summary>How far the host's world is from being opened to LAN by Campfire itself.</summary>
    internal enum AutoLanState
    {
        /// <summary>Not tried: the setting is off, the port was typed in, or the world is already found.</summary>
        Off,
        /// <summary>No game is running yet.</summary>
        NoGame,
        /// <summary>The game was asked; the world opens once the host is in one.</summary>
        Waiting,
        /// <summary>This game cannot do it; the host opens the world by hand.</summary>
        Unsupported,
    }

    /// <summary>
    /// Creates or joins one room and keeps it running. All members are read and changed on the UI thread;
    /// <see cref="Changed"/> is raised there whenever something visible changes.
    /// </summary>
    internal sealed class LinkSession
    {
        private static readonly TimeSpan DirectGracePeriod = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan ConnectRoomTimeout = TimeSpan.FromSeconds(30);

        private readonly SynchronizationContext _ui;
        private readonly Dictionary<LinkStep, StepState> _steps = new Dictionary<LinkStep, StepState>();

        private CancellationTokenSource _run;
        private EasyTierInstance _network;
        private RoomServer _server;
        private RoomClient _client;
        private LanWorldAnnouncer _announcer;
        private GameProxy _gate;
        private AgentState _agentState;
        private readonly List<LanOpener> _lanOpeners = new List<LanOpener>();
        private readonly Dictionary<string, DateTime> _removedNames = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private volatile string _lanStatus;
        private PlayerProfile _self;
        private LobbyPublisher _lobby;

        /// <summary>Guest: local port forwarded to the host's room service.</summary>
        private int _serviceLocalPort;

        /// <summary>Guest: whether the host answers mod list requests.</summary>
        private bool _hostSharesMods;

        /// <param name="ui">Context of the UI thread; background events are posted to it.</param>
        public LinkSession(SynchronizationContext ui)
        {
            _ui = ui;
        }

        public event EventHandler Changed;

        public LinkRole Role { get; private set; }

        public LinkStage Stage { get; private set; }

        /// <summary>Whether a room is being set up or is open.</summary>
        public bool IsActive => Stage == LinkStage.Working || Stage == LinkStage.Ready;

        public IReadOnlyList<LinkStep> Steps => Role == LinkRole.Host
            ? new[] { LinkStep.Component, LinkStep.World, LinkStep.Network }
            : new[] { LinkStep.Component, LinkStep.Network, LinkStep.FindHost, LinkStep.Enter };

        public DownloadProgress? Download { get; private set; }

        public RoomCode Code { get; private set; }

        /// <summary>Host: the port of the LAN world.</summary>
        public int MinecraftPort { get; private set; }

        /// <summary>Host: whether Campfire is opening the world to LAN itself while the room is set up.</summary>
        public AutoLanState AutoLan { get; private set; }

        /// <summary>Guest: the local port that leads to the host's world.</summary>
        public int LocalPort { get; private set; }

        /// <summary>Guest: whether other devices in the local network can also reach the room.</summary>
        public bool IsSharedWithLan { get; private set; }

        public IReadOnlyList<PlayerProfile> Players { get; private set; } = Array.Empty<PlayerProfile>();

        /// <summary>Guest: the host as seen by the virtual network (latency, direct or relayed).</summary>
        public EasyTierPeer HostPeer { get; private set; }

        /// <summary>Host: devices connected directly and through a relay.</summary>
        public int DirectPeers { get; private set; }

        public int RelayedPeers { get; private set; }

        /// <summary>Connected public nodes, or -1 before the first check.</summary>
        public int ConnectedNodes { get; private set; } = -1;

        public NatKind LocalNat { get; private set; }

        /// <summary>Guest: how long the host has been searched for.</summary>
        public TimeSpan SearchTime { get; private set; }

        public LinkException Error { get; private set; }

        /// <summary>Host: the listing in the public lobby, or <c>null</c> when the room is not listed there.</summary>
        public LobbyPublisher Lobby => _lobby;

        /// <summary>Guest: the host's game version and mods, once <see cref="ModsState"/> is <see cref="HostModsState.Ready"/>.</summary>
        public Mods.ModList HostMods { get; private set; }

        public HostModsState ModsState { get; private set; }

        /// <summary>Host: the copy of the world made when the room opened.</summary>
        public BackupResult Backup { get; private set; } = new BackupResult(BackupState.None);

        public StepState GetStepState(LinkStep step)
        {
            return _steps.TryGetValue(step, out var state) ? state : StepState.Pending;
        }

        // ======================== Host ========================

        /// <summary>Opens a room for the LAN world on this computer.</summary>
        /// <param name="manualPort">The game port typed by the user, or <c>null</c> to detect it.</param>
        /// <param name="reuseCode">The invite code of an earlier room to open again, or <c>null</c> for a new one.</param>
        public async void StartHost(int? manualPort, RoomCode reuseCode = null)
        {
            var run = Begin(LinkRole.Host);
            var token = run.Token;
            try
            {
                EnsureSupportedSystem();

                // The world can be opened while the component downloads; both are waited for together.
                SetStep(LinkStep.World, StepState.Active);
                var world = manualPort.HasValue
                    ? Task.FromResult(manualPort.Value)
                    : LocalWorldFinder.WaitForLocalWorldAsync(token);
                if (!manualPort.HasValue && LinkSettings.AutoOpenLan)
                    _ = OpenWorldToLanAsync(run, world);
                await InstallComponentAsync(token);

                int port = await world;
                StopOpeningLan();
                AutoLan = AutoLanState.Off;
                if (manualPort.HasValue && !await MinecraftProbe.IsServerAsync(port, TimeSpan.FromSeconds(3)))
                    throw new LinkException("Link.Error.PortNotGame", port);
                token.ThrowIfCancellationRequested();
                MinecraftPort = port;
                SetStep(LinkStep.World, StepState.Done);
                LinkLog.Info($"Hosting the LAN world on port {port}.");

                SetStep(LinkStep.Network, StepState.Active);
                Code = reuseCode ?? RoomCode.Create();
                _ = BackupWorldAsync(run, port);
                await GameAccount.RefreshAsync();
                _self = PlayerProfile.CreateLocal(PlayerKind.Host);
                // Guests reach the world through a gate on this computer, which can turn removed players away and
                // adds the room status to their game.
                _gate = GameProxy.Start(new IPEndPoint(IPAddress.Loopback, 0), port, BuildGameOverlay);
                _gate.Refuse = RefuseGamePlayer;
                _server = RoomServer.Start(_self, _gate.Port);
                _server.PlayersChanged += (sender, e) => Post(run, () => Players = _server?.GetPlayers() ?? Players);
                _ = PrepareModsAsync(run, port);

                int gameProcess = GameAccount.Current?.ProcessId ?? 0;
                if (gameProcess > 0)
                {
                    // The hook only speaks while this state is kept fresh.
                    _agentState = new AgentState(gameProcess);
                    _ = AttachGameOverlayAsync(run, gameProcess);
                }
                else
                    LinkLog.Warn("The running game was not found; the in-game overlay agent cannot be attached.");

                var config = CreateConfig(Code, RoomProtocol.HostNamePrefix + _server.Port, RoomProtocol.HostAddress);
                config.TcpWhitelist.Add(_server.Port.ToString());
                config.TcpWhitelist.Add(_gate.Port.ToString());
                config.UdpWhitelist.Add(_gate.Port.ToString());
                await StartNetworkAsync(config, run);

                // Waits up to a few seconds for a node connection.
                for (int i = 0; i < 10 && ConnectedNodes <= 0; i++)
                {
                    ConnectedNodes = await _network.CountConnectedNodesAsync(token);
                    if (ConnectedNodes <= 0)
                        await Task.Delay(1000, token);
                }

                Players = _server.GetPlayers();
                SetStep(LinkStep.Network, StepState.Done);
                Stage = LinkStage.Ready;
                LinkLog.Info($"Room {Mask(Code)} is open ({ConnectedNodes} nodes connected).");
                RoomHistory.Record(true, Code, _self.Name);
                Online.Telemetry.Count("link.host");
                OnChanged();

                await MonitorHostAsync(run);
            }
            catch (Exception ex)
            {
                HandleFailure(run, ex);
            }
        }

        private async Task MonitorHostAsync(CancellationTokenSource run)
        {
            var token = run.Token;
            int tick = 0;
            int missedPings = 0;
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token);
                tick++;

                _server.RemoveSilentGuests();
                var peers = await _network.GetPeersAsync(token);
                var members = peers.Where(p => p.IsMember && !p.IsLocal).ToList();
                DirectPeers = members.Count(p => p.IsDirect);
                RelayedPeers = members.Count - DirectPeers;
                LocalNat = peers.FirstOrDefault(p => p.IsLocal)?.Nat ?? LocalNat;

                if (tick % 3 == 0)
                {
                    ConnectedNodes = await _network.CountConnectedNodesAsync(token);

                    // The room closes once the host leaves the world.
                    if (await MinecraftProbe.IsServerAsync(MinecraftPort, TimeSpan.FromSeconds(4)))
                        missedPings = 0;
                    else if (++missedPings >= 3)
                        throw new LinkException("Link.Error.WorldClosed");
                }
                OnChanged();
            }
        }

        // ======================== Guest ========================

        /// <summary>Joins the room behind <paramref name="code"/>.</summary>
        public async void StartJoin(RoomCode code)
        {
            var run = Begin(LinkRole.Guest);
            var token = run.Token;
            Code = code;
            try
            {
                EnsureSupportedSystem();
                await InstallComponentAsync(token);

                SetStep(LinkStep.Network, StepState.Active);
                await GameAccount.RefreshAsync();
                _self = PlayerProfile.CreateLocal(PlayerKind.Local);
                var config = CreateConfig(code, "campfire-" + Hex.Random(4), null);
                config.TcpWhitelist.Add("0");
                config.UdpWhitelist.Add("0");
                await StartNetworkAsync(config, run);
                SetStep(LinkStep.Network, StepState.Done);

                SetStep(LinkStep.FindHost, StepState.Active);
                var host = await FindHostAsync(config.DirectOnly, config.HideAddress, token);
                var hostAddress = IPAddress.Parse(host.IPv4);
                int servicePort = int.Parse(host.Hostname.Substring(RoomProtocol.HostNamePrefix.Length));
                SetStep(LinkStep.FindHost, StepState.Done);

                SetStep(LinkStep.Enter, StepState.Active);
                _serviceLocalPort = EasyTierInstance.FindFreePort();
                if (!await _network.AddPortForwardAsync("tcp", new IPEndPoint(IPAddress.Loopback, _serviceLocalPort),
                        new IPEndPoint(hostAddress, servicePort), token))
                    throw new LinkException("Link.Error.ForwardFailed");

                _client = await ConnectRoomAsync(_serviceLocalPort, token);
                int hostPort = await _client.GetServerPortAsync(token);
                // Shared with the LAN, the port listens on every interface; otherwise only on this computer.
                IsSharedWithLan = LinkSettings.ShareWithLan;
                var bindAddress = IsSharedWithLan ? IPAddress.Any : IPAddress.Loopback;
                int localPort = ChooseLocalPort(hostPort, bindAddress);
                var target = new IPEndPoint(hostAddress, hostPort);

                if (!await _network.AddPortForwardAsync("tcp", new IPEndPoint(bindAddress, localPort), target, token)
                    || !await _network.AddPortForwardAsync("udp", new IPEndPoint(bindAddress, localPort), target, token))
                    throw new LinkException("Link.Error.ForwardFailed");
                if (IsSharedWithLan)
                    await _network.AddPortForwardAsync("tcp", new IPEndPoint(IPAddress.IPv6Any, localPort), target, token);
                LocalPort = localPort;
                LinkLog.Info($"Host game port {hostPort} is reachable at local port {localPort}.");

                bool reachable = false;
                for (int i = 0; i < 6 && !reachable; i++)
                {
                    reachable = await MinecraftProbe.IsServerAsync(localPort, TimeSpan.FromSeconds(4));
                    if (!reachable)
                        await Task.Delay(1000, token);
                }
                if (!reachable)
                    LinkLog.Warn("The host's game did not answer through the tunnel yet; continuing anyway.");

                _self = _self.WithLatency(HostPeer?.LatencyMs);
                await _client.SendHeartbeatAsync(_self, token);
                Players = MarkLocal(await _client.GetPlayersAsync(token));
                UpdateLanStatus();
                // The room always appears in this computer's LAN list: an offline account can only enter from there.
                _announcer = new LanWorldAnnouncer(LanEntryWithStatus, localPort, !IsSharedWithLan);

                SetStep(LinkStep.Enter, StepState.Done);
                Stage = LinkStage.Ready;
                LinkLog.Info($"Joined room {Mask(Code)}.");
                var hostPlayer = Players.FirstOrDefault(p => p.Kind == PlayerKind.Host);
                RoomHistory.Record(false, Code, hostPlayer?.Name);
                Online.Telemetry.Count("link.join");
                OnChanged();
                _ = FetchHostModsAsync(run);

                await MonitorGuestAsync(run);
            }
            catch (Exception ex)
            {
                HandleFailure(run, ex);
            }
        }

        private async Task<EasyTierPeer> FindHostAsync(bool directOnly, bool hideAddress, CancellationToken token)
        {
            var started = DateTime.UtcNow;
            DateTime? firstSeen = null;
            while (true)
            {
                SearchTime = DateTime.UtcNow - started;
                var peers = await _network.GetPeersAsync(token);
                LocalNat = peers.FirstOrDefault(p => p.IsLocal)?.Nat ?? LocalNat;

                var host = peers.FirstOrDefault(IsRoomHost);
                if (host != null)
                {
                    HostPeer = host;
                    firstSeen = firstSeen ?? DateTime.UtcNow;
                    // A direct path is preferred; a hidden address takes the relay at once.
                    if (host.IsDirect || hideAddress || (!directOnly && DateTime.UtcNow - firstSeen > DirectGracePeriod))
                    {
                        LinkLog.Info($"Found host {host.Hostname} ({(host.IsDirect ? "direct" : "relayed")}, {host.Tunnel}).");
                        return host;
                    }
                }

                if (SearchTime.TotalSeconds > LinkSettings.SearchTimeoutSeconds)
                {
                    if (host != null)
                        throw new LinkException("Link.Error.NoDirectPath");
                    if (ConnectedNodes == 0)
                        throw new LinkException("Link.Error.NoNodes");
                    throw new LinkException("Link.Error.RoomNotFound");
                }

                if (SearchTime.TotalSeconds > 10 && (int)SearchTime.TotalSeconds % 5 < 2)
                    ConnectedNodes = await _network.CountConnectedNodesAsync(token);
                OnChanged();
                await Task.Delay(1500, token);
            }
        }

        /// <summary>The host puts the port of its room service at the end of its name.</summary>
        private static bool IsRoomHost(EasyTierPeer peer)
        {
            return !peer.IsLocal && peer.IsMember
                && peer.Hostname.StartsWith(RoomProtocol.HostNamePrefix, StringComparison.Ordinal)
                && int.TryParse(peer.Hostname.Substring(RoomProtocol.HostNamePrefix.Length), out int port) && port > 0 && port < 65536;
        }

        private static async Task<RoomClient> ConnectRoomAsync(int localPort, CancellationToken token)
        {
            var deadline = DateTime.UtcNow + ConnectRoomTimeout;
            while (DateTime.UtcNow < deadline)
            {
                var client = await RoomClient.TryConnectAsync(localPort, token);
                if (client != null)
                    return client;
                await Task.Delay(2000, token);
            }
            throw new LinkException("Link.Error.HostNoAnswer");
        }

        private async Task MonitorGuestAsync(CancellationTokenSource run)
        {
            var token = run.Token;
            int tick = 0;
            int failures = 0;
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token);
                tick++;

                var peers = await _network.GetPeersAsync(token);
                LocalNat = peers.FirstOrDefault(p => p.IsLocal)?.Nat ?? LocalNat;
                var host = peers.FirstOrDefault(IsRoomHost);
                if (host != null)
                    HostPeer = host;

                // Players often start the game after joining; pick up its name and skin once it runs.
                if (tick % 5 == 0)
                {
                    await GameAccount.RefreshAsync();
                    var refreshed = PlayerProfile.CreateLocal(PlayerKind.Local);
                    if (refreshed.Name != _self.Name || refreshed.Uuid != _self.Uuid || refreshed.GameName != _self.GameName)
                        _self = refreshed;
                }

                // Heartbeat every few seconds; the host drops players silent for 10 seconds.
                if (tick % 2 == 0)
                {
                    try
                    {
                        _self = _self.WithLatency(HostPeer?.LatencyMs);
                        await _client.SendHeartbeatAsync(_self, token);
                        var players = MarkLocal(await _client.GetPlayersAsync(token));
                        Players = players;
                        failures = 0;
                    }
                    catch (Exception ex) when (RoomClient.IsConnectionError(ex) && !token.IsCancellationRequested)
                    {
                        LinkLog.Warn("Heartbeat failed: " + ex.Message);
                        if (++failures >= 2)
                            throw new LinkException("Link.Error.HostLost");
                        _client.Dispose();
                        _client = await RoomClient.TryConnectAsync(_serviceLocalPort, token)
                                  ?? throw new LinkException("Link.Error.HostLost");
                    }
                }

                // The host may turn mod sync on or off, or reopen the world with other mods.
                if (_hostSharesMods && tick % 5 == 1)
                {
                    try
                    {
                        await ReadHostModsAsync(token);
                    }
                    catch (Exception ex) when (RoomClient.IsConnectionError(ex) && !token.IsCancellationRequested)
                    {
                        // A lost host is noticed by the heartbeat.
                    }
                }
                UpdateLanStatus();
                OnChanged();
            }
        }

        // ======================== Control ========================

        /// <summary>
        /// Takes a guest out of the room and out of the world. A removed player may come back after a minute; a
        /// blocked one is turned away from every room this computer opens until unblocked in the settings.
        /// </summary>
        public void RemovePlayer(PlayerProfile player, bool block)
        {
            if (Role != LinkRole.Host || _server == null || player.Kind == PlayerKind.Host)
                return;

            if (block)
                Blocklist.Add(player);
            _server.Remove(player.MachineId);

            var names = new[] { player.Name, player.GameName }.Where(n => !string.IsNullOrEmpty(n)).ToList();
            lock (_removedNames)
            {
                foreach (string name in names)
                    _removedNames[name] = DateTime.UtcNow + RoomServer.RemovalTime;
            }
            int closed = _gate?.Disconnect(name => names.Contains(name, StringComparer.OrdinalIgnoreCase),
                Text.Get(block ? "Link.Game.BlockedKick" : "Link.Game.RemovedKick")) ?? 0;
            LinkLog.Info((block ? "Blocked" : "Removed") + $" a player; {closed} game connection(s) closed.");

            Players = _server.GetPlayers();
            OnChanged();
        }

        /// <summary>Tells the gate whether a player name may enter the world; called on a worker thread.</summary>
        private string RefuseGamePlayer(string name)
        {
            if (Blocklist.ContainsName(name))
                return Text.Get("Link.Game.BlockedKick");
            lock (_removedNames)
            {
                if (_removedNames.TryGetValue(name, out var until) && until > DateTime.UtcNow)
                    return Text.Get("Link.Game.RemovedKick");
            }
            return null;
        }

        /// <summary>
        /// The room as seen by a player inside the game; called on a worker thread for every joining connection.
        /// </summary>
        private GameOverlayInfo BuildGameOverlay(string playerName)
        {
            var players = _server?.GetPlayers()
                          ?? (_self == null ? Array.Empty<PlayerProfile>() : new[] { _self });
            var host = players.FirstOrDefault(p => p.Kind == PlayerKind.Host) ?? _self;
            var guest = players.FirstOrDefault(p => p != null
                && ((!string.IsNullOrEmpty(p.GameName) && string.Equals(p.GameName, playerName, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase)));

            return new GameOverlayInfo(host?.GameName ?? host?.Name, players.Count, guest?.LatencyMs);
        }


        // ======================== Backup ========================

        /// <summary>Host: copies the world in the background; the room does not wait for it.</summary>
        private async Task BackupWorldAsync(CancellationTokenSource run, int gamePort)
        {
            if (!LinkSettings.BackupWorld)
                return;

            Backup = new BackupResult(BackupState.Working);
            OnChanged();
            BackupResult result;
            try
            {
                result = await Task.Run(() => WorldBackup.Run(gamePort, run.Token));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LinkLog.Warn("The world could not be backed up: " + ex.Message);
                result = new BackupResult(BackupState.Failed);
            }
            if (run != _run)
                return;
            Backup = result;
            OnChanged();
        }

        // ======================== Mods ========================

        /// <summary>Host: reads the mods of the game running the world.</summary>
        private async Task PrepareModsAsync(CancellationTokenSource run, int gamePort)
        {
            Mods.ModHost mods = null;
            try
            {
                mods = await Mods.ModHost.PrepareAsync(gamePort, LinkSettings.ServeMods);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException
                                       || ex is ArgumentException)
            {
                LinkLog.Warn("The host's mods could not be read: " + ex.Message);
            }
            if (run != _run || _server == null)
                return;
            if (mods != null)
                _server.Mods = mods;
            else
                _server.ModsUnavailable = true;
            OnChanged();
        }

        /// <summary>Host: the mods of the game running the world, once read.</summary>
        public Mods.ModList OwnMods => _server?.Mods?.List;

        /// <summary>Host: whether players may compare and sync mods with this computer.</summary>
        public bool ModSyncAllowed => _server?.ModsAllowed ?? LinkSettings.ShareMods;

        /// <summary>Host: lets players sync mods or stops them, at once and for later rooms too.</summary>
        public void SetModSync(bool allowed)
        {
            LinkSettings.ShareMods = allowed;
            if (_server != null)
                _server.ModsAllowed = allowed;
            LinkLog.Info("Mod sync " + (allowed ? "allowed" : "stopped") + " by the host.");
            OnChanged();
        }

        /// <summary>Guest: asks the host for its mods once the room is joined; hosts that do not share them are left alone.</summary>
        private async Task FetchHostModsAsync(CancellationTokenSource run)
        {
            var token = run.Token;
            try
            {
                var protocols = await _client.GetProtocolsAsync(token);
                if (!protocols.Contains(RoomProtocol.ModList) || run != _run)
                    return;
                ModsState = HostModsState.Loading;
                OnChanged();

                // The host reads its mods while the room opens; a large folder can take a moment.
                for (int attempt = 0; attempt < 20 && run == _run; attempt++)
                {
                    if (await ReadHostModsAsync(token))
                    {
                        // From now on the list is checked again now and then.
                        _hostSharesMods = run == _run;
                        return;
                    }
                    await Task.Delay(3000, token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (RoomClient.IsConnectionError(ex))
            {
                LinkLog.Warn("The host's mod list could not be read: " + ex.Message);
            }
            if (run == _run)
            {
                ModsState = HostModsState.None;
                OnChanged();
            }
        }

        /// <summary>Guest: asks the host for its mod list once. Returns <c>false</c> while the host is still reading it.</summary>
        private async Task<bool> ReadHostModsAsync(CancellationToken token)
        {
            var (list, off) = await _client.GetModListAsync(token);
            if (off)
            {
                if (ModsState != HostModsState.Off)
                {
                    ModsState = HostModsState.Off;
                    OnChanged();
                }
                return true;
            }
            if (list == null)
                return false;

            bool changed = ModsState != HostModsState.Ready;
            if (HostMods == null || HostMods.Signature != list.Signature)
            {
                // The list object is only replaced when something differs.
                HostMods = list;
                changed = true;
                LinkLog.Info($"The host plays {list.GameVersion ?? "?"} {list.Loader?.ToString() ?? "?"} with {list.Mods.Count} mod(s).");
            }
            ModsState = HostModsState.Ready;
            if (changed)
                OnChanged();
            return true;
        }

        /// <summary>
        /// Guest: a separate connection to the host's room service for downloading mods. Returns <c>null</c> when the room is gone.
        /// </summary>
        public Task<RoomClient> ConnectForModsAsync(CancellationToken cancellation)
        {
            if (Role != LinkRole.Guest || Stage != LinkStage.Ready || _serviceLocalPort == 0)
                return Task.FromResult<RoomClient>(null);
            return RoomClient.TryConnectAsync(_serviceLocalPort, cancellation);
        }

        // ======================== Lobby ========================

        /// <summary>
        /// Host: keeps the room listed in the public lobby until it closes or <see cref="Unpublish"/> is called.
        /// Returns <c>false</c> when the room it was listed for is no longer open; the caller then takes the listing down.
        /// </summary>
        /// <param name="ticket">The listing just made for <see cref="LobbyListing.Code"/>.</param>
        public bool Publish(LobbyListing listing, LobbyTicket ticket)
        {
            if (Role != LinkRole.Host || Stage != LinkStage.Ready || Code?.Code != listing.Code?.Code)
                return false;
            _lobby?.Dispose();
            _lobby = new LobbyPublisher(listing, ticket, () => Players.Count, _ui);
            _lobby.Changed += (sender, e) => OnChanged();
            _lobby.Start();
            OnChanged();
            return true;
        }

        /// <summary>Host: takes the room out of the public lobby; the room itself stays open.</summary>
        public void Unpublish()
        {
            if (_lobby == null)
                return;
            _lobby.Dispose();
            _lobby = null;
            OnChanged();
        }

        /// <summary>Leaves or closes the room, or cancels setting it up.</summary>
        public void Stop()
        {
            if (Stage == LinkStage.Idle)
                return;
            LinkLog.Info("Room stopped by the user.");
            Release();
            Role = LinkRole.None;
            Stage = LinkStage.Idle;
            Error = null;
            OnChanged();
        }

        /// <summary>Ends everything immediately; used when Campfire exits.</summary>
        public void Shutdown()
        {
            // Closes the lobby listing, waiting up to two seconds.
            var closing = _lobby?.CloseAsync();
            Release();
            closing?.Wait(TimeSpan.FromSeconds(2));
        }

        // ======================== Helpers ========================

        private CancellationTokenSource Begin(LinkRole role)
        {
            Release();
            _run = new CancellationTokenSource();
            _steps.Clear();
            Role = role;
            Stage = LinkStage.Working;
            Error = null;
            Download = null;
            Code = null;
            MinecraftPort = 0;
            AutoLan = AutoLanState.Off;
            LocalPort = 0;
            IsSharedWithLan = false;
            Players = Array.Empty<PlayerProfile>();
            HostPeer = null;
            DirectPeers = 0;
            RelayedPeers = 0;
            ConnectedNodes = -1;
            LocalNat = NatKind.Unknown;
            SearchTime = TimeSpan.Zero;
            HostMods = null;
            ModsState = HostModsState.None;
            Backup = new BackupResult(BackupState.None);
            _hostSharesMods = false;
            _serviceLocalPort = 0;
            OnChanged();
            return _run;
        }

        private void Release()
        {
            _run?.Cancel();
            _run = null;
            _lobby?.Dispose();
            _lobby = null;
            _announcer?.Dispose();
            _announcer = null;
            _gate?.Dispose();
            _gate = null;
            _agentState?.Dispose();
            _agentState = null;
            StopOpeningLan();
            lock (_removedNames)
                _removedNames.Clear();
            _lanStatus = null;
            _client?.Dispose();
            _client = null;
            _server?.Dispose();
            _server = null;
            _network?.Dispose();
            _network = null;
        }

        private void HandleFailure(CancellationTokenSource run, Exception ex)
        {
            // A stopped or replaced run ends quietly.
            if (run != _run || run.IsCancellationRequested)
                return;

            if (ex is RoomRejectedException rejected)
                ex = new LinkException(rejected.Blocked ? "Link.Error.Blocked" : "Link.Error.Removed");
            Error = ex as LinkException ?? new LinkException(ex, "Link.Error.Unexpected", ex.Message);
            LinkLog.Error("Room failed: " + Error.Message, ex is LinkException ? ex.InnerException : ex);
            if (_network != null)
                LinkLog.Info("Recent network output:" + Environment.NewLine + _network.GetRecentOutput());
            Release();
            Stage = LinkStage.Failed;
            OnChanged();
        }

        /// <summary>
        /// Adds the room status from inside the host's server. Once the hook confirms it runs, the proxy overlay is turned off.
        /// </summary>
        private async Task AttachGameOverlayAsync(CancellationTokenSource run, int processId)
        {
            try
            {
                bool attached = await JavaAgent.AttachAsync(processId, run.Token);
                if (!attached || run != _run || run.IsCancellationRequested || _gate == null)
                    return;

                _gate.OverlayEnabled = false;
                LinkLog.Info("In-game overlay is now provided by the game agent.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is InvalidOperationException || ex is NotSupportedException)
            {
                LinkLog.Warn("The in-game overlay agent could not be started: " + ex.Message);
            }
            catch (Exception ex)
            {
                LinkLog.Warn("The in-game overlay agent could not be started: " + ex.Message);
            }
        }

        /// <summary>
        /// While the room waits for a LAN world, asks each running game to open the world the host plays to LAN,
        /// the way the game's own button does. Opening it by hand still works; whichever happens first counts.
        /// </summary>
        private async Task OpenWorldToLanAsync(CancellationTokenSource run, Task world)
        {
            var token = run.Token;
            var asked = new HashSet<int>();
            var answered = new HashSet<int>();
            SetAutoLan(AutoLanState.NoGame);
            try
            {
                while (!world.IsCompleted && run == _run && !token.IsCancellationRequested)
                {
                    var games = await Task.Run(() => Worlds.RunningGame.Find(), token);
                    if (run != _run || world.IsCompleted)
                        return;

                    // A game that was closed stops waiting; one started later is asked as soon as it appears.
                    var running = new HashSet<int>(games.Select(g => g.ProcessId));
                    foreach (var gone in Openers().Where(o => !running.Contains(o.ProcessId)))
                        RemoveOpener(gone);

                    foreach (var game in games.Where(g => asked.Add(g.ProcessId)))
                    {
                        var opener = new LanOpener(game.ProcessId, null);
                        lock (_lanOpeners)
                            _lanOpeners.Add(opener);
                        LinkLog.Info($"Asking game {game.ProcessId} to open its world to LAN.");
                        if (!await JavaAgent.LoadAsync(game.ProcessId, token))
                        {
                            LinkLog.Warn($"Game {game.ProcessId} cannot open its world to LAN by itself.");
                            RemoveOpener(opener);
                        }
                        if (run != _run)
                            return;
                    }

                    foreach (var opener in Openers())
                    {
                        int? result = opener.ReadResult();
                        if (result == -1)
                        {
                            LinkLog.Warn($"Game {opener.ProcessId} could not open its world to LAN.");
                            RemoveOpener(opener);
                        }
                        else if (result != null && answered.Add(opener.ProcessId))
                        {
                            // The world is open; finding it on the port takes a moment.
                            LinkLog.Info(result > 0
                                ? $"Game {opener.ProcessId} opened its world to LAN on port {result}."
                                : $"The world of game {opener.ProcessId} was already open to LAN.");
                        }
                    }

                    SetAutoLan(asked.Count == 0 || running.Count == 0 ? AutoLanState.NoGame
                        : Openers().Count > 0 ? AutoLanState.Waiting : AutoLanState.Unsupported);
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LinkLog.Warn("Opening the world to LAN failed: " + ex.Message);
                if (run == _run)
                    SetAutoLan(AutoLanState.Unsupported);
            }

            void SetAutoLan(AutoLanState state)
            {
                if (run != _run || world.IsCompleted || AutoLan == state)
                    return;
                AutoLan = state;
                OnChanged();
            }
        }

        private List<LanOpener> Openers()
        {
            lock (_lanOpeners)
                return _lanOpeners.ToList();
        }

        private void RemoveOpener(LanOpener opener)
        {
            lock (_lanOpeners)
                _lanOpeners.Remove(opener);
            opener.Dispose();
        }

        /// <summary>Withdraws every request to open a world to LAN.</summary>
        private void StopOpeningLan()
        {
            foreach (var opener in Openers())
                RemoveOpener(opener);
        }

        private async Task InstallComponentAsync(CancellationToken token)
        {
            SetStep(LinkStep.Component, StepState.Active);
            int shownPercent = -1;
            var progress = new Progress<DownloadProgress>(p =>
            {
                Download = p;
                int percent = (int)(p.Fraction * 100);
                if (percent != shownPercent)
                {
                    shownPercent = percent;
                    OnChanged();
                }
            });
            await EasyTierPackage.InstallAsync(progress, token);
            Download = null;
            // Windows 7 can lack system functions the component needs; find out before going any further.
            if (EasyTierPackage.IsWindows7 && !await EasyTierPackage.CanRunAsync())
                throw new LinkException("Link.Error.Windows7Updates");
            SetStep(LinkStep.Component, StepState.Done);
        }

        private async Task StartNetworkAsync(EasyTierConfig config, CancellationTokenSource run)
        {
            _network = await Task.Run(() => EasyTierInstance.Start(config), run.Token);
            var network = _network;
            network.Exited += (sender, e) => Post(run, () =>
            {
                if (_network == network)
                    HandleFailure(run, new LinkException("Link.Error.NetworkCrashed"));
            });
        }

        private static EasyTierConfig CreateConfig(RoomCode code, string hostname, string address)
        {
            var config = new EasyTierConfig
            {
                NetworkName = code.NetworkName,
                NetworkSecret = code.NetworkSecret,
                Hostname = hostname,
                IPv4 = address,
                DirectOnly = !LinkSettings.AllowRelay,
                HideAddress = LinkSettings.HideAddress,
            };
            config.Peers.AddRange(LinkSettings.GetNodes());
            return config;
        }

        private IReadOnlyList<PlayerProfile> MarkLocal(IReadOnlyList<PlayerProfile> players)
        {
            var list = players
                .Select(p => p.MachineId == _self.MachineId ? p.WithKind(PlayerKind.Local) : p)
                .ToList();
            if (list.All(p => p.Kind != PlayerKind.Local))
                list.Add(_self);
            return list.OrderBy(p => p.Kind == PlayerKind.Host ? 0 : 1).ToList();
        }

        /// <summary>
        /// Uses the port set by the user if any. Otherwise prefers the host's port number, and falls back to any free port.
        /// </summary>
        private static int ChooseLocalPort(int hostPort, IPAddress bindAddress)
        {
            int configured = LinkSettings.LocalPort;
            if (configured > 0)
            {
                if (!IsPortFree(configured, bindAddress))
                    throw new LinkException("Link.Error.LocalPortBusy", configured);
                return configured;
            }

            if (IsPortFree(hostPort, bindAddress))
                return hostPort;
            for (int i = 0; i < 20; i++)
            {
                int port = EasyTierInstance.FindFreePort();
                if (IsPortFree(port, bindAddress))
                    return port;
            }
            return EasyTierInstance.FindFreePort();
        }

        /// <summary>
        /// Checks the system's listener tables.
        /// </summary>
        private static bool IsPortFree(int port, IPAddress bindAddress)
        {
            try
            {
                var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                bool Clashes(IPEndPoint used) => used.Port == port
                    && (IPAddress.Any.Equals(bindAddress) || IPAddress.Any.Equals(used.Address)
                        || IPAddress.IPv6Any.Equals(used.Address) || used.Address.Equals(bindAddress));
                return !properties.GetActiveTcpListeners().Any(Clashes) && !properties.GetActiveUdpListeners().Any(Clashes);
            }
            catch (System.Net.NetworkInformation.NetworkInformationException)
            {
                return false;
            }
        }

        /// <summary>Text of the room entry in the multiplayer list.</summary>
        private static string LanEntryText()
        {
            string custom = LinkSettings.LanEntryName;
            return custom.Length > 0 ? custom : Text.Get("Link.LanEntry");
        }

        /// <summary>The entry text followed by the live room status; read from the announcer's thread.</summary>
        private string LanEntryWithStatus()
        {
            string status = _lanStatus;
            // One line only: the multiplayer list draws a line break as a glyph and overflows the row.
            return status == null ? LanEntryText() : LanEntryText() + " §8· §7" + status;
        }

        private void UpdateLanStatus()
        {
            var host = HostPeer;
            string status = host?.LatencyMs == null
                ? Text.Format("Link.Game.ListStatus", Players.Count)
                : Text.Format("Link.Game.ListStatusLatency", Players.Count, FormatLatency(host.LatencyMs.Value));

            // Show the host name before entering the world too: the encrypted LAN connection keeps the
            // in-game overlay out, but the LAN entry is always plain broadcast text.
            var hostPlayer = Players.FirstOrDefault(p => p.Kind == PlayerKind.Host);
            string hostName = hostPlayer?.GameName ?? hostPlayer?.Name;
            _lanStatus = string.IsNullOrWhiteSpace(hostName) ? status : hostName + " · " + status;
        }

        internal static string FormatLatency(double latency)
        {
            return latency.ToString(latency < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Keeps invite codes out of the log: only the first group is written.</summary>
        internal static string Mask(RoomCode code)
        {
            return code == null ? "-" : code.Code.Substring(0, 6) + "-****-****-****";
        }

        private static void EnsureSupportedSystem()
        {
            if (!EasyTierPackage.IsSupportedSystem)
                throw new LinkException("Link.Error.SystemTooOld");
            if (EasyTierPackage.IsWindows7 && !EasyTierPackage.HasLoaderUpdate)
                throw new LinkException("Link.Error.Windows7Updates");
        }

        private void SetStep(LinkStep step, StepState state)
        {
            _steps[step] = state;
            OnChanged();
        }

        private void Post(CancellationTokenSource run, Action action)
        {
            _ui.Post(_ =>
            {
                if (run == _run && !run.IsCancellationRequested)
                {
                    action();
                    OnChanged();
                }
            }, null);
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
