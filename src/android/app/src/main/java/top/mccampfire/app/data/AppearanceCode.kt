package top.mccampfire.app.data

import android.util.Base64
import top.mccampfire.app.feature.settings.AppearanceExtras
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** Shareable text form of the appearance settings. */
object AppearanceCode {
    private const val Prefix = "ZUI1-"
    private const val Flags = Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING
    private val Pattern = Regex("""ZUI1-[A-Za-z0-9_-]+""")
    private val json = Json {
        ignoreUnknownKeys = true
        coerceInputValues = true
    }

    @Serializable
    private data class Payload(
        val themeMode: top.mccampfire.app.feature.settings.ThemeMode,
        val uiStyle: com.zeroui.kit.theme.UiStyle,
        val monet: Boolean,
        val monetKeyColor: Int,
        val monetPaletteStyle: com.zeroui.kit.theme.PaletteStyle,
        val pureBlack: Boolean,
        val reduceMotion: com.zeroui.kit.theme.MotionMode,
        val glassMode: com.zeroui.kit.theme.GlassMode,
        val glassIntensity: Int,
        val predictiveBack: com.zeroui.kit.navigation.PredictiveBackMode,
        val iconStyle: com.zeroui.kit.components.SettingIconStyle,
        val extras: AppearanceExtras,
    )

    /** Encodes the shareable part of [settings]. */
    fun export(settings: AppSettings): String {
        val payload = Payload(
            themeMode = settings.themeMode,
            uiStyle = settings.uiStyle,
            monet = settings.monet,
            monetKeyColor = settings.monetKeyColor,
            monetPaletteStyle = settings.monetPaletteStyle,
            pureBlack = settings.pureBlack,
            reduceMotion = settings.reduceMotion,
            glassMode = settings.glassMode,
            glassIntensity = settings.glassIntensity,
            predictiveBack = settings.predictiveBack,
            iconStyle = settings.iconStyle,
            extras = settings.extras,
        )
        val bytes = json.encodeToString(Payload.serializer(), payload).toByteArray()
        return Prefix + Base64.encodeToString(bytes, Flags)
    }

    /** True when [text] contains a code. */
    fun looksLike(text: String): Boolean = Pattern.containsMatchIn(text)

    /** Applies the first code in [text] onto [current]; null when there is no valid code. */
    fun apply(current: AppSettings, text: String): AppSettings? {
        val match = Pattern.find(text)?.value ?: return null
        val payload = runCatching {
            val bytes = Base64.decode(match.removePrefix(Prefix), Flags)
            json.decodeFromString(Payload.serializer(), String(bytes))
        }.getOrNull() ?: return null
        return current.copy(
            themeMode = payload.themeMode,
            uiStyle = payload.uiStyle,
            monet = payload.monet,
            monetKeyColor = payload.monetKeyColor,
            monetPaletteStyle = payload.monetPaletteStyle,
            pureBlack = payload.pureBlack,
            reduceMotion = payload.reduceMotion,
            glassMode = payload.glassMode,
            glassIntensity = payload.glassIntensity,
            predictiveBack = payload.predictiveBack,
            iconStyle = payload.iconStyle,
            extras = payload.extras.copy(
                bgBlur = current.extras.bgBlur,
                bgCards = current.extras.bgCards,
                bgScope = current.extras.bgScope,
                refreshRate = current.extras.refreshRate,
            ),
        )
    }
}
