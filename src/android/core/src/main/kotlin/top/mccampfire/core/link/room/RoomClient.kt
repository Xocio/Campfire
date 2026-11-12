package top.mccampfire.core.link.room

import kotlinx.serialization.json.Json
import top.mccampfire.core.link.runtime.Connector
import java.io.Closeable
import java.io.IOException
import java.net.Inet4Address
import java.net.Socket
import java.net.SocketTimeoutException

/** The host turned this player away: removed, blocked, room full or not on the allowlist. */
class RoomRejectedException(val reason: Reason) : IOException(reason.name) {
    enum class Reason { Removed, Blocked, Full, NotAllowed }
}

/** Connection from a joining player to the host's room service. */
class RoomClient private constructor(private val socket: Socket) : Closeable {
    private val input = socket.getInputStream().buffered()
    private val output = socket.getOutputStream()
    private val lock = Any()

    /** The host's game port. */
    fun serverPort(): Int {
        val response = send(RoomProtocol.ServerPort, ByteArray(0), RequestTimeoutMillis)
        if (!response.isOk || response.body.size != 2) {
            throw IOException("The host has no game running (status ${response.status}).")
        }
        return ((response.body[0].toInt() and 0xFF) shl 8) or (response.body[1].toInt() and 0xFF)
    }

    /** Tells the host this player is still here. */
    fun heartbeat(self: PlayerProfile) {
        val body = self.toJson(includeKind = false).toString().toByteArray(Charsets.UTF_8)
        val response = send(RoomProtocol.PlayerPing, body, RequestTimeoutMillis)
        if (response.isOk) return
        throw when (val reply = response.text()) {
            RoomProtocol.Reply.Removed -> RoomRejectedException(RoomRejectedException.Reason.Removed)
            RoomProtocol.Reply.Blocked -> RoomRejectedException(RoomRejectedException.Reason.Blocked)
            RoomProtocol.Reply.Full -> RoomRejectedException(RoomRejectedException.Reason.Full)
            RoomProtocol.Reply.NotAllowed -> RoomRejectedException(RoomRejectedException.Reason.NotAllowed)
            else -> IOException("Heartbeat rejected: $reply")
        }
    }

    /** Everyone in the room as the host sees it. */
    fun players(): List<PlayerProfile> {
        val response = send(RoomProtocol.PlayerList, ByteArray(0), RequestTimeoutMillis)
        if (!response.isOk) throw IOException("Player list rejected.")
        return PlayerProfile.listFromJson(parse(response.text())) ?: throw IOException("Player list is not an array.")
    }

    /** The request kinds the host's room service answers. */
    fun protocols(): List<String> {
        val response = send(RoomProtocol.Protocols, ByteArray(0), RequestTimeoutMillis)
        if (!response.isOk) return emptyList()
        return response.text().split('\u0000').filter { it.isNotEmpty() }
    }

    /** The room's name, announcement and player limit. */
    fun roomInfo(): RoomInfo {
        val response = send(RoomProtocol.Room, ByteArray(0), RequestTimeoutMillis)
        if (!response.isOk) throw IOException("Room request rejected.")
        return RoomInfo.fromJson(parse(response.text())) ?: throw IOException("Room answer is not an object.")
    }

    /** The host's voice chat, or null while the host has it off. */
    fun voice(): VoiceOffer? {
        val response = send(RoomProtocol.Voice, ByteArray(0), RequestTimeoutMillis)
        if (!response.isOk) {
            if (response.text() == RoomProtocol.Reply.VoiceOff) return null
            throw IOException("Voice request rejected.")
        }
        val json = parse(response.text()) as? kotlinx.serialization.json.JsonObject ?: throw IOException("Voice answer is not an object.")
        return VoiceOffer.fromJson(json) ?: throw IOException("Voice answer is malformed.")
    }

    override fun close() {
        runCatching { socket.close() }
    }

    private fun send(kind: String, body: ByteArray, timeoutMillis: Int): RoomFraming.Response = synchronized(lock) {
        try {
            socket.soTimeout = timeoutMillis
            RoomFraming.writeRequest(output, kind, body)
            RoomFraming.readResponse(input)
        } catch (e: SocketTimeoutException) {
            close()
            throw IOException("The host did not answer $kind.", e)
        }
    }

    companion object {
        private const val ConnectTimeoutMillis = 5_000
        private const val PingTimeoutMillis = 8_000
        private const val RequestTimeoutMillis = 64_000

        private val JsonParser = Json { ignoreUnknownKeys = true }

        private fun parse(text: String) = runCatching { JsonParser.parseToJsonElement(text) }.getOrNull()

        /** Connects and confirms that a room service answers; returns null when nothing usable answers yet. */
        fun tryConnect(connector: Connector, address: Inet4Address, port: Int): RoomClient? {
            val socket = try {
                connector.connect(address, port, ConnectTimeoutMillis)
            } catch (e: IOException) {
                return null
            }
            val client = RoomClient(socket)
            return try {
                val response = client.send(RoomProtocol.Ping, RoomProtocol.Fingerprint, PingTimeoutMillis)
                if (response.isOk && response.body.contentEquals(RoomProtocol.Fingerprint)) {
                    client
                } else {
                    client.close()
                    null
                }
            } catch (e: IOException) {
                client.close()
                null
            }
        }
    }
}
