package top.mccampfire.app.feature.linksettings

import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.AccentBanner
import android.content.Intent
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.theme.ZeroTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R

private const val MaxShown = 64 * 1024

/** The newest multiplayer log with a share action. */
@Composable
fun LinkLogPage(pad: PaddingValues) {
    val context = LocalContext.current
    val log = (context.applicationContext as CampfireApp).container.link.log
    val text by produceState<String?>(initialValue = null) {
        value = withContext(Dispatchers.IO) {
            log.files().firstOrNull()?.let { file -> runCatching { file.readText().takeLast(MaxShown) }.getOrNull() }.orEmpty()
        }
    }
    val shareTitle = stringResource(R.string.link_log_share)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(pad)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        val current = text
        if (current != null && current.isEmpty()) {
            AccentBanner(text = stringResource(R.string.link_log_empty))
            return@Column
        }
        ListCard {
            ArrowPreference(
                title = shareTitle,
                enabled = !current.isNullOrEmpty(),
                onClick = {
                    val send = Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, current)
                    context.startActivity(Intent.createChooser(send, shareTitle))
                },
            )
        }
        Card(modifier = Modifier.weight(1f), contentPadding = PaddingValues(12.dp)) {
            Text(
                text = current.orEmpty(),
                modifier = Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .horizontalScroll(rememberScrollState()),
                fontSize = 11.sp,
                style = ZeroTheme.typography.caption.copy(fontFamily = FontFamily.Monospace),
                softWrap = false,
            )
        }
    }
}
