package top.mccampfire.app.data

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

/** Notices of the Campfire service: the home page banner and the dialogs waiting to be shown. */
class NoticeCenter(private val settings: SettingsRepository) {
    private val _banner = MutableStateFlow<Notice?>(null)
    private val _dialogs = MutableStateFlow<List<Notice>>(emptyList())
    private val shownThisRun = mutableSetOf<String>()

    /** Notice shown on the home page until it is closed. */
    val banner: StateFlow<Notice?> = _banner.asStateFlow()

    /** Dialog notices in the order they are shown. */
    val dialogs: StateFlow<List<Notice>> = _dialogs.asStateFlow()

    /** Fetches the notices; does nothing in builds without a service address or when notices are turned off. */
    suspend fun refresh(version: String, language: String) {
        val baseUrl = CampfireService.baseUrl ?: return
        if (!settings.settings.value.receiveNotices) return
        val manifest = CampfireService.fetchManifest(baseUrl) ?: return
        show(parseNotices(manifest), version, language)
    }

    /** Picks the newest banner and queues the dialogs not shown yet. */
    internal fun show(notices: List<Notice>, version: String, language: String) {
        val fresh = notices.applicable(version, language, seen()).filter { it.id !in shownThisRun }
        fresh.firstOrNull { it.isBanner }?.let { _banner.value = it }
        val dialogs = fresh.filter { !it.isBanner }
        shownThisRun += dialogs.map { it.id }
        if (dialogs.isNotEmpty()) _dialogs.update { it + dialogs }
    }

    /** The first dialog was answered. */
    fun dialogClosed(notice: Notice) {
        _dialogs.update { list -> list.filterNot { it.id == notice.id } }
        markSeen(notice)
    }

    /** The banner was closed. */
    fun bannerClosed(notice: Notice) {
        if (_banner.value?.id == notice.id) _banner.value = null
        shownThisRun += notice.id
        markSeen(notice)
    }

    private fun seen(): Set<String> =
        settings.settings.value.seenNotices.split(',').filter { it.isNotEmpty() }.toSet()

    private fun markSeen(notice: Notice) {
        if (notice.repeats) return
        settings.update { current ->
            val ids = current.seenNotices.split(',').filter { it.isNotEmpty() }
            if (notice.id in ids) current else current.copy(seenNotices = (ids + notice.id).takeLast(MaxRemembered).joinToString(","))
        }
    }

    private companion object {
        const val MaxRemembered = 100
    }
}
