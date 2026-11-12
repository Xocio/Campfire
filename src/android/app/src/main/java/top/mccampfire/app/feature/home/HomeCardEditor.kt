package top.mccampfire.app.feature.home

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.state.ToggleableState
import androidx.compose.ui.unit.dp
import top.mccampfire.app.R
import com.zeroui.kit.components.Checkbox

private val CornerOrigin = TransformOrigin(1f, 0f)

@Composable
fun HomeCardEditable(
    editing: Boolean,
    dragging: Boolean,
    checked: Boolean,
    onToggle: () -> Unit,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    val scale by animateFloatAsState(
        targetValue = if (dragging) 1.02f else 1f,
        animationSpec = spring(dampingRatio = 0.75f, stiffness = Spring.StiffnessMediumLow),
        label = "cardDragScale"
    )

    val dim by animateFloatAsState(
        targetValue = if (checked) 1f else 0.45f,
        animationSpec = tween(140),
        label = "cardEditDim"
    )

    val desc = stringResource(R.string.home_card_on_home)

    Box(modifier = modifier) {
        val needsLayer = scale != 1f || dim != 1f
        Box(
            modifier = if (needsLayer) {
                Modifier.graphicsLayer {
                    scaleX = scale
                    scaleY = scale
                    alpha = dim
                }
            } else {
                Modifier
            }
        ) {
            content()
        }

        if (editing) {
            Box(
                modifier = Modifier
                    .matchParentSize()
                    .pointerInput(Unit) { detectTapGestures { } }
            )
        }

        AnimatedVisibility(
            visible = editing,
            enter = fadeIn(tween(150)) + scaleIn(
                animationSpec = spring(dampingRatio = 0.6f, stiffness = Spring.StiffnessMedium),
                initialScale = 0.4f,
                transformOrigin = CornerOrigin
            ),
            exit = fadeOut(tween(120)) + scaleOut(
                animationSpec = tween(120),
                targetScale = 0.4f,
                transformOrigin = CornerOrigin
            ),
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(top = 6.dp, end = 6.dp)
        ) {
            Checkbox(
                state = if (checked) ToggleableState.On else ToggleableState.Off,
                onClick = onToggle,
                modifier = Modifier.semantics { contentDescription = desc }
            )
        }
    }
}
