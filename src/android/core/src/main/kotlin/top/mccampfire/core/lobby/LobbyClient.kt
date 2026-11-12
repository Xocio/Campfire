package top.mccampfire.core.lobby

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.put
import top.mccampfire.core.link.RoomCode
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URI

/** A room in the lobby as everyone sees it; the invite code is only handed out on joining. */
data class LobbyRoom(
    val id: String,
    val title: String,
    val host: String,
    val version: String,
    val map: String,
    val tags: List<String>,
    val players: Int,
    val maxPlayers: Int,
    val country: String,
    val region: String,
    val createdAtMillis: Long,
    val locked: Boolean,
    /** Room picture as base64 PNG or JPEG, or empty. */
    val icon: String,
    val description: String,
    val voice: Boolean,
)

/** The lobby list and the settings the service sends with it. */
data class LobbyList(
    val enabled: Boolean,
    val notice: String,
    val refreshSeconds: Int,
    val tags: List<String>,
    val rooms: List<LobbyRoom>,
)

/** What a host lists a room with. */
data class LobbyListing(
    /** Random identifier of this device used only by the lobby. */
    val owner: String,
    /** Version name of the app, such as 0.1.0. */
    val app: String,
    val code: RoomCode,
    val title: String,
    val host: String,
    val version: String,
    val map: String,
    val tags: List<String>,
    val maxPlayers: Int,
    val showRegion: Boolean,
    val description: String,
    val voice: Boolean,
    /** Empty for a room without a password. */
    val password: String,
)

/** A listed room as its host holds it; the token proves the listing is theirs. */
data class LobbyTicket(val id: String, val token: String, val title: String, val heartbeatSeconds: Int)

/** Why a lobby request failed. */
enum class LobbyError { Network, Closed, Full, NotFound, Removed, Reported, Blocked, Banned, UpdateRequired, TooMany, AlreadyListed, WrongPassword, Unexpected }

class LobbyException(val kind: LobbyError, message: String, cause: Throwable? = null, val field: String? = null) : IOException(message, cause)

/** The public room lobby of the Campfire service. */
class LobbyClient(private val baseUrl: String, private val userAgent: String, private val nowMillis: () -> Long = System::currentTimeMillis) {
    /** Rooms listed right now. */
    fun list(): LobbyList = parseList(send("GET", "/v1/lobby", null))

    /** The invite code of a listed room; [password] is needed when the room is locked. */
    fun join(roomId: String, password: String? = null): RoomCode {
        val body = buildJsonObject { if (!password.isNullOrEmpty()) put("password", password) }
        val json = send("POST", "/v1/lobby/rooms/${encode(roomId)}/join", body)
        return RoomCode.find(json.text("code")) ?: throw LobbyException(LobbyError.Unexpected, "The service sent no invite code.")
    }

    /** Lists a room; the ticket is needed for every later heartbeat and for taking it down. */
    fun publish(listing: LobbyListing, players: Int): LobbyTicket {
        val json = send("POST", "/v1/lobby/rooms", publishBody(listing, players))
        val id = json.text("id")
        val token = json.text("token")
        if (id.isEmpty() || token.isEmpty()) throw LobbyException(LobbyError.Unexpected, "The service sent no ticket.")
        return LobbyTicket(id, token, json.text("title").ifEmpty { listing.title }, heartbeatSeconds(json))
    }

    /** Body of a publish request. */
    fun publishBody(listing: LobbyListing, players: Int): JsonObject = buildJsonObject {
        put("platform", "android")
        put("owner", listing.owner)
        put("app", listing.app)
        put("code", listing.code.code)
        put("title", listing.title)
        put("host", listing.host)
        put("version", listing.version)
        put("map", listing.map)
        put("tags", JsonArray(listing.tags.map(::JsonPrimitive)))
        put("players", players)
        put("maxPlayers", listing.maxPlayers)
        put("showRegion", listing.showRegion)
        put("description", listing.description)
        put("voice", listing.voice)
        if (listing.password.isNotEmpty()) put("password", listing.password)
    }

    /** Keeps a listed room in the lobby; returns the seconds until the next heartbeat. */
    fun heartbeat(ticket: LobbyTicket, title: String, players: Int, maxPlayers: Int, voice: Boolean): Int {
        val body = buildJsonObject {
            put("title", title)
            put("players", players)
            put("maxPlayers", maxPlayers)
            put("voice", voice)
        }
        return heartbeatSeconds(send("PUT", "/v1/lobby/rooms/${encode(ticket.id)}", body, ticket.token))
    }

    /** Takes a listed room down; a listing that is already gone counts as done. */
    fun close(ticket: LobbyTicket) {
        send("DELETE", "/v1/lobby/rooms/${encode(ticket.id)}", null, ticket.token)
    }

