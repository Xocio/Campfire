package top.mccampfire.app.feature.linksettings

import top.mccampfire.app.data.AllowedPlayer
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ConfirmDialog
import com.zeroui.kit.components.AccentBanner
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.SwitchPreference
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.feature.room.AllowlistConfirm
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** The allowlist switch and the players on it. */
@Composable
fun AllowlistPage(pad: PaddingValues) {
    val link = (LocalContext.current.applicationContext as CampfireApp).container.link
    val prefs = LocalAppController.current.observe { it.link }
    var confirming by remember { mutableStateOf(false) }
    var removing by remember { mutableStateOf<AllowedPlayer?>(null) }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item {
            ListCard(CardModifier) {
                SwitchPreference(
                    title = stringResource(R.string.allowlist),
                    summary = allowlistSummary(prefs.allowlistOnly, prefs.allowlist.size),
                    checked = prefs.allowlistOnly,
                    onCheckedChange = { on -> if (on) confirming = true else link.setAllowlistOnly(false) },
                )
            }
        }
        item { SectionTitle(stringResource(R.string.allowlist_players)) }
        if (prefs.allowlist.isEmpty()) {
            item {
                AccentBanner(text = stringResource(R.string.allowlist_empty), modifier = CardModifier)
            }
        } else {
            item {
                ListCard(CardModifier) {
                    prefs.allowlist.forEach { player ->
                        ArrowPreference(
                            title = player.name,
                            summary = "#" + player.id.take(4).uppercase(),
                            onClick = { removing = player },
                        )
                    }
                }
            }
        }
    }

    removing?.let { player ->
        ConfirmDialog(
            title = stringResource(R.string.allowlist_remove_player),
            message = stringResource(R.string.allowlist_remove_confirm, player.name),
            confirmText = stringResource(R.string.allowlist_remove),
            onDismiss = { removing = null },
            onConfirm = {
                removing = null
                link.disallow(player.id)
            },
        )
    }

    if (confirming) {
        AllowlistConfirm(
            onDismiss = { confirming = false },
            onConfirm = {
                confirming = false
                link.setAllowlistOnly(true)
            },
        )
    }
}

/** Summary of the allowlist state. */
@Composable
fun allowlistSummary(on: Boolean, count: Int): String =
    if (on) stringResource(R.string.allowlist_on, count) else stringResource(R.string.allowlist_off)
