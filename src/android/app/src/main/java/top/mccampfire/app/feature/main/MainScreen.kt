package top.mccampfire.app.feature.main

import android.app.Activity
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.PagerDefaults
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import com.zeroui.kit.components.ConfirmDialog
import com.zeroui.kit.components.DialogButtons
import com.zeroui.kit.components.ManagedDialog
import com.zeroui.kit.components.Icon
import com.zeroui.kit.glass.LiquidBottomTab
import com.zeroui.kit.glass.LiquidBottomTabs
import com.zeroui.kit.glass.LocalLiquidBottomTabActive
import com.zeroui.kit.glass.LocalLiquidIntensity
import com.zeroui.kit.glass.LocalPageBackground
import com.zeroui.kit.glass.frostedGlass
import com.zeroui.kit.glass.isLiquidGlassSupported
import com.zeroui.kit.glass.liquidSource
import com.zeroui.kit.glass.rememberBlurBackdrop
import com.zeroui.kit.glass.rememberLiquidBackdrop
import com.zeroui.kit.glass.rememberSquircleShape
import com.zeroui.kit.motion.LocalReducedMotion
import com.zeroui.kit.theme.GlassMode
import com.zeroui.kit.theme.LocalGlassMode
import com.zeroui.kit.theme.isMaterial
import top.mccampfire.app.BuildConfig
import top.mccampfire.app.R
import top.mccampfire.app.data.AvailableUpdate
import top.mccampfire.app.data.CampfireService
import top.mccampfire.app.data.UpdateResult
import top.mccampfire.app.feature.home.HomeScreen
import top.mccampfire.app.feature.home.rememberPageBackground
import top.mccampfire.app.feature.settings.BackgroundScope
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.LocalAppearanceExtras
import top.mccampfire.app.feature.settings.NavLabelMode
import top.mccampfire.app.feature.settings.WideNav
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.app.feature.linksettings.LinkSettingsScreen
import top.mccampfire.app.feature.lobby.LobbyScreen
import top.mccampfire.app.feature.room.ClipboardPrompt
import top.mccampfire.app.feature.update.UpdateDialog
import top.mccampfire.app.data.Notice
import top.mccampfire.app.feature.notice.NoticeDialog
import top.mccampfire.app.feature.notice.noticeLanguage
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import kotlinx.coroutines.delay
import top.mccampfire.app.feature.update.openDownload
import top.mccampfire.app.navigation.AppRoute
import kotlinx.coroutines.launch
import top.yukonga.miuix.kmp.basic.Badge
import top.yukonga.miuix.kmp.basic.FloatingNavigationBar
import top.yukonga.miuix.kmp.basic.FloatingNavigationBarItem
import top.yukonga.miuix.kmp.basic.FloatingToolbarDefaults
import top.yukonga.miuix.kmp.basic.NavigationBar
import top.yukonga.miuix.kmp.basic.NavigationBarDefaults
import top.yukonga.miuix.kmp.basic.NavigationBarDisplayMode
import top.yukonga.miuix.kmp.basic.NavigationBarItem
import top.yukonga.miuix.kmp.basic.NavigationRail
import top.yukonga.miuix.kmp.basic.NavigationRailItem
import top.yukonga.miuix.kmp.nav.core.LocalNavTransitionScope
import top.yukonga.miuix.kmp.nav.transition.NavTransitionScope
import com.zeroui.kit.glass.blurSource
import top.yukonga.miuix.kmp.theme.MiuixTheme
import top.yukonga.miuix.kmp.utils.PagerNavigationSpringSpec
import top.yukonga.miuix.kmp.utils.springAnimateToPage
import androidx.compose.material3.Badge as MaterialBadge
import androidx.compose.material3.BadgedBox as MaterialBadgedBox
import androidx.compose.material3.Icon as MaterialIcon
import androidx.compose.material3.MaterialTheme as Material
import androidx.compose.material3.NavigationBar as MaterialNavigationBar
import androidx.compose.material3.NavigationBarItem as MaterialNavigationBarItem
import androidx.compose.material3.NavigationRail as MaterialNavigationRail
import androidx.compose.material3.NavigationRailItem as MaterialNavigationRailItem
import androidx.compose.material3.Text as MaterialText

