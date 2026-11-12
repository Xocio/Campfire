package top.mccampfire.app.feature.home

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.offset
import com.zeroui.kit.components.excludeFromDragContainer
import com.zeroui.kit.motion.ZeroMotion
import top.mccampfire.app.feature.settings.okAccentColor
import top.mccampfire.app.feature.settings.okContainerColor
import top.mccampfire.app.feature.settings.neutralContainerColor
import top.mccampfire.core.link.LinkSession
import androidx.compose.animation.Crossfade
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.PreferenceIcon
import com.zeroui.kit.components.SettingIcons
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import top.mccampfire.app.CampfireApp
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.LinkStage
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.DraggableItem
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.IconButton
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.PressFeedback
import com.zeroui.kit.components.Scaffold
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TopAppBar
import com.zeroui.kit.components.dragContainer
import com.zeroui.kit.components.fastItemPlacement
import com.zeroui.kit.components.rememberDragDropState
import com.zeroui.kit.components.rememberTopBarScrollBehavior
import com.zeroui.kit.glass.BlurredTopBar
import com.zeroui.kit.glass.LocalPageBackground
import com.zeroui.kit.glass.rememberTopBarBackdrop
import com.zeroui.kit.glass.topBarBlurSource
import com.zeroui.kit.glass.topBarColor
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.R
import top.mccampfire.app.data.AppSettings
import top.mccampfire.app.data.Notice
import top.mccampfire.app.feature.notice.NoticeBanner
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.homeTitleText
import top.mccampfire.app.navigation.AppRoute
import top.yukonga.miuix.kmp.basic.SnackbarHost
import top.yukonga.miuix.kmp.basic.SnackbarHostState
import top.yukonga.miuix.kmp.basic.TooltipBox
import top.yukonga.miuix.kmp.icon.MiuixIcons
import top.yukonga.miuix.kmp.icon.extended.Settings

/** Reorderable cards of the home page. */
enum class HomeCard { Status, Actions, Guide }

private data class HomeLayout(val visible: List<HomeCard>, val hidden: List<HomeCard>)

private fun parseCards(csv: String): List<HomeCard> =
    csv.split(",").mapNotNull { name -> HomeCard.entries.firstOrNull { it.name == name } }.distinct()

private fun AppSettings.homeLayout(): HomeLayout {
    val hidden = parseCards(homeHidden)
    val saved = parseCards(homeOrder).filterNot { it in hidden }
    val visible = saved.toMutableList()
    HomeCard.entries.filterNot { it in saved || it in hidden }.forEach { card ->
        visible.add(HomeCard.entries.indexOf(card).coerceAtMost(visible.size), card)
    }
    return HomeLayout(visible, hidden)
}

/** Visible home cards in their current order. */
internal fun AppSettings.visibleHomeCards(): List<HomeCard> = homeLayout().visible

/** State of the multiplayer status card. */
enum class LinkStatus { Idle, Connecting, Joined, Hosting, Failed }

