package top.mccampfire.core.link.room

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.put

/** Role of a player in a room. */
enum class PlayerKind {
    Host,
    Guest,

    /** The player on this device, when it joined someone else's room. */
    Local,
}

/** A player in a room; the machine id tells devices apart. */
data class PlayerProfile(
    val name: String,
    val machineId: String,
    val vendor: String,
    val kind: PlayerKind,
    val uuid: String? = null,
    val gameName: String? = null,
    val latencyMs: Double? = null,
    val path: String? = null,
    val lossPercent: Double? = null,
) {
    fun withKind(kind: PlayerKind): PlayerProfile = copy(kind = kind)

    /** The same player with the latest measured connection to the host. */
    fun withConnection(latencyMs: Double?, path: String?, lossPercent: Double?): PlayerProfile =
        copy(latencyMs = latencyMs.validLatency(), path = path.validPath(), lossPercent = lossPercent.validLoss())

    fun toJson(includeKind: Boolean): JsonObject = buildJsonObject {
        put("name", name)
        put("machine_id", machineId)
        put("vendor", vendor)
        if (includeKind) put("kind", if (kind == PlayerKind.Host) "HOST" else "GUEST")
        uuid?.let { put("uuid", it) }
        gameName?.let { put("game_name", it) }
        latencyMs?.let { put("latency_ms", it) }
        path?.let { put("path", it) }
        lossPercent?.let { put("loss", it) }
    }

    companion object {
        const val PathDirect = "direct"
        const val PathRelay = "relay"

        /**
         * Reads a profile; returns null when a required field is missing. [kindOverride] replaces the kind
         * written in the JSON.
         */
        fun fromJson(value: JsonElement?, kindOverride: PlayerKind?): PlayerProfile? {
            val json = value as? JsonObject ?: return null
            val name = json.string("name") ?: return null
            val machineId = json.string("machine_id")?.takeIf { it.isNotEmpty() } ?: return null
            val kind = kindOverride ?: when (json.string("kind")) {
                "HOST" -> PlayerKind.Host
                "GUEST" -> PlayerKind.Guest
                else -> return null
            }
            val uuid = json.string("uuid")?.takeIf { it.length == 32 && it.all { c -> c.isHexDigitChar() } }
            val gameName = json.string("game_name")?.takeIf { it.length in 1..16 }
            return PlayerProfile(
                name = name,
                machineId = machineId,
                vendor = json.string("vendor") ?: "",
                kind = kind,
                uuid = uuid,
                gameName = gameName,
                latencyMs = json.number("latency_ms").validLatency(),
                path = json.string("path").validPath(),
                lossPercent = json.number("loss").validLoss(),
            )
        }

        /** Reads a JSON array of profiles, skipping invalid entries. */
        fun listFromJson(value: JsonElement?): List<PlayerProfile>? =
            (value as? JsonArray)?.mapNotNull { fromJson(it, null) }

        private fun Double?.validLatency(): Double? = this?.takeIf { !it.isNaN() && it in 0.0..60000.0 }

        private fun Double?.validLoss(): Double? = this?.takeIf { !it.isNaN() && it in 0.0..100.0 }

        private fun String?.validPath(): String? = this?.takeIf { it == PathDirect || it == PathRelay }

        private fun Char.isHexDigitChar(): Boolean = this in '0'..'9' || this in 'a'..'f' || this in 'A'..'F'
    }
}

/** The room's name, announcement and player limit, as the host sets them. */
data class RoomInfo(val name: String, val notice: String, val maxPlayers: Int) {
    fun toJson(): JsonObject = buildJsonObject {
        put("name", name)
        put("notice", notice)
        put("max", maxPlayers.toDouble())
    }

    companion object {
        const val MaxName = 48
        const val MaxNotice = 200

        /** A room info with the values cleaned up the same way the host does. */
        fun of(name: String?, notice: String?, maxPlayers: Int): RoomInfo =
            RoomInfo(clip(name, MaxName), clip(notice, MaxNotice), if (maxPlayers in 2..100) maxPlayers else 0)

        /** Reads what the host sent; returns null when it is not an object. */
        fun fromJson(value: JsonElement?): RoomInfo? {
            val json = value as? JsonObject ?: return null
            val max = json.number("max")?.takeIf { it in 0.0..100.0 }?.toInt() ?: 0
            return of(json.string("name"), json.string("notice"), max)
        }

        /** One line of plain text: control characters replaced, cut to [limit] characters. */
        private fun clip(text: String?, limit: Int): String {
            if (text.isNullOrBlank()) return ""
            val clean = text.trim().map { if (it.isISOControl()) ' ' else it }.joinToString("")
            return if (clean.length > limit) clean.substring(0, limit) else clean
        }
    }
}

internal fun JsonObject.string(key: String): String? = (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content

internal fun JsonObject.number(key: String): Double? = (this[key] as? JsonPrimitive)?.takeIf { !it.isString }?.doubleOrNull
