package top.mccampfire.core.link

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import top.mccampfire.core.link.runtime.NetworkConfig
import top.mccampfire.core.link.runtime.NetworkRuntime
import java.security.SecureRandom

/** Measures the round-trip time to public nodes with a short-lived network of this device alone. */
class NodeProbe(
    private val runtime: NetworkRuntime,
    private val timeoutMillis: Long = 15_000,
    private val pollMillis: Long = 1_000,
) {
    /** Latency of [url] in milliseconds, or null when the node does not answer in time. */
    suspend fun measure(url: String): Double? {
        val name = "campfire-probe-" + randomHex(4)
        val config = NetworkConfig(
            instanceName = name,
            networkName = "campfire-probe-" + randomHex(4),
            networkSecret = randomHex(8),
            hostname = "campfire-probe",
            peers = listOf(url),
            hideAddress = true,
        )
        val started = runCatching { withContext(Dispatchers.IO) { runtime.start(config) } }.isSuccess
        if (!started) return null
        try {
            var first: Double? = null
            val polls = (timeoutMillis / pollMillis).coerceAtLeast(1)
            repeat(polls.toInt()) {
                delay(pollMillis)
                val latency = withContext(Dispatchers.IO) { runtime.info(name) }?.nodeLatencyMs ?: return@repeat
                if (first == null) first = latency else return minOf(first!!, latency)
            }
            return first
        } finally {
            withContext(Dispatchers.IO) { runCatching { runtime.stop(name) } }
        }
    }

    /** Latency of every node in [nodes] by id, measured at the same time. */
    suspend fun measureAll(nodes: List<PublicNode>): Map<String, Double?> = coroutineScope {
        nodes.map { node -> async { node.id to measure(node.url) } }.awaitAll().toMap()
    }

    private companion object {
        val Random = SecureRandom()

        fun randomHex(bytes: Int): String = ByteArray(bytes).also(Random::nextBytes).joinToString("") { "%02x".format(it) }
    }
}