/** Home tab with reorderable cards and the settings entry in the top bar. */
@Composable
fun HomeScreen(
    bottomPadding: Dp,
    hasUpdate: Boolean,
    onUpdateClick: () -> Unit,
    onNavigate: (AppRoute) -> Unit,
    onNoticeAction: (Notice) -> Unit,
) {
    val controller = LocalAppController.current
    val container = (LocalContext.current.applicationContext as CampfireApp).container
    val link = container.link
    val noticeBanner by container.notices.banner.collectAsStateWithLifecycle()
    val linkState by link.state.collectAsStateWithLifecycle()
    val status = when {
        linkState.stage == LinkStage.Working -> LinkStatus.Connecting
        linkState.stage == LinkStage.Ready && linkState.role == LinkRole.Host -> LinkStatus.Hosting
        linkState.stage == LinkStage.Ready -> LinkStatus.Joined
        linkState.stage == LinkStage.Failed -> LinkStatus.Failed
        else -> LinkStatus.Idle
    }
    val playersValue = if (linkState.stage == LinkStage.Ready) linkState.players.size.toString() else "-"
    val latency = linkState.hostPeer?.latencyMs
    val showLatency = status == LinkStatus.Joined && latency != null
    val secondLabel = stringResource(if (showLatency) R.string.home_stat_latency else R.string.home_stat_nodes)
    val secondValue = when {
        showLatency -> LinkSession.formatLatency(latency!!) + " ms"
        linkState.isActive && linkState.connectedNodes >= 0 -> linkState.connectedNodes.toString()
        else -> "-"
    }
    val showRoom = linkState.stage != LinkStage.Idle
    val snackbar = remember { SnackbarHostState() }

    val saved = remember { controller.settings.homeLayout() }
    val order = remember { mutableStateListOf<HomeCard>().apply { addAll(saved.visible) } }
    val hidden = remember { mutableStateListOf<HomeCard>().apply { addAll(saved.hidden) } }
    var editing by rememberSaveable { mutableStateOf(false) }
    val persist = {
        controller.setHomeLayout(order.joinToString(",") { it.name }, hidden.joinToString(",") { it.name })
    }

    val listState = rememberLazyListState()
    // The notice banner sits above the cards and takes the first list position.
    val cardOffset = rememberUpdatedState(if (noticeBanner != null) 1 else 0)
    val dragDropState = rememberDragDropState(
        lazyListState = listState,
        canDrag = { index -> index - cardOffset.value in order.indices },
        onMove = { from, to ->
            val offset = cardOffset.value
            order.add(to - offset, order.removeAt(from - offset))
            persist()
        },
    )
    LaunchedEffect(order.size) { if (order.isEmpty()) editing = true }
    BackHandler(enabled = editing && order.isNotEmpty()) { editing = false }
    val editHint = stringResource(R.string.home_edit_hint)
    LaunchedEffect(editing) { if (editing) snackbar.showSnackbar(editHint) }

    val homeBackground = LocalPageBackground.current ?: rememberPageBackground()
    CompositionLocalProvider(LocalPageBackground provides homeBackground) {
        val scrollBehavior = rememberTopBarScrollBehavior()
        val topBarBackdrop = rememberTopBarBackdrop()

        Scaffold(
            modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
            topBar = {
                BlurredTopBar(topBarBackdrop, scrollBehavior) {
                    TopAppBar(
                        color = topBarColor(topBarBackdrop),
                        title = if (editing) {
                            stringResource(R.string.home_edit_title)
                        } else {
                            homeTitleText(stringResource(R.string.app_name))
                        },
                        scrollBehavior = scrollBehavior,
                        actions = {
                            AnimatedContent(
                                targetState = editing,
                                transitionSpec = { ZeroMotion.fadeThrough(clip = false) },
                                contentAlignment = Alignment.CenterEnd,
                                label = "homeActions",
                            ) { inEditMode ->
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    if (inEditMode) {
                                        TextButton(
                                            text = stringResource(R.string.done),
                                            onClick = { editing = false },
                                            enabled = order.isNotEmpty(),
                                            variant = ButtonVariant.Primary,
                                            minWidth = 64.dp,
                                            minHeight = 34.dp,
                                            insideMargin = PaddingValues(horizontal = 14.dp, vertical = 7.dp),
                                            modifier = Modifier.padding(end = 12.dp),
                                        )
                                    } else {
                                        if (hasUpdate) {
                                            val updateText = stringResource(R.string.update_available)
                                            TooltipBox(text = updateText) {
                                                IconButton(onClick = onUpdateClick) {
                                                    Icon(
                                                        painter = painterResource(R.drawable.ic_alert_circle),
                                                        contentDescription = updateText,
                                                        tint = ZeroTheme.colors.danger,
                                                    )
                                                }
                                            }
                                        }
                                        val settingsText = stringResource(R.string.app_settings)
                                        TooltipBox(text = settingsText) {
                                            IconButton(onClick = { onNavigate(AppRoute.AppSettings) }) {
                                                Icon(
                                                    imageVector = MiuixIcons.Settings,
                                                    contentDescription = settingsText,
                                                    tint = ZeroTheme.colors.content,
                                                )
                                            }
                                        }
                                    }
                                }
                            }
                        },
                    )
                }
            },
            snackbarHost = { SnackbarHost(snackbar, modifier = Modifier.padding(bottom = bottomPadding)) },
        ) { innerPadding ->
            val renderCard: @Composable (HomeCard) -> Unit = { card ->
                when (card) {
                    HomeCard.Status -> StatusCard(
                        status = status,
                        playersValue = playersValue,
                        secondLabel = secondLabel,
                        secondValue = secondValue,
                        onStatusClick = { onNavigate(if (showRoom) AppRoute.Room else AppRoute.Join) },
                        workingCardModifier = Modifier.excludeFromDragContainer(dragDropState),
                    )
                    HomeCard.Actions -> ActionsCard(onHost = { onNavigate(AppRoute.Host) }, onJoin = { onNavigate(AppRoute.Join) })
                    HomeCard.Guide -> GuideCard()
                }
            }

            LazyColumn(
                state = listState,
                modifier = Modifier
                    .fillMaxSize()
                    .topBarBlurSource(topBarBackdrop)
                    .padding(horizontal = 16.dp)
                    .dragContainer(dragDropState, onLongPress = { editing = true }),
                contentPadding = PaddingValues(
                    top = innerPadding.calculateTopPadding() + 6.dp,
                    bottom = innerPadding.calculateBottomPadding() + bottomPadding + 10.dp,
                ),
            ) {
                val banner = noticeBanner
                if (banner != null) {
                    item(key = "notice-" + banner.id) {
                        NoticeBanner(
                            notice = banner,
                            onAction = onNoticeAction,
                            onClose = container.notices::bannerClosed,
                            modifier = fastItemPlacement().padding(vertical = 6.dp),
                        )
                    }
                }
                itemsIndexed(order, key = { _, card -> card.name }) { index, card ->
                    DraggableItem(dragDropState, index + cardOffset.value) { isDragging ->
                        HomeCardEditable(
                            editing = editing,
                            dragging = isDragging,
                            checked = true,
                            onToggle = {
                                order.remove(card)
                                if (card !in hidden) hidden.add(card)
                                persist()
                            },
                            modifier = Modifier.padding(vertical = 6.dp),
                        ) { renderCard(card) }
                    }
                }
                if (editing && hidden.isNotEmpty()) {
                    item(key = "homeClosedTitle") {
                        SectionTitle(
                            text = stringResource(R.string.home_edit_closed_title),
                            modifier = fastItemPlacement(),
                        )
                    }
                    items(hidden, key = { it.name }) { card ->
                        HomeCardEditable(
                            editing = true,
                            dragging = false,
                            checked = false,
                            onToggle = {
                                hidden.remove(card)
                                if (card !in order) order.add(card)
                                persist()
                            },
                            modifier = fastItemPlacement().padding(vertical = 6.dp),
                        ) { renderCard(card) }
                    }
                }
            }
        }
    }
}

