package top.mccampfire.app.feature.lobby

import com.zeroui.kit.components.AccentBanner
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.ConfirmDialog
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TextField
import com.zeroui.kit.theme.ZeroTheme
import kotlinx.coroutines.launch
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.core.link.RoomCode
import top.mccampfire.core.lobby.LobbyError
import top.mccampfire.core.lobby.LobbyException

/** Details of one lobby room with the join and report actions. */
@Composable
fun LobbyRoomPage(pad: PaddingValues, roomId: String, onJoin: (RoomCode) -> Unit, toast: (String) -> Unit) {
    val context = LocalContext.current
    val repository = (context.applicationContext as CampfireApp).container.lobby
    val state by repository.state.collectAsStateWithLifecycle()
    val room = state.list?.rooms?.firstOrNull { it.id == roomId }
    val scope = rememberCoroutineScope()
    var password by rememberSaveable { mutableStateOf("") }
    var joining by remember { mutableStateOf(false) }
    var problem by remember { mutableStateOf<String?>(null) }
    var confirmReport by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(pad)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (room == null) {
            AccentBanner(text = stringResource(R.string.lobby_gone))
            return@Column
        }
        Card(contentPadding = PaddingValues(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                RoomIcon(room, 64.dp)
                Spacer(Modifier.width(14.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(text = room.title, fontSize = 19.sp, fontWeight = FontWeight.SemiBold)
                    Spacer(Modifier.height(6.dp))
                    RoomBadges(room)
                }
            }
            if (room.description.isNotBlank()) {
                Spacer(Modifier.height(12.dp))
                Text(text = room.description, fontSize = 14.sp, color = ZeroTheme.colors.contentMuted)
            }
        }
        ListCard {
            if (room.host.isNotBlank()) ListRow(title = stringResource(R.string.lobby_host_label), summary = room.host)
            if (room.map.isNotBlank()) ListRow(title = stringResource(R.string.lobby_map_label), summary = room.map)
            ListRow(title = stringResource(R.string.lobby_players_label), summary = playersText(room))
            val region = listOf(room.country, room.region).filter { it.isNotBlank() }.joinToString(" ")
            if (region.isNotBlank()) ListRow(title = stringResource(R.string.lobby_region_label), summary = region)
        }
        if (room.locked) {
            Card(contentPadding = PaddingValues(16.dp)) {
                TextField(
                    value = password,
                    onValueChange = { if (it.length <= 64) password = it },
                    label = stringResource(R.string.lobby_password_hint),
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
        problem?.let { AccentBanner(text = it, danger = true) }
        val passwordEmpty = stringResource(R.string.lobby_password_empty)
        val texts = LobbyTexts(
            gone = stringResource(R.string.lobby_gone),
            wrongPassword = stringResource(R.string.lobby_wrong_password),
            tooMany = stringResource(R.string.lobby_too_many),
            closed = stringResource(R.string.lobby_closed),
            network = stringResource(R.string.lobby_error),
        )
        TextButton(
            text = stringResource(R.string.lobby_join),
            enabled = !joining,
            variant = ButtonVariant.Primary,
            modifier = Modifier.fillMaxWidth(),
            onClick = {
                if (room.locked && password.isBlank()) {
                    problem = passwordEmpty
                    return@TextButton
                }
                joining = true
                problem = null
                scope.launch {
                    try {
                        onJoin(repository.join(room.id, password.takeIf { room.locked }))
                    } catch (e: LobbyException) {
                        problem = texts.of(e.kind)
                    } finally {
                        joining = false
                    }
                }
            },
        )
        ListCard {
            ArrowPreference(title = stringResource(R.string.lobby_report), onClick = { confirmReport = true })
        }
    }

    if (confirmReport && room != null) {
        val reported = stringResource(R.string.lobby_reported)
        val failed = stringResource(R.string.lobby_error)
        ConfirmDialog(
            title = stringResource(R.string.lobby_report_title),
            message = stringResource(R.string.lobby_report_message, room.title),
            confirmText = stringResource(R.string.lobby_report_confirm),
            onDismiss = { confirmReport = false },
            onConfirm = {
                confirmReport = false
                scope.launch { toast(if (runCatching { repository.report(room.id) }.isSuccess) reported else failed) }
            },
        )
    }
}

/** Messages of lobby failures in the current language. */
private class LobbyTexts(val gone: String, val wrongPassword: String, val tooMany: String, val closed: String, val network: String) {
    fun of(kind: LobbyError): String = when (kind) {
        LobbyError.NotFound -> gone
        LobbyError.WrongPassword -> wrongPassword
        LobbyError.TooMany -> tooMany
        LobbyError.Closed -> closed
        else -> network
    }
}
