package top.mccampfire.core.link.room

import kotlinx.serialization.json.Json
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import top.mccampfire.core.link.runtime.DirectConnector
import top.mccampfire.core.link.runtime.parseIpv4
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import java.net.ServerSocket

class RoomServiceTest {
    private val loopback = parseIpv4("127.0.0.1")!!
    private val host = PlayerProfile("Host", "a".repeat(32), "Campfire", PlayerKind.Host)
    private var now = 1_000L
    private val blocked = mutableSetOf<String>()
    private val allowed = mutableSetOf<String>()
    private val gate = object : RoomGate {
        override fun isBlocked(player: PlayerProfile) = player.machineId in blocked
        override fun isAllowed(player: PlayerProfile) = player.machineId in allowed
    }
    private val server = RoomServer.start(host, gamePort = 25565, gate = gate, clock = { now }, preferredPort = 0)
    private val clients = mutableListOf<RoomClient>()

    @After
    fun close() {
        clients.forEach { it.close() }
        server.close()
    }

    private fun connect(): RoomClient = RoomClient.tryConnect(DirectConnector, loopback, server.port)!!.also { clients += it }

    private fun guest(id: Char, name: String = "Guest$id") = PlayerProfile(name, id.toString().repeat(32), "Campfire Android", PlayerKind.Local)

    @Test
    fun framesRoundTrip() {
        val buffer = ByteArrayOutputStream()
        RoomFraming.writeRequest(buffer, "c:ping", byteArrayOf(1, 2, 3))
        val request = RoomFraming.readRequest(ByteArrayInputStream(buffer.toByteArray()))!!
        assertEquals("c:ping", request.kind)
        assertTrue(request.body.contentEquals(byteArrayOf(1, 2, 3)))
        assertNull(RoomFraming.readRequest(ByteArrayInputStream(ByteArray(0))))
    }

    @Test(expected = IOException::class)
    fun oversizedBodiesAreRejected() {
        val frame = byteArrayOf(0, 0x7F, 0, 0, 0)
        RoomFraming.readResponse(ByteArrayInputStream(frame))
    }

    @Test(expected = EOFException::class)
    fun truncatedResponsesFail() {
        RoomFraming.readResponse(ByteArrayInputStream(byteArrayOf(0, 0, 0, 0, 4, 1)))
    }

    @Test
    fun guestJoinsAndSeesThePlayerList() {
        val client = connect()
        assertEquals(25565, client.serverPort())
        client.heartbeat(guest('b'))
        val players = client.players()
        assertEquals(listOf("Host", "Guestb"), players.map { it.name })
        assertEquals(listOf(PlayerKind.Host, PlayerKind.Guest), players.map { it.kind })
        assertTrue(RoomProtocol.Room in client.protocols())
        assertEquals(9, client.protocols().size)
    }

    @Test
    fun roomInfoIsSharedAndCleaned() {
        server.room = RoomInfo.of("  Weekend\u0007 world ", "x".repeat(300), 8)
        val info = connect().roomInfo()
        assertEquals("Weekend  world", info.name)
        assertEquals(RoomInfo.MaxNotice, info.notice.length)
        assertEquals(8, info.maxPlayers)
    }

    @Test
    fun fullRoomsTurnNewPlayersAway() {
        server.room = RoomInfo.of("Room", "", 2)
        val client = connect()
        client.heartbeat(guest('b'))
        expectRejected(RoomRejectedException.Reason.Full) { client.heartbeat(guest('c')) }
        client.heartbeat(guest('b', "Renamed"))
    }

    @Test
    fun removedBlockedAndAllowlistRepliesReachTheGuest() {
        val client = connect()
        client.heartbeat(guest('b'))
        server.remove("b".repeat(32), now + 60_000)
        expectRejected(RoomRejectedException.Reason.Removed) { client.heartbeat(guest('b')) }
        now += 61_000
        client.heartbeat(guest('b'))

        blocked += "c".repeat(32)
        expectRejected(RoomRejectedException.Reason.Blocked) { client.heartbeat(guest('c')) }

        server.allowlistOnly = true
        expectRejected(RoomRejectedException.Reason.NotAllowed) { client.heartbeat(guest('d')) }
        allowed += "d".repeat(32)
        client.heartbeat(guest('d'))
    }

    @Test
    fun silentGuestsAreDropped() {
        val client = connect()
        client.heartbeat(guest('b'))
        now += 11_000
        server.removeSilentGuests()
        assertEquals(1, client.players().size)
    }

    @Test
    fun theHostsOwnMachineIdIsRefused() {
        val client = connect()
        try {
            client.heartbeat(host.copy(kind = PlayerKind.Local))
            fail("heartbeat accepted")
        } catch (e: IOException) {
            assertTrue(e.message!!.contains("belongs to the host"))
        }
    }

    @Test
    fun anythingButARoomServiceIsIgnored() {
        ServerSocket(0).use { other ->
            Thread { runCatching { other.accept().use { it.getOutputStream().write(byteArrayOf(0, 0, 0, 0, 1, 9)) } } }.start()
            assertNull(RoomClient.tryConnect(DirectConnector, loopback, other.localPort))
        }
    }

    @Test
    fun profilesKeepTheirFieldsAndDropInvalidValues() {
        val profile = PlayerProfile("A", "b".repeat(32), "Campfire", PlayerKind.Host, uuid = "c".repeat(32), gameName = "Steve",
            latencyMs = 12.5, path = PlayerProfile.PathRelay, lossPercent = 1.5)
        val json = profile.toJson(includeKind = true)
        assertEquals(setOf("name", "machine_id", "vendor", "kind", "uuid", "game_name", "latency_ms", "path", "loss"), json.keys)
        assertEquals(profile, PlayerProfile.fromJson(json, null))

        val raw = Json.parseToJsonElement(
            """{"name":"A","machine_id":"x","vendor":"v","kind":"GUEST","uuid":"zz","game_name":"${"n".repeat(17)}",
               "latency_ms":70000,"path":"other","loss":101}""",
        )
        val cleaned = PlayerProfile.fromJson(raw, null)!!
        assertNull(cleaned.uuid)
        assertNull(cleaned.gameName)
        assertNull(cleaned.latencyMs)
        assertNull(cleaned.path)
        assertNull(cleaned.lossPercent)
        assertNull(PlayerProfile.fromJson(Json.parseToJsonElement("""{"name":"A","machine_id":"x","kind":"OTHER"}"""), null))
        assertNotNull(PlayerProfile.fromJson(Json.parseToJsonElement("""{"name":"A","machine_id":"x"}"""), PlayerKind.Guest))
    }

    private fun expectRejected(reason: RoomRejectedException.Reason, block: () -> Unit) {
        try {
            block()
            fail("expected $reason")
        } catch (e: RoomRejectedException) {
            assertEquals(reason, e.reason)
        }
    }
}
