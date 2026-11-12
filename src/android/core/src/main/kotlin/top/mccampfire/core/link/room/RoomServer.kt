package top.mccampfire.core.link.room

import top.mccampfire.core.link.runtime.LoopbackV4
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import java.io.Closeable
import java.io.IOException
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketTimeoutException
import kotlin.concurrent.thread

/** Host-side checks on a joining player. */
interface RoomGate {
    /** True when the player is on the host's blocklist. */
    fun isBlocked(player: PlayerProfile): Boolean = false

    /** True when the player is on the host's allowlist. */
    fun isAllowed(player: PlayerProfile): Boolean = false
}

/** The host's room service: answers each player's requests and keeps the player list. */
class RoomServer private constructor(
    private val host: PlayerProfile,
    private val listener: ServerSocket,
    private val gate: RoomGate,
    private val clock: () -> Long,
) : Closeable {
    private val guests = mutableListOf<Guest>()
    private val removed = mutableMapOf<String, Long>()
    private val connections = mutableSetOf<Socket>()

    @Volatile
    private var closed = false

    @Volatile
    private var roomJson: ByteArray = RoomInfo.of(null, null, 0).toJson().toString().toByteArray(Charsets.UTF_8)

    /** Port the game connections go to: the gate in front of the world, or the world itself. */
    @Volatile
    var gamePort: Int = 0

    /** Whether only players on the allowlist may join. */
    @Volatile
    var allowlistOnly: Boolean = false

    @Volatile
    private var voiceJson: ByteArray? = null

    /** The host's voice chat as players receive it, or null while it is off. */
    @Volatile
    var voice: VoiceOffer? = null
        set(value) {
            field = value
            voiceJson = value?.toJson()?.toString()?.toByteArray(Charsets.UTF_8)
        }

    /** The room's name, announcement and player limit. */
    @Volatile
    var room: RoomInfo = RoomInfo.of(null, null, 0)
        set(value) {
            field = value
            roomJson = value.toJson().toString().toByteArray(Charsets.UTF_8)
        }

    /** Runs on a worker thread when a player joins, leaves or renames. */
    @Volatile
    var onPlayersChanged: () -> Unit = {}

    /** Local port of the room service. */
    val port: Int get() = listener.localPort

    /** The host followed by every guest, in joining order. */
    fun players(): List<PlayerProfile> = synchronized(guests) { listOf(host) + guests.map { it.profile } }

    /** Drops guests that have not checked in for a while. */
    fun removeSilentGuests() {
        val now = clock()
        val changed = synchronized(guests) { guests.removeAll { now - it.lastSeen > GuestTimeoutMillis } }
        if (changed) onPlayersChanged()
    }

    /** Takes a guest out of the room; the device is turned away until [untilMillis]. */
    fun remove(machineId: String, untilMillis: Long) {
        synchronized(guests) {
            removed[machineId] = untilMillis
            guests.removeAll { it.profile.machineId == machineId }
        }
        onPlayersChanged()
    }

    override fun close() {
        closed = true
        runCatching { listener.close() }
        synchronized(connections) {
            connections.forEach { runCatching { it.close() } }
            connections.clear()
        }
    }

    private fun acceptLoop() {
        while (!closed) {
            val socket = try {
                listener.accept()
            } catch (e: IOException) {
                return
            }
            synchronized(connections) { connections += socket }
            thread(name = "room-service", isDaemon = true) { serve(socket) }
        }
    }

    private fun serve(socket: Socket) {
        try {
            socket.use {
                it.tcpNoDelay = true
                it.soTimeout = IdleTimeoutMillis
                val input = it.getInputStream().buffered()
                val output = it.getOutputStream()
                while (!closed) {
                    val request = RoomFraming.readRequest(input) ?: return
                    val (status, body) = handle(request)
                    RoomFraming.writeResponse(output, status, body)
                }
            }
        } catch (e: SocketTimeoutException) {
            return
        } catch (e: IOException) {
            return
        } finally {
            synchronized(connections) { connections -= socket }
        }
    }

    /** Answers one request. */
    internal fun handle(request: RoomFraming.Request): Pair<Int, ByteArray> = when (request.kind) {
        RoomProtocol.Ping -> RoomFraming.StatusOk to request.body
        RoomProtocol.Protocols -> RoomFraming.StatusOk to ProtocolList
        RoomProtocol.ServerPort -> RoomFraming.StatusOk to byteArrayOf((gamePort ushr 8).toByte(), gamePort.toByte())
        RoomProtocol.PlayerPing -> handlePlayerPing(request.body)
        RoomProtocol.PlayerList -> RoomFraming.StatusOk to serializePlayers()
        RoomProtocol.ModList -> reject(RoomProtocol.Reply.ModsOff)
        RoomProtocol.ModFile -> reject(RoomProtocol.Reply.ModFileRefused)
        RoomProtocol.Room -> RoomFraming.StatusOk to roomJson
        RoomProtocol.Voice -> voiceJson?.let { RoomFraming.StatusOk to it } ?: reject(RoomProtocol.Reply.VoiceOff)
        else -> reject(RoomProtocol.Reply.NotImplemented)
    }

    private fun handlePlayerPing(body: ByteArray): Pair<Int, ByteArray> {
        val json = runCatching { JsonParser.parseToJsonElement(body.toString(Charsets.UTF_8)) }.getOrNull()
        val profile = PlayerProfile.fromJson(json, PlayerKind.Guest) ?: return reject("Invalid player profile.")
        if (profile.machineId == host.machineId) return reject("The machine id belongs to the host.")
        if (gate.isBlocked(profile)) return reject(RoomProtocol.Reply.Blocked)
        if (allowlistOnly && !gate.isAllowed(profile)) return reject(RoomProtocol.Reply.NotAllowed)

        val now = clock()
        val changed: Boolean
        synchronized(guests) {
            val until = removed[profile.machineId]
            if (until != null && until > now) return reject(RoomProtocol.Reply.Removed)
            val index = guests.indexOfFirst { it.profile.machineId == profile.machineId }
            val limit = room.maxPlayers
            if (index < 0 && limit > 0 && guests.size + 1 >= limit) return reject(RoomProtocol.Reply.Full)
            if (index < 0) {
                guests += Guest(profile, now)
                changed = true
            } else {
                changed = guests[index].profile.name != profile.name
                guests[index] = Guest(profile, now)
            }
        }
        if (changed) onPlayersChanged()
        return RoomFraming.StatusOk to ByteArray(0)
    }

    private fun serializePlayers(): ByteArray =
        JsonArray(players().map { it.toJson(includeKind = true) }).toString().toByteArray(Charsets.UTF_8)

    private fun reject(text: String): Pair<Int, ByteArray> = RoomFraming.StatusError to text.toByteArray(Charsets.UTF_8)

    private class Guest(val profile: PlayerProfile, val lastSeen: Long)

    companion object {
        const val PreferredPort = 13448
        private const val GuestTimeoutMillis = 10_000L
        private const val IdleTimeoutMillis = 64_000

        private val JsonParser = Json { ignoreUnknownKeys = true }

        private val ProtocolList = listOf(
            RoomProtocol.Ping, RoomProtocol.Protocols, RoomProtocol.ServerPort, RoomProtocol.PlayerPing,
            RoomProtocol.PlayerList, RoomProtocol.ModList, RoomProtocol.ModFile, RoomProtocol.Voice, RoomProtocol.Room,
        ).joinToString("\u0000").toByteArray(Charsets.UTF_8)

        /** Starts the service on the loopback address, on [PreferredPort] when it is free. */
        fun start(
            host: PlayerProfile,
            gamePort: Int,
            gate: RoomGate = object : RoomGate {},
            clock: () -> Long = System::currentTimeMillis,
            preferredPort: Int = PreferredPort,
        ): RoomServer {
            val loopback = LoopbackV4
            val listener = try {
                ServerSocket().apply { bind(InetSocketAddress(loopback, preferredPort)) }
            } catch (e: IOException) {
                ServerSocket().apply { bind(InetSocketAddress(loopback, 0)) }
            }
            val server = RoomServer(host, listener, gate, clock)
            server.gamePort = gamePort
            thread(name = "room-accept", isDaemon = true) { server.acceptLoop() }
            return server
        }
    }
}
