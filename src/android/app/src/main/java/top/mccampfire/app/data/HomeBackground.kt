package top.mccampfire.app.data

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.ImageDecoder
import android.net.Uri
import android.os.Build
import com.materialkolor.quantize.QuantizerCelebi
import com.materialkolor.score.Score
import java.io.File
import java.io.InputStream
import java.security.MessageDigest
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

object HomeBackground {
    private const val DIR = "home_bg"
    private const val PREFIX = "bg_"

    const val MAX_HISTORY = 12

    private fun dir(context: Context): File = File(context.filesDir, DIR)

    fun file(context: Context, name: String): File? {
        if (name.isEmpty() || name.contains('/')) return null
        return File(dir(context), name).takeIf { it.isFile }
    }

    private val seedCache = java.util.concurrent.ConcurrentHashMap<String, Int>()

    fun cachedSeed(context: Context, name: String): Int {
        if (name.isEmpty()) return 0
        return seedCache.getOrPut(name) { seedColor(context, name) }
    }

    private fun seedColor(context: Context, name: String): Int {
        val src = file(context, name)?.let { decode(it, maxSide = 256) } ?: return 0
        val scale = 128f / max(src.width, src.height)
        val bmp = if (scale < 1f) {
            Bitmap.createScaledBitmap(
                src,
                (src.width * scale).roundToInt().coerceAtLeast(1),
                (src.height * scale).roundToInt().coerceAtLeast(1),
                true
            )
        } else src
        val pixels = IntArray(bmp.width * bmp.height)
        bmp.getPixels(pixels, 0, bmp.width, 0, 0, bmp.width, bmp.height)
        val counts = QuantizerCelebi.quantize(pixels, 128)
        val seed = Score.score(counts, desired = 1, fallbackColorArgb = null).firstOrNull()
            ?: Score.score(counts, desired = 1, fallbackColorArgb = null, filter = false).firstOrNull()
            ?: return 0

        return seed or 0xFF000000.toInt()
    }

    fun history(context: Context): List<File> =
        dir(context).listFiles { f -> f.isFile && f.name.startsWith(PREFIX) }
            ?.sortedByDescending { it.lastModified() }
            .orEmpty()

    fun import(context: Context, uri: Uri, keep: String): String? {
        val resolver = context.contentResolver
        return save(context, keep) {
            decodeForScreen(context, { ImageDecoder.createSource(resolver, uri) }) { resolver.openInputStream(uri) }
        }
    }

    fun importWallpaper(context: Context, raw: File, keep: String): String? {
        val digest = runCatching {
            val md = MessageDigest.getInstance("SHA-1")
            raw.inputStream().use { input ->
                val buf = ByteArray(64 * 1024)
                while (true) {
                    val n = input.read(buf)
                    if (n < 0) break
                    md.update(buf, 0, n)
                }
            }
            md.digest().take(8).joinToString("") { "%02x".format(it) }
        }.getOrNull() ?: return null
        val tag = "${PREFIX}wp_$digest"
        history(context).firstOrNull { it.name.substringBeforeLast('.') == tag }?.let {
            touch(context, it.name)
            return it.name
        }
        return save(context, keep, tag) {
            decodeForScreen(context, { ImageDecoder.createSource(raw) }) { raw.inputStream() }
        }
    }

    private fun save(
        context: Context,
        keep: String,
        tag: String = PREFIX + System.currentTimeMillis(),
        decode: () -> Bitmap?,
    ): String? {
        val bitmap = runCatching { decode() }.getOrNull() ?: return null

        val png = bitmap.hasAlpha()
        val name = tag + if (png) ".png" else ".jpg"
        val target = File(dir(context).apply { mkdirs() }, name)
        val ok = runCatching {
            target.outputStream().use {
                bitmap.compress(if (png) Bitmap.CompressFormat.PNG else Bitmap.CompressFormat.JPEG, 92, it)
            }
        }.getOrDefault(false)
        bitmap.recycle()
        if (!ok) {
            target.delete()
            return null
        }
        trim(context, keep = setOf(keep, name))
        return name
    }

    fun touch(context: Context, name: String) {
        file(context, name)?.setLastModified(System.currentTimeMillis())
    }

    fun delete(context: Context, name: String) {
        file(context, name)?.delete()
    }

