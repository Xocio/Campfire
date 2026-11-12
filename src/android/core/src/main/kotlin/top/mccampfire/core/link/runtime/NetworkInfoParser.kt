package top.mccampfire.core.link.runtime

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.longOrNull

/** Reads the running information of an instance from the JSON the engine reports. */
object NetworkInfoParser {
    private val Parser = Json { ignoreUnknownKeys = true }

    /** The state of [instanceName] in [json]; null when the instance is not listed. */
    fun parse(json: String, instanceName: String): NetworkInfo? {
        val root = runCatching { Parser.parseToJsonElement(json) }.getOrNull() as? JsonObject ?: return null
        val instances = root["map"] as? JsonObject ?: root
        val info = instances[instanceName] as? JsonObject ?: return null
        return parseInstance(info)
    }

    private fun parseInstance(info: JsonObject): NetworkInfo {
        val myNode = info["my_node_info"] as? JsonObject
        val myPeerId = myNode.long("peer_id")
        val connections = mutableMapOf<Long, List<JsonObject>>()
        for (peer in info.array("peers")) {
            val id = peer.long("peer_id") ?: continue
            connections[id] = peer.array("conns").filter { it.bool("is_closed") != true }
        }

        val peers = mutableListOf<NetworkPeer>()
        if (myNode != null) {
            peers += NetworkPeer(
                hostname = myNode.text("hostname"),
                ipv4 = ipv4(myNode["virtual_ipv4"]),
                isLocal = true,
                isDirect = false,
                latencyMs = null,
                lossPercent = null,
                tunnel = null,
                nat = nat(myNode["stun_info"]),
            )
        }

        val members = mutableSetOf<Long>()
        for (route in info.array("routes")) {
            val id = route.long("peer_id") ?: continue
            if (id == myPeerId) continue
            val address = ipv4(route["ipv4_addr"])
            val publicServer = (route["feature_flag"] as? JsonObject)?.bool("is_public_server") == true
            if (publicServer || address.isEmpty()) continue
            members += id
            val conns = connections[id].orEmpty()
            val direct = conns.isNotEmpty()
            val connLatency = conns.mapNotNull { (it["stats"] as? JsonObject).long("latency_us") }.filter { it > 0 }.minOrNull()
            val latency = if (direct && connLatency != null) connLatency / 1000.0 else route.double("path_latency")?.takeIf { it >= 0 }
            peers += NetworkPeer(
                hostname = route.text("hostname"),
                ipv4 = address,
                isLocal = false,
                isDirect = direct,
                latencyMs = latency,
                lossPercent = conns.mapNotNull { it.double("loss_rate") }.minOrNull()?.let(::lossPercent),
                tunnel = conns.firstNotNullOfOrNull { (it["tunnel"] as? JsonObject)?.text("tunnel_type")?.ifEmpty { null } },
                nat = nat(route["stun_info"]),
            )
        }

        val nodeConnections = connections.filter { (id, conns) -> id != myPeerId && id !in members && conns.isNotEmpty() }
        val nodes = nodeConnections.size
        val nodeLatency = nodeConnections.values.flatten()
            .mapNotNull { (it["stats"] as? JsonObject).long("latency_us") }
            .filter { it > 0 }
            .minOrNull()?.let { it / 1000.0 }
        return NetworkInfo(
            running = info.bool("running") ?: false,
            errorMessage = info.text("error_msg").ifEmpty { null },
            peers = peers,
            connectedNodes = nodes,
            nodeLatencyMs = nodeLatency,
        )
    }

    /** Reads "1.5%", "1.5" (percent) or a fraction such as 0.015 as a percentage. */
    private fun lossPercent(value: Double): Double? {
        if (value.isNaN() || value < 0) return null
        val percent = if (value <= 1.0) value * 100 else value
        return percent.takeIf { it <= 100 }
    }

    /** Reads an address written as "a.b.c.d/len", "a.b.c.d" or `{address: {addr: u32}}`. */
    internal fun ipv4(value: JsonElement?): String = when (value) {
        is JsonPrimitive -> value.content.substringBefore('/').takeIf { parseIpv4(it) != null }.orEmpty()
        is JsonObject -> {
            val address = value["address"]
            val raw = when (address) {
                is JsonObject -> address.long("addr")
                is JsonPrimitive -> address.longOrNull ?: return ipv4(address)
                else -> value.long("addr")
            }
            raw?.takeIf { it in 1..0xFFFFFFFFL }?.let {
                "${(it shr 24) and 0xFF}.${(it shr 16) and 0xFF}.${(it shr 8) and 0xFF}.${it and 0xFF}"
            }.orEmpty()
        }
        else -> ""
    }

    /** Reads the UDP NAT type of a STUN result, written as a number or a name. */
    internal fun nat(value: JsonElement?): NatKind {
        val stun = value as? JsonObject ?: return NatKind.Unknown
        val type = stun["udp_nat_type"] as? JsonPrimitive ?: return NatKind.Unknown
        val name = if (type.isString) type.content else NatNames.getOrNull(type.longOrNull?.toInt() ?: -1)
        return when (name) {
            "OpenInternet", "NoPat", "NoPAT" -> NatKind.Open
            "FullCone" -> NatKind.FullCone
            "Restricted" -> NatKind.Restricted
            "PortRestricted" -> NatKind.PortRestricted
            "Symmetric", "SymUdpFirewall", "SymmetricEasyInc", "SymmetricEasyDec" -> NatKind.Symmetric
            else -> NatKind.Unknown
        }
    }

    private val NatNames = listOf(
        "Unknown", "OpenInternet", "NoPat", "FullCone", "Restricted", "PortRestricted", "Symmetric", "SymUdpFirewall",
        "SymmetricEasyInc", "SymmetricEasyDec",
    )

    private fun JsonObject.array(key: String): List<JsonObject> = (this[key] as? JsonArray)?.filterIsInstance<JsonObject>().orEmpty()

    private fun JsonObject.text(key: String): String = (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content.orEmpty()

    private fun JsonObject?.long(key: String): Long? = (this?.get(key) as? JsonPrimitive)?.longOrNull

    private fun JsonObject.double(key: String): Double? = (this[key] as? JsonPrimitive)?.doubleOrNull

    private fun JsonObject.bool(key: String): Boolean? = (this[key] as? JsonPrimitive)?.booleanOrNull
}
