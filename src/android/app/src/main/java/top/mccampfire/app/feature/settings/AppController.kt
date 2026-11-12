package top.mccampfire.app.feature.settings

import androidx.compose.runtime.Composable
import androidx.compose.runtime.IntState
import androidx.compose.runtime.Stable
import androidx.compose.runtime.State
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import com.zeroui.kit.components.SettingIconStyle
import com.zeroui.kit.navigation.PredictiveBackMode
import com.zeroui.kit.theme.GlassMode
import com.zeroui.kit.theme.MotionMode
import com.zeroui.kit.theme.UiStyle
import top.mccampfire.app.data.AppSettings
import top.mccampfire.app.data.AppearanceCode
import top.mccampfire.app.data.LinkPrefs
import com.zeroui.kit.theme.PaletteStyle

/** Read and write access to [AppSettings] for the settings pages. */
@Stable
class AppController(
    private val settingsState: State<AppSettings>,
    private val backgroundSeedState: IntState,
    private val update: ((AppSettings) -> AppSettings) -> Unit,
    private val reset: () -> Unit,
) {
    val settings: AppSettings get() = settingsState.value
    val backgroundSeed: Int get() = backgroundSeedState.intValue

    val themeMode: ThemeMode get() = settings.themeMode
    val glassMode: GlassMode get() = settings.glassMode
    val glassIntensity: Int get() = settings.glassIntensity
    val predictiveBackMode: PredictiveBackMode get() = settings.predictiveBack
    val iconStyle: SettingIconStyle get() = settings.iconStyle
    val monet: Boolean get() = settings.monet
    val monetKeyColor: Int get() = settings.monetKeyColor
    val monetPaletteStyle: PaletteStyle get() = settings.monetPaletteStyle
    val uiStyle: UiStyle get() = settings.uiStyle
    val homeBackground: String get() = settings.homeBackground
    val homeBackgroundDim: Int get() = settings.homeBackgroundDim
    val pureBlack: Boolean get() = settings.pureBlack
    val reduceMotionMode: MotionMode get() = settings.reduceMotion
    val extras: AppearanceExtras get() = settings.extras

    fun setThemeMode(value: ThemeMode) = update { it.copy(themeMode = value) }
    fun setGlassMode(value: GlassMode) = update { it.copy(glassMode = value) }
    fun setGlassIntensity(value: Int) = update { it.copy(glassIntensity = value) }
    fun setPredictiveBackMode(value: PredictiveBackMode) = update { it.copy(predictiveBack = value) }
    fun setIconStyle(value: SettingIconStyle) = update { it.copy(iconStyle = value) }
    fun setMonet(value: Boolean) = update { it.copy(monet = value) }
    fun setMonetKeyColor(value: Int) = update { it.copy(monetKeyColor = value) }
    fun setMonetPaletteStyle(value: PaletteStyle) = update { it.copy(monetPaletteStyle = value) }
    fun setUiStyle(value: UiStyle) = update { it.copy(uiStyle = value) }
    fun setHomeBackground(value: String) = update { it.copy(homeBackground = value) }
    fun setHomeBackgroundDim(value: Int) = update { it.copy(homeBackgroundDim = value) }
    fun setPureBlack(value: Boolean) = update { it.copy(pureBlack = value) }
    fun setReduceMotionMode(value: MotionMode) = update { it.copy(reduceMotion = value) }
    fun setExtras(value: AppearanceExtras) = update { it.copy(extras = value) }
    fun setLanguage(value: Int) = update { it.copy(language = value) }
    fun setCrashLog(value: Boolean) = update { it.copy(crashLog = value) }
    fun setAutoCheckUpdate(value: Boolean) = update { it.copy(autoCheckUpdate = value) }
    fun setLastUpdateCheck(value: Long) = update { it.copy(lastUpdateCheck = value) }
    fun setReceiveNotices(value: Boolean) = update { it.copy(receiveNotices = value) }
    fun setNavLayout(order: String, hidden: String) = update { it.copy(navOrder = order, navHidden = hidden) }
    fun setHomeLayout(order: String, hidden: String) = update { it.copy(homeOrder = order, homeHidden = hidden) }
    fun setLink(transform: (LinkPrefs) -> LinkPrefs) = update { it.copy(link = transform(it.link)) }
    fun replayWelcome() = update { it.copy(welcomeCompleted = false) }

    /** Restores the default appearance. */
    fun resetAppearance() = reset()

    /** Applies the appearance code in [text]; returns false when there is none. */
    fun importAppearance(text: String): Boolean {
        val next = AppearanceCode.apply(settings, text) ?: return false
        update { next }
        return true
    }
}

/** Reads one value from the settings; the caller recomposes when that value changes. */
@Composable
fun <T> AppController.observe(selector: (AppSettings) -> T): T =
    remember(this) { derivedStateOf { selector(settings) } }.value

/** Settings access for the current composition. */
val LocalAppController = staticCompositionLocalOf<AppController> {
    error("AppController is not provided")
}
