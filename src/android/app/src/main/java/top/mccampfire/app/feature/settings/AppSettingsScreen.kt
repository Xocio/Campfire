package top.mccampfire.app.feature.settings

import android.app.Activity
import android.widget.Toast
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.ManagedDialog
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.derivedStateOf
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
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Fill
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kyant.shapes.Capsule
import com.mikepenz.aboutlibraries.Libs
import com.mikepenz.aboutlibraries.entity.Library
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.ConfirmDialog
import com.zeroui.kit.components.DropdownPreference
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.PreferenceIcon
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.SettingIcons
import com.zeroui.kit.components.SwitchPreference
import com.zeroui.kit.components.Text
import com.zeroui.kit.effect.BgEffectBackground
import com.zeroui.kit.glass.LiquidSlider
import com.zeroui.kit.glass.liquidGlass
import com.zeroui.kit.glass.liquidSource
import com.zeroui.kit.glass.rememberLiquidBackdrop
import top.mccampfire.app.BuildConfig
import top.mccampfire.app.R
import top.mccampfire.app.data.AvailableUpdate
import top.mccampfire.app.data.CrashLogger
import top.mccampfire.app.data.CampfireService
import top.mccampfire.app.ui.CampfireLogo
import top.mccampfire.app.data.LauncherIcon
import top.mccampfire.app.data.UpdateResult
import top.mccampfire.app.feature.update.UpdateDialog
import top.mccampfire.app.feature.update.UpdateMessageDialog
import top.mccampfire.app.feature.update.openDownload
import top.mccampfire.app.feature.support.SupportIcon
import top.mccampfire.easytier.EasyTierVersion
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import top.yukonga.miuix.kmp.icon.MiuixIcons
import top.yukonga.miuix.kmp.icon.extended.Messages
import top.yukonga.miuix.kmp.icon.extended.Search
import com.zeroui.kit.components.IconHue
import com.zeroui.kit.components.SettingIcon
import top.yukonga.miuix.kmp.icon.extended.Sort
import top.yukonga.miuix.kmp.theme.MiuixTheme
import com.zeroui.kit.components.PressFeedback
import kotlin.math.roundToInt

private val GlassIntensityRange = 0.25f..2f

private val RowCardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)
private val NoticeIcon = SettingIcon({ MiuixIcons.Messages }, { MiuixIcons.Messages }, IconHue.Orange)

