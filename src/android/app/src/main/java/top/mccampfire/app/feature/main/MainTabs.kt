package top.mccampfire.app.feature.main

import androidx.annotation.StringRes
import androidx.compose.ui.graphics.vector.ImageVector
import top.mccampfire.app.R
import top.mccampfire.app.data.AppSettings
import top.mccampfire.app.data.CampfireService
import top.yukonga.miuix.kmp.icon.MiuixIcons
import top.yukonga.miuix.kmp.icon.extended.GridView
import top.yukonga.miuix.kmp.icon.extended.Search
import top.yukonga.miuix.kmp.icon.extended.Settings

/** Top-level tabs; [removable] tabs can be hidden from the navigation bar. */
enum class MainTab(@StringRes val labelRes: Int, val removable: Boolean) {
    Home(R.string.tab_home, false),
    Lobby(R.string.tab_lobby, true),
    Settings(R.string.tab_settings, false),
}

/** Icon of a tab. */
fun mainTabIcon(tab: MainTab): ImageVector = when (tab) {
    MainTab.Home -> MiuixIcons.GridView
    MainTab.Lobby -> MiuixIcons.Search
    MainTab.Settings -> MiuixIcons.Settings
}

/** Visible tabs in order and hidden tabs. */
data class NavLayout(val order: List<MainTab>, val hidden: List<MainTab>)

private fun parseTabs(csv: String): List<MainTab> =
    csv.split(",").mapNotNull { name -> MainTab.entries.firstOrNull { it.name == name } }.distinct()

/** Reads the navigation layout; tabs missing from both lists return to their default position. */
fun AppSettings.navLayout(): NavLayout {
    val all = MainTab.entries.filter { it != MainTab.Lobby || CampfireService.baseUrl != null }
    val hidden = parseTabs(navHidden).filter { it.removable && it in all }
    val saved = parseTabs(navOrder).filter { it in all && it !in hidden }
    val order = saved.toMutableList()
    all.filterNot { it in saved || it in hidden }.forEach { tab ->
        order.add(all.indexOf(tab).coerceAtMost(order.size), tab)
    }
    return NavLayout(order, hidden)
}
