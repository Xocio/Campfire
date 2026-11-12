package top.mccampfire.app.feature.linksettings

import com.zeroui.kit.components.AccentBanner
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ConfirmDialog
import com.zeroui.kit.components.DialogButtons
import com.zeroui.kit.components.DropdownPreference
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ManagedDialog
import com.zeroui.kit.components.PreferenceIcon
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.SettingIcons
import com.zeroui.kit.components.SwitchPreference
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextField
import com.zeroui.kit.components.ZeroPageFrame
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.data.LinkPrefs
import top.mccampfire.app.data.effectivePlayerName
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.navigation.AppRoute

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** Icons of the multiplayer settings rows. */
internal object LinkIcons {
    val playerName = SettingIcons.contributors
    val gameName = SettingIcons.navLabels
    val relay = SettingIcons.share
    val hideAddress = SettingIcons.hideLauncher
    val timeout = SettingIcons.refreshRate
    val clipboard = SettingIcons.importData
    val shareVersion = SettingIcons.about
    val skins = SettingIcons.homeBackground
    val resetId = SettingIcons.reset
    val nodes = SettingIcons.layers
    val log = SettingIcons.appLog
    val room = SettingIcons.tune
    val allowlist = SettingIcons.navTabs
    val diagnostics = SettingIcons.refreshRate
    val games = SettingIcons.apps
}

/** Player limit and notice of the saved room settings. */
@Composable
private fun roomSettingsSummary(prefs: LinkPrefs): String {
    val limit = if (prefs.maxPlayers > 0) stringResource(R.string.room_settings_limit, prefs.maxPlayers) else stringResource(R.string.room_settings_unlimited)
    val name = prefs.roomName.takeIf { it.isNotBlank() }
    val notice = if (prefs.roomNotice.isNotBlank()) stringResource(R.string.room_settings_has_notice) else null
    return listOfNotNull(name, limit, notice).joinToString(" · ")
}

