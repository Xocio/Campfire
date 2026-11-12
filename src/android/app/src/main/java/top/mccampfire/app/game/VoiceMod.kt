package top.mccampfire.app.game

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.longOrNull
import top.mccampfire.core.link.room.VoiceModFile
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.security.MessageDigest

/** Why the voice chat mod could not be installed; [key] names the text. */
class VoiceInstallException(val key: Problem, cause: Throwable? = null) : IOException(key.name, cause) {
    enum class Problem { NoVersion, NoLoader, NotFound, NoAccess, Unexpected }
}

/** What an install did. */
enum class VoiceInstall { Present, Enabled, Installed }

/** The voice chat mod Simple Voice Chat: finding it, installing it from Modrinth and reading its server port. */
class VoiceMod(private val cacheDir: File, private val userAgent: String) {
    private val parser = Json { ignoreUnknownKeys = true }

    /** The enabled voice chat jar in the mods folder of [gameDir], or null. */
    fun find(gameDir: SafFile): SafFile? =
        gameDir.child("mods")?.children()?.firstOrNull { isVoiceJar(it.name) && it.name.endsWith(".jar", ignoreCase = true) }

    /**
     * Puts the voice chat mod into the mods folder of [version]: a present jar is kept, a disabled one is enabled,
     * otherwise [preferred] or the newest release for the game version and loader is downloaded.
     */
    fun install(version: GameVersion, preferred: VoiceModFile?): Pair<VoiceInstall, String> {
        val mods = version.gameDir.directory("mods") ?: throw VoiceInstallException(VoiceInstallException.Problem.NoAccess)
        val children = mods.children()
        children.firstOrNull { isVoiceJar(it.name) && it.name.endsWith(".jar", ignoreCase = true) }?.let { return VoiceInstall.Present to it.name }
        children.firstOrNull { isVoiceJar(it.name) && it.name.endsWith(".jar.disabled", ignoreCase = true) }?.let { disabled ->
            val name = disabled.name.removeSuffix(".disabled")
            if (disabled.rename(name)) return VoiceInstall.Enabled to name
        }

        val file = preferred?.let { byHash(it.sha1) } ?: run {
            val game = version.gameVersion ?: throw VoiceInstallException(VoiceInstallException.Problem.NoVersion)
            if (version.loader == "vanilla") throw VoiceInstallException(VoiceInstallException.Problem.NoLoader)
            newest(game, loaderNames(version.loader)) ?: throw VoiceInstallException(VoiceInstallException.Problem.NotFound)
        }
        val bytes = download(file)
        val target = mods.createFile(file.fileName, "application/java-archive") ?: throw VoiceInstallException(VoiceInstallException.Problem.NoAccess)
        val written = runCatching { target.openOutput()?.use { it.write(bytes); true } ?: false }.getOrDefault(false)
        if (!written) {
            target.delete()
            throw VoiceInstallException(VoiceInstallException.Problem.NoAccess)
        }
        return VoiceInstall.Installed to file.fileName
    }

    /** Lets the voice chat run in singleplayer and LAN worlds of the game in [gameDir]. */
    fun enableLocalServer(gameDir: SafFile): Boolean {
        val config = gameDir.find("config/voicechat/voicechat-client.properties") ?: return false
        val text = runCatching { config.openInput()?.use { it.readBytes().toString(Charsets.ISO_8859_1) } }.getOrNull() ?: return false
        if (!LocalServerOff.containsMatchIn(text)) return false
        val fixed = LocalServerOff.replace(text) { it.groupValues[1] + "true" }
        return runCatching { config.openOutput()?.use { it.write(fixed.toByteArray(Charsets.ISO_8859_1)); true } ?: false }.getOrDefault(false)
    }

    /** The UDP port the voice server of the game in [gameDir] last reported in the game log, or null. */
    fun serverPort(gameDir: SafFile): Int? {
        val log = gameDir.find("logs/latest.log") ?: return null
        val text = log.readTail(LogTail) ?: return null
        val port = ServerPortLine.findAll(text).lastOrNull()?.groupValues?.get(1)?.toIntOrNull() ?: return null
        return port.takeIf { it in 1..65535 }
    }

    private fun newest(gameVersion: String, loaders: List<String>): VoiceModFile? {
        val query = "loaders=" + encode(loaders.joinToString(",", "[", "]") { "\"$it\"" }) +
            "&game_versions=" + encode("[\"$gameVersion\"]")
        val versions = get("https://api.modrinth.com/v2/project/$ProjectId/version?$query") as? JsonArray ?: return null
        val list = versions.mapNotNull { it as? JsonObject }
        val chosen = list.firstOrNull { (it["version_type"] as? JsonPrimitive)?.content == "release" } ?: list.firstOrNull() ?: return null
        return primaryFile(chosen)
    }

