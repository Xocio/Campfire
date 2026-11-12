package top.mccampfire.core.link

import top.mccampfire.core.link.room.PlayerProfile
import top.mccampfire.core.link.room.RoomInfo
import top.mccampfire.core.link.room.VoiceOffer
import top.mccampfire.core.link.runtime.NatKind
import top.mccampfire.core.link.runtime.NetworkPeer

/** Role of this device in a room. */
enum class LinkRole { Host, Guest }

/** Phase of a session. */
enum class LinkStage { Idle, Working, Ready, Failed }

/** Steps shown while a session is set up. */
enum class LinkStep { World, Network, FindHost, Enter }

/** Why a session could not start or ended. */
enum class LinkError {
    NetworkStartFailed,
    NetworkCrashed,
    NoNodes,
    RoomNotFound,
    NoDirectPath,
    HostNoAnswer,
    HostLost,
    ForwardFailed,
    LocalPortBusy,
    Removed,
    Blocked,
    RoomFull,
    NotAllowed,
    PortNotGame,
    WorldClosed,
    Unexpected,
}

/** Everything the pages show about the current session. */
data class LinkState(
    val stage: LinkStage = LinkStage.Idle,
    val role: LinkRole? = null,
    val step: LinkStep? = null,
    val code: RoomCode? = null,
    val players: List<PlayerProfile> = emptyList(),
    val room: RoomInfo? = null,
    /** Guest: local port that leads to the host's world. Host: port of the LAN world. */
    val gamePort: Int = 0,
    /** Guest: the host as seen by the virtual network. */
    val hostPeer: NetworkPeer? = null,
    /** Host: guests reached directly and through a relay. */
    val directCount: Int = 0,
    val relayCount: Int = 0,
    val localNat: NatKind = NatKind.Unknown,
    /** Connected public nodes, or -1 before the first check. */
    val connectedNodes: Int = -1,
    /** Guest: how long the host has been searched for. */
    val searchMillis: Long = 0,
    val error: LinkError? = null,
    /** Extra text for [error], such as an exception message. */
    val errorDetail: String? = null,
    /** Host: the world closed and the room waits for it to open again. */
    val waitingForWorld: Boolean = false,
    /** Guest: the host's voice chat, or null while it is off. */
    val hostVoice: VoiceOffer? = null,
    /** Guest: the local UDP port forwarded to the host's voice server, or 0. */
    val voiceForwardPort: Int = 0,
    /** Guest: the UDP port of the host's voice server is in use on this device. */
    val voiceForwardBusy: Boolean = false,
) {
    val isActive: Boolean get() = stage == LinkStage.Working || stage == LinkStage.Ready
}

/** Values a session reads from the settings when it starts. */
data class LinkOptions(
    val playerName: String,
    val gameName: String?,
    val machineId: String,
    val vendor: String,
    val allowRelay: Boolean,
    val hideAddress: Boolean,
    val searchTimeoutSeconds: Int,
    val localPort: Int,
    val lanEntryName: String,
    val nodes: List<String>,
    /** Host: only players on the allowlist may join. */
    val allowlistOnly: Boolean = false,
)

/** Localized texts a session needs for the LAN entry. */
interface LinkTexts {
    /** Default name of the LAN entry. */
    val defaultLanEntry: String

    /** Status after the entry name, such as "3 players · 25 ms"; [latency] is null when not measured. */
    fun lanStatus(players: Int, latency: String?): String
}

/** Writes session events to a log. */
interface LinkLogger {
    fun info(message: String)
    fun warn(message: String)
    fun error(message: String)
}
