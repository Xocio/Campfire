package top.mccampfire.app.game

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract

/** A Minecraft launcher for Android whose game folder Campfire can open. */
data class KnownLauncher(
    val id: String,
    val name: String,
    val packageName: String,
    /** Authority of the launcher's own file provider, or null when it has none. */
    val authority: String?,
    /** Document opened first in the file picker. */
    val initialDocument: String?,
    /** A game folder in shared storage, relative to its top, or null. */
    val sharedPath: String? = null,
) {
    /** Where the file picker starts for this launcher. */
    fun initialUri(): Uri? = when {
        authority != null && initialDocument != null -> DocumentsContract.buildDocumentUri(authority, initialDocument)
        else -> null
    }

    /** Where the file picker starts for the shared game folder, or null. */
    fun sharedUri(): Uri? = sharedPath?.let { DocumentsContract.buildDocumentUri(ExternalStorage, "primary:$it") }

    fun isInstalled(context: Context): Boolean = runCatching {
        @Suppress("DEPRECATION")
        context.packageManager.getPackageInfo(packageName, 0)
        true
    }.getOrDefault(false)

    companion object {
        /** Authority of the system provider for shared storage. */
        const val ExternalStorage = "com.android.externalstorage.documents"

        /** Launchers with known storage. */
        val All = listOf(
            KnownLauncher(
                id = "fcl",
                name = "Fold Craft Launcher",
                packageName = "com.tungsten.fcl",
                authority = "com.tungsten.fcl.document.provider",
                initialDocument = "com.tungsten.fcl",
                sharedPath = "FCL",
            ),
            KnownLauncher(
                id = "zalith2",
                name = "Zalith Launcher 2",
                packageName = "com.movtery.zalithlauncher.v2",
                authority = "com.movtery.zalithlauncher.v2.document.provider",
                initialDocument = "com.movtery.zalithlauncher.v2",
            ),
            KnownLauncher(
                id = "zalith",
                name = "Zalith Launcher",
                packageName = "com.movtery.zalithlauncher",
                authority = "com.movtery.zalithlauncher.scoped.gamefolder",
                initialDocument = "/storage/emulated/0/Android/data/com.movtery.zalithlauncher/files",
            ),
            KnownLauncher(
                id = "amethyst",
                name = "Amethyst",
                packageName = "org.angelauramc.amethyst",
                authority = "org.angelauramc.amethyst.scoped.gamefolder",
                initialDocument = "/storage/emulated/0/Android/data/org.angelauramc.amethyst/files",
                sharedPath = "games/Amethyst",
            ),
            KnownLauncher(
                id = "mojo",
                name = "MojoLauncher",
                packageName = "git.artdeell.mojo",
                authority = "git.artdeell.mojo.scoped.gamefolder",
                initialDocument = "/storage/emulated/0/Android/data/git.artdeell.mojo/files",
            ),
            KnownLauncher(
                id = "pojav",
                name = "PojavLauncher",
                packageName = "net.kdt.pojavlaunch",
                authority = "net.kdt.pojavlaunch.scoped.gamefolder",
                initialDocument = "/storage/emulated/0/Android/data/net.kdt.pojavlaunch/files",
                sharedPath = "games/PojavLauncher",
            ),
        )

        fun byId(id: String): KnownLauncher? = All.firstOrNull { it.id == id }
    }
}
