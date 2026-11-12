package top.mccampfire.app.data

import top.mccampfire.app.feature.settings.ThemeMode
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SettingsRepositoryTest {
    private class MemoryStorage(var value: String? = null) : SettingsStorage {
        var backup: String? = null

        override fun read(): String? = value

        override fun backup(value: String) {
            backup = value
        }

        override fun write(value: String) {
            this.value = value
        }
    }

    @Test
    fun updatesArePublishedAndPersisted() {
        val storage = MemoryStorage()
        val repository = SettingsRepository(storage)

        repository.update { it.copy(themeMode = ThemeMode.Dark) }

        assertEquals(ThemeMode.Dark, repository.settings.value.themeMode)
        assertEquals(ThemeMode.Dark, SettingsRepository(storage).settings.value.themeMode)
    }

    @Test
    fun resetAppearanceKeepsWelcomeState() {
        val repository = SettingsRepository(MemoryStorage())
        repository.update { it.copy(welcomeCompleted = true, pureBlack = true) }

        repository.resetAppearance()

        assertTrue(repository.settings.value.welcomeCompleted)
        assertFalse(repository.settings.value.pureBlack)
    }

    @Test
    fun corruptStorageIsBackedUpAndLoadsDefaults() {
        val storage = MemoryStorage("{broken")
        var cause: Throwable? = null

        val repository = SettingsRepository(storage) { cause = it }

        assertEquals(AppSettings(), repository.settings.value)
        assertEquals("{broken", storage.backup)
        assertTrue(cause != null)
    }

    @Test
    fun validStorageIsNotBackedUp() {
        val storage = MemoryStorage()
        SettingsRepository(storage).update { it.copy(pureBlack = true) }

        SettingsRepository(storage)

        assertEquals(null, storage.backup)
    }
}
