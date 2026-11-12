package top.mccampfire.fakegame

import android.app.Activity
import android.net.wifi.WifiManager
import android.os.Bundle
import android.util.Log
import android.widget.ScrollView
import android.widget.TextView
import top.mccampfire.core.link.minecraft.LanAnnouncement
import top.mccampfire.core.link.minecraft.MinecraftProbe
import java.net.DatagramPacket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.ServerSocket
import kotlin.concurrent.thread

/**
 * Stands in for the game of a launcher. Mode "guest" lists LAN entries and pings each one through its port;
 * mode "host" opens a world that answers pings and announces it to the LAN group.
 */
class MainActivity : Activity() {
    private lateinit var output: TextView
    @Volatile
    private var running = true

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        output = TextView(this).apply { setPadding(32, 96, 32, 32) }
        setContentView(ScrollView(this).apply { addView(output) })
        intent.getStringExtra("clip")?.let { text ->
            getSystemService(android.content.ClipboardManager::class.java)
                ?.setPrimaryClip(android.content.ClipData.newPlainText("text", text))
            report("clip: copied")
        }
        when (intent.getStringExtra("mode") ?: "guest") {
            "host" -> host()
            else -> guest()
        }
    }

    override fun onDestroy() {
        running = false
        super.onDestroy()
    }

    private fun report(text: String) {
        Log.i(Tag, text)
        runOnUiThread { output.append(text + "\n") }
    }

    private fun guest() {
        report("guest: listening on ${LanAnnouncement.Port}")
        thread(isDaemon = true) {
            val seen = mutableSetOf<Int>()
            MulticastSocket(null).use { socket ->
                socket.reuseAddress = true
                socket.bind(InetSocketAddress(LanAnnouncement.Port))
                runCatching { socket.joinGroup(InetAddress.getByName(LanAnnouncement.Group)) }
                    .onFailure { report("guest: joinGroup failed: ${it.message}") }
                val buffer = ByteArray(1024)
                while (running) {
                    val packet = DatagramPacket(buffer, buffer.size)
                    socket.receive(packet)
                    val text = String(packet.data, 0, packet.length, Charsets.UTF_8)
                    val entry = LanAnnouncement.parse(text) ?: continue
                    if (seen.add(entry.port)) {
                        report("guest: entry '${entry.motd}' port ${entry.port} from ${packet.address.hostAddress}")
                        thread(isDaemon = true) {
                            val status = MinecraftProbe.status(entry.port, 8_000)
                            report("guest: ping port ${entry.port} -> ${status ?: "no answer"}")
                        }
                    }
                }
            }
        }
    }

    private fun host() {
        val lock = getSystemService(WifiManager::class.java)?.createMulticastLock("fakegame")?.apply { acquire() }
        val server = ServerSocket(0)
        report("host: world on port ${server.localPort}")
        thread(isDaemon = true) {
            while (running) {
                val socket = runCatching { server.accept() }.getOrNull() ?: break
                thread(isDaemon = true) {
                    socket.use {
                        val input = it.getInputStream()
                        val first = input.read()
                        if (first != 0xFE) return@use
                        it.soTimeout = 300
                        val second = runCatching { input.read() }.getOrDefault(-1)
                        val text = "§1\u0000127\u00001.20.1\u0000Fake - Test World\u00001\u00008"
                        val body = if (second == 1) text.toByteArray(Charsets.UTF_16BE) else ByteArray(0)
                        val length = if (second == 1) text.length else 0
                        it.getOutputStream().write(byteArrayOf(0xFF.toByte(), (length shr 8).toByte(), length.toByte()) + body)
                        report("host: answered a ping")
                    }
                }
            }
        }
        thread(isDaemon = true) {
            MulticastSocket().use { socket ->
                val group = InetAddress.getByName(LanAnnouncement.Group)
                val bytes = LanAnnouncement("Fake - Test World", server.localPort).format().toByteArray(Charsets.UTF_8)
                while (running) {
                    runCatching { socket.send(DatagramPacket(bytes, bytes.size, group, LanAnnouncement.Port)) }
                        .onFailure { report("host: announce failed: ${it.message}") }
                    Thread.sleep(1_500)
                }
            }
            lock?.release()
        }
    }

    private companion object {
        const val Tag = "FakeGame"
    }
}
