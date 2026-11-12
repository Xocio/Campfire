package top.mccampfire.app.game

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.provider.DocumentsContract.Document
import java.io.InputStream
import java.io.OutputStream

/** A file or folder inside a folder the user granted through the system file picker. */
class SafFile(
    private val context: Context,
    /** The granted tree. */
    val tree: Uri,
    val documentId: String,
    val name: String,
    val isDirectory: Boolean,
    val lastModified: Long,
    val size: Long,
) {
    val uri: Uri get() = DocumentsContract.buildDocumentUriUsingTree(tree, documentId)

    /** Files and folders directly inside this folder. */
    fun children(): List<SafFile> {
        if (!isDirectory) return emptyList()
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(tree, documentId)
        val result = mutableListOf<SafFile>()
        runCatching {
            context.contentResolver.query(childrenUri, Projection, null, null, null)?.use { cursor ->
                while (cursor.moveToNext()) result += fromCursor(context, tree, cursor)
            }
        }
        return result
    }

    /** The child named [childName], or null. */
    fun child(childName: String): SafFile? = children().firstOrNull { it.name == childName }

    /** The folder at [path] below this one, such as "config/voicechat", or null. */
    fun find(path: String): SafFile? =
        path.split('/').filter { it.isNotEmpty() }.fold(this as SafFile?) { dir, part -> dir?.child(part) }

    /** The child folder named [childName], created when missing. */
    fun directory(childName: String): SafFile? {
        child(childName)?.let { return it.takeIf { found -> found.isDirectory } }
        val created = runCatching {
            DocumentsContract.createDocument(context.contentResolver, uri, Document.MIME_TYPE_DIR, childName)
        }.getOrNull() ?: return null
        return SafFile(context, tree, DocumentsContract.getDocumentId(created), childName, true, System.currentTimeMillis(), 0)
    }

    /** Creates a file named [childName]; returns null when the provider refuses. */
    fun createFile(childName: String, mimeType: String = "application/octet-stream"): SafFile? {
        val created = runCatching {
            DocumentsContract.createDocument(context.contentResolver, uri, mimeType, childName)
        }.getOrNull() ?: return null
        return SafFile(context, tree, DocumentsContract.getDocumentId(created), childName, false, System.currentTimeMillis(), 0)
    }

    fun openInput(): InputStream? = runCatching { context.contentResolver.openInputStream(uri) }.getOrNull()

    fun openOutput(): OutputStream? = runCatching { context.contentResolver.openOutputStream(uri, "wt") }.getOrNull()

    fun rename(newName: String): Boolean =
        runCatching { DocumentsContract.renameDocument(context.contentResolver, uri, newName) != null }.getOrDefault(false)

    fun delete(): Boolean = runCatching { DocumentsContract.deleteDocument(context.contentResolver, uri) }.getOrDefault(false)

    /** The last [maxBytes] of the file as text, or null when it cannot be read. */
    fun readTail(maxBytes: Long): String? = runCatching {
        openInput()?.use { input ->
            if (size > maxBytes) input.skip(size - maxBytes)
            input.readBytes().toString(Charsets.UTF_8)
        }
    }.getOrNull()

    companion object {
        private val Projection = arrayOf(
            Document.COLUMN_DOCUMENT_ID,
            Document.COLUMN_DISPLAY_NAME,
            Document.COLUMN_MIME_TYPE,
            Document.COLUMN_LAST_MODIFIED,
            Document.COLUMN_SIZE,
        )

        /** The top folder of a granted [tree], or null when the grant is gone. */
        fun root(context: Context, tree: Uri): SafFile? {
            val id = runCatching { DocumentsContract.getTreeDocumentId(tree) }.getOrNull() ?: return null
            return document(context, tree, id) ?: SafFile(context, tree, id, id.substringAfterLast('/'), true, 0, 0)
        }

        /** The document [documentId] inside [tree], or null. */
        fun document(context: Context, tree: Uri, documentId: String): SafFile? = runCatching {
            val uri = DocumentsContract.buildDocumentUriUsingTree(tree, documentId)
            context.contentResolver.query(uri, Projection, null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) fromCursor(context, tree, cursor) else null
            }
        }.getOrNull()

        private fun fromCursor(context: Context, tree: Uri, cursor: android.database.Cursor) = SafFile(
            context = context,
            tree = tree,
            documentId = cursor.getString(0),
            name = cursor.getString(1).orEmpty(),
            isDirectory = cursor.getString(2) == Document.MIME_TYPE_DIR,
            lastModified = if (cursor.isNull(3)) 0 else cursor.getLong(3),
            size = if (cursor.isNull(4)) 0 else cursor.getLong(4),
        )
    }
}