/** Application settings list. */
@Composable
fun AppSettingsPage(
    pad: PaddingValues,
    onTheme: () -> Unit,
    onSupport: () -> Unit,
    onAbout: () -> Unit,
) {
    val context = LocalContext.current
    val controller = LocalAppController.current
    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item { SectionTitle(stringResource(R.string.appearance)) }
        item {
            ListCard(RowCardModifier) {
                ArrowPreference(
                    title = stringResource(R.string.theme_settings),
                    startAction = { PreferenceIcon(SettingIcons.theme) },
                    summary = stringResource(R.string.theme_settings_sub),
                    onClick = onTheme,
                )
            }
        }

        item { SectionTitle(stringResource(R.string.language)) }
        item {
            ListCard(RowCardModifier) {
                DropdownPreference(
                    title = stringResource(R.string.language),
                    startAction = { PreferenceIcon(SettingIcons.translate) },
                    summary = stringResource(R.string.language_sub),
                    items = listOf(
                        stringResource(R.string.lang_system),
                        stringResource(R.string.lang_zh_hans),
                        stringResource(R.string.lang_zh_hant),
                        stringResource(R.string.lang_yue),
                        stringResource(R.string.lang_en),
                    ),
                    selectedIndex = controller.settings.language.coerceIn(0, 4),
                    onSelectedIndexChange = { index ->
                        if (index != controller.settings.language) {
                            controller.setLanguage(index)
                            (context as? Activity)?.recreate()
                        }
                    },
                )
            }
        }

        item { SectionTitle(stringResource(R.string.log_section)) }
        item {
            ListCard(RowCardModifier) {
                SwitchPreference(
                    title = stringResource(R.string.app_log),
                    startAction = { PreferenceIcon(SettingIcons.appLog) },
                    summary = stringResource(R.string.app_log_sub),
                    checked = controller.settings.crashLog,
                    onCheckedChange = {
                        controller.setCrashLog(it)
                        CrashLogger.setEnabled(it)
                    },
                )
            }
        }

        if (CampfireService.updateChecker != null) item { SectionTitle(stringResource(R.string.update_section)) }
        if (CampfireService.updateChecker != null) item {
            ListCard(RowCardModifier) {
                SwitchPreference(
                    title = stringResource(R.string.auto_check_update),
                    startAction = { PreferenceIcon(SettingIcons.autoUpdate) },
                    summary = stringResource(R.string.auto_check_update_sub),
                    checked = controller.settings.autoCheckUpdate,
                    onCheckedChange = { controller.setAutoCheckUpdate(it) },
                )
                SwitchPreference(
                    title = stringResource(R.string.notices_receive),
                    startAction = { PreferenceIcon(NoticeIcon) },
                    summary = stringResource(R.string.notices_receive_sub),
                    checked = controller.settings.receiveNotices,
                    onCheckedChange = { controller.setReceiveNotices(it) },
                )
            }
        }

        item { SectionTitle(stringResource(R.string.privacy_section)) }
        item {
            var iconVisible by remember { mutableStateOf(LauncherIcon.isVisible(context)) }
            var showHideConfirm by remember { mutableStateOf(false) }
            val telemetry = (context.applicationContext as CampfireApp).container.telemetry
            var showStats by remember { mutableStateOf(false) }
            val resetDone = stringResource(R.string.stats_reset_done)
            ListCard(RowCardModifier) {
                if (telemetry.available) {
                    SwitchPreference(
                        title = stringResource(R.string.stats_title),
                        startAction = { PreferenceIcon(SettingIcons.share) },
                        summary = stringResource(R.string.stats_sub),
                        checked = controller.settings.statsConsent == true,
                        onCheckedChange = telemetry::setConsent,
                    )
                    ArrowPreference(
                        title = stringResource(R.string.stats_preview),
                        startAction = { PreferenceIcon(SettingIcons.listDensity) },
                        summary = stringResource(R.string.stats_preview_sub),
                        onClick = { showStats = true },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.stats_reset),
                        startAction = { PreferenceIcon(SettingIcons.reset) },
                        summary = stringResource(R.string.stats_reset_sub),
                        onClick = {
                            telemetry.resetId()
                            Toast.makeText(context, resetDone, Toast.LENGTH_SHORT).show()
                        },
                    )
                }
                SwitchPreference(
                    title = stringResource(R.string.hide_launcher_icon),
                    startAction = { PreferenceIcon(SettingIcons.hideLauncher) },
                    summary = stringResource(R.string.hide_launcher_icon_sub),
                    checked = !iconVisible,
                    onCheckedChange = { hide ->
                        if (hide) {
                            showHideConfirm = true
                        } else {
                            iconVisible = true
                            LauncherIcon.setVisible(context, true)
                        }
                    },
                )
            }
            if (showHideConfirm) {
                ConfirmDialog(
                    title = stringResource(R.string.hide_launcher_icon),
                    message = stringResource(R.string.hide_launcher_icon_confirm_msg),
                    confirmText = stringResource(R.string.hide_launcher_icon_do),
                    onDismiss = { showHideConfirm = false },
                    onConfirm = {
                        showHideConfirm = false
                        iconVisible = false
                        LauncherIcon.setVisible(context, false)
                    },
                )
            }
            if (showStats) {
                val pending = stringResource(R.string.stats_id_pending)
                ManagedDialog(
                    onDismiss = { showStats = false },
                    title = stringResource(R.string.stats_preview),
                    summary = stringResource(R.string.stats_preview_note),
                ) {
                    Text(
                        text = telemetry.preview(pending),
                        fontSize = 13.sp,
                        color = ZeroTheme.colors.contentMuted,
                        modifier = Modifier.padding(bottom = 16.dp),
                    )
                    TextButton(
                        text = stringResource(R.string.ok),
                        onClick = { close { showStats = false } },
                        variant = ButtonVariant.Primary,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }

        item { SectionTitle(stringResource(R.string.others)) }
        item {
            ListCard(RowCardModifier) {
                ArrowPreference(
                    title = stringResource(R.string.support_title),
                    startAction = { PreferenceIcon(SupportIcon) },
                    summary = stringResource(R.string.support_entry_sub),
                    onClick = onSupport,
                )
                ArrowPreference(
                    title = stringResource(R.string.about),
                    startAction = { PreferenceIcon(SettingIcons.about) },
                    summary = stringResource(R.string.about_sub),
                    onClick = onAbout,
                )
            }
        }
    }
}

/** Liquid Glass strength page with a live preview. */
@Composable
fun GlassIntensityPage(pad: PaddingValues) {
    val controller = LocalAppController.current
    var draft by remember(controller.glassIntensity) { mutableIntStateOf(controller.glassIntensity) }
    val draftPercent by rememberUpdatedState(draft)
    val intensityProvider = remember { { draftPercent / 100f } }
    val intensity = draft / 100f
    val previewBackdrop = rememberLiquidBackdrop()
    val iconTint = MiuixTheme.colorScheme.onSurfaceVariantSummary

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(pad)
            .padding(horizontal = 12.dp),
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(260.dp)
                .clip(RoundedCornerShape(28.dp)),
        ) {
            Image(
                painter = painterResource(R.drawable.glass_preview),
                contentDescription = null,
                modifier = Modifier.matchParentSize().liquidSource(previewBackdrop),
                contentScale = ContentScale.Crop,
            )
            Row(
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .padding(16.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = Modifier
                        .liquidGlass(backdrop = previewBackdrop, shape = { Capsule() }, intensity = intensity)
                        .size(44.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(imageVector = MiuixIcons.Sort, contentDescription = null, tint = Color.White)
                }
                Row(
                    modifier = Modifier
                        .weight(1f)
                        .liquidGlass(backdrop = previewBackdrop, shape = { Capsule() }, intensity = intensity)
                        .height(44.dp)
                        .padding(horizontal = 16.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(
                        imageVector = MiuixIcons.Search,
                        contentDescription = null,
                        tint = Color.White.copy(alpha = 0.85f),
                    )
                    Text(
                        text = stringResource(R.string.glass_preview_search),
                        color = Color.White.copy(alpha = 0.85f),
                    )
                }
            }
        }

        Spacer(Modifier.height(20.dp))

        Card(
            cornerRadius = 26.dp,
            contentPadding = PaddingValues(horizontal = 18.dp, vertical = 14.dp),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                GlassScaleIcon(filled = false, tint = iconTint)
                LiquidSlider(
                    value = intensityProvider,
                    onValueChange = { draft = (it * 100f).roundToInt() },
                    onValueChangeFinished = { controller.setGlassIntensity(draft) },
                    modifier = Modifier.weight(1f),
                    valueRange = GlassIntensityRange,
                    trackHeight = 8.dp,
                    knobWidth = 40.dp,
                    knobHeight = 28.dp,
                    tickCount = 3,
                    intensity = intensity,
                )
                GlassScaleIcon(filled = true, tint = iconTint)
            }
        }

        Spacer(Modifier.height(12.dp))

        Text(
            text = stringResource(R.string.glass_intensity_desc),
            modifier = Modifier.padding(horizontal = 12.dp),
            fontSize = 13.sp,
            color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
        )
    }
}

