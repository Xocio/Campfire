package top.mccampfire.app.feature.welcome

import androidx.compose.ui.res.imageResource
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.painter.BitmapPainter
import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.PagerDefaults
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.isSpecified
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.layout.layout
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.glass.rememberLiquidBackdrop
import com.zeroui.kit.components.Button
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Text
import com.zeroui.kit.effect.BgEffectBackground
import com.zeroui.kit.effect.LiquidRippleMillis
import com.zeroui.kit.effect.rememberLiquidRipple
import com.zeroui.kit.glass.isLiquidGlassSupported
import com.zeroui.kit.glass.liquidSource
import top.mccampfire.app.R
import top.mccampfire.app.core.appViewModel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlin.math.PI
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.math.sin

private const val PAGE_COUNT = 4

/** Spring shared by button paging and drag snapping. */
private val PageSpring = spring<Float>(dampingRatio = 0.88f, stiffness = 170f)

private fun CheckStatus.toGlyph(): GlyphState = when (this) {
    CheckStatus.Success -> GlyphState.Ok
    CheckStatus.Failed -> GlyphState.Fail
    else -> GlyphState.Checking
}

private fun CheckStatus.toOrb(): OrbStatus = when (this) {
    CheckStatus.Idle -> OrbStatus.Idle
    CheckStatus.Checking -> OrbStatus.Busy
    CheckStatus.Success -> OrbStatus.Success
    CheckStatus.Failed -> OrbStatus.Failed
}

/** Hero center and size on one page. */
private class HeroSlot(val center: Dp, val size: Dp)

/** Hero placement for each page. */
private fun heroSlots(areaHeight: Dp): List<HeroSlot> = listOf(
    HeroSlot(center = areaHeight * 0.3f, size = 128.dp),
    HeroSlot(center = areaHeight * 0.32f, size = 208.dp),
    HeroSlot(center = 44.dp, size = 80.dp),
    HeroSlot(center = areaHeight * 0.32f, size = 208.dp),
)

/** Vertical space reserved above page content. */
private fun HeroSlot.reserved(): Dp = center + size / 2 + 28.dp

/** Ripple origin in root coordinates, read only during the layer phase. */
private class RippleAnchor {
    var center: Offset = Offset.Unspecified
    var radius: Float = 0f
    var origin: Offset = Offset.Zero
}

/** Places the hero by interpolating between neighboring page slots during layout. */
private fun Modifier.heroPlacement(
    slots: List<HeroSlot>,
    position: () -> Float,
): Modifier = layout { measurable, constraints ->
    val p = position().coerceIn(0f, (slots.size - 1).toFloat())
    val i = floor(p).toInt().coerceAtMost(slots.size - 2)
    val t = p - i
    val a = slots[i]
    val b = slots[i + 1]
    val arc = sin(t * PI.toFloat()) * 18.dp.toPx()
    val cy = a.center.toPx() + (b.center.toPx() - a.center.toPx()) * t - arc
    val s = (a.size.toPx() + (b.size.toPx() - a.size.toPx()) * t).roundToInt().coerceAtLeast(0)
    val placeable = measurable.measure(Constraints.fixed(s, s))
    layout(constraints.maxWidth, constraints.maxHeight) {
        placeable.place((constraints.maxWidth - s) / 2, (cy - s / 2f).roundToInt())
    }
}

