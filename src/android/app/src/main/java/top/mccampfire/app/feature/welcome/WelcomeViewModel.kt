package top.mccampfire.app.feature.welcome

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.PowerManager
import androidx.annotation.StringRes
import androidx.compose.runtime.Immutable
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import top.mccampfire.app.R
import top.mccampfire.app.data.SettingsRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/** Progress of a setup check. */
enum class CheckStatus { Idle, Checking, Success, Failed }

/** Rows of the setup status page. */
enum class SetupItemId(@StringRes val labelRes: Int, val required: Boolean) {
    SystemVersion(R.string.welcome_item_system, required = true),
    Notifications(R.string.welcome_item_notifications, required = true),
    BackgroundRun(R.string.welcome_item_background, required = false),
    LocalNetwork(R.string.welcome_item_local_network, required = true),
}

/** Result of one setup row. */
@Immutable
data class SetupItem(val id: SetupItemId, val status: CheckStatus)

/** State of the welcome flow. */
@Immutable
data class WelcomeUiState(
    val accessStatus: CheckStatus = CheckStatus.Idle,
    val items: List<SetupItem> = emptyList(),
) {
    val setupReady: Boolean
        get() = items.isNotEmpty() && items.all { !it.id.required || it.status == CheckStatus.Success }

    /** Last page the user can reach. */
    val lastOpenPage: Int
        get() = when {
            accessStatus != CheckStatus.Success -> PAGE_ACCESS
            !setupReady -> PAGE_STATUS
            else -> PAGE_DONE
        }
}

/** App-specific requirements of the welcome flow. */
interface SetupChecks {
    /** Runs the blocking check of the access page. */
    suspend fun checkAccess(): Boolean

    /** Returns whether [id] is currently satisfied. */
    fun isSatisfied(id: SetupItemId): Boolean
}

/** Default checks using platform capabilities. */
class AndroidSetupChecks(
    private val context: Context,
    private val minimumSdk: Int,
) : SetupChecks {
    override suspend fun checkAccess(): Boolean = Build.VERSION.SDK_INT >= minimumSdk

    override fun isSatisfied(id: SetupItemId): Boolean = when (id) {
        SetupItemId.SystemVersion -> Build.VERSION.SDK_INT >= minimumSdk
        SetupItemId.BackgroundRun -> context.getSystemService(PowerManager::class.java)
            ?.isIgnoringBatteryOptimizations(context.packageName) == true
        SetupItemId.LocalNetwork -> top.mccampfire.app.link.LocalNetwork.isGranted(context)
        SetupItemId.Notifications -> Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED
    }
}

/** Runs the setup checks and records completion. */
class WelcomeViewModel(
    private val checks: SetupChecks,
    private val settings: SettingsRepository,
) : ViewModel() {
    private val state = MutableStateFlow(WelcomeUiState())

    val uiState: StateFlow<WelcomeUiState> = state.asStateFlow()

    /** Starts the access check when it is not already running. */
    fun runAccessCheck() {
        if (state.value.accessStatus == CheckStatus.Checking) return
        state.update { it.copy(accessStatus = CheckStatus.Checking) }
        viewModelScope.launch {
            val granted = checks.checkAccess()
            delay(MinimumCheckMillis)
            state.update {
                it.copy(accessStatus = if (granted) CheckStatus.Success else CheckStatus.Failed)
            }
        }
    }

    /** Re-reads every setup row. */
    fun refreshItems() {
        state.update { current ->
            current.copy(
                items = SetupItemId.entries.map { id ->
                    SetupItem(id, if (checks.isSatisfied(id)) CheckStatus.Success else CheckStatus.Failed)
                },
            )
        }
    }

    /** Marks the flow as completed and clears its state. */
    fun complete() {
        state.value = WelcomeUiState()
        settings.update { it.copy(welcomeCompleted = true) }
    }

    private companion object {
        const val MinimumCheckMillis = 700L
    }
}
