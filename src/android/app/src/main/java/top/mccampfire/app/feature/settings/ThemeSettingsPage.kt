package top.mccampfire.app.feature.settings

import com.zeroui.kit.navigation.PageTransition
import com.zeroui.kit.theme.CardMaterial
import com.zeroui.kit.theme.DialogPosition
import com.zeroui.kit.theme.GlassMode
import com.zeroui.kit.theme.ListDensity
import com.zeroui.kit.theme.LocalIsDark
import com.zeroui.kit.theme.TopBarStyle
import com.zeroui.kit.motion.LocalReducedMotion
import androidx.compose.animation.AnimatedVisibility
import com.zeroui.kit.components.SwitchPreference
import com.zeroui.kit.theme.PaletteStyle
import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import top.mccampfire.app.R
import top.mccampfire.app.data.AppearanceCode

import com.zeroui.kit.components.HighRefreshRate
import com.zeroui.kit.components.rememberSupportedRefreshRates
import com.zeroui.kit.glass.shaderUsable
import com.zeroui.kit.navigation.PredictiveBackMode
import com.zeroui.kit.theme.MotionMode
import com.zeroui.kit.components.PreferenceIcon
import com.zeroui.kit.components.SettingIconStyle
import com.zeroui.kit.components.SettingIcons
import com.zeroui.kit.glass.isLiquidEnabled
import top.mccampfire.app.feature.home.HomeBackgroundImage
import top.mccampfire.app.feature.home.HomeMiniPreview
import top.mccampfire.app.feature.main.navLayout
import top.mccampfire.app.feature.home.rememberHomeBackground
import top.mccampfire.app.feature.main.MainTab
import com.zeroui.kit.components.Card
import com.zeroui.kit.theme.UiStyle
import com.zeroui.kit.theme.UiStyleSelectable
import com.zeroui.kit.theme.isMaterial
import com.zeroui.kit.components.TabRow
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.DropdownPreference
import top.yukonga.miuix.kmp.theme.MiuixTheme

