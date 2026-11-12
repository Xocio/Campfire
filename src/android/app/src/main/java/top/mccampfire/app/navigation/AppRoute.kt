package top.mccampfire.app.navigation

import kotlinx.serialization.Serializable
import top.yukonga.miuix.kmp.nav.core.NavKey

/** Destinations of the app navigation stack. */
@Serializable
sealed interface AppRoute : NavKey {
    @Serializable
    data object Main : AppRoute

    @Serializable
    data object AppSettings : AppRoute

    @Serializable
    data object Join : AppRoute

    @Serializable
    data object Host : AppRoute

    @Serializable
    data object Room : AppRoute

    @Serializable
    data object Nodes : AppRoute

    @Serializable
    data object LinkLog : AppRoute

    @Serializable
    data object RoomSettings : AppRoute

    @Serializable
    data object Allowlist : AppRoute

    @Serializable
    data object LobbyPublish : AppRoute

    @Serializable
    data object Diagnostics : AppRoute

    @Serializable
    data object GameFolders : AppRoute

    @Serializable
    data class LobbyRoom(val id: String) : AppRoute

    @Serializable
    data object Theme : AppRoute

    @Serializable
    data object NavTabs : AppRoute

    @Serializable
    data object HomeBackground : AppRoute

    @Serializable
    data object GlassIntensity : AppRoute

    @Serializable
    data object About : AppRoute

    @Serializable
    data object Licenses : AppRoute

    @Serializable
    data object Contributors : AppRoute

    @Serializable
    data object Support : AppRoute
}
