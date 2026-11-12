package top.mccampfire.app.feature.welcome

import top.mccampfire.app.ui.CampfireLogo
import android.os.Build
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.PagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.BlendMode
import androidx.compose.ui.graphics.BlurEffect
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.CompositingStrategy
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.TileMode
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.painter.Painter
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.glass.LiquidBackdrop
import com.kyant.shapes.RoundedRectangle
import top.mccampfire.app.R
import com.zeroui.kit.motion.ZeroMotion
import com.zeroui.kit.glass.liquidGlass
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.launch
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.IconButton
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import top.yukonga.miuix.kmp.icon.MiuixIcons
import top.yukonga.miuix.kmp.icon.extended.Back
import top.yukonga.miuix.kmp.icon.extended.Refresh
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.exp
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.round
import kotlin.math.sin

/** Fixed light palette of the welcome flow. */
internal val TitleColor = Color(0xFF14182B)
internal val SubtitleColor = Color(0xFF5B6076)
internal val BodyColor = Color(0xFF3A3F55)
internal val SuccessGreen = Color(0xFF30C85E)
internal val StatusRed = Color(0xFFE0604D)
internal val AccentBlue = Color(0xFF3482FF)

/** Base color of the flowing background. */
internal val WelcomeSurface = Color(0xFFF7F7F7)

/** Page indexes. */
internal const val PAGE_WELCOME = 0
internal const val PAGE_ACCESS = 1
internal const val PAGE_STATUS = 2
internal const val PAGE_DONE = 3

/** Fast-in, slow-out easing. */
private val EnterEasing = CubicBezierEasing(0.05f, 0.7f, 0.1f, 1f)

/** Translucent white panel. */
internal fun Modifier.frostedPanel(radius: Dp, alpha: Float = 0.30f): Modifier =
    clip(RoundedCornerShape(radius)).background(Color.White.copy(alpha = alpha))

private fun smoothstep(x: Float): Float {
    val t = x.coerceIn(0f, 1f)
    return t * t * (3f - 2f * t)
}

/** Continuous pager position. */
internal fun PagerState.position(): Float = currentPage + currentPageOffsetFraction

/** Offset of [page] from the viewport center. */
internal fun PagerState.offsetOf(page: Int): Float = (currentPage - page) + currentPageOffsetFraction

/** Weight of [page] at [position]: 1 at the center, 0 one page away. */
private fun weightOf(position: Float, page: Int): Float = (1f - abs(position - page)).coerceIn(0f, 1f)

/** Base distance of parallax offsets. */
private val PARALLAX_SPAN = 150.dp

/** Scales, fades and blurs a page by its distance from the center. */
internal fun Modifier.depthPage(state: PagerState, page: Int): Modifier = graphicsLayer {
    val a = abs(state.offsetOf(page)).coerceIn(0f, 1f)
    val s = 1f - 0.06f * a
    scaleX = s
    scaleY = s
    alpha = 1f - a * 0.85f
    renderEffect = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && a > 0.01f) {
        val r = a * 10.dp.toPx()
        BlurEffect(r, r, TileMode.Decal)
    } else {
        null
    }
}

/** Shifts an element horizontally by [depth] during page drags. */
internal fun Modifier.parallax(state: PagerState, page: Int, depth: Float): Modifier = graphicsLayer {
    translationX = state.offsetOf(page).coerceIn(-1f, 1f) * depth * PARALLAX_SPAN.toPx()
}

private const val REVEAL_BASE_MS = 180
private const val REVEAL_STAGGER_MS = 90

