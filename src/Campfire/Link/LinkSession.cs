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

    /// <summary>Host: how far the voice chat of the room is.</summary>
    internal enum HostVoiceState
    {
        /// <summary>The host has voice chat off.</summary>
        Off,

        /// <summary>Looking for the voice chat mod in the host's game.</summary>
        Checking,

        /// <summary>The host's game lacks the voice chat mod; it is installed once the host asks for it.</summary>
        Missing,

        /// <summary>Downloading the voice chat mod into the host's game.</summary>
        Installing,

        /// <summary>The mod is in the host's game, and its voice server starts once the game is started again.</summary>
        Restart,

        Ready,

        /// <summary>Voice chat cannot run for this world; see <see cref="LinkSession.VoiceProblem"/>.</summary>
        Unavailable,
    }

    /// <summary>Guest: the host's voice chat as seen from this computer.</summary>
    internal enum GuestVoiceState
    {
        /// <summary>The host has voice chat off, or its Campfire does not offer it.</summary>
        Off,

        /// <summary>The host turned voice chat on; its voice server is not running yet.</summary>
        Waiting,

        Ready,

        /// <summary>The voice port is in use on this computer.</summary>
        PortBusy,
    }

    /// <summary>
    /// Creates or joins one room and keeps it running. All members are read and changed on the UI thread;
    /// <see cref="Changed"/> is raised there whenever something visible changes.
    /// </summary>
    internal sealed class LinkSession
    {
        private static readonly TimeSpan DirectGracePeriod = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan ConnectRoomTimeout = TimeSpan.FromSeconds(30);

        /// <summary>How long a room waits for the host's world while the game restarts.</summary>
        private static readonly TimeSpan RestartWait = TimeSpan.FromMinutes(10);

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

        /// <summary>Host: counts voice chat preparations; an older one stops once a newer one starts.</summary>
        private int _voiceSerial;

        /// <summary>Host: the game running the world, its version and voice chat jar, once found.</summary>
        private Worlds.RunningGame _voiceGame;
        private Worlds.InstalledVersion _voiceVersion;
        private Mods.ModInfo _voiceMod;

        /// <summary>Host: the UDP port of the game's voice server once found, and the one open to the room or 0.</summary>
        private int _voicePort;
        private int _openVoicePort;

        /// <summary>Guest: whether the host answers voice requests.</summary>
        private bool _hostOffersVoice;

        /// <summary>Guest: the host's virtual address, and the UDP port forwarded to its voice server or 0.</summary>
        private IPAddress _hostAddress;
        private int _voiceForwardPort;

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

        /// <summary>Host: the voice chat of the room.</summary>
        public HostVoiceState Voice { get; private set; }

        /// <summary>Host: the world closed for a game restart and the room waits for it to be opened to LAN again.</summary>
        public bool WaitingForWorld { get; private set; }

        /// <summary>Host: the text key under "Voice.Problem." while <see cref="Voice"/> is <see cref="HostVoiceState.Unavailable"/>.</summary>
        public string VoiceProblem { get; private set; }

        /// <summary>Guest: the host's voice chat, or <c>null</c> while it is off.</summary>
        public Mods.VoiceOffer HostVoice { get; private set; }

        public GuestVoiceState GuestVoice { get; private set; }

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
                _server.Room = CurrentRoomInfo();
                _server.PlayersChanged += (sender, e) => Post(run, () => Players = _server?.GetPlayers() ?? Players);
                _ = PrepareModsAsync(run, port);

                int gameProcess = await Task.Run(() => WorldGameProcess(port));
                if (gameProcess > 0)
                {
                    // The state the hook reads, rewritten every few seconds.
                    _agentState = new AgentState(gameProcess);
                    _agentState.Notice = LinkSettings.RoomNotice;
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
                _backupCheckedUtc = DateTime.UtcNow;
                Stage = LinkStage.Ready;
                LinkLog.Info($"Room {Mask(Code)} is open ({ConnectedNodes} nodes connected).");
                RoomHistory.Record(true, Code, _self.Name);
                Online.Telemetry.Count("link.host");
                OnChanged();
                if (LinkSettings.Voice)
                    _ = PrepareVoiceAsync(run, false);

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

                    // The room closes once the host leaves the world, unless the game is restarting for voice chat.
                    if (await MinecraftProbe.IsServerAsync(MinecraftPort, TimeSpan.FromSeconds(4)))
                    {
                        missedPings = 0;
                    }
                    else if (++missedPings >= 3)
                    {
                        if (Voice != HostVoiceState.Restart)
                            throw new LinkException("Link.Error.WorldClosed");
                        await ReturnToWorldAsync(run);
                        missedPings = 0;
                        continue;
                    }

                    if (Voice == HostVoiceState.Ready || Voice == HostVoiceState.Restart)
                        await RefreshHostVoiceAsync(run, _voiceSerial);
                }
                ScheduleBackup(run);
                OnChanged();
            }
        }

        /// <summary>
        /// Host: keeps the room open while the game restarts, and takes up the world again once it is open to LAN,
        /// on the same invite code. Gives up after <see cref="RestartWait"/>.
        /// </summary>
        private async Task ReturnToWorldAsync(CancellationTokenSource run)
        {
            var token = run.Token;
            WaitingForWorld = true;
            LinkLog.Info("The world closed for a game restart; the room waits for it to open again.");
            OnChanged();

            // Only a world of the same game folder counts; other games on this computer are left alone.
            string gameDirectory = _voiceGame?.GameDirectory;
            bool SameGame(int candidate) => gameDirectory == null || string.Equals(
                Worlds.RunningGame.FromProcess(LocalWorldFinder.FindProcess(candidate))?.GameDirectory, gameDirectory,
                StringComparison.OrdinalIgnoreCase);

            int port;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                wait.CancelAfter(RestartWait);
                var world = LocalWorldFinder.WaitForLocalWorldAsync(wait.Token, SameGame);
                if (LinkSettings.AutoOpenLan)
                    _ = OpenWorldToLanAsync(run, world, gameDirectory);
                try
                {
                    port = await world;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new LinkException("Link.Error.WorldClosed");
                }
                finally
                {
                    StopOpeningLan();
                    AutoLan = AutoLanState.Off;
                }
            }

            MinecraftPort = port;
            _gate.TargetPort = port;
            WaitingForWorld = false;
            LinkLog.Info($"The world is open again on port {port}.");
            _ = PrepareModsAsync(run, port);

            await GameAccount.RefreshAsync();
            int gameProcess = await Task.Run(() => WorldGameProcess(port));
            if (gameProcess > 0)
            {
                _agentState?.Dispose();
                _agentState = new AgentState(gameProcess);
                _agentState.Notice = LinkSettings.RoomNotice;
                _gate.OverlayEnabled = true;
                _ = AttachGameOverlayAsync(run, gameProcess);
            }
            _voiceGame = null;
            _voicePort = 0;
            _ = PrepareVoiceAsync(run, false);
            OnChanged();
        }

        /// <summary>
        /// The game process that listens on <paramref name="port"/>, or the signed-in game's when that process is not
        /// a game started by a launcher; 0 when neither is found.
        /// </summary>
        private static int WorldGameProcess(int port)
        {
            int owner = LocalWorldFinder.FindProcess(port);
            if (owner > 0 && Worlds.RunningGame.FromProcess(owner) != null)
                return owner;
            return GameAccount.Current?.ProcessId ?? 0;
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
                _hostAddress = hostAddress;
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

                _self = WithHostConnection(_self);
                await _client.SendHeartbeatAsync(_self, token);
                Players = MarkLocal(await _client.GetPlayersAsync(token));
                UpdateLanStatus();
                // The room always appears in this computer's LAN list.
                _announcer = new LanWorldAnnouncer(LanEntryWithStatus, localPort, !IsSharedWithLan);

                SetStep(LinkStep.Enter, StepState.Done);
                Stage = LinkStage.Ready;
                LinkLog.Info($"Joined room {Mask(Code)}.");
                var hostPlayer = Players.FirstOrDefault(p => p.Kind == PlayerKind.Host);
                RoomHistory.Record(false, Code, hostPlayer?.Name);
                Online.Telemetry.Count("link.join");
                OnChanged();
                _ = FetchHostModsAsync(run);
                _ = FetchHostVoiceAsync(run);
                _ = FetchRoomInfoAsync(run);

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

                // Reads the game's player name and skin every ten seconds.
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
                        _self = WithHostConnection(_self);
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

                // Reads the host's mod list every ten seconds.
                if (_hostSharesMods && tick % 5 == 1)
                {
                    try
                    {
                        await ReadHostModsAsync(token);
                    }
                    catch (Exception ex) when (RoomClient.IsConnectionError(ex) && !token.IsCancellationRequested)
                    {
                    }
                }

                // Reads the host's voice chat and room information every ten seconds.
                if (tick % 5 == 3)
                {
                    try
                    {
                        if (_hostOffersVoice)
                            await ReadHostVoiceAsync(token);
                        if (_hostSharesRoom)
                            await ReadRoomInfoAsync(token);
                    }
                    catch (Exception ex) when (RoomClient.IsConnectionError(ex) && !token.IsCancellationRequested)
                    {
                    }
                }
                UpdateLanStatus();
                OnChanged();
            }
        }

        // ======================== Control ========================

        /// <summary>
        /// Takes a guest out of the room and out of the world. A removed player may come back once
        /// <paramref name="duration"/> has passed, or after the room closes when it is <c>null</c>; a blocked one is
        /// turned away from every room this computer opens until unblocked in the settings.
        /// </summary>
        public void RemovePlayer(PlayerProfile player, bool block, TimeSpan? duration = null)
        {
            if (Role != LinkRole.Host || _server == null || player.Kind == PlayerKind.Host)
                return;

            if (block)
            {
                Blocklist.Add(player);
                Allowlist.Remove(player.MachineId);
            }
            var until = duration.HasValue ? DateTime.UtcNow + duration.Value : DateTime.MaxValue;
            _server.Remove(player.MachineId, until);

            var names = new[] { player.Name, player.GameName }.Where(n => !string.IsNullOrEmpty(n)).ToList();
            lock (_removedNames)
            {
                foreach (string name in names)
                    _removedNames[name] = until;
            }
            int closed = _gate?.Disconnect(name => names.Contains(name, StringComparer.OrdinalIgnoreCase),
                Text.Get(block ? "Link.Game.BlockedKick" : "Link.Game.RemovedKick")) ?? 0;
            LinkLog.Info((block ? "Blocked" : "Removed") + $" a player; {closed} game connection(s) closed.");

            Players = _server.GetPlayers();
            OnChanged();
        }

        /// <summary>Host: whether the room only takes the players on the allowlist.</summary>
        public bool AllowlistOnly => _server?.AllowlistOnly ?? LinkSettings.AllowlistOnly;

        /// <summary>
        /// Host: lets only allowed players in, or everyone with the invite code, at once and for later rooms too.
        /// Turning it on adds the players already in the room to the allowlist. Returns how many were added.
        /// </summary>
        public int SetAllowlistOnly(bool on)
        {
            int added = 0;
            if (on && Role == LinkRole.Host && _server != null)
                added = Allowlist.Add(_server.GetPlayers().Where(p => p.Kind != PlayerKind.Host));
            LinkSettings.AllowlistOnly = on;
            if (_server != null)
                _server.AllowlistOnly = on;
            LinkLog.Info("Allowlist " + (on ? "turned on" : "turned off") + $" by the host; {added} player(s) added.");
            OnChanged();
            return added;
        }

        /// <summary>Host: puts a guest on the allowlist.</summary>
        public void AllowPlayer(PlayerProfile player)
        {
            if (player == null || player.Kind == PlayerKind.Host)
                return;
            Allowlist.Add(new[] { player });
            OnChanged();
        }

        /// <summary>Tells the gate whether a player name may enter the world; called on a worker thread.</summary>
        private string RefuseGamePlayer(string name)
        {
            if (Blocklist.ContainsName(name))
                return Text.Get("Link.Game.BlockedKick");
            if (_server != null && _server.AllowlistOnly && !Allowlist.ContainsName(name))
                return Text.Get("Link.Game.NotAllowedKick");
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

        /// <summary>Host: copies the world in the background when the setting asks for it; the room does not wait for it.</summary>
        private async Task BackupWorldAsync(CancellationTokenSource run, int gamePort)
        {
            if (LinkSettings.BackupWorld)
                await RunBackupAsync(run, gamePort);
        }

        /// <summary>Host: copies the world now and returns the result.</summary>
        public Task<BackupResult> BackupNowAsync()
        {
            if (Role != LinkRole.Host || _run == null || Backup.State == BackupState.Working)
                return Task.FromResult(Backup);
            return RunBackupAsync(_run, MinecraftPort);
        }

        /// <summary>Host: when the world was last copied, in UTC, or <see cref="DateTime.MinValue"/>.</summary>
        private DateTime _lastBackupUtc;

        /// <summary>Host: when the timed backup last checked the world, in UTC.</summary>
        private DateTime _backupCheckedUtc;

        /// <summary>Host: when the next timed backup is due, in local time, or <c>null</c> while there is none.</summary>
        public DateTime? NextBackupAt
        {
            get
            {
                int minutes = LinkSettings.BackupIntervalMinutes;
                if (Role != LinkRole.Host || Stage != LinkStage.Ready || minutes <= 0)
                    return null;
                return (_backupCheckedUtc + TimeSpan.FromMinutes(minutes)).ToLocalTime();
            }
        }

        /// <summary>
        /// Host: starts a timed backup once the interval has passed. A world without changes since the last backup
        /// is not copied again.
        /// </summary>
        private void ScheduleBackup(CancellationTokenSource run)
        {
            int minutes = LinkSettings.BackupIntervalMinutes;
            if (minutes <= 0 || WaitingForWorld || Backup.State == BackupState.Working
                || DateTime.UtcNow - _backupCheckedUtc < TimeSpan.FromMinutes(minutes))
                return;
            _backupCheckedUtc = DateTime.UtcNow;
            _ = RunTimedBackupAsync(run, MinecraftPort, _lastBackupUtc);
        }

        private async Task RunTimedBackupAsync(CancellationTokenSource run, int gamePort, DateTime sinceUtc)
        {
            bool changed = await Task.Run(() => WorldBackup.HasChangedSince(gamePort, sinceUtc));
            if (run != _run || run.IsCancellationRequested)
                return;
            if (!changed)
            {
                LinkLog.Info("The world has not changed since the last backup; the timed backup was skipped.");
                return;
            }
            await RunBackupAsync(run, gamePort);
        }

        private async Task<BackupResult> RunBackupAsync(CancellationTokenSource run, int gamePort)
        {
            var started = DateTime.UtcNow;
            Backup = new BackupResult(BackupState.Working, Backup.WorldName);
            OnChanged();
            BackupResult result;
            try
            {
                result = await Task.Run(() => WorldBackup.Run(gamePort, run.Token));
            }
            catch (OperationCanceledException)
            {
                return Backup;
            }
            catch (Exception ex)
            {
                LinkLog.Warn("The world could not be backed up: " + ex.Message);
                result = new BackupResult(BackupState.Failed, Backup.WorldName);
            }
            if (run != _run)
                return result;
            if (result.State == BackupState.Done)
            {
                _roomBackups.Add(result.Path);
                _lastBackupUtc = started;
            }
            Backup = result;
            OnChanged();
            return result;
        }

        private readonly HashSet<string> _roomBackups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Host: whether the backup at <paramref name="path"/> was made while this room is open.</summary>
        public bool IsRoomBackup(string path) => path != null && _roomBackups.Contains(path);

        /// <summary>Host: the folder name of the world the room runs, or <c>null</c> when it is not found.</summary>
        public string WorldName => Backup.WorldName ?? (MinecraftPort > 0 ? WorldBackup.FindWorldName(MinecraftPort) : null);

        /// <summary>
        /// Host: copies a backup into the saves folder of the game running the world as a new world, and returns the
        /// new world folder. The world being played is not changed.
        /// </summary>
        public async Task<string> RestoreBackupAsync(BackupEntry entry)
        {
            int port = MinecraftPort;
            return await Task.Run(() =>
            {
                var game = Worlds.RunningGame.FromProcess(WorldGameProcess(port))
                           ?? throw new InvalidOperationException("The game running the world was not found.");
                var source = Worlds.WorldSource.Open(entry.Path);
                var world = source.Worlds.FirstOrDefault() ?? throw new InvalidDataException("The backup holds no world.");
                string target = Worlds.WorldImporter.Import(source, world, game.SavesDirectory);
                LinkLog.Info("Restored a backup as " + Path.GetFileName(target) + ".");
                return target;
            });
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

        /// <summary>Host: whether the mods of the game running the world cannot be read, such as for a dedicated server.</summary>
        public bool OwnModsUnavailable => _server?.ModsUnavailable ?? false;

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

                // Asks again while the host is still reading its mods.
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

        // ======================== Voice ========================

        /// <summary>Host: turns voice chat on or off, at once and for later rooms too.</summary>
        public void SetVoice(bool on)
        {
            LinkSettings.Voice = on;
            LinkLog.Info("Voice chat turned " + (on ? "on" : "off") + " by the host.");
            if (Role != LinkRole.Host || Stage != LinkStage.Ready || _run == null)
            {
                OnChanged();
                return;
            }
            if (on)
            {
                _ = PrepareVoiceAsync(_run, true);
            }
            else
            {
                _voiceSerial++;
                SetHostVoice(HostVoiceState.Off, null);
            }
        }

        /// <summary>
        /// Host: finds the voice chat mod in the game running the world and checks whether its voice server runs.
        /// With <paramref name="install"/> a missing mod is installed or enabled; without it the state becomes
        /// <see cref="HostVoiceState.Missing"/>.
        /// </summary>
        private async Task PrepareVoiceAsync(CancellationTokenSource run, bool install)
        {
            int serial = ++_voiceSerial;
            bool Current() => run == _run && serial == _voiceSerial && !run.IsCancellationRequested;
            int port = MinecraftPort;
            SetHostVoice(HostVoiceState.Checking, null);
            try
            {
                var game = await Task.Run(() => Worlds.RunningGame.FromProcess(LocalWorldFinder.FindProcess(port)), run.Token);
                if (!Current())
                    return;
                if (game == null)
                {
                    SetHostVoice(HostVoiceState.Unavailable, "NoGame");
                    return;
                }

                var version = await Task.Run(() => game.FindVersion(), run.Token);
                string mods = Path.Combine(game.GameDirectory, "mods");
                var present = await Task.Run(() => Mods.VoiceMod.Find(mods), run.Token);
                if (!Current())
                    return;
                _voiceGame = game;
                _voiceVersion = version;
                if (present == null && !install)
                {
                    SetHostVoice(HostVoiceState.Missing, null);
                    return;
                }
                if (present == null)
                    SetHostVoice(HostVoiceState.Installing, null);

                var (_, mod) = await Mods.VoiceMod.InstallAsync(mods, version?.GameVersion, version?.Loader, null, run.Token);
                await Task.Run(() => Mods.VoiceMod.EnableLocalServer(game.GameDirectory), run.Token);
                if (!Current())
                    return;
                _voiceMod = mod;
                await RefreshHostVoiceAsync(run, serial);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Mods.ModSyncException ex)
            {
                if (Current())
                    SetHostVoice(HostVoiceState.Unavailable, ex.Key);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException
                                       || ex is ArgumentException)
            {
                LinkLog.Warn("Voice chat could not be prepared: " + ex.Message);
                if (Current())
                    SetHostVoice(HostVoiceState.Unavailable, "Unexpected");
            }
        }

        /// <summary>Host: reads whether the voice server of the game runs, and on which UDP port.</summary>
        private async Task RefreshHostVoiceAsync(CancellationTokenSource run, int serial)
        {
            var game = _voiceGame;
            int gamePort = MinecraftPort;
            if (game == null)
                return;
            int port = await Task.Run(() => FindVoiceServer(game, gamePort));
            if (run != _run || serial != _voiceSerial || run.IsCancellationRequested)
                return;
            var state = port > 0 ? HostVoiceState.Ready : HostVoiceState.Restart;
            if (state != Voice || (port > 0 && port != _voicePort))
            {
                LinkLog.Info(port > 0 ? $"The voice server of the game is running on UDP port {port}." : "The voice server of the game is not running.");
                if (port > 0)
                    _voicePort = port;
                SetHostVoice(state, null);
            }
        }

        /// <summary>
        /// The UDP port of the voice server in <paramref name="game"/>: the one its log reports, else the game port,
        /// as long as the game holds it; 0 when the voice server does not run.
        /// </summary>
        private static int FindVoiceServer(Worlds.RunningGame game, int gamePort)
        {
            int? reported = Mods.VoiceMod.ReadServerPort(game.GameDirectory);
            if (reported != null && LocalWorldFinder.HasUdpSocket(reported.Value, game.ProcessId))
                return reported.Value;
            return LocalWorldFinder.HasUdpSocket(gamePort, game.ProcessId) ? gamePort : 0;
        }

        /// <summary>Host: shows the voice chat state, offers it to players and opens or closes its UDP port to the room.</summary>
        private void SetHostVoice(HostVoiceState state, string problem)
        {
            bool wasReady = Voice == HostVoiceState.Ready;
            Voice = state;
            VoiceProblem = problem;
            bool ready = state == HostVoiceState.Ready;
            int port = _voicePort > 0 ? _voicePort : MinecraftPort;
            if (_server != null)
                _server.Voice = state == HostVoiceState.Off || state == HostVoiceState.Unavailable || state == HostVoiceState.Missing
                    ? null
                    : new Mods.VoiceOffer(ready, port, _voiceVersion?.GameVersion, _voiceVersion?.Loader, _voiceMod);

            int open = ready ? port : 0;
            if (open != _openVoicePort && _network != null && _gate != null)
            {
                _openVoicePort = open;
                _ = _network.SetUdpWhitelistAsync(open > 0 ? new[] { _gate.Port, open } : new[] { _gate.Port }, CancellationToken.None);
            }
            if (wasReady != ready)
                _lobby?.Refresh();
            OnChanged();
        }

        /// <summary>Guest: asks whether the host offers voice chat; hosts without it are left alone.</summary>
        private async Task FetchHostVoiceAsync(CancellationTokenSource run)
        {
            try
            {
                var protocols = await _client.GetProtocolsAsync(run.Token);
                if (!protocols.Contains(RoomProtocol.Voice) || run != _run)
                    return;
                _hostOffersVoice = true;
                await ReadHostVoiceAsync(run.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (RoomClient.IsConnectionError(ex))
            {
                LinkLog.Warn("The host's voice chat could not be read: " + ex.Message);
            }
        }

        /// <summary>Guest: reads the host's voice chat and forwards its UDP port once its voice server runs.</summary>
        private async Task ReadHostVoiceAsync(CancellationToken token)
        {
            var offer = await _client.GetVoiceAsync(token);
            var state = offer == null ? GuestVoiceState.Off : offer.Ready ? GuestVoiceState.Ready : GuestVoiceState.Waiting;
            if (state == GuestVoiceState.Ready && _voiceForwardPort != offer.Port)
            {
                var bind = IsSharedWithLan ? IPAddress.Any : IPAddress.Loopback;
                if (IsPortFree(offer.Port, bind)
                    && await _network.AddPortForwardAsync("udp", new IPEndPoint(bind, offer.Port), new IPEndPoint(_hostAddress, offer.Port), token))
                {
                    _voiceForwardPort = offer.Port;
                    LinkLog.Info($"The host's voice chat is reachable at UDP port {offer.Port}.");
                }
                else
                {
                    state = GuestVoiceState.PortBusy;
                    if (GuestVoice != GuestVoiceState.PortBusy)
                        LinkLog.Warn($"UDP port {offer.Port} is in use on this computer; the host's voice chat cannot be reached.");
                }
            }

            bool changed = state != GuestVoice || (HostVoice?.Mod?.Sha1 != offer?.Mod?.Sha1) || (HostVoice == null) != (offer == null);
            HostVoice = offer;
            GuestVoice = state;
            if (changed)
                OnChanged();
        }

        // ======================== Room ========================

        /// <summary>Guest: the host's room name, announcement and player limit, or <c>null</c> when the host does not tell.</summary>
        public RoomInfo HostRoom { get; private set; }

        /// <summary>Guest: whether the host answers room requests.</summary>
        private bool _hostSharesRoom;

        /// <summary>Host: the room as the settings describe it.</summary>
        private static RoomInfo CurrentRoomInfo()
        {
            return new RoomInfo(LinkSettings.RoomName, LinkSettings.RoomNotice, LinkSettings.MaxPlayers);
        }

        /// <summary>
        /// Host: hands the saved room settings to the room's players, the game and the lobby listing at once.
        /// </summary>
        public async Task ApplyRoomSettingsAsync()
        {
            if (Role != LinkRole.Host || Stage != LinkStage.Ready || _server == null)
                return;
            _server.Room = CurrentRoomInfo();
            if (_agentState != null)
                _agentState.Notice = LinkSettings.RoomNotice;
            LinkLog.Info("Room settings changed by the host.");

            var lobby = _lobby;
            if (lobby != null)
            {
                var listing = lobby.Listing;
                listing.Title = LinkSettings.RoomName;
                listing.Tags = LinkSettings.LobbyTags;
                listing.Description = LinkSettings.LobbyDescription;
                listing.ShowRegion = LinkSettings.LobbyShowRegion;
                int limit = LinkSettings.MaxPlayers;
                if (limit > 0)
                {
                    listing.MaxPlayers = limit;
                }
                else
                {
                    var status = await MinecraftProbe.GetStatusAsync(MinecraftPort, TimeSpan.FromSeconds(3));
                    listing.MaxPlayers = status?.MaxPlayers ?? 0;
                }
                if (_lobby == lobby)
                {
                    lobby.Dispose();
                    _lobby = new LobbyPublisher(listing, null, () => Players.Count, () => Voice == HostVoiceState.Ready, _ui);
                    _lobby.Changed += (sender, e) => OnChanged();
                    _lobby.Start();
                }
            }
            OnChanged();
        }

        /// <summary>Guest: asks for the host's room information; hosts without it are left alone.</summary>
        private async Task FetchRoomInfoAsync(CancellationTokenSource run)
        {
            try
            {
                var protocols = await _client.GetProtocolsAsync(run.Token);
                if (!protocols.Contains(RoomProtocol.Room) || run != _run)
                    return;
                _hostSharesRoom = true;
                await ReadRoomInfoAsync(run.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (RoomClient.IsConnectionError(ex))
            {
                LinkLog.Warn("The host's room information could not be read: " + ex.Message);
            }
        }

        private async Task ReadRoomInfoAsync(CancellationToken token)
        {
            var room = await _client.GetRoomInfoAsync(token);
            bool changed = HostRoom == null || HostRoom.Name != room.Name || HostRoom.Notice != room.Notice || HostRoom.MaxPlayers != room.MaxPlayers;
            HostRoom = room;
            if (changed)
                OnChanged();
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
            _lobby = new LobbyPublisher(listing, ticket, () => Players.Count, () => Voice == HostVoiceState.Ready, _ui);
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
            Voice = HostVoiceState.Off;
            VoiceProblem = null;
            WaitingForWorld = false;
            HostVoice = null;
            GuestVoice = GuestVoiceState.Off;
            _voiceSerial++;
            _voiceGame = null;
            _voiceVersion = null;
            _voiceMod = null;
            _voicePort = 0;
            _openVoicePort = 0;
            _hostOffersVoice = false;
            _hostSharesRoom = false;
            HostRoom = null;
            _roomBackups.Clear();
            _lastBackupUtc = DateTime.MinValue;
            _backupCheckedUtc = DateTime.UtcNow;
            _hostAddress = null;
            _voiceForwardPort = 0;
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
                ex = new LinkException(rejected.Full ? "Link.Error.RoomFull" : rejected.NotAllowed ? "Link.Error.NotAllowed"
                    : rejected.Blocked ? "Link.Error.Blocked" : "Link.Error.Removed");
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
        /// <param name="gameDirectory">Only games with this game folder are asked, or every game when <c>null</c>.</param>
        private async Task OpenWorldToLanAsync(CancellationTokenSource run, Task world, string gameDirectory = null)
        {
            var token = run.Token;
            var asked = new HashSet<int>();
            var answered = new HashSet<int>();
            SetAutoLan(AutoLanState.NoGame);
            try
            {
                while (!world.IsCompleted && run == _run && !token.IsCancellationRequested)
                {
                    var games = (await Task.Run(() => Worlds.RunningGame.Find(), token))
                        .Where(g => gameDirectory == null || string.Equals(g.GameDirectory, gameDirectory, StringComparison.OrdinalIgnoreCase))
                        .ToList();
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
            // On Windows 7, checks that the component can run.
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

        /// <summary>Guest: <paramref name="profile"/> with the latency, path and loss to the host measured last.</summary>
        private PlayerProfile WithHostConnection(PlayerProfile profile)
        {
            var host = HostPeer;
            string path = host == null ? null : host.IsDirect ? PlayerProfile.PathDirect : PlayerProfile.PathRelay;
            return profile.WithConnection(host?.LatencyMs, path, host?.LossPercent);
        }

        private IReadOnlyList<PlayerProfile> MarkLocal(IReadOnlyList<PlayerProfile> players)
        {
            // The local row shows the newest measurement rather than the one the host received last.
            var list = players
                .Select(p => p.MachineId == _self.MachineId ? WithHostConnection(p.WithKind(PlayerKind.Local)) : p)
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

        /// <summary>Text of the room entry in the multiplayer list: the player's own name for it, else the host's room name.</summary>
        private string LanEntryText()
        {
            string custom = LinkSettings.LanEntryName;
            if (custom.Length > 0)
                return custom;
            string room = HostRoom?.Name;
            return string.IsNullOrEmpty(room) ? Text.Get("Link.LanEntry") : room;
        }

        /// <summary>The entry text followed by the live room status on one line; read from the announcer's thread.</summary>
        private string LanEntryWithStatus()
        {
            string status = _lanStatus;
            return status == null ? LanEntryText() : LanEntryText() + " §8· §7" + status;
        }

        private void UpdateLanStatus()
        {
            var host = HostPeer;
            string status = host?.LatencyMs == null
                ? Text.Format("Link.Game.ListStatus", Players.Count)
                : Text.Format("Link.Game.ListStatusLatency", Players.Count, FormatLatency(host.LatencyMs.Value));

            // The LAN entry shows the host name.
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