/** First-run flow: welcome, access check, setup status and completion. */
@Composable
fun WelcomeScreen() {
    val viewModel = appViewModel { WelcomeViewModel(setupChecks, settings) }
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    val activity = context as? android.app.Activity
    var requestLocalNetworkLater by remember { mutableStateOf(false) }
    val notificationLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        viewModel.refreshItems()
        if (granted && !top.mccampfire.app.link.LocalNetwork.isGranted(context)) requestLocalNetworkLater = true
        if (!granted && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            activity?.shouldShowRequestPermissionRationale(Manifest.permission.POST_NOTIFICATIONS) == false
        ) {
            openAppSettings(context)
        }
    }
    val requestNotifications = {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            notificationLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }
    var notificationsPrompted by rememberSaveable { mutableStateOf(false) }
    val localNetworkLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        viewModel.refreshItems()
        if (!granted && activity?.shouldShowRequestPermissionRationale(top.mccampfire.app.link.LocalNetwork.Permission) == false) {
            openAppSettings(context)
        }
    }
    val requestLocalNetwork = {
        if (top.mccampfire.app.link.LocalNetwork.isRuntime) localNetworkLauncher.launch(top.mccampfire.app.link.LocalNetwork.Permission)
    }
    LaunchedEffect(requestLocalNetworkLater) {
        if (requestLocalNetworkLater) {
            requestLocalNetworkLater = false
            requestLocalNetwork()
        }
    }

    val pagerState = rememberPagerState { state.lastOpenPage + 1 }
    val position: () -> Float = remember(pagerState) { { pagerState.position() } }
    fun goTo(page: Int) {
        scope.launch { pagerState.animateScrollToPage(page, animationSpec = PageSpring) }
    }

    LifecycleResumeEffect(viewModel) {
        viewModel.refreshItems()
        onPauseOrDispose { }
    }

    LaunchedEffect(pagerState.settledPage) {
        when (pagerState.settledPage) {
            PAGE_ACCESS -> if (state.accessStatus == CheckStatus.Idle) viewModel.runAccessCheck()
            PAGE_STATUS -> if (!notificationsPrompted) {
                notificationsPrompted = true
                val notifications = state.items.firstOrNull { it.id == SetupItemId.Notifications }
                val localNetwork = state.items.firstOrNull { it.id == SetupItemId.LocalNetwork }
                when {
                    notifications?.status == CheckStatus.Failed -> requestNotifications()
                    localNetwork?.status == CheckStatus.Failed -> requestLocalNetwork()
                }
            }
        }
    }

    val statusRows = state.items.map { item ->
        StatusRow(
            label = stringResource(item.id.labelRes),
            state = item.status.toGlyph(),
            onClick = when {
                item.status != CheckStatus.Failed -> null
                item.id == SetupItemId.Notifications -> requestNotifications
                item.id == SetupItemId.BackgroundRun -> { { requestBackgroundRun(context) } }
                item.id == SetupItemId.LocalNetwork -> requestLocalNetwork
                else -> null
            },
        )
    }

    var doneVisits by remember { mutableIntStateOf(0) }
    LaunchedEffect(pagerState.settledPage) {
        if (pagerState.settledPage == PAGE_DONE) doneVisits++
    }

    var openDoc by rememberSaveable { mutableStateOf<AgreementDoc?>(null) }
    var shownDoc by rememberSaveable { mutableStateOf(AgreementDoc.Disclaimer) }
    BackHandler(enabled = openDoc != null) { openDoc = null }
    val docProgress by animateFloatAsState(
        targetValue = if (openDoc != null) 1f else 0f,
        animationSpec = spring(dampingRatio = 0.9f, stiffness = 320f),
        label = "docRecede",
    )

    val glassSource = rememberLiquidBackdrop(always = true)

    val ripple = rememberLiquidRipple()
    val rippleProgress = remember { Animatable(1f) }
    val rippleAnchor = remember { RippleAnchor() }
    fun splash() {
        if (ripple == null) return
        scope.launch {
            rippleProgress.snapTo(0f)
            rippleProgress.animateTo(1f, tween(LiquidRippleMillis, easing = LinearEasing))
        }
    }
    LaunchedEffect(state.accessStatus) {
        if (state.accessStatus == CheckStatus.Success) {
            delay(800)
            splash()
        }
    }
    LaunchedEffect(pagerState.settledPage) {
        if (pagerState.settledPage == PAGE_DONE) splash()
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .onGloballyPositioned { rippleAnchor.origin = it.positionInRoot() }
            .then(
                if (ripple == null) {
                    Modifier
                } else {
                    Modifier.graphicsLayer {
                        val center = rippleAnchor.center
                        renderEffect = if (center.isSpecified) {
                            ripple.effect(
                                p = rippleProgress.value,
                                center = center - rippleAnchor.origin,
                                minRadius = rippleAnchor.radius,
                                maxRadius = max(size.width, size.height) * 0.75f,
                                density = density,
                            )
                        } else {
                            null
                        }
                    }
                },
            )
            .background(WelcomeSurface),
    ) {
        BgEffectBackground(
            dynamicBackground = true,
            modifier = Modifier
                .fillMaxSize()
                .graphicsLayer {
                    val scale = 1f - 0.05f * docProgress
                    scaleX = scale
                    scaleY = scale
                    alpha = 1f - 0.4f * docProgress
                },
            isFullSize = true,
            bgModifier = Modifier
                .liquidSource(glassSource)
                .graphicsLayer {
                    scaleX = 1.2f
                    scaleY = 1.2f
                    val p = position()
                    translationX = (1.5f - p) * 18.dp.toPx()
                    translationY = (1.5f - p) * 12.dp.toPx()
                },
            surface = WelcomeSurface,
            isDarkTheme = false,
        ) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .systemBarsPadding(),
            ) {
                Text(
                    text = stringResource(R.string.app_name),
                    color = TitleColor,
                    fontSize = 24.sp,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier
                        .padding(start = 28.dp, top = 12.dp),
                )

                BoxWithConstraints(
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f),
                ) {
                    val slots = heroSlots(maxHeight)

                    HorizontalPager(
                        state = pagerState,
                        modifier = Modifier.fillMaxSize(),
                        beyondViewportPageCount = 1,
                        flingBehavior = PagerDefaults.flingBehavior(
                            state = pagerState,
                            snapAnimationSpec = PageSpring,
                        ),
                    ) { page ->
                        Box(
                            modifier = Modifier
                                .fillMaxSize()
                                .depthPage(pagerState, page),
                        ) {
                            Column(
                                modifier = Modifier
                                    .fillMaxSize()
                                    .verticalScroll(rememberScrollState())
                                    .padding(horizontal = 28.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                            ) {
                                Spacer(Modifier.height(slots[page].reserved()))
                                when (page) {
                                    PAGE_WELCOME -> PageHeading(
                                        state = pagerState,
                                        page = page,
                                        title = stringResource(R.string.welcome_title),
                                        subtitle = stringResource(R.string.welcome_subtitle),
                                    )

                                    PAGE_ACCESS -> PageHeading(
                                        state = pagerState,
                                        page = page,
                                        title = stringResource(R.string.welcome_access_title),
                                        subtitle = stringResource(
                                            when (state.accessStatus) {
                                                CheckStatus.Idle -> R.string.welcome_access_idle
                                                CheckStatus.Checking -> R.string.welcome_access_checking
                                                CheckStatus.Success -> R.string.welcome_access_success
                                                CheckStatus.Failed -> R.string.welcome_access_failed
                                            },
                                        ),
                                    )

                                    PAGE_STATUS -> {
                                        PageHeading(
                                            state = pagerState,
                                            page = page,
                                            title = stringResource(R.string.welcome_status_title),
                                            subtitle = stringResource(R.string.welcome_status_subtitle),
                                        )
                                        Spacer(Modifier.height(20.dp))
                                        StatusListCard(
                                            rows = statusRows,
                                            onRetry = viewModel::refreshItems,
                                            state = pagerState,
                                            page = page,
                                            visible = pagerState.currentPage == page,
                                            glass = glassSource,
                                        )
                                    }

                                    else -> PageHeading(
                                        state = pagerState,
                                        page = page,
                                        title = stringResource(R.string.welcome_done_title),
                                        subtitle = stringResource(R.string.welcome_done_subtitle),
                                        shineKey = doneVisits,
                                    )
                                }
                                Spacer(Modifier.height(16.dp))
                            }

                            if (page == PAGE_WELCOME) {
                                AgreementLinks(
                                    onOpen = { doc ->
                                        shownDoc = doc
                                        openDoc = doc
                                    },
                                    modifier = Modifier
                                        .align(Alignment.BottomCenter)
                                        .padding(horizontal = 28.dp)
                                        .padding(bottom = 12.dp)
                                        .parallax(pagerState, page, 0.3f),
                                )
                            }
                        }
                    }

                    HeroScene(
                        position = position,
                        settledPage = pagerState.settledPage,
                        accessStatus = state.accessStatus.toOrb(),
                        accessIcon = BitmapPainter(ImageBitmap.imageResource(R.drawable.ic_shield), filterQuality = FilterQuality.None),
                        accessIconRound = false,
                        statusPassed = statusRows.count { it.state == GlyphState.Ok },
                        statusTotal = statusRows.size,
                        modifier = Modifier
                            .matchParentSize()
                            .heroPlacement(slots, position)
                            .onGloballyPositioned { coordinates ->
                                rippleAnchor.center = coordinates.positionInRoot() +
                                    Offset(coordinates.size.width / 2f, coordinates.size.height / 2f)
                                rippleAnchor.radius = coordinates.size.width * 0.38f
                            },
                    )
                }

                WormIndicator(
                    count = PAGE_COUNT,
                    position = position,
                    modifier = Modifier
                        .align(Alignment.CenterHorizontally)
                        .padding(vertical = 18.dp),
                )

                WelcomeBottomBar(
                    page = pagerState.currentPage,
                    state = state,
                    onPrevious = { goTo(pagerState.currentPage - 1) },
                    onNext = { goTo(pagerState.currentPage + 1) },
                    onRetryAccess = viewModel::runAccessCheck,
                    onRetrySetup = {
                        val missing = state.items.firstOrNull { it.id.required && it.status == CheckStatus.Failed }?.id
                        when (missing) {
                            SetupItemId.Notifications -> requestNotifications()
                            SetupItemId.LocalNetwork -> requestLocalNetwork()
                            else -> viewModel.refreshItems()
                        }
                    },
                    onFinish = viewModel::complete,
                    modifier = Modifier
                        .padding(horizontal = 28.dp)
                        .padding(bottom = 20.dp),
                )
            }
        }

        AnimatedVisibility(
            visible = openDoc != null,
            enter = slideInHorizontally(initialOffsetX = { it }) + fadeIn(),
            exit = slideOutHorizontally(targetOffsetX = { it }) + fadeOut(),
        ) {
            AgreementPage(
                doc = shownDoc,
                onBack = { openDoc = null },
                modifier = Modifier.fillMaxSize(),
            )
        }
    }
}

