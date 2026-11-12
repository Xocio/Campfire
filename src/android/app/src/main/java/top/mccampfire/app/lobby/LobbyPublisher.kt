package top.mccampfire.app.lobby

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import top.mccampfire.app.BuildConfig
import top.mccampfire.app.data.CampfireService
import top.mccampfire.app.data.LinkIdentity
import top.mccampfire.app.data.SettingsRepository
import top.mccampfire.app.data.Telemetry
import top.mccampfire.app.data.effectivePlayerName
import top.mccampfire.app.link.LinkController
import top.mccampfire.app.link.VoiceController
import top.mccampfire.app.link.VoiceState
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.LinkStage
import top.mccampfire.core.link.LinkState
import top.mccampfire.core.link.minecraft.MinecraftProbe
import top.mccampfire.core.lobby.LobbyClient
import top.mccampfire.core.lobby.LobbyError
import top.mccampfire.core.lobby.LobbyException
import top.mccampfire.core.lobby.LobbyListing
import top.mccampfire.core.lobby.LobbyTicket

/** Whether the open room is listed in the lobby. */
enum class PublishPhase {
    /** Not listed. */
    Off,

    /** Being listed, or listed again after a network problem. */
    Publishing,
    Listed,

    /** No longer listed and not trying again; see [PublishState.error]. */
    Stopped,
}

/** Lobby listing of the open room as the pages show it. */
data class PublishState(
    val phase: PublishPhase = PublishPhase.Off,
    /** Title as listed. */
    val title: String = "",
    val locked: Boolean = false,
    /** Last problem, or null when all is well. */
    val error: LobbyError? = null,
    /** Field the service refused, such as "title" or "description". */
    val errorField: String? = null,
)

/**
 * Keeps the open room listed in the lobby: lists it, sends heartbeats at the pace the service asks for,
 * lists it again when the service has dropped it, and takes it down when the room closes.
 */
class LobbyPublisher(
    private val link: LinkController,
    private val voice: VoiceController,
    private val settings: SettingsRepository,
    private val identity: LinkIdentity,
    private val telemetry: Telemetry,
    baseUrl: String? = CampfireService.baseUrl,
) {
    private val client = baseUrl?.let { LobbyClient(it, CampfireService.userAgent) }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val mutableState = MutableStateFlow(PublishState())
    private val wake = Channel<Unit>(Channel.CONFLATED)
    private var job: Job? = null
    private var ticket: LobbyTicket? = null

    /** Whether this build has a service with a lobby. */
    val available: Boolean = client != null

    val state: StateFlow<PublishState> = mutableState.asStateFlow()

    init {
        scope.launch {
            var room = link.state.value.room
            link.state.collect { current ->
                if (!isHosting(current)) {
                    if (job != null) unpublish()
                } else if (current.room != room) {
                    wake.trySend(Unit)
                }
                room = current.room
            }
        }
    }

    /** Lists the open room with [password], which may be empty; does nothing while it is listed already. */
    @Synchronized
    fun publish(password: String) {
        val api = client ?: return
        val current = link.state.value
        val code = current.code
        if (!isHosting(current) || code == null || job?.isActive == true) return
        mutableState.value = PublishState(PublishPhase.Publishing, title(current), locked = password.isNotEmpty())
        job = scope.launch {
            val prefs = settings.settings.value.link
            val status = withContext(Dispatchers.IO) { MinecraftProbe.status(current.gamePort, ProbeTimeoutMillis) }
            val listing = LobbyListing(
                owner = identity.lobbyId,
                app = BuildConfig.VERSION_NAME,
                code = code,
                title = title(current),
                host = prefs.effectivePlayerName(identity),
                version = status?.version.orEmpty(),
                map = status?.worldName.orEmpty(),
                tags = prefs.lobbyTags.take(MaxTags),
                maxPlayers = current.room?.maxPlayers ?: 0,
                showRegion = prefs.lobbyShowRegion,
                description = prefs.lobbyDescription.trim(),
                voice = voiceOn(),
                password = password,
            )
            run(api, listing)
        }
    }

    /** Takes the room down from the lobby. */
    @Synchronized
    fun unpublish() {
        job?.cancel()
        job = null
        val listed = ticket
        ticket = null
        mutableState.value = PublishState()
        if (listed != null && client != null) {
            scope.launch(NonCancellable) { closeQuietly(client, listed) }
        }
    }

    private suspend fun run(api: LobbyClient, listing: LobbyListing) {
        while (true) {
            val wait: Long = try {
                val held = ticket
                if (held == null) {
                    val players = playerCount()
                    val listed = withContext(Dispatchers.IO) { api.publish(listing, players) }
                    ticket = listed
                    telemetry.count("lobby.publish")
                    link.log.info("The room is listed in the lobby.")
                    mutableState.value = PublishState(PublishPhase.Listed, listed.title, listing.password.isNotEmpty())
                    listed.heartbeatSeconds * 1000L
                } else {
                    val current = link.state.value
                    val title = title(current)
                    val seconds = withContext(Dispatchers.IO) {
                        api.heartbeat(held, title, playerCount(), current.room?.maxPlayers ?: 0, voiceOn())
                    }
                    mutableState.value = PublishState(PublishPhase.Listed, title, listing.password.isNotEmpty())
                    seconds * 1000L
                }
            } catch (e: LobbyException) {
                when {
                    e.kind == LobbyError.NotFound && ticket != null -> {
                        link.log.info("The lobby listing expired; listing the room again.")
                        ticket = null
                        continue
                    }
                    e.kind == LobbyError.Network || e.kind == LobbyError.TooMany || e.kind == LobbyError.Unexpected -> {
                        link.log.warn("The lobby could not be reached: " + e.message)
                        mutableState.value = mutableState.value.copy(phase = PublishPhase.Publishing, error = e.kind)
                        RetryDelayMillis
                    }
                    else -> {
                        link.log.warn("The room is no longer listed in the lobby: " + e.message)
                        ticket = null
                        mutableState.value = mutableState.value.copy(phase = PublishPhase.Stopped, error = e.kind, errorField = e.field)
                        return
                    }
                }
            }
            withTimeoutOrNull(wait) { wake.receive() }
        }
    }

    private suspend fun closeQuietly(api: LobbyClient, listed: LobbyTicket) {
        try {
            withContext(Dispatchers.IO) { api.close(listed) }
        } catch (e: LobbyException) {
            link.log.warn("The room could not be taken down from the lobby: " + e.message)
        }
    }

    private fun isHosting(state: LinkState): Boolean = state.role == LinkRole.Host && state.stage == LinkStage.Ready

    private fun title(state: LinkState): String {
        val name = state.room?.name?.trim().orEmpty().ifEmpty { link.defaultRoomName() }
        val points = name.codePointCount(0, name.length)
        return if (points <= MaxTitle) name else name.substring(0, name.offsetByCodePoints(0, MaxTitle))
    }

    private fun playerCount(): Int = link.state.value.players.size.coerceAtLeast(1)

    private fun voiceOn(): Boolean = voice.state.value.state == VoiceState.HostReady

    private companion object {
        const val RetryDelayMillis = 60_000L
        const val ProbeTimeoutMillis = 1_500
        const val MaxTitle = 24
        const val MaxTags = 3
    }
}
