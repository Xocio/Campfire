package top.mccampfire.app.data

import android.content.Context
import android.os.Build
import androidx.core.content.edit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import top.mccampfire.app.BuildConfig
import java.net.HttpURLConnection
import java.net.URL
import java.security.SecureRandom
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Anonymous usage statistics, sent at most once a day and only after the user agreed.
 * The statistics id is a random number of its own, separate from the multiplayer and lobby ids.
 */
class Telemetry(context: Context, private val settings: SettingsRepository) {
    private val preferences = context.applicationContext.getSharedPreferences(FileName, Context.MODE_PRIVATE)
    private val sending = Mutex()

    /** Whether this build can send statistics at all. */
    val available: Boolean get() = CampfireService.baseUrl != null

    /** Records the answer to the statistics question; declining drops the counts kept so far. */
    fun setConsent(allowed: Boolean) = settings.update {
        it.copy(statsConsent = allowed, statsCounts = if (allowed) it.statsCounts else emptyMap())
    }

    /** Counts one use of [feature]; nothing is counted without consent. */
    fun count(feature: String) {
        if (settings.settings.value.statsConsent != true) return
        settings.update { current ->
            if (current.statsConsent != true) return@update current
            val next = ((current.statsCounts[feature] ?: 0) + 1).coerceAtMost(MaxCount)
            current.copy(statsCounts = current.statsCounts + (feature to next))
        }
    }

    @Volatile
    private var launchCounted = false

    /** Counts one start of the app per process. */
    fun countLaunch() {
        if (launchCounted) return
        launchCounted = true
        count("launch")
    }

    /** Replaces the statistics id. */
    fun resetId() {
        preferences.edit { putString(IdKey, randomId()) }
    }

    /** The next report as readable lines, without creating an id. */
    fun preview(pendingId: String): String {
        val id = storedId() ?: pendingId
        return report(id).entries.joinToString("\n") { (key, value) ->
            val text = if (value is JsonObject) {
                if (value.isEmpty()) "{}" else value.entries.joinToString(", ") { (k, v) -> k + "=" + (v as JsonPrimitive).content }
            } else {
                (value as JsonPrimitive).content
            }
            "$key: $text"
        }
    }

    /** Sends today's report when allowed and not sent yet today. */
    suspend fun sendIfDue() {
        val baseUrl = CampfireService.baseUrl ?: return
        val today = today()
        val current = settings.settings.value
        if (current.statsConsent != true || current.statsSentDay == today) return
        if (!sending.tryLock()) return
        try {
            val body = report(storedId() ?: randomId().also { id -> preferences.edit { putString(IdKey, id) } })
            val sent = withContext(Dispatchers.IO) { post("$baseUrl/v1/report", body) }
            if (!sent) return
            val counted = (body["features"] as JsonObject).mapValues { (_, value) -> (value as JsonPrimitive).content.toInt() }
            settings.update { latest ->
                val remaining = latest.statsCounts
                    .mapValues { (key, value) -> value - (counted[key] ?: 0) }
                    .filterValues { it > 0 }
                latest.copy(statsCounts = remaining, statsSentDay = today)
            }
        } finally {
            sending.unlock()
        }
    }

    /** Exactly what the next report sends. */
    private fun report(id: String): JsonObject {
        val current = settings.settings.value
        return buildJsonObject {
            put("platform", "android")
            put("id", id)
            put("version", BuildConfig.VERSION_NAME)
            put("os", osVersion())
            put("arch", architecture())
            put("lang", language(current.language))
            put("features", JsonObject(current.statsCounts.filterValues { it > 0 }.mapValues { (_, n) -> JsonPrimitive(n) }))
        }
    }

    private fun post(url: String, body: JsonObject): Boolean = runCatching {
        val connection = URL(url).openConnection() as HttpURLConnection
        try {
            connection.requestMethod = "POST"
            connection.connectTimeout = TimeoutMillis
            connection.readTimeout = TimeoutMillis
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            connection.setRequestProperty("User-Agent", CampfireService.userAgent)
            connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            connection.responseCode in 200..299
        } finally {
            connection.disconnect()
        }
    }.getOrDefault(false)

    private fun storedId(): String? = preferences.getString(IdKey, null)?.takeIf { it.length == 32 && it.all(::isHex) }

    private companion object {
        const val FileName = "stats"
        const val IdKey = "id"
        const val MaxCount = 100_000
        const val TimeoutMillis = 8_000
        val Random = SecureRandom()

        fun isHex(c: Char): Boolean = c in '0'..'9' || c in 'a'..'f'

        fun randomId(): String = ByteArray(16).also(Random::nextBytes).joinToString("") { "%02x".format(it) }

        fun today(): String = SimpleDateFormat("yyyy-MM-dd", Locale.ROOT).format(Date())

        /** Android release such as 14, limited to the characters the service accepts. */
        fun osVersion(): String = Build.VERSION.RELEASE.orEmpty().filter { it.isLetterOrDigit() || it in "._-" }.take(24).ifEmpty { "unknown" }

        fun architecture(): String = when (Build.SUPPORTED_ABIS.firstOrNull()) {
            "x86_64" -> "x86_64"
            else -> "arm64"
        }

        /** Interface language: the app setting, or the system language when it follows the system. */
        fun language(setting: Int): String = when (setting) {
            1 -> "zh"
            2 -> "zh-Hant"
            3 -> "zh-HK"
            4 -> "en"
            else -> Locale.getDefault().let { locale ->
                when {
                    locale.language == "yue" -> "zh-HK"
                    locale.language != "zh" -> "en"
                    locale.script == "Hant" || locale.country in setOf("TW", "HK", "MO") -> "zh-Hant"
                    else -> "zh"
                }
            }
        }
    }
}
