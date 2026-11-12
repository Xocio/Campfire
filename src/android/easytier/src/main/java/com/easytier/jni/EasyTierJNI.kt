package com.easytier.jni

/** Native entry points of the EasyTier Android library. */
object EasyTierJNI {
    init {
        System.loadLibrary("easytier_android_jni")
    }

    /** Validates a TOML configuration; returns 0 when it is valid. */
    @JvmStatic
    external fun parseConfig(config: String): Int

    /** Starts an instance from a TOML configuration; returns 0 on success. */
    @JvmStatic
    external fun runNetworkInstance(config: String): Int

    /** Keeps the named instances and stops every other one; null or empty stops all. */
    @JvmStatic
    external fun retainNetworkInstance(instanceNames: Array<String>?): Int

    /** Running information of every instance as JSON. */
    @JvmStatic
    external fun collectNetworkInfos(): String?

    /** The last error of the calling thread, or null. */
    @JvmStatic
    external fun getLastError(): String?
}
