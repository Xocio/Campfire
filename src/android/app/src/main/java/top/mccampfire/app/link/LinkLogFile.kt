package top.mccampfire.app.link

import android.content.Context
import android.util.Log
import top.mccampfire.core.link.LinkLogger
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Multiplayer log written to one file per day, keeping the newest files. */
class LinkLogFile(context: Context) : LinkLogger {
    private val directory = File(context.applicationContext.filesDir, "logs")
    private val lock = Any()

    /** Log files, newest first. */
    fun files(): List<File> = directory.listFiles { file -> file.isFile && file.name.startsWith(Prefix) }
        ?.sortedByDescending { it.name }
        .orEmpty()

    override fun info(message: String) = write("INFO", message)

    override fun warn(message: String) = write("WARN", message)

    override fun error(message: String) = write("ERROR", message)

    private fun write(level: String, message: String) {
        Log.println(if (level == "ERROR") Log.ERROR else if (level == "WARN") Log.WARN else Log.INFO, Tag, message)
        synchronized(lock) {
            runCatching {
                directory.mkdirs()
                val now = Date()
                val file = File(directory, Prefix + SimpleDateFormat("yyyy-MM-dd", Locale.ROOT).format(now) + ".log")
                if (!file.exists()) prune()
                file.appendText("${SimpleDateFormat("HH:mm:ss.SSS", Locale.ROOT).format(now)} $level $message\n")
            }
        }
    }

    private fun prune() {
        files().drop(MaxFiles - 1).forEach { it.delete() }
    }

    private companion object {
        const val Tag = "Campfire"
        const val Prefix = "link-"
        const val MaxFiles = 5
    }
}
