package top.mccampfire.app.link

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancelChildren
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.selects.select
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.jsonObject
import top.mccampfire.app.BuildConfig
import top.mccampfire.app.R
import top.mccampfire.app.data.AllowedPlayer
import top.mccampfire.app.data.LinkIdentity
import top.mccampfire.app.data.LinkPrefs
import top.mccampfire.app.data.LocaleHelper
import top.mccampfire.app.data.SettingsRepository
import top.mccampfire.app.data.Telemetry
import top.mccampfire.app.data.effectivePlayerName
import top.mccampfire.core.link.LinkOptions
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.LinkSession
import top.mccampfire.core.link.LinkState
import top.mccampfire.core.link.LinkTexts
import top.mccampfire.core.link.PublicNodes
import top.mccampfire.core.link.RoomCode
import top.mccampfire.core.link.room.PlayerKind
import top.mccampfire.core.link.room.PlayerProfile
import top.mccampfire.core.link.room.RoomGate
import top.mccampfire.core.link.room.RoomInfo
import top.mccampfire.core.link.minecraft.MinecraftProbe
import top.mccampfire.app.game.GameFolders
import top.mccampfire.core.link.room.VoiceOffer
import top.mccampfire.easytier.EasyTierVersion
import top.mccampfire.easytier.JniNetworkRuntime

