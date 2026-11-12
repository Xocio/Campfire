package top.mccampfire.app.feature.welcome

import top.mccampfire.app.data.SettingsRepository
import top.mccampfire.app.data.SettingsStorage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class WelcomeViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    private class FakeChecks(
        var access: Boolean = true,
        var satisfied: Set<SetupItemId> = SetupItemId.entries.toSet(),
    ) : SetupChecks {
        override suspend fun checkAccess(): Boolean = access

        override fun isSatisfied(id: SetupItemId): Boolean = id in satisfied
    }

    private object EmptyStorage : SettingsStorage {
        override fun read(): String? = null

        override fun write(value: String) = Unit
    }

    @Before
    fun setUp() {
        Dispatchers.setMain(dispatcher)
    }

    @After
    fun tearDown() {
        Dispatchers.resetMain()
    }

    @Test
    fun accessCheckMovesThroughCheckingToItsResult() = runTest(dispatcher) {
        val viewModel = WelcomeViewModel(FakeChecks(access = false), SettingsRepository(EmptyStorage))

        viewModel.runAccessCheck()
        assertEquals(CheckStatus.Checking, viewModel.uiState.value.accessStatus)

        advanceUntilIdle()
        assertEquals(CheckStatus.Failed, viewModel.uiState.value.accessStatus)
    }

    @Test
    fun optionalItemsDoNotBlockTheFlow() {
        val checks = FakeChecks(satisfied = setOf(SetupItemId.SystemVersion, SetupItemId.Notifications, SetupItemId.LocalNetwork))
        val viewModel = WelcomeViewModel(checks, SettingsRepository(EmptyStorage))

        viewModel.refreshItems()

        assertTrue(viewModel.uiState.value.setupReady)
    }

    @Test
    fun missingPermissionsBlockTheFlow() {
        val checks = FakeChecks(satisfied = setOf(SetupItemId.SystemVersion, SetupItemId.Notifications, SetupItemId.BackgroundRun))
        val viewModel = WelcomeViewModel(checks, SettingsRepository(EmptyStorage))

        viewModel.refreshItems()

        assertFalse(viewModel.uiState.value.setupReady)
    }

    @Test
    fun requiredItemBlocksTheFlow() {
        val checks = FakeChecks(satisfied = emptySet())
        val viewModel = WelcomeViewModel(checks, SettingsRepository(EmptyStorage))

        viewModel.refreshItems()

        assertFalse(viewModel.uiState.value.setupReady)
    }

    @Test
    fun lastOpenPageFollowsCheckResults() {
        assertEquals(PAGE_ACCESS, WelcomeUiState().lastOpenPage)
        assertEquals(
            PAGE_STATUS,
            WelcomeUiState(accessStatus = CheckStatus.Success).lastOpenPage,
        )
        assertEquals(
            PAGE_DONE,
            WelcomeUiState(
                accessStatus = CheckStatus.Success,
                items = listOf(SetupItem(SetupItemId.SystemVersion, CheckStatus.Success)),
            ).lastOpenPage,
        )
    }

    @Test
    fun completeStoresTheResultAndClearsState() {
        val repository = SettingsRepository(EmptyStorage)
        val viewModel = WelcomeViewModel(FakeChecks(), repository)
        viewModel.refreshItems()

        viewModel.complete()

        assertTrue(repository.settings.value.welcomeCompleted)
        assertEquals(WelcomeUiState(), viewModel.uiState.value)
    }
}
