package top.mccampfire.core.link

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import top.mccampfire.core.link.runtime.NetworkConfig
import top.mccampfire.core.link.runtime.NetworkInfo
import top.mccampfire.core.link.runtime.NetworkRuntime
import java.io.IOException

class NodeProbeTest {
    /** Runtime whose instances report the latencies of [readings] by peer url, one per poll. */
    private class FakeRuntime(private val readings: Map<String, List<Double?>>, private val failing: Set<String> = emptySet()) : NetworkRuntime {
        private val instances = mutableMapOf<String, String>()
        private val polls = mutableMapOf<String, Int>()
        val started = mutableListOf<NetworkConfig>()
        val stopped = mutableListOf<String>()

        @Synchronized
        override fun start(config: NetworkConfig) {
            val url = config.peers.single()
            if (url in failing) throw IOException("refused")
            started += config
            instances[config.instanceName] = url
        }

        @Synchronized
        override fun stop(instanceName: String) {
            stopped += instanceName
            instances.remove(instanceName)
        }

        @Synchronized
        override fun info(instanceName: String): NetworkInfo? {
            val url = instances[instanceName] ?: return null
            val poll = polls.merge(instanceName, 1, Int::plus)!! - 1
            val latency = readings[url].orEmpty().getOrNull(poll)
            return NetworkInfo(running = true, errorMessage = null, peers = emptyList(), connectedNodes = if (latency != null) 1 else 0, nodeLatencyMs = latency)
        }
    }

    @Test
    fun theLowerOfTwoReadingsIsKept() = runBlocking {
        val runtime = FakeRuntime(mapOf("tcp://a:1" to listOf(null, 80.0, 40.0)))
        val latency = NodeProbe(runtime, timeoutMillis = 500, pollMillis = 10).measure("tcp://a:1")
        assertEquals(40.0, latency!!, 0.001)
        assertEquals(runtime.started.single().instanceName, runtime.stopped.single())
        assertTrue(runtime.started.single().hideAddress)
    }

    @Test
    fun aSilentNodeHasNoLatency() = runBlocking {
        val runtime = FakeRuntime(emptyMap(), failing = setOf("tcp://down:1"))
        val probe = NodeProbe(runtime, timeoutMillis = 100, pollMillis = 10)
        assertNull(probe.measure("tcp://quiet:1"))
        assertNull(probe.measure("tcp://down:1"))
    }

    @Test
    fun everyNodeIsMeasured() = runBlocking {
        val runtime = FakeRuntime(mapOf("tcp://a:1" to listOf(20.0, 30.0), "tcp://b:1" to listOf(60.0)))
        val result = NodeProbe(runtime, timeoutMillis = 100, pollMillis = 10)
            .measureAll(listOf(PublicNode("a", "tcp://a:1", "A"), PublicNode("b", "tcp://b:1", "B"), PublicNode("c", "tcp://c:1", "C")))
        assertEquals(20.0, result["a"]!!, 0.001)
        assertEquals(60.0, result["b"]!!, 0.001)
        assertTrue(result.containsKey("c") && result["c"] == null)
    }
}
