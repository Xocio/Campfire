package top.mccampfire.app.feature.settings

import com.zeroui.kit.components.rememberDragDropState
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import top.mccampfire.app.R
import com.zeroui.kit.components.DraggableItem
import com.zeroui.kit.components.dragContainer
import com.zeroui.kit.components.fastItemPlacement
import top.mccampfire.app.feature.main.MainTab
import top.mccampfire.app.feature.main.navLayout
import top.mccampfire.app.feature.main.mainTabIcon
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.Switch
import com.zeroui.kit.components.Text
import top.yukonga.miuix.kmp.theme.MiuixTheme

private const val NAV_TABS_HEADER = 1

@Composable
internal fun NavTabsPage(pad: PaddingValues) {
    val controller = LocalAppController.current
    val listed: (MainTab) -> Boolean = { true }

    val saved = remember { controller.settings.navLayout() }
    val order = remember { mutableStateListOf<MainTab>().apply { addAll(saved.order) } }
    val hidden = remember { mutableStateListOf<MainTab>().apply { addAll(saved.hidden) } }

    fun shownNow(): List<MainTab> = order.filter(listed)
    fun persist() = controller.setNavLayout(order.joinToString(",") { it.name }, hidden.joinToString(",") { it.name })

    val listState = rememberLazyListState()
    val dragDropState = rememberDragDropState(
        lazyListState = listState,
        canDrag = { index -> index - NAV_TABS_HEADER in shownNow().indices },
        onMove = { from, to ->
            val shown = shownNow()
            val moving = shown[from - NAV_TABS_HEADER]
            val target = shown[to - NAV_TABS_HEADER]

            order.remove(moving)
            val at = order.indexOf(target)
            order.add(if (to > from) at + 1 else at, moving)
            persist()
        }
    )

    val shown = shownNow()
    val closed = hidden.filter(listed)

    LazyColumn(
        state = listState,
        modifier = Modifier.fillMaxSize().dragContainer(dragDropState),
        contentPadding = pad
    ) {
        item(key = "navShownTitle") { SectionTitle(stringResource(R.string.nav_tabs_shown)) }
        itemsIndexed(shown, key = { _, tab -> tab.name }) { index, tab ->
            DraggableItem(dragDropState, NAV_TABS_HEADER + index) {
                NavTabCard(
                    tab = tab,
                    checked = true,
                    onToggle = {
                        order.remove(tab)
                        if (tab !in hidden) hidden.add(tab)
                        persist()
                    }
                )
            }
        }

        if (closed.isNotEmpty()) {
            item(key = "navClosedTitle") {
                SectionTitle(
                    text = stringResource(R.string.nav_tabs_closed),
                    modifier = fastItemPlacement()
                )
            }
            items(closed, key = { it.name }) { tab ->
                NavTabCard(
                    tab = tab,
                    checked = false,
                    modifier = fastItemPlacement(),
                    onToggle = {
                        hidden.remove(tab)
                        if (tab !in order) order.add(tab)
                        persist()
                    }
                )
            }
        }

        item(key = "navHint") {
            Text(
                text = stringResource(R.string.nav_tabs_hint),
                modifier = fastItemPlacement().padding(horizontal = 24.dp, vertical = 8.dp),
                fontSize = 13.sp,
                color = MiuixTheme.colorScheme.onSurfaceVariantSummary
            )
        }
    }
}

@Composable
private fun NavTabCard(
    tab: MainTab,
    checked: Boolean,
    onToggle: () -> Unit,
    modifier: Modifier = Modifier
) {
    Card(
        modifier = modifier.padding(horizontal = 12.dp, vertical = 6.dp),
        contentPadding = PaddingValues(0.dp)
    ) {
        ListRow(
            title = stringResource(tab.labelRes),
            summary = if (tab.removable) null else stringResource(R.string.nav_tabs_fixed),
            startAction = {
                Icon(
                    imageVector = mainTabIcon(tab),
                    contentDescription = null,
                    tint = MiuixTheme.colorScheme.onSurface,
                    modifier = Modifier
                        .padding(end = 16.dp)
                        .size(24.dp)
                        .alpha(if (checked) 1f else 0.4f)
                )
            },
            endActions = {
                Switch(
                    checked = checked,
                    enabled = tab.removable,
                    onCheckedChange = { onToggle() }
                )
            }
        )
    }
}