@Composable
private fun GlassScaleIcon(filled: Boolean, tint: Color) {
    Canvas(modifier = Modifier.size(22.dp)) {
        val stroke = 1.6f.dp.toPx()
        val box = size.minDimension * 0.66f
        val radius = CornerRadius(4f.dp.toPx())
        drawRoundRect(
            color = tint,
            topLeft = Offset(size.width - box, 0f),
            size = Size(box, box),
            cornerRadius = radius,
            style = Stroke(stroke),
        )
        drawRoundRect(
            color = tint,
            topLeft = Offset(0f, size.height - box),
            size = Size(box, box),
            cornerRadius = radius,
            style = if (filled) Fill else Stroke(stroke),
        )
    }
}

/** About page with version, update check and links. */
@Composable
fun AboutPage(
    pad: PaddingValues,
    onLicenses: () -> Unit,
    onContributors: () -> Unit,
) {
    val context = LocalContext.current
    val controller = LocalAppController.current
    val scope = rememberCoroutineScope()
    var checking by remember { mutableStateOf(false) }
    var update by remember { mutableStateOf<AvailableUpdate?>(null) }
    var message by remember { mutableStateOf<String?>(null) }
    val upToDateMessage = stringResource(R.string.update_up_to_date)
    val failedMessage = stringResource(R.string.update_check_failed)

    val listState = rememberLazyListState()
    val scrollProgress by remember {
        derivedStateOf {
            if (listState.firstVisibleItemIndex > 0) {
                1f
            } else {
                val header = listState.layoutInfo.visibleItemsInfo.firstOrNull()
                if (header != null && header.size > 0) {
                    (listState.firstVisibleItemScrollOffset.toFloat() / header.size).coerceIn(0f, 1f)
                } else {
                    0f
                }
            }
        }
    }

    BgEffectBackground(
        dynamicBackground = true,
        modifier = Modifier.fillMaxSize(),
        isFullSize = true,
        alpha = { 1f - scrollProgress },
        tint = if (controller.monet) MiuixTheme.colorScheme.primary else null,
    ) {
        LazyColumn(
            state = listState,
            modifier = Modifier.fillMaxSize(),
            contentPadding = pad,
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            item {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Spacer(Modifier.height(28.dp))
                    CampfireLogo(
                        shape = RoundedCornerShape(22.dp),
                        description = stringResource(R.string.app_icon),
                        modifier = Modifier.size(88.dp),
                    )
                    Spacer(Modifier.height(14.dp))
                    Text(
                        text = stringResource(R.string.app_name),
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold,
                        color = MiuixTheme.colorScheme.onSurface,
                    )
                    Spacer(Modifier.height(4.dp))
                    Text(
                        text = "v${BuildConfig.VERSION_NAME}",
                        fontSize = 14.sp,
                        color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
                    )
                    Spacer(Modifier.height(28.dp))
                }
            }
            item {
                ListCard(RowCardModifier) {
                    val checker = CampfireService.updateChecker
                    if (checker != null) ArrowPreference(
                        title = stringResource(R.string.check_update),
                        startAction = { PreferenceIcon(SettingIcons.checkUpdate) },
                        summary = if (checking) {
                            stringResource(R.string.update_checking)
                        } else {
                            stringResource(R.string.check_update_sub)
                        },
                        onClick = {
                            if (checking) return@ArrowPreference
                            checking = true
                            scope.launch {
                                val result = checker.check(BuildConfig.VERSION_NAME)
                                checking = false
                                if (result !is UpdateResult.Failed) {
                                    controller.setLastUpdateCheck(System.currentTimeMillis())
                                }
                                when (result) {
                                    is UpdateResult.Available -> update = result.update
                                    UpdateResult.UpToDate -> message = upToDateMessage
                                    UpdateResult.Failed -> message = failedMessage
                                }
                            }
                        },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.github_repo),
                        startAction = { PreferenceIcon(SettingIcons.github) },
                        summary = stringResource(R.string.github_repo_sub),
                        onClick = { openDownload(context, CampfireService.RepositoryUrl) },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.open_source_licenses),
                        startAction = { PreferenceIcon(SettingIcons.licenses) },
                        summary = stringResource(R.string.licenses_sub),
                        onClick = onLicenses,
                    )
                    ArrowPreference(
                        title = stringResource(R.string.contributors_title),
                        startAction = { PreferenceIcon(SettingIcons.contributors) },
                        summary = stringResource(R.string.contributors_sub),
                        onClick = onContributors,
                    )
                }
            }
        }
    }

    UpdateDialog(
        update = update,
        onDismiss = { update = null },
        onUpdate = {
            update?.let { openDownload(context, it.downloadUrl) }
            update = null
        },
    )
    UpdateMessageDialog(
        title = stringResource(R.string.check_update),
        message = message,
        onDismiss = { message = null },
    )
}

