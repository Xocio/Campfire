package top.mccampfire.app.feature.settings

import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import top.mccampfire.app.R
import com.zeroui.kit.components.PreferenceIcon
import com.zeroui.kit.components.SettingIcons
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.Slider
import com.zeroui.kit.components.TextField
import com.zeroui.kit.components.Text
import top.yukonga.miuix.kmp.theme.MiuixTheme
import kotlin.math.roundToInt
import com.zeroui.kit.components.ManagedDialog
import com.zeroui.kit.components.DialogButtons

@Composable
internal fun UiScalePreference() {
    val controller = LocalAppController.current
    val extras = controller.extras
    var draft by remember(extras.uiScale) { mutableFloatStateOf(extras.uiScale.toFloat()) }
    var showInput by remember { mutableStateOf(false) }
    val commit = { percent: Int ->
        val v = percent.coerceIn(UiScaleRange)
        draft = v.toFloat()
        if (v != extras.uiScale) controller.setExtras(extras.copy(uiScale = v))
    }

    ArrowPreference(
        title = stringResource(R.string.ui_scale),
        startAction = { PreferenceIcon(SettingIcons.uiScale) },
        summary = stringResource(R.string.ui_scale_sub),
        endActions = {
            Text(
                text = "${draft.roundToInt()}%",
                fontSize = MiuixTheme.textStyles.body2.fontSize,
                color = MiuixTheme.colorScheme.onSurfaceVariantActions,
            )
        },
        bottomAction = {
            Slider(
                value = draft,
                onValueChange = { draft = it },
                valueRange = UiScaleRange.first.toFloat()..UiScaleRange.last.toFloat(),
                onValueChangeFinished = { commit(draft.roundToInt()) },
                keyPoints = UiScaleKeyPoints.map { it.toFloat() },
                modifier = Modifier.fillMaxWidth(),
            )
        },
        onClick = { showInput = true },
    )

    if (showInput) {
        UiScaleInputDialog(
            initial = extras.uiScale,
            onDismiss = { showInput = false },
            onConfirm = { v ->
                showInput = false
                commit(v)
            },
        )
    }
}

@Composable
private fun UiScaleInputDialog(initial: Int, onDismiss: () -> Unit, onConfirm: (Int) -> Unit) {
    var text by remember { mutableStateOf(initial.toString()) }
    ManagedDialog(onDismiss = onDismiss, title = stringResource(R.string.ui_scale)) {
        TextField(
            value = text,
            onValueChange = { v -> text = v.filter { it.isDigit() }.take(3) },
            label = stringResource(R.string.ui_scale_input_hint, UiScaleRange.first, UiScaleRange.last),
            useLabelAsPlaceholder = true,
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth()
        )
        Spacer(Modifier.height(20.dp))
        DialogButtons(
            confirmText = stringResource(R.string.ok),
            onConfirm = { text.toIntOrNull()?.let { v -> close { onConfirm(v) } } },
            onCancel = { close(onDismiss) },
            confirmEnabled = text.toIntOrNull() != null,
        )
    }
}
