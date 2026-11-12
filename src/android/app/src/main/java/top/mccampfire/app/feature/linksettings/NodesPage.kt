package top.mccampfire.app.feature.linksettings

import com.zeroui.kit.components.ConfirmDialog
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
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.ManagedDialog
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.DialogButtons
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.AccentBanner
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.SwitchPreference
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TextField
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.link.NodeTest
import top.mccampfire.core.link.LinkSession
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.core.link.PublicNode
import top.mccampfire.core.link.PublicNodes

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** Built-in relay servers with switches, and the custom servers field. */
@Composable
fun NodesPage(pad: PaddingValues) {
    val controller = LocalAppController.current
    val prefs = controller.settings.link
    val disabled = prefs.disabledNodes.split(',').filter { it.isNotBlank() }.toSet()
    val customNodes = PublicNodes.parseCustom(prefs.customNodes)
    var adding by rememberSaveable { mutableStateOf(false) }
    var removing by rememberSaveable { mutableStateOf<String?>(null) }
    val tester = (LocalContext.current.applicationContext as CampfireApp).container.link.nodeTester
    val test by tester.state.collectAsStateWithLifecycle()
    LaunchedEffect(Unit) { tester.refreshIfStale() }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item { SectionTitle(stringResource(R.string.nodes_builtin)) }
        item {
            ListCard(CardModifier) {
                PublicNodes.All.forEach { node ->
                    SwitchPreference(
                        title = regionName(node),
                        summary = stringResource(R.string.nodes_provided_by, node.provider) +
                            (if (node.limited) " · " + stringResource(R.string.nodes_limited) else "") +
                            " · " + latencyText(node.id, test),
                        checked = node.id !in disabled,
                        onCheckedChange = { on ->
                            val next = if (on) disabled - node.id else disabled + node.id
                            controller.setLink { it.copy(disabledNodes = next.joinToString(","), usePublicNodes = next.size < PublicNodes.All.size) }
                        },
                    )
                }
            }
        }
        item {
            ListCard(CardModifier) {
                ArrowPreference(
                    title = stringResource(R.string.nodes_test),
                    summary = if (test.running) stringResource(R.string.nodes_testing) else null,
                    enabled = !test.running,
                    onClick = { tester.test() },
                )
            }
        }
        item { AccentBanner(text = stringResource(R.string.nodes_thanks), modifier = CardModifier) }
        item { SectionTitle(stringResource(R.string.nodes_custom)) }
        item {
            ListCard(CardModifier) {
                customNodes.forEach { node ->
                    ArrowPreference(title = node, onClick = { removing = node })
                }
                ArrowPreference(
                    title = stringResource(R.string.nodes_add),
                    summary = stringResource(R.string.nodes_custom_note),
                    onClick = { adding = true },
                )
            }
        }
    }

    removing?.let { node ->
        ConfirmDialog(
            title = stringResource(R.string.nodes_remove_title),
            message = stringResource(R.string.nodes_remove_confirm, node),
            confirmText = stringResource(R.string.nodes_remove),
            onDismiss = { removing = null },
            onConfirm = {
                removing = null
                controller.setLink { it.copy(customNodes = (customNodes - node).joinToString(" ")) }
            },
        )
    }

    if (adding) {
        AddNodeDialog(
            onDismiss = { adding = false },
            onAdd = { added -> controller.setLink { it.copy(customNodes = (customNodes + added).distinct().joinToString(" ")) } },
        )
    }
}

/** Asks for one or more server addresses and checks them before they are added. */
@Composable
private fun AddNodeDialog(onDismiss: () -> Unit, onAdd: (List<String>) -> Unit) {
    var text by rememberSaveable { mutableStateOf("") }
    val invalid = mutableListOf<String>()
    val nodes = PublicNodes.parseCustom(text, invalid)
    val bad = invalid.isNotEmpty()
    ManagedDialog(onDismiss = onDismiss, title = stringResource(R.string.nodes_add)) {
        Column(modifier = Modifier.fillMaxWidth()) {
            TextField(
                value = text,
                onValueChange = { if (it.length <= 500) text = it },
                label = stringResource(R.string.nodes_custom_hint),
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(10.dp))
            AccentBanner(
                text = if (bad) stringResource(R.string.nodes_custom_invalid, invalid.joinToString(" ")) else stringResource(R.string.nodes_custom_note),
                compact = true,
                danger = bad,
            )
            Spacer(Modifier.height(16.dp))
            DialogButtons(
                confirmText = stringResource(R.string.nodes_add_confirm),
                confirmEnabled = nodes.isNotEmpty() && !bad,
                onConfirm = { close { onAdd(nodes); onDismiss() } },
                onCancel = { close(onDismiss) },
            )
        }
    }
}

/** Region and network of a built-in node in the current language. */
@Composable
private fun regionName(node: PublicNode): String = stringResource(
    when (node.id) {
        "sh-ct" -> R.string.nodes_region_sh_ct
        "xm-ct" -> R.string.nodes_region_xm_ct
        "cq-cm" -> R.string.nodes_region_cq_cm
        "zz-bgp" -> R.string.nodes_region_zz_bgp
        "sh-bgp" -> R.string.nodes_region_sh_bgp
        "cn-eo" -> R.string.nodes_region_cn_eo
        "yn-cu" -> R.string.nodes_region_yn_cu
        "intl-cf" -> R.string.nodes_region_intl_cf
        else -> R.string.nodes_region_intl_wk
    },
)

/** Latest latency of a node, or its test state. */
@Composable
private fun latencyText(id: String, test: NodeTest): String = when {
    test.running && id !in test.latencies -> stringResource(R.string.nodes_testing)
    id !in test.latencies -> "-"
    else -> test.latencies[id]?.let { stringResource(R.string.nodes_latency, LinkSession.formatLatency(it)) }
        ?: stringResource(R.string.nodes_unreachable)
}