/** Settings tab with the multiplayer options. */
@Composable
fun LinkSettingsScreen(bottomPadding: Dp, onNavigate: (AppRoute) -> Unit) {
    val controller = LocalAppController.current
    val identity = (LocalContext.current.applicationContext as CampfireApp).container.identity
    val prefs = controller.settings.link
    var editing by rememberSaveable { mutableStateOf<NameField?>(null) }
    var confirmReset by remember { mutableStateOf(false) }
    var identitySerial by remember { mutableStateOf(0) }

    ZeroPageFrame(
        title = stringResource(R.string.link_settings_title),
        bottomInset = bottomPadding,
    ) { padding ->
        LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = padding) {
            item { SectionTitle(stringResource(R.string.link_section_player)) }
            item {
                val fallback = remember(identitySerial) { identity.fallbackName() }
                ListCard(CardModifier) {
                    ArrowPreference(
                        title = stringResource(R.string.link_player_name),
                        summary = prefs.playerName.ifBlank { fallback },
                        startAction = { PreferenceIcon(LinkIcons.playerName) },
                        onClick = { editing = NameField.Player },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.link_game_name),
                        summary = prefs.gameName.ifBlank { stringResource(R.string.link_game_name_empty) },
                        startAction = { PreferenceIcon(LinkIcons.gameName) },
                        onClick = { editing = NameField.Game },
                    )
                }
            }

            item { SectionTitle(stringResource(R.string.link_section_network)) }
            item {
                ListCard(CardModifier) {
                    SwitchPreference(
                        title = stringResource(R.string.link_allow_relay),
                        summary = stringResource(R.string.link_allow_relay_sub),
                        startAction = { PreferenceIcon(LinkIcons.relay) },
                        checked = prefs.allowRelay,
                        onCheckedChange = { value -> controller.setLink { it.copy(allowRelay = value) } },
                    )
                    SwitchPreference(
                        title = stringResource(R.string.link_hide_address),
                        summary = stringResource(R.string.link_hide_address_sub),
                        startAction = { PreferenceIcon(LinkIcons.hideAddress) },
                        checked = prefs.hideAddress,
                        onCheckedChange = { value -> controller.setLink { it.copy(hideAddress = value) } },
                    )
                    val choices = LinkPrefs.SearchTimeoutChoices
                    DropdownPreference(
                        title = stringResource(R.string.link_search_timeout),
                        summary = stringResource(R.string.link_search_timeout_sub),
                        startAction = { PreferenceIcon(LinkIcons.timeout) },
                        items = choices.map { stringResource(R.string.link_seconds, it) },
                        selectedIndex = choices.indexOf(prefs.searchTimeoutSeconds).coerceAtLeast(0),
                        onSelectedIndexChange = { index ->
                            controller.setLink { it.copy(searchTimeoutSeconds = choices[index]) }
                        },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.nodes_title),
                        summary = stringResource(R.string.nodes_sub),
                        startAction = { PreferenceIcon(LinkIcons.nodes) },
                        onClick = { onNavigate(AppRoute.Nodes) },
                    )
                    SwitchPreference(
                        title = stringResource(R.string.link_detect_clipboard),
                        summary = stringResource(R.string.link_detect_clipboard_sub),
                        startAction = { PreferenceIcon(LinkIcons.clipboard) },
                        checked = prefs.detectClipboard,
                        onCheckedChange = { value -> controller.setLink { it.copy(detectClipboard = value) } },
                    )
                }
            }

            item { SectionTitle(stringResource(R.string.link_section_privacy)) }
            item {
                ListCard(CardModifier) {
                    SwitchPreference(
                        title = stringResource(R.string.link_share_version),
                        summary = stringResource(R.string.link_share_version_sub),
                        startAction = { PreferenceIcon(LinkIcons.shareVersion) },
                        checked = prefs.shareVersion,
                        onCheckedChange = { value -> controller.setLink { it.copy(shareVersion = value) } },
                    )
                    SwitchPreference(
                        title = stringResource(R.string.link_show_skins),
                        summary = stringResource(R.string.link_show_skins_sub),
                        startAction = { PreferenceIcon(LinkIcons.skins) },
                        checked = prefs.showSkins,
                        onCheckedChange = { value -> controller.setLink { it.copy(showSkins = value) } },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.link_reset_id),
                        summary = stringResource(R.string.link_reset_id_sub),
                        startAction = { PreferenceIcon(LinkIcons.resetId) },
                        onClick = { confirmReset = true },
                    )
                }
            }

            item { SectionTitle(stringResource(R.string.games_section)) }
            item {
                ListCard(CardModifier) {
                    ArrowPreference(
                        title = stringResource(R.string.games_title),
                        summary = stringResource(R.string.games_sub),
                        startAction = { PreferenceIcon(LinkIcons.games) },
                        onClick = { onNavigate(AppRoute.GameFolders) },
                    )
                }
            }

            item { SectionTitle(stringResource(R.string.link_section_room)) }
            item {
                ListCard(CardModifier) {
                    ArrowPreference(
                        title = stringResource(R.string.room_settings),
                        summary = roomSettingsSummary(prefs),
                        startAction = { PreferenceIcon(LinkIcons.room) },
                        onClick = { onNavigate(AppRoute.RoomSettings) },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.allowlist),
                        summary = allowlistSummary(prefs.allowlistOnly, prefs.allowlist.size),
                        startAction = { PreferenceIcon(LinkIcons.allowlist) },
                        onClick = { onNavigate(AppRoute.Allowlist) },
                    )
                }
            }

            item { SectionTitle(stringResource(R.string.link_section_diagnostics)) }
            item {
                ListCard(CardModifier) {
                    ArrowPreference(
                        title = stringResource(R.string.diag_title),
                        summary = stringResource(R.string.diag_sub),
                        startAction = { PreferenceIcon(LinkIcons.diagnostics) },
                        onClick = { onNavigate(AppRoute.Diagnostics) },
                    )
                    ArrowPreference(
                        title = stringResource(R.string.link_log),
                        summary = stringResource(R.string.link_log_sub),
                        startAction = { PreferenceIcon(LinkIcons.log) },
                        onClick = { onNavigate(AppRoute.LinkLog) },
                    )
                }
            }
        }
    }

    when (editing) {
        NameField.Player -> NameDialog(
            title = stringResource(R.string.link_player_name),
            hint = stringResource(R.string.link_player_name_hint),
            note = stringResource(R.string.link_player_name_note, prefs.effectivePlayerName(identity)),
            initial = prefs.playerName,
            maxLength = LinkPrefs.MaxPlayerName,
            isValid = { true },
            onDismiss = { editing = null },
            onSave = { value -> controller.setLink { it.copy(playerName = value) } },
        )
        NameField.Game -> NameDialog(
            title = stringResource(R.string.link_game_name),
            hint = stringResource(R.string.link_game_name_hint),
            note = stringResource(R.string.link_game_name_note),
            initial = prefs.gameName,
            maxLength = LinkPrefs.MaxGameName,
            isValid = { it.isEmpty() || LinkPrefs.isValidGameName(it) },
            onDismiss = { editing = null },
            onSave = { value -> controller.setLink { it.copy(gameName = value) } },
        )
        null -> Unit
    }

    if (confirmReset) {
        ConfirmDialog(
            title = stringResource(R.string.link_reset_id),
            message = stringResource(R.string.link_reset_id_confirm),
            confirmText = stringResource(R.string.link_reset_id_action),
            onDismiss = { confirmReset = false },
            onConfirm = {
                confirmReset = false
                identity.resetMachineId()
                identitySerial++
            },
        )
    }
}

private enum class NameField { Player, Game }

@Composable
private fun NameDialog(
    title: String,
    hint: String,
    note: String,
    initial: String,
    maxLength: Int,
    isValid: (String) -> Boolean,
    onDismiss: () -> Unit,
    onSave: (String) -> Unit,
) {
    var text by rememberSaveable { mutableStateOf(initial) }
    val trimmed = text.trim()
    val valid = isValid(trimmed)
    ManagedDialog(onDismiss = onDismiss, title = title) {
        Column(modifier = Modifier.fillMaxWidth()) {
            TextField(
                value = text,
                onValueChange = { if (it.length <= maxLength) text = it },
                label = hint,
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(10.dp))
            AccentBanner(text = note, compact = true, danger = !valid)
            Spacer(Modifier.height(16.dp))
            DialogButtons(
                confirmText = stringResource(R.string.link_save),
                confirmEnabled = valid,
                onConfirm = { close { onSave(trimmed); onDismiss() } },
                onCancel = { close(onDismiss) },
            )
        }
    }
}
