package top.mccampfire.app.feature.room

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.components.AccentBanner
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.Badge
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.ConfirmDialog
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.LoadingIndicator
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.SwitchPreference
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.feature.linksettings.allowlistSummary
import top.mccampfire.app.feature.lobby.publishSummary
import top.mccampfire.app.link.playerHeadSlot
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.core.link.room.RoomInfo
import top.mccampfire.core.link.LinkError
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.runtime.NatKind
import com.zeroui.kit.components.ManagedBottomSheet
import top.mccampfire.core.link.LinkSession
import top.mccampfire.core.link.LinkStage
import top.mccampfire.core.link.LinkState
import top.mccampfire.core.link.LinkStep
import top.mccampfire.core.link.room.PlayerKind
import top.mccampfire.core.link.room.PlayerProfile

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** Page of the current room: setup progress, the joined room or the failure. */
@Composable
fun RoomPage(
    pad: PaddingValues,
    onBack: () -> Unit,
    onOpenLog: () -> Unit,
    onRoomSettings: () -> Unit,
    onLobbyPublish: () -> Unit,
    onDiagnostics: () -> Unit,
    onGameFolders: () -> Unit,
    toast: (String) -> Unit,
) {
    val context = LocalContext.current
    val container = (context.applicationContext as CampfireApp).container
    val link = container.link
    val publisher = container.publisher
    val state by link.state.collectAsStateWithLifecycle()
    val publish by publisher.state.collectAsStateWithLifecycle()
    var confirmLeave by remember { mutableStateOf(false) }
    var removing by remember { mutableStateOf<PlayerProfile?>(null) }
    var confirmAllowlist by remember { mutableStateOf(false) }
    val prefs = LocalAppController.current.observe { it.link }
    val shareTitle = stringResource(R.string.room_share_code)
    val copiedText = stringResource(R.string.room_copied)
    val shareText = stringResource(R.string.room_share_text, state.code?.code.orEmpty())

    LaunchedEffect(state.stage) {
        if (state.stage == LinkStage.Idle) onBack()
    }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        when (state.stage) {
            LinkStage.Working -> progress(state, onCancel = link::stop)
            LinkStage.Ready -> if (state.role == LinkRole.Host) {
                hosted(
                    state = state,
                    onCopy = { text ->
                        context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Campfire", text))
                        toast(copiedText)
                    },
                    onShare = {
                        val send = Intent(Intent.ACTION_SEND)
                            .setType("text/plain")
                            .putExtra(Intent.EXTRA_TEXT, shareText)
                        context.startActivity(Intent.createChooser(send, shareTitle))
                    },
                    onPlayer = { removing = it },
                    onClose = { confirmLeave = true },
                    showSkins = prefs.showSkins,
                    tools = {
                        ArrowPreference(
                            title = stringResource(R.string.room_settings),
                            summary = roomSummary(state.room),
                            onClick = onRoomSettings,
                        )
                        if (publisher.available) {
                            ArrowPreference(
                                title = stringResource(R.string.lobby_publish),
                                summary = publishSummary(publish),
                                onClick = onLobbyPublish,
                            )
                        }
                        SwitchPreference(
                            title = stringResource(R.string.allowlist),
                            summary = allowlistSummary(prefs.allowlistOnly, prefs.allowlist.size),
                            checked = prefs.allowlistOnly,
                            onCheckedChange = { on -> if (on) confirmAllowlist = true else link.setAllowlistOnly(false) },
                        )
                        VoiceRow(onFolders = onGameFolders)
                        ArrowPreference(
                            title = stringResource(R.string.diag_title),
                            summary = stringResource(R.string.room_connections, state.directCount, state.relayCount) + " · " + nodesText(state.connectedNodes),
                            onClick = onDiagnostics,
                        )
                    },
                )
            } else joined(
                state = state,
                onCopy = { text ->
                    context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Campfire", text))
                    toast(copiedText)
                },
                onLeave = { confirmLeave = true },
                showSkins = prefs.showSkins,
                tools = {
                    VoiceRow(onFolders = onGameFolders)
                    ArrowPreference(
                        title = stringResource(R.string.diag_title),
                        summary = stringResource(if (state.hostPeer?.isDirect == true) R.string.diag_host_direct else R.string.diag_host_relayed) +
                            " · " + nodesText(state.connectedNodes),
                        onClick = onDiagnostics,
                    )
                },
            )
            LinkStage.Failed -> failure(
                state = state,
                onRetry = { state.code?.let(link::join) },
                onBack = {
                    link.dismissFailure()
                    onBack()
                },
                onOpenLog = onOpenLog,
            )
            LinkStage.Idle -> Unit
        }
    }

    removing?.let { player ->
        val allowed = prefs.allowlist.any { it.id == player.machineId }
        RemoveSheet(
            player = player,
            allowed = allowed,
            onDismiss = { removing = null },
            onRemove = { duration -> link.removePlayer(player.machineId, duration) },
            onToggleAllow = { if (allowed) link.disallow(player.machineId) else link.allow(player) },
        )
    }

    if (confirmAllowlist) {
        AllowlistConfirm(
            onDismiss = { confirmAllowlist = false },
            onConfirm = {
                confirmAllowlist = false
                link.setAllowlistOnly(true)
            },
        )
    }

    if (confirmLeave) {
        val host = state.role == LinkRole.Host
        ConfirmDialog(
            title = stringResource(if (host) R.string.room_close else R.string.room_leave),
            message = stringResource(if (host) R.string.room_close_confirm else R.string.room_leave_confirm),
            confirmText = stringResource(if (host) R.string.room_close else R.string.room_leave),
            onDismiss = { confirmLeave = false },
            onConfirm = {
                confirmLeave = false
                link.stop()
            },
        )
    }
}

