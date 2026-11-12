package top.mccampfire.app.feature.settings

import com.zeroui.kit.theme.LocalIsDark
import com.zeroui.kit.theme.isMaterial
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import top.yukonga.miuix.kmp.theme.MiuixTheme

val LocalMonetEnabled = staticCompositionLocalOf { false }

private val DefaultOkAccent = Color(0xFF36D167)
private val DefaultOkContainerLight = Color(0xFFDFFAE4)
private val DefaultOkContainerDark = Color(0xFF1A3825)

val okAccentColor: Color
    @Composable get() =
        if (LocalMonetEnabled.current) MiuixTheme.colorScheme.primary.copy(alpha = 0.8f) else DefaultOkAccent

val okContainerColor: Color
    @Composable get() = when {
        LocalMonetEnabled.current -> MiuixTheme.colorScheme.secondaryContainer
        LocalIsDark.current -> DefaultOkContainerDark
        else -> DefaultOkContainerLight
    }

/** Quiet container for neutral status cards. */
val neutralContainerColor: Color
    @Composable get() =
        if (isMaterial()) MaterialTheme.colorScheme.secondaryContainer else MiuixTheme.colorScheme.secondaryContainer