/** Status row: the room state on the left, two figures on the right. */
@Composable
private fun StatusCard(
    status: LinkStatus,
    playersValue: String,
    secondLabel: String,
    secondValue: String,
    onStatusClick: () -> Unit,
    workingCardModifier: Modifier,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(IntrinsicSize.Min),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        LinkStatusCard(
            status = status,
            onClick = onStatusClick,
            modifier = Modifier
                .weight(1f)
                .fillMaxHeight()
                .then(workingCardModifier),
        )
        Column(modifier = Modifier.weight(1f)) {
            StatCard(
                label = stringResource(R.string.home_stat_players),
                value = playersValue,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(12.dp))
            StatCard(
                label = secondLabel,
                value = secondValue,
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}

/** Large status card with crossfading states. */
@Composable
private fun LinkStatusCard(status: LinkStatus, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val ready = status == LinkStatus.Joined || status == LinkStatus.Hosting
    val container by animateColorAsState(
        targetValue = if (ready) okContainerColor else neutralContainerColor,
        animationSpec = tween(450),
        label = "linkStatusContainer",
    )
    Card(
        modifier = modifier,
        color = container,
        onClick = onClick,
        showIndication = true,
        pressFeedbackType = PressFeedback.Tilt,
    ) {
        Box(modifier = Modifier.fillMaxSize()) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .offset(38.dp, 45.dp),
                contentAlignment = Alignment.BottomEnd,
            ) {
                Crossfade(targetState = status, animationSpec = tween(450), label = "linkStatusDecor") { current ->
                    when (current) {
                        LinkStatus.Connecting -> Box(Modifier.size(170.dp))
                        LinkStatus.Joined, LinkStatus.Hosting -> Icon(
                            painter = painterResource(R.drawable.ic_check_circle),
                            contentDescription = null,
                            tint = okAccentColor,
                            modifier = Modifier.size(170.dp),
                        )
                        LinkStatus.Idle, LinkStatus.Failed -> Icon(
                            painter = painterResource(R.drawable.ic_x_circle),
                            contentDescription = null,
                            tint = ZeroTheme.colors.danger,
                            modifier = Modifier.size(170.dp),
                        )
                    }
                }
            }
            Column(modifier = Modifier.padding(16.dp)) {
                Crossfade(targetState = status, animationSpec = tween(450), label = "linkStatusText") { current ->
                    Column {
                        Text(
                            text = stringResource(
                                when (current) {
                                    LinkStatus.Idle -> R.string.home_link_idle
                                    LinkStatus.Connecting -> R.string.home_link_connecting
                                    LinkStatus.Joined -> R.string.home_link_joined
                                    LinkStatus.Hosting -> R.string.home_link_hosting
                                    LinkStatus.Failed -> R.string.home_link_failed
                                },
                            ),
                            fontSize = 20.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = ZeroTheme.colors.content,
                        )
                        Spacer(Modifier.height(2.dp))
                        Text(
                            text = stringResource(
                                when (current) {
                                    LinkStatus.Idle -> R.string.home_link_idle_sub
                                    LinkStatus.Connecting -> R.string.home_link_connecting_sub
                                    LinkStatus.Joined -> R.string.home_link_joined_sub
                                    LinkStatus.Hosting -> R.string.home_link_hosting_sub
                                    LinkStatus.Failed -> R.string.home_link_failed_sub
                                },
                            ),
                            fontSize = 14.sp,
                            fontWeight = FontWeight.Medium,
                            color = ZeroTheme.colors.contentMuted,
                        )
                    }
                }
            }
        }
    }
}

