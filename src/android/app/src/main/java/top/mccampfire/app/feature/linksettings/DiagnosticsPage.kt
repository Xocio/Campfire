package top.mccampfire.app.feature.linksettings

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.Card
import com.zeroui.kit.components.Icon
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.LoadingIndicator
import com.zeroui.kit.components.SectionTitle
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.observe
import top.mccampfire.app.link.DiagCheck
import top.mccampfire.app.link.DiagResult
import top.mccampfire.app.link.DiagState
import top.mccampfire.app.link.Diagnostics

private val CardModifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)

/** Runs the multiplayer checks one after the other and shows each result. */
@Composable
fun DiagnosticsPage(pad: PaddingValues) {
    val context = LocalContext.current
    val container = (context.applicationContext as CampfireApp).container
    val prefs by rememberUpdatedState(LocalAppController.current.observe { it.link })
    val diagnostics = remember { Diagnostics(context.applicationContext, container.link, container.identity) { prefs } }
    val results = remember { mutableStateMapOf<DiagCheck, DiagResult>() }
    var round by remember { mutableIntStateOf(0) }
    val running = DiagCheck.entries.any { results[it]?.state.let { state -> state == null || state == DiagState.Running || state == DiagState.Pending } }

    LaunchedEffect(round) {
        results.clear()
        DiagCheck.entries.forEach { results[it] = DiagResult(DiagState.Pending) }
        for (check in DiagCheck.entries) {
            results[check] = DiagResult(DiagState.Running)
            results[check] = runCatching { diagnostics.run(check) }.getOrElse { DiagResult(DiagState.Info) }
        }
    }

    LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = pad) {
        item { SectionTitle(stringResource(R.string.diag_header)) }
        item {
            ListCard(CardModifier) {
                DiagCheck.entries.forEach { check ->
                    val result = results[check] ?: DiagResult(DiagState.Pending)
                    ListRow(
                        title = stringResource(check.title),
                        summary = detailText(result),
                        endActions = { StateMark(result.state) },
                    )
                }
            }
        }
        if (!running) {
            item {
                val fails = results.values.count { it.state == DiagState.Fail }
                val warns = results.values.count { it.state == DiagState.Warn }
                Card(modifier = CardModifier.fillMaxWidth(), contentPadding = PaddingValues(16.dp)) {
                    Text(
                        text = when {
                            fails > 0 -> stringResource(R.string.diag_result_fail, fails)
                            warns > 0 -> stringResource(R.string.diag_result_warn, warns)
                            else -> stringResource(R.string.diag_result_ok)
                        },
                        fontSize = 14.sp,
                        color = if (fails > 0) ZeroTheme.colors.danger else ZeroTheme.colors.content,
                    )
                }
            }
        }
        item {
            TextButton(
                text = stringResource(R.string.diag_run),
                onClick = { round++ },
                enabled = !running,
                variant = ButtonVariant.Primary,
                modifier = CardModifier.fillMaxWidth(),
            )
        }
    }
}

@Composable
private fun detailText(result: DiagResult): String? = when {
    result.state == DiagState.Running -> stringResource(R.string.diag_checking)
    result.detail == null -> null
    else -> stringResource(result.detail, *result.args.toTypedArray())
}

@Composable
private fun StateMark(state: DiagState) {
    when (state) {
        DiagState.Pending -> Unit
        DiagState.Running -> LoadingIndicator(size = 20.dp)
        DiagState.Ok -> Icon(
            painter = painterResource(R.drawable.ic_check_circle),
            contentDescription = null,
            tint = ZeroTheme.colors.success,
            modifier = Modifier.size(22.dp),
        )
        DiagState.Info -> Icon(
            painter = painterResource(R.drawable.ic_alert_circle),
            contentDescription = null,
            tint = ZeroTheme.colors.contentMuted,
            modifier = Modifier.size(22.dp),
        )
        DiagState.Warn -> Icon(
            painter = painterResource(R.drawable.ic_alert_circle),
            contentDescription = null,
            tint = ZeroTheme.colors.accent,
            modifier = Modifier.size(22.dp),
        )
        DiagState.Fail -> Icon(
            painter = painterResource(R.drawable.ic_x_circle),
            contentDescription = null,
            tint = ZeroTheme.colors.danger,
            modifier = Modifier.size(22.dp),
        )
    }
}