/** Staggered entrance; [wipe] adds a left-to-right reveal, [tick] adds a haptic. */
@Composable
internal fun Modifier.revealIn(shown: Boolean, order: Int, wipe: Boolean = false, tick: Boolean = false): Modifier {
    val haptic = LocalHapticFeedback.current
    val v = remember { Animatable(if (shown) 1f else 0f) }
    LaunchedEffect(shown) {
        if (!shown || v.value >= 1f) return@LaunchedEffect
        delay((REVEAL_BASE_MS + order * REVEAL_STAGGER_MS).toLong())
        if (tick) {
            launch {
                delay(260)
                haptic.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
            }
        }
        v.animateTo(1f, tween(820, easing = EnterEasing))
    }
    return this
        .graphicsLayer {
            val t = v.value
            if (t >= 1f) return@graphicsLayer
            alpha = t
            translationY = (1f - t) * 26.dp.toPx()
            val s = 0.95f + 0.05f * t
            scaleX = s
            scaleY = s
            if (wipe) compositingStrategy = CompositingStrategy.Offscreen
            renderEffect = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && t < 0.99f) {
                val r = (1f - t) * 14.dp.toPx()
                BlurEffect(r, r, TileMode.Decal)
            } else {
                null
            }
        }
        .drawWithContent {
            drawContent()
            val t = v.value
            if (!wipe || t >= 1f) return@drawWithContent
            val band = size.width * 0.45f
            val edge = -band + (size.width + band) * t
            drawRect(
                brush = Brush.horizontalGradient(
                    colors = listOf(Color.Black, Color.Transparent),
                    startX = edge,
                    endX = edge + band,
                ),
                blendMode = BlendMode.DstIn,
            )
        }
}

/** Sweeps a highlight across the content when [key] increases. */
@Composable
internal fun Modifier.shineOn(key: Int): Modifier {
    val progress = remember { Animatable(1f) }
    LaunchedEffect(key) {
        if (key <= 0) return@LaunchedEffect
        delay(200)
        progress.snapTo(0f)
        progress.animateTo(1f, tween(1300, easing = FastOutSlowInEasing))
    }
    return this
        .graphicsLayer { compositingStrategy = CompositingStrategy.Offscreen }
        .drawWithContent {
            drawContent()
            val p = progress.value
            if (p <= 0f || p >= 1f) return@drawWithContent
            val band = size.width * 0.3f
            val x = -band + (size.width + band * 2f) * p
            drawRect(
                brush = Brush.linearGradient(
                    colors = listOf(Color.Transparent, Color.White.copy(alpha = 0.9f), Color.Transparent),
                    start = Offset(x - band, 0f),
                    end = Offset(x + band, size.height),
                ),
                blendMode = BlendMode.SrcAtop,
            )
        }
}

/** Page title and subtitle. */
@Composable
internal fun PageHeading(
    state: PagerState,
    page: Int,
    title: String,
    subtitle: String,
    shineKey: Int = 0,
) {
    Text(
        text = title,
        modifier = Modifier
            .parallax(state, page, 0.18f)
            .shineOn(shineKey),
        color = TitleColor,
        fontSize = 30.sp,
        fontWeight = FontWeight.Bold,
        textAlign = TextAlign.Center,
    )
    Spacer(Modifier.height(12.dp))
    AnimatedContent(
        targetState = subtitle,
        modifier = Modifier
            .parallax(state, page, 0.38f),
        transitionSpec = { ZeroMotion.fadeThrough(clip = false) },
        contentAlignment = Alignment.Center,
        label = "pageSubtitle",
    ) { sub ->
        Text(
            text = sub,
            color = SubtitleColor,
            fontSize = 16.sp,
            textAlign = TextAlign.Center,
        )
    }
}

/** Visual state of the access page ring. */
internal enum class OrbStatus { Idle, Busy, Success, Failed }

private const val TICKS = 60
private val TickDim = Color(0xFF14182B).copy(alpha = 0.12f)