    private fun byHash(sha1: String): VoiceModFile? = runCatching {
        val version = get("https://api.modrinth.com/v2/version_file/$sha1?algorithm=sha1") as? JsonObject ?: return@runCatching null
        val files = (version["files"] as? JsonArray)?.mapNotNull { it as? JsonObject }.orEmpty()
        files.firstOrNull { ((it["hashes"] as? JsonObject)?.get("sha1") as? JsonPrimitive)?.content == sha1 }?.let(::fileOf)
    }.getOrNull()

    private fun primaryFile(version: JsonObject): VoiceModFile? {
        val files = (version["files"] as? JsonArray)?.mapNotNull { it as? JsonObject }.orEmpty()
        val file = files.firstOrNull { (it["primary"] as? JsonPrimitive)?.booleanOrNull == true } ?: files.firstOrNull() ?: return null
        return fileOf(file)
    }

    private fun fileOf(file: JsonObject): VoiceModFile? {
        val name = (file["filename"] as? JsonPrimitive)?.content ?: return null
        val sha1 = ((file["hashes"] as? JsonObject)?.get("sha1") as? JsonPrimitive)?.content ?: return null
        val url = (file["url"] as? JsonPrimitive)?.content ?: return null
        if (!url.startsWith("https://cdn.modrinth.com/")) return null
        if (name.contains('/') || name.contains('\\') || !name.endsWith(".jar")) return null
        urls[sha1] = url
        return VoiceModFile(name, sha1, (file["size"] as? JsonPrimitive)?.longOrNull ?: 0)
    }

    private val urls = mutableMapOf<String, String>()

    private fun download(file: VoiceModFile): ByteArray {
        val cached = File(cacheDir, "voice/" + file.sha1 + ".jar")
        if (cached.isFile && sha1(cached.readBytes()) == file.sha1) return cached.readBytes()
        val url = urls[file.sha1] ?: throw VoiceInstallException(VoiceInstallException.Problem.NotFound)
        val bytes = try {
            open(url).inputStream.use { it.readBytes() }
        } catch (e: IOException) {
            throw VoiceInstallException(VoiceInstallException.Problem.Unexpected, e)
        }
        if (sha1(bytes) != file.sha1) throw VoiceInstallException(VoiceInstallException.Problem.Unexpected)
        runCatching {
            cached.parentFile?.mkdirs()
            cached.writeBytes(bytes)
        }
        return bytes
    }

    private fun get(url: String) = try {
        val connection = open(url)
        if (connection.responseCode != 200) null else parser.parseToJsonElement(connection.inputStream.use { it.readBytes().toString(Charsets.UTF_8) })
    } catch (e: IOException) {
        throw VoiceInstallException(VoiceInstallException.Problem.Unexpected, e)
    }

    private fun open(url: String): HttpURLConnection = (URL(url).openConnection() as HttpURLConnection).apply {
        connectTimeout = 15_000
        readTimeout = 30_000
        setRequestProperty("User-Agent", userAgent)
    }

    private fun encode(text: String) = URLEncoder.encode(text, "UTF-8")

    private fun sha1(bytes: ByteArray) = MessageDigest.getInstance("SHA-1").digest(bytes).joinToString("") { "%02x".format(it) }

    companion object {
        /** The Modrinth project of the voice chat mod. */
        private const val ProjectId = "9eGKb6K1"
        private const val LogTail = 1024L * 1024
        private val LocalServerOff = Regex("""(?m)^(\s*run_local_server\s*[=:]\s*)false(?=\s*$)""")
        private val ServerPortLine = Regex("""\[voicechat\] (?:Voice chat server started at (?:port )?(?:\S*:)?|Changing voice chat port to )(\d{1,5})\b""")

        /** True for file names of the voice chat mod. */
        fun isVoiceJar(name: String): Boolean = name.contains("voicechat", ignoreCase = true)

        /** Loader names Modrinth uses for [loader]. */
        fun loaderNames(loader: String): List<String> = when (loader) {
            "quilt" -> listOf("quilt", "fabric")
            "forge" -> listOf("forge")
            "neoforge" -> listOf("neoforge")
            else -> listOf("fabric")
        }
    }
}
