package top.mccampfire.app.link

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import com.zeroui.kit.theme.ZeroTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import top.mccampfire.app.CampfireApp
import top.mccampfire.app.data.CampfireService
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Faces of premium players, cut from their skins (face plus hat layer). Skins come from Mojang's public
 * profile service and are cached on disk for a day.
 */
class PlayerHeads(context: Context) {
    private val cacheDir = File(context.applicationContext.cacheDir, "heads")
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val requests = HashMap<String, Deferred<ImageBitmap?>>()

    /** Face of the account [uuid] (32 hex digits); null when it cannot be loaded. Each account is requested once per run. */
    fun face(uuid: String): Deferred<ImageBitmap?> {
        val id = uuid.lowercase()
        if (id.length != 32 || !id.all { it in '0'..'9' || it in 'a'..'f' }) return CompletableDeferred(null)
        synchronized(requests) {
            return requests.getOrPut(id) { scope.async { load(id) } }
        }
    }

    private fun load(uuid: String): ImageBitmap? {
        val cached = File(cacheDir, "$uuid.png")
        val fresh = cached.isFile && System.currentTimeMillis() - cached.lastModified() < CacheLifetimeMillis
        if (fresh) decode(cached.readBytes())?.let { return it }
        return try {
            val skinUrl = lookUpSkin(uuid) ?: return null
            val face = cutFace(download(skinUrl, MaxSkinBytes)) ?: return null
            cacheDir.mkdirs()
            cached.writeBytes(face)
            decode(face)
        } catch (e: IOException) {
            if (cached.isFile) decode(cached.readBytes()) else null
        }
    }

    private fun lookUpSkin(uuid: String): String? {
        val profile = Parser.parseToJsonElement(String(download(ProfileUrl + uuid, MaxProfileBytes), Charsets.UTF_8)) as? JsonObject
            ?: return null
        val properties = profile["properties"] as? JsonArray ?: return null
        for (property in properties.filterIsInstance<JsonObject>()) {
            if ((property["name"] as? JsonPrimitive)?.content != "textures") continue
            val encoded = (property["value"] as? JsonPrimitive)?.content ?: continue
            val textures = runCatching {
                Parser.parseToJsonElement(String(Base64.decode(encoded, Base64.DEFAULT), Charsets.UTF_8)) as? JsonObject
            }.getOrNull() ?: continue
            val skin = ((textures["textures"] as? JsonObject)?.get("SKIN") as? JsonObject)?.get("url") as? JsonPrimitive ?: continue
            val url = skin.content.replace("http://", "https://")
            return url.takeIf { it.startsWith(TextureHost) }
        }
        return null
    }

    private fun download(url: String, limit: Int): ByteArray {
        val connection = URL(url).openConnection() as HttpURLConnection
        try {
            connection.connectTimeout = TimeoutMillis
            connection.readTimeout = TimeoutMillis
            connection.instanceFollowRedirects = false
            connection.setRequestProperty("User-Agent", CampfireService.userAgent)
            if (connection.responseCode != HttpURLConnection.HTTP_OK) throw IOException("HTTP " + connection.responseCode)
            connection.inputStream.use { input ->
                val output = ByteArrayOutputStream()
                val buffer = ByteArray(8192)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    output.write(buffer, 0, read)
                    if (output.size() > limit) throw IOException("Response too large")
                }
                return output.toByteArray()
            }
        } finally {
            connection.disconnect()
        }
    }

    /** An 8×8 PNG (scaled for high-resolution skins) of the face with the hat layer drawn over it. */
    private fun cutFace(skinPng: ByteArray): ByteArray? {
        val skin = BitmapFactory.decodeByteArray(skinPng, 0, skinPng.size) ?: return null
        if (skin.width < 64 || skin.height < 32) return null
        val scale = skin.width / 64
        val size = 8 * scale
        val face = IntArray(size * size)
        val hat = IntArray(size * size)
        skin.getPixels(face, 0, size, 8 * scale, 8 * scale, size, size)
        skin.getPixels(hat, 0, size, 40 * scale, 8 * scale, size, size)
        for (i in face.indices) {
            val alpha = hat[i] ushr 24
            var color = 0xFF shl 24
            for (shift in intArrayOf(16, 8, 0)) {
                val top = (hat[i] shr shift) and 0xFF
                val bottom = (face[i] shr shift) and 0xFF
                color = color or (((top * alpha + bottom * (255 - alpha)) / 255) shl shift)
            }
            face[i] = color
        }
        val result = Bitmap.createBitmap(face, size, size, Bitmap.Config.ARGB_8888)
        return ByteArrayOutputStream().use { output ->
            result.compress(Bitmap.CompressFormat.PNG, 100, output)
            output.toByteArray()
        }
    }

    private fun decode(png: ByteArray): ImageBitmap? = BitmapFactory.decodeByteArray(png, 0, png.size)?.asImageBitmap()

    private companion object {
        const val ProfileUrl = "https://sessionserver.mojang.com/session/minecraft/profile/"
        const val TextureHost = "https://textures.minecraft.net/"
        const val CacheLifetimeMillis = 24 * 60 * 60 * 1000L
        const val TimeoutMillis = 8_000
        const val MaxProfileBytes = 64 * 1024
        const val MaxSkinBytes = 512 * 1024
        val Parser = Json { ignoreUnknownKeys = true }
    }
}

/** The face of the premium player [uuid]; a plain square while it loads or when it cannot be loaded. */
@Composable
fun PlayerHead(uuid: String, modifier: Modifier = Modifier) {
    val heads = (LocalContext.current.applicationContext as CampfireApp).container.heads
    val face by produceState<ImageBitmap?>(null, uuid) { value = heads.face(uuid).await() }
    val shaped = modifier.size(32.dp).clip(RoundedCornerShape(6.dp))
    val bitmap = face
    if (bitmap == null) {
        Box(shaped.background(ZeroTheme.colors.outline))
    } else {
        Image(
            bitmap = bitmap,
            contentDescription = null,
            contentScale = ContentScale.FillBounds,
            filterQuality = FilterQuality.None,
            modifier = shaped,
        )
    }
}

/** A start slot with the player's face, or null when the player has no premium account or skins are off. */
fun playerHeadSlot(player: top.mccampfire.core.link.room.PlayerProfile, showSkins: Boolean): (@Composable () -> Unit)? {
    val uuid = player.uuid?.takeIf { showSkins } ?: return null
    return { PlayerHead(uuid) }
}