/** Owns the room session of this process and starts the service that keeps it running. */
class LinkController(
    context: Context,
    private val settings: SettingsRepository,
    private val identity: LinkIdentity,
    private val telemetry: Telemetry,
) {
    private val appContext = context.applicationContext
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    /** Engine of the virtual network. */
    val runtime = JniNetworkRuntime()

    /** Multiplayer log. */
    val log = LinkLogFile(appContext)

    private val session = LinkSession(runtime, Texts(), log, scope)

    /** Speed test of the built-in nodes. */
    val nodeTester = NodeTester(runtime, scope, log)

    private val gate = object : RoomGate {
        override fun isAllowed(player: PlayerProfile): Boolean =
            settings.settings.value.link.allowlist.any { it.id == player.machineId }
    }

    /** The current session. */
    val state: StateFlow<LinkState> = session.state

    init {
        if (BuildConfig.DEBUG) {
            scope.launch {
                while (true) {
                    delay(15_000)
                    if (state.value.isActive) runtime.rawInfo()?.let { log.info("Network info: " + withoutEvents(it)) }
                }
            }
        }
    }

    private fun withoutEvents(json: String): String = runCatching {
        val root = kotlinx.serialization.json.Json.parseToJsonElement(json).jsonObject
        val map = root["map"]?.jsonObject ?: return json
        kotlinx.serialization.json.JsonObject(map.mapValues { (_, value) -> kotlinx.serialization.json.JsonObject(value.jsonObject - "events") }).toString()
    }.getOrDefault(json)

    /** Joins the room behind [code]. */
    fun join(code: RoomCode) {
        session.join(code, options())
        telemetry.count("link.join")
        LinkService.start(appContext)
    }

    /** Opens a room for the LAN world on [gamePort] of this device, or for the next one announced when null. */
    fun host(gamePort: Int?) {
        val options = options()
        val world: suspend () -> Int = if (gamePort != null) {
            { gamePort }
        } else {
            {
                val listener = LanWorldListener(appContext)
                try {
                    listener.start()
                    coroutineScope {
                        val announced = async { listener.worlds.first { it.isNotEmpty() }.first().port }
                        val logged = async { waitForLoggedWorld() }
                        select {
                            announced.onAwait { it }
                            logged.onAwait { it }
                        }.also { coroutineContext.cancelChildren() }
                    }
                } finally {
                    listener.close()
                }
            }
        }
        session.host(world, options, roomInfo(settings.settings.value.link), gate)
        telemetry.count("link.host")
        LinkService.start(appContext)
    }

    /** Name used for a room when the host has not set one. */
    fun defaultRoomName(): String {
        val prefs = settings.settings.value.link
        return LocaleHelper.wrap(appContext, settings.settings.value.language)
            .getString(R.string.host_default_room_name, prefs.effectivePlayerName(identity))
    }

    private fun roomInfo(prefs: LinkPrefs): RoomInfo =
        RoomInfo.of(prefs.roomName.ifBlank { defaultRoomName() }, prefs.roomNotice, prefs.maxPlayers)

    /** Saves the room's name, notice and player limit, and applies them to the open room. */
    fun saveRoom(name: String, notice: String, maxPlayers: Int) {
        settings.update { it.copy(link = it.link.copy(roomName = name.trim(), roomNotice = notice.trim(), maxPlayers = maxPlayers)) }
        if (state.value.role == LinkRole.Host && state.value.isActive) session.updateRoom(roomInfo(settings.settings.value.link))
    }

    /** Turns the allowlist on or off; turning it on adds the players in the open room. */
    fun setAllowlistOnly(on: Boolean) {
        val guests = if (on && state.value.role == LinkRole.Host) {
            state.value.players.filter { it.kind == PlayerKind.Guest }.map { AllowedPlayer(it.machineId, it.name) }
        } else {
            emptyList()
        }
        settings.update { current ->
            val list = (current.link.allowlist + guests).distinctBy { it.id }
            current.copy(link = current.link.copy(allowlistOnly = on, allowlist = list))
        }
        session.setAllowlistOnly(on)
    }

    /** Adds [player] to the allowlist. */
    fun allow(player: PlayerProfile) = settings.update { current ->
        val list = current.link.allowlist.filterNot { it.id == player.machineId } + AllowedPlayer(player.machineId, player.name)
        current.copy(link = current.link.copy(allowlist = list))
    }

    /** Takes the player with [machineId] off the allowlist. */
    fun disallow(machineId: String) = settings.update { current ->
        current.copy(link = current.link.copy(allowlist = current.link.allowlist.filterNot { it.id == machineId }))
    }

    /** Port of a LAN world the game in a granted folder reports in its log and that answers on this device. */
    private suspend fun waitForLoggedWorld(): Int {
        val since = System.currentTimeMillis() - LoggedWorldWindowMillis
        val dirs = withContext(Dispatchers.IO) {
            settings.settings.value.link.gameFolders.flatMap { GameFolders.versions(appContext, it) }.map { it.gameDir }.distinctBy { it.documentId }
        }
        if (dirs.isEmpty()) awaitCancellation()
        while (true) {
            for (dir in dirs) {
                val port = withContext(Dispatchers.IO) {
                    val log = dir.find("logs/latest.log")?.takeIf { it.lastModified >= since } ?: return@withContext null
                    log.readTail(LogTailBytes)?.let { text -> ServingLine.findAll(text).lastOrNull()?.groupValues?.get(1)?.toIntOrNull() }
                } ?: continue
                if (withContext(Dispatchers.IO) { MinecraftProbe.isServer(port, 1_000) }) {
                    log.info("Found the LAN world on port $port in the game log.")
                    return port
                }
            }
            delay(2_000)
        }
    }

    /** Host: takes a guest out of the room for [durationMillis], or until the room closes when null. */
    fun removePlayer(machineId: String, durationMillis: Long?) {
        session.removePlayer(machineId, if (durationMillis == null) Long.MAX_VALUE else System.currentTimeMillis() + durationMillis)
    }

    /** Host: the voice chat players receive, or null to turn it off. */
    fun setVoice(offer: VoiceOffer?) = session.setVoice(offer)

    /** Host: opens the UDP [port] of the game's voice server to the room. */
    fun openVoicePort(port: Int) = session.openVoicePort(port)

    /** Leaves or closes the room. */
    fun stop() = session.stop()

    /** Returns to the idle state after a failure. */
    fun dismissFailure() = session.dismissFailure()

    private fun options(): LinkOptions {
        val prefs = settings.settings.value.link
        return LinkOptions(
            playerName = prefs.effectivePlayerName(identity),
            gameName = prefs.gameName.takeIf(LinkPrefs::isValidGameName),
            machineId = identity.machineId,
            vendor = if (prefs.shareVersion) "Campfire Android ${BuildConfig.VERSION_NAME}, EasyTier v$EasyTierVersion" else "Campfire",
            allowRelay = prefs.allowRelay,
            hideAddress = prefs.hideAddress,
            searchTimeoutSeconds = prefs.searchTimeoutSeconds.takeIf { it in LinkPrefs.SearchTimeoutChoices } ?: 60,
            localPort = prefs.localPort.takeIf { it in 1..65535 } ?: 0,
            lanEntryName = prefs.lanEntryName,
            nodes = nodes(prefs),
            allowlistOnly = prefs.allowlistOnly,
        )
    }

    private fun nodes(prefs: LinkPrefs): List<String> {
        val disabled = prefs.disabledNodes.split(',').filter { it.isNotBlank() }.toSet()
        val enabled = if (prefs.usePublicNodes) PublicNodes.All.filter { it.id !in disabled } else emptyList()
        val test = nodeTester.state.value
        val picked = if (prefs.autoPickNodes) {
            PublicNodes.pick(enabled, test.latencies, test.testedAtMillis, System.currentTimeMillis())
        } else {
            enabled
        }
        val builtIn = picked.map { it.url }
        return (PublicNodes.parseCustom(prefs.customNodes) + builtIn).distinctBy { it.lowercase() }
    }

    private companion object {
        /** Logs older than this are not read for a LAN world. */
        const val LoggedWorldWindowMillis = 6 * 60 * 60 * 1000L
        const val LogTailBytes = 512L * 1024
        val ServingLine = Regex("""Started serving on (\d{1,5})""")
    }

    /** Texts in the language chosen in the app. */
    private inner class Texts : LinkTexts {
        private fun localized(): Context = LocaleHelper.wrap(appContext, settings.settings.value.language)

        override val defaultLanEntry: String get() = localized().getString(R.string.link_lan_entry)

        override fun lanStatus(players: Int, latency: String?): String = if (latency == null) {
            localized().getString(R.string.link_lan_status, players)
        } else {
            localized().getString(R.string.link_lan_status_latency, players, latency)
        }
    }
}