/** Label, availability and action of the main button. */
private data class NextAction(val text: String, val enabled: Boolean, val action: () -> Unit)

/** Previous button that expands after the first page, followed by the main button. */
@Composable
private fun WelcomeBottomBar(
    page: Int,
    state: WelcomeUiState,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onRetryAccess: () -> Unit,
    onRetrySetup: () -> Unit,
    onFinish: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val nextText = stringResource(R.string.action_next)
    val retryText = stringResource(R.string.action_retry)
    val checkingText = stringResource(R.string.action_checking)
    val next = when (page) {
        PAGE_WELCOME -> NextAction(nextText, true, onNext)
        PAGE_ACCESS -> when (state.accessStatus) {
            CheckStatus.Success -> NextAction(nextText, true, onNext)
            CheckStatus.Failed -> NextAction(retryText, true, onRetryAccess)
            else -> NextAction(checkingText, false) {}
        }
        PAGE_STATUS -> if (state.setupReady) {
            NextAction(nextText, true, onNext)
        } else {
            NextAction(retryText, true, onRetrySetup)
        }
        else -> NextAction(stringResource(R.string.action_get_started), true, onFinish)
    }

    val previous by animateFloatAsState(
        targetValue = if (page > 0) 1f else 0f,
        animationSpec = spring(dampingRatio = 0.78f, stiffness = 300f),
        label = "previousButton",
    )
    val haptic = LocalHapticFeedback.current

    Row(
        modifier = modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (previous > 0.01f) {
            Box(
                modifier = Modifier
                    .weight(previous)
                    .clipToBounds()
                    .graphicsLayer {
                        alpha = previous.coerceIn(0f, 1f)
                        translationX = (1f - previous) * -24.dp.toPx()
                    },
            ) {
                Button(
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.VirtualKey)
                        onPrevious()
                    },
                    enabled = page > 0,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text(stringResource(R.string.action_previous), maxLines = 1, softWrap = false)
                }
            }
            Spacer(Modifier.width(14.dp * previous.coerceIn(0f, 1f)))
        }
        Button(
            onClick = {
                haptic.performHapticFeedback(HapticFeedbackType.VirtualKey)
                next.action()
            },
            enabled = next.enabled,
            variant = ButtonVariant.Primary,
            modifier = Modifier.weight(1f),
        ) {
            AnimatedLabel(next.text)
        }
    }
}

/** Opens the system page of this app, where denied permissions can be allowed. */
internal fun openAppSettings(context: Context) {
    runCatching {
        context.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + context.packageName)))
    }
}

/** Opens the system page that exempts the app from battery optimization. */
@SuppressLint("BatteryLife")
internal fun requestBackgroundRun(context: Context) {
    val request = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + context.packageName))
    val fallback = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
    runCatching { context.startActivity(request) }.recoverCatching { context.startActivity(fallback) }
}
