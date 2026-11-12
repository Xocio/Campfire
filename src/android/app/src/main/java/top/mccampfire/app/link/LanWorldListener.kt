package top.mccampfire.app.link

import android.content.Context
import android.net.wifi.WifiManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import top.mccampfire.core.link.minecraft.LanAnnouncement
import java.io.Closeable
import java.io.IOException
import java.net.DatagramPacket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import kotlin.concurrent.thread

/** A LAN world announced on this device. */
data class LanWorld(val port: Int, val motd: String, val seenAtMillis: Long)

/** Listens for the LAN world announcements of games on this device. */
class LanWorldListener(context: Context) : Closeable {
    private val wifi = context.applicationContext.getSystemService(WifiManager::class.java)
    private val lock = wifi?.createMulticastLock("campfire-lan")?.apply { setReferenceCounted(false) }
    private val mutableWorlds = MutableStateFlow<List<LanWorld>>(emptyList())

    @Volatile
    private var socket: MulticastSocket? = null

    /** Worlds announced in the last few seconds, newest first. */
    val worlds: StateFlow<List<LanWorld>> = mutableWorlds.asStateFlow()

    /** Starts listening; does nothing when it already listens. */
    fun start() {
        if (socket != null) return
        runCatching { lock?.acquire() }
        val listener = try {
            MulticastSocket(null).apply {
                reuseAddress = true
                soTimeout = 1_000
                bind(InetSocketAddress(LanAnnouncement.Port))
            }
        } catch (e: IOException) {
            return
        }
        joinGroups(listener)
        socket = listener
        thread(name = "lan-listener", isDaemon = true) { receive(listener) }
    }

    override fun close() {
        socket?.close()
        socket = null
        runCatching { lock?.release() }
        mutableWorlds.value = emptyList()
    }

    private fun joinGroups(listener: MulticastSocket) {
        val group = InetSocketAddress(InetAddress.getByName(LanAnnouncement.Group), LanAnnouncement.Port)
        val interfaces = runCatching { NetworkInterface.getNetworkInterfaces().toList() }.getOrDefault(emptyList())
            .filter { runCatching { it.isUp && it.supportsMulticast() }.getOrDefault(false) }
        for (network in interfaces) runCatching { listener.joinGroup(group, network) }
        runCatching { listener.joinGroup(InetAddress.getByName(LanAnnouncement.Group)) }
    }

    private fun receive(listener: MulticastSocket) {
        val buffer = ByteArray(1024)
        while (socket === listener) {
            try {
                val packet = DatagramPacket(buffer, buffer.size)
                listener.receive(packet)
                val text = String(packet.data, 0, packet.length, Charsets.UTF_8)
                LanAnnouncement.parse(text)?.let { seen(it) }
            } catch (e: SocketTimeoutException) {
                prune()
            } catch (e: IOException) {
                if (socket !== listener) return
            }
        }
    }

    private fun seen(announcement: LanAnnouncement) {
        val now = System.currentTimeMillis()
        val others = mutableWorlds.value.filter { it.port != announcement.port && now - it.seenAtMillis < KeepMillis }
        mutableWorlds.value = listOf(LanWorld(announcement.port, announcement.motd, now)) + others
    }

    private fun prune() {
        val now = System.currentTimeMillis()
        val kept = mutableWorlds.value.filter { now - it.seenAtMillis < KeepMillis }
        if (kept.size != mutableWorlds.value.size) mutableWorlds.value = kept
    }

    private companion object {
        const val KeepMillis = 6_000L
    }
}