/** Logo and tick ring that morph with the pager position. */
@Composable
internal fun HeroScene(
    position: () -> Float,
    settledPage: Int,
    accessStatus: OrbStatus,
    accessIcon: Painter,

    accessIconRound: Boolean,
    statusPassed: Int,
    statusTotal: Int,
    modifier: Modifier = Modifier,
) {
    val haptic = LocalHapticFeedback.current
    val textMeasurer = rememberTextMeasurer()
    val currentStatus by rememberUpdatedState(accessStatus)
    val nearestPage by remember { derivedStateOf { round(position()).toInt() } }

    LaunchedEffect(Unit) {
        snapshotFlow { nearestPage }.drop(1).collect {
            haptic.performHapticFeedback(HapticFeedbackType.SegmentTick)
        }
    }

    val accessDone = accessStatus == OrbStatus.Success || accessStatus == OrbStatus.Failed
    val accessFill by animateFloatAsState(
        targetValue = if (accessDone) 1f else 0f,
        animationSpec = tween(if (accessStatus == OrbStatus.Success) 1000 else 420, easing = FastOutSlowInEasing),
        label = "accessFill",
    )
    val accessColor by animateColorAsState(
        targetValue = when (accessStatus) {
            OrbStatus.Success -> SuccessGreen
            OrbStatus.Failed -> StatusRed
            else -> AccentBlue
        },
        animationSpec = tween(360),
        label = "accessColor",
    )

    val badgePop = remember { Animatable(0f) }
    val badgeTrim = remember { Animatable(0f) }
    LaunchedEffect(accessStatus) {
        when (accessStatus) {
            OrbStatus.Success, OrbStatus.Failed -> {
                badgeTrim.snapTo(0f)
                if (accessStatus == OrbStatus.Success) delay(800)
                launch { badgePop.animateTo(1f, spring(dampingRatio = 0.45f, stiffness = 320f)) }
                delay(120)
                badgeTrim.animateTo(1f, tween(340, easing = FastOutSlowInEasing))
            }
            else -> {
                launch { badgeTrim.animateTo(0f, tween(120)) }
                badgePop.animateTo(0f, tween(180))
            }
        }
    }
    val scan by animateFloatAsState(
        targetValue = if (accessStatus == OrbStatus.Busy) 1f else 0f,
        animationSpec = tween(420),
        label = "scan",
    )
    val total = statusTotal.coerceAtLeast(1)
    val statusFill by animateFloatAsState(
        targetValue = statusPassed.toFloat() / total,
        animationSpec = spring(dampingRatio = 0.95f, stiffness = 40f),
        label = "statusFill",
    )
    val statusColor by animateColorAsState(
        targetValue = if (statusPassed >= total) SuccessGreen else AccentBlue,
        animationSpec = tween(420),
        label = "statusColor",
    )

    var scanAngle by remember { mutableFloatStateOf(-PI.toFloat() / 2f) }
    LaunchedEffect(Unit) {
        var last = withFrameNanos { it }
        while (true) {
            withFrameNanos { now ->
                val raw = scanAngle + 7f * (now - last) / 1_000_000_000f
                if (raw >= 1.5f * PI.toFloat() && currentStatus == OrbStatus.Busy) {
                    haptic.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
                }
                scanAngle = if (raw >= 1.5f * PI.toFloat()) raw - 2f * PI.toFloat() else raw
                last = now
            }
        }
    }

    val shake = remember { Animatable(1f) }
    LaunchedEffect(accessStatus) {
        when (accessStatus) {
            OrbStatus.Success -> {
                haptic.performHapticFeedback(HapticFeedbackType.Confirm)

                delay(420)
                haptic.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
                delay(420)
                haptic.performHapticFeedback(HapticFeedbackType.SegmentTick)
            }
            OrbStatus.Failed -> {
                haptic.performHapticFeedback(HapticFeedbackType.Reject)
                launch {
                    delay(160)
                    haptic.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
                    delay(107)
                    haptic.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
                }
                shake.snapTo(0f)
                shake.animateTo(1f, tween(640, easing = LinearEasing))
            }
            else -> Unit
        }
    }

    val wave = remember { Animatable(1f) }
    LaunchedEffect(settledPage) {
        if (settledPage != PAGE_DONE) return@LaunchedEffect
        launch {
            repeat(8) { i ->
                delay(if (i == 0) 60L else 120L)
                haptic.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
            }
            haptic.performHapticFeedback(HapticFeedbackType.Confirm)
        }
        wave.snapTo(0f)
        wave.animateTo(1f, tween(1150, easing = LinearOutSlowInEasing))
    }

    Box(
        modifier = modifier.graphicsLayer {
            val sh = shake.value
            translationX = if (sh < 1f) sin(sh * PI.toFloat() * 6f) * (1f - sh) * 12.dp.toPx() else 0f
        },
        contentAlignment = Alignment.Center,
    ) {
        CampfireLogo(
            shape = LogoShape,
            background = false,
            modifier = Modifier
                .fillMaxSize()
                .graphicsLayer {
                    val w0 = weightOf(position(), PAGE_WELCOME)
                    val s = 0.45f + 0.55f * w0
                    scaleX = s
                    scaleY = s
                    alpha = w0
                },
        )

        Canvas(Modifier.fillMaxSize()) {
            val p = position()
            val ringVis = 1f - weightOf(p, PAGE_WELCOME)
            if (ringVis <= 0.001f) return@Canvas

            val q = p.coerceIn(PAGE_ACCESS.toFloat(), PAGE_DONE.toFloat())
            val i = floor(q).toInt().coerceAtMost(PAGE_DONE - 1)
            val t = q - i
            fun fillOf(k: Int) = when (k) {
                PAGE_ACCESS -> accessFill
                PAGE_STATUS -> statusFill
                else -> 1f
            }
            fun colorOf(k: Int) = when (k) {
                PAGE_ACCESS -> accessColor
                PAGE_STATUS -> statusColor
                else -> SuccessGreen
            }
            val fill = fillOf(i) + (fillOf(i + 1) - fillOf(i)) * t
            val color = lerp(colorOf(i), colorOf(i + 1), t)
            val scanAmt = scan * weightOf(p, PAGE_ACCESS)
            val waveV = wave.value
            val waveAmt = if (waveV < 1f) weightOf(p, PAGE_DONE) else 0f

            val r = size.minDimension / 2f
            val c = center

            val open = 0.7f + 0.3f * ringVis

            drawCircle(
                brush = Brush.radialGradient(
                    colors = listOf(color.copy(alpha = 0.16f * ringVis * (0.3f + 0.7f * fill)), Color.Transparent),
                    center = c,
                    radius = r * 1.2f,
                ),
                radius = r * 1.2f,
                center = c,
            )

            val rIn = r * 0.8f * open
            val base = r * 0.1f
            val sw = max(r * 0.032f, 1.2.dp.toPx())
            val edge = fill * TICKS
            val filling = fill > 0.001f && fill < 0.999f
            val twoPi = 2f * PI.toFloat()
            for (k in 0 until TICKS) {
                val th = -PI.toFloat() / 2f + k * twoPi / TICKS
                val lit = (edge - k).coerceIn(0f, 1f)

                val bulge = if (filling) exp(-((k + 0.5f - edge) / 2.2f).pow(2)) else 0f

                var d = abs(th - scanAngle) % twoPi
                if (d > PI.toFloat()) d = twoPi - d
                val sc = scanAmt * exp(-(d / 0.5f).pow(2))

                val wv = waveAmt * exp(-((k.toFloat() / TICKS - (waveV * 1.25f - 0.1f)) / 0.05f).pow(2))
                val len = base * (1f + 0.35f * lit + 0.8f * bulge + 0.9f * sc + 1.1f * wv)
                val mix = max(lit, sc)
                val tickColor = lerp(TickDim, color, mix)
                val dir = Offset(cos(th), sin(th))
                drawLine(
                    color = tickColor.copy(alpha = tickColor.alpha * ringVis),
                    start = c + dir * rIn,
                    end = c + dir * (rIn + len),
                    strokeWidth = sw,
                    cap = StrokeCap.Round,
                )
            }

            val box = r * 0.9f
            val stroke = r * 0.075f

            val w2 = weightOf(p, PAGE_STATUS)
            if (w2 > 0.001f) {
                val layout = textMeasurer.measure(
                    text = "$statusPassed/$total",
                    style = TextStyle(
                        fontSize = (r * 0.42f).toSp(),
                        fontWeight = FontWeight.SemiBold,
                        color = TitleColor,
                    ),
                )
                drawText(
                    textLayoutResult = layout,
                    topLeft = Offset(c.x - layout.size.width / 2f, c.y - layout.size.height / 2f),
                    alpha = w2,
                )
            }

            val w3 = weightOf(p, PAGE_DONE)
            if (w3 > 0.001f) {
                drawCheck(c, box, SuccessGreen.copy(alpha = w3), stroke * 1.1f, smoothstep((w3 - 0.3f) / 0.7f))
            }
        }

        Image(
            painter = accessIcon,
            contentDescription = null,
            modifier = Modifier
                .fillMaxSize(ACCESS_ICON_FRACTION)
                .graphicsLayer {
                    val w1 = weightOf(position(), PAGE_ACCESS)
                    alpha = w1
                    val breath = 1f + 0.03f * scan * sin(scanAngle)
                    val sc = (0.65f + 0.35f * w1) * breath
                    scaleX = sc
                    scaleY = sc
                    shadowElevation = if (accessIconRound) 14.dp.toPx() * w1 else 0f
                    shape = if (accessIconRound) LogoShape else CircleShape
                    clip = accessIconRound
                },
        )

        Canvas(Modifier.fillMaxSize()) {
            val pop = badgePop.value
            val w1 = weightOf(position(), PAGE_ACCESS)
            if (pop <= 0.001f || w1 <= 0.001f) return@Canvas
            val s = size.minDimension
            val half = s * ACCESS_ICON_FRACTION / 2f
            val bc = center + Offset(half * 0.72f, half * 0.72f)
            val br = s * 0.075f * pop
            drawCircle(Color.White.copy(alpha = w1), radius = br * 1.28f, center = bc)
            drawCircle(accessColor.copy(alpha = w1), radius = br, center = bc)
            val t = badgeTrim.value
            if (t > 0.001f) {
                val mark = Color.White.copy(alpha = w1)
                val width = br * 0.26f
                if (accessStatus == OrbStatus.Success) {
                    drawTrimmedPolyline(
                        points = listOf(
                            Offset(bc.x - br * 0.42f, bc.y + br * 0.02f),
                            Offset(bc.x - br * 0.1f, bc.y + br * 0.34f),
                            Offset(bc.x + br * 0.44f, bc.y - br * 0.3f),
                        ),
                        t = t,
                        color = mark,
                        width = width,
                    )
                } else {
                    val d = br * 0.34f
                    drawTrimmedPolyline(
                        points = listOf(Offset(bc.x - d, bc.y - d), Offset(bc.x + d, bc.y + d)),
                        t = (t * 2f).coerceAtMost(1f),
                        color = mark,
                        width = width,
                    )
                    drawTrimmedPolyline(
                        points = listOf(Offset(bc.x + d, bc.y - d), Offset(bc.x - d, bc.y + d)),
                        t = (t * 2f - 1f).coerceIn(0f, 1f),
                        color = mark,
                        width = width,
                    )
                }
            }
        }
    }
}