private val NavBottomPadding = 96.dp
private val UpdateDot: @Composable () -> Unit = { Badge() }
private val DockedNavBottomPadding = NavigationBarDefaults.ItemHeight
private val MaterialNavBottomPadding = 80.dp
private val RailMinWidth = 600.dp
private val NavGlassWidth = 200.dp
private val NavGlassHeight = 56.dp
private val LiquidNavBottomGap = 28.dp
private const val AutoCheckIntervalMs = 6 * 60 * 60 * 1000L
private const val NoticeIntervalMs = 6 * 60 * 60 * 1000L
private const val NavRetreatSpan = 0.6f

/** Navigation bar placement, used to pick the retreat direction. */
private enum class NavEdge { Bottom, Start }

/**
 * Moves a navigation bar off its edge and fades it as this page gets covered.
 *
 * @param extraDistance Gap between the bar and the screen edge, added to the travel distance.
 */
private fun Modifier.retreatWhenCovered(
    transition: NavTransitionScope,
    edge: NavEdge,
    reduced: Boolean,
    extraDistance: Dp = 0.dp,
): Modifier = graphicsLayer {
    val covered = (transition.relativeDepth / NavRetreatSpan).coerceIn(0f, 1f)
    val eased = covered * covered * (3f - 2f * covered)
    alpha = 1f - eased
    if (!reduced) {
        when (edge) {
            NavEdge.Bottom -> translationY = eased * (size.height + extraDistance.toPx())
            NavEdge.Start -> {
                val direction = if (transition.layoutDirection == LayoutDirection.Rtl) 1f else -1f
                translationX = direction * eased * size.width
            }
        }
    }
}

/**
 * Main frame: tab pager with a docked bar, floating bar, Liquid Glass bar or side rail.
 *
 * @param isActive True while this page is the top of the navigation stack.
 */
