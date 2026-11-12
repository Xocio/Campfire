package top.mccampfire.app.feature.lobby

import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ArrowPreference
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.components.AccentBanner
import com.zeroui.kit.components.Badge
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.LoadingIndicator
import com.zeroui.kit.components.PressFeedback
import com.zeroui.kit.components.PullToRefresh
import com.zeroui.kit.components.Scaffold
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TopAppBar
import com.zeroui.kit.components.rememberPullRefreshState
import com.zeroui.kit.components.rememberTopBarScrollBehavior
import com.zeroui.kit.glass.BlurredTopBar
import com.zeroui.kit.glass.rememberTopBarBackdrop
import com.zeroui.kit.glass.topBarBlurSource
import com.zeroui.kit.glass.topBarColor
import com.zeroui.kit.theme.ZeroTheme
import kotlinx.coroutines.launch
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.navigation.AppRoute
import top.mccampfire.core.lobby.LobbyError
import top.mccampfire.core.lobby.LobbyRoom

/** Lobby tab: rooms other players listed. */
@Composable
fun LobbyScreen(bottomPadding: Dp, onNavigate: (AppRoute) -> Unit) {
    val repository = (LocalContext.current.applicationContext as CampfireApp).container.lobby
    val state by repository.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val scrollBehavior = rememberTopBarScrollBehavior()
    val backdrop = rememberTopBarBackdrop()
    val pullState = rememberPullRefreshState()

    LaunchedEffect(Unit) { repository.refresh() }

    Scaffold(
        topBar = {
            BlurredTopBar(backdrop, scrollBehavior) {
                TopAppBar(
                    title = stringResource(R.string.lobby_title),
                    color = topBarColor(backdrop),
                    scrollBehavior = scrollBehavior,
                )
            }
        },
    ) { padding ->
        PullToRefresh(
            modifier = Modifier.topBarBlurSource(backdrop),
            isRefreshing = state.loading && state.list != null,
            onRefresh = { scope.launch { repository.refresh(force = true) } },
            state = pullState,
            contentPadding = padding,
            topBarScroll = scrollBehavior,
            refreshTexts = listOf(
                stringResource(R.string.pull_to_refresh),
                stringResource(R.string.release_to_refresh),
                stringResource(R.string.refreshing),
                stringResource(R.string.refresh_success),
            ),
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 12.dp),
                contentPadding = PaddingValues(
                    top = padding.calculateTopPadding() + 6.dp,
                    bottom = padding.calculateBottomPadding() + bottomPadding + 10.dp,
                ),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                val list = state.list
                list?.notice?.takeIf { it.isNotBlank() }?.let { notice -> item { AccentBanner(text = notice) } }
                when {
                    list == null && state.loading -> item { Centered { LoadingIndicator() } }
                    list == null && state.error != null -> item {
                        Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            AccentBanner(
                                text = stringResource(if (state.error == LobbyError.Closed) R.string.lobby_closed else R.string.lobby_error),
                                modifier = Modifier.fillMaxWidth(),
                                danger = true,
                            )
                            ListCard {
                                ArrowPreference(title = stringResource(R.string.lobby_retry), onClick = { scope.launch { repository.refresh(force = true) } })
                            }
                        }
                    }
                    list != null && !list.enabled -> item { Message(stringResource(R.string.lobby_closed)) }
                    list != null && list.rooms.isEmpty() -> item { Message(stringResource(R.string.lobby_empty)) }
                    list != null -> items(list.rooms, key = { it.id }) { room ->
                        LobbyRoomCard(room = room, onClick = { onNavigate(AppRoute.LobbyRoom(room.id)) })
                    }
                }
            }
        }
    }
}

@Composable
private fun Centered(content: @Composable () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 48.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) { content() }
}

@Composable
private fun Message(text: String) {
    AccentBanner(text = text, modifier = Modifier.fillMaxWidth())
}

/** One room of the lobby list. */
@Composable
private fun LobbyRoomCard(room: LobbyRoom, onClick: () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(14.dp),
        pressFeedbackType = PressFeedback.Sink,
        showIndication = true,
        onClick = onClick,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            RoomIcon(room, 52.dp)
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = room.title,
                    fontSize = 16.sp,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                val subtitle = listOf(room.host, room.map).filter { it.isNotBlank() }.joinToString(" · ")
                if (subtitle.isNotEmpty()) {
                    Text(
                        text = subtitle,
                        fontSize = 13.sp,
                        color = ZeroTheme.colors.contentMuted,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                Spacer(Modifier.height(6.dp))
                RoomBadges(room)
            }
            Spacer(Modifier.width(8.dp))
            Column(horizontalAlignment = Alignment.End) {
                Text(text = playersText(room), fontSize = 15.sp, fontWeight = FontWeight.Medium)
                Text(text = ageText(room.createdAtMillis), fontSize = 12.sp, color = ZeroTheme.colors.contentMuted)
            }
        }
    }
}