/** Icon size relative to the hero size. */
private const val ACCESS_ICON_FRACTION = 0.56f

private val LogoShape = RoundedCornerShape(22)

/** Draws a check mark at progress [t]. */
private fun DrawScope.drawCheck(center: Offset, box: Float, color: Color, stroke: Float, t: Float) {
    val ox = center.x - box / 2f
    val oy = center.y - box / 2f
    drawTrimmedPolyline(
        points = listOf(
            Offset(ox + 0.3f * box, oy + 0.52f * box),
            Offset(ox + 0.45f * box, oy + 0.66f * box),
            Offset(ox + 0.72f * box, oy + 0.36f * box),
        ),
        t = t,
        color = color,
        width = stroke,
    )
}

/** Draws the leading fraction [t] of a polyline. */
private fun DrawScope.drawTrimmedPolyline(points: List<Offset>, t: Float, color: Color, width: Float) {
    if (t <= 0f || points.size < 2) return
    var total = 0f
    for (i in 1 until points.size) total += (points[i] - points[i - 1]).getDistance()
    var remain = total * t.coerceAtMost(1f)
    val path = Path().apply { moveTo(points[0].x, points[0].y) }
    for (i in 1 until points.size) {
        if (remain <= 0f) break
        val seg = (points[i] - points[i - 1]).getDistance()
        val f = (remain / seg).coerceAtMost(1f)
        path.lineTo(
            points[i - 1].x + (points[i].x - points[i - 1].x) * f,
            points[i - 1].y + (points[i].y - points[i - 1].y) * f,
        )
        remain -= seg
    }
    drawPath(path, color, style = Stroke(width = width, cap = StrokeCap.Round, join = StrokeJoin.Round))
}