private data class Contributor(val name: String, val roleRes: Int, val url: String, val avatarRes: Int? = null)

private val Contributors = listOf(
    Contributor("Xocio", R.string.contributor_role_author, "https://github.com/Xocio", R.drawable.contributor_xocio),
)

/** Contributor list. */
@Composable
fun ContributorsPage(pad: PaddingValues) {
    val context = LocalContext.current
    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item { Spacer(Modifier.height(8.dp)) }
        items(Contributors, key = { it.name }) { contributor ->
            Card(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                contentPadding = PaddingValues(horizontal = 20.dp, vertical = 18.dp),
                pressFeedbackType = PressFeedback.Sink,
                showIndication = true,
                onClick = { openDownload(context, contributor.url) },
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    val avatarModifier = Modifier.size(56.dp).clip(RoundedCornerShape(14.dp))
                    if (contributor.avatarRes != null) {
                        Image(
                            painter = painterResource(contributor.avatarRes),
                            contentDescription = contributor.name,
                            modifier = avatarModifier,
                            contentScale = ContentScale.Crop,
                        )
                    } else {
                        Box(
                            modifier = avatarModifier.background(MiuixTheme.colorScheme.primary.copy(alpha = 0.12f)),
                            contentAlignment = Alignment.Center,
                        ) {
                            Text(
                                text = contributor.name.take(1).uppercase(),
                                fontSize = 22.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = MiuixTheme.colorScheme.primary,
                            )
                        }
                    }
                    Spacer(Modifier.width(16.dp))
                    Column(Modifier.weight(1f)) {
                        Text(
                            text = contributor.name,
                            fontSize = 22.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = MiuixTheme.colorScheme.onSurface,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Spacer(Modifier.height(8.dp))
                        LicenseChip(stringResource(contributor.roleRes))
                    }
                }
            }
        }
    }
}

