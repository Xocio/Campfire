package top.mccampfire.app.data

import android.content.Context
import androidx.compose.runtime.Immutable
import androidx.core.content.edit
import kotlinx.serialization.Serializable
import top.mccampfire.app.game.GameFolderRef
import java.security.SecureRandom

/** Multiplayer settings. */
@Immutable
@Serializable
data class LinkPrefs(
    val playerName: String = "",
    val gameName: String = "",
    val lanEntryName: String = "",
    val allowRelay: Boolean = true,
    val hideAddress: Boolean = false,
    val usePublicNodes: Boolean = true,
    val autoPickNodes: Boolean = true,
    val customNodes: String = "",
    val disabledNodes: String = "",
    val searchTimeoutSeconds: Int = 60,
    val localPort: Int = 0,
    val shareWithLan: Boolean = false,
    val showSkins: Boolean = true,
    val shareVersion: Boolean = true,
    val detectClipboard: Boolean = true,
    val maxPlayers: Int = 0,
    val roomNotice: String = "",
    val roomName: String = "",
    val allowlistOnly: Boolean = false,
    val allowlist: List<AllowedPlayer> = emptyList(),
    val gameFolders: List<GameFolderRef> = emptyList(),
    val voice: Boolean = false,
    val voiceVersion: String = "",
    /** Description of the room in the public lobby. */
    val lobbyDescription: String = "",
    /** Lobby tag identifiers of the room. */
    val lobbyTags: List<String> = emptyList(),
    /** Whether the lobby shows the host's region. */
    val lobbyShowRegion: Boolean = true,
) {
    companion object {
        /** Choices of [searchTimeoutSeconds]. */
        val SearchTimeoutChoices = listOf(30, 60, 120)

        /** Longest player name. */
        const val MaxPlayerName = 32

        /** Choices of [maxPlayers]; 0 means no limit. */
        val MaxPlayerChoices = listOf(0, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 30, 50)

        /** Longest in-game name. */
        const val MaxGameName = 16

        private val GameNamePattern = Regex("^[A-Za-z0-9_]{1,16}$")

        /** True when [name] is a valid in-game player name. */
        fun isValidGameName(name: String): Boolean = GameNamePattern.matches(name)
    }
}

/** A player the host lets into rooms while the allowlist is on. */
@Immutable
@Serializable
data class AllowedPlayer(val id: String, val name: String)

/** Random identifiers of this device for multiplayer and the public lobby. */
class LinkIdentity(context: Context) {
    private val preferences = context.applicationContext.getSharedPreferences(FileName, Context.MODE_PRIVATE)

    /** Identifier shared with the other players of a room. */
    val machineId: String get() = read(MachineKey)

    /** Identifier used only by the public lobby. */
    val lobbyId: String get() = read(LobbyKey)

    /** Replaces the identifier shared with other players. */
    fun resetMachineId(): String = create(MachineKey)

    /** Name used when the player has not set one. */
    fun fallbackName(): String = "Campfire #" + machineId.take(4).uppercase()

    private fun read(key: String): String {
        val stored = preferences.getString(key, null)
        return if (stored != null && isHex32(stored)) stored else create(key)
    }

    private fun create(key: String): String {
        val id = randomHex(16)
        preferences.edit { putString(key, id) }
        return id
    }

    private companion object {
        const val FileName = "link_identity"
        const val MachineKey = "machine_id"
        const val LobbyKey = "lobby_id"

        val Random = SecureRandom()

        fun isHex32(text: String): Boolean = text.length == 32 && text.all { it in '0'..'9' || it in 'a'..'f' }

        fun randomHex(bytes: Int): String {
            val data = ByteArray(bytes)
            Random.nextBytes(data)
            return data.joinToString("") { "%02x".format(it) }
        }
    }
}

/** Name shown to other players. */
fun LinkPrefs.effectivePlayerName(identity: LinkIdentity): String =
    playerName.trim().ifEmpty { identity.fallbackName() }
