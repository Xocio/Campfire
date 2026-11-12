package top.mccampfire.app.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NoticesTest {
    private val manifest = """
        {"update":null,"notices":[
          {"id":"a","title":"Banner","body":"Hi","style":"banner","repeat":"once","level":"info",
           "minVersion":"","maxVersion":"","lang":"","buttonText":"Support","buttonUrl":"campfire://support",
           "titleColor":"#FF8800","bodyColor":""},
          {"id":"b","title":"Dialog","body":"","style":"dialog","repeat":"always","level":"warning",
           "minVersion":"0.2.0","maxVersion":"","lang":"zh","buttonText":"Open","buttonUrl":"https://example.com/x",
           "titleColor":"","bodyColor":"bad"},
          {"id":"c","title":"Maps","style":"dialog","buttonText":"Maps","buttonUrl":"campfire://maps"},
          {"id":"","title":"No id"},
          {"id":"d","title":"Plain http","buttonText":"Go","buttonUrl":"http://example.com"}
        ],"config":{}}
    """.trimIndent()

    @Test
    fun parsesNoticesAndSkipsEntriesWithoutId() {
        val notices = parseNotices(manifest)
        assertEquals(listOf("a", "b", "c", "d"), notices.map { it.id })

        val banner = notices[0]
        assertTrue(banner.isBanner)
        assertFalse(banner.repeats)
        assertEquals("support", banner.buttonPage)
        assertNull(banner.buttonUrl)
        assertTrue(banner.hasButton)
        assertEquals(0xFFFF8800, banner.titleColor)
        assertNull(banner.bodyColor)

        val dialog = notices[1]
        assertTrue(dialog.repeats)
        assertTrue(dialog.isWarning)
        assertEquals("https://example.com/x", dialog.buttonUrl)
        assertNull(dialog.buttonPage)
        assertNull(dialog.bodyColor)
    }

    @Test
    fun pagesMissingOnAndroidAndPlainHttpHaveNoButton() {
        val notices = parseNotices(manifest).associateBy { it.id }
        assertFalse(notices.getValue("c").hasButton)
        assertFalse(notices.getValue("d").hasButton)
    }

    @Test
    fun parsePageIgnoresCaseAndTrailingSlash() {
        assertEquals("about", NoticePages.parse("CAMPFIRE://About/"))
        assertNull(NoticePages.parse("campfire://unknown"))
        assertNull(NoticePages.parse("https://campfire/support"))
        assertNull(NoticePages.parse(null))
    }

    @Test
    fun filtersByVersionLanguageAndSeen() {
        val notices = parseNotices(manifest)
        assertEquals(listOf("a", "c", "d"), notices.applicable("0.1.0", "zh", emptySet()).map { it.id })
        assertEquals(listOf("a", "b", "c", "d"), notices.applicable("0.2.0", "zh", emptySet()).map { it.id })
        assertEquals(listOf("a", "c", "d"), notices.applicable("0.2.0", "en", emptySet()).map { it.id })
        assertEquals(listOf("b", "c", "d"), notices.applicable("0.2.0", "zh", setOf("a", "b")).map { it.id })
    }

    @Test
    fun maxVersionIsInclusive() {
        val notice = parseNotices("""{"notices":[{"id":"x","title":"t","maxVersion":"0.1.0"}]}""")
        assertEquals(1, notice.applicable("0.1.0", "en", emptySet()).size)
        assertEquals(0, notice.applicable("0.1.1", "en", emptySet()).size)
    }

    @Test
    fun malformedManifestGivesNoNotices() {
        assertTrue(parseNotices("not json").isEmpty())
        assertTrue(parseNotices("""{"notices":"x"}""").isEmpty())
    }

    @Test
    fun centerShowsNewestBannerQueuesDialogsAndRemembersSeen() {
        val storage = object : SettingsStorage {
            var value: String? = null
            override fun read(): String? = value
            override fun write(value: String) { this.value = value }
        }
        val settings = SettingsRepository(storage)
        val center = NoticeCenter(settings)
        val notices = parseNotices(manifest)

        center.show(notices, "0.2.0", "zh")
        assertEquals("a", center.banner.value?.id)
        assertEquals(listOf("b", "c", "d"), center.dialogs.value.map { it.id })

        center.dialogClosed(center.dialogs.value.first())
        center.dialogClosed(center.dialogs.value.first())
        center.bannerClosed(center.banner.value!!)
        assertNull(center.banner.value)
        assertEquals(listOf("d"), center.dialogs.value.map { it.id })
        assertEquals(setOf("c", "a"), settings.settings.value.seenNotices.split(',').toSet())

        center.show(notices, "0.2.0", "zh")
        assertNull(center.banner.value)
        assertEquals(listOf("d"), center.dialogs.value.map { it.id })

        val restarted = NoticeCenter(settings)
        restarted.show(notices, "0.2.0", "zh")
        assertNull(restarted.banner.value)
        assertEquals(listOf("b", "d"), restarted.dialogs.value.map { it.id })
    }
}