@Composable
internal fun ThemeSettingsPage(
    pad: PaddingValues,
    onGlassIntensity: () -> Unit,
    onNavTabs: () -> Unit,
    onHomeBackground: () -> Unit,
) {
    val context = LocalContext.current
    val controller = LocalAppController.current
    var showCustomColor by remember { mutableStateOf(false) }
    var showResetConfirm by remember { mutableStateOf(false) }
    var showShare by remember { mutableStateOf(false) }
    var showApply by remember { mutableStateOf(false) }
    var shareCode by remember { mutableStateOf("") }
    val extras = controller.extras

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item {
            Spacer(Modifier.height(32.dp))
            ThemePreview(pages = true)
            Spacer(Modifier.height(54.dp))
        }

        item {
            TabRow(
                tabs = ThemeModeTabLabels.map { stringResource(it) },
                selectedTabIndex = controller.themeMode.ordinal,
                onTabSelected = { controller.setThemeMode(ThemeMode.entries[it]) },
                modifier = Modifier.padding(horizontal = 12.dp),
            )
            Spacer(Modifier.height(6.dp))
        }

        item { SectionTitle(stringResource(R.string.theme_section_color)) }
        item {
            ThemeCard {
                Column {
                    Column {
                        SwitchPreference(
                            title = stringResource(R.string.monet),
                            startAction = { PreferenceIcon(SettingIcons.monet) },
                            summary = stringResource(R.string.monet_sub),
                            checked = controller.monet,
                            onCheckedChange = { controller.setMonet(it) }
                        )
                        AnimatedVisibility(visible = controller.monet) {
                            Column {
                                val key = controller.monetKeyColor
                                val names = MonetKeyColors.map { stringResource(it.labelRes) }
                                val presetIndex = MonetKeyColors.indexOfFirst { it.argb == key }
                                ListRow(
                                    title = stringResource(R.string.monet_key_color),
                                    summary = when {
                                        key == 0 -> stringResource(R.string.monet_key_color_wallpaper)
                                        key == KeyColorFromBackground -> stringResource(R.string.monet_key_color_background)
                                        presetIndex < 0 -> stringResource(R.string.monet_key_color_custom)
                                        else -> names[presetIndex]
                                    },
                                    startAction = { PreferenceIcon(SettingIcons.keyColor) },
                                )
                                KeyColorRow(
                                    presets = MonetKeyColors.map { it.argb }.zip(names),
                                    selected = key,
                                    backgroundSeed = controller.backgroundSeed,
                                    onWallpaper = { controller.setMonetKeyColor(0) },
                                    onBackground = { controller.setMonetKeyColor(KeyColorFromBackground) },
                                    onPreset = { controller.setMonetKeyColor(it) },

                                    onCustom = { showCustomColor = true },
                                )

                                AnimatedVisibility(visible = controller.monetKeyColor != 0) {
                                    DropdownPreference(
                                        title = stringResource(R.string.monet_palette_style),
                                        startAction = { PreferenceIcon(SettingIcons.paletteStyle) },
                                        items = MonetPaletteStyleLabels.map { stringResource(it) },
                                        selectedIndex = controller.monetPaletteStyle.ordinal,
                                        onSelectedIndexChange = {
                                            controller.setMonetPaletteStyle(PaletteStyle.entries[it])
                                        }
                                    )
                                }
                            }
                        }
                    }
                }

                AnimatedVisibility(visible = controller.themeMode != ThemeMode.Light) {
                    SwitchPreference(
                        title = stringResource(R.string.pure_black),
                        startAction = { PreferenceIcon(SettingIcons.pureBlack) },
                        summary = stringResource(R.string.pure_black_sub),
                        checked = controller.pureBlack,
                        onCheckedChange = { controller.setPureBlack(it) }
                    )
                }
            }
        }

        item { SectionTitle(stringResource(R.string.theme_section_interface)) }
        item {
            ThemeCard {
                if (UiStyleSelectable) {
                    DropdownPreference(
                        title = stringResource(R.string.ui_style),
                        startAction = { PreferenceIcon(SettingIcons.uiStyle) },
                        summary = stringResource(R.string.ui_style_sub),
                        items = UiStyle.entries.map { stringResource(it.labelRes) },
                        selectedIndex = controller.uiStyle.ordinal,
                        onSelectedIndexChange = { controller.setUiStyle(UiStyle.entries[it]) }
                    )
                }

                val material = isMaterial()
                if (!material) {
                    DropdownPreference(
                        title = stringResource(R.string.advanced_material),
                        startAction = { PreferenceIcon(SettingIcons.material) },
                        summary = stringResource(R.string.advanced_material_sub),
                        items = GlassMode.entries.map { stringResource(it.labelRes) },
                        selectedIndex = controller.glassMode.ordinal,
                        onSelectedIndexChange = { controller.setGlassMode(GlassMode.entries[it]) }
                    )
                }

                if (!material && isLiquidEnabled()) {
                    ArrowPreference(
                        title = stringResource(R.string.glass_intensity),
                        startAction = { PreferenceIcon(SettingIcons.tune) },
                        summary = stringResource(R.string.glass_intensity_sub),
                        onClick = onGlassIntensity
                    )
                }
                DropdownPreference(
                    title = stringResource(R.string.icon_style),
                    startAction = { PreferenceIcon(SettingIcons.iconStyle) },
                    summary = stringResource(R.string.icon_style_sub),
                    items = SettingIconStyle.entries.map { stringResource(it.labelRes) },
                    selectedIndex = controller.iconStyle.ordinal,
                    onSelectedIndexChange = { controller.setIconStyle(SettingIconStyle.entries[it]) }
                )

                UiScalePreference()
                DropdownPreference(
                    title = stringResource(R.string.list_density),
                    startAction = { PreferenceIcon(SettingIcons.listDensity) },
                    summary = stringResource(R.string.list_density_sub),
                    items = ListDensity.entries.map { stringResource(it.labelRes) },
                    selectedIndex = extras.listDensity.ordinal,
                    onSelectedIndexChange = { controller.setExtras(extras.copy(listDensity = ListDensity.entries[it])) }
                )

                if (!material && shaderUsable()) {
                    SwitchPreference(
                        title = stringResource(R.string.smooth_corners),
                        startAction = { PreferenceIcon(SettingIcons.smoothCorners) },
                        summary = stringResource(R.string.smooth_corners_sub),
                        checked = extras.smoothCorners,
                        onCheckedChange = { controller.setExtras(extras.copy(smoothCorners = it)) }
                    )
                }
                DropdownPreference(
                    title = stringResource(R.string.top_bar_style),
                    startAction = { PreferenceIcon(SettingIcons.topBar) },
                    summary = stringResource(R.string.top_bar_style_sub),
                    items = TopBarStyle.entries.map { stringResource(it.labelRes) },
                    selectedIndex = extras.topBar.ordinal,
                    onSelectedIndexChange = { controller.setExtras(extras.copy(topBar = TopBarStyle.entries[it])) }
                )
                if (!material && shaderUsable()) {
                    SwitchPreference(
                        title = stringResource(R.string.top_bar_blur),
                        startAction = { PreferenceIcon(SettingIcons.material) },
                        summary = stringResource(R.string.top_bar_blur_sub),
                        checked = extras.topBarBlur,
                        onCheckedChange = { controller.setExtras(extras.copy(topBarBlur = it)) }
                    )
                }
                SwitchPreference(
                    title = stringResource(R.string.elastic_scroll),
                    startAction = { PreferenceIcon(SettingIcons.elasticScroll) },
                    summary = stringResource(R.string.elastic_scroll_sub),
                    checked = extras.elasticScroll,
                    onCheckedChange = { controller.setExtras(extras.copy(elasticScroll = it)) }
                )

                if (!material) {
                    DropdownPreference(
                        title = stringResource(R.string.dialog_position),
                        startAction = { PreferenceIcon(SettingIcons.dialogPosition) },
                        summary = stringResource(R.string.dialog_position_sub),
                        items = DialogPosition.entries.map { stringResource(it.labelRes) },
                        selectedIndex = extras.dialogPosition.ordinal,
                        onSelectedIndexChange = {
                            controller.setExtras(extras.copy(dialogPosition = DialogPosition.entries[it]))
                        }
                    )
                }
            }
        }

        item { SectionTitle(stringResource(R.string.theme_section_home)) }
        item {
            ThemeCard {
                ArrowPreference(
                    title = stringResource(R.string.home_bg_title),
                    startAction = { PreferenceIcon(SettingIcons.homeBackground) },
                    summary = stringResource(
                        if (controller.homeBackground.isEmpty()) R.string.home_bg_entry_off
                        else R.string.home_bg_entry_on
                    ),
                    onClick = onHomeBackground
                )
                DropdownPreference(
                    title = stringResource(R.string.home_title),
                    startAction = { PreferenceIcon(SettingIcons.homeTitle) },
                    summary = stringResource(R.string.home_title_sub),
                    items = HomeTitle.entries.map { stringResource(it.labelRes) },
                    selectedIndex = extras.homeTitle.ordinal,
                    onSelectedIndexChange = { controller.setExtras(extras.copy(homeTitle = HomeTitle.entries[it])) }
                )
                ArrowPreference(
                    title = stringResource(R.string.nav_tabs_title),
                    startAction = { PreferenceIcon(SettingIcons.navTabs) },
                    summary = stringResource(R.string.nav_tabs_sub),
                    onClick = onNavTabs
                )

                AnimatedVisibility(visible = isMaterial() || controller.glassMode == GlassMode.Off) {
                    DropdownPreference(
                        title = stringResource(R.string.nav_labels),
                        startAction = { PreferenceIcon(SettingIcons.navLabels) },
                        summary = stringResource(R.string.nav_labels_sub),
                        items = NavLabelMode.entries.map { stringResource(it.labelRes) },
                        selectedIndex = extras.navLabels.ordinal,
                        onSelectedIndexChange = { controller.setExtras(extras.copy(navLabels = NavLabelMode.entries[it])) }
                    )
                }
                DropdownPreference(
                    title = stringResource(R.string.wide_nav),
                    startAction = { PreferenceIcon(SettingIcons.wideNav) },
                    summary = stringResource(R.string.wide_nav_sub),
                    items = WideNav.entries.map { stringResource(it.labelRes) },
                    selectedIndex = extras.wideNav.ordinal,
                    onSelectedIndexChange = { controller.setExtras(extras.copy(wideNav = WideNav.entries[it])) }
                )

            }
        }

        item { SectionTitle(stringResource(R.string.theme_section_motion)) }
        item {
            ThemeCard {
                DropdownPreference(
                    title = stringResource(R.string.reduce_motion),
                    startAction = { PreferenceIcon(SettingIcons.reduceMotion) },
                    summary = stringResource(R.string.reduce_motion_sub),
                    items = MotionMode.entries.map { stringResource(it.labelRes) },
                    selectedIndex = controller.reduceMotionMode.ordinal,
                    onSelectedIndexChange = { controller.setReduceMotionMode(MotionMode.entries[it]) }
                )

                DropdownPreference(
                    title = stringResource(R.string.page_transition),
                    startAction = { PreferenceIcon(SettingIcons.pageTransition) },
                    summary = stringResource(
                        if (LocalReducedMotion.current) R.string.page_transition_reduced else R.string.page_transition_sub
                    ),
                    items = PageTransition.entries.map { stringResource(it.labelRes) },
                    selectedIndex = extras.pageTransition.ordinal,
                    enabled = !LocalReducedMotion.current,
                    onSelectedIndexChange = { controller.setExtras(extras.copy(pageTransition = PageTransition.entries[it])) }
                )
                DropdownPreference(
                    title = stringResource(R.string.predictive_back),
                    startAction = { PreferenceIcon(SettingIcons.predictiveBack) },
                    summary = stringResource(R.string.predictive_back_sub),
                    items = PredictiveBackMode.entries.map { stringResource(it.labelRes) },
                    selectedIndex = controller.predictiveBackMode.ordinal,
                    onSelectedIndexChange = { controller.setPredictiveBackMode(PredictiveBackMode.entries[it]) }
                )
                SwitchPreference(
                    title = stringResource(R.string.haptics),
                    startAction = { PreferenceIcon(SettingIcons.haptics) },
                    summary = stringResource(R.string.haptics_sub),
                    checked = extras.haptics,
                    onCheckedChange = { controller.setExtras(extras.copy(haptics = it)) }
                )

                val rates = rememberSupportedRefreshRates()
                if (rates.isNotEmpty()) {
                    val selected = when (val pref = extras.refreshRate) {
                        HighRefreshRate.SYSTEM -> 0
                        HighRefreshRate.MAX -> 1
                        else -> (rates.indexOf(pref) + 1).takeIf { it > 0 } ?: 1
                    }
                    DropdownPreference(
                        title = stringResource(R.string.refresh_rate),
                        startAction = { PreferenceIcon(SettingIcons.refreshRate) },
                        summary = stringResource(R.string.refresh_rate_sub),
                        items = listOf(stringResource(R.string.refresh_rate_system)) +
                            rates.map { stringResource(R.string.refresh_rate_hz, it) },
                        selectedIndex = selected,
                        onSelectedIndexChange = { i ->
                            val value = when (i) {
                                0 -> HighRefreshRate.SYSTEM
                                1 -> HighRefreshRate.MAX
                                else -> rates[i - 1]
                            }
                            controller.setExtras(extras.copy(refreshRate = value))
                        }
                    )
                }
            }
        }

        item { SectionTitle(stringResource(R.string.theme_section_manage)) }
        item {
            ThemeCard {
                ArrowPreference(
                    title = stringResource(R.string.share_appearance),
                    startAction = { PreferenceIcon(SettingIcons.share) },
                    summary = stringResource(R.string.share_appearance_sub),
                    onClick = {
                        shareCode = AppearanceCode.export(controller.settings)
                        showShare = true
                    }
                )
                ArrowPreference(
                    title = stringResource(R.string.apply_appearance),
                    startAction = { PreferenceIcon(SettingIcons.importData) },
                    summary = stringResource(R.string.apply_appearance_sub),
                    onClick = { showApply = true }
                )
                ArrowPreference(
                    title = stringResource(R.string.reset_appearance),
                    startAction = { PreferenceIcon(SettingIcons.resetAppearance) },
                    summary = stringResource(R.string.reset_appearance_sub),
                    onClick = { showResetConfirm = true }
                )
            }
        }
    }

    ShareAppearanceDialog(show = showShare, code = shareCode, onDismiss = { showShare = false })
    ApplyAppearanceDialog(
        show = showApply,
        onDismiss = { showApply = false },
        onApply = { text -> controller.importAppearance(text).also { ok -> if (ok) showApply = false } },
    )

    ResetAppearanceDialog(
        show = showResetConfirm,
        onDismiss = { showResetConfirm = false },
        onConfirm = {
            showResetConfirm = false
            controller.resetAppearance()
        },
    )

    val key = controller.monetKeyColor
    CustomKeyColorDialog(
        show = showCustomColor,
        initial = if (key != 0 && key != KeyColorFromBackground && MonetKeyColors.none { it.argb == key }) key
        else DefaultCustomKeyColor,
        onDismiss = { showCustomColor = false },
        onConfirm = { argb ->
            showCustomColor = false

            controller.setMonetKeyColor(argb)
        },
    )
}

