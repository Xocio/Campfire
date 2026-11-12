package top.mccampfire.app.feature.room

import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.AccentBanner
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TextField
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.R
import top.mccampfire.core.link.RoomCode

/** Page that reads an invite code and joins its room. */
@Composable
fun JoinPage(pad: PaddingValues, onJoin: (RoomCode) -> Unit) {
    val context = LocalContext.current
    var text by rememberSaveable { mutableStateOf("") }
    var prefilled by rememberSaveable { mutableStateOf(false) }
    val code = remember(text) { RoomCode.find(text) }

    LaunchedEffect(Unit) {
        if (!prefilled) {
            prefilled = true
            clipboardCode(context)?.let { text = it.code }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(pad)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Card(contentPadding = PaddingValues(16.dp)) {
            TextField(
                value = text,
                onValueChange = { if (it.length <= MaxInput) text = it },
                label = stringResource(R.string.join_code_hint),
                singleLine = true,
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Go),
                keyboardActions = KeyboardActions(onGo = { code?.let(onJoin) }),
                modifier = Modifier.fillMaxWidth(),
            )
            AccentBanner(
                text = when {
                    code != null -> stringResource(R.string.join_found, code.code)
                    text.isNotBlank() -> stringResource(R.string.join_invalid)
                    else -> stringResource(R.string.join_note)
                },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 10.dp),
                compact = true,
                danger = code == null && text.isNotBlank(),
            )
        }
        ListCard {
            ArrowPreference(
                title = stringResource(R.string.join_paste_row),
                onClick = { clipboardText(context)?.let { pasted -> text = RoomCode.find(pasted)?.code ?: pasted.take(MaxInput) } },
            )
        }
        TextButton(
            text = stringResource(R.string.join_action),
            onClick = { code?.let(onJoin) },
            enabled = code != null,
            variant = ButtonVariant.Primary,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

private const val MaxInput = 200

/** Text on the clipboard, or null when it cannot be read. */
internal fun clipboardText(context: Context): String? = runCatching {
    val clipboard = context.getSystemService(ClipboardManager::class.java)
    clipboard?.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(context)?.toString()
}.getOrNull()

/** The invite code on the clipboard, or null. */
internal fun clipboardCode(context: Context): RoomCode? = RoomCode.find(clipboardText(context))
