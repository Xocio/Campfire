package top.mccampfire.core.link.minecraft

import top.mccampfire.core.link.runtime.LoopbackV4
import java.io.Closeable
import java.io.DataInputStream
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import kotlin.concurrent.thread

/** The announcement Minecraft sends for an open LAN world: `[MOTD]text[/MOTD][AD]port[/AD]`. */
data class LanAnnouncement(val motd: String, val port: Int) {
    fun format(): String = "[MOTD]$motd[/MOTD][AD]$port[/AD]"

    companion object {
        /** Multicast group of LAN world announcements. */
        const val Group = "224.0.2.60"

        /** UDP port of LAN world announcements. */
        const val Port = 4445

        private val Pattern = Regex("\\[MOTD](.*?)\\[/MOTD]\\[AD](\\d{1,5})\\[/AD]", RegexOption.DOT_MATCHES_ALL)

        /** Reads an announcement; returns null for anything else. */
        fun parse(text: String): LanAnnouncement? {
            val match = Pattern.find(text) ?: return null
            val port = match.groupValues[2].toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
            return LanAnnouncement(match.groupValues[1], port)
        }
    }
}

/**
 * Makes a room appear under the LAN worlds of the game on this device by sending the announcement to the
 * loopback address every 1.5 seconds.
 */
class LanAnnouncer(private val motd: () -> String, private val port: Int) : Closeable {
    @Volatile
    private var closed = false
    private val socket = DatagramSocket()
    private val target = InetSocketAddress(LoopbackV4, LanAnnouncement.Port)

    init {
        thread(name = "lan-announcer", isDaemon = true) { run() }
    }

    private fun run() {
        while (!closed) {
            val bytes = LanAnnouncement(motd(), port).format().toByteArray(Charsets.UTF_8)
            try {
                socket.send(DatagramPacket(bytes, bytes.size, target))
            } catch (e: IOException) {
                if (closed) return
            }
            try {
                Thread.sleep(IntervalMillis)
            } catch (e: InterruptedException) {
                return
            }
        }
    }

    override fun close() {
        closed = true
        socket.close()
    }

    private companion object {
        const val IntervalMillis = 1_500L
    }
}

/** What a world says about itself in the server list. */
data class ServerStatus(val version: String, val message: String, val maxPlayers: Int) {
    /** The world name of a LAN world, or the whole message for anything else, without color codes. */
    val worldName: String
        get() {
            val text = message.replace(Regex("§.?"), "")
            val dash = text.indexOf(" - ")
            return (if (dash >= 0) text.substring(dash + 3) else text).trim()
        }
}

/** Legacy server list pings, answered by every Java Edition server. */
object MinecraftProbe {
    /** True when a Java Edition server answers on [port] of [address] within [timeoutMillis]. */
    fun isServer(port: Int, timeoutMillis: Int, address: InetAddress = LoopbackV4): Boolean =
        runCatching {
            Socket().use { socket ->
                socket.connect(InetSocketAddress(address, port), timeoutMillis)
                socket.soTimeout = timeoutMillis
                socket.getOutputStream().apply { write(0xFE); flush() }
                socket.getInputStream().read() == 0xFF
            }
        }.getOrDefault(false)

    /** Version, message and player limit from the legacy ping in either reply format; null when there is no answer. */
    fun status(port: Int, timeoutMillis: Int, address: InetAddress = LoopbackV4): ServerStatus? =
        runCatching {
            Socket().use { socket ->
                socket.connect(InetSocketAddress(address, port), timeoutMillis)
                socket.soTimeout = timeoutMillis
                socket.getOutputStream().apply { write(byteArrayOf(0xFE.toByte(), 0x01)); flush() }
                socket.shutdownOutput()
                val input = DataInputStream(socket.getInputStream())
                if (input.readUnsignedByte() != 0xFF) return@use null
                val length = input.readUnsignedShort()
                val body = ByteArray(length * 2).also(input::readFully)
                val text = String(body, Charsets.UTF_16BE)
                val parts = text.split('\u0000')
                if (parts.size >= 6 && parts[0] == "§1") return@use ServerStatus(parts[2], parts[3], parts[5].toIntOrNull() ?: 0)
                val old = text.split('§')
                if (old.size >= 3) ServerStatus("", old.dropLast(2).joinToString("§"), old.last().toIntOrNull() ?: 0) else null
            }
        }.getOrNull()
}