private val ThemeModeTabLabels = listOf(
    R.string.theme_mode_tab_system,
    R.string.theme_light,
    R.string.theme_dark,
)

private enum class MonetKeyColor(@androidx.annotation.StringRes val labelRes: Int, val argb: Int) {
    Red(R.string.monet_color_red, 0xFFE5484D.toInt()),
    Pink(R.string.monet_color_pink, 0xFFD6409F.toInt()),
    Purple(R.string.monet_color_purple, 0xFF8E4EC6.toInt()),
    DeepPurple(R.string.monet_color_deep_purple, 0xFF6E56CF.toInt()),
    Indigo(R.string.monet_color_indigo, 0xFF3E63DD.toInt()),
    Blue(R.string.monet_color_blue, 0xFF1B6BFF.toInt()),
    Cyan(R.string.monet_color_cyan, 0xFF05A2C2.toInt()),
    Teal(R.string.monet_color_teal, 0xFF12A594.toInt()),
    Green(R.string.monet_color_green, 0xFF30A46C.toInt()),
    Yellow(R.string.monet_color_yellow, 0xFFF5D90A.toInt()),
    Amber(R.string.monet_color_amber, 0xFFFFB224.toInt()),
    Orange(R.string.monet_color_orange, 0xFFF76808.toInt()),
    Brown(R.string.monet_color_brown, 0xFFAD7F58.toInt()),
    BlueGrey(R.string.monet_color_blue_grey, 0xFF687D91.toInt()),
    Sakura(R.string.monet_color_sakura, 0xFFF5A3B7.toInt()),
}

