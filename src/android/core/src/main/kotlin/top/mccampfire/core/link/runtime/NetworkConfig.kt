package top.mccampfire.core.link.runtime

/** Settings of one virtual network instance, written as TOML. */
data class NetworkConfig(
    val instanceName: String,
    val networkName: String,
    val networkSecret: String,
    val hostname: String,
    /** A fixed virtual address, or null to get one automatically. */
    val ipv4: String? = null,
    val peers: List<String> = emptyList(),
    /** Ports other members may reach on this device. "0" alone blocks every incoming connection. */
    val tcpWhitelist: List<String> = listOf("0"),
    val udpWhitelist: List<String> = listOf("0"),
    /** Traffic only flows over direct connections and never through a relay node. */
    val directOnly: Boolean = false,
    /** This device never connects directly to other members and does not listen for them. */
    val hideAddress: Boolean = false,
    /** Loopback port of the SOCKS5 server that reaches the network, or null for none. */
    val socksPort: Int? = null,
    /** Local ports forwarded to addresses in the network. */
    val portForwards: List<PortForward> = emptyList(),
) {
    fun toToml(): String = buildString {
        appendLine("instance_name = " + quote(instanceName))
        appendLine("hostname = " + quote(hostname))
        if (ipv4 != null) appendLine("ipv4 = " + quote(ipv4)) else appendLine("dhcp = true")
        appendLine(if (hideAddress) "listeners = []" else "listeners = [\"tcp://0.0.0.0:0\", \"udp://0.0.0.0:0\"]")
        appendLine("tcp_whitelist = " + array(tcpWhitelist))
        appendLine("udp_whitelist = " + array(udpWhitelist))
        if (socksPort != null) appendLine("socks5_proxy = " + quote("socks5://127.0.0.1:$socksPort"))
        appendLine()

        appendLine("[network_identity]")
        appendLine("network_name = " + quote(networkName))
        appendLine("network_secret = " + quote(networkSecret))
        appendLine()

        for (peer in peers) {
            appendLine("[[peer]]")
            appendLine("uri = " + quote(peer))
            appendLine()
        }

        for (forward in portForwards) {
            appendLine("[[port_forward]]")
            appendLine("bind_addr = " + quote(forward.bind))
            appendLine("dst_addr = " + quote(forward.destination))
            appendLine("proto = " + quote(forward.protocol))
            appendLine()
        }

        appendLine("[flags]")
        appendLine("no_tun = true")
        appendLine("latency_first = true")
        appendLine("enable_kcp_proxy = true")
        appendLine("enable_quic_proxy = true")
        appendLine("data_compress_algo = 2")
        appendLine("private_mode = true")
        appendLine("multi_thread = true")
        appendLine("p2p_only = " + (directOnly && !hideAddress))
        if (hideAddress) {
            appendLine("disable_p2p = true")
            appendLine("disable_udp_hole_punching = true")
            appendLine("disable_tcp_hole_punching = true")
            appendLine("disable_sym_hole_punching = true")
            appendLine("disable_upnp = true")
        }
    }

    private fun array(values: List<String>): String = values.joinToString(", ", "[", "]") { quote(it) }

    private fun quote(value: String): String = buildString {
        append('"')
        for (c in value) {
            when {
                c == '"' || c == '\\' -> append('\\').append(c)
                c < ' ' -> append("\\u").append("%04X".format(c.code))
                else -> append(c)
            }
        }
        append('"')
    }
}

/** A local address forwarded to an address in the network; [protocol] is "tcp" or "udp". */
data class PortForward(val bind: String, val destination: String, val protocol: String)

/** A member of the virtual network as seen from this device. */
data class NetworkPeer(
    val hostname: String,
    /** Virtual IPv4 address; empty for public nodes, which are not network members. */
    val ipv4: String,
    val isLocal: Boolean,
    /** Traffic goes straight to this peer rather than through a relay. */
    val isDirect: Boolean,
    val latencyMs: Double?,
    val lossPercent: Double?,
    val tunnel: String?,
    val nat: NatKind,
) {
    val isMember: Boolean get() = ipv4.isNotEmpty()
}

/** How open a network is to incoming connections. */
enum class NatKind { Unknown, Open, FullCone, Restricted, PortRestricted, Symmetric }

/** State of the running instance. */
data class NetworkInfo(
    val running: Boolean,
    val errorMessage: String?,
    val peers: List<NetworkPeer>,
    /** Public nodes this device is connected to. */
    val connectedNodes: Int,
    /** Lowest round-trip time to a connected public node. */
    val nodeLatencyMs: Double? = null,
) {
    val local: NetworkPeer? get() = peers.firstOrNull { it.isLocal }
}

/** A virtual network engine that runs instances by name. */
interface NetworkRuntime {
    /** Starts an instance; throws [java.io.IOException] with the engine's message when it cannot start. */
    fun start(config: NetworkConfig)

    /** Stops the instance named [instanceName]. */
    fun stop(instanceName: String)

    /** Current state of [instanceName], or null when it is not running. */
    fun info(instanceName: String): NetworkInfo?
}
