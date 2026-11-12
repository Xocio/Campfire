package top.mccampfire.app

import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.compose.animation.AnimatedContent
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.zeroui.kit.components.ZeroPageFrame
import com.zeroui.kit.motion.ZeroMotion
import com.zeroui.kit.navigation.ZeroNavHost
import com.zeroui.kit.navigation.page
import com.zeroui.kit.navigation.pushUnique
import com.zeroui.kit.navigation.PageTransition
import top.mccampfire.app.feature.main.MainScreen
import top.mccampfire.app.feature.settings.AboutPage
import top.mccampfire.app.feature.support.SupportPage
import top.mccampfire.app.feature.settings.AppSettingsPage
import top.mccampfire.app.feature.settings.ContributorsPage
import top.mccampfire.app.feature.settings.GlassIntensityPage
import top.mccampfire.app.feature.settings.HomeBackgroundPage
import top.mccampfire.app.feature.settings.LicensesPage
import top.mccampfire.app.feature.settings.NavTabsPage
import top.mccampfire.app.feature.settings.ThemeSettingsPage
import top.mccampfire.app.feature.welcome.WelcomeScreen
import top.mccampfire.app.navigation.AppRoute
import top.mccampfire.app.feature.room.HostPage
import top.mccampfire.app.feature.linksettings.AllowlistPage
import top.mccampfire.app.feature.linksettings.DiagnosticsPage
import top.mccampfire.app.feature.linksettings.GameFoldersPage
import top.mccampfire.app.feature.linksettings.LinkLogPage
import top.mccampfire.app.feature.room.RoomSettingsPage
import top.mccampfire.app.feature.linksettings.NodesPage
import top.mccampfire.app.feature.room.JoinPage
import top.mccampfire.app.feature.room.RoomPage
import top.mccampfire.app.feature.lobby.LobbyPublishPage
import top.mccampfire.app.feature.lobby.LobbyRoomPage
import androidx.compose.ui.platform.LocalContext
import top.yukonga.miuix.kmp.basic.SnackbarHost
import top.yukonga.miuix.kmp.basic.SnackbarHostState
import top.yukonga.miuix.kmp.nav.core.NavController
import top.yukonga.miuix.kmp.nav.core.rememberNavController
import kotlinx.coroutines.launch

/** Chooses between the welcome flow and the main navigation graph. */
@Composable
fun AppRoot(welcomeCompleted: Boolean, pageTransition: PageTransition) {
    val context = LocalContext.current
    var permitted by remember { mutableStateOf(requiredPermissionsGranted(context)) }
    LifecycleResumeEffect(Unit) {
        permitted = requiredPermissionsGranted(context)
        onPauseOrDispose { }
    }
    AnimatedContent(
        targetState = welcomeCompleted && permitted,
        transitionSpec = { ZeroMotion.crossFade() },
        label = "appRoot",
    ) { completed ->
        if (completed) AppNavGraph(pageTransition) else WelcomeScreen()
    }
}

/** True when the permissions the welcome flow requires are all granted. */
private fun requiredPermissionsGranted(context: android.content.Context): Boolean {
    val notifications = android.os.Build.VERSION.SDK_INT < android.os.Build.VERSION_CODES.TIRAMISU ||
        androidx.core.content.ContextCompat.checkSelfPermission(context, android.Manifest.permission.POST_NOTIFICATIONS) ==
        android.content.pm.PackageManager.PERMISSION_GRANTED
    return notifications && top.mccampfire.app.link.LocalNetwork.isGranted(context)
}

