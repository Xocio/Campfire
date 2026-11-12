package top.mccampfire.app.game

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class GameFoldersTest {
    @Test
    fun loadersAreRecognized() {
        assertEquals("fabric", GameFolders.loader("""{"libraries":[{"name":"net.fabricmc:fabric-loader:0.15.11"}]}"""))
        assertEquals("quilt", GameFolders.loader("""{"libraries":[{"name":"org.quiltmc:quilt-loader:0.26.0"}]}"""))
        assertEquals("forge", GameFolders.loader("""{"libraries":[{"name":"net.minecraftforge:fmlloader:1.20.1-47.2.0"}]}"""))
        assertEquals("neoforge", GameFolders.loader("""{"libraries":[{"name":"net.neoforged.fancymodloader:loader:4.0.24"}]}"""))
        assertEquals("vanilla", GameFolders.loader("""{"id":"1.21.1"}"""))
    }

    @Test
    fun gameVersionsAreRead() {
        assertEquals("1.20.1", GameFolders.gameVersion("""{"id":"Fabric 1.20.1","inheritsFrom":"1.20.1"}""", "Fabric 1.20.1"))
        assertEquals("1.21.1", GameFolders.gameVersion("""{"id":"x","patches":[{"id":"game","version":"1.21.1"},{"id":"fabric","version":"0.16"}]}""", "x"))
        assertEquals("1.19.2", GameFolders.gameVersion("""{"id":"1.19.2-forge-43.3.0"}""", "1.19.2-forge-43.3.0"))
        assertEquals("1.21", GameFolders.gameVersion("""{"id":"1.21"}""", "1.21"))
        assertNull(GameFolders.gameVersion("""{"id":"My Pack"}""", "My Pack"))
    }
}
