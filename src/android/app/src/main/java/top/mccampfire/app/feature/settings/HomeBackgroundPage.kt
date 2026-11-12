package top.mccampfire.app.feature.settings

import com.zeroui.kit.theme.CardMaterial
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import top.mccampfire.app.R
import top.mccampfire.app.data.HomeBackground
import com.zeroui.kit.theme.isMaterial
import com.zeroui.kit.glass.isLiquidGlassSupported
import com.zeroui.kit.components.Badge
import com.zeroui.kit.components.PreferenceIcon
import com.zeroui.kit.components.SettingIcons
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.HorizontalDivider
import com.zeroui.kit.components.DropdownPreference
import com.zeroui.kit.components.ConfirmDialog
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.Text
import top.yukonga.miuix.kmp.icon.MiuixIcons
import top.yukonga.miuix.kmp.icon.extended.Ok
import top.yukonga.miuix.kmp.theme.MiuixTheme
import java.io.File
import kotlin.math.abs

private val DimLevels = listOf(0, 20, 40, 60)
private val DimLabels = listOf(
    R.string.home_bg_dim_none,
    R.string.home_bg_dim_light,
    R.string.home_bg_dim_medium,
    R.string.home_bg_dim_heavy,
)

private val BlurLabels = listOf(
    R.string.home_bg_dim_none,
    R.string.home_bg_blur_light,
    R.string.home_bg_blur_medium,
    R.string.home_bg_blur_strong,
)

private const val HistoryColumns = 3

@Composable
internal fun HomeBackgroundPage(pad: PaddingValues, toast: (String) -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val controller = LocalAppController.current

    val latest by rememberUpdatedState(controller)
    val current = controller.homeBackground

    var importing by remember { mutableStateOf(false) }

    var historyVersion by remember { mutableIntStateOf(0) }
    val history by produceState(emptyList<File>(), historyVersion) {
        value = withContext(Dispatchers.IO) { HomeBackground.history(context) }
    }
    var pendingDelete by remember { mutableStateOf<File?>(null) }

    val importFailed = stringResource(R.string.home_bg_import_failed)
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri == null) return@rememberLauncherForActivityResult
        importing = true
        scope.launch {
            val name = withContext(Dispatchers.IO) {
                HomeBackground.import(context, uri, keep = latest.homeBackground)
            }
            importing = false
            if (name == null) toast(importFailed)
            else {
                latest.setHomeBackground(name)
                historyVersion++
            }
        }
    }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item {
            Spacer(Modifier.height(32.dp))
            ThemePreview()
            Spacer(Modifier.height(32.dp))
        }

        item {
            Card(
                modifier = Modifier.padding(horizontal = 12.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                ArrowPreference(
                    title = stringResource(R.string.home_bg_pick),
                    startAction = { PreferenceIcon(SettingIcons.homeBackground) },
                    summary = stringResource(if (importing) R.string.home_bg_importing else R.string.home_bg_pick_sub),
                    onClick = {
                        if (!importing) {
                            picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
                        }
                    }
                )
                AnimatedVisibility(visible = current.isNotEmpty()) {
                    Column {
                        val dimIndex = DimLevels.indices.minBy { abs(DimLevels[it] - controller.homeBackgroundDim) }
                        DropdownPreference(
                            title = stringResource(R.string.home_bg_dim),
                            startAction = { PreferenceIcon(SettingIcons.tune) },
                            summary = stringResource(R.string.home_bg_dim_sub),
                            items = DimLabels.map { stringResource(it) },
                            selectedIndex = dimIndex,
                            onSelectedIndexChange = { controller.setHomeBackgroundDim(DimLevels[it]) }
                        )
                        val extras = controller.extras
                        DropdownPreference(
                            title = stringResource(R.string.home_bg_blur),
                            startAction = { PreferenceIcon(SettingIcons.material) },
                            summary = stringResource(R.string.home_bg_blur_sub),
                            items = BlurLabels.map { stringResource(it) },
                            selectedIndex = extras.bgBlur,
                            onSelectedIndexChange = { controller.setExtras(extras.copy(bgBlur = it)) }
                        )

                        val materials = CardMaterial.entries.filter {
                            it != CardMaterial.Liquid || (isLiquidGlassSupported() && !isMaterial())
                        }
                        DropdownPreference(
                            title = stringResource(R.string.home_bg_cards),
                            startAction = { PreferenceIcon(SettingIcons.layers) },
                            summary = stringResource(
                                if (isMaterial()) R.string.home_bg_cards_sub_material else R.string.home_bg_cards_sub
                            ),
                            items = materials.map { stringResource(it.labelRes) },
                            selectedIndex = materials.indexOf(extras.bgCards).coerceAtLeast(0),
                            onSelectedIndexChange = { controller.setExtras(extras.copy(bgCards = materials[it])) }
                        )
                        DropdownPreference(
                            title = stringResource(R.string.home_bg_scope),
                            startAction = { PreferenceIcon(SettingIcons.apps) },
                            summary = stringResource(R.string.home_bg_scope_sub),
                            items = BackgroundScope.entries.map { stringResource(it.labelRes) },
                            selectedIndex = extras.bgScope.ordinal,
                            onSelectedIndexChange = { controller.setExtras(extras.copy(bgScope = BackgroundScope.entries[it])) }
                        )
                        ArrowPreference(
                            title = stringResource(R.string.home_bg_remove),
                            startAction = { PreferenceIcon(SettingIcons.reset) },
                            summary = stringResource(R.string.home_bg_remove_sub),
                            onClick = { controller.setHomeBackground("") }
                        )
                    }
                }
            }
        }

        item {
            HistoryCard(
                history = history,
                current = current,
                onApply = { file ->
                    if (file.name != current) {
                        controller.setHomeBackground(file.name)
                        scope.launch {
                            withContext(Dispatchers.IO) { HomeBackground.touch(context, file.name) }
                            historyVersion++
                        }
                    }
                },
                onDelete = { pendingDelete = it }
            )
        }
    }

    pendingDelete?.let { file ->
        val inUse = file.name == current
        ConfirmDialog(
            title = stringResource(R.string.home_bg_delete_title),
            message = stringResource(if (inUse) R.string.home_bg_delete_current_msg else R.string.home_bg_delete_msg),
            confirmText = stringResource(R.string.home_bg_delete_do),
            onDismiss = { pendingDelete = null },
            onConfirm = {
                pendingDelete = null
                if (inUse) controller.setHomeBackground("")
                scope.launch {
                    withContext(Dispatchers.IO) { HomeBackground.delete(context, file.name) }
                    historyVersion++
                }
            }
        )
    }
}

