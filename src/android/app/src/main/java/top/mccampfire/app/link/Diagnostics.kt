package top.mccampfire.app.link

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.os.Build
import androidx.annotation.StringRes
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import top.mccampfire.app.R
import top.mccampfire.app.data.LinkIdentity
import top.mccampfire.app.data.LinkPrefs
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.LinkSession
import top.mccampfire.core.link.PublicNodes
import top.mccampfire.core.link.runtime.TcpForwarder
import top.mccampfire.easytier.EasyTierVersion
import java.net.Inet6Address

/** Outcome of one diagnostic check. */
enum class DiagState {
    Pending,
    Running,
    Ok,

    /** Nothing wrong, but worth knowing. */
    Info,
    Warn,
    Fail,
}

/** The checks of multiplayer diagnostics, in the order they run. */
enum class DiagCheck(@StringRes val title: Int) {
    System(R.string.diag_system),
    Component(R.string.diag_component),
    Name(R.string.diag_name),
    Network(R.string.diag_network),
    Nodes(R.string.diag_nodes),
    Relay(R.string.diag_relay),
    Port(R.string.diag_port),
    World(R.string.diag_world),
}

/** Result of a check: its state and a text resource with arguments. */
data class DiagResult(val state: DiagState, @StringRes val detail: Int? = null, val args: List<Any> = emptyList())

/** What a room needs from this device; nothing is changed or sent to other players. */
class Diagnostics(
    private val context: Context,
    private val link: LinkController,
    private val identity: LinkIdentity,
    private val prefs: () -> LinkPrefs,
) {
    /** Runs [check] and returns its result. */
    suspend fun run(check: DiagCheck): DiagResult = when (check) {
        DiagCheck.System -> DiagResult(DiagState.Ok, R.string.diag_system_ok, listOf(Build.VERSION.RELEASE, Build.SUPPORTED_ABIS.firstOrNull().orEmpty()))
        DiagCheck.Component -> component()
        DiagCheck.Name -> name()
        DiagCheck.Network -> network()
        DiagCheck.Nodes -> nodes()
        DiagCheck.Relay -> relay()
        DiagCheck.Port -> port()
        DiagCheck.World -> world()
    }

    private suspend fun component(): DiagResult = withContext(Dispatchers.IO) {
        if (link.runtime.isAvailable()) {
            DiagResult(DiagState.Ok, R.string.diag_component_ok, listOf(EasyTierVersion))
        } else {
            DiagResult(DiagState.Fail, R.string.diag_component_broken)
        }
    }

    private fun name(): DiagResult {
        val name = prefs().playerName.trim()
        return if (name.isNotEmpty()) {
            DiagResult(DiagState.Ok, R.string.diag_name_ok, listOf(name))
        } else {
            DiagResult(DiagState.Info, R.string.diag_name_default, listOf(identity.fallbackName()))
        }
    }

    private fun network(): DiagResult {
        val connectivity = context.getSystemService(ConnectivityManager::class.java)
        val network = connectivity?.activeNetwork ?: return DiagResult(DiagState.Fail, R.string.diag_network_none)
        val caps = connectivity.getNetworkCapabilities(network) ?: return DiagResult(DiagState.Fail, R.string.diag_network_none)
        if (!caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)) return DiagResult(DiagState.Warn, R.string.diag_network_offline)
        if (caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) && !caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) {
            return DiagResult(DiagState.Info, R.string.diag_network_cellular)
        }
        val v6 = connectivity.getLinkProperties(network)?.linkAddresses.orEmpty().any { address ->
            val ip = address.address
            ip is Inet6Address && !ip.isLinkLocalAddress && !ip.isSiteLocalAddress && !ip.isLoopbackAddress &&
                (ip.address[0].toInt() and 0xE0) == 0x20
        }
        return DiagResult(DiagState.Ok, if (v6) R.string.diag_network_ok_v6 else R.string.diag_network_ok)
    }

    private suspend fun nodes(): DiagResult {
        val prefs = prefs()
        if (!prefs.usePublicNodes) return DiagResult(DiagState.Info, R.string.diag_nodes_own)
        val disabled = prefs.disabledNodes.split(',').filter { it.isNotBlank() }.toSet()
        val enabled = PublicNodes.All.filter { it.id !in disabled }.map { it.id }
        val test = link.nodeTester.test().await()
        val reachable = enabled.count { test.latencies[it] != null }
        val best = test.best(enabled) ?: return DiagResult(DiagState.Fail, R.string.diag_nodes_none)
        val state = if (best > PublicNodes.HighLatencyMs) DiagState.Warn else DiagState.Ok
        return DiagResult(state, R.string.diag_nodes_ok, listOf(reachable, enabled.size, LinkSession.formatLatency(best)))
    }

    private fun relay(): DiagResult {
        val prefs = prefs()
        return when {
            prefs.hideAddress -> DiagResult(DiagState.Info, R.string.diag_relay_hidden)
            prefs.allowRelay -> DiagResult(DiagState.Ok, R.string.diag_relay_ok)
            else -> DiagResult(DiagState.Warn, R.string.diag_relay_off)
        }
    }

    private suspend fun port(): DiagResult {
        val port = prefs().localPort.takeIf { it in 1..65535 } ?: return DiagResult(DiagState.Ok, R.string.diag_port_auto)
        val state = link.state.value
        val ours = state.isActive && state.role == LinkRole.Guest && state.gamePort == port
        val free = ours || withContext(Dispatchers.IO) { TcpForwarder.isPortFree(port) }
        return if (free) DiagResult(DiagState.Ok, R.string.diag_port_free, listOf(port)) else DiagResult(DiagState.Fail, R.string.diag_port_busy, listOf(port))
    }

    private suspend fun world(): DiagResult {
        if (!LocalNetwork.isGranted(context)) return DiagResult(DiagState.Warn, R.string.diag_world_permission)
        val listener = LanWorldListener(context)
        return try {
            listener.start()
            val worlds = withTimeoutOrNull(WorldWaitMillis) { listener.worlds.first { it.isNotEmpty() } }
            val world = worlds?.firstOrNull() ?: return DiagResult(DiagState.Info, R.string.diag_world_none)
            DiagResult(DiagState.Ok, R.string.diag_world_ok, listOf(world.port))
        } finally {
            listener.close()
        }
    }

    private companion object {
        const val WorldWaitMillis = 4_000L
    }
}
