package top.mccampfire.app.feature.room

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalWindowInfo
import androidx.compose.ui.res.stringResource
import com.zeroui.kit.components.ConfirmDialog
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.core.link.RoomCode

/** Offers to join when the app gains focus with an invite code on the clipboard. */
@Composable
fun ClipboardPrompt(onJoined: () -> Unit) {
    val context = LocalContext.current
    val link = (context.applicationContext as CampfireApp).container.link
    val enabled = LocalAppController.current.observe { it.link.detectClipboard }
    val focused = LocalWindowInfo.current.isWindowFocused
    var offered by rememberSaveable { mutableStateOf("") }
    var pending by rememberSaveable { mutableStateOf<String?>(null) }

    LaunchedEffect(focused, enabled) {
        if (!focused || !enabled || link.state.value.isActive) return@LaunchedEffect
        val code = clipboardCode(context)?.code ?: return@LaunchedEffect
        if (code != offered) {
            offered = code
            pending = code
        }
    }

    pending?.let { code ->
        ConfirmDialog(
            title = stringResource(R.string.join_clipboard_title),
            message = stringResource(R.string.join_clipboard_message, code),
            confirmText = stringResource(R.string.join_action),
            onDismiss = { pending = null },
            onConfirm = {
                pending = null
                RoomCode.find(code)?.let { found ->
                    link.join(found)
                    onJoined()
                }
            },
        )
    }
}
