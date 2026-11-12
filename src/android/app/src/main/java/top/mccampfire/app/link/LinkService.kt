package top.mccampfire.app.link

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.MainActivity
import top.mccampfire.app.R
import top.mccampfire.app.data.LocaleHelper
import top.mccampfire.core.link.LinkRole
import top.mccampfire.core.link.LinkStage
import top.mccampfire.core.link.LinkState
import top.mccampfire.core.link.room.PlayerKind

/** Foreground service that keeps the room running while the game is in front. */
class LinkService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var watcher: Job? = null

    private val controller: LinkController get() = (application as CampfireApp).container.link

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ActionLeave -> controller.stop()
            ActionCopy -> controller.state.value.code?.let { code ->
                getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Campfire", code.code))
            }
        }
        val state = controller.state.value
        ServiceCompat.startForeground(
            this,
            NotificationId,
            build(state),
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE else 0,
        )
        if (!state.isActive) {
            finish()
            return START_NOT_STICKY
        }
        if (watcher == null) {
            watcher = scope.launch {
                controller.state.collect { current ->
                    if (current.isActive) {
                        getSystemService(NotificationManager::class.java)?.notify(NotificationId, build(current))
                    } else {
                        finish()
                    }
                }
            }
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    private fun finish() {
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun build(state: LinkState): Notification {
        val text = LocaleHelper.wrap(this, (application as CampfireApp).container.settings.settings.value.language)
        val open = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val leave = PendingIntent.getService(
            this,
            1,
            Intent(this, LinkService::class.java).setAction(ActionLeave),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val builder = NotificationCompat.Builder(this, ChannelId)
            .setSmallIcon(R.drawable.ic_stat_campfire)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .setContentIntent(open)
        if (state.stage == LinkStage.Ready && state.role == LinkRole.Host) {
            val copy = PendingIntent.getService(
                this,
                2,
                Intent(this, LinkService::class.java).setAction(ActionCopy),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            builder.setContentTitle(text.getString(R.string.notif_hosting, state.players.size))
                .setContentText(text.getString(R.string.notif_hosting_sub, state.code?.code.orEmpty()))
                .addAction(0, text.getString(R.string.notif_copy_code), copy)
        } else if (state.stage == LinkStage.Ready && state.role == LinkRole.Guest) {
            val host = state.players.firstOrNull { it.kind == PlayerKind.Host }
            val title = state.room?.name?.ifBlank { null } ?: host?.name ?: state.code?.code.orEmpty()
            builder.setContentTitle(text.getString(R.string.notif_joined, title))
                .setContentText(text.getString(R.string.notif_joined_sub, state.gamePort, state.players.size))
        } else if (state.role == LinkRole.Host && state.step == top.mccampfire.core.link.LinkStep.World) {
            builder.setContentTitle(text.getString(R.string.notif_waiting_world))
                .setContentText(text.getString(R.string.room_world_waiting))
        } else {
            builder.setContentTitle(text.getString(R.string.notif_connecting))
                .setProgress(0, 0, true)
        }
        builder.addAction(0, text.getString(if (state.role == LinkRole.Host) R.string.notif_close else R.string.notif_leave), leave)
        return builder.build()
    }

    companion object {
        private const val NotificationId = 1001
        private const val ChannelId = "link"
        private const val ActionLeave = "top.mccampfire.app.action.LEAVE"
        private const val ActionCopy = "top.mccampfire.app.action.COPY_CODE"

        /** Creates the notification channel of the service. */
        fun createChannel(context: Context) {
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
            val text = context.applicationContext
            val channel = NotificationChannel(ChannelId, text.getString(R.string.notif_channel_link), NotificationManager.IMPORTANCE_LOW)
            channel.description = text.getString(R.string.notif_channel_link_sub)
            channel.setShowBadge(false)
            text.getSystemService(NotificationManager::class.java)?.createNotificationChannel(channel)
        }

        /** Starts the service; call while the app is in front. */
        fun start(context: Context) {
            ContextCompat.startForegroundService(context, Intent(context, LinkService::class.java))
        }
    }
}
