package top.mccampfire.core.link

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import top.mccampfire.core.link.minecraft.LanAnnouncer
import top.mccampfire.core.link.minecraft.MinecraftProbe
import top.mccampfire.core.link.room.PlayerKind
import top.mccampfire.core.link.room.PlayerProfile
import top.mccampfire.core.link.room.RoomClient
import top.mccampfire.core.link.room.RoomGate
import top.mccampfire.core.link.room.RoomInfo
import top.mccampfire.core.link.room.RoomServer
import top.mccampfire.core.link.room.RoomProtocol
import top.mccampfire.core.link.room.RoomRejectedException
import top.mccampfire.core.link.room.VoiceOffer
import top.mccampfire.core.link.runtime.Connector
import top.mccampfire.core.link.runtime.LoopbackV4
import top.mccampfire.core.link.runtime.NetworkConfig
import top.mccampfire.core.link.runtime.NetworkPeer
import top.mccampfire.core.link.runtime.NetworkRuntime
import top.mccampfire.core.link.runtime.PortForward
import top.mccampfire.core.link.runtime.Socks5Connector
import top.mccampfire.core.link.runtime.TcpForwarder
import top.mccampfire.core.link.runtime.parseIpv4
import java.io.Closeable
import java.io.IOException
import java.security.SecureRandom
import java.util.Locale

/** A failure that ends a session with [error]. */
class LinkFailure(val error: LinkError, detail: String? = null, cause: Throwable? = null) : Exception(detail ?: error.name, cause)

