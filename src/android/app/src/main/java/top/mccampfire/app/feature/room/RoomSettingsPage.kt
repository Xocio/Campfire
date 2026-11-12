package top.mccampfire.app.feature.room

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.components.AccentBanner
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.DropdownPreference
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TextField
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.data.LinkPrefs
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.room.RoomInfo

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** Name, notice and player limit of the rooms this device hosts. */
@Composable
fun RoomSettingsPage(pad: PaddingValues, toast: (String) -> Unit) {
    val link = (LocalContext.current.applicationContext as CampfireApp).container.link
    val state by link.state.collectAsStateWithLifecycle()
    val prefs = LocalAppController.current.observe { it.link }
    var name by rememberSaveable { mutableStateOf(prefs.roomName) }
    var notice by rememberSaveable { mutableStateOf(prefs.roomNotice) }
    var limitIndex by rememberSaveable { mutableIntStateOf(LinkPrefs.MaxPlayerChoices.indexOf(prefs.maxPlayers).coerceAtLeast(0)) }
    val hosting = state.isActive && state.role == LinkRole.Host
    val changed = name.trim() != prefs.roomName || notice.trim() != prefs.roomNotice ||
        LinkPrefs.MaxPlayerChoices[limitIndex] != prefs.maxPlayers
    val savedText = stringResource(R.string.room_settings_saved)
    val limitLabels = LinkPrefs.MaxPlayerChoices.map { limit ->
        if (limit == 0) stringResource(R.string.room_settings_unlimited) else stringResource(R.string.room_settings_limit, limit)
    }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item { SectionTitle(stringResource(R.string.room_settings_header)) }
        item {
            Card(modifier = CardModifier, contentPadding = PaddingValues(16.dp)) {
                Column {
                    FieldTitle(stringResource(R.string.room_settings_name))
                    TextField(
                        value = name,
                        onValueChange = { if (it.length <= RoomInfo.MaxName) name = it },
                        label = link.defaultRoomName(),
                        useLabelAsPlaceholder = true,
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Spacer(Modifier.height(18.dp))
                    FieldTitle(stringResource(R.string.room_settings_notice))
                    TextField(
                        value = notice,
                        onValueChange = { if (it.length <= RoomInfo.MaxNotice) notice = it },
                        label = stringResource(R.string.room_settings_notice_hint),
                        useLabelAsPlaceholder = true,
                        minLines = 3,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }
        item {
            ListCard(CardModifier) {
                DropdownPreference(
                    items = limitLabels,
                    selectedIndex = limitIndex,
                    onSelectedIndexChange = { limitIndex = it },
                    title = stringResource(R.string.room_settings_max),
                )
            }
        }
        item {
            AccentBanner(
                text = stringResource(if (hosting) R.string.room_settings_note else R.string.room_settings_note_idle),
                modifier = CardModifier,
            )
        }
        item {
            TextButton(
                text = stringResource(R.string.link_save),
                enabled = changed,
                onClick = {
                    link.saveRoom(name, notice, LinkPrefs.MaxPlayerChoices[limitIndex])
                    toast(savedText)
                },
                variant = ButtonVariant.Primary,
                modifier = CardModifier.fillMaxWidth(),
            )
        }
    }
}

/** Title above a field inside a card. */
@Composable
private fun FieldTitle(text: String) {
    Text(
        text = text,
        modifier = Modifier.padding(start = 4.dp, bottom = 8.dp),
        fontSize = 16.sp,
        fontWeight = FontWeight.Medium,
        color = ZeroTheme.colors.content,
    )
}
