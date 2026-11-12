package top.mccampfire.app.feature.support

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.IconHue
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.PreferenceIcon
import com.zeroui.kit.components.SettingIcon
import com.zeroui.kit.components.Text
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.R
import top.mccampfire.app.feature.update.openDownload
import top.yukonga.miuix.kmp.icon.MiuixIcons
import top.yukonga.miuix.kmp.icon.extended.Favorites
import top.yukonga.miuix.kmp.icon.extended.FavoritesFill
import top.yukonga.miuix.kmp.icon.extended.MindMap

/** Sponsorship links shown on the support page. */
object SupportLinks {
    const val Developer = "https://afdian.com/a/xocio"
    const val EasyTier = "https://github.com/EasyTier/EasyTier"
}

/** Icon of the support entry and the developer row. */
val SupportIcon = SettingIcon({ MiuixIcons.Favorites }, { MiuixIcons.FavoritesFill }, IconHue.Pink)
private val EasyTierIcon = SettingIcon({ MiuixIcons.MindMap }, { MiuixIcons.MindMap }, IconHue.Teal)

/** Support page with the Campfire and EasyTier sponsorship entries. */
@Composable
fun SupportPage(pad: PaddingValues) {
    val context = LocalContext.current
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = pad,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        item {
            Column(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 28.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Spacer(Modifier.height(24.dp))
                Box(
                    modifier = Modifier
                        .size(72.dp)
                        .background(IconHue.Pink.color.copy(alpha = 0.14f), CircleShape),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        imageVector = MiuixIcons.FavoritesFill,
                        contentDescription = null,
                        tint = IconHue.Pink.color,
                        modifier = Modifier.size(36.dp),
                    )
                }
                Spacer(Modifier.height(16.dp))
                Text(
                    text = stringResource(R.string.support_title),
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = ZeroTheme.colors.content,
                )
                Spacer(Modifier.height(8.dp))
                Text(
                    text = stringResource(R.string.support_intro),
                    fontSize = 14.sp,
                    color = ZeroTheme.colors.contentMuted,
                    textAlign = TextAlign.Center,
                )
                Spacer(Modifier.height(24.dp))
            }
        }
        item {
            ListCard(Modifier.padding(horizontal = 12.dp, vertical = 6.dp)) {
                ArrowPreference(
                    title = stringResource(R.string.support_developer),
                    startAction = { PreferenceIcon(SupportIcon) },
                    summary = stringResource(R.string.support_developer_sub),
                    onClick = { openDownload(context, SupportLinks.Developer) },
                )
                ArrowPreference(
                    title = stringResource(R.string.support_easytier),
                    startAction = { PreferenceIcon(EasyTierIcon) },
                    summary = stringResource(R.string.support_easytier_sub),
                    onClick = { openDownload(context, SupportLinks.EasyTier) },
                )
            }
        }
    }
}
