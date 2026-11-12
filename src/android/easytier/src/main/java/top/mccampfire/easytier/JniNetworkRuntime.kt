package top.mccampfire.easytier

import com.easytier.jni.EasyTierJNI
import top.mccampfire.core.link.runtime.NetworkConfig
import top.mccampfire.core.link.runtime.NetworkInfo
import top.mccampfire.core.link.runtime.NetworkInfoParser
import top.mccampfire.core.link.runtime.NetworkRuntime
import java.io.IOException

/** [NetworkRuntime] backed by the EasyTier library in this process. */
class JniNetworkRuntime : NetworkRuntime {
    private val lock = Any()
    private val running = linkedSetOf<String>()

    override fun start(config: NetworkConfig) = synchronized(lock) {
        val toml = config.toToml()
        val result = try {
            EasyTierJNI.runNetworkInstance(toml)
        } catch (e: RuntimeException) {
            throw IOException(e.message ?: "The network instance could not start.", e)
        } catch (e: UnsatisfiedLinkError) {
            throw IOException("The network component is missing.", e)
        }
        if (result != 0) throw IOException(EasyTierJNI.getLastError() ?: "The network instance could not start.")
        running += config.instanceName
        Unit
    }

    override fun stop(instanceName: String): Unit = synchronized(lock) {
        if (!running.remove(instanceName)) return
        runCatching { EasyTierJNI.retainNetworkInstance(running.toTypedArray()) }
    }

    override fun info(instanceName: String): NetworkInfo? = synchronized(lock) {
        if (instanceName !in running) return null
        val json = runCatching { EasyTierJNI.collectNetworkInfos() }.getOrNull() ?: return null
        NetworkInfoParser.parse(json, instanceName)
    }

    /** True when the native library loads on this device. */
    fun isAvailable(): Boolean = runCatching { EasyTierJNI.getLastError() }.isSuccess

    /** Raw running information of every instance, for diagnostics. */
    fun rawInfo(): String? = synchronized(lock) { runCatching { EasyTierJNI.collectNetworkInfos() }.getOrNull() }
}
