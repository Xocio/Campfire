package top.mccampfire.app.data

import androidx.compose.runtime.Immutable
import com.zeroui.kit.components.SettingIconStyle
import com.zeroui.kit.navigation.PredictiveBackMode
import com.zeroui.kit.theme.DarkMode
import com.zeroui.kit.theme.GlassMode
import com.zeroui.kit.theme.MotionMode
import com.zeroui.kit.theme.ThemeSettings
import com.zeroui.kit.theme.UiStyle
import top.mccampfire.app.feature.settings.AppearanceExtras
import top.mccampfire.app.feature.settings.KeyColorFromBackground
import top.mccampfire.app.feature.settings.ThemeMode
import kotlinx.serialization.Serializable
import com.zeroui.kit.theme.PaletteStyle

/** Persisted user settings. */
@Immutable
@Serializable
data class AppSettings(
    val welcomeCompleted: Boolean = false,
    val themeMode: ThemeMode = ThemeMode.System,
    val uiStyle: UiStyle = UiStyle.Miuix,
    val monet: Boolean = false,
    val monetKeyColor: Int = 0,
    val monetPaletteStyle: PaletteStyle = PaletteStyle.TonalSpot,
    val pureBlack: Boolean = false,
    val reduceMotion: MotionMode = MotionMode.System,
    val glassMode: GlassMode = GlassMode.Liquid,
    val glassIntensity: Int = 100,
    val predictiveBack: PredictiveBackMode = PredictiveBackMode.Custom,
    val iconStyle: SettingIconStyle = SettingIconStyle.Filled,
    val homeBackground: String = "",
    val homeBackgroundDim: Int = 40,
    val extras: AppearanceExtras = AppearanceExtras(),
    val language: Int = 0,
    val crashLog: Boolean = true,
    val autoCheckUpdate: Boolean = false,
    val lastUpdateCheck: Long = 0L,
    val receiveNotices: Boolean = true,
    /** Ids of one-time notices already shown, separated by commas. */
    val seenNotices: String = "",
    /** Usage statistics: true allowed, false declined, null not asked yet. */
    val statsConsent: Boolean? = null,
    /** Feature uses counted since the last report. */
    val statsCounts: Map<String, Int> = emptyMap(),
    /** Day of the last report, as yyyy-MM-dd. */
    val statsSentDay: String = "",
    val navOrder: String = "",
    val navHidden: String = "",
    val homeOrder: String = "",
    val homeHidden: String = "",
    val link: LinkPrefs = LinkPrefs(),
)

/** Returns a copy with default appearance values and every other field unchanged. */
fun AppSettings.withDefaultAppearance(): AppSettings {
    val defaults = AppSettings()
    return copy(
        themeMode = defaults.themeMode,
        uiStyle = defaults.uiStyle,
        monet = defaults.monet,
        monetKeyColor = defaults.monetKeyColor,
        monetPaletteStyle = defaults.monetPaletteStyle,
        pureBlack = defaults.pureBlack,
        reduceMotion = defaults.reduceMotion,
        glassMode = defaults.glassMode,
        glassIntensity = defaults.glassIntensity,
        predictiveBack = defaults.predictiveBack,
        iconStyle = defaults.iconStyle,
        homeBackground = defaults.homeBackground,
        homeBackgroundDim = defaults.homeBackgroundDim,
        extras = defaults.extras,
    )
}

/** Maps the settings to the theme input. */
fun AppSettings.toThemeSettings(backgroundSeed: Int): ThemeSettings = ThemeSettings(
    style = uiStyle,
    darkMode = when (themeMode) {
        ThemeMode.System -> DarkMode.System
        ThemeMode.Light -> DarkMode.Light
        ThemeMode.Dark -> DarkMode.Dark
    },
    dynamicColor = monet,
    seedColor = if (monetKeyColor == KeyColorFromBackground) backgroundSeed else monetKeyColor,
    palette = monetPaletteStyle,
    pureBlack = pureBlack,
    motion = reduceMotion,
    uiScale = extras.uiScale,
    glassMode = glassMode,
    glassIntensity = glassIntensity,
    listDensity = extras.listDensity,
    topBar = extras.topBar,
    topBarBlur = extras.topBarBlur,
    smoothCorners = extras.smoothCorners,
    elasticScroll = extras.elasticScroll,
    haptics = extras.haptics,
    dialogPosition = extras.dialogPosition,
    iconStyle = iconStyle,
    predictiveBack = predictiveBack,
)
