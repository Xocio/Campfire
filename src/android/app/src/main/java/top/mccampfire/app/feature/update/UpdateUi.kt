package top.mccampfire.app.feature.update

import com.zeroui.kit.components.ButtonVariant
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import top.mccampfire.app.R
import top.mccampfire.app.data.AvailableUpdate
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import top.yukonga.miuix.kmp.theme.MiuixTheme
import com.zeroui.kit.components.WindowDialog

@Composable
fun UpdateDialog(
    update: AvailableUpdate?,
    onDismiss: () -> Unit,
    onUpdate: () -> Unit,
) {
    val last = remember { mutableStateOf<AvailableUpdate?>(null) }
    if (update != null && update != last.value) last.value = update
    val data = last.value

    val noNotes = stringResource(R.string.update_no_notes)

    WindowDialog(
        show = update != null,
        onDismissRequest = onDismiss,
        title = stringResource(R.string.update_log_title),
        summary = data?.let { "v${it.versionName}" },
    ) {
        if (data == null) return@WindowDialog
        Column(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(max = 320.dp)
                    .verticalScroll(rememberScrollState())
            ) {
                if (data.releaseNotes.isBlank()) {
                    Text(text = noNotes, color = MiuixTheme.colorScheme.onSurfaceVariantSummary)
                } else {
                    MarkdownText(markdown = data.releaseNotes, modifier = Modifier.fillMaxWidth())
                }
            }
            Spacer(Modifier.height(20.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                TextButton(
                    text = stringResource(R.string.cancel),
                    onClick = onDismiss,
                    modifier = Modifier.weight(1f)
                )
                TextButton(
                    text = stringResource(R.string.update_action),
                    onClick = onUpdate,
                    variant = ButtonVariant.Primary,
                    modifier = Modifier.weight(1f)
                )
            }
        }
    }
}

@Composable
fun UpdateMessageDialog(
    title: String,
    message: String?,
    onDismiss: () -> Unit,
) {
    val last = remember { mutableStateOf("") }
    if (!message.isNullOrEmpty()) last.value = message

    WindowDialog(
        show = message != null,
        onDismissRequest = onDismiss,
        title = title,
        summary = last.value,
    ) {
        TextButton(
            text = stringResource(R.string.ok),
            onClick = onDismiss,
            variant = ButtonVariant.Primary,
            modifier = Modifier.fillMaxWidth()
        )
    }
}

fun openDownload(context: Context, url: String) {
    runCatching {
        context.startActivity(
            Intent(Intent.ACTION_VIEW, Uri.parse(url)).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
        )
    }
}
