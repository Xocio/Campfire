package top.mccampfire.app.data

import com.zeroui.kit.navigation.PageTransition
import com.zeroui.kit.navigation.PredictiveBackMode
import com.zeroui.kit.theme.GlassMode
import com.zeroui.kit.theme.MotionMode
import com.zeroui.kit.theme.UiStyle
import top.mccampfire.app.feature.settings.AppearanceExtras
import top.mccampfire.app.feature.settings.ThemeMode
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import com.zeroui.kit.theme.PaletteStyle

class SettingsCodecTest {
    private val sample = AppSettings(
        welcomeCompleted = true,
        themeMode = ThemeMode.Dark,
        uiStyle = UiStyle.Material,
        monet = true,
        monetKeyColor = 0xFF00A87E.toInt(),
        monetPaletteStyle = PaletteStyle.Vibrant,
        pureBlack = true,
        reduceMotion = MotionMode.Reduced,
        glassMode = GlassMode.Liquid,
        glassIntensity = 150,
        predictiveBack = PredictiveBackMode.Off,
        extras = AppearanceExtras(uiScale = 90, pageTransition = PageTransition.SlideFade),
    )

    @Test
    fun encodedSettingsDecodeToTheSameValue() {
        assertEquals(sample, SettingsCodec.decode(SettingsCodec.encode(sample)))
    }

    @Test
    fun missingOrMalformedInputUsesDefaults() {
        assertEquals(AppSettings(), SettingsCodec.decode(null))
        assertEquals(AppSettings(), SettingsCodec.decode("not json"))
        assertEquals(AppSettings(), SettingsCodec.decode("[1, 2]"))
    }

    @Test
    fun unknownValuesFallBackPerField() {
        val decoded = SettingsCodec.decode("""{"welcomeCompleted":true,"uiStyle":"Removed","unknownKey":1}""")

        assertTrue(decoded.welcomeCompleted)
        assertEquals(UiStyle.Miuix, decoded.uiStyle)
    }

    @Test
    fun defaultAppearanceKeepsOtherFields() {
        val reset = sample.copy(language = 2).withDefaultAppearance()

        assertTrue(reset.welcomeCompleted)
        assertEquals(2, reset.language)
        assertEquals(AppSettings().uiStyle, reset.uiStyle)
        assertEquals(AppSettings().extras, reset.extras)
    }

    @Test
    fun appearanceCodeDetectsAndRejectsText() {
        assertTrue(AppearanceCode.looksLike("Try ZUI1-abc_d"))
        assertNull(AppearanceCode.apply(AppSettings(), "no code here"))
        assertNotNull(AppearanceCode)
    }

    @Test
    fun encodedTextCarriesTheCurrentVersion() {
        assertTrue(SettingsCodec.encode(sample).contains("\"schemaVersion\":${SettingsCodec.CurrentVersion}"))
    }

    @Test
    fun unversionedTextIsMigratedAndDecoded() {
        val decoded = SettingsCodec.decode("""{"welcomeCompleted":true,"glassIntensity":120}""")

        assertTrue(decoded.welcomeCompleted)
        assertEquals(120, decoded.glassIntensity)
    }

    @Test
    fun newerVersionIsDecodedWithoutReset() {
        val decoded = SettingsCodec.decode("""{"schemaVersion":99,"welcomeCompleted":true}""")

        assertTrue(decoded.welcomeCompleted)
    }

    @Test
    fun undecodableTextIsReportedOnce() {
        val reported = mutableListOf<String>()

        val decoded = SettingsCodec.decode("{broken") { raw, _ -> reported += raw }

        assertEquals(AppSettings(), decoded)
        assertEquals(listOf("{broken"), reported)
    }

    @Test
    fun blankTextIsNotReported() {
        var reported = false

        SettingsCodec.decode("  ") { _, _ -> reported = true }
        SettingsCodec.decode(null) { _, _ -> reported = true }

        assertFalse(reported)
    }
}
