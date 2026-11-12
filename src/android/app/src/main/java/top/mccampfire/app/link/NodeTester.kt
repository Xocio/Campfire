package top.mccampfire.app.link

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import top.mccampfire.core.link.NodeProbe
import top.mccampfire.core.link.PublicNode
import top.mccampfire.core.link.PublicNodes
import top.mccampfire.core.link.runtime.NetworkRuntime

/** Latest speed test of the built-in nodes. */
data class NodeTest(
    /** Latency of each tested node id; a null value means the node did not answer. */
    val latencies: Map<String, Double?> = emptyMap(),
    val testedAtMillis: Long? = null,
    val running: Boolean = false,
) {
    /** Lowest latency among [ids], or null when none of them answered. */
    fun best(ids: Collection<String>): Double? = ids.mapNotNull { latencies[it] }.minOrNull()
}

/** Runs one speed test of the built-in nodes at a time and keeps the result. */
class NodeTester(runtime: NetworkRuntime, private val scope: CoroutineScope, private val log: LinkLogFile) {
    private val probe = NodeProbe(runtime)
    private val mutableState = MutableStateFlow(NodeTest())
    private var current: Deferred<NodeTest>? = null

    val state: StateFlow<NodeTest> = mutableState.asStateFlow()

    /** Starts a test of every built-in node, or returns the one already running. */
    @Synchronized
    fun test(): Deferred<NodeTest> {
        current?.takeIf { it.isActive }?.let { return it }
        mutableState.update { it.copy(running = true) }
        return scope.async {
            val nodes: List<PublicNode> = PublicNodes.All
            val result = try {
                probe.measureAll(nodes)
            } finally {
                mutableState.update { it.copy(running = false) }
            }
            log.info("Node test: " + nodes.joinToString { node -> node.id + "=" + (result[node.id]?.let { "%.0f".format(it) } ?: "-") })
            NodeTest(result, System.currentTimeMillis(), running = false).also { mutableState.value = it }
        }.also { current = it }
    }

    /** Starts a test when the last one is older than [PublicNodes.PickFreshnessMillis]. */
    fun refreshIfStale() {
        val tested = mutableState.value.testedAtMillis
        if (tested == null || System.currentTimeMillis() - tested > PublicNodes.PickFreshnessMillis) test()
    }
}