/** Documents linked from the first page. */
internal enum class AgreementDoc { Disclaimer, Terms }

/** Entry buttons for the documents. */
@Composable
internal fun AgreementLinks(onOpen: (AgreementDoc) -> Unit, modifier: Modifier = Modifier) {
    val haptic = LocalHapticFeedback.current
    Column(modifier = modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        Text(
            text = stringResource(R.string.welcome_agreement_hint),
            color = SubtitleColor,
            fontSize = 13.sp,
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            for (doc in AgreementDoc.entries) {
                TextButton(
                    text = stringResource(doc.titleRes()),
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.VirtualKey)
                        onOpen(doc)
                    },
                    containerColor = AccentBlue.copy(alpha = 0.12f),
                    contentColor = AccentBlue,
                    minHeight = 36.dp,
                    insideMargin = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
                    textStyle = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Medium),
                )
            }
        }
    }
}

private fun AgreementDoc.titleRes(): Int = when (this) {
    AgreementDoc.Disclaimer -> R.string.welcome_doc_disclaimer
    AgreementDoc.Terms -> R.string.welcome_doc_terms
}

private fun AgreementDoc.bodyRes(): Int = when (this) {
    AgreementDoc.Disclaimer -> R.string.welcome_doc_disclaimer_body
    AgreementDoc.Terms -> R.string.welcome_doc_terms_body
}

