package top.mccampfire.app.feature.settings

import com.zeroui.kit.components.ButtonVariant
import android.content.ClipData
import android.content.ClipboardManager
import android.os.Build
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.colorResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import top.mccampfire.app.R
import top.mccampfire.app.data.AppearanceCode
import com.zeroui.kit.components.TextField
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.WindowDialog
import top.yukonga.miuix.kmp.basic.ColorPalette
import com.zeroui.kit.components.Icon
import top.yukonga.miuix.kmp.icon.MiuixIcons
import top.yukonga.miuix.kmp.icon.extended.Background
import top.yukonga.miuix.kmp.icon.extended.Edit
import top.yukonga.miuix.kmp.icon.extended.Image
import com.zeroui.kit.components.Text
import top.yukonga.miuix.kmp.theme.MiuixTheme

internal const val DefaultCustomKeyColor: Int = 0xFF2196F3.toInt()

@Composable
internal fun CustomKeyColorDialog(
    show: Boolean,
    initial: Int,
    onDismiss: () -> Unit,
    onConfirm: (Int) -> Unit,
) {
    WindowDialog(
        show = show,
        title = stringResource(R.string.monet_custom_color_title),
        onDismissRequest = onDismiss,
    ) {
        var draft by remember(initial) { mutableStateOf(Color(initial)) }
        Column(modifier = Modifier.fillMaxWidth()) {
            ColorPalette(
                color = draft,
                onColorChanged = { draft = it.copy(alpha = 1f) },
                includeGrayColumn = false,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(12.dp))
            Text(
                text = "#%06X".format(draft.toArgb() and 0xFFFFFF),
                fontSize = 14.sp,
                color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
                textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(22.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                TextButton(
                    text = stringResource(R.string.cancel),
                    onClick = onDismiss,
                    modifier = Modifier.weight(1f),
                )
                TextButton(
                    text = stringResource(R.string.ok),
                    onClick = { onConfirm(draft.copy(alpha = 1f).toArgb()) },
                    variant = ButtonVariant.Primary,
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}

@Composable
internal fun KeyColorRow(
    presets: List<Pair<Int, String>>,
    selected: Int,
    backgroundSeed: Int,
    onWallpaper: () -> Unit,
    onBackground: () -> Unit,
    onPreset: (Int) -> Unit,
    onCustom: () -> Unit,
) {
    val colors = MiuixTheme.colorScheme
    val neutral = colors.surfaceContainerHighest
    val presetIndex = presets.indexOfFirst { it.first == selected }
    val fromBackground = selected == KeyColorFromBackground
    val custom = selected != 0 && !fromBackground && presetIndex < 0

    val showBackground = backgroundSeed != 0 || fromBackground
    val offset = if (showBackground) 1 else 0

    val selectedSlot = when {
        selected == 0 -> 0
        fromBackground -> 1
        custom -> presets.size + 1 + offset
        else -> presetIndex + 1 + offset
    }
    val listState = rememberLazyListState(initialFirstVisibleItemIndex = (selectedSlot - 2).coerceAtLeast(0))
    val wallpaperColor = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        colorResource(android.R.color.system_accent1_500)
    } else neutral

    LazyRow(
        state = listState,
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 14.dp),
        contentPadding = PaddingValues(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        item {
            KeySwatch(
                color = wallpaperColor,
                selected = selected == 0,
                label = stringResource(R.string.monet_key_color_wallpaper),
                icon = MiuixIcons.Background,
                onClick = onWallpaper,
            )
        }
        if (showBackground) {
            item {
                KeySwatch(
                    color = if (backgroundSeed != 0) Color(backgroundSeed) else neutral,
                    selected = fromBackground,
                    label = stringResource(R.string.monet_key_color_background),
                    icon = MiuixIcons.Image,
                    onClick = onBackground,
                )
            }
        }
        items(presets.size) { i ->
            val (argb, name) = presets[i]
            KeySwatch(color = Color(argb), selected = i == presetIndex, label = name, onClick = { onPreset(argb) })
        }
        item {
            KeySwatch(
                color = if (custom) Color(selected) else neutral,
                selected = custom,
                label = stringResource(R.string.monet_key_color_custom),
                icon = MiuixIcons.Edit,
                onClick = onCustom,
            )
        }
    }
}

@Composable
private fun KeySwatch(
    color: Color,
    selected: Boolean,
    label: String,
    onClick: () -> Unit,
    icon: ImageVector? = null,
) {
    val ring = MiuixTheme.colorScheme.primary
    Box(
        modifier = Modifier
            .size(40.dp)
            .clip(CircleShape)
            .then(if (selected) Modifier.border(2.dp, ring, CircleShape) else Modifier)
            .clickable(onClickLabel = label, role = Role.RadioButton, onClick = onClick)
            .semantics { contentDescription = label }
            .padding(5.dp),
        contentAlignment = Alignment.Center,
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .clip(CircleShape)
                .background(color),
            contentAlignment = Alignment.Center,
        ) {
            if (icon != null) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = if (color.luminance() > 0.5f) Color.Black.copy(alpha = 0.7f) else Color.White,
                    modifier = Modifier.size(16.dp),
                )
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------------

private val MiniShape = RoundedCornerShape(6.dp)

@Composable
private fun MiniTitle(text: String, textColor: Color) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(44.dp)
            .padding(start = 10.dp, end = 10.dp, top = 20.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text = text, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = textColor)
    }
}

@Composable
private fun MiniBar(color: Color, width: Dp, modifier: Modifier = Modifier, height: Dp = 3.dp) {
    Box(
        modifier = modifier
            .width(width)
            .height(height)
            .clip(RoundedCornerShape(50))
            .background(color)
    )
}

@Composable
private fun MiniSettingRow(textColor: Color, switch: Boolean, on: Boolean = true) {
    val primary = MiuixTheme.colorScheme.primary
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(20.dp)
            .padding(horizontal = 5.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            Modifier
                .size(9.dp)
                .clip(RoundedCornerShape(3.dp))
                .background(textColor.copy(alpha = 0.4f))
        )
        Spacer(Modifier.width(5.dp))
        Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
            MiniBar(textColor.copy(alpha = 0.45f), 26.dp)
            MiniBar(textColor.copy(alpha = 0.18f), 38.dp, height = 2.dp)
        }
        Spacer(Modifier.weight(1f))
        if (switch) {
            Box(
                modifier = Modifier
                    .size(width = 13.dp, height = 8.dp)
                    .clip(RoundedCornerShape(50))
                    .background(if (on) primary else textColor.copy(alpha = 0.15f)),
                contentAlignment = if (on) Alignment.CenterEnd else Alignment.CenterStart
            ) {
                Box(
                    Modifier
                        .padding(horizontal = 1.dp)
                        .size(6.dp)
                        .clip(CircleShape)
                        .background(Color.White)
                )
            }
        } else {
            MiniBar(textColor.copy(alpha = 0.25f), 4.dp, height = 4.dp)
        }
    }
}

@Composable
private fun MiniSettingCard(cardColor: Color, content: @Composable () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(MiniShape)
            .background(cardColor)
            .padding(vertical = 3.dp),
        verticalArrangement = Arrangement.spacedBy(2.dp)
    ) { content() }
}

