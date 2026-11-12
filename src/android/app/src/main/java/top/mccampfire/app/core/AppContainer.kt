package top.mccampfire.app.core

import android.content.Context
import android.os.Build
import android.util.Log
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.data.LinkIdentity
import top.mccampfire.app.data.NoticeCenter
import top.mccampfire.app.data.Telemetry
import top.mccampfire.app.link.LinkController
import top.mccampfire.app.link.PlayerHeads
import top.mccampfire.app.link.VoiceController
import top.mccampfire.app.lobby.LobbyPublisher
import top.mccampfire.app.lobby.LobbyRepository
import top.mccampfire.app.data.SettingsRepository
import top.mccampfire.app.data.SharedPreferencesSettingsStorage
import top.mccampfire.app.feature.welcome.AndroidSetupChecks
import top.mccampfire.app.feature.welcome.SetupChecks

/** Application-wide dependencies. */
class AppContainer(context: Context) {
    private val appContext = context.applicationContext

    val settings: SettingsRepository by lazy {
        SettingsRepository(SharedPreferencesSettingsStorage(appContext)) {
            Log.w("Settings", "Stored settings were unreadable; defaults loaded", it)
        }
    }
    val identity: LinkIdentity by lazy { LinkIdentity(appContext) }
    val telemetry: Telemetry by lazy { Telemetry(appContext, settings) }
    val link: LinkController by lazy { LinkController(appContext, settings, identity, telemetry) }
    val lobby: LobbyRepository by lazy { LobbyRepository(telemetry) }
    val voice: VoiceController by lazy { VoiceController(appContext, settings, link) }
    val publisher: LobbyPublisher by lazy { LobbyPublisher(link, voice, settings, identity, telemetry) }
    val heads: PlayerHeads by lazy { PlayerHeads(appContext) }
    val notices: NoticeCenter by lazy { NoticeCenter(settings) }
    val setupChecks: SetupChecks by lazy { AndroidSetupChecks(appContext, minimumSdk = Build.VERSION_CODES.N) }
}

/** Returns the [VM] of the current store owner, creating it from the [AppContainer]. */
@Composable
inline fun <reified VM : ViewModel> appViewModel(crossinline create: AppContainer.() -> VM): VM {
    val container = (LocalContext.current.applicationContext as CampfireApp).container
    return viewModel<VM>(
        factory = viewModelFactory {
            initializer { container.create() }
        },
    )
}