@Composable
fun MainScreen(
    isActive: Boolean,
    onNavigate: (AppRoute) -> Unit,
) {
    val context = LocalContext.current
    val controller = LocalAppController.current
    val visibleTabs = controller.observe { it.navLayout().order }
    val pagerState = rememberPagerState(
        initialPage = visibleTabs.indexOf(MainTab.Home).coerceAtLeast(0),
        pageCount = { visibleTabs.size },
    )
    val pagerScope = rememberCoroutineScope()
    val tab = visibleTabs.getOrElse(pagerState.targetPage) { MainTab.Home }

    var activeTab by rememberSaveable { mutableStateOf(MainTab.Home) }
    val visibleTabsState = rememberUpdatedState(visibleTabs)
    LaunchedEffect(pagerState) {
        snapshotFlow { pagerState.settledPage }.collect { page ->
            visibleTabsState.value.getOrNull(page)?.let { activeTab = it }
        }
    }
    LaunchedEffect(visibleTabs) {
        val index = visibleTabs.indexOf(activeTab).let { if (it >= 0) it else 0 }
        if (index != pagerState.currentPage) pagerState.scrollToPage(index)
    }
    var neighborsReady by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        withFrameNanos { }
        neighborsReady = true
    }
    val goToTab: (Int) -> Unit = { index ->
        if (index != pagerState.targetPage) pagerScope.launch { pagerState.springAnimateToPage(index) }
    }

    var showExit by remember { mutableStateOf(false) }
    var availableUpdate by remember { mutableStateOf<AvailableUpdate?>(null) }
    var showUpdateDialog by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        val link = controller.settings.link
        if (link.usePublicNodes && link.autoPickNodes) {
            (context.applicationContext as top.mccampfire.app.CampfireApp).container.link.nodeTester.refreshIfStale()
        }
    }
    val noticeCenter = (context.applicationContext as top.mccampfire.app.CampfireApp).container.notices
    val language by rememberUpdatedState(noticeLanguage())
    LaunchedEffect(noticeCenter) {
        while (true) {
            noticeCenter.refresh(BuildConfig.VERSION_NAME, language)
            delay(NoticeIntervalMs)
        }
    }
    val noticeDialogs by noticeCenter.dialogs.collectAsStateWithLifecycle()
    val openNotice: (Notice) -> Unit = { notice ->
        val page = notice.buttonPage
        val tabFor: (MainTab) -> Unit = { target ->
            visibleTabs.indexOf(target).takeIf { it >= 0 }?.let(goToTab)
        }
        when {
            page == "home" -> tabFor(MainTab.Home)
            page == "lobby" -> tabFor(MainTab.Lobby)
            page == "linksettings" -> tabFor(MainTab.Settings)
            page == "settings" -> onNavigate(AppRoute.AppSettings)
            page == "support" -> onNavigate(AppRoute.Support)
            page == "about" -> onNavigate(AppRoute.About)
            page == "licenses" -> onNavigate(AppRoute.Licenses)
            notice.buttonUrl != null -> openDownload(context, notice.buttonUrl)
        }
    }

    LaunchedEffect(Unit) {
        val settings = controller.settings
        val checker = CampfireService.updateChecker ?: return@LaunchedEffect
        if (!settings.autoCheckUpdate) return@LaunchedEffect
        val now = System.currentTimeMillis()
        if (now - settings.lastUpdateCheck < AutoCheckIntervalMs) return@LaunchedEffect
        when (val result = checker.check(BuildConfig.VERSION_NAME)) {
            is UpdateResult.Available -> {
                availableUpdate = result.update
                showUpdateDialog = true
                controller.setLastUpdateCheck(now)
            }
            UpdateResult.UpToDate -> {
                availableUpdate = null
                controller.setLastUpdateCheck(now)
            }
            UpdateResult.Failed -> Unit
        }
    }

    val telemetry = (context.applicationContext as top.mccampfire.app.CampfireApp).container.telemetry
    var askStats by remember { mutableStateOf(false) }
    val statsScope = rememberCoroutineScope()
    LaunchedEffect(Unit) {
        telemetry.countLaunch()
        if (telemetry.available && controller.settings.statsConsent == null) askStats = true
        telemetry.sendIfDue()
    }

    val glassMode = LocalGlassMode.current
    val liquid = glassMode == GlassMode.Liquid && isLiquidGlassSupported()
    val docked = glassMode == GlassMode.Off
    val backdrop = if (liquid) null else rememberBlurBackdrop()
    val liquidBackdrop = rememberLiquidBackdrop()
    val intensity = LocalLiquidIntensity.current
    val selectedTabProvider = remember(pagerState) { { pagerState.targetPage } }
    val navShape = rememberSquircleShape(FloatingToolbarDefaults.CornerRadius)

    BackHandler(enabled = isActive) {
        if (tab != MainTab.Home) goToTab(visibleTabs.indexOf(MainTab.Home)) else showExit = true
    }

    val material = isMaterial()
    val background = if (material) Material.colorScheme.surfaceContainer else MiuixTheme.colorScheme.background

    val tabs: @Composable (bottomPadding: Dp) -> Unit = { padding ->
        HorizontalPager(
            state = pagerState,
            modifier = Modifier.fillMaxSize(),
            beyondViewportPageCount = if (neighborsReady) 1 else 0,
            flingBehavior = PagerDefaults.flingBehavior(
                state = pagerState,
                snapAnimationSpec = PagerNavigationSpringSpec,
            ),
            key = { visibleTabs.getOrNull(it)?.name ?: "page-$it" },
        ) { index ->
            when (visibleTabs.getOrNull(index)) {
                MainTab.Home -> HomeScreen(
                    bottomPadding = padding,
                    hasUpdate = availableUpdate != null,
                    onUpdateClick = { if (availableUpdate != null) showUpdateDialog = true },
                    onNavigate = onNavigate,
                    onNoticeAction = openNotice,
                )
                MainTab.Lobby -> LobbyScreen(bottomPadding = padding, onNavigate = onNavigate)
                MainTab.Settings -> LinkSettingsScreen(bottomPadding = padding, onNavigate = onNavigate)
                null -> Unit
            }
        }
    }

    val tabBadge: (MainTab) -> (@Composable () -> Unit)? = { entry ->
        if (entry == MainTab.Home && availableUpdate != null) UpdateDot else null
    }
    val navLabels = LocalAppearanceExtras.current.navLabels
    val railAllowed = LocalAppearanceExtras.current.wideNav == WideNav.Rail

    val floatingBar: @Composable () -> Unit = {
        FloatingNavigationBar(
            color = if (backdrop != null) Color.Transparent else MiuixTheme.colorScheme.surfaceContainer,
            modifier = Modifier.frostedGlass(
                backdrop = backdrop,
                shape = navShape,
                tint = MiuixTheme.colorScheme.surfaceContainer.copy(alpha = 0.6f),
            ),
        ) {
            visibleTabs.forEach { entry ->
                FloatingNavigationBarItem(
                    selected = tab == entry,
                    onClick = { goToTab(visibleTabs.indexOf(entry)) },
                    icon = mainTabIcon(entry),
                    label = stringResource(entry.labelRes),
                    badge = tabBadge(entry),
                )
            }
        }
    }
    val dockedBar: @Composable () -> Unit = {
        NavigationBar(
            mode = when (navLabels) {
                NavLabelMode.IconAndText -> NavigationBarDisplayMode.IconAndText
                NavLabelMode.SelectedOnly -> NavigationBarDisplayMode.IconWithSelectedLabel
                NavLabelMode.IconOnly -> NavigationBarDisplayMode.IconOnly
            },
        ) {
            visibleTabs.forEach { entry ->
                NavigationBarItem(
                    selected = tab == entry,
                    onClick = { goToTab(visibleTabs.indexOf(entry)) },
                    icon = mainTabIcon(entry),
                    label = stringResource(entry.labelRes),
                    badge = tabBadge(entry),
                )
            }
        }
    }
    val rail: @Composable () -> Unit = {
        NavigationRail {
            visibleTabs.forEach { entry ->
                NavigationRailItem(
                    selected = tab == entry,
                    onClick = { goToTab(visibleTabs.indexOf(entry)) },
                    icon = mainTabIcon(entry),
                    label = stringResource(entry.labelRes),
                    badge = tabBadge(entry),
                )
            }
        }
    }
    val materialIcon: @Composable (MainTab) -> Unit = { entry ->
        MaterialBadgedBox(
            badge = { if (entry == MainTab.Home && availableUpdate != null) MaterialBadge() },
        ) {
            MaterialIcon(imageVector = mainTabIcon(entry), contentDescription = null)
        }
    }
    val materialBar: @Composable () -> Unit = {
        MaterialNavigationBar {
            visibleTabs.forEach { entry ->
                MaterialNavigationBarItem(
                    selected = tab == entry,
                    onClick = { goToTab(visibleTabs.indexOf(entry)) },
                    icon = { materialIcon(entry) },
                    label = if (navLabels == NavLabelMode.IconOnly) {
                        null
                    } else {
                        { MaterialText(stringResource(entry.labelRes)) }
                    },
                    alwaysShowLabel = navLabels == NavLabelMode.IconAndText,
                )
            }
        }
    }
    val materialRail: @Composable () -> Unit = {
        MaterialNavigationRail {
            visibleTabs.forEach { entry ->
                MaterialNavigationRailItem(
                    selected = tab == entry,
                    onClick = { goToTab(visibleTabs.indexOf(entry)) },
                    icon = { materialIcon(entry) },
                    label = { MaterialText(stringResource(entry.labelRes)) },
                )
            }
        }
    }

    val navTransition = LocalNavTransitionScope.current
    val reducedMotion = LocalReducedMotion.current
    val bottomRetreat = Modifier.retreatWhenCovered(navTransition, NavEdge.Bottom, reducedMotion)

    val pageBackground = rememberPageBackground()
        ?.takeIf { LocalAppearanceExtras.current.bgScope == BackgroundScope.All }
    CompositionLocalProvider(LocalPageBackground provides pageBackground) {
        BoxWithConstraints(modifier = Modifier.fillMaxSize().background(background)) {
            when {
                maxWidth >= RailMinWidth && railAllowed -> Row(modifier = Modifier.fillMaxSize()) {
                    Box(modifier = Modifier.retreatWhenCovered(navTransition, NavEdge.Start, reducedMotion)) {
                        if (material) materialRail() else rail()
                    }
                    Box(modifier = Modifier.weight(1f).fillMaxHeight()) { tabs(0.dp) }
                }
                material -> Box(modifier = Modifier.fillMaxSize()) {
                    tabs(MaterialNavBottomPadding)
                    Box(modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth().then(bottomRetreat)) {
                        materialBar()
                    }
                }
                liquid && liquidBackdrop != null -> Box(modifier = Modifier.fillMaxSize()) {
                    Box(modifier = Modifier.fillMaxSize().liquidSource(liquidBackdrop)) { tabs(NavBottomPadding) }
                    Box(
                        modifier = Modifier
                            .align(Alignment.BottomCenter)
                            .padding(bottom = LiquidNavBottomGap)
                            .retreatWhenCovered(navTransition, NavEdge.Bottom, reducedMotion, LiquidNavBottomGap),
                    ) {
                        LiquidBottomTabs(
                            selectedTabIndex = selectedTabProvider,
                            onTabSelected = goToTab,
                            backdrop = liquidBackdrop,
                            tabsCount = visibleTabs.size,
                            modifier = Modifier.width(NavGlassWidth),
                            barHeight = NavGlassHeight,
                            intensity = intensity,
                        ) {
                            val iconColor = MiuixTheme.colorScheme.onSurfaceContainer
                            visibleTabs.forEach { entry ->
                                LiquidBottomTab(onClick = { goToTab(visibleTabs.indexOf(entry)) }) {
                                    val active = LocalLiquidBottomTabActive.current
                                    Icon(
                                        imageVector = mainTabIcon(entry),
                                        contentDescription = stringResource(entry.labelRes),
                                        tint = if (active) iconColor else iconColor.copy(alpha = 0.4f),
                                        modifier = Modifier.size(28.dp),
                                    )
                                }
                            }
                        }
                    }
                }
                else -> Box(modifier = Modifier.fillMaxSize()) {
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .then(if (backdrop != null) Modifier.blurSource(backdrop) else Modifier),
                    ) {
                        tabs(if (docked) DockedNavBottomPadding else NavBottomPadding)
                    }
                    Box(modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth().then(bottomRetreat)) {
                        if (docked) dockedBar() else floatingBar()
                    }
                }
            }
        }
    }

    ClipboardPrompt(onJoined = { onNavigate(AppRoute.Room) })

    if (showExit) {
        ConfirmDialog(
            title = stringResource(R.string.exit_app),
            message = stringResource(R.string.exit_app_msg),
            confirmText = stringResource(R.string.exit),
            onDismiss = { showExit = false },
            onConfirm = {
                showExit = false
                (context as? Activity)?.finish()
            },
        )
    }

    if (askStats && isActive && !showUpdateDialog) {
        ManagedDialog(
            onDismiss = { askStats = false },
            title = stringResource(R.string.stats_ask_title),
            summary = stringResource(R.string.stats_ask_body),
            dismissible = false,
        ) {
            DialogButtons(
                confirmText = stringResource(R.string.stats_allow),
                cancelText = stringResource(R.string.stats_decline),
                onConfirm = {
                    close {
                        askStats = false
                        telemetry.setConsent(true)
                        telemetry.countLaunch()
                        statsScope.launch { telemetry.sendIfDue() }
                    }
                },
                onCancel = {
                    close {
                        askStats = false
                        telemetry.setConsent(false)
                    }
                },
            )
        }
    }

    NoticeDialog(
        notice = noticeDialogs.firstOrNull().takeIf { isActive && !showUpdateDialog && !askStats },
        onAction = openNotice,
        onClose = noticeCenter::dialogClosed,
    )

    UpdateDialog(
        update = if (showUpdateDialog) availableUpdate else null,
        onDismiss = { showUpdateDialog = false },
        onUpdate = {
            availableUpdate?.let { openDownload(context, it.downloadUrl) }
            showUpdateDialog = false
        },
    )
}
