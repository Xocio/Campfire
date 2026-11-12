package top.mccampfire.app.feature.room

import com.zeroui.kit.components.SwitchPreference
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ListRow
import com.zeroui.kit.components.LoadingIndicator
import com.zeroui.kit.components.ManagedBottomSheet
import com.zeroui.kit.components.TextButton
import kotlinx.coroutines.launch
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.R
import top.mccampfire.app.feature.linksettings.versionSummary
import top.mccampfire.app.game.GameVersion
import top.mccampfire.app.game.VoiceInstallException
import top.mccampfire.app.link.VoiceState

/** The voice chat row of the room tools. */
@Composable
fun VoiceRow(onFolders: () -> Unit) {
    val voice = (LocalContext.current.applicationContext as CampfireApp).container.voice
    val ui by voice.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    var choosing by remember { mutableStateOf<List<GameVersion>?>(null) }
    LaunchedEffect(Unit) { voice.refresh() }

    val title = stringResource(R.string.voice_label)
    val version = ui.version.orEmpty()
    val summary = when (ui.state) {
        VoiceState.HostOff -> stringResource(R.string.voice_host_off)
        VoiceState.HostNeedFolder, VoiceState.GuestNoFolder -> stringResource(R.string.voice_need_folder)
        VoiceState.HostInstalling -> stringResource(R.string.voice_host_installing)
        VoiceState.HostRestart -> stringResource(R.string.voice_host_restart)
        VoiceState.HostReady -> stringResource(R.string.voice_host_ready)
        VoiceState.HostProblem, VoiceState.GuestProblem -> problemText(ui.problem)
        VoiceState.GuestOff -> stringResource(R.string.voice_guest_off)
        VoiceState.GuestChecking -> stringResource(R.string.voice_guest_checking)
        VoiceState.GuestWaiting -> stringResource(R.string.voice_guest_waiting)
        VoiceState.GuestNoVersion -> stringResource(R.string.voice_guest_no_version)
        VoiceState.GuestMissing -> stringResource(R.string.voice_guest_missing, "", version)
        VoiceState.GuestInstalling -> stringResource(R.string.voice_installing)
        VoiceState.GuestInstalled -> stringResource(R.string.voice_installed_restart, "Simple Voice Chat", version)
        VoiceState.GuestReady -> stringResource(R.string.voice_guest_ready)
        VoiceState.GuestPortBusy -> stringResource(R.string.voice_guest_port_busy, ui.port.toString())
    }

    when (ui.state) {
        VoiceState.HostNeedFolder, VoiceState.GuestNoFolder -> ArrowPreference(title = title, summary = summary, onClick = onFolders)
        VoiceState.GuestInstalling, VoiceState.GuestChecking ->
            ListRow(title = title, summary = summary, endActions = { LoadingIndicator() })
        VoiceState.HostOff, VoiceState.HostProblem, VoiceState.HostInstalling, VoiceState.HostRestart, VoiceState.HostReady -> SwitchPreference(
            title = title,
            summary = summary,
            checked = ui.state == VoiceState.HostInstalling || ui.state == VoiceState.HostRestart || ui.state == VoiceState.HostReady,
            enabled = ui.state != VoiceState.HostInstalling,
            onCheckedChange = { on -> if (on) scope.launch { choosing = voice.versions() } else voice.turnOff() },
        )
        VoiceState.GuestMissing, VoiceState.GuestProblem -> ArrowPreference(
            title = title,
            summary = summary,
            onClick = { scope.launch { voice.guestTarget()?.let(voice::installForGuest) } },
        )
        else -> ListRow(title = title, summary = summary)
    }

    choosing?.let { versions ->
        ManagedBottomSheet(onDismiss = { choosing = null }, title = stringResource(R.string.voice_choose_version)) {
            ListCard(Modifier.padding(bottom = 16.dp)) {
                if (versions.isEmpty()) {
                    ListRow(title = stringResource(R.string.games_no_versions))
                }
                versions.forEach { version ->
                    ArrowPreference(
                        title = version.name,
                        summary = versionSummary(version),
                        onClick = { close { voice.turnOn(version); choosing = null } },
                    )
                }
            }
        }
    }
}

@Composable
private fun problemText(problem: VoiceInstallException.Problem?): String = stringResource(
    when (problem) {
        VoiceInstallException.Problem.NoVersion -> R.string.voice_problem_voice_no_version
        VoiceInstallException.Problem.NoLoader -> R.string.voice_problem_voice_no_loader
        VoiceInstallException.Problem.NotFound -> R.string.voice_problem_voice_not_found
        else -> R.string.voice_problem_unexpected
    },
)
