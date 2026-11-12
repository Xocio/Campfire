package top.mccampfire.app.game

import android.content.Context
import android.net.Uri
import androidx.compose.runtime.Immutable
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/** A game folder the user granted: the picked tree and the launcher it belongs to. */
@Immutable
@Serializable
data class GameFolderRef(val tree: String, val launcher: String)

/** An installed game version inside a granted folder. */
data class GameVersion(
    val folder: GameFolderRef,
    val name: String,
    /** The Minecraft version, such as "1.20.1", or null when unknown. */
    val gameVersion: String?,
    /** "vanilla", "fabric", "quilt", "forge" or "neoforge". */
    val loader: String,
    /** The folder holding mods, saves, config and logs of this version. */
    val gameDir: SafFile,
    /** Last time the game of this version wrote its log. */
    val lastPlayed: Long,
) {
    /** Key saved in the settings. */
    val key: String get() = folder.tree + "\n" + name
}

/** Finds game folders and versions inside folders granted through the system file picker. */
object GameFolders {
    private val Parser = Json { ignoreUnknownKeys = true }
    private val Preferred = listOf(".minecraft", "android_data", "files", "FCL", "games", "Amethyst", "PojavLauncher")
    private val Skipped = setOf("data", "cache", "runtimes", "runtime", "lib", "libraries", "assets", "jre", "control", "controlmap")
    private const val MaxVisited = 60
    private const val MaxDepth = 4

    /** Keeps access to [tree] after the app restarts. */
    fun persist(context: Context, tree: Uri) {
        runCatching {
            context.contentResolver.takePersistableUriPermission(
                tree,
                android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION,
            )
        }
    }

    /** Gives up the access to [tree]. */
    fun release(context: Context, tree: Uri) {
        runCatching {
            context.contentResolver.releasePersistableUriPermission(
                tree,
                android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION,
            )
        }
    }

    /** The .minecraft folder inside the granted [tree], or null. */
    fun findMinecraft(context: Context, tree: Uri): SafFile? {
        val root = SafFile.root(context, tree) ?: return null
        val queue = ArrayDeque(listOf(root to 0))
        var visited = 0
        var fallback: SafFile? = null
        while (queue.isNotEmpty() && visited < MaxVisited) {
            val (dir, depth) = queue.removeFirst()
            visited++
            val children = dir.children()
            if (children.any { it.isDirectory && it.name == "versions" }) return dir
            if (dir.name == ".minecraft" && fallback == null) fallback = dir
            if (depth >= MaxDepth) continue
            children.filter { it.isDirectory && it.name !in Skipped }
                .sortedBy { child -> Preferred.indexOf(child.name).let { if (it < 0) Preferred.size else it } }
                .forEach { queue.addLast(it to depth + 1) }
        }
        return fallback
    }

    /** Versions installed in the game folder of [folder], most recently played first. */
    fun versions(context: Context, folder: GameFolderRef): List<GameVersion> {
        val minecraft = findMinecraft(context, Uri.parse(folder.tree)) ?: return emptyList()
        val versionsDir = minecraft.child("versions") ?: return emptyList()
        val sharedLog by lazy { minecraft.find("logs/latest.log")?.lastModified ?: 0L }
        return versionsDir.children().filter { it.isDirectory }.mapNotNull { dir ->
            val children = dir.children()
            val json = children.firstOrNull { it.name == dir.name + ".json" } ?: return@mapNotNull null
            val text = runCatching { json.openInput()?.use { it.readBytes().toString(Charsets.UTF_8) } }.getOrNull() ?: return@mapNotNull null
            val isolated = children.any { it.isDirectory && it.name in IsolationMarkers }
            val gameDir = if (isolated) dir else minecraft
            val played = if (isolated) dir.find("logs/latest.log")?.lastModified ?: dir.lastModified else sharedLog
            GameVersion(folder, dir.name, gameVersion(text, dir.name), loader(text), gameDir, played)
        }.sortedByDescending { it.lastPlayed }
    }

    private val IsolationMarkers = setOf("mods", "saves", "config", "logs", "resourcepacks")

    /** The mod loader named in a version file. */
    internal fun loader(text: String): String = when {
        "net.neoforged" in text -> "neoforge"
        "net.minecraftforge" in text -> "forge"
        "org.quiltmc:quilt-loader" in text -> "quilt"
        "net.fabricmc:fabric-loader" in text -> "fabric"
        else -> "vanilla"
    }

    /** The Minecraft version of a version file, or null. */
    internal fun gameVersion(text: String, name: String): String? {
        val json = runCatching { Parser.parseToJsonElement(text) as? JsonObject }.getOrNull()
        json?.get("inheritsFrom")?.let { (it as? JsonPrimitive)?.content?.takeIf(String::isNotBlank)?.let { v -> return v } }
        (json?.get("patches") as? JsonArray)?.forEach { patch ->
            val obj = patch as? JsonObject ?: return@forEach
            if ((obj["id"] as? JsonPrimitive)?.content == "game") (obj["version"] as? JsonPrimitive)?.content?.let { return it }
        }
        (json?.get("clientVersion") as? JsonPrimitive)?.content?.takeIf(String::isNotBlank)?.let { return it }
        val id = (json?.get("id") as? JsonPrimitive)?.content ?: name
        return VersionPattern.find(id)?.value ?: VersionPattern.find(name)?.value
    }

    private val VersionPattern = Regex("""\b1\.\d{1,2}(?:\.\d{1,2})?\b|\b\d{2}\.\d{1,2}(?:\.\d{1,2})?\b""")
}