private fun LazyListScope.progress(state: LinkState, onCancel: () -> Unit) {
    item {
        ListCard(CardModifier) {
            val steps = if (state.role == LinkRole.Host) {
                listOf(LinkStep.World to R.string.room_step_world, LinkStep.Network to R.string.room_step_network)
            } else {
                listOf(LinkStep.Network to R.string.room_step_network, LinkStep.FindHost to R.string.room_step_find_host, LinkStep.Enter to R.string.room_step_enter)
            }
            val current = steps.indexOfFirst { it.first == state.step }
            steps.forEachIndexed { index, (step, label) ->
                ListRow(
                    title = stringResource(label),
                    summary = when {
                        index != current -> null
                        step == LinkStep.FindHost -> findHostSummary(state)
                        step == LinkStep.World && state.gamePort > 0 -> stringResource(R.string.room_world_port_waiting, state.gamePort)
                        step == LinkStep.World -> stringResource(R.string.room_world_waiting)
                        else -> null
                    },
                    endActions = { StepMark(done = index < current, active = index == current) },
                )
            }
        }
    }
    item {
        ListCard(CardModifier) { ArrowPreference(title = stringResource(R.string.room_cancel), onClick = onCancel) }
    }
}

@Composable
private fun findHostSummary(state: LinkState): String {
    val seconds = stringResource(R.string.room_searching, (state.searchMillis / 1000).toInt())
    return if (state.connectedNodes >= 0) seconds + " · " + stringResource(R.string.room_nodes, state.connectedNodes) else seconds
}

@Composable
private fun StepMark(done: Boolean, active: Boolean) {
    when {
        active -> LoadingIndicator(size = 20.dp)
        done -> Icon(
            painter = painterResource(R.drawable.ic_check_circle),
            contentDescription = null,
            tint = ZeroTheme.colors.success,
            modifier = Modifier.size(22.dp),
        )
        else -> Spacer(Modifier.size(22.dp))
    }
}

