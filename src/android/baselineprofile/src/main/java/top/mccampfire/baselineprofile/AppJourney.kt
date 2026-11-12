package top.mccampfire.baselineprofile

import androidx.benchmark.macro.MacrobenchmarkScope
import androidx.test.uiautomator.By
import androidx.test.uiautomator.Until

/** Package of the app under test. */
const val TargetPackage = "top.mccampfire.app"

private const val WaitMillis = 5_000L
private const val WelcomeSteps = 10
private const val WelcomeStepMillis = 1_200L
private const val SwipeSteps = 30

/** Reads a string of the target app by resource name. */
private fun MacrobenchmarkScope.targetString(name: String): String {
    val resources = instrumentation.context.packageManager.getResourcesForApplication(TargetPackage)
    return resources.getString(resources.getIdentifier(name, "string", TargetPackage))
}

/** Clicks through the welcome flow when it is showing. */
fun MacrobenchmarkScope.passWelcome() {
    val next = targetString("action_next")
    val start = targetString("action_get_started")
    val settings = targetString("app_settings")
    device.executeShellCommand("pm grant $TargetPackage android.permission.POST_NOTIFICATIONS")
    device.executeShellCommand("pm grant $TargetPackage android.permission.ACCESS_LOCAL_NETWORK")
    repeat(WelcomeSteps) {
        if (device.hasObject(By.desc(settings))) return
        val button = device.findObject(By.text(start)) ?: device.findObject(By.text(next))
        button?.click()
        device.waitForIdle()
        Thread.sleep(WelcomeStepMillis)
    }
    device.wait(Until.hasObject(By.desc(settings)), WaitMillis)
}

/** Swipes across the main tabs and back. */
fun MacrobenchmarkScope.swipeTabs() {
    val width = device.displayWidth
    val y = device.displayHeight / 2
    repeat(2) {
        device.swipe(width * 4 / 5, y, width / 5, y, SwipeSteps)
        device.waitForIdle()
    }
    repeat(2) {
        device.swipe(width / 5, y, width * 4 / 5, y, SwipeSteps)
        device.waitForIdle()
    }
    device.findObject(By.text(targetString("tab_home")))?.click()
    device.waitForIdle()
}

/** Opens app settings and the theme page, scrolls it, then returns to the main page. */
fun MacrobenchmarkScope.visitSettings() {
    val settings = device.wait(Until.findObject(By.desc(targetString("app_settings"))), WaitMillis) ?: return
    settings.click()
    val theme = device.wait(Until.findObject(By.text(targetString("theme_settings"))), WaitMillis)
    if (theme != null) {
        theme.click()
        device.waitForIdle()
        val x = device.displayWidth / 2
        val top = device.displayHeight / 4
        val bottom = device.displayHeight * 3 / 4
        repeat(2) {
            device.swipe(x, bottom, x, top, SwipeSteps)
            device.waitForIdle()
        }
        device.swipe(x, top, x, bottom, SwipeSteps)
        device.waitForIdle()
        device.pressBack()
        device.waitForIdle()
    }
    device.pressBack()
    device.waitForIdle()
}
