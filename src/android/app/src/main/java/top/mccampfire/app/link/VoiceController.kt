package top.mccampfire.app.link

import android.content.Context
import android.net.Uri
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import top.mccampfire.app.data.CampfireService
import top.mccampfire.app.data.SettingsRepository
import top.mccampfire.app.game.GameFolderRef
import top.mccampfire.app.game.GameFolders
import top.mccampfire.app.game.GameVersion
import top.mccampfire.app.game.VoiceInstall
import top.mccampfire.app.game.VoiceInstallException
import top.mccampfire.app.game.VoiceMod
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.LinkStage
import top.mccampfire.core.link.LinkState
import top.mccampfire.core.link.room.VoiceOffer

/** States the voice row of the room page shows. */
enum class VoiceState {
    HostOff, HostNeedFolder, HostInstalling, HostRestart, HostReady, HostProblem,
    GuestOff, GuestChecking, GuestWaiting, GuestNoFolder, GuestNoVersion, GuestMissing, GuestInstalling, GuestInstalled,
    GuestReady, GuestPortBusy, GuestProblem,
}

/** What the voice row shows: the state, the version it concerns and the port or problem. */
data class VoiceUi(
    val state: VoiceState,
    val version: String? = null,
    val port: Int = 0,
    val problem: VoiceInstallException.Problem? = null,
)

