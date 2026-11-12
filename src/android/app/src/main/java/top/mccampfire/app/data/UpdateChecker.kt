package top.mccampfire.app.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import top.mccampfire.app.BuildConfig
import java.net.HttpURLConnection
import java.net.URL

/** Available application update. */
data class AvailableUpdate(
    val versionName: String,
    val releaseNotes: String,
    val downloadUrl: String,
)

/** Result of an update check. */
sealed interface UpdateResult {
    data class Available(val update: AvailableUpdate) : UpdateResult
    data object UpToDate : UpdateResult
    data object Failed : UpdateResult
}

/** Source of update information. */
fun interface UpdateChecker {
    suspend fun check(currentVersion: String): UpdateResult
}

/** Campfire service settings written at build time. */
object CampfireService {
    /** Base address of the service, or null in builds without one. */
    val baseUrl: String? = BuildConfig.SERVICE_URL.trim().trimEnd('/').takeIf { it.startsWith("https://") || it.startsWith("http://") }

    /** Client name sent with every request. */
    val userAgent: String = "Campfire-Android/" + BuildConfig.VERSION_NAME

    /** Update checker, or null in builds without a service address. */
    val updateChecker: UpdateChecker? = baseUrl?.let { ServiceUpdateChecker(it) }

    /** Address of the source code repository. */
    const val RepositoryUrl = "https://github.com/Xocio/Campfire"

    private const val TimeoutMillis = 8_000

    /** Downloads the Android manifest of the service; null when it cannot be reached. */
    suspend fun fetchManifest(baseUrl: String): String? = withContext(Dispatchers.IO) {
        runCatching {
            val connection = URL("$baseUrl/v1/manifest?platform=android").openConnection() as HttpURLConnection
            connection.connectTimeout = TimeoutMillis
            connection.readTimeout = TimeoutMillis
            connection.setRequestProperty("Accept", "application/json")
            connection.setRequestProperty("User-Agent", userAgent)
            try {
                if (connection.responseCode != HttpURLConnection.HTTP_OK) {
                    null
                } else {
                    connection.inputStream.bufferedReader().use { it.readText() }
                }
            } finally {
                connection.disconnect()
            }
        }.getOrNull()
    }
}

/** Reads the Android release from the manifest of the Campfire service. */
class ServiceUpdateChecker(private val baseUrl: String) : UpdateChecker {
    override suspend fun check(currentVersion: String): UpdateResult {
        val body = CampfireService.fetchManifest(baseUrl) ?: return UpdateResult.Failed
        return runCatching { parse(body, currentVersion) }.getOrDefault(UpdateResult.Failed)
    }

    private fun parse(body: String, currentVersion: String): UpdateResult {
        val android = Json.parseToJsonElement(body).jsonObject["android"] as? JsonObject ?: return UpdateResult.UpToDate
        val version = android.text("version")
        if (!VersionPattern.matches(version) || !isNewerVersion(version, currentVersion)) return UpdateResult.UpToDate
        val download = (android["downloads"] as? JsonArray)
            ?.mapNotNull { (it as? JsonPrimitive)?.content }
            ?.firstOrNull { it.startsWith("https://") }
            ?: return UpdateResult.UpToDate
        return UpdateResult.Available(AvailableUpdate(version, android.text("notes"), download))
    }

    private fun JsonObject.text(key: String): String = (this[key] as? JsonPrimitive)?.content.orEmpty()

    private companion object {
        val VersionPattern = Regex("^\\d+\\.\\d+\\.\\d+$")
    }
}

/** Compares dotted version strings numerically. */
fun isNewerVersion(candidate: String, current: String): Boolean {
    val left = candidate.split(".").map { it.toIntOrNull() ?: 0 }
    val right = current.split(".").map { it.toIntOrNull() ?: 0 }
    for (index in 0 until maxOf(left.size, right.size)) {
        val difference = left.getOrElse(index) { 0 } - right.getOrElse(index) { 0 }
        if (difference != 0) return difference > 0
    }
    return false
}