@Composable
private fun LicenseChip(text: String) {
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(MiuixTheme.colorScheme.primary.copy(alpha = 0.12f))
            .padding(horizontal = 14.dp, vertical = 6.dp),
    ) {
        Text(
            text = text,
            fontSize = 14.sp,
            fontWeight = FontWeight.Medium,
            color = MiuixTheme.colorScheme.primary,
        )
    }
}

/** Open source license list collected at build time. */
@Composable
fun LicensesPage(pad: PaddingValues) {
    val context = LocalContext.current
    val libraries by produceState<List<Library>?>(initialValue = null) {
        value = withContext(Dispatchers.IO) {
            runCatching {
                val json = context.resources.openRawResource(R.raw.aboutlibraries)
                    .bufferedReader().use { it.readText() }
                Libs.Builder().withJson(json).build().libraries.sortedBy { it.name.lowercase() }
            }.getOrDefault(emptyList())
        }
    }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item { SectionTitle(stringResource(R.string.bundled_title)) }
        item {
            LicenseCard(
                name = "EasyTier",
                version = EasyTierVersion,
                author = "EasyTier",
                license = "LGPL-3.0",
                url = "https://github.com/EasyTier/EasyTier",
            )
        }
        item {
            LicenseCard(
                name = stringResource(R.string.notice_campfire_icon),
                version = null,
                author = stringResource(R.string.notice_campfire_icon_owner),
                license = stringResource(R.string.notice_copyright),
                url = null,
            )
        }
        item { SectionTitle(stringResource(R.string.dependencies_title)) }
        val loaded = libraries
        if (loaded == null) {
            item {
                Text(
                    text = stringResource(R.string.loading),
                    modifier = Modifier.padding(16.dp),
                    fontSize = 14.sp,
                    color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
                )
            }
        } else {
            items(loaded, key = { it.uniqueId }) { library -> LicenseCard(library) }
        }
    }
}

@Composable
private fun LicenseCard(library: Library) {
    LicenseCard(
        name = library.name,
        version = library.artifactVersion,
        author = library.developers.mapNotNull { it.name }.joinToString().ifEmpty { library.organization?.name.orEmpty() },
        license = library.licenses.firstOrNull()?.name,
        url = library.website ?: library.scm?.url ?: library.licenses.firstOrNull()?.url,
    )
}

/** One project or asset with its owner and license. */
@Composable
private fun LicenseCard(name: String, version: String?, author: String, license: String?, url: String?) {
    val context = LocalContext.current
    Card(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 18.dp),
        pressFeedbackType = PressFeedback.Sink,
        showIndication = url != null,
        onClick = url?.let { target -> { openDownload(context, target) } },
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = name,
                modifier = Modifier.weight(1f),
                fontSize = 22.sp,
                fontWeight = FontWeight.SemiBold,
                color = MiuixTheme.colorScheme.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            version?.let { value ->
                Spacer(Modifier.width(10.dp))
                Text(
                    text = value,
                    fontSize = 15.sp,
                    color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
                )
            }
        }
        if (author.isNotEmpty()) {
            Spacer(Modifier.height(8.dp))
            Text(text = author, fontSize = 15.sp, color = MiuixTheme.colorScheme.onSurfaceVariantSummary)
        }
        if (license != null) {
            Spacer(Modifier.height(14.dp))
            LicenseChip(license)
        }
    }
}
