package top.mccampfire.app.feature.home

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.Text
import top.mccampfire.app.R
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.app.feature.settings.okAccentColor
import top.mccampfire.app.feature.settings.okContainerColor

private val MiniShape = RoundedCornerShape(6.dp)
private val MiniGap = 5.dp

/** Home page thumbnail for the theme preview. */
@Composable
internal fun HomeMiniPreview(cardColor: Color, textColor: Color, modifier: Modifier = Modifier) {
    val cards = LocalAppController.current.observe { it.visibleHomeCards() }
    val label = textColor.copy(alpha = 0.18f)
    val value = textColor.copy(alpha = 0.45f)

    Column(modifier = modifier) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(44.dp)
                .padding(start = 10.dp, end = 10.dp, top = 20.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(R.string.app_name),
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold,
                color = textColor,
            )
            Spacer(Modifier.weight(1f))
            Box(
                modifier = Modifier
                    .padding(start = 5.dp)
                    .size(7.dp)
                    .clip(CircleShape)
                    .background(textColor.copy(alpha = 0.55f)),
            )
        }
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 8.dp),
            verticalArrangement = Arrangement.spacedBy(MiniGap),
        ) {
            cards.forEach { card ->
                when (card) {
                    HomeCard.Status -> MiniStatus(cardColor, textColor, label, value)
                    HomeCard.Actions, HomeCard.Guide -> MiniRow(cardColor, label)
                }
            }
        }
    }
}

@Composable
private fun MiniBar(color: Color, width: Dp, modifier: Modifier = Modifier, height: Dp = 3.dp) {
    Box(
        modifier = modifier
            .width(width)
            .height(height)
            .clip(RoundedCornerShape(50))
            .background(color),
    )
}

@Composable
private fun MiniStatus(cardColor: Color, textColor: Color, label: Color, value: Color) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(48.dp),
        horizontalArrangement = Arrangement.spacedBy(MiniGap),
    ) {
        Box(
            modifier = Modifier
                .weight(1f)
                .fillMaxHeight()
                .clip(MiniShape)
                .background(okContainerColor),
        ) {
            MiniBar(textColor.copy(alpha = 0.3f), 20.dp, Modifier.padding(5.dp).align(Alignment.TopStart))
            Icon(
                painter = painterResource(R.drawable.ic_check_circle),
                contentDescription = null,
                tint = okAccentColor,
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .offset(9.dp, 9.dp)
                    .size(32.dp),
            )
        }
        Column(
            modifier = Modifier
                .weight(1f)
                .fillMaxHeight(),
            verticalArrangement = Arrangement.spacedBy(MiniGap),
        ) {
            repeat(2) {
                Column(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxWidth()
                        .clip(MiniShape)
                        .background(cardColor)
                        .padding(5.dp),
                    verticalArrangement = Arrangement.spacedBy(3.dp),
                ) {
                    MiniBar(label, 16.dp)
                    MiniBar(value, 24.dp, height = 4.dp)
                }
            }
        }
    }
}

@Composable
private fun MiniRow(cardColor: Color, label: Color) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(26.dp)
            .clip(MiniShape)
            .background(cardColor)
            .padding(5.dp),
    ) {
        MiniBar(label, 22.dp, Modifier.align(Alignment.TopStart))
        MiniBar(label.copy(alpha = label.alpha * 0.6f), 46.dp, Modifier.align(Alignment.BottomStart))
    }
}