    /** Reports a room with one of the reasons the service accepts. */
    fun report(roomId: String, reason: String) {
        send("POST", "/v1/lobby/rooms/${encode(roomId)}/report", buildJsonObject { put("reason", reason) })
    }

    /** Parses a list answer. */
    fun parseList(json: JsonObject): LobbyList = LobbyList(
        enabled = (json["enabled"] as? JsonPrimitive)?.booleanOrNull ?: true,
        notice = json.text("notice"),
        refreshSeconds = maxOf(5, json.int("refreshSeconds", 30)),
        tags = json.strings("tags"),
        rooms = (json["rooms"] as? JsonArray).orEmpty().filterIsInstance<JsonObject>().map { room ->
            LobbyRoom(
                id = room.text("id"),
                title = room.text("title"),
                host = room.text("host"),
                version = room.text("version"),
                map = room.text("map"),
                tags = room.strings("tags"),
                players = maxOf(1, room.int("players", 1)),
                maxPlayers = room.int("maxPlayers", 0),
                country = room.text("country"),
                region = room.text("region"),
                createdAtMillis = (room["createdAt"] as? JsonPrimitive)?.doubleOrNull?.toLong() ?: nowMillis(),
                locked = (room["locked"] as? JsonPrimitive)?.booleanOrNull == true,
                icon = room.text("icon"),
                description = room.text("description"),
                voice = (room["voice"] as? JsonPrimitive)?.booleanOrNull == true,
            )
        }.filter { it.id.isNotEmpty() && it.title.isNotEmpty() },
    )

    private fun heartbeatSeconds(json: JsonObject): Int = json.int("heartbeatSeconds", 240).coerceIn(30, 3600)

    private fun send(method: String, path: String, body: JsonObject?, token: String? = null): JsonObject {
        val connection = try {
            URI(baseUrl + path).toURL().openConnection() as HttpURLConnection
        } catch (e: Exception) {
            throw LobbyException(LobbyError.Unexpected, e.message ?: "Invalid address", e)
        }
        try {
            connection.requestMethod = method
            connection.connectTimeout = TimeoutMillis
            connection.readTimeout = TimeoutMillis
            connection.setRequestProperty("User-Agent", userAgent)
            connection.setRequestProperty("Accept", "application/json")
            if (token != null) connection.setRequestProperty("Authorization", "Bearer $token")
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            val stream = if (status in 200..299) connection.inputStream else connection.errorStream
            val text = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }.orEmpty()
            val json = runCatching { Parser.parseToJsonElement(text) as? JsonObject }.getOrNull()
            if (status in 200..299) return json ?: JsonObject(emptyMap())
            throw failure(status, json?.text("error").orEmpty(), json?.text("field"))
        } catch (e: LobbyException) {
            throw e
        } catch (e: IOException) {
            throw LobbyException(LobbyError.Network, e.message ?: "Network error", e)
        } finally {
            connection.disconnect()
        }
    }

    private fun failure(status: Int, error: String, field: String?): LobbyException {
        val message = "The service answered $status" + if (error.isNotEmpty()) " ($error)." else "."
        val kind = when (status) {
            401 -> if (error == "wrong password" || error == "password required") LobbyError.WrongPassword else LobbyError.Unexpected
            403 -> LobbyError.Banned
            404 -> LobbyError.NotFound
            409 -> LobbyError.AlreadyListed
            410 -> if (error == "removed") LobbyError.Removed else LobbyError.Reported
            422 -> LobbyError.Blocked
            426 -> LobbyError.UpdateRequired
            429 -> LobbyError.TooMany
            503 -> if (error == "lobby full") LobbyError.Full else LobbyError.Closed
            else -> LobbyError.Unexpected
        }
        return LobbyException(kind, message, field = field)
    }

    private companion object {
        const val TimeoutMillis = 15_000
        val Parser = Json { ignoreUnknownKeys = true }

        fun encode(segment: String): String = java.net.URLEncoder.encode(segment, "UTF-8").replace("+", "%20")

        fun JsonObject.text(key: String): String = (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content.orEmpty()

        fun JsonObject.int(key: String, fallback: Int): Int =
            (this[key] as? JsonPrimitive)?.doubleOrNull?.takeIf { it >= 0 && it <= Int.MAX_VALUE }?.toInt() ?: fallback

        fun JsonObject.strings(key: String): List<String> = (this[key] as? JsonArray).orEmpty()
            .mapNotNull { (it as? JsonPrimitive)?.takeIf(JsonPrimitive::isString)?.content?.takeIf(String::isNotEmpty) }

        fun JsonArray?.orEmpty(): List<JsonElement> = this ?: emptyList()
    }
}
