package top.mccampfire.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.ui.draw.scale
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.res.imageResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.graphics.ImageBitmap
import com.zeroui.kit.motion.LocalReducedMotion
import kotlinx.coroutines.delay
import top.mccampfire.app.R
import kotlin.math.roundToInt

private const val FrameMillis = 100L
private const val ContentFraction = 2f / 3f

/** Zoom of the icon background that shows the part a launcher shows. */
private const val BackgroundZoom = 1.5f

/** Area of the sprite frames that holds the campfire in every frame. */
private val SourceOffset = IntOffset(3, 9)
private val SourceSize = IntSize(90, 85)

/** Animated campfire tile, with the icon background unless [background] is false; shows a still frame while reduced motion is on. */
@Composable
fun CampfireLogo(shape: Shape, modifier: Modifier = Modifier, description: String? = null, background: Boolean = true) {
    val sheet = ImageBitmap.imageResource(R.drawable.campfire_frames)
    val frameSize = sheet.height
    val count = sheet.width / frameSize
    val reduced = LocalReducedMotion.current
    var frame by remember { mutableIntStateOf(0) }
    LaunchedEffect(reduced, count) {
        if (reduced) {
            frame = 0
            return@LaunchedEffect
        }
        while (true) {
            delay(FrameMillis)
            frame = (frame + 1) % count
        }
    }
    Box(
        modifier = modifier
            .clip(shape)
            .then(if (description != null) Modifier.semantics { contentDescription = description } else Modifier),
    ) {
        if (background) Image(
            painter = painterResource(R.drawable.ic_launcher_bg),
            contentDescription = null,
            modifier = Modifier
                .matchParentSize()
                .scale(BackgroundZoom),
            contentScale = ContentScale.Crop,
        )
        Canvas(modifier = Modifier.matchParentSize()) {
            val scale = size.minDimension * ContentFraction / maxOf(SourceSize.width, SourceSize.height)
            val width = (SourceSize.width * scale).roundToInt()
            val height = (SourceSize.height * scale).roundToInt()
            drawImage(
                image = sheet,
                srcOffset = IntOffset(frame * frameSize + SourceOffset.x, SourceOffset.y),
                srcSize = SourceSize,
                dstOffset = IntOffset(((size.width - width) / 2).roundToInt(), ((size.height - height) / 2).roundToInt()),
                dstSize = IntSize(width, height),
                filterQuality = FilterQuality.None,
            )
        }
    }
}
