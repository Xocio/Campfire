package top.mccampfire.core.link.room

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.put

/** The voice chat jar the host plays with: file name and SHA-1. */
data class VoiceModFile(val fileName: String, val sha1: String, val size: Long)

/** The host's voice chat as the room service hands it to players. */
data class VoiceOffer(
    /** Whether the voice server of the host's game is running. */
    val ready: Boolean,
    /** UDP port of the voice server. */
    val port: Int,
    /** The host's game version, or null. */
    val gameVersion: String?,
    /** The host's mod loader in lower case, such as "fabric", or null. */
    val loader: String?,
    /** The jar the host plays with, or null while it is not known. */
    val mod: VoiceModFile? = null,
) {
    fun toJson(): JsonObject = buildJsonObject {
        put("ready", ready)
        put("port", port.toDouble())
        gameVersion?.let { put("game", it) }
        loader?.let { put("loader", it) }
        mod?.let { file ->
            put(
                "mod",
                buildJsonObject {
                    put("file", file.fileName)
                    put("sha1", file.sha1)
                    put("size", file.size.toDouble())
                    put("ids", JsonArray(listOf(JsonPrimitive(VoiceModId))))
                    put("side", "both")
                },
            )
        }
    }

    companion object {
        /** Mod id of the voice chat mod. */
        const val VoiceModId = "voicechat"

        /** Default UDP port of the voice server. */
        const val DefaultPort = 24454

        /** Loaders a voice offer may name. */
        val Loaders = setOf("vanilla", "fabric", "quilt", "forge", "neoforge")

        /** Reads what the host sent; returns null when it is malformed. */
        fun fromJson(json: JsonObject): VoiceOffer? {
            val port = (json["port"] as? JsonPrimitive)?.doubleOrNull ?: return null
            if (port < 1 || port > 65535 || port != Math.floor(port)) return null
            val ready = (json["ready"] as? JsonPrimitive)?.booleanOrNull ?: false
            val game = (json["game"] as? JsonPrimitive)?.takeIf { it.isString }?.content?.takeIf { it.isNotEmpty() && it.length <= 64 }
            val loader = (json["loader"] as? JsonPrimitive)?.takeIf { it.isString }?.content?.lowercase()?.takeIf { it in Loaders }
            val mod = (json["mod"] as? JsonObject)?.let { value ->
                val file = (value["file"] as? JsonPrimitive)?.takeIf { it.isString }?.content
                val sha1 = (value["sha1"] as? JsonPrimitive)?.takeIf { it.isString }?.content
                val size = (value["size"] as? JsonPrimitive)?.doubleOrNull?.toLong() ?: 0
                if (file.isNullOrBlank() || sha1 == null || !Sha1Pattern.matches(sha1)) null else VoiceModFile(file, sha1.lowercase(), size)
            }
            return VoiceOffer(ready, port.toInt(), game, loader, mod)
        }

        private val Sha1Pattern = Regex("^[0-9a-fA-F]{40}$")
    }
}
