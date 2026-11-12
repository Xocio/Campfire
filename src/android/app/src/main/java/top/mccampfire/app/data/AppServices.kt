package top.mccampfire.app.data

import android.content.ComponentName
import android.content.Context
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.os.Build
import java.io.File
import java.io.PrintWriter
import java.io.StringWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Applies the language setting: 0 system, 1 Simplified Chinese, 2 Traditional Chinese, 3 Cantonese, 4 English. */
object LocaleHelper {
    private fun localeFor(language: Int): Locale? = when (language) {
        1 -> Locale.forLanguageTag("zh-Hans-CN")
        2 -> Locale.forLanguageTag("zh-Hant-TW")
        3 -> Locale.forLanguageTag("yue-Hant-HK")
        4 -> Locale.ENGLISH
        else -> null
    }

    /** Wraps [context] with the configured locale. */
    fun wrap(context: Context, language: Int): Context {
        val locale = localeFor(language) ?: return context
        Locale.setDefault(locale)
        val config = Configuration(context.resources.configuration)
        config.setLocale(locale)
        return context.createConfigurationContext(config)
    }
}

/** Visibility of the launcher alias. */
object LauncherIcon {
    private fun component(context: Context) =
        ComponentName(context.packageName, "${context.packageName}.MainActivityLauncher")

    /** True when the launcher icon is shown. */
    fun isVisible(context: Context): Boolean =
        context.packageManager.getComponentEnabledSetting(component(context)) !=
            PackageManager.COMPONENT_ENABLED_STATE_DISABLED

    /** Shows or hides the launcher icon. */
    fun setVisible(context: Context, visible: Boolean) {
        context.packageManager.setComponentEnabledSetting(
            component(context),
            if (visible) {
                PackageManager.COMPONENT_ENABLED_STATE_ENABLED
            } else {
                PackageManager.COMPONENT_ENABLED_STATE_DISABLED
            },
            PackageManager.DONT_KILL_APP,
        )
    }
}

/** Writes uncaught exceptions to the app log directory. */
object CrashLogger {
    private const val Directory = "logs"
    private const val MaxFiles = 5

    @Volatile
    private var appContext: Context? = null

    @Volatile
    private var enabled = true

    /** Installs the uncaught exception handler. */
    fun install(context: Context, enabled: Boolean) {
        appContext = context.applicationContext
        this.enabled = enabled
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            if (this.enabled) runCatching { write(throwable, thread.name) }
            previous?.uncaughtException(thread, throwable)
        }
    }

    /** Turns logging on or off. */
    fun setEnabled(value: Boolean) {
        enabled = value
    }

    private fun logDir(): File? {
        val context = appContext ?: return null
        val dir = context.getExternalFilesDir(Directory) ?: File(context.filesDir, Directory)
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    private fun write(throwable: Throwable, threadName: String) {
        val dir = logDir() ?: return
        pruneOld(dir)
        val time = SimpleDateFormat("yyyy-MM-dd_HH-mm-ss", Locale.US).format(Date())
        val stack = StringWriter()
        PrintWriter(stack).use { throwable.printStackTrace(it) }
        val text = buildString {
            appendLine("Time: $time")
            appendLine("Thread: $threadName")
            appendLine("Device: ${Build.MANUFACTURER} ${Build.MODEL}")
            appendLine("Android: ${Build.VERSION.RELEASE} (SDK ${Build.VERSION.SDK_INT})")
            appendLine("ABI: ${Build.SUPPORTED_ABIS.joinToString()}")
            append(stack.toString())
        }
        runCatching { File(dir, "crash_$time.log").writeText(text) }
    }

    private fun pruneOld(dir: File) {
        val files = dir.listFiles { file -> file.isFile && file.name.startsWith("crash_") } ?: return
        if (files.size < MaxFiles) return
        files.sortedBy { it.lastModified() }.dropLast(MaxFiles - 1).forEach { it.delete() }
    }
}
