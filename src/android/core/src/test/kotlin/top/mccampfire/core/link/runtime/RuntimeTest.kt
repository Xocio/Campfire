package top.mccampfire.core.link.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import top.mccampfire.core.link.minecraft.LanAnnouncement
import top.mccampfire.core.link.minecraft.MinecraftProbe
import top.mccampfire.core.link.minecraft.ServerStatus
import java.io.DataInputStream
import java.io.IOException
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import kotlin.concurrent.thread

class RuntimeTest {
    private val loopback = parseIpv4("127.0.0.1")!!

    /** Echo server that answers every connection until closed. */
    private fun echoServer(): ServerSocket = ServerSocket(0, 50, LoopbackV4).also { server ->
        thread(isDaemon = true) {
            while (!server.isClosed) {
                val socket = runCatching { server.accept() }.getOrNull() ?: break
                thread(isDaemon = true) { socket.use { s -> s.getInputStream().copyTo(s.getOutputStream()) } }
            }
        }
    }

    /** Minimal SOCKS5 server that relays CONNECT requests, or refuses them with [refuse]. */
    private fun socksServer(refuse: Int = 0, seen: MutableList<String> = mutableListOf()): ServerSocket =
        ServerSocket(0, 50, LoopbackV4).also { server ->
            thread(isDaemon = true) {
                while (!server.isClosed) {
                    val client = runCatching { server.accept() }.getOrNull() ?: break
                    thread(isDaemon = true) {
                        client.use {
                            val input = DataInputStream(it.getInputStream())
                            val output = it.getOutputStream()
                            input.readUnsignedByte()
                            val methods = input.readUnsignedByte()
                            input.skipBytes(methods)
                            output.write(byteArrayOf(5, 0))
                            val head = ByteArray(4).also(input::readFully)
                            assertEquals(1, head[3].toInt())
                            val address = ByteArray(4).also(input::readFully)
                            val port = input.readUnsignedShort()
                            seen += InetAddress.getByAddress(address).hostAddress + ":" + port
                            output.write(byteArrayOf(5, refuse.toByte(), 0, 1, 0, 0, 0, 0, 0, 0))
                            if (refuse != 0) return@thread
                            Socket(InetAddress.getByAddress(address), port).use { target ->
                                val up = thread(isDaemon = true) { runCatching { input.copyTo(target.getOutputStream()); target.shutdownOutput() } }
                                runCatching { target.getInputStream().copyTo(output) }
                                up.join(2_000)
                            }
                        }
                    }
                }
            }
        }

    @Test
    fun socksConnectionsReachTheTarget() {
        echoServer().use { echo ->
            val seen = mutableListOf<String>()
            socksServer(seen = seen).use { socks ->
                Socks5Connector(socks.localPort).connect(loopback, echo.localPort, 2_000).use { socket ->
                    socket.getOutputStream().write("hello".toByteArray())
                    val reply = ByteArray(5).also { DataInputStream(socket.getInputStream()).readFully(it) }
                    assertEquals("hello", String(reply))
                }
                assertEquals(listOf("127.0.0.1:${echo.localPort}"), seen)
            }
        }
    }

    @Test(expected = IOException::class)
    fun refusedSocksRequestsFail() {
        socksServer(refuse = 5).use { socks -> Socks5Connector(socks.localPort).connect(loopback, 9, 2_000) }
    }

    @Test
    fun forwarderCarriesDataThroughSocks() {
        echoServer().use { echo ->
            socksServer().use { socks ->
                TcpForwarder.start(0, Socks5Connector(socks.localPort), loopback, echo.localPort).use { forwarder ->
                    Socket(LoopbackV4, forwarder.port).use { socket ->
                        val payload = ByteArray(200_000) { (it % 251).toByte() }
                        thread(isDaemon = true) { socket.getOutputStream().write(payload) }
                        val back = ByteArray(payload.size).also { DataInputStream(socket.getInputStream()).readFully(it) }
                        assertTrue(payload.contentEquals(back))
                    }
                }
            }
        }
    }

    @Test
    fun portChecks() {
        ServerSocket(0, 50, LoopbackV4).use { busy ->
            assertFalse(TcpForwarder.isPortFree(busy.localPort))
        }
        assertTrue(TcpForwarder.freePort() in 1..65535)
    }

    @Test
    fun ipv4Parsing() {
        assertEquals("10.144.144.1", parseIpv4("10.144.144.1")!!.hostAddress)
        assertNull(parseIpv4("10.144.144"))
        assertNull(parseIpv4("10.144.144.256"))
        assertNull(parseIpv4("a.b.c.d"))
    }

