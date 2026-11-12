package top.mccampfire.core.link

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import top.mccampfire.core.link.runtime.NetworkConfig

/** Compares results with values produced by the desktop sources. */
class DesktopGoldenTest {
    private val golden: JsonObject = Json.parseToJsonElement(
        javaClass.getResource("/golden/desktop.json")!!.readText(),
    ).jsonObject

    @Test
    fun inviteCodesMatchTheDesktop() {
        val codes = golden.getValue("codes").jsonArray
        assertEquals(40, codes.size)
        for (entry in codes) {
            val item = entry.jsonObject
            val bytes = item.getValue("bytes").jsonPrimitive.content.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
            val code = RoomCode.fromRandom(bytes)
            assertEquals(item.getValue("code").jsonPrimitive.content, code.code)
            assertEquals(item.getValue("name").jsonPrimitive.content, code.networkName)
            assertEquals(item.getValue("secret").jsonPrimitive.content, code.networkSecret)
            assertEquals(code, RoomCode.find("join ${code.code.lowercase()} please"))
        }
    }

    @Test
    fun findingCodesInTextMatchesTheDesktop() {
        for (entry in golden.getValue("find").jsonArray) {
            val item = entry.jsonObject
            val expected = item.getValue("code")
            val found = RoomCode.find(item.getValue("text").jsonPrimitive.content)
            if (expected is JsonNull) assertNull(found) else assertEquals(expected.jsonPrimitive.content, found?.code)
        }
    }

    @Test
    fun configurationTextMatchesTheDesktop() {
        val expected = golden.getValue("toml").jsonArray.map { it.jsonPrimitive.content }
        val configs = listOf(
            NetworkConfig(
                instanceName = "campfire-room",
                networkName = "scaffolding-mc-ABCD-EFGH",
                networkSecret = "JKLM-NPQR",
                hostname = "scaffolding-mc-server-13448",
                ipv4 = "10.144.144.1",
                peers = listOf("tcp://225284.xyz:11010", "wss://et.chinokou.cn"),
                tcpWhitelist = listOf("13448", "40001"),
                udpWhitelist = listOf("40001"),
            ),
            NetworkConfig(
                instanceName = "campfire-room",
                networkName = "scaffolding-mc-0000-1111",
                networkSecret = "2222-\"\\3",
                hostname = "campfire-0a1b2c3d",
                peers = listOf("udp://et.basd1.de:11010"),
                directOnly = true,
                socksPort = 41000,
            ),
            NetworkConfig(
                instanceName = "campfire-room",
                networkName = "n",
                networkSecret = "s",
                hostname = "h",
                directOnly = true,
                hideAddress = true,
            ),
        )
        for ((index, config) in configs.withIndex()) {
            assertEquals("config $index", normalize(expected[index]), normalize(config.toToml()))
        }
    }

    /** Lines only the Android configuration has are left out. */
    private fun normalize(toml: String): List<String> = toml.replace("\r\n", "\n").lines()
        .filterNot { it.startsWith("instance_name") || it.startsWith("socks5_proxy") || it.startsWith("multi_thread") }
}
