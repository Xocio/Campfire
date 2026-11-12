package top.mccampfire.core.link

import top.mccampfire.core.link.runtime.LoopbackV4
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import top.mccampfire.core.link.minecraft.LanAnnouncement
import top.mccampfire.core.link.room.PlayerKind
import top.mccampfire.core.link.room.PlayerProfile
import top.mccampfire.core.link.room.RoomInfo
import top.mccampfire.core.link.room.RoomProtocol
import top.mccampfire.core.link.room.RoomServer
import top.mccampfire.core.link.runtime.DirectConnector
import top.mccampfire.core.link.runtime.NatKind
import top.mccampfire.core.link.runtime.NetworkConfig
import top.mccampfire.core.link.runtime.NetworkInfo
import top.mccampfire.core.link.runtime.NetworkPeer
import top.mccampfire.core.link.runtime.NetworkRuntime
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.ServerSocket
import java.net.SocketTimeoutException
import kotlin.concurrent.thread

class LinkSessionTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val closeables = mutableListOf<AutoCloseable>()

    /** Runtime whose network shows a host after [hostAfterPolls] reads. */
    private class FakeRuntime(var hostPeer: NetworkPeer?, var hostAfterPolls: Int = 0, var nodes: Int = 1) : NetworkRuntime {
        val started = mutableListOf<NetworkConfig>()
        val stopped = mutableListOf<String>()
        private var polls = 0

        override fun start(config: NetworkConfig) {
            started += config
        }

        override fun stop(instanceName: String) {
            stopped += instanceName
        }

        override fun info(instanceName: String): NetworkInfo {
            polls++
            val local = NetworkPeer("campfire-local", "10.144.144.2", true, false, null, null, null, NatKind.FullCone)
            val peers = listOfNotNull(local, hostPeer.takeIf { polls > hostAfterPolls })
            return NetworkInfo(running = true, errorMessage = null, peers = peers, connectedNodes = nodes)
        }
    }

    private object Texts : LinkTexts {
        override val defaultLanEntry = "Campfire Room"
        override fun lanStatus(players: Int, latency: String?) = if (latency == null) "$players players" else "$players players · $latency ms"
    }

    private object Log : LinkLogger {
        override fun info(message: String) = Unit
        override fun warn(message: String) = Unit
        override fun error(message: String) = Unit
    }

    private val timing = LinkSession.Timing(
        pollMillis = 50,
        directGraceMillis = 200,
        connectRoomMillis = 2_000,
        connectRetryMillis = 100,
        tickMillis = 100,
        probeTries = 2,
        probeTimeoutMillis = 500,
        worldWaitMillis = 1_000,
        reconnectMillis = 3_000,
        hostGoneMillis = 300,
    )

    private fun options(timeout: Int = 1, relay: Boolean = true) = LinkOptions(
        playerName = "Alex",
        gameName = "Alex_99",
        machineId = "b".repeat(32),
        vendor = "Campfire Android 0.1.0, EasyTier v2.6.4",
        allowRelay = relay,
        hideAddress = false,
        searchTimeoutSeconds = timeout,
        localPort = 0,
        lanEntryName = "",
        nodes = listOf("tcp://example.invalid:11010"),
    )

    /** A world that answers the legacy ping. */
    private fun fakeWorld(): ServerSocket = ServerSocket(0, 50, LoopbackV4).also { server ->
        closeables += server
        thread(isDaemon = true) {
            while (!server.isClosed) {
                val socket = runCatching { server.accept() }.getOrNull() ?: break
                thread(isDaemon = true) {
                    socket.use {
                        if (it.getInputStream().read() == 0xFE) it.getOutputStream().write(byteArrayOf(0xFF.toByte(), 0, 0))
                    }
                }
            }
        }
    }

    private fun hostPeer(servicePort: Int, direct: Boolean = true) =
        NetworkPeer(RoomProtocol.HostNamePrefix + servicePort, "127.0.0.1", false, direct, 20.0, 0.0, "udp", NatKind.Open)

    private fun session(runtime: NetworkRuntime) = LinkSession(runtime, Texts, Log, scope, timing = timing, connectorFor = { DirectConnector })

    @After
    fun tearDown() {
        scope.cancel()
        closeables.asReversed().forEach { runCatching { it.close() } }
    }

    @Test
    fun guestJoinsAHostAndAnnouncesTheRoom() = runBlocking {
        val world = fakeWorld()
        val host = PlayerProfile("Steve", "a".repeat(32), "Campfire 1.1.1, EasyTier v2.6.4", PlayerKind.Host)
        val server = RoomServer.start(host, world.localPort, preferredPort = 0).also { closeables += it }
        server.room = RoomInfo.of("Weekend", "Be nice", 8)
        val runtime = FakeRuntime(hostPeer(server.port), hostAfterPolls = 2)

        val listener = DatagramSocket(0, LoopbackV4).also { closeables += it }
        val session = session(runtime)
        session.join(RoomCode.fromRandom(ByteArray(16) { it.toByte() }), options())

        val ready = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Ready || it.stage == LinkStage.Failed } }
        assertEquals(LinkStage.Ready, ready.stage)
        assertEquals("Weekend", ready.room?.name)
        assertEquals(listOf(PlayerKind.Host, PlayerKind.Local), ready.players.map { it.kind })
        assertTrue(ready.gamePort > 0)
        assertEquals(listOf("0"), runtime.started.single().tcpWhitelist)
        assertTrue(runtime.started.single().socksPort != null)

        val guests = server.players().drop(1)
        assertEquals("Alex", guests.single().name)
        assertEquals("Alex_99", guests.single().gameName)
        assertEquals(PlayerProfile.PathDirect, guests.single().path)

        session.stop()
        assertEquals(listOf(LinkSession.InstanceName), runtime.stopped)
        assertEquals(LinkStage.Idle, session.state.value.stage)
        assertTrue(listener.localPort > 0)
    }

    @Test
    fun theLanEntryReachesTheGameOnThisDevice() = runBlocking {
        val socket = runCatching { DatagramSocket(LanAnnouncement.Port, LoopbackV4) }.getOrNull()
            ?: return@runBlocking
        closeables += socket
        val world = fakeWorld()
        val host = PlayerProfile("Steve", "a".repeat(32), "Campfire", PlayerKind.Host)
        val server = RoomServer.start(host, world.localPort, preferredPort = 0).also { closeables += it }
        val session = session(FakeRuntime(hostPeer(server.port)))
        session.join(RoomCode.create(), options())
        val ready = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Ready } }

        socket.soTimeout = 5_000
        val packet = DatagramPacket(ByteArray(512), 512)
        try {
            socket.receive(packet)
        } catch (e: SocketTimeoutException) {
            throw AssertionError("no LAN announcement", e)
        }
        val announcement = LanAnnouncement.parse(String(packet.data, 0, packet.length, Charsets.UTF_8))!!
        assertEquals(ready.gamePort, announcement.port)
        assertTrue(announcement.motd.startsWith("Campfire Room §8· §7Steve · "))
        session.stop()
    }

    @Test
    fun aMissingRoomFailsAfterTheTimeout() = runBlocking {
        val session = session(FakeRuntime(hostPeer = null))
        session.join(RoomCode.create(), options(timeout = 1))
        val failed = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Failed } }
        assertEquals(LinkError.RoomNotFound, failed.error)
    }

    @Test
    fun noNodesIsReportedWhenNothingConnects() = runBlocking {
        val session = session(FakeRuntime(hostPeer = null, nodes = 0))
        session.join(RoomCode.create(), options(timeout = 1))
        val failed = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Failed } }
        assertEquals(LinkError.NoNodes, failed.error)
    }

    @Test
    fun aRelayedHostIsRefusedWhenRelaysAreOff() = runBlocking {
        val session = session(FakeRuntime(hostPeer(40000, direct = false)))
        session.join(RoomCode.create(), options(timeout = 1, relay = false))
        val failed = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Failed } }
        assertEquals(LinkError.NoDirectPath, failed.error)
    }

    @Test
    fun aGuestReconnectsWhenTheRoomServiceComesBack() = runBlocking {
        val world = fakeWorld()
        val host = PlayerProfile("Steve", "a".repeat(32), "Campfire", PlayerKind.Host)
        val first = RoomServer.start(host, world.localPort, preferredPort = 0)
        val port = first.port
        val session = session(FakeRuntime(hostPeer(port)))
        session.join(RoomCode.create(), options())
        withTimeout(10_000) { session.state.first { it.stage == LinkStage.Ready } }

        first.close()
        delay(500)
        val second = RoomServer.start(host, world.localPort, preferredPort = port).also { closeables += it }
        assertEquals(port, second.port)
        withTimeout(10_000) { while (second.players().size < 2) delay(100) }
        assertEquals(LinkStage.Ready, session.state.value.stage)
        assertEquals("Alex", second.players()[1].name)
    }

    @Test
    fun aGuestFailsWhenTheHostLeavesTheNetwork() = runBlocking {
        val world = fakeWorld()
        val host = PlayerProfile("Steve", "a".repeat(32), "Campfire", PlayerKind.Host)
        val server = RoomServer.start(host, world.localPort, preferredPort = 0)
        val runtime = FakeRuntime(hostPeer(server.port))
        val session = session(runtime)
        session.join(RoomCode.create(), options())
        withTimeout(10_000) { session.state.first { it.stage == LinkStage.Ready } }

        runtime.hostPeer = null
        server.close()
        val failed = withTimeout(2_000) { session.state.first { it.stage == LinkStage.Failed } }
        assertEquals(LinkError.HostLost, failed.error)
    }

    @Test
    fun aRemovedGuestCannotJoinAgain() = runBlocking {
        val world = fakeWorld()
        val host = PlayerProfile("Steve", "a".repeat(32), "Campfire", PlayerKind.Host)
        val server = RoomServer.start(host, world.localPort, preferredPort = 0).also { closeables += it }
        server.remove("b".repeat(32), System.currentTimeMillis() + 60_000)
        val session = session(FakeRuntime(hostPeer(server.port)))
        session.join(RoomCode.create(), options())
        val failed = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Failed } }
        assertEquals(LinkError.Removed, failed.error)
    }

    @Test
    fun aRemovedGuestLeavesTheRoom() = runBlocking {
        val world = fakeWorld()
        val host = PlayerProfile("Steve", "a".repeat(32), "Campfire", PlayerKind.Host)
        val server = RoomServer.start(host, world.localPort, preferredPort = 0).also { closeables += it }
        val session = session(FakeRuntime(hostPeer(server.port)))
        session.join(RoomCode.create(), options())
        withTimeout(10_000) { session.state.first { it.stage == LinkStage.Ready } }

        server.remove("b".repeat(32), System.currentTimeMillis() + 60_000)
        val failed = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Failed } }
        assertEquals(LinkError.Removed, failed.error)
    }

    @Test
    fun hostOpensARoomForTheWorldAndTracksGuests() = runBlocking {
        val world = fakeWorld()
        val runtime = FakeRuntime(hostPeer = null)
        val session = session(runtime)
        session.host({ world.localPort }, options(), RoomInfo.of("Island", "", 0), object : top.mccampfire.core.link.room.RoomGate {})

        val ready = withTimeout(20_000) { session.state.first { it.stage == LinkStage.Ready || it.stage == LinkStage.Failed } }
        assertEquals(LinkStage.Ready, ready.stage)
        assertEquals(LinkRole.Host, ready.role)
        val config = runtime.started.single()
        assertEquals(RoomProtocol.HostAddress, config.ipv4)
        assertEquals(ready.code!!.networkName, config.networkName)
        val servicePort = config.hostname.removePrefix(RoomProtocol.HostNamePrefix).toInt()
        assertEquals(listOf(servicePort.toString(), world.localPort.toString()), config.tcpWhitelist)

        val guest = top.mccampfire.core.link.room.RoomClient.tryConnect(DirectConnector, top.mccampfire.core.link.runtime.parseIpv4("127.0.0.1")!!, servicePort)!!
        closeables += guest
        assertEquals(world.localPort, guest.serverPort())
        val profile = PlayerProfile("Alex", "c".repeat(32), "Campfire 1.1.1", PlayerKind.Local)
        guest.heartbeat(profile)
        withTimeout(5_000) { session.state.first { it.players.size == 2 } }

        session.removePlayer(profile.machineId, System.currentTimeMillis() + 60_000)
        withTimeout(5_000) { session.state.first { it.players.size == 1 } }

        world.close()
        val failed = withTimeout(10_000) { session.state.first { it.stage == LinkStage.Failed } }
        assertEquals(LinkError.WorldClosed, failed.error)
        assertEquals(listOf(LinkSession.InstanceName), runtime.stopped)
    }

    @Test
    fun hostingWaitsUntilTheWorldAnswers() = runBlocking {
        val runtime = FakeRuntime(hostPeer = null)
        val session = session(runtime)
        val port = ServerSocket(0).use { it.localPort }
        session.host({ port }, options(), RoomInfo.of("", "", 0), object : top.mccampfire.core.link.room.RoomGate {})
        val waiting = withTimeout(10_000) { session.state.first { it.waitingForWorld } }
        assertEquals(LinkStep.World, waiting.step)
        assertTrue(runtime.started.isEmpty())

        val world = ServerSocket(port, 50, LoopbackV4).also { closeables += it }
        thread(isDaemon = true) {
            while (!world.isClosed) {
                val socket = runCatching { world.accept() }.getOrNull() ?: break
                socket.use { if (it.getInputStream().read() == 0xFE) it.getOutputStream().write(byteArrayOf(0xFF.toByte(), 0, 0)) }
            }
        }
        val ready = withTimeout(20_000) { session.state.first { it.stage == LinkStage.Ready } }
        assertEquals(port, ready.gamePort)
        session.stop()
    }

    @Test
    fun latencyIsFormattedLikeTheDesktop() {
        assertEquals("4.5", LinkSession.formatLatency(4.46))
        assertEquals("25", LinkSession.formatLatency(25.2))
    }
}
