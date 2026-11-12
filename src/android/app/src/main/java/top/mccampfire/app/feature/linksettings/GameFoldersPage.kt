package top.mccampfire.app.feature.linksettings

import com.zeroui.kit.components.ConfirmDialog
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.zeroui.kit.components.AccentBanner
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.LoadingIndicator
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.TextButton
import kotlinx.coroutines.launch
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.app.game.GameFolderRef
import top.mccampfire.app.game.GameVersion
import top.mccampfire.app.game.KnownLauncher

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** Launchers on this device, the folders granted to Campfire and the versions inside them. */
@Composable
fun GameFoldersPage(pad: PaddingValues, toast: (String) -> Unit) {
    val context = LocalContext.current
    val voice = (context.applicationContext as CampfireApp).container.voice
    val folders = LocalAppController.current.observe { it.link.gameFolders }
    val installed = remember { KnownLauncher.All.filter { it.isInstalled(context) } }
    val scope = rememberCoroutineScope()
    var pending by remember { mutableStateOf("other") }
    var versions by remember { mutableStateOf<List<GameVersion>?>(null) }
    val notFound = stringResource(R.string.games_no_minecraft)
    var removing by remember { mutableStateOf<GameFolderRef?>(null) }

    LaunchedEffect(folders) {
        versions = null
        versions = voice.versions()
    }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { tree: Uri? ->
        if (tree != null) scope.launch { if (!voice.addFolder(tree, pending)) toast(notFound) }
    }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item { SectionTitle(stringResource(R.string.games_launchers)) }
        item {
            ListCard(CardModifier) {
                installed.forEach { launcher ->
                    launcher.sharedUri()?.let { shared ->
                        val sharedRef = folders.firstOrNull { it.launcher == launcher.id && it.isShared() }
                        ArrowPreference(
                            title = stringResource(R.string.games_shared, launcher.name),
                            summary = grantSummary(sharedRef, versions),
                            onClick = {
                                pending = launcher.id
                                picker.launch(shared)
                            },
                        )
                    }
                    val own = folders.firstOrNull { it.launcher == launcher.id && !it.isShared() }
                    ArrowPreference(
                        title = if (launcher.sharedPath != null) stringResource(R.string.games_private, launcher.name) else launcher.name,
                        summary = grantSummary(own, versions),
                        onClick = {
                            pending = launcher.id
                            picker.launch(launcher.initialUri())
                        },
                    )
                }
                ArrowPreference(
                    title = stringResource(R.string.games_other),
                    summary = stringResource(R.string.games_other_sub),
                    onClick = {
                        pending = "other"
                        picker.launch(null)
                    },
                )
            }
        }
        item {
            AccentBanner(
                text = (if (installed.isEmpty()) stringResource(R.string.games_none_installed) + "\n\n" else "") + stringResource(R.string.games_hint),
                modifier = CardModifier,
            )
        }
        if (folders.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.games_granted_title)) }
            item {
                ListCard(CardModifier) {
                    folders.forEach { ref ->
                        ArrowPreference(
                            title = KnownLauncher.byId(ref.launcher)?.name ?: stringResource(R.string.games_other_launcher),
                            summary = stringResource(if (ref.isShared()) R.string.games_folder_shared else R.string.games_folder_private),
                            onClick = { removing = ref },
                        )
                    }
                }
            }
            item { SectionTitle(stringResource(R.string.games_versions)) }
            item {
                val list = versions
                when {
                    list == null -> ListCard(CardModifier) {
                        ListRow(title = stringResource(R.string.games_loading), endActions = { LoadingIndicator() })
                    }
                    list.isEmpty() -> AccentBanner(text = stringResource(R.string.games_no_versions), modifier = CardModifier)
                    else -> ListCard(CardModifier) {
                        list.forEach { version -> ListRow(title = version.name, summary = versionSummary(version)) }
                    }
                }
            }
        }
    }

    removing?.let { ref ->
        ConfirmDialog(
            title = stringResource(R.string.games_remove_title),
            message = stringResource(R.string.games_remove_confirm),
            confirmText = stringResource(R.string.games_remove),
            onDismiss = { removing = null },
            onConfirm = {
                removing = null
                voice.removeFolder(ref)
            },
        )
    }
}

/** Whether [this] folder lies in shared storage. */
private fun GameFolderRef.isShared(): Boolean = Uri.parse(tree).authority == KnownLauncher.ExternalStorage

/** Whether a launcher row is granted, and how many versions it holds. */
@Composable
private fun grantSummary(ref: GameFolderRef?, versions: List<GameVersion>?): String {
    if (ref == null) return stringResource(R.string.games_not_granted)
    val count = versions?.count { it.folder.tree == ref.tree } ?: return stringResource(R.string.games_loading)
    return stringResource(R.string.games_granted, count)
}

/** Game version and loader of a version in one line. */
@Composable
fun versionSummary(version: GameVersion): String {
    val loader = when (version.loader) {
        "fabric" -> "Fabric"
        "quilt" -> "Quilt"
        "forge" -> "Forge"
        "neoforge" -> "NeoForge"
        else -> stringResource(R.string.games_vanilla)
    }
    return listOfNotNull(version.gameVersion, loader).joinToString(" · ")
}
