package top.mccampfire.app

import android.content.Context
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.zeroui.kit.components.HighRefreshRateEffect
import com.zeroui.kit.components.LocalRefreshRatePreference
import com.zeroui.kit.theme.ZeroTheme
import top.mccampfire.app.data.HomeBackground
import top.mccampfire.app.data.LocaleHelper
import top.mccampfire.app.data.toThemeSettings
import top.mccampfire.app.feature.settings.AppController
import top.mccampfire.app.feature.settings.LocalAppController
import top.mccampfire.app.feature.settings.LocalAppearanceExtras
import top.mccampfire.app.feature.settings.LocalMonetEnabled
import top.mccampfire.app.feature.settings.languageOf
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Single activity host. */
class MainActivity : ComponentActivity() {
    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(LocaleHelper.wrap(newBase, languageOf(newBase)))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val repository = (application as CampfireApp).container.settings
        setContent {
            val settingsState = repository.settings.collectAsStateWithLifecycle()
            val settings = settingsState.value
            val backgroundSeed = remember { mutableIntStateOf(0) }
            LaunchedEffect(settings.homeBackground) {
                backgroundSeed.intValue = withContext(Dispatchers.IO) {
                    HomeBackground.cachedSeed(this@MainActivity, settings.homeBackground)
                }
            }
            HighRefreshRateEffect(settings.extras.refreshRate)
            val controller = remember(repository) {
                AppController(
                    settingsState = settingsState,
                    backgroundSeedState = backgroundSeed,
                    update = repository::update,
                    reset = repository::resetAppearance,
                )
            }
            ZeroTheme(settings.toThemeSettings(backgroundSeed.intValue)) {
                CompositionLocalProvider(
                    LocalAppController provides controller,
                    LocalAppearanceExtras provides settings.extras,
                    LocalMonetEnabled provides settings.monet,
                    LocalRefreshRatePreference provides settings.extras.refreshRate,
                ) {
                    AppRoot(
                        welcomeCompleted = settings.welcomeCompleted,
                        pageTransition = settings.extras.pageTransition,
                    )
                }
            }
        }
    }
}
