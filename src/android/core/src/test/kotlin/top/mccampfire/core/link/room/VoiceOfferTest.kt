package top.mccampfire.core.link.room

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import top.mccampfire.core.link.runtime.DirectConnector
import top.mccampfire.core.link.runtime.NetworkConfig
import top.mccampfire.core.link.runtime.PortForward
import top.mccampfire.core.link.runtime.parseIpv4

class VoiceOfferTest {
    private val host = PlayerProfile("Steve", "a".repeat(32), "Campfire", PlayerKind.Host)
    private val server = RoomServer.start(host, 25565, preferredPort = 0)

    @After
    fun close() = server.close()

    @Test
    fun desktopOffersAreRead() {
        val text = """{"ready":true,"port":24454,"game":"1.20.1","loader":"fabric",
            "mod":{"file":"voicechat-fabric-1.20.1-2.5.0.jar","sha1":"0123456789abcdef0123456789abcdef01234567","size":1200,"ids":["voicechat"],"side":"both"}}"""
        val offer = VoiceOffer.fromJson(Json.parseToJsonElement(text) as JsonObject)!!
        assertTrue(offer.ready)
        assertEquals(24454, offer.port)
        assertEquals("1.20.1", offer.gameVersion)
        assertEquals("fabric", offer.loader)
        assertEquals("voicechat-fabric-1.20.1-2.5.0.jar", offer.mod!!.fileName)
        assertEquals(offer, VoiceOffer.fromJson(offer.toJson()))
        assertNull(VoiceOffer.fromJson(Json.parseToJsonElement("""{"port":70000}""") as JsonObject))
        assertNull(VoiceOffer.fromJson(Json.parseToJsonElement("""{"ready":true,"port":24454,"loader":"rift"}""") as JsonObject)!!.loader)
    }

    @Test
    fun theRoomServiceHandsOutTheOffer() {
        val client = RoomClient.tryConnect(DirectConnector, parseIpv4("127.0.0.1")!!, server.port)!!
        client.use {
            assertNull(it.voice())
            server.voice = VoiceOffer(false, 24454, "1.21.1", "neoforge")
            assertEquals(VoiceOffer(false, 24454, "1.21.1", "neoforge"), it.voice())
            server.voice = null
            assertNull(it.voice())
        }
    }

    @Test
    fun portForwardsAreWritten() {
        val toml = NetworkConfig(
            instanceName = "campfire-room",
            networkName = "n",
            networkSecret = "s",
            hostname = "h",
            portForwards = listOf(PortForward("127.0.0.1:24454", "10.144.144.1:24454", "udp")),
        ).toToml()
        assertTrue(toml.contains("[[port_forward]]\nbind_addr = \"127.0.0.1:24454\"\ndst_addr = \"10.144.144.1:24454\"\nproto = \"udp\""))
    }
}
