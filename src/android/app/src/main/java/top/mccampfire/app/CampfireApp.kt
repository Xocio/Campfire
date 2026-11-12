package top.mccampfire.app

import android.app.Application
import top.mccampfire.app.core.AppContainer
import top.mccampfire.app.data.CrashLogger
import top.mccampfire.app.link.LinkService

/** Application entry that owns the dependency container. */
class CampfireApp : Application() {
    val container: AppContainer by lazy { AppContainer(this) }

    override fun onCreate() {
        super.onCreate()
        CrashLogger.install(this, container.settings.settings.value.crashLog)
        LinkService.createChannel(this)
    }
}
