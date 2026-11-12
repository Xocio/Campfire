package top.mccampfire.app.feature.settings

import android.content.Context
import androidx.annotation.StringRes
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.res.stringResource
import com.zeroui.kit.components.HighRefreshRate
import com.zeroui.kit.navigation.PageTransition
import com.zeroui.kit.theme.CardMaterial
import com.zeroui.kit.theme.DialogPosition
import com.zeroui.kit.theme.ListDensity
import com.zeroui.kit.theme.TopBarStyle
import top.mccampfire.app.R
import kotlinx.serialization.Serializable
import java.util.Calendar

/** Theme mode selection. */
enum class ThemeMode(@StringRes val labelRes: Int) {
    System(R.string.follow_system),
    Light(R.string.theme_light),
    Dark(R.string.theme_dark),
}

/** Label display of the docked navigation bar. */
enum class NavLabelMode(@StringRes val labelRes: Int) {
    IconAndText(R.string.nav_labels_all),
    SelectedOnly(R.string.nav_labels_selected),
    IconOnly(R.string.nav_labels_none),
}

/** Title of the home top bar. */
enum class HomeTitle(@StringRes val labelRes: Int) {
    AppName(R.string.home_title_app_name),
    Greeting(R.string.home_title_greeting),
    Hidden(R.string.home_title_hidden),
}

/** Navigation layout on wide windows. */
enum class WideNav(@StringRes val labelRes: Int) {
    Rail(R.string.wide_nav_rail),
    Bottom(R.string.wide_nav_bottom),
}

/** Pages that show the background image. */
enum class BackgroundScope(@StringRes val labelRes: Int) {
    Home(R.string.home_bg_scope_home),
    All(R.string.home_bg_scope_all),
}

/** Range of the UI scale in percent. */
val UiScaleRange = 80..110

/** Snap points of the UI scale slider. */
val UiScaleKeyPoints = listOf(80, 90, 100, 110)

/** Number of background blur levels. */
const val BackgroundBlurLevels = 4

/** Key color value that follows the background image. */
const val KeyColorFromBackground: Int = 1

/** Secondary appearance settings. */
@Immutable
@Serializable
data class AppearanceExtras(
    val navLabels: NavLabelMode = NavLabelMode.IconAndText,
    val uiScale: Int = 100,
    val smoothCorners: Boolean = true,
    val pageTransition: PageTransition = PageTransition.Slide,
    val haptics: Boolean = true,
    val listDensity: ListDensity = ListDensity.Standard,
    val bgBlur: Int = 0,
    val bgCards: CardMaterial = CardMaterial.Solid,
    val bgScope: BackgroundScope = BackgroundScope.Home,
    val topBar: TopBarStyle = TopBarStyle.Large,
    val topBarBlur: Boolean = false,
    val elasticScroll: Boolean = true,
    val homeTitle: HomeTitle = HomeTitle.AppName,
    val wideNav: WideNav = WideNav.Rail,
    val dialogPosition: DialogPosition = DialogPosition.Bottom,
    val refreshRate: Int = HighRefreshRate.MAX,
)

/** Current secondary appearance settings. */
val LocalAppearanceExtras = staticCompositionLocalOf { AppearanceExtras() }

/** Title text for the home top bar. */
@Composable
fun homeTitleText(appName: String): String = when (LocalAppearanceExtras.current.homeTitle) {
    HomeTitle.AppName -> appName
    HomeTitle.Hidden -> ""
    HomeTitle.Greeting -> stringResource(
        when (Calendar.getInstance().get(Calendar.HOUR_OF_DAY)) {
            in 5..10 -> R.string.greeting_morning
            in 11..12 -> R.string.greeting_noon
            in 13..17 -> R.string.greeting_afternoon
            else -> R.string.greeting_evening
        },
    )
}

/** Reads the language setting without composition. */
fun languageOf(context: Context): Int =
    (context.applicationContext as top.mccampfire.app.CampfireApp).container.settings.settings.value.language