/** Voice chat of the room: installs the mod into the game, tells players the host's voice server and checks the guest side. */
class VoiceController(
    context: Context,
    private val settings: SettingsRepository,
    private val link: LinkController,
) {
    private val appContext = context.applicationContext
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val mod = VoiceMod(appContext.cacheDir, CampfireService.userAgent)
    private val mutableState = MutableStateFlow(VoiceUi(VoiceState.HostOff))
    private var hostJob: Job? = null
    private var guestJob: Job? = null
    private var guestInstalled: String? = null

    val state: StateFlow<VoiceUi> = mutableState.asStateFlow()

    init {
        scope.launch {
            link.state.map { listOf(it.stage, it.role, it.hostVoice, it.voiceForwardPort, it.voiceForwardBusy) }.distinctUntilChanged().collect { refresh() }
        }
    }

    private val prefs get() = settings.settings.value.link

    /** Every version in the granted game folders, most recently played first. */
    suspend fun versions(): List<GameVersion> = withContext(Dispatchers.IO) {
        prefs.gameFolders.flatMap { GameFolders.versions(appContext, it) }.sortedByDescending { it.lastPlayed }
    }

    /** Host: installs the mod into [version] and offers voice chat to the room. */
    fun turnOn(version: GameVersion) {
        hostJob?.cancel()
        mutableState.value = VoiceUi(VoiceState.HostInstalling, version.name)
        hostJob = scope.launch {
            try {
                val (result, _) = withContext(Dispatchers.IO) { mod.install(version, null) }
                withContext(Dispatchers.IO) { mod.enableLocalServer(version.gameDir) }
                settings.update { it.copy(link = it.link.copy(voice = true, voiceVersion = version.key)) }
                link.log.info("Voice chat mod ${result.name.lowercase()} for ${version.name}.")
                watchHost(version, restartFirst = result != VoiceInstall.Present)
            } catch (e: VoiceInstallException) {
                link.log.warn("Voice chat could not be turned on: ${e.key}")
                mutableState.value = VoiceUi(VoiceState.HostProblem, version.name, problem = e.key)
            }
        }
    }

    /** Host: stops offering voice chat. */
    fun turnOff() {
        hostJob?.cancel()
        settings.update { it.copy(link = it.link.copy(voice = false)) }
        link.setVoice(null)
        mutableState.value = VoiceUi(if (prefs.gameFolders.isEmpty()) VoiceState.HostNeedFolder else VoiceState.HostOff)
    }

    /** Guest: installs the mod into [version] to match the host's. */
    fun installForGuest(version: GameVersion) {
        guestJob?.cancel()
        mutableState.value = VoiceUi(VoiceState.GuestInstalling, version.name)
        guestJob = scope.launch {
            try {
                withContext(Dispatchers.IO) { mod.install(version, link.state.value.hostVoice?.mod) }
                withContext(Dispatchers.IO) { mod.enableLocalServer(version.gameDir) }
                guestInstalled = version.key
                mutableState.value = VoiceUi(VoiceState.GuestInstalled, version.name)
            } catch (e: VoiceInstallException) {
                mutableState.value = VoiceUi(VoiceState.GuestProblem, version.name, problem = e.key)
            }
        }
    }

    /** Re-reads the state after the room, the host's offer or the folders changed. */
    fun refresh() {
        val current = link.state.value
        when {
            current.stage == LinkStage.Ready && current.role == LinkRole.Host -> refreshHost()
            current.stage == LinkStage.Ready && current.role == LinkRole.Guest -> refreshGuest(current)
            else -> {
                hostJob?.cancel()
                guestJob?.cancel()
                guestInstalled = null
                mutableState.value = VoiceUi(VoiceState.HostOff)
            }
        }
    }

    private fun refreshHost() {
        if (hostJob?.isActive == true) return
        if (!prefs.voice) {
            mutableState.value = VoiceUi(if (prefs.gameFolders.isEmpty()) VoiceState.HostNeedFolder else VoiceState.HostOff)
            return
        }
        hostJob = scope.launch {
            val version = versions().firstOrNull { it.key == prefs.voiceVersion }
            if (version == null) {
                mutableState.value = VoiceUi(VoiceState.HostNeedFolder)
                return@launch
            }
            watchHost(version, restartFirst = false)
        }
    }

    /** Host: reads the game log for the voice server and keeps the room's offer current. */
    private suspend fun watchHost(version: GameVersion, restartFirst: Boolean) {
        var waitRestart = restartFirst
        val startedAt = System.currentTimeMillis()
        while (true) {
            val log = withContext(Dispatchers.IO) { version.gameDir.find("logs/latest.log") }
            val port = if (waitRestart && (log?.lastModified ?: 0) < startedAt) null else withContext(Dispatchers.IO) { mod.serverPort(version.gameDir) }
            if (port != null) waitRestart = false
            link.setVoice(VoiceOffer(port != null, port ?: VoiceOffer.DefaultPort, version.gameVersion, version.loader))
            if (port != null) link.openVoicePort(port)
            mutableState.value = VoiceUi(if (port != null) VoiceState.HostReady else VoiceState.HostRestart, version.name, port ?: 0)
            delay(5_000)
        }
    }

    private fun refreshGuest(current: LinkState) {
        val offer = current.hostVoice
        if (offer == null) {
            mutableState.value = VoiceUi(VoiceState.GuestOff)
            return
        }
        if (guestJob?.isActive == true) return
        guestJob = scope.launch {
            mutableState.value = VoiceUi(VoiceState.GuestChecking)
            if (prefs.gameFolders.isEmpty()) {
                mutableState.value = VoiceUi(VoiceState.GuestNoFolder)
                return@launch
            }
            val matches = versions().filter { it.gameVersion == offer.gameVersion && loaderFits(it.loader, offer.loader) }
            val withMod = withContext(Dispatchers.IO) { matches.firstOrNull { it.key == guestInstalled || mod.find(it.gameDir) != null } }
            mutableState.value = when {
                matches.isEmpty() -> VoiceUi(VoiceState.GuestNoVersion)
                withMod == null -> VoiceUi(VoiceState.GuestMissing, matches.first().name)
                current.voiceForwardBusy -> VoiceUi(VoiceState.GuestPortBusy, withMod.name, offer.port)
                offer.port != current.voiceForwardPort -> VoiceUi(VoiceState.GuestChecking, withMod.name)
                !offer.ready -> VoiceUi(VoiceState.GuestWaiting, withMod.name)
                else -> VoiceUi(VoiceState.GuestReady, withMod.name)
            }
        }
    }

    /** Guest: the version the install button targets. */
    suspend fun guestTarget(): GameVersion? {
        val offer = link.state.value.hostVoice ?: return null
        return versions().firstOrNull { it.gameVersion == offer.gameVersion && loaderFits(it.loader, offer.loader) }
    }

    /** Adds a granted folder; returns false when it holds no game folder. */
    suspend fun addFolder(tree: Uri, launcher: String): Boolean = withContext(Dispatchers.IO) {
        GameFolders.persist(appContext, tree)
        if (GameFolders.findMinecraft(appContext, tree) == null) {
            GameFolders.release(appContext, tree)
            return@withContext false
        }
        val ref = GameFolderRef(tree.toString(), launcher)
        settings.update { it.copy(link = it.link.copy(gameFolders = (it.link.gameFolders.filterNot { f -> f.tree == ref.tree } + ref))) }
        refresh()
        true
    }

    /** Removes a granted folder. */
    fun removeFolder(ref: GameFolderRef) {
        GameFolders.release(appContext, Uri.parse(ref.tree))
        settings.update { it.copy(link = it.link.copy(gameFolders = it.link.gameFolders.filterNot { f -> f.tree == ref.tree })) }
        refresh()
    }

    private fun loaderFits(mine: String, host: String?): Boolean = when (host) {
        null -> mine != "vanilla"
        "quilt", "fabric" -> mine == "fabric" || mine == "quilt"
        else -> mine == host
    }

}
