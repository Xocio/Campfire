package top.mccampfire.app.resources

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Element

class StringResourcesTest {
    private val moduleRoots = listOf(File("src/main/res"))
    private val locales = listOf("values-b+zh+Hans", "values-b+zh+Hant", "values-b+yue+Hant")
    private val placeholder = Regex("%\\d+\\$[a-zA-Z]")
    private val kitRes = File("../zeroui/kit/src/main/res")

    /** Locale folders of the ZeroUI kit that supply each app locale. */
    private val kitLocales = mapOf(
        "values-b+zh+Hans" to "values-zh",
        "values-b+zh+Hant" to null,
        "values-b+yue+Hant" to null,
    )

    private fun stringsOrEmpty(file: File): Map<String, String> = if (file.exists()) strings(file) else emptyMap()

    private fun strings(file: File): Map<String, String> {
        val nodes = DocumentBuilderFactory.newInstance()
            .newDocumentBuilder()
            .parse(file)
            .getElementsByTagName("string")
        return (0 until nodes.length)
            .map { nodes.item(it) as Element }
            .filter { it.getAttribute("translatable") != "false" }
            .associate { it.getAttribute("name") to it.textContent }
    }

    @Test
    fun everyLocaleDefinesTheSameKeys() {
        for (root in moduleRoots) {
            val base = strings(File(root, "values/strings.xml"))
            assertTrue("No strings in $root", base.isNotEmpty())
            for (locale in locales) {
                val translated = strings(File(root, "$locale/strings.xml"))
                assertEquals("Keys differ in $root/$locale", base.keys, translated.keys)
            }
        }
    }

    @Test
    fun everyLocaleCoversTheKitStrings() {
        val kit = strings(File(kitRes, "values/strings.xml"))
        assertTrue("No strings in $kitRes", kit.isNotEmpty())
        val app = File("src/main/res")
        for (locale in locales) {
            val available = stringsOrEmpty(File(app, "$locale/strings.xml")).keys +
                stringsOrEmpty(File(app, "$locale/kit_strings.xml")).keys +
                (kitLocales[locale]?.let { stringsOrEmpty(File(kitRes, "$it/strings.xml")).keys } ?: emptySet())
            assertEquals("Kit strings missing in $locale", emptySet<String>(), kit.keys - available)
        }
    }

    @Test
    fun everyLocaleUsesTheSamePlaceholders() {
        for (root in moduleRoots) {
            val base = strings(File(root, "values/strings.xml"))
            for (locale in locales) {
                val translated = strings(File(root, "$locale/strings.xml"))
                for ((name, value) in base) {
                    assertEquals(
                        "Placeholders differ in $locale/$name",
                        placeholder.findAll(value).map { it.value }.sorted().toList(),
                        placeholder.findAll(translated.getValue(name)).map { it.value }.sorted().toList(),
                    )
                }
            }
        }
    }
}
