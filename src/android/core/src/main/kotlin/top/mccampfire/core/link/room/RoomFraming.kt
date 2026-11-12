package top.mccampfire.core.link.room

import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

/** Request kinds of the room service and fixed values both sides must agree on. */
object RoomProtocol {
    /** Echoes the body; used with [Fingerprint] to confirm the service is a room. */
    const val Ping = "c:ping"

    /** Lists supported kinds, separated by NUL characters. */
    const val Protocols = "c:protocols"

    /** Returns the host's game port as two big-endian bytes. */
    const val ServerPort = "c:server_port"

    /** A guest's heartbeat with its profile as JSON. */
    const val PlayerPing = "c:player_ping"

    /** Returns every player in the room as a JSON array. */
    const val PlayerList = "c:player_profiles_list"

    /** The game version, loader and mods of the host's game as JSON. */
    const val ModList = "campfire:mods"

    /** A piece of one of the host's mods. */
    const val ModFile = "campfire:mod_file"

    /** The host's voice chat as JSON. */
    const val Voice = "campfire:voice"

    /** The room's name, announcement and player limit as JSON. */
    const val Room = "campfire:room"

    /** Virtual host name prefix of a room's host; the room service port follows it. */
    const val HostNamePrefix = "scaffolding-mc-server-"

    /** Virtual address of the host in its room network. */
    const val HostAddress = "10.144.144.1"

    /** Body of [Ping] that identifies a room service. */
    val Fingerprint: ByteArray = byteArrayOf(
        0x41, 0x57, 0x48, 0x44, 0x86.toByte(), 0x37, 0x40, 0x59, 0x57, 0x44, 0x92.toByte(), 0x43, 0x96.toByte(),
        0x99.toByte(), 0x85.toByte(), 0x01,
    )

    /** Error bodies of a host. */
    object Reply {
        const val Removed = "campfire:removed"
        const val Blocked = "campfire:blocked"
        const val NotAllowed = "campfire:not_allowed"
        const val Full = "campfire:full"
        const val ModsPending = "campfire:mods_pending"
        const val ModsUnavailable = "campfire:mods_unavailable"
        const val ModsOff = "campfire:mods_off"
        const val ModFileRefused = "campfire:mod_file_refused"
        const val VoiceOff = "campfire:voice_off"
        const val NotImplemented = "Requested protocol hasn't been implemented."
    }
}

/**
 * Message framing of the room service, which runs over one TCP connection per player.
 *
 * Request: kind length (1 byte), kind (UTF-8), body length (4 bytes, big-endian), body.
 * Response: status (1 byte, 0 = success), body length (4 bytes, big-endian), body.
 */
object RoomFraming {
    const val StatusOk: Int = 0
    const val StatusNotReady: Int = 32
    const val StatusError: Int = 255

    /** Largest body accepted. */
    const val MaxBodySize: Int = 1024 * 1024

    /** One request. */
    class Request(val kind: String, val body: ByteArray)

    /** One response. */
    class Response(val status: Int, val body: ByteArray) {
        val isOk: Boolean get() = status == StatusOk

        /** The body as UTF-8 text. */
        fun text(): String = body.toString(Charsets.UTF_8)
    }

    fun writeRequest(output: OutputStream, kind: String, body: ByteArray = ByteArray(0)) {
        val kindBytes = kind.toByteArray(Charsets.UTF_8)
        require(kindBytes.size <= 255) { "Request kind is too long." }
        val frame = ByteArray(1 + kindBytes.size + 4 + body.size)
        frame[0] = kindBytes.size.toByte()
        kindBytes.copyInto(frame, 1)
        writeInt(frame, 1 + kindBytes.size, body.size)
        body.copyInto(frame, 5 + kindBytes.size)
        output.write(frame)
        output.flush()
    }

    /** Reads one request; returns null when the peer closed the connection cleanly. */
    fun readRequest(input: InputStream): Request? {
        val size = input.read()
        if (size < 0) return null
        val data = DataInputStream(input)
        val kind = ByteArray(size).also { readFully(data, it) }
        return Request(kind.toString(Charsets.UTF_8), readBody(data))
    }

    fun writeResponse(output: OutputStream, status: Int, body: ByteArray = ByteArray(0)) {
        val frame = ByteArray(5 + body.size)
        frame[0] = status.toByte()
        writeInt(frame, 1, body.size)
        body.copyInto(frame, 5)
        output.write(frame)
        output.flush()
    }

    fun readResponse(input: InputStream): Response {
        val data = DataInputStream(input)
        val status = data.read()
        if (status < 0) throw EOFException("The connection was closed.")
        return Response(status, readBody(data))
    }

    private fun readBody(data: DataInputStream): ByteArray {
        val length = data.readInt()
        if (length < 0 || length > MaxBodySize) throw IOException("Message body is too large.")
        return ByteArray(length).also { readFully(data, it) }
    }

    private fun readFully(data: DataInputStream, buffer: ByteArray) {
        try {
            data.readFully(buffer)
        } catch (e: EOFException) {
            throw EOFException("The connection was closed.")
        }
    }

    private fun writeInt(buffer: ByteArray, offset: Int, value: Int) {
        buffer[offset] = (value ushr 24).toByte()
        buffer[offset + 1] = (value ushr 16).toByte()
        buffer[offset + 2] = (value ushr 8).toByte()
        buffer[offset + 3] = value.toByte()
    }
}