private val MonetKeyColors = MonetKeyColor.entries

private val MonetPaletteStyleLabels = listOf(
    R.string.monet_style_tonal_spot,
    R.string.monet_style_neutral,
    R.string.monet_style_vibrant,
    R.string.monet_style_expressive,
    R.string.monet_style_rainbow,
    R.string.monet_style_fruit_salad,
    R.string.monet_style_monochrome,
    R.string.monet_style_fidelity,
    R.string.monet_style_content,
)

@Composable
private fun ThemeCard(content: @Composable () -> Unit) {
    Card(
        modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
        contentPadding = PaddingValues(0.dp)
    ) {
        content()
    }
}

@Composable
internal fun ThemePreview(pages: Boolean = false) {
    val controller = LocalAppController.current
    val colors = MiuixTheme.colorScheme
    val configuration = LocalConfiguration.current
    val screenRatio = configuration.screenWidthDp.toFloat() / configuration.screenHeightDp.toFloat()

    val tabs = remember {
        controller.settings.navLayout().order
    }
    val tabCount = tabs.size
    val previewPages = remember(tabs, pages) {
        if (!pages) listOf(MainTab.Home)
        else listOf(MainTab.Home, MainTab.Settings)
    }
    val pagerState = rememberPagerState { previewPages.size }
    val activeTab = tabs.indexOf(previewPages[pagerState.currentPage.coerceIn(0, previewPages.lastIndex)])

    val material = isMaterial()
    val floating = !material && controller.glassMode != GlassMode.Off
    val m3 = androidx.compose.material3.MaterialTheme.colorScheme
    val dark = LocalIsDark.current

    val frame = RoundedCornerShape(20.dp)

    val pageColor = if (material) m3.surfaceContainer else colors.surface
    val textColor = if (material) m3.onSurface else colors.onSurface
    val cardColor = if (material) m3.surfaceBright else colors.surfaceContainer
    val selected = colors.primary
    val unselected = textColor.copy(alpha = if (floating) 1f else 0.5f)

    Column(modifier = Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            modifier = Modifier
                .fillMaxWidth(0.4f)
                .aspectRatio(screenRatio)
                .clip(frame)
                .background(pageColor)

                .border(1.dp, colors.dividerLine, frame)
        ) {
            val background = rememberHomeBackground()
            val extras = controller.extras

            val bgCard = if (background != null && extras.bgCards != CardMaterial.Solid) cardColor.copy(alpha = 0.6f) else cardColor
            val bgEverywhere = extras.bgScope == BackgroundScope.All
            HorizontalPager(
                state = pagerState,
                modifier = Modifier.matchParentSize(),
                userScrollEnabled = previewPages.size > 1,
            ) { page ->
                val tab = previewPages[page]

                val withBackground = tab != MainTab.Settings || bgEverywhere
                Box(Modifier.fillMaxSize()) {
                    if (withBackground) {
                        HomeBackgroundImage(
                            bitmap = background,
                            dimPercent = controller.homeBackgroundDim,
                            modifier = Modifier.matchParentSize(),
                            veil = pageColor
                        )
                    }
                    val card = if (withBackground) bgCard else cardColor
                    when (tab) {
                        MainTab.Settings -> SettingsMiniPreview(cardColor = card, textColor = textColor)
                        else -> HomeMiniPreview(cardColor = card, textColor = textColor)
                    }
                }
            }

            if (floating) {
                val capsule = RoundedCornerShape(14.dp)
                Row(
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .padding(bottom = 8.dp)
                        .height(28.dp)
                        .clip(capsule)
                        .background(cardColor.copy(alpha = 0.85f))
                        .border(0.5.dp, textColor.copy(alpha = 0.1f), capsule)
                        .padding(horizontal = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    repeat(tabCount) { PreviewTab(13, if (it == activeTab) selected else unselected) }
                }
            } else {
                Column(
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .fillMaxWidth()
                ) {
                    if (!material) {
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(0.5.dp)
                                .background(textColor.copy(alpha = 0.1f))
                        )
                    }
                    Row(
                        modifier = Modifier
                            .height(36.dp)
                            .fillMaxWidth()
                            .background(pageColor)
                            .padding(top = 2.dp, bottom = 8.dp),
                        horizontalArrangement = Arrangement.SpaceEvenly,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        repeat(tabCount) { PreviewTab(15, if (it == activeTab) selected else unselected) }
                    }
                }
            }
        }
        if (previewPages.size > 1) {
            Spacer(Modifier.height(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                previewPages.indices.forEach { i ->
                    Box(
                        modifier = Modifier
                            .size(6.dp)
                            .clip(androidx.compose.foundation.shape.CircleShape)
                            .background(if (i == pagerState.currentPage) colors.primary else textColor.copy(alpha = 0.2f))
                    )
                }
            }
        }
    }
}

@Composable
private fun PreviewTab(sizeDp: Int, color: androidx.compose.ui.graphics.Color) {
    Box(
        modifier = Modifier
            .size(sizeDp.dp)
            .clip(RoundedCornerShape(3.dp))
            .background(color)
    )
}