    private fun trim(context: Context, keep: Set<String>) {
        history(context).drop(MAX_HISTORY).filterNot { it.name in keep }.forEach { it.delete() }
    }

    fun blurred(src: Bitmap, level: Int): Bitmap {
        val (factor, radiusFrac) = when (level) {
            1 -> 4 to 0.012f
            2 -> 6 to 0.03f
            else -> 8 to 0.06f
        }
        val w = (src.width / factor).coerceAtLeast(1)
        val h = (src.height / factor).coerceAtLeast(1)
        val small = Bitmap.createScaledBitmap(src, w, h, true)
        val radius = (src.width * radiusFrac / factor).roundToInt().coerceAtLeast(1)
        val pixels = IntArray(w * h)
        small.getPixels(pixels, 0, w, 0, 0, w, h)
        if (small !== src) small.recycle()
        val tmp = IntArray(w * h)
        repeat(3) {
            boxBlur(pixels, tmp, w, h, radius, horizontal = true)
            boxBlur(tmp, pixels, w, h, radius, horizontal = false)
        }
        return Bitmap.createBitmap(pixels, w, h, Bitmap.Config.ARGB_8888)
    }

    private fun boxBlur(src: IntArray, dst: IntArray, w: Int, h: Int, r: Int, horizontal: Boolean) {
        val len = if (horizontal) w else h
        val lines = if (horizontal) h else w
        val step = if (horizontal) 1 else w
        val div = 2 * r + 1
        for (line in 0 until lines) {
            val base = if (horizontal) line * w else line
            fun at(i: Int) = src[base + i.coerceIn(0, len - 1) * step]
            var a = 0
            var red = 0
            var g = 0
            var b = 0
            for (i in -r..r) {
                val p = at(i)
                a += p ushr 24; red += (p shr 16) and 0xFF; g += (p shr 8) and 0xFF; b += p and 0xFF
            }
            for (i in 0 until len) {
                dst[base + i * step] = ((a / div) shl 24) or ((red / div) shl 16) or ((g / div) shl 8) or (b / div)
                val out = at(i - r)
                val inp = at(i + r + 1)
                a += (inp ushr 24) - (out ushr 24)
                red += ((inp shr 16) and 0xFF) - ((out shr 16) and 0xFF)
                g += ((inp shr 8) and 0xFF) - ((out shr 8) and 0xFF)
                b += (inp and 0xFF) - (out and 0xFF)
            }
        }
    }

    fun decode(file: File, maxSide: Int = 0): Bitmap? = runCatching {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.path, bounds)
        var sample = 1
        while (maxSide > 0 && max(bounds.outWidth, bounds.outHeight) / (sample * 2) >= maxSide) sample *= 2
        BitmapFactory.decodeFile(file.path, BitmapFactory.Options().apply { inSampleSize = sample })
    }.getOrNull()

    private fun decodeForScreen(
        context: Context,
        source: () -> ImageDecoder.Source,
        open: () -> InputStream?,
    ): Bitmap? {
        val dm = context.resources.displayMetrics
        val screenW = min(dm.widthPixels, dm.heightPixels).toFloat()
        val screenH = max(dm.widthPixels, dm.heightPixels).toFloat()
        fun scaleFor(w: Int, h: Int) = min(1f, max(screenW / w, screenH / h))

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            return ImageDecoder.decodeBitmap(source()) { decoder, info, _ ->
                val w = info.size.width
                val h = info.size.height
                val s = scaleFor(w, h)
                decoder.setTargetSize((w * s).roundToInt().coerceAtLeast(1), (h * s).roundToInt().coerceAtLeast(1))

                decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
            }
        }

        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        open()?.use { BitmapFactory.decodeStream(it, null, bounds) }
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
        val s = scaleFor(bounds.outWidth, bounds.outHeight)
        var sample = 1
        while (s * sample * 2 <= 1f) sample *= 2
        val raw = open()?.use {
            BitmapFactory.decodeStream(it, null, BitmapFactory.Options().apply { inSampleSize = sample })
        } ?: return null
        val w = (bounds.outWidth * s).roundToInt().coerceAtLeast(1)
        val h = (bounds.outHeight * s).roundToInt().coerceAtLeast(1)
        if (raw.width == w && raw.height == h) return raw
        return Bitmap.createScaledBitmap(raw, w, h, true).also { if (it !== raw) raw.recycle() }
    }
}
