package top.mccampfire.app.lobby

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import top.mccampfire.app.data.CampfireService
import top.mccampfire.app.data.Telemetry
import top.mccampfire.core.link.RoomCode
import top.mccampfire.core.lobby.LobbyClient
import top.mccampfire.core.lobby.LobbyError
import top.mccampfire.core.lobby.LobbyException
import top.mccampfire.core.lobby.LobbyList

/** State of the lobby list. */
data class LobbyState(
    val loading: Boolean = false,
    val list: LobbyList? = null,
    val error: LobbyError? = null,
    val loadedAtMillis: Long = 0,
)

/** The public lobby of the Campfire service, shared by the lobby pages. */
class LobbyRepository(private val telemetry: Telemetry? = null, baseUrl: String? = CampfireService.baseUrl) {
    private val client = baseUrl?.let { LobbyClient(it, CampfireService.userAgent) }
    private val mutableState = MutableStateFlow(LobbyState())
    private val mutex = Mutex()

    /** Whether this build has a service with a lobby. */
    val available: Boolean = client != null

    val state: StateFlow<LobbyState> = mutableState.asStateFlow()

    /** Loads the list unless it was loaded within the interval the service asks for. */
    suspend fun refresh(force: Boolean = false) {
        val api = client ?: return
        mutex.withLock {
            val current = mutableState.value
            val wait = (current.list?.refreshSeconds ?: 0) * 1000L
            if (!force && current.list != null && System.currentTimeMillis() - current.loadedAtMillis < wait) return
            mutableState.update { it.copy(loading = true) }
            try {
                val list = withContext(Dispatchers.IO) { api.list() }
                mutableState.value = LobbyState(list = list, loadedAtMillis = System.currentTimeMillis())
            } catch (e: LobbyException) {
                mutableState.update { it.copy(loading = false, error = e.kind) }
            }
        }
    }

    /** The invite code of a listed room. */
    suspend fun join(roomId: String, password: String?): RoomCode {
        val api = client ?: throw LobbyException(LobbyError.Closed, "No service")
        return withContext(Dispatchers.IO) { api.join(roomId, password) }.also { telemetry?.count("lobby.join") }
    }

    /** Reports a room whose name or content is not fit to show. */
    suspend fun report(roomId: String) {
        val api = client ?: return
        withContext(Dispatchers.IO) { api.report(roomId, "content") }
    }
}