/** Small card with a label and a figure. */
@Composable
private fun StatCard(label: String, value: String, modifier: Modifier = Modifier) {
    Card(
        modifier = modifier,
        contentPadding = PaddingValues(16.dp),
        pressFeedbackType = PressFeedback.Sink,
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = label,
                fontSize = 15.sp,
                fontWeight = FontWeight.Medium,
                color = ZeroTheme.colors.contentMuted,
            )
            AnimatedContent(
                targetState = value,
                transitionSpec = { ZeroMotion.textTicker() },
                label = "statValue",
            ) { current ->
                Text(
                    text = current,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = ZeroTheme.colors.content,
                    maxLines = 1,
                )
            }
        }
    }
}

/** Entries that start a session. */
@Composable
private fun ActionsCard(onHost: () -> Unit, onJoin: () -> Unit) {
    ListCard {
        ArrowPreference(
            title = stringResource(R.string.home_host),
            summary = stringResource(R.string.home_host_sub),
            startAction = { PreferenceIcon(SettingIcons.share) },
            onClick = onHost,
        )
        ArrowPreference(
            title = stringResource(R.string.home_join),
            summary = stringResource(R.string.home_join_sub),
            startAction = { PreferenceIcon(SettingIcons.importData) },
            onClick = onJoin,
        )
    }
}

/** Numbered steps of a multiplayer session. */
@Composable
private fun GuideCard() {
    val steps = listOf(
        R.string.home_guide_step_1,
        R.string.home_guide_step_2,
        R.string.home_guide_step_3,
    )
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = stringResource(R.string.home_guide_title),
            modifier = Modifier.padding(start = 4.dp, bottom = 8.dp),
            fontSize = 14.sp,
            fontWeight = FontWeight.Medium,
            color = ZeroTheme.colors.contentMuted,
        )
        ListCard {
            steps.forEachIndexed { index, text ->
                ListRow(
                    title = stringResource(text),
                    startAction = { StepNumber(index + 1) },
                )
            }
        }
    }
}

@Composable
private fun StepNumber(number: Int) {
    Box(
        modifier = Modifier
            .size(28.dp)
            .background(ZeroTheme.colors.accentContainer, CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = number.toString(),
            fontSize = 14.sp,
            fontWeight = FontWeight.SemiBold,
            color = ZeroTheme.colors.onAccentContainer,
        )
    }
}