@Composable
internal fun SettingsMiniPreview(cardColor: Color, textColor: Color, modifier: Modifier = Modifier) {
    Column(modifier = modifier) {
        MiniTitle(stringResource(R.string.tab_settings), textColor)
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 8.dp),
            verticalArrangement = Arrangement.spacedBy(5.dp)
        ) {
            MiniSettingCard(cardColor) {
                MiniSettingRow(textColor, switch = true)
                MiniSettingRow(textColor, switch = true, on = false)
                MiniSettingRow(textColor, switch = false)
            }
            MiniSettingCard(cardColor) {
                MiniSettingRow(textColor, switch = false)
                MiniSettingRow(textColor, switch = true)
            }
            MiniSettingCard(cardColor) {
                MiniSettingRow(textColor, switch = false)
                MiniSettingRow(textColor, switch = false)
            }
        }
    }
}

@Composable
internal fun ResetAppearanceDialog(show: Boolean, onDismiss: () -> Unit, onConfirm: () -> Unit) {
    WindowDialog(
        show = show,
        title = stringResource(R.string.reset_appearance),
        summary = stringResource(R.string.reset_appearance_confirm),
        onDismissRequest = onDismiss,
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            TextButton(
                text = stringResource(R.string.cancel),
                onClick = onDismiss,
                modifier = Modifier.weight(1f),
            )
            TextButton(
                text = stringResource(R.string.reset_appearance_action),
                onClick = onConfirm,
                variant = ButtonVariant.Primary,
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@Composable
internal fun ShareAppearanceDialog(show: Boolean, code: String, onDismiss: () -> Unit) {
    val context = LocalContext.current
    var copied by remember(show) { mutableStateOf(false) }
    WindowDialog(
        show = show,
        title = stringResource(R.string.share_appearance),
        summary = stringResource(R.string.share_appearance_note),
        onDismissRequest = onDismiss,
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            SelectionContainer {
                Text(
                    text = code,
                    fontSize = 14.sp,
                    color = MiuixTheme.colorScheme.onSurface,
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(12.dp))
                        .background(MiuixTheme.colorScheme.onSurface.copy(alpha = 0.06f))
                        .padding(12.dp),
                )
            }
            Spacer(Modifier.height(22.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                TextButton(
                    text = stringResource(R.string.close),
                    onClick = onDismiss,
                    modifier = Modifier.weight(1f),
                )
                TextButton(
                    text = stringResource(if (copied) R.string.share_appearance_copied else R.string.share_appearance_copy),
                    onClick = {
                        context.getSystemService(ClipboardManager::class.java)
                            ?.setPrimaryClip(ClipData.newPlainText("Campfire", code))
                        copied = true
                    },
                    enabled = !copied,
                    variant = ButtonVariant.Primary,
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}

@Composable
internal fun ApplyAppearanceDialog(show: Boolean, onDismiss: () -> Unit, onApply: (String) -> Boolean) {
    val context = LocalContext.current
    var text by remember(show) { mutableStateOf("") }
    var invalid by remember(show) { mutableStateOf(false) }
    LaunchedEffect(show) {
        if (!show) return@LaunchedEffect
        val clip = runCatching {
            context.getSystemService(ClipboardManager::class.java)
                ?.primaryClip?.getItemAt(0)?.coerceToText(context)?.toString()
        }.getOrNull().orEmpty()
        if (AppearanceCode.looksLike(clip)) text = clip.trim()
    }
    WindowDialog(
        show = show,
        title = stringResource(R.string.apply_appearance),
        summary = stringResource(R.string.apply_appearance_note),
        onDismissRequest = onDismiss,
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            TextField(
                value = text,
                onValueChange = { text = it; invalid = false },
                label = stringResource(R.string.apply_appearance_hint),
                useLabelAsPlaceholder = true,
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            if (invalid) {
                Spacer(Modifier.height(8.dp))
                Text(
                    text = stringResource(R.string.apply_appearance_invalid),
                    fontSize = 13.sp,
                    color = MiuixTheme.colorScheme.error,
                )
            }
            Spacer(Modifier.height(22.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                TextButton(
                    text = stringResource(R.string.cancel),
                    onClick = onDismiss,
                    modifier = Modifier.weight(1f),
                )
                TextButton(
                    text = stringResource(R.string.apply_appearance_action),
                    onClick = { if (!onApply(text)) invalid = true },
                    enabled = text.isNotBlank(),
                    variant = ButtonVariant.Primary,
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}