/** Full-screen document page. */
@Composable
internal fun AgreementPage(doc: AgreementDoc, onBack: () -> Unit, modifier: Modifier = Modifier) {
    val body = stringResource(doc.bodyRes())
    val paragraphs = remember(body) { body.split("\n\n") }
    var shown by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { shown = true }
    Box(modifier = modifier.background(WelcomeSurface)) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .systemBarsPadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 28.dp)
        ) {
            Spacer(Modifier.height(72.dp))
            Text(
                text = stringResource(doc.titleRes()),
                modifier = Modifier.revealIn(shown, 0, wipe = true),
                color = TitleColor,
                fontSize = 32.sp,
                fontWeight = FontWeight.Bold,
            )
            Spacer(Modifier.height(24.dp))
            paragraphs.forEachIndexed { index, paragraph ->

                val split = paragraph.indexOf('\n')
                val heading = if (split > 0) paragraph.substring(0, split) else null
                val text = if (split > 0) paragraph.substring(split + 1) else paragraph

                val order = 1 + minOf(index, 5)
                Column(Modifier.revealIn(shown, order)) {
                    if (heading != null) {
                        Text(
                            text = heading,
                            color = TitleColor,
                            fontSize = 17.sp,
                            fontWeight = FontWeight.SemiBold,
                        )
                        Spacer(Modifier.height(6.dp))
                    }
                    Text(
                        text = text,
                        color = BodyColor,
                        fontSize = 15.sp,
                        lineHeight = 25.sp,
                    )
                }
                Spacer(Modifier.height(22.dp))
            }
            Spacer(Modifier.height(32.dp))
        }
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(120.dp)
                .background(Brush.verticalGradient(listOf(WelcomeSurface, WelcomeSurface.copy(alpha = 0f))))
        )
        IconButton(
            onClick = onBack,
            modifier = Modifier
                .systemBarsPadding()
                .padding(start = 16.dp, top = 12.dp),
            backgroundColor = Color.White.copy(alpha = 0.8f),
        ) {
            Icon(
                imageVector = MiuixIcons.Back,
                contentDescription = stringResource(R.string.back),
                tint = TitleColor,
            )
        }
    }
}