    @Test
    fun lanAnnouncementsRoundTrip() {
        val text = LanAnnouncement("§6Campfire Room §8· §72 players", 51732).format()
        assertEquals("[MOTD]§6Campfire Room §8· §72 players[/MOTD][AD]51732[/AD]", text)
        assertEquals(LanAnnouncement("§6Campfire Room §8· §72 players", 51732), LanAnnouncement.parse(text))
        assertNull(LanAnnouncement.parse("[MOTD]x[/MOTD][AD]70000[/AD]"))
        assertNull(LanAnnouncement.parse("hello"))
    }

    @Test
    fun legacyPingReadsTheServerStatus() {
        ServerSocket(0, 50, LoopbackV4).use { server ->
            thread(isDaemon = true) {
                repeat(2) {
                    server.accept().use { socket ->
                        val input = socket.getInputStream()
                        input.read()
                        if (input.available() > 0 || runCatching { socket.soTimeout = 200; input.read() }.getOrDefault(-1) == 1) {
                            val text = "§1\u0000127\u00001.20.1\u0000Steve - Island\u00001\u00008"
                            val body = text.toByteArray(Charsets.UTF_16BE)
                            socket.getOutputStream().write(byteArrayOf(0xFF.toByte(), 0, text.length.toByte()) + body)
                        } else {
                            socket.getOutputStream().write(byteArrayOf(0xFF.toByte(), 0, 0))
                        }
                    }
                }
            }
            assertTrue(MinecraftProbe.isServer(server.localPort, 2_000))
            val status = MinecraftProbe.status(server.localPort, 2_000)
            assertEquals(ServerStatus("1.20.1", "Steve - Island", 8), status)
            assertEquals("Island", status!!.worldName)
        }
        assertFalse(MinecraftProbe.isServer(TcpForwarder.freePort(), 500))

        ServerSocket(0, 50, LoopbackV4).use { server ->
            thread(isDaemon = true) {
                server.accept().use { socket ->
                    socket.getInputStream().read()
                    val text = "Steve - Old World§1§8"
                    socket.getOutputStream().write(byteArrayOf(0xFF.toByte(), 0, text.length.toByte()) + text.toByteArray(Charsets.UTF_16BE))
                }
            }
            val old = MinecraftProbe.status(server.localPort, 2_000)
            assertEquals(ServerStatus("", "Steve - Old World", 8), old)
            assertEquals("Old World", old!!.worldName)
        }
    }

    @Test
    fun runningInformationIsRead() {
        val json = """
            {"map":{"campfire-room":{
              "running":true,
              "my_node_info":{"peer_id":1,"hostname":"campfire-0a1b2c3d","virtual_ipv4":{"address":{"addr":177246210},"network_length":24},
                              "stun_info":{"udp_nat_type":3}},
              "routes":[
                {"peer_id":2,"hostname":"scaffolding-mc-server-13448","ipv4_addr":{"address":{"addr":177246209},"network_length":24},
                 "cost":1,"path_latency":40,"stun_info":{"udp_nat_type":"Symmetric"}},
                {"peer_id":3,"hostname":"campfire-ffff0000","ipv4_addr":"10.144.144.3/24","cost":2,"path_latency":90},
                {"peer_id":9,"hostname":"PublicServer_node","ipv4_addr":null,"cost":1,"path_latency":40,
                 "feature_flag":{"is_public_server":true}},
                {"peer_id":7,"hostname":"PublicServer_other","ipv4_addr":null,"cost":1,"path_latency":40,
                 "feature_flag":{"is_public_server":true}}
              ],
              "peers":[
                {"peer_id":2,"conns":[{"tunnel":{"tunnel_type":"udp"},"stats":{"latency_us":12500},"loss_rate":0.02,"is_closed":false}]},
                {"peer_id":9,"conns":[{"tunnel":{"tunnel_type":"tcp"},"stats":{"latency_us":30000},"loss_rate":0}]},
                {"peer_id":8,"conns":[{"is_closed":true}]}
              ]}}}
        """.trimIndent()
        val info = NetworkInfoParser.parse(json, "campfire-room")!!
        assertTrue(info.running)
        assertEquals(1, info.connectedNodes)
        assertEquals(30.0, info.nodeLatencyMs!!, 0.001)
        val local = info.local!!
        assertEquals("10.144.144.2", local.ipv4)
        assertEquals(NatKind.FullCone, local.nat)
        val host = info.peers.first { it.hostname.startsWith("scaffolding") }
        assertEquals("10.144.144.1", host.ipv4)
        assertTrue(host.isDirect)
        assertEquals(12.5, host.latencyMs!!, 0.001)
        assertEquals(2.0, host.lossPercent!!, 0.001)
        assertEquals("udp", host.tunnel)
        assertEquals(NatKind.Symmetric, host.nat)
        val relayed = info.peers.first { it.hostname == "campfire-ffff0000" }
        assertFalse(relayed.isDirect)
        assertEquals("10.144.144.3", relayed.ipv4)
        assertEquals(90.0, relayed.latencyMs!!, 0.001)
        assertEquals(3, info.peers.size)
        assertNull(NetworkInfoParser.parse(json, "other"))
    }
}
