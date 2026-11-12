package top.mccampfire.core.link

/** One built-in public node; pages show its region and provider, never its address. */
data class PublicNode(val id: String, val url: String, val provider: String, val limited: Boolean = false)

/** The built-in public nodes and how a room picks among them. */
object PublicNodes {
    /** Round-trip time above which a node counts as slow. */
    const val HighLatencyMs = 150.0

    /** Test results up to this age are used by [pick]. */
    const val PickFreshnessMillis = 60 * 60 * 1000L

    /** Community nodes that relay traffic, from several providers. */
    val All: List<PublicNode> = listOf(
        PublicNode("sh-ct", "tcp://225284.xyz:11010", "海波"),
        PublicNode("xm-ct", "tcp://easytier.weiai.org.cn:11010", "为爱，唯爱"),
        PublicNode("cq-cm", "tcp://183.230.36.171:11010", "Just the Two of Us"),
        PublicNode("zz-bgp", "udp://et.basd1.de:11010", "宇智波.希"),
        PublicNode("sh-bgp", "tcp://et.sumnight.top:11010", "夏夜", limited = true),
        PublicNode("cn-eo", "wss://et.chinokou.cn", "ChinoKou"),
        PublicNode("yn-cu", "https://et-public.lctn.top", "凉城听暖"),
        PublicNode("intl-cf", "wss://et.xn--6kr850c.com", "林然"),
        PublicNode("intl-wk", "https://etnode.zkitefly.eu.org/node2", "zkitefly"),
    )

    /**
     * The nodes of [enabled] a room connects to. With a test newer than [PickFreshnessMillis]: the ones that answered,
     * capped nodes last and then fastest first, or all of them when none answered. Without one: all of them in order.
     *
     * @param latencies Latency of each tested node id; a null value means the node did not answer.
     */
    fun pick(enabled: List<PublicNode>, latencies: Map<String, Double?>, testedAtMillis: Long?, nowMillis: Long): List<PublicNode> {
        if (testedAtMillis == null || nowMillis - testedAtMillis > PickFreshnessMillis) return enabled
        val measured = enabled.mapNotNull { node ->
            when {
                !latencies.containsKey(node.id) -> node to Double.MAX_VALUE
                latencies[node.id] != null -> node to latencies.getValue(node.id)!!
                else -> null
            }
        }
        if (measured.isEmpty()) return enabled
        return measured.sortedWith(compareBy({ if (it.first.limited) 1 else 0 }, { it.second })).map { it.first }
    }

    /**
     * Splits user input into node addresses. Spaces, commas, semicolons and line breaks separate entries; a bare
     * `host:port` is read as TCP. Entries that are not addresses go to [invalid].
     */
    fun parseCustom(text: String, invalid: MutableList<String>? = null): List<String> {
        val nodes = mutableListOf<String>()
        if (text.isBlank()) return nodes
        for (part in text.split(' ', '\t', '\r', '\n', ',', ';', '，', '；', '、').filter { it.isNotEmpty() }) {
            val node = if ("://" in part) part else "tcp://$part"
            val scheme = node.substringBefore("://").lowercase()
            val host = node.substringAfter("://").substringBefore('/').substringBefore('?')
                .let { if (it.startsWith("[")) it.substringBefore(']') + "]" else it.substringBeforeLast(':') }
            if (scheme in Schemes && host.isNotEmpty() && host.none { it.isWhitespace() }) nodes += node else invalid?.add(part)
        }
        return nodes
    }

    private val Schemes = setOf("tcp", "udp", "ws", "wss", "quic", "http", "https", "txt", "srv")
}
