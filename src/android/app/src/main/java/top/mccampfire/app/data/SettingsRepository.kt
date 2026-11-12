package top.mccampfire.app.data

import android.content.Context
import androidx.core.content.edit
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.updateAndGet
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject

/** Raw storage of the encoded settings. */
interface SettingsStorage {
    fun read(): String?
    fun write(value: String)

    /** Keeps [value], the stored text that could not be decoded. */
    fun backup(value: String) = Unit
}

/** [SettingsStorage] backed by shared preferences. */
class SharedPreferencesSettingsStorage(context: Context) : SettingsStorage {
    private val preferences = context.getSharedPreferences(FileName, Context.MODE_PRIVATE)

    override fun read(): String? = preferences.getString(Key, null)

    override fun write(value: String) {
        preferences.edit { putString(Key, value) }
    }

    override fun backup(value: String) {
        preferences.edit { putString(BackupKey, value) }
    }

    private companion object {
        const val FileName = "settings"
        const val Key = "app_settings"
        const val BackupKey = "app_settings_backup"
    }
}

/** Converts [AppSettings] to and from JSON; missing or invalid fields use defaults. */
object SettingsCodec {
    /** Version written by [encode]. */
    const val CurrentVersion = 1

    private const val VersionKey = "schemaVersion"

    private val json = Json {
        ignoreUnknownKeys = true
        coerceInputValues = true
    }

    /** Steps that upgrade a stored object from the key version to the next one. */
    private val migrations: Map<Int, (JsonObject) -> JsonObject> = mapOf(
        0 to { it },
    )

    fun encode(settings: AppSettings): String {
        val body = json.encodeToJsonElement(AppSettings.serializer(), settings).jsonObject
        return JsonObject(body + (VersionKey to JsonPrimitive(CurrentVersion))).toString()
    }

    /**
     * Decodes [raw]. Null or blank input yields defaults silently; text that cannot be
     * decoded yields defaults and is passed to [onCorrupt] with the cause.
     */
    fun decode(raw: String?, onCorrupt: (String, Throwable) -> Unit = { _, _ -> }): AppSettings {
        if (raw.isNullOrBlank()) return AppSettings()
        return try {
            var obj = json.parseToJsonElement(raw).jsonObject
            var version = (obj[VersionKey] as? JsonPrimitive)?.intOrNull ?: 0
            while (version < CurrentVersion) {
                obj = migrations.getValue(version)(obj)
                version++
            }
            json.decodeFromJsonElement(AppSettings.serializer(), obj)
        } catch (e: Exception) {
            onCorrupt(raw, e)
            AppSettings()
        }
    }
}

/** Single source of truth for [AppSettings]. */
class SettingsRepository(
    private val storage: SettingsStorage,
    onCorrupt: (Throwable) -> Unit = {},
) {
    private val state = MutableStateFlow(
        SettingsCodec.decode(storage.read()) { raw, cause ->
            storage.backup(raw)
            onCorrupt(cause)
        },
    )

    val settings: StateFlow<AppSettings> = state.asStateFlow()

    /** Applies [transform] and persists the result. */
    fun update(transform: (AppSettings) -> AppSettings) {
        val next = state.updateAndGet(transform)
        storage.write(SettingsCodec.encode(next))
    }

    /** Restores every appearance value. */
    fun resetAppearance() {
        update { it.withDefaultAppearance() }
    }
}