private fun LazyListScope.joined(
    state: LinkState,
    onCopy: (String) -> Unit,
    onLeave: () -> Unit,
    showSkins: Boolean,
    tools: @Composable ColumnScope.() -> Unit,
) {
    val host = state.players.firstOrNull { it.kind == PlayerKind.Host }
    item {
        Card(modifier = CardModifier.fillMaxWidth(), contentPadding = PaddingValues(horizontal = 20.dp, vertical = 18.dp)) {
            Text(
                text = state.room?.name?.ifBlank { null } ?: host?.name ?: state.code?.code.orEmpty(),
                fontSize = 20.sp,
                fontWeight = FontWeight.SemiBold,
            )
            Spacer(Modifier.height(4.dp))
            Text(text = state.code?.code.orEmpty(), fontSize = 14.sp, color = ZeroTheme.colors.contentMuted)
            state.room?.notice?.takeIf { it.isNotBlank() }?.let { notice ->
                Spacer(Modifier.height(12.dp))
                AccentBanner(text = notice, compact = true)
            }
        }
    }
    item {
        val address = "127.0.0.1:${state.gamePort}"
        ListCard(CardModifier) {
            ArrowPreference(
                title = stringResource(R.string.room_local_address),
                summary = address,
                onClick = { onCopy(address) },
            )
            state.hostPeer?.let { peer ->
                ListRow(
                    title = stringResource(R.string.room_connection),
                    summary = connectionText(peer.isDirect, peer.latencyMs, peer.lossPercent),
                )
            }
        }
    }
    item {
        AccentBanner(
            text = stringResource(R.string.room_enter_hint) + "\n\n" + stringResource(R.string.room_direct_hint),
            modifier = CardModifier,
        )
    }
    item { SectionTitle(stringResource(R.string.room_players, state.players.size)) }
    item {
        ListCard(CardModifier) {
            state.players.forEach { player -> PlayerRow(player, showSkins) }
        }
    }
    item { SectionTitle(stringResource(R.string.room_tools)) }
    item { ListCard(CardModifier, content = tools) }
    item {
        ListCard(CardModifier) { ArrowPreference(title = stringResource(R.string.room_leave), onClick = onLeave) }
    }
}

@Composable
private fun PlayerRow(player: PlayerProfile, showSkins: Boolean) {
    ListRow(
        title = player.name,
        summary = playerSummary(player),
        startAction = playerHeadSlot(player, showSkins),
        endActions = {
            when (player.kind) {
                PlayerKind.Host -> Badge(stringResource(R.string.room_host))
                PlayerKind.Local -> Badge(stringResource(R.string.room_you), accent = false)
                PlayerKind.Guest -> Unit
            }
        },
    )
}

@Composable
private fun connectionText(direct: Boolean, latency: Double?, loss: Double?): String {
    val parts = mutableListOf(stringResource(if (direct) R.string.room_path_direct else R.string.room_path_relay))
    latency?.let { parts += stringResource(R.string.room_latency, LinkSession.formatLatency(it)) }
    loss?.takeIf { it > 0 }?.let { parts += stringResource(R.string.room_loss, LinkSession.formatLatency(it)) }
    return parts.joinToString(" · ")
}