@Composable
private fun AppNavGraph(pageTransition: PageTransition) {
    val controller = rememberNavController<AppRoute>(AppRoute.Main)
    ZeroNavHost(
        controller = controller,
        transition = pageTransition,
    ) {
        page<AppRoute.Main>(controller) {
            MainScreen(
                isActive = controller.backStack.size == 1,
                onNavigate = { route -> controller.pushUnique(route) },
            )
        }
        page<AppRoute.Join>(controller) {
            val link = (LocalContext.current.applicationContext as CampfireApp).container.link
            SettingsPage(controller, R.string.join_title) { padding ->
                JoinPage(
                    pad = padding,
                    onJoin = { code ->
                        link.join(code)
                        controller.pop()
                        controller.pushUnique(AppRoute.Room)
                    },
                )
            }
        }
        page<AppRoute.Host>(controller) {
            val link = (LocalContext.current.applicationContext as CampfireApp).container.link
            SettingsPage(controller, R.string.host_title) { padding ->
                HostPage(
                    pad = padding,
                    onHost = { port ->
                        link.host(port)
                        controller.pop()
                        controller.pushUnique(AppRoute.Room)
                    },
                )
            }
        }
        page<AppRoute.Room>(controller) {
            val snackbar = remember { SnackbarHostState() }
            val scope = rememberCoroutineScope()
            SettingsPage(controller, R.string.room_title, snackbar) { padding ->
                RoomPage(
                    pad = padding,
                    onBack = { controller.pop() },
                    onOpenLog = { controller.pushUnique(AppRoute.LinkLog) },
                    onRoomSettings = { controller.pushUnique(AppRoute.RoomSettings) },
                    onLobbyPublish = { controller.pushUnique(AppRoute.LobbyPublish) },
                    onDiagnostics = { controller.pushUnique(AppRoute.Diagnostics) },
                    onGameFolders = { controller.pushUnique(AppRoute.GameFolders) },
                    toast = { message -> scope.launch { snackbar.showSnackbar(message) } },
                )
            }
        }
        page<AppRoute.RoomSettings>(controller) {
            val snackbar = remember { SnackbarHostState() }
            val scope = rememberCoroutineScope()
            SettingsPage(controller, R.string.room_settings, snackbar) { padding ->
                RoomSettingsPage(padding, toast = { message -> scope.launch { snackbar.showSnackbar(message) } })
            }
        }
        page<AppRoute.LobbyPublish>(controller) {
            SettingsPage(controller, R.string.lobby_publish) { padding ->
                LobbyPublishPage(padding, onRoomSettings = { controller.pushUnique(AppRoute.RoomSettings) })
            }
        }
        page<AppRoute.Allowlist>(controller) {
            SettingsPage(controller, R.string.allowlist) { padding -> AllowlistPage(padding) }
        }
        page<AppRoute.GameFolders>(controller) {
            val snackbar = remember { SnackbarHostState() }
            val scope = rememberCoroutineScope()
            SettingsPage(controller, R.string.games_title, snackbar) { padding ->
                GameFoldersPage(padding, toast = { message -> scope.launch { snackbar.showSnackbar(message) } })
            }
        }
        page<AppRoute.Diagnostics>(controller) {
            SettingsPage(controller, R.string.diag_title) { padding -> DiagnosticsPage(padding) }
        }
        page<AppRoute.LobbyRoom>(controller) { route ->
            val link = (LocalContext.current.applicationContext as CampfireApp).container.link
            val snackbar = remember { SnackbarHostState() }
            val scope = rememberCoroutineScope()
            SettingsPage(controller, R.string.lobby_room_title, snackbar) { padding ->
                LobbyRoomPage(
                    pad = padding,
                    roomId = route.id,
                    onJoin = { code ->
                        link.join(code)
                        controller.pop()
                        controller.pushUnique(AppRoute.Room)
                    },
                    toast = { message -> scope.launch { snackbar.showSnackbar(message) } },
                )
            }
        }
        page<AppRoute.Nodes>(controller) {
            SettingsPage(controller, R.string.nodes_title) { padding -> NodesPage(padding) }
        }
        page<AppRoute.LinkLog>(controller) {
            SettingsPage(controller, R.string.link_log) { padding -> LinkLogPage(padding) }
        }
        page<AppRoute.AppSettings>(controller) {
            SettingsPage(controller, R.string.app_settings) { padding ->
                AppSettingsPage(
                    pad = padding,
                    onTheme = { controller.pushUnique(AppRoute.Theme) },
                    onSupport = { controller.pushUnique(AppRoute.Support) },
                    onAbout = { controller.pushUnique(AppRoute.About) },
                )
            }
        }
        page<AppRoute.Theme>(controller) {
            SettingsPage(controller, R.string.theme_settings) { padding ->
                ThemeSettingsPage(
                    pad = padding,
                    onGlassIntensity = { controller.pushUnique(AppRoute.GlassIntensity) },
                    onNavTabs = { controller.pushUnique(AppRoute.NavTabs) },
                    onHomeBackground = { controller.pushUnique(AppRoute.HomeBackground) },
                )
            }
        }
        page<AppRoute.NavTabs>(controller) {
            SettingsPage(controller, R.string.nav_tabs_title) { padding -> NavTabsPage(padding) }
        }
        page<AppRoute.HomeBackground>(controller) {
            val snackbar = remember { SnackbarHostState() }
            val scope = rememberCoroutineScope()
            SettingsPage(controller, R.string.home_bg_title, snackbar) { padding ->
                HomeBackgroundPage(
                    pad = padding,
                    toast = { message -> scope.launch { snackbar.showSnackbar(message) } },
                )
            }
        }
        page<AppRoute.GlassIntensity>(controller) {
            SettingsPage(controller, R.string.glass_intensity) { padding -> GlassIntensityPage(padding) }
        }
        page<AppRoute.About>(controller) {
            SettingsPage(controller, R.string.about) { padding ->
                AboutPage(
                    pad = padding,
                    onLicenses = { controller.pushUnique(AppRoute.Licenses) },
                    onContributors = { controller.pushUnique(AppRoute.Contributors) },
                )
            }
        }
        page<AppRoute.Licenses>(controller) {
            SettingsPage(controller, R.string.open_source_licenses) { padding -> LicensesPage(padding) }
        }
        page<AppRoute.Contributors>(controller) {
            SettingsPage(controller, R.string.contributors_title) { padding -> ContributorsPage(padding) }
        }
        page<AppRoute.Support>(controller) {
            SettingsPage(controller, R.string.support_title) { padding -> SupportPage(padding) }
        }
    }
}

@Composable
private fun SettingsPage(
    controller: NavController,
    titleRes: Int,
    snackbar: SnackbarHostState? = null,
    content: @Composable (PaddingValues) -> Unit,
) {
    ZeroPageFrame(
        title = stringResource(titleRes),
        onBack = { controller.pop() },
        bottomInset = 96.dp,
        snackbarHost = { if (snackbar != null) SnackbarHost(snackbar) },
        content = content,
    )
}