/** Sets up, runs and ends a room session on this device. */
class LinkSession(
    private val runtime: NetworkRuntime,
    private val texts: LinkTexts,
    private val log: LinkLogger,
    private val scope: CoroutineScope,
    private val clock: () -> Long = System::currentTimeMillis,
    private val timing: Timing = Timing(),
    private val connectorFor: (socksPort: Int) -> Connector = ::Socks5Connector,
) {
    /** Intervals and limits of a session. */
    data class Timing(
        val pollMillis: Long = 1_500,
        val directGraceMillis: Long = 8_000,
        val connectRoomMillis: Long = 30_000,
        val connectRetryMillis: Long = 2_000,
        val tickMillis: Long = 2_000,
        val probeTries: Int = 6,
        val probeTimeoutMillis: Int = 4_000,
        val worldWaitMillis: Long = 10 * 60 * 1000L,
        val reconnectMillis: Long = 30_000,
        val hostGoneMillis: Long = 10_000,
    )

    /** Host: the network settings of the open room. */
    @Volatile
    private var hostConfig: NetworkConfig? = null

    /** Guest: the network settings of the joined room, and the last voice port a forward was tried for. */
    @Volatile
    private var guestConfig: NetworkConfig? = null
    private var triedVoicePort = 0

    private val mutableState = MutableStateFlow(LinkState())
    private val resources = mutableListOf<Closeable>()
    private var job: Job? = null
    private var client: RoomClient? = null
    private var server: RoomServer? = null

    /** The current session. */
    val state: StateFlow<LinkState> = mutableState.asStateFlow()

    /** Joins the room behind [code]. */
    fun join(code: RoomCode, options: LinkOptions) {
        stop()
        mutableState.value = LinkState(stage = LinkStage.Working, role = LinkRole.Guest, step = LinkStep.Network, code = code)
        launch { runGuest(code, options) }
    }

    /**
     * Opens a room for a LAN world on this device once it answers.
     *
     * @param world Waits for the port of the world; a typed port is returned at once.
     * @param reuseCode The invite code of an earlier room to open again, or null for a new one.
     */
    fun host(world: suspend () -> Int, options: LinkOptions, room: RoomInfo, gate: RoomGate, reuseCode: RoomCode? = null) {
        stop()
        mutableState.value = LinkState(stage = LinkStage.Working, role = LinkRole.Host, step = LinkStep.World)
        launch { runHost(world, options, room, gate, reuseCode) }
    }

    /** Host: takes a guest out of the room until [untilMillis]. */
    fun removePlayer(machineId: String, untilMillis: Long) {
        server?.remove(machineId, untilMillis)
    }

    /** Host: changes the room's name, announcement and player limit for the players. */
    fun updateRoom(room: RoomInfo) {
        server?.room = room
        mutableState.update { it.copy(room = room) }
    }

    /** Host: lets only allowlisted players in, or everyone with the code. */
    fun setAllowlistOnly(on: Boolean) {
        server?.allowlistOnly = on
    }

    /** Host: the voice chat players receive, or null to turn it off. */
    fun setVoice(offer: VoiceOffer?) {
        server?.voice = offer
    }

    /** Host: opens the UDP [port] of the game's voice server to the room; restarts the network when it was closed. */
    fun openVoicePort(port: Int) {
        val config = hostConfig ?: return
        if (port.toString() in config.udpWhitelist) return
        val next = config.copy(udpWhitelist = config.udpWhitelist + port.toString())
        hostConfig = next
        scope.launch {
            try {
                io { runtime.stop(InstanceName) }
                startNetwork(next)
                log.info("Opened UDP port $port of the voice server to the room.")
            } catch (e: Exception) {
                log.warn("The voice port could not be opened: ${e.message}")
            }
        }
    }

    private fun launch(block: suspend () -> Unit) {
        job = scope.launch {
            try {
                block()
            } catch (e: CancellationException) {
                throw e
            } catch (e: LinkFailure) {
                fail(e.error, e.message, e)
            } catch (e: RoomRejectedException) {
                fail(rejectionError(e.reason), e.message, e)
            } catch (e: Exception) {
                fail(LinkError.Unexpected, e.message, e)
            }
        }
    }

    private suspend fun runHost(world: suspend () -> Int, options: LinkOptions, room: RoomInfo, gate: RoomGate, reuseCode: RoomCode?) {
        val gamePort = world()
        mutableState.update { it.copy(gamePort = gamePort) }
        while (!io { MinecraftProbe.isServer(gamePort, timing.probeTimeoutMillis) }) {
            mutableState.update { it.copy(waitingForWorld = true) }
            delay(timing.tickMillis)
        }
        mutableState.update { it.copy(waitingForWorld = false) }
        log.info("Hosting the LAN world on port $gamePort.")

        setStep(LinkStep.Network)
        val code = reuseCode ?: RoomCode.create()
        val self = selfProfile(options).withKind(PlayerKind.Host)
        val roomServer = io { RoomServer.start(self, gamePort, gate) }
        roomServer.room = room
        roomServer.allowlistOnly = options.allowlistOnly
        roomServer.onPlayersChanged = {
            val players = roomServer.players()
            log.info("Players: " + players.joinToString { it.name + " (" + it.vendor.substringBefore(',') + ")" })
            mutableState.update { it.copy(players = players) }
        }
        server = roomServer
        keep(roomServer)

        val config = NetworkConfig(
            instanceName = InstanceName,
            networkName = code.networkName,
            networkSecret = code.networkSecret,
            hostname = RoomProtocol.HostNamePrefix + roomServer.port,
            ipv4 = RoomProtocol.HostAddress,
            peers = options.nodes,
            tcpWhitelist = listOf(roomServer.port.toString(), gamePort.toString()),
            udpWhitelist = listOf(gamePort.toString()),
            directOnly = !options.allowRelay,
            hideAddress = options.hideAddress,
        )
        hostConfig = config
        startNetwork(config)
        for (attempt in 0 until 10) {
            val nodes = io { runtime.info(InstanceName) }?.connectedNodes ?: 0
            mutableState.update { it.copy(connectedNodes = nodes) }
            if (nodes > 0) break
            delay(1_000)
        }

        mutableState.update {
            it.copy(stage = LinkStage.Ready, step = null, code = code, room = room, players = roomServer.players())
        }
        log.info("Room ${code.masked()} is open (${mutableState.value.connectedNodes} nodes connected).")
        monitorHost(gamePort, roomServer)
    }

    private suspend fun monitorHost(gamePort: Int, roomServer: RoomServer) {
        var tick = 0
        var silentSince = 0L
        while (true) {
            delay(timing.tickMillis)
            tick++
            roomServer.removeSilentGuests()
            val info = io { runtime.info(InstanceName) }
            if (info != null) {
                if (!info.running && info.errorMessage != null) throw LinkFailure(LinkError.NetworkCrashed, info.errorMessage)
                noteNodes(info.connectedNodes)
                val members = info.peers.filter { !it.isLocal && it.isMember }
                mutableState.update {
                    it.copy(
                        players = roomServer.players(),
                        directCount = members.count { peer -> peer.isDirect },
                        relayCount = members.count { peer -> !peer.isDirect },
                        connectedNodes = info.connectedNodes,
                        localNat = info.local?.nat ?: it.localNat,
                    )
                }
            }
            if (tick % 3 == 0) {
                if (io { MinecraftProbe.isServer(gamePort, timing.probeTimeoutMillis) }) {
                    silentSince = 0
                    mutableState.update { it.copy(waitingForWorld = false) }
                } else {
                    if (silentSince == 0L) silentSince = clock()
                    mutableState.update { it.copy(waitingForWorld = true) }
                    if (clock() - silentSince > timing.worldWaitMillis) throw LinkFailure(LinkError.WorldClosed)
                }
            }
        }
    }

    /** Leaves or closes the room, or cancels setting it up. */
    fun stop() {
        job?.cancel()
        job = null
        release()
        if (mutableState.value.isActive) {
            log.info("Session stopped.")
            mutableState.value = LinkState()
        }
    }

    /** Clears a failure so the pages return to the idle state. */
    fun dismissFailure() {
        if (mutableState.value.stage == LinkStage.Failed) mutableState.value = LinkState()
    }

    private suspend fun runGuest(code: RoomCode, options: LinkOptions) {
        val socksPort = io { TcpForwarder.freePort() }
        val config = NetworkConfig(
            instanceName = InstanceName,
            networkName = code.networkName,
            networkSecret = code.networkSecret,
            hostname = "campfire-" + randomHex(4),
            peers = options.nodes,
            directOnly = !options.allowRelay,
            hideAddress = options.hideAddress,
            socksPort = socksPort,
        )
        guestConfig = config
        startNetwork(config)
        log.info("Joining room ${code.masked()} with ${options.nodes.size} nodes.")

        setStep(LinkStep.FindHost)
        val host = findHost(options)
        val hostAddress = parseIpv4(host.ipv4) ?: throw LinkFailure(LinkError.RoomNotFound, "Invalid host address ${host.ipv4}")
        val servicePort = host.hostname.removePrefix(RoomProtocol.HostNamePrefix).toInt()

        setStep(LinkStep.Enter)
        val connector = connectorFor(socksPort)
        val room = connectRoom(connector, hostAddress, servicePort)
        val hostPort = io { room.serverPort() }
        val localPort = chooseLocalPort(options.localPort, hostPort)
        val forwarder = try {
            io { TcpForwarder.start(localPort, connector, hostAddress, hostPort, onError = { log.warn("Forward failed: ${it.message}") }) }
        } catch (e: IOException) {
            throw LinkFailure(LinkError.ForwardFailed, e.message, e)
        }
        keep(forwarder)
        log.info("Host game port $hostPort is reachable at local port $localPort.")

        var reachable = false
        for (attempt in 0 until timing.probeTries) {
            reachable = io { MinecraftProbe.isServer(localPort, timing.probeTimeoutMillis) }
            if (reachable) break
            delay(1_000)
        }
        if (!reachable) log.warn("The host's game did not answer through the tunnel yet; continuing anyway.")

        var self = selfProfile(options)
        self = withHostConnection(self)
        io { room.heartbeat(self) }
        val players = io { room.players() }
        val protocols = io { room.protocols() }
        val sharesRoom = RoomProtocol.Room in protocols
        val roomInfo = if (sharesRoom) runCatching { io { room.roomInfo() } }.getOrNull() else null
        mutableState.update {
            it.copy(
                stage = LinkStage.Ready,
                step = null,
                gamePort = localPort,
                players = markLocal(players, self),
                room = roomInfo,
            )
        }
        keep(LanAnnouncer({ lanEntry(options) }, localPort))
        log.info("Joined room ${code.masked()}.")

        monitorGuest(options, room, connector, hostAddress, servicePort, sharesRoom, self)
    }

    private suspend fun monitorGuest(
        options: LinkOptions,
        firstRoom: RoomClient,
        connector: Connector,
        hostAddress: java.net.Inet4Address,
        servicePort: Int,
        sharesRoom: Boolean,
        initialSelf: PlayerProfile,
    ) {
        var room = firstRoom
        var self = initialSelf
        var tick = 0
        while (true) {
            delay(timing.tickMillis)
            tick++
            refreshNetwork()

            if (tick % 2 == 0) {
                try {
                    self = withHostConnection(self)
                    io { room.heartbeat(self) }
                    val players = io { room.players() }
                    mutableState.update { it.copy(players = markLocal(players, self)) }
                } catch (e: RoomRejectedException) {
                    throw LinkFailure(rejectionError(e.reason))
                } catch (e: IOException) {
                    log.warn("Heartbeat failed: ${e.message}")
                    room.close()
                    room = reconnectRoom(connector, hostAddress, servicePort) ?: throw LinkFailure(LinkError.HostLost, e.message, e)
                    client = room
                    log.info("Reconnected to the room service.")
                }
            }

            if (tick % 5 == 1) {
                runCatching { io { room.voice() } }.onSuccess { offer ->
                    if (offer != mutableState.value.hostVoice) mutableState.update { it.copy(hostVoice = offer) }
                    if (offer != null) forwardVoice(offer.port)
                }
            }

            if (sharesRoom && tick % 5 == 3) {
                runCatching { io { room.roomInfo() } }.getOrNull()?.let { info -> mutableState.update { it.copy(room = info) } }
            }
        }
    }

    /** Guest: forwards the local UDP [port] to the host's voice server; restarts the network when the port changes. */
    private suspend fun forwardVoice(port: Int) {
        val current = mutableState.value
        if (current.voiceForwardPort == port || port == triedVoicePort) return
        triedVoicePort = port
        val config = guestConfig ?: return
        if (!io { isUdpPortFree(port) }) {
            log.warn("UDP port $port of the host's voice server is in use on this device.")
            mutableState.update { it.copy(voiceForwardPort = 0, voiceForwardBusy = true) }
            return
        }
        val next = config.copy(portForwards = listOf(PortForward("${LoopbackV4.hostAddress}:$port", "${RoomProtocol.HostAddress}:$port", "udp")))
        guestConfig = next
        io { runtime.stop(InstanceName) }
        startNetwork(next)
        mutableState.update { it.copy(voiceForwardPort = port, voiceForwardBusy = false) }
        log.info("Forwarding UDP port $port to the host's voice server.")
    }

    /** Reconnects to the room service while the host stays in the network; null when it does not come back. */
    private suspend fun reconnectRoom(connector: Connector, address: java.net.Inet4Address, port: Int): RoomClient? {
        val deadline = clock() + timing.reconnectMillis
        var goneSince = 0L
        while (clock() < deadline) {
            val info = io { runtime.info(InstanceName) }
            if (info != null && info.peers.none(::isRoomHost)) {
                if (goneSince == 0L) goneSince = clock()
                if (clock() - goneSince > timing.hostGoneMillis) return null
            } else {
                goneSince = 0L
            }
            io { RoomClient.tryConnect(connector, address, port) }?.let { return it }
            delay(timing.connectRetryMillis)
        }
        return null
    }

    private suspend fun startNetwork(config: NetworkConfig) {
        try {
            io { runtime.start(config) }
        } catch (e: IOException) {
            throw LinkFailure(LinkError.NetworkStartFailed, e.message, e)
        }
        keep { runtime.stop(config.instanceName) }
    }

    private suspend fun findHost(options: LinkOptions): NetworkPeer {
        val started = clock()
        var firstSeen: Long? = null
        val timeoutMillis = options.searchTimeoutSeconds * 1000L
        while (true) {
            val searchMillis = clock() - started
            val info = io { runtime.info(InstanceName) }
            if (info != null && !info.running && info.errorMessage != null) {
                throw LinkFailure(LinkError.NetworkCrashed, info.errorMessage)
            }
            val host = info?.peers?.firstOrNull(::isRoomHost)
            mutableState.update {
                it.copy(
                    searchMillis = searchMillis,
                    hostPeer = host ?: it.hostPeer,
                    connectedNodes = info?.connectedNodes ?: it.connectedNodes,
                    localNat = info?.local?.nat ?: it.localNat,
                )
            }
            if (host != null) {
                val seen = firstSeen ?: clock().also { firstSeen = it }
                if (host.isDirect || options.hideAddress || (options.allowRelay && clock() - seen > timing.directGraceMillis)) {
                    log.info("Found host ${host.hostname} (${if (host.isDirect) "direct" else "relayed"}, ${host.tunnel}).")
                    return host
                }
            }
            if (searchMillis > timeoutMillis) {
                throw LinkFailure(
                    when {
                        host != null -> LinkError.NoDirectPath
                        (info?.connectedNodes ?: 0) == 0 -> LinkError.NoNodes
                        else -> LinkError.RoomNotFound
                    },
                )
            }
            delay(timing.pollMillis)
        }
    }

    private suspend fun connectRoom(connector: Connector, address: java.net.Inet4Address, port: Int): RoomClient {
        val deadline = clock() + timing.connectRoomMillis
        while (clock() < deadline) {
            val room = io { RoomClient.tryConnect(connector, address, port) }
            if (room != null) {
                client = room
                keep { client?.close() }
                return room
            }
            delay(timing.connectRetryMillis)
        }
        throw LinkFailure(LinkError.HostNoAnswer)
    }

    private suspend fun chooseLocalPort(configured: Int, hostPort: Int): Int = io {
        when {
            configured > 0 -> if (TcpForwarder.isPortFree(configured)) configured else throw LinkFailure(LinkError.LocalPortBusy, configured.toString())
            TcpForwarder.isPortFree(hostPort) -> hostPort
            else -> TcpForwarder.freePort()
        }
    }

    private suspend fun refreshNetwork() {
        val info = io { runtime.info(InstanceName) } ?: return
        if (!info.running && info.errorMessage != null) throw LinkFailure(LinkError.NetworkCrashed, info.errorMessage)
        noteNodes(info.connectedNodes)
        val host = info.peers.firstOrNull(::isRoomHost)
        mutableState.update {
            it.copy(
                hostPeer = host ?: it.hostPeer,
                connectedNodes = info.connectedNodes,
                localNat = info.local?.nat ?: it.localNat,
            )
        }
    }

    private fun selfProfile(options: LinkOptions): PlayerProfile = PlayerProfile(
        name = options.playerName,
        machineId = options.machineId,
        vendor = options.vendor,
        kind = PlayerKind.Local,
        gameName = options.gameName,
    )

    private fun withHostConnection(profile: PlayerProfile): PlayerProfile {
        val host = mutableState.value.hostPeer
        val path = host?.let { if (it.isDirect) PlayerProfile.PathDirect else PlayerProfile.PathRelay }
        return profile.withConnection(host?.latencyMs, path, host?.lossPercent)
    }

    private fun markLocal(players: List<PlayerProfile>, self: PlayerProfile): List<PlayerProfile> {
        val list = players.map { if (it.machineId == self.machineId) withHostConnection(it.withKind(PlayerKind.Local)) else it }
            .toMutableList()
        if (list.none { it.kind == PlayerKind.Local }) list += self
        return list.sortedBy { if (it.kind == PlayerKind.Host) 0 else 1 }
    }

    private fun lanEntry(options: LinkOptions): String {
        val state = mutableState.value
        val name = options.lanEntryName.ifBlank { state.room?.name?.ifBlank { null } ?: texts.defaultLanEntry }
        val latency = state.hostPeer?.latencyMs?.let(::formatLatency)
        val status = texts.lanStatus(state.players.size, latency)
        val host = state.players.firstOrNull { it.kind == PlayerKind.Host }
        val hostName = host?.gameName ?: host?.name
        return name + " §8· §7" + (if (hostName.isNullOrBlank()) status else "$hostName · $status")
    }

    private var loggedNodes = -1

    private fun noteNodes(nodes: Int) {
        if (nodes != loggedNodes) {
            loggedNodes = nodes
            log.info("Connected nodes: $nodes")
        }
    }

    private fun setStep(step: LinkStep) = mutableState.update { it.copy(step = step) }

    private fun fail(error: LinkError, detail: String?, cause: Throwable) {
        log.error("Session failed: $error ${detail.orEmpty()} ${cause::class.java.simpleName}")
        release()
        mutableState.update { it.copy(stage = LinkStage.Failed, error = error, errorDetail = detail) }
    }

    private fun keep(resource: Closeable) = synchronized(resources) { resources += resource }

    private fun release() {
        val list = synchronized(resources) { resources.toList().also { resources.clear() } }
        list.asReversed().forEach { runCatching { it.close() } }
        client = null
        server = null
        hostConfig = null
        guestConfig = null
        triedVoicePort = 0
    }

    private suspend fun <T> io(block: () -> T): T = withContext(Dispatchers.IO) { block() }

    private fun rejectionError(reason: RoomRejectedException.Reason): LinkError = when (reason) {
        RoomRejectedException.Reason.Removed -> LinkError.Removed
        RoomRejectedException.Reason.Blocked -> LinkError.Blocked
        RoomRejectedException.Reason.Full -> LinkError.RoomFull
        RoomRejectedException.Reason.NotAllowed -> LinkError.NotAllowed
    }

    companion object {
        /** Name of the room's network instance. */
        const val InstanceName = "campfire-room"

        private val Random = SecureRandom()

        private fun randomHex(bytes: Int): String =
            ByteArray(bytes).also(Random::nextBytes).joinToString("") { "%02x".format(it) }

        /** The host puts the port of its room service at the end of its name. */
        /** True when nothing listens on the UDP [port] of the loopback address. */
        fun isUdpPortFree(port: Int): Boolean = runCatching {
            java.net.DatagramSocket(null).use { it.bind(java.net.InetSocketAddress(LoopbackV4, port)) }
        }.isSuccess

        fun isRoomHost(peer: NetworkPeer): Boolean {
            if (peer.isLocal || !peer.isMember || !peer.hostname.startsWith(RoomProtocol.HostNamePrefix)) return false
            val port = peer.hostname.removePrefix(RoomProtocol.HostNamePrefix).toIntOrNull() ?: return false
            return port in 1..65535
        }

        /** Latency as the room shows it: one decimal below 10 ms. */
        fun formatLatency(latency: Double): String =
            if (latency < 10) String.format(Locale.ROOT, "%.1f", latency) else String.format(Locale.ROOT, "%.0f", latency)
    }
}
