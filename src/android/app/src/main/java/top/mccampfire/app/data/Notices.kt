package top.mccampfire.app.data

import androidx.compose.runtime.Immutable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject

/** Notice published by the Campfire service. */
@Immutable
data class Notice(
    val id: String,
    val title: String,
    val body: String,
    val isBanner: Boolean,
    val repeats: Boolean,
    val isWarning: Boolean,
    val minVersion: String,
    val maxVersion: String,
    /** "zh", "en", or empty for every language. */
    val language: String,
    val buttonText: String,
    /** Web page the button opens. */
    val buttonUrl: String?,
    /** Page inside Campfire the button opens, one of [NoticePages.Supported]. */
    val buttonPage: String?,
    /** Title color as ARGB, or null for the theme's. */
    val titleColor: Long?,
    val bodyColor: Long?,
) {
    /** Whether the notice has a button that opens a web page or a page inside Campfire. */
    val hasButton: Boolean get() = buttonText.isNotEmpty() && (buttonUrl != null || buttonPage != null)
}

/** Pages a notice button can open on Android, written as campfire://name. */
object NoticePages {
    private const val Scheme = "campfire://"

    val Supported = setOf("home", "lobby", "linksettings", "settings", "support", "about", "licenses")

    /** The page named by a campfire:// link, or null for anything else. */
    fun parse(url: String?): String? {
        if (url == null || !url.startsWith(Scheme, ignoreCase = true)) return null
        val name = url.substring(Scheme.length).trim().trimEnd('/').lowercase()
        return name.takeIf { it in Supported }
    }
}

/** Reads the notices of a service manifest; malformed entries are skipped. */
fun parseNotices(manifest: String): List<Notice> {
    val root = runCatching { Json.parseToJsonElement(manifest).jsonObject }.getOrNull() ?: return emptyList()
    val items = root["notices"] as? JsonArray ?: return emptyList()
    return items.mapNotNull { element ->
        val item = element as? JsonObject ?: return@mapNotNull null
        val id = item.text("id")
        val title = item.text("title")
        if (id.isEmpty() || title.isEmpty()) return@mapNotNull null
        val url = item.text("buttonUrl")
        Notice(
            id = id,
            title = title,
            body = item.text("body"),
            isBanner = item.text("style") == "banner",
            repeats = item.text("repeat") == "always",
            isWarning = item.text("level") == "warning",
            minVersion = item.text("minVersion"),
            maxVersion = item.text("maxVersion"),
            language = item.text("lang"),
            buttonText = item.text("buttonText"),
            buttonUrl = url.takeIf { it.startsWith("https://", ignoreCase = true) },
            buttonPage = NoticePages.parse(url),
            titleColor = parseColor(item.text("titleColor")),
            bodyColor = parseColor(item.text("bodyColor")),
        )
    }
}

/** Notices meant for this version and language that have not been shown already. */
fun List<Notice>.applicable(version: String, language: String, seen: Set<String>): List<Notice> = filter { notice ->
    (notice.minVersion.isEmpty() || !isNewerVersion(notice.minVersion, version)) &&
        (notice.maxVersion.isEmpty() || !isNewerVersion(version, notice.maxVersion)) &&
        (notice.language.isEmpty() || notice.language == language) &&
        (notice.repeats || notice.id !in seen)
}

/** Reads #RRGGBB as an opaque ARGB value. */
internal fun parseColor(text: String): Long? {
    if (text.length != 7 || text[0] != '#') return null
    val rgb = text.substring(1).toLongOrNull(16) ?: return null
    return 0xFF000000 or rgb
}

private fun JsonObject.text(key: String): String = (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content.orEmpty()