@Composable
private fun HistoryCard(
    history: List<File>,
    current: String,
    onApply: (File) -> Unit,
    onDelete: (File) -> Unit,
) {
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp)
            .padding(top = 12.dp),
        contentPadding = PaddingValues(0.dp)
    ) {
        Column(Modifier.padding(start = 18.dp, end = 18.dp, top = 16.dp, bottom = 18.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(R.string.home_bg_history),
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    color = MiuixTheme.colorScheme.onSurface,
                )
                if (history.isNotEmpty()) {
                    Spacer(Modifier.size(8.dp))
                    Badge(text = "${history.size}/${HomeBackground.MAX_HISTORY}", accent = false)
                }
            }
            Spacer(Modifier.height(3.dp))
            Text(
                text = stringResource(R.string.home_bg_history_sub),
                fontSize = 13.sp,
                color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
            )

            Spacer(Modifier.height(14.dp))
            HorizontalDivider(thickness = 0.5.dp, color = MiuixTheme.colorScheme.dividerLine)
            Spacer(Modifier.height(14.dp))

            if (history.isEmpty()) {
                Text(
                    text = stringResource(R.string.home_bg_history_empty),
                    fontSize = 13.sp,
                    color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
                )
            } else {
                val configuration = LocalConfiguration.current
                val ratio = minOf(configuration.screenWidthDp, configuration.screenHeightDp).toFloat() /
                    maxOf(configuration.screenWidthDp, configuration.screenHeightDp).toFloat()
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    history.chunked(HistoryColumns).forEach { row ->
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            row.forEach { file ->
                                HistoryThumb(
                                    file = file,
                                    selected = file.name == current,
                                    ratio = ratio,
                                    onClick = { onApply(file) },
                                    onLongClick = { onDelete(file) },
                                    modifier = Modifier.weight(1f)
                                )
                            }

                            repeat(HistoryColumns - row.size) { Spacer(Modifier.weight(1f)) }
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun HistoryThumb(
    file: File,
    selected: Boolean,
    ratio: Float,
    onClick: () -> Unit,
    onLongClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val primary = MiuixTheme.colorScheme.primary
    val shape = RoundedCornerShape(12.dp)
    val thumb by produceState<ImageBitmap?>(null, file.path) {
        value = withContext(Dispatchers.IO) { HomeBackground.decode(file, 360)?.asImageBitmap() }
    }
    Box(
        modifier = modifier
            .aspectRatio(ratio)
            .clip(shape)
            .background(primary.copy(alpha = 0.08f))
            .then(if (selected) Modifier.border(2.dp, primary, shape) else Modifier)
            .combinedClickable(onClick = onClick, onLongClick = onLongClick)
    ) {
        thumb?.let {
            Image(
                bitmap = it,
                contentDescription = null,
                modifier = Modifier.fillMaxSize(),
                contentScale = ContentScale.Crop
            )
        }
        if (selected) {
            Box(
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .padding(6.dp)
                    .size(20.dp)
                    .clip(CircleShape)
                    .background(primary),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = MiuixIcons.Ok,
                    contentDescription = stringResource(R.string.home_bg_in_use),
                    tint = Color.White,
                    modifier = Modifier.size(14.dp)
                )
            }
        }
    }
}
