package top.mccampfire.core.lobby

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LobbyClientTest {
    private val client = LobbyClient("https://example.invalid", "Campfire-Android/test", nowMillis = { 42L })

    @Test
    fun listAnswersAreRead() {
        val json = Json.parseToJsonElement(
            """
            {"enabled":true,"notice":"Be kind","minAppVersion":"","refreshSeconds":2,"tags":["survival","pvp"],
             "rooms":[
               {"id":"r1","title":"Weekend","host":"Steve","version":"1.20.1","map":"Island","tags":["survival"],
                "players":3,"maxPlayers":8,"country":"CN","region":"Sichuan","createdAt":1700000000000,"locked":true,
                "icon":"iVBORw0KGgo=","description":"Hello","voice":true},
               {"id":"r2","title":"","players":0},
               {"id":"r3","title":"No extras","players":0}
             ]}
            """.trimIndent(),
        ).jsonObject
        val list = client.parseList(json)
        assertTrue(list.enabled)
        assertEquals("Be kind", list.notice)
        assertEquals(5, list.refreshSeconds)
        assertEquals(listOf("survival", "pvp"), list.tags)
        assertEquals(listOf("r1", "r3"), list.rooms.map { it.id })

        val first = list.rooms[0]
        assertEquals(3, first.players)
        assertEquals(8, first.maxPlayers)
        assertEquals(1700000000000L, first.createdAtMillis)
        assertTrue(first.locked)
        assertTrue(first.voice)
        assertEquals("iVBORw0KGgo=", first.icon)

        val second = list.rooms[1]
        assertEquals(1, second.players)
        assertEquals(0, second.maxPlayers)
        assertEquals(42L, second.createdAtMillis)
        assertFalse(second.locked)
    }

    @Test
    fun publishBodyCarriesTheListing() {
        val code = top.mccampfire.core.link.RoomCode.create()
        val listing = LobbyListing(
            owner = "0123456789abcdef0123456789abcdef",
            app = "0.1.0",
            code = code,
            title = "Weekend",
            host = "Steve",
            version = "1.20.1",
            map = "Island",
            tags = listOf("survival"),
            maxPlayers = 8,
            showRegion = false,
            description = "Hello",
            voice = true,
            password = "",
        )
        val body = client.publishBody(listing, players = 2)
        assertEquals("android", body["platform"]?.jsonPrimitive?.content)
        assertEquals(code.code, body["code"]?.jsonPrimitive?.content)
        assertEquals("2", body["players"]?.jsonPrimitive?.content)
        assertEquals("false", body["showRegion"]?.jsonPrimitive?.content)
        assertFalse(body.containsKey("password"))
        assertTrue(client.publishBody(listing.copy(password = "secret"), 1).containsKey("password"))
    }

    @Test
    fun missingFieldsUseDefaults() {
        val list = client.parseList(Json.parseToJsonElement("{}").jsonObject)
        assertTrue(list.enabled)
        assertEquals(30, list.refreshSeconds)
        assertTrue(list.rooms.isEmpty())
    }
}