/** Result state of a status row. */
internal enum class GlyphState { Checking, Ok, Fail }

/** One row of the status card. */
internal class StatusRow(val label: String, val state: GlyphState, val onClick: (() -> Unit)? = null)

private val GlassCardShape = RoundedRectangle(28.dp)

/** Status card rendered as glass or a translucent panel. */
@Composable
internal fun StatusListCard(
    rows: List<StatusRow>,
    onRetry: () -> Unit,
    state: PagerState,
    page: Int,
    visible: Boolean,
    glass: LiquidBackdrop?,
) {
    val scope = rememberCoroutineScope()
    val haptic = LocalHapticFeedback.current
    val retrySpin = remember { Animatable(0f) }
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .parallax(state, page, 0.5f)
            .then(
                if (glass != null) {
                    Modifier.liquidGlass(
                        backdrop = glass,
                        shape = { GlassCardShape },
                        tint = Color.White.copy(alpha = 0.3f),
                        blurRadius = 14.dp,
                        refractionHeight = 18.dp,
                        refractionAmount = 34.dp,
                    )
                } else {
                    Modifier.frostedPanel(28.dp)
                }
            )
            .padding(14.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.status_check_title),
                color = TitleColor,
                fontSize = 16.sp,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.padding(start = 6.dp),
            )
            Spacer(Modifier.weight(1f))
            Row(
                modifier = Modifier
                    .clip(RoundedCornerShape(10.dp))
                    .clickable {
                        haptic.performHapticFeedback(HapticFeedbackType.VirtualKey)
                        scope.launch {
                            retrySpin.snapTo(0f)
                            retrySpin.animateTo(1f, tween(640, easing = FastOutSlowInEasing))
                        }
                        onRetry()
                    }
                    .padding(horizontal = 8.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(
                    imageVector = MiuixIcons.Refresh,
                    contentDescription = null,
                    tint = SubtitleColor,
                    modifier = Modifier
                        .size(18.dp)
                        .graphicsLayer { rotationZ = retrySpin.value * 360f },
                )
                Spacer(Modifier.width(5.dp))
                Text(stringResource(R.string.retry), color = SubtitleColor, fontSize = 14.sp)
            }
        }
        Spacer(Modifier.height(10.dp))
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            rows.forEachIndexed { index, row ->
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .parallax(state, page, 0.12f + index * 0.07f)
                        .frostedPanel(18.dp, alpha = 0.34f)
                        .then(if (row.onClick != null) Modifier.clickable(onClick = row.onClick) else Modifier)
                        .padding(horizontal = 16.dp, vertical = 13.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = row.label,
                        modifier = Modifier.weight(1f),
                        color = TitleColor,
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Medium,
                    )
                    StatusGlyph(
                        state = row.state,
                        visible = visible,
                        enterDelay = 120L + index * 70L,
                        modifier = Modifier.size(24.dp),
                    )
                }
            }
        }
    }
}

