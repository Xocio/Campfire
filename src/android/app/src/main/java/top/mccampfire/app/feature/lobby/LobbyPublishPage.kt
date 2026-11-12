package top.mccampfire.app.feature.lobby

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.state.ToggleableState
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.components.AccentBanner
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.Checkbox
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.SwitchPreference
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TextField
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.app.lobby.PublishPhase
import top.mccampfire.app.lobby.PublishState
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.LinkStage
import top.mccampfire.core.lobby.LobbyError

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** Tags offered when the lobby list has not been loaded. */
private val KnownTags = listOf("survival", "creative", "building", "redstone", "minigame", "modded", "adventure", "pvp", "casual")

private const val MaxTags = 3
private const val MaxDescription = 100
private const val MaxPassword = 32
private const val ShownTitleLength = 24

/** Lists the open room in the public lobby, or takes it down. */
@Composable
fun LobbyPublishPage(pad: PaddingValues, onRoomSettings: () -> Unit) {
    val container = (LocalContext.current.applicationContext as CampfireApp).container
    val publisher = container.publisher
    val lobby = container.lobby
    val link = container.link
    val state by link.state.collectAsStateWithLifecycle()
    val publish by publisher.state.collectAsStateWithLifecycle()
    val lobbyState by lobby.state.collectAsStateWithLifecycle()
    val controller = LocalAppController.current
    val prefs = controller.observe { it.link }
    var description by rememberSaveable { mutableStateOf(prefs.lobbyDescription) }
    var password by rememberSaveable { mutableStateOf("") }

    LaunchedEffect(Unit) { lobby.refresh() }

    val hosting = state.role == LinkRole.Host && state.stage == LinkStage.Ready
    val editable = publish.phase == PublishPhase.Off || publish.phase == PublishPhase.Stopped
    val offered = (lobbyState.list?.tags?.takeIf { it.isNotEmpty() } ?: KnownTags)
    val roomName = state.room?.name?.ifBlank { null } ?: link.defaultRoomName()
    val errorText = publishErrorText(publish)

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item {
            ListCard(CardModifier) {
                ListRow(title = stringResource(R.string.lobby_publish), summary = publishSummary(publish))
            }
        }
        errorText?.let { message ->
            item { AccentBanner(text = message, modifier = CardModifier) }
        }
        item {
            ListCard(CardModifier) {
                ArrowPreference(
                    title = stringResource(R.string.room_settings_name),
                    summary = roomName + "\n" + stringResource(R.string.lobby_publish_name_note, ShownTitleLength),
                    onClick = onRoomSettings,
                )
            }
        }
        item {
            Card(modifier = CardModifier, contentPadding = PaddingValues(16.dp)) {
                Column {
                    FieldTitle(stringResource(R.string.lobby_publish_description))
                    TextField(
                        value = description,
                        onValueChange = { if (it.length <= MaxDescription) description = it },
                        label = stringResource(R.string.lobby_publish_description_hint),
                        useLabelAsPlaceholder = true,
                        enabled = editable,
                        minLines = 2,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Spacer(Modifier.height(18.dp))
                    FieldTitle(stringResource(R.string.lobby_publish_password))
                    TextField(
                        value = password,
                        onValueChange = { if (it.length <= MaxPassword) password = it },
                        label = stringResource(R.string.lobby_publish_password_hint),
                        useLabelAsPlaceholder = true,
                        enabled = editable,
                        singleLine = true,
                        visualTransformation = PasswordVisualTransformation(),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }
        item { SectionTitle(stringResource(R.string.lobby_publish_tags) + " · " + stringResource(R.string.lobby_publish_tags_sub, MaxTags)) }
        item {
            ListCard(CardModifier) {
                offered.forEach { id ->
                    val name = tagName(id) ?: return@forEach
                    val chosen = id in prefs.lobbyTags
                    val allowed = editable && (chosen || prefs.lobbyTags.size < MaxTags)
                    val toggle = {
                        controller.setLink { link ->
                            val tags = if (id in link.lobbyTags) link.lobbyTags - id else (link.lobbyTags + id).take(MaxTags)
                            link.copy(lobbyTags = tags)
                        }
                    }
                    ListRow(
                        title = name,
                        enabled = allowed,
                        onClick = if (allowed) toggle else null,
                        endActions = {
                            Checkbox(
                                state = if (chosen) ToggleableState.On else ToggleableState.Off,
                                onClick = if (allowed) toggle else null,
                                enabled = allowed,
                            )
                        },
                    )
                }
            }
        }
        item {
            ListCard(CardModifier) {
                SwitchPreference(
                    title = stringResource(R.string.lobby_publish_region),
                    summary = stringResource(R.string.lobby_publish_region_sub),
                    checked = prefs.lobbyShowRegion,
                    enabled = editable,
                    onCheckedChange = { value -> controller.setLink { it.copy(lobbyShowRegion = value) } },
                )
            }
        }
        item {
            AccentBanner(
                text = stringResource(if (hosting) R.string.lobby_publish_note else R.string.lobby_publish_need_room),
                modifier = CardModifier,
            )
        }
        item {
            if (editable) {
                TextButton(
                    text = stringResource(R.string.lobby_publish_action),
                    enabled = hosting,
                    onClick = {
                        controller.setLink { it.copy(lobbyDescription = description.trim()) }
                        publisher.publish(password)
                    },
                    variant = ButtonVariant.Primary,
                    modifier = CardModifier.fillMaxWidth(),
                )
            } else {
                TextButton(
                    text = stringResource(R.string.lobby_unpublish_action),
                    onClick = publisher::unpublish,
                    modifier = CardModifier.fillMaxWidth(),
                )
            }
        }
    }
}

/** One line about the lobby listing of the open room. */
@Composable
internal fun publishSummary(publish: PublishState): String = when (publish.phase) {
    PublishPhase.Off -> stringResource(R.string.lobby_publish_off)
    PublishPhase.Publishing -> stringResource(R.string.lobby_publish_publishing)
    PublishPhase.Listed -> stringResource(R.string.lobby_publish_listed) + " · " + publish.title
    PublishPhase.Stopped -> stringResource(R.string.lobby_publish_stopped)
}

/** What went wrong with the listing, or null when nothing did. */
@Composable
internal fun publishErrorText(publish: PublishState): String? = when (publish.error) {
    null -> null
    LobbyError.Network, LobbyError.Unexpected -> stringResource(R.string.lobby_publish_retrying)
    LobbyError.TooMany -> stringResource(R.string.lobby_too_many)
    LobbyError.Blocked -> stringResource(if (publish.errorField == "description") R.string.lobby_publish_err_description else R.string.lobby_publish_err_title)
    LobbyError.Closed -> stringResource(R.string.lobby_publish_err_closed)
    LobbyError.Full -> stringResource(R.string.lobby_publish_err_full)
    LobbyError.UpdateRequired -> stringResource(R.string.lobby_publish_err_update)
    LobbyError.Banned -> stringResource(R.string.lobby_publish_err_banned)
    LobbyError.AlreadyListed -> stringResource(R.string.lobby_publish_err_already)
    LobbyError.Removed -> stringResource(R.string.lobby_publish_err_removed)
    LobbyError.Reported -> stringResource(R.string.lobby_publish_err_reported)
    LobbyError.NotFound, LobbyError.WrongPassword -> stringResource(R.string.lobby_publish_err_other)
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
