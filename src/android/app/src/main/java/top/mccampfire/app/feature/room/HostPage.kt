package top.mccampfire.app.feature.room

import top.mccampfire.app.link.LocalNetwork
import androidx.compose.ui.platform.LocalContext
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.AccentBanner
import com.zeroui.kit.components.ArrowPreference
import com.zeroui.kit.components.ButtonVariant
import com.zeroui.kit.components.DialogButtons
import com.zeroui.kit.components.ListCard
import com.zeroui.kit.components.ManagedDialog
import com.zeroui.kit.components.Text
import com.zeroui.kit.components.TextButton
import com.zeroui.kit.components.TextField
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.R

/**
 * Page that starts a room. [onHost] gets the typed port, or null to wait for the world announced on this device.
 */
@Composable
fun HostPage(pad: PaddingValues, onHost: (Int?) -> Unit) {
    var manual by rememberSaveable { mutableStateOf(false) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { onHost(null) }
    val context = LocalContext.current

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(pad)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        AccentBanner(text = stringResource(R.string.host_hint))
        TextButton(
            text = stringResource(R.string.host_start),
            onClick = {
                if (LocalNetwork.isGranted(context)) onHost(null) else permission.launch(LocalNetwork.Permission)
            },
            variant = ButtonVariant.Primary,
            modifier = Modifier.fillMaxWidth(),
        )
        ListCard {
            ArrowPreference(
                title = stringResource(R.string.host_manual),
                summary = stringResource(R.string.host_manual_sub),
                onClick = { manual = true },
            )
        }
    }

    if (manual) {
        PortDialog(onDismiss = { manual = false }, onConfirm = onHost)
    }
}

@Composable
private fun PortDialog(onDismiss: () -> Unit, onConfirm: (Int) -> Unit) {
    var text by rememberSaveable { mutableStateOf("") }
    val port = text.toIntOrNull()?.takeIf { it in 1..65535 }
    ManagedDialog(onDismiss = onDismiss, title = stringResource(R.string.host_manual)) {
        Column(modifier = Modifier.fillMaxWidth()) {
            TextField(
                value = text,
                onValueChange = { value -> if (value.length <= 5 && value.all(Char::isDigit)) text = value },
                label = stringResource(R.string.host_port_hint),
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(10.dp))
            AccentBanner(
                text = stringResource(if (text.isNotEmpty() && port == null) R.string.host_port_invalid else R.string.host_manual_sub),
                compact = true,
                danger = text.isNotEmpty() && port == null,
            )
            Spacer(Modifier.height(16.dp))
            DialogButtons(
                confirmText = stringResource(R.string.host_start),
                confirmEnabled = port != null,
                onConfirm = { close { port?.let(onConfirm); onDismiss() } },
                onCancel = { close(onDismiss) },
            )
        }
    }
}