private fun LazyListScope.failure(state: LinkState, onRetry: () -> Unit, onBack: () -> Unit, onOpenLog: () -> Unit) {
    item {
        Card(modifier = CardModifier.fillMaxWidth(), contentPadding = PaddingValues(horizontal = 20.dp, vertical = 18.dp)) {
            Text(
                text = stringResource(R.string.room_failed),
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                color = ZeroTheme.colors.danger,
            )
            Spacer(Modifier.height(6.dp))
            val (title, advice) = errorTexts(state.error ?: LinkError.Unexpected, state.errorDetail)
            Text(text = title, fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
            if (advice != null) {
                Spacer(Modifier.height(8.dp))
                Text(text = advice, fontSize = 14.sp, color = ZeroTheme.colors.contentMuted)
            }
        }
    }
    item {
        TextButton(
            text = stringResource(R.string.room_retry),
            onClick = onRetry,
            variant = ButtonVariant.Primary,
            modifier = CardModifier.fillMaxWidth(),
        )
    }
    item {
        ListCard(CardModifier) {
            ArrowPreference(title = stringResource(R.string.link_open_log), onClick = onOpenLog)
            ArrowPreference(title = stringResource(R.string.room_back), onClick = onBack)
        }
    }
}

/** Title and advice of a session failure. */
@Composable
internal fun errorTexts(error: LinkError, detail: String?): Pair<String, String?> = when (error) {
    LinkError.NetworkStartFailed -> stringResource(R.string.link_error_network_start_failed) to stringResource(R.string.link_error_network_start_failed_advice)
    LinkError.NetworkCrashed -> stringResource(R.string.link_error_network_crashed) to stringResource(R.string.link_error_network_crashed_advice)
    LinkError.NoNodes -> stringResource(R.string.link_error_no_nodes) to stringResource(R.string.link_error_no_nodes_advice)
    LinkError.RoomNotFound -> stringResource(R.string.link_error_room_not_found) to stringResource(R.string.link_error_room_not_found_advice)
    LinkError.NoDirectPath -> stringResource(R.string.link_error_no_direct_path) to stringResource(R.string.link_error_no_direct_path_advice)
    LinkError.HostNoAnswer -> stringResource(R.string.link_error_host_no_answer) to stringResource(R.string.link_error_host_no_answer_advice)
    LinkError.HostLost -> stringResource(R.string.link_error_host_lost) to stringResource(R.string.link_error_host_lost_advice)
    LinkError.ForwardFailed -> stringResource(R.string.link_error_forward_failed) to stringResource(R.string.link_error_forward_failed_advice)
    LinkError.LocalPortBusy -> stringResource(R.string.link_error_local_port_busy, detail.orEmpty()) to stringResource(R.string.link_error_local_port_busy_advice)
    LinkError.Removed -> stringResource(R.string.link_error_removed) to stringResource(R.string.link_error_removed_advice)
    LinkError.Blocked -> stringResource(R.string.link_error_blocked) to stringResource(R.string.link_error_blocked_advice)
    LinkError.RoomFull -> stringResource(R.string.link_error_room_full) to stringResource(R.string.link_error_room_full_advice)
    LinkError.NotAllowed -> stringResource(R.string.link_error_not_allowed) to stringResource(R.string.link_error_not_allowed_advice)
    LinkError.PortNotGame -> stringResource(R.string.link_error_port_not_game, detail.orEmpty()) to stringResource(R.string.link_error_port_not_game_advice)
    LinkError.WorldClosed -> stringResource(R.string.link_error_world_closed) to null
    LinkError.Unexpected ->
        stringResource(R.string.link_error_unexpected, detail ?: error.name) to null
}

private fun LazyListScope.hosted(
    state: LinkState,
    onCopy: (String) -> Unit,
    onShare: (String) -> Unit,
    onPlayer: (PlayerProfile) -> Unit,
    onClose: () -> Unit,
    showSkins: Boolean,
    tools: @Composable ColumnScope.() -> Unit,
) {
    val code = state.code?.code.orEmpty()
    item {
        Card(modifier = CardModifier.fillMaxWidth(), contentPadding = PaddingValues(horizontal = 20.dp, vertical = 18.dp)) {
            Text(text = stringResource(R.string.room_code_label), fontSize = 14.sp, color = ZeroTheme.colors.contentMuted)
            Spacer(Modifier.height(4.dp))
            Text(text = code, fontSize = 24.sp, fontWeight = FontWeight.SemiBold, color = ZeroTheme.colors.accent)
            Spacer(Modifier.height(14.dp))
            TextButton(
                text = stringResource(R.string.room_copy_code),
                onClick = { onCopy(code) },
                variant = ButtonVariant.Primary,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(12.dp))
            Text(
                text = stringResource(if (state.waitingForWorld) R.string.room_world_silent else R.string.room_host_hint),
                fontSize = 13.sp,
                color = if (state.waitingForWorld) ZeroTheme.colors.danger else ZeroTheme.colors.contentMuted,
            )
        }
    }
    item {
        ListCard(CardModifier) {
            ArrowPreference(title = stringResource(R.string.room_share_row), onClick = { onShare(code) })
            ListRow(title = stringResource(R.string.room_game_port), summary = state.gamePort.toString())
            ListRow(title = stringResource(R.string.room_connection), summary = stringResource(R.string.room_connections, state.directCount, state.relayCount))
            ListRow(title = stringResource(R.string.room_nat), summary = natText(state.localNat))
        }
    }
    item { SectionTitle(stringResource(R.string.room_players, state.players.size)) }
    item {
        ListCard(CardModifier) {
            state.players.forEach { player ->
                if (player.kind == PlayerKind.Host) {
                    PlayerRow(player, showSkins)
                } else {
                    ArrowPreference(
                        title = player.name,
                        summary = playerSummary(player),
                        startAction = playerHeadSlot(player, showSkins),
                        onClick = { onPlayer(player) },
                    )
                }
            }
        }
    }
    item { SectionTitle(stringResource(R.string.room_tools)) }
    item { ListCard(CardModifier, content = tools) }
    item {
        ListCard(CardModifier) { ArrowPreference(title = stringResource(R.string.room_close), onClick = onClose) }
    }
}

@Composable
private fun playerSummary(player: PlayerProfile): String? {
    val vendor = player.vendor.substringBefore(',').trim()
    val connection = player.path?.let { connectionText(it == PlayerProfile.PathDirect, player.latencyMs, player.lossPercent) }
    return listOfNotNull(vendor.ifBlank { null }, connection).joinToString(" · ").ifBlank { null }
}

@Composable
private fun natText(nat: NatKind): String = stringResource(
    when (nat) {
        NatKind.Unknown -> R.string.nat_unknown
        NatKind.Open -> R.string.nat_open
        NatKind.FullCone -> R.string.nat_full_cone
        NatKind.Restricted -> R.string.nat_restricted
        NatKind.PortRestricted -> R.string.nat_port_restricted
        NatKind.Symmetric -> R.string.nat_symmetric
    },
)

/** Ways to take a guest out of the room. */
@Composable
private fun RemoveSheet(
    player: PlayerProfile,
    allowed: Boolean,
    onDismiss: () -> Unit,
    onRemove: (Long?) -> Unit,
    onToggleAllow: () -> Unit,
) {
    val choices = listOf(
        R.string.room_remove_1m to 60_000L,
        R.string.room_remove_10m to 600_000L,
        R.string.room_remove_1h to 3_600_000L,
        R.string.room_remove_room to null,
    )
    ManagedBottomSheet(onDismiss = onDismiss, title = stringResource(R.string.room_remove_title, player.name)) {
        ListCard(Modifier.padding(bottom = 16.dp)) {
            choices.forEach { (label, duration) ->
                ArrowPreference(title = stringResource(label), onClick = { close { onRemove(duration); onDismiss() } })
            }
        }
        ListCard(Modifier.padding(bottom = 16.dp)) {
            ArrowPreference(
                title = stringResource(if (allowed) R.string.allowlist_remove_player else R.string.allowlist_add_player),
                onClick = { close { onToggleAllow(); onDismiss() } },
            )
        }
    }
}

/** Asks before the allowlist turns on. */
@Composable
fun AllowlistConfirm(onDismiss: () -> Unit, onConfirm: () -> Unit) {
    ConfirmDialog(
        title = stringResource(R.string.allowlist_confirm_title),
        message = stringResource(R.string.allowlist_confirm),
        confirmText = stringResource(R.string.allowlist_turn_on),
        onDismiss = onDismiss,
        onConfirm = onConfirm,
    )
}

/** Name, limit and notice of a room in one line. */
@Composable
private fun roomSummary(room: RoomInfo?): String {
    val parts = mutableListOf<String>()
    room?.name?.takeIf { it.isNotBlank() }?.let(parts::add)
    val max = room?.maxPlayers ?: 0
    parts += if (max > 0) stringResource(R.string.room_settings_limit, max) else stringResource(R.string.room_settings_unlimited)
    if (!room?.notice.isNullOrBlank()) parts += stringResource(R.string.room_settings_has_notice)
    return parts.joinToString(" · ")
}

@Composable
private fun nodesText(nodes: Int): String =
    if (nodes < 0) stringResource(R.string.diag_nodes_checking) else stringResource(R.string.diag_nodes_count, nodes)
