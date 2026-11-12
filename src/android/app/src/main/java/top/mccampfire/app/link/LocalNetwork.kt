package top.mccampfire.app.link

import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat

/** The permission that lets Campfire hear LAN worlds announced on this device and its network. */
object LocalNetwork {
    const val Permission = "android.permission.ACCESS_LOCAL_NETWORK"

    /** Android version from which the permission is asked at runtime. */
    private const val RuntimeFromSdk = 37

    /** True when the permission has to be asked for on this device. */
    val isRuntime: Boolean get() = Build.VERSION.SDK_INT >= RuntimeFromSdk

    fun isGranted(context: Context): Boolean =
        !isRuntime || ContextCompat.checkSelfPermission(context, Permission) == PackageManager.PERMISSION_GRANTED
}
