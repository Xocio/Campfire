package top.mccampfire.app.feature.notice

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.WindowDialog
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.R
import top.mccampfire.app.data.Notice

/** Language code notices are filtered by: "zh" for every Chinese variant, otherwise "en". */
@Composable
fun noticeLanguage(): String {
    val locales = LocalConfiguration.current.locales
    val language = if (locales.isEmpty) "" else locales[0].language
    return if (language == "zh" || language == "yue") "zh" else "en"
}

/** Dialog for the first waiting notice; [onAction] runs the button, [onClose] answers it either way. */
@Composable
fun NoticeDialog(
    notice: Notice?,
    onAction: (Notice) -> Unit,
    onClose: (Notice) -> Unit,
) {
    val last = remember { mutableStateOf<Notice?>(null) }
    if (notice != null) last.value = notice
    val data = last.value

    WindowDialog(
        show = notice != null,
        onDismissRequest = { notice?.let(onClose) },
        title = data?.title,
    ) {
        if (data == null) return@WindowDialog
        Column(modifier = Modifier.fillMaxWidth()) {
            if (data.body.isNotBlank()) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 320.dp)
                        .verticalScroll(rememberScrollState()),
                ) {
                    Text(
                        text = data.body,
                        fontSize = 15.sp,
                        color = data.bodyColor?.let { Color(it) } ?: ZeroTheme.colors.contentMuted,
                    )
                }
                Spacer(Modifier.height(20.dp))
            }
            if (data.hasButton) {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    TextButton(
                        text = stringResource(R.string.close),
                        onClick = { onClose(data) },
                        modifier = Modifier.weight(1f),
                    )
                    TextButton(
                        text = data.buttonText,
                        onClick = {
                            onClose(data)
                            onAction(data)
                        },
                        variant = ButtonVariant.Primary,
                        modifier = Modifier.weight(1f),
                    )
                }
            } else {
                TextButton(
                    text = stringResource(R.string.ok),
                    onClick = { onClose(data) },
                    variant = ButtonVariant.Primary,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
    }
}

/** Notice card on the home page with its button and a close button. */
@Composable
fun NoticeBanner(
    notice: Notice,
    onAction: (Notice) -> Unit,
    onClose: (Notice) -> Unit,
    modifier: Modifier = Modifier,
) {
    val tone = if (notice.isWarning) ZeroTheme.colors.danger else ZeroTheme.colors.accent
    Card(modifier = modifier.fillMaxWidth(), contentPadding = PaddingValues(16.dp)) {
        Text(
            text = notice.title,
            fontSize = 17.sp,
            fontWeight = FontWeight.SemiBold,
            color = notice.titleColor?.let { Color(it) } ?: tone,
        )
        if (notice.body.isNotBlank()) {
            Spacer(Modifier.height(4.dp))
            Text(
                text = notice.body,
                fontSize = 14.sp,
                color = notice.bodyColor?.let { Color(it) } ?: ZeroTheme.colors.contentMuted,
            )
        }
        Spacer(Modifier.height(12.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp, androidx.compose.ui.Alignment.End),
        ) {
            TextButton(
                text = stringResource(R.string.close),
                onClick = { onClose(notice) },
                minWidth = 64.dp,
                minHeight = 34.dp,
                insideMargin = PaddingValues(horizontal = 14.dp, vertical = 7.dp),
            )
            if (notice.hasButton) {
                TextButton(
                    text = notice.buttonText,
                    onClick = { onAction(notice) },
                    variant = ButtonVariant.Primary,
                    minWidth = 64.dp,
                    minHeight = 34.dp,
                    insideMargin = PaddingValues(horizontal = 14.dp, vertical = 7.dp),
                )
            }
        }
    }
}
