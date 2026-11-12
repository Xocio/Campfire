package top.mccampfire.app.feature.home

import androidx.compose.animation.Crossfade
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import top.mccampfire.app.data.HomeBackground
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.app.feature.settings.LocalAppearanceExtras
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import top.yukonga.miuix.kmp.theme.MiuixTheme

private object HomeBackgroundCache {
    var name: String = ""
    var bitmap: ImageBitmap? = null

    fun peek(key: String): ImageBitmap? = if (key == name) bitmap else null
}

@Composable
fun rememberHomeBackground(): ImageBitmap? {
    val context = LocalContext.current
    val name = LocalAppController.current.observe { it.homeBackground }
    val blur = LocalAppearanceExtras.current.bgBlur
    val key = "$name#$blur"
    val bitmap by produceState(HomeBackgroundCache.peek(key), key) {
        value = HomeBackgroundCache.peek(key) ?: if (name.isEmpty()) null else withContext(Dispatchers.IO) {
            HomeBackground.file(context, name)
                ?.let { HomeBackground.decode(it) }
                ?.let { if (blur > 0) HomeBackground.blurred(it, blur).also { _ -> it.recycle() } else it }
                ?.asImageBitmap()
        }?.also {
            HomeBackgroundCache.name = key
            HomeBackgroundCache.bitmap = it
        }
    }
    return bitmap
}

@Composable
internal fun HomeBackgroundImage(
    bitmap: ImageBitmap?,
    dimPercent: Int,
    modifier: Modifier = Modifier,
    veil: Color = MiuixTheme.colorScheme.surface,
) {
    Crossfade(targetState = bitmap, modifier = modifier, animationSpec = tween(300), label = "homeBg") { image ->
        if (image != null) {
            Box(Modifier.fillMaxSize()) {
                Image(
                    bitmap = image,
                    contentDescription = null,
                    modifier = Modifier.fillMaxSize(),
                    contentScale = ContentScale.Crop
                )
                if (dimPercent > 0) {
                    Box(Modifier.fillMaxSize().background(veil.copy(alpha = dimPercent / 100f)))
                }
            }
        }
    }
}

/** Page background assembled from the current settings; null when there is none. */
@Composable
fun rememberPageBackground(): com.zeroui.kit.glass.PageBackground? {
    val bitmap = rememberHomeBackground() ?: return null
    val dim = LocalAppController.current.observe { it.homeBackgroundDim }
    val cards = LocalAppearanceExtras.current.bgCards
    return androidx.compose.runtime.remember(bitmap, dim, cards) {
        com.zeroui.kit.glass.PageBackground(bitmap, dim, cards)
    }
}