/** Version, tags and flags of a room. */
@Composable
internal fun RoomBadges(room: LobbyRoom) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        if (room.version.isNotBlank()) Badge(room.version)
        room.tags.mapNotNull { tagName(it) }.forEach { Badge(it, accent = false) }
        if (room.locked) Badge(stringResource(R.string.lobby_locked), accent = false)
        if (room.voice) Badge(stringResource(R.string.lobby_voice), accent = false)
    }
}

/** The picture of a room, or its first letter. */
@Composable
internal fun RoomIcon(room: LobbyRoom, size: Dp) {
    val bitmap = remember(room.icon) {
        room.icon.takeIf { it.isNotEmpty() }?.let { text ->
            runCatching {
                val bytes = Base64.decode(text, Base64.DEFAULT)
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
            }.getOrNull()
        }
    }
    val shape = RoundedCornerShape(size / 4)
    Image(
        bitmap = bitmap ?: DefaultRoomIcon,
        contentDescription = null,
        modifier = Modifier
            .size(size)
            .clip(shape),
        contentScale = ContentScale.Crop,
        filterQuality = FilterQuality.None,
    )
}

/** A 16 by 16 grass block, shown for rooms without a picture. */
private val DefaultRoomIcon: ImageBitmap by lazy {
    val n = 16
    val grass = intArrayOf(0xFF5EA332.toInt(), 0xFF6BB33A.toInt(), 0xFF52922A.toInt(), 0xFF78C045.toInt())
    val dirt = intArrayOf(0xFF866043.toInt(), 0xFF966C4B.toInt(), 0xFF735238.toInt(), 0xFF7D593D.toInt())
    val pixels = IntArray(n * n)
    for (y in 0 until n) {
        for (x in 0 until n) {
            val edge = 3 + if (iconHash(x, 99) % 3 == 0) 2 else if (iconHash(x, 98) % 2 == 0) 1 else 0
            val palette = if (y < edge) grass else dirt
            pixels[y * n + x] = palette[iconHash(x, y) % palette.size]
        }
    }
    Bitmap.createBitmap(pixels, n, n, Bitmap.Config.ARGB_8888).asImageBitmap()
}

/** A fixed scatter of small numbers. */
private fun iconHash(x: Int, y: Int): Int {
    var h = (x * 73856093).toUInt() xor (y * 19349663).toUInt()
    h = h xor (h shr 13)
    h *= 0x5bd1e995u
    h = h xor (h shr 15)
    return (h % 1000u).toInt()
}

@Composable
internal fun playersText(room: LobbyRoom): String =
    if (room.maxPlayers > 0) stringResource(R.string.lobby_players, room.players, room.maxPlayers) else room.players.toString()

@Composable
internal fun ageText(createdAtMillis: Long): String {
    val minutes = ((System.currentTimeMillis() - createdAtMillis) / 60_000).coerceAtLeast(0)
    return when {
        minutes < 1 -> stringResource(R.string.lobby_just_now)
        minutes < 60 -> stringResource(R.string.lobby_minutes_ago, minutes.toInt())
        else -> stringResource(R.string.lobby_hours_ago, (minutes / 60).toInt())
    }
}

/** Name of a tag in the current language, or null for a tag this version does not know. */
@Composable
internal fun tagName(id: String): String? = when (id) {
    "survival" -> stringResource(R.string.lobby_tag_survival)
    "creative" -> stringResource(R.string.lobby_tag_creative)
    "building" -> stringResource(R.string.lobby_tag_building)
    "redstone" -> stringResource(R.string.lobby_tag_redstone)
    "minigame" -> stringResource(R.string.lobby_tag_minigame)
    "modded" -> stringResource(R.string.lobby_tag_modded)
    "adventure" -> stringResource(R.string.lobby_tag_adventure)
    "pvp" -> stringResource(R.string.lobby_tag_pvp)
    "casual" -> stringResource(R.string.lobby_tag_casual)
    else -> null
}