/** Animated spinner, check or cross. */
@Composable
private fun StatusGlyph(state: GlyphState, visible: Boolean, enterDelay: Long, modifier: Modifier) {
    val haptic = LocalHapticFeedback.current
    val pop = remember { Animatable(0f) }
    val trim = remember { Animatable(0f) }
    var mark by remember { mutableStateOf(GlyphState.Ok) }
    var played by remember { mutableStateOf<GlyphState?>(null) }
    val spinAlpha by animateFloatAsState(
        targetValue = if (state == GlyphState.Checking) 1f else 0f,
        animationSpec = tween(200),
        label = "glyphSpin",
    )
    val spin by rememberInfiniteTransition(label = "glyphSpin").animateFloat(
        initialValue = 0f,
        targetValue = 360f,
        animationSpec = infiniteRepeatable(tween(900, easing = LinearEasing)),
        label = "glyphAngle",
    )
    LaunchedEffect(state, visible) {
        if (state == GlyphState.Checking) {
            played = null
            launch { trim.animateTo(0f, tween(140)) }
            pop.animateTo(0f, tween(180))
            return@LaunchedEffect
        }
        if (!visible || played == state) return@LaunchedEffect
        val first = played == null && pop.value == 0f
        played = state
        mark = state
        trim.snapTo(0f)
        pop.snapTo(0f)
        if (first) delay(enterDelay)
        haptic.performHapticFeedback(
            if (state == GlyphState.Ok) HapticFeedbackType.SegmentTick else HapticFeedbackType.Reject
        )
        launch { pop.animateTo(1f, spring(dampingRatio = 0.42f, stiffness = 420f)) }
        delay(110)
        trim.animateTo(1f, tween(360, easing = FastOutSlowInEasing))
    }
    Canvas(modifier) {
        val w = size.minDimension
        val c = center
        if (spinAlpha > 0.01f) {
            val inset = w * 0.1f
            rotate(spin, c) {
                drawArc(
                    brush = Brush.sweepGradient(
                        0f to SubtitleColor.copy(alpha = 0f),
                        0.78f to SubtitleColor.copy(alpha = spinAlpha),
                        center = c,
                    ),
                    startAngle = 0f,
                    sweepAngle = 280f,
                    useCenter = false,
                    topLeft = Offset(inset, inset),
                    size = Size(w - inset * 2f, w - inset * 2f),
                    style = Stroke(width = w * 0.1f, cap = StrokeCap.Round),
                )
            }
        }
        val p = pop.value
        if (p > 0.001f) {
            drawCircle(if (mark == GlyphState.Ok) SuccessGreen else StatusRed, radius = w * 0.5f * p, center = c)
        }
        val t = trim.value
        if (t > 0.001f) {
            val stroke = w * 0.11f
            if (mark == GlyphState.Ok) {
                drawTrimmedPolyline(
                    points = listOf(Offset(w * 0.28f, w * 0.52f), Offset(w * 0.44f, w * 0.67f), Offset(w * 0.73f, w * 0.37f)),
                    t = t,
                    color = Color.White,
                    width = stroke,
                )
            } else {
                drawTrimmedPolyline(
                    points = listOf(Offset(w * 0.34f, w * 0.34f), Offset(w * 0.66f, w * 0.66f)),
                    t = (t * 2f).coerceAtMost(1f),
                    color = Color.White,
                    width = stroke,
                )
                drawTrimmedPolyline(
                    points = listOf(Offset(w * 0.66f, w * 0.34f), Offset(w * 0.34f, w * 0.66f)),
                    t = (t * 2f - 1f).coerceIn(0f, 1f),
                    color = Color.White,
                    width = stroke,
                )
            }
        }
    }
}

/** Page indicator with a stretching pill. */
@Composable
internal fun WormIndicator(count: Int, position: () -> Float, modifier: Modifier = Modifier) {
    val spacing = 20.dp
    val dot = 8.dp
    val pill = 22.dp
    Canvas(modifier.size(width = spacing * (count - 1) + pill, height = dot)) {
        val sp = spacing.toPx()
        val d = dot.toPx()
        val half = pill.toPx() / 2f
        val y = size.height / 2f
        for (i in 0 until count) {
            drawCircle(TitleColor.copy(alpha = 0.16f), radius = d / 2f, center = Offset(half + i * sp, y))
        }
        val p = position().coerceIn(0f, (count - 1).toFloat())
        val i = floor(p)
        val t = p - i
        val head = i + min(1f, t * 2f)
        val tail = i + max(0f, t * 2f - 1f)
        drawRoundRect(
            color = TitleColor,
            topLeft = Offset(tail * sp, 0f),
            size = Size((head - tail) * sp + half * 2f, d),
            cornerRadius = androidx.compose.ui.geometry.CornerRadius(d / 2f),
        )
    }
}

/** Button label with a rolling text change. */
@Composable
internal fun AnimatedLabel(text: String) {
    AnimatedContent(
        targetState = text,
        transitionSpec = { ZeroMotion.textTicker() },
        label = "actionLabel",
    ) { t ->
        Text(t, maxLines = 1, softWrap = false)
    }
}
