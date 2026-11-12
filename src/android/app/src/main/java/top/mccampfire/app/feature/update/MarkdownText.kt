package top.mccampfire.app.feature.update

import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.foundation.text.InlineTextContent
import androidx.compose.foundation.text.appendInlineContent
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.Placeholder
import androidx.compose.ui.text.PlaceholderVerticalAlign
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.withLink
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.zeroui.kit.components.BreathingDot
import top.yukonga.miuix.kmp.theme.MiuixTheme

private const val CURSOR_ID = "zero_cursor"

@Composable
fun MarkdownText(
    markdown: String,
    modifier: Modifier = Modifier,

    bodyColor: Color = MiuixTheme.colorScheme.onSurfaceVariantSummary,

    bodySize: TextUnit = 14.sp,

    bodyLineHeight: TextUnit = 20.sp,

    paragraphGap: Dp = 6.dp,

    trailingCursor: Boolean = false,

    cursorColor: Color = MiuixTheme.colorScheme.onSurfaceVariantSummary,
) {
    val body = bodyColor
    val bodyStyle = TextStyle(color = body, fontSize = bodySize, lineHeight = bodyLineHeight)
    val strong = MiuixTheme.colorScheme.onSurface
    val link = MiuixTheme.colorScheme.primary
    val codeBg = MiuixTheme.colorScheme.surfaceContainer

    Column(modifier = modifier) {
        val lines = markdown.replace("\r\n", "\n").replace("\r", "\n").split("\n")
        var inFence = false
        val fence = StringBuilder()

        val cursorAt = if (trailingCursor) lines.indexOfLast { it.isNotBlank() } else -1

        val cursorSize = (bodySize.value * 0.55f).sp
        val cursorInline = remember(bodySize, cursorColor) {
            mapOf(
                CURSOR_ID to InlineTextContent(
                    Placeholder(cursorSize, cursorSize, PlaceholderVerticalAlign.Center)
                ) {
                    BreathingDot(modifier = Modifier.fillMaxSize(), color = cursorColor)
                }
            )
        }

        fun withCursor(content: AnnotatedString, index: Int): AnnotatedString =
            if (index != cursorAt) content
            else buildAnnotatedString {
                append(content)
                append(' ')
                appendInlineContent(CURSOR_ID, "●")
            }

        for ((lineIndex, raw) in lines.withIndex()) {
            if (raw.trimStart().startsWith("```")) {
                if (inFence) {
                    CodeBlock(fence.toString().trimEnd('\n'), codeBg, strong)
                    fence.clear()
                    inFence = false
                } else {
                    inFence = true
                    fence.clear()
                }
                continue
            }
            if (inFence) {
                fence.append(raw).append('\n')
                continue
            }

            val heading = HEADING.find(raw)

            val rule = RULE.matches(raw)
            val bullet = BULLET.find(raw)
            val ordered = ORDERED.find(raw)
            val quote = QUOTE.find(raw)
            when {
                raw.isBlank() -> Spacer(Modifier.height(paragraphGap))

                rule -> {
                    Spacer(Modifier.height(paragraphGap))
                    Box(
                        Modifier
                            .fillMaxWidth()
                            .height(1.dp)
                            .background(MiuixTheme.colorScheme.dividerLine)
                    )
                    Spacer(Modifier.height(paragraphGap))
                }

                heading != null -> {
                    val level = heading.groupValues[1].length
                    val size = when (level) {
                        1 -> bodySize * 1.35f
                        2 -> bodySize * 1.2f
                        3 -> bodySize * 1.08f
                        else -> bodySize
                    }
                    Spacer(Modifier.height(paragraphGap))
                    BasicText(
                        text = withCursor(
                            inline(heading.groupValues[2], strong, strong, codeBg, link),
                            lineIndex,
                        ),
                        style = bodyStyle.copy(
                            color = strong,
                            fontSize = size,
                            lineHeight = size * 1.4f,
                            fontWeight = FontWeight.Bold,
                        ),
                        inlineContent = cursorInline,
                    )
                    Spacer(Modifier.height(paragraphGap / 2))
                }

                quote != null -> QuoteRow(
                    content = withCursor(
                        inline(quote.groupValues[1], strong, strong, codeBg, link),
                        lineIndex,
                    ),
                    style = bodyStyle.copy(color = strong),
                    accent = MiuixTheme.colorScheme.dividerLine,
                    inlineContent = cursorInline,
                )

                bullet != null -> ListRow(
                    marker = "•",
                    content = withCursor(
                        inline(bullet.groupValues[2], body, strong, codeBg, link),
                        lineIndex,
                    ),
                    style = bodyStyle,
                    indent = indentOf(bullet.groupValues[1]),
                    inlineContent = cursorInline,
                )

                ordered != null -> ListRow(
                    marker = ordered.groupValues[2] + ".",
                    content = withCursor(
                        inline(ordered.groupValues[3], body, strong, codeBg, link),
                        lineIndex,
                    ),
                    style = bodyStyle,
                    indent = indentOf(ordered.groupValues[1]),
                    inlineContent = cursorInline,
                )

                else -> BasicText(
                    text = withCursor(inline(raw, body, strong, codeBg, link), lineIndex),
                    style = bodyStyle,
                    inlineContent = cursorInline,
                )
            }
        }

        if (inFence && fence.isNotEmpty()) CodeBlock(fence.toString().trimEnd('\n'), codeBg, strong)
    }
}

@Composable
private fun ListRow(
    marker: String,
    content: AnnotatedString,
    style: TextStyle,
    indent: Dp = 0.dp,
    inlineContent: Map<String, InlineTextContent> = mapOf(),
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = indent, top = 2.dp, bottom = 2.dp)
    ) {
        BasicText(text = AnnotatedString("$marker "), style = style)
        BasicText(
            text = content,
            style = style,
            modifier = Modifier.weight(1f),
            inlineContent = inlineContent,
        )
    }
}

private fun indentOf(leading: String): Dp {
    val spaces = leading.sumOf { if (it == '\t') 4L else 1L }.toInt()
    return (minOf(spaces / 2, 3) * 16).dp
}

@Composable
private fun QuoteRow(
    content: AnnotatedString,
    style: TextStyle,
    accent: Color,
    inlineContent: Map<String, InlineTextContent> = mapOf(),
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp)
            .height(IntrinsicSize.Min)
    ) {
        Box(
            modifier = Modifier
                .width(3.dp)
                .fillMaxHeight()
                .clip(RoundedCornerShape(2.dp))
                .background(accent)
        )
        BasicText(
            text = content,
            style = style.copy(fontWeight = FontWeight.Medium),
            modifier = Modifier
                .weight(1f)
                .padding(start = 12.dp),
            inlineContent = inlineContent,
        )
    }
}

@Composable
private fun CodeBlock(code: String, bg: Color, color: Color) {
    Spacer(Modifier.height(4.dp))
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(8.dp))
            .background(bg)
            .horizontalScroll(rememberScrollState())
            .padding(horizontal = 12.dp, vertical = 8.dp)
    ) {
        BasicText(
            text = AnnotatedString(code),
            style = TextStyle(color = color, fontSize = 13.sp, fontFamily = FontFamily.Monospace)
        )
    }
    Spacer(Modifier.height(4.dp))
}

private val HEADING = Regex("^\\s{0,3}(#{1,6})\\s+(.*)$")

private val BULLET = Regex("^([ \\t]*)[-*+]\\s+(.*)$")
private val ORDERED = Regex("^([ \\t]*)(\\d+)[.)]\\s+(.*)$")

private val QUOTE = Regex("^\\s{0,3}>\\s?(.*)$")

private val RULE = Regex("^\\s{0,3}([-*_])(\\s*\\1){2,}\\s*$")

private fun inline(
    text: String,
    base: Color,
    strong: Color,
    codeBg: Color,
    link: Color,
): AnnotatedString =
    buildAnnotatedString {
        var i = 0
        while (i < text.length) {
            val c = text[i]
            when {
                text.startsWith("***", i) -> {
                    val end = text.indexOf("***", i + 3)
                    if (end > i + 2) {
                        withStyle(
                            SpanStyle(
                                fontWeight = FontWeight.Bold,
                                fontStyle = FontStyle.Italic,
                                color = strong,
                            )
                        ) {
                            append(text.substring(i + 3, end))
                        }
                        i = end + 3
                    } else { append(c); i++ }
                }

                text.startsWith("**", i) -> {
                    val end = text.indexOf("**", i + 2)
                    if (end > i + 1) {
                        withStyle(SpanStyle(fontWeight = FontWeight.Bold, color = strong)) {
                            append(text.substring(i + 2, end))
                        }
                        i = end + 2
                    } else { append(c); i++ }
                }

                text.startsWith("~~", i) -> {
                    val end = text.indexOf("~~", i + 2)
                    if (end > i + 1) {
                        withStyle(SpanStyle(textDecoration = TextDecoration.LineThrough)) {
                            append(text.substring(i + 2, end))
                        }
                        i = end + 2
                    } else { append(c); i++ }
                }

                c == '`' -> {
                    val end = text.indexOf('`', i + 1)
                    if (end > i) {
                        withStyle(SpanStyle(fontFamily = FontFamily.Monospace, background = codeBg)) {
                            append(text.substring(i + 1, end))
                        }
                        i = end + 1
                    } else { append(c); i++ }
                }

                c == '[' -> {
                    val close = text.indexOf(']', i + 1)
                    val open = if (close in 0 until text.length - 1 && text[close + 1] == '(') close + 1 else -1
                    val paren = if (open > 0) text.indexOf(')', open + 1) else -1
                    if (paren > 0) {
                        val label = text.substring(i + 1, close)
                        val url = text.substring(open + 1, paren)
                        withLink(
                            LinkAnnotation.Url(
                                url,
                                TextLinkStyles(SpanStyle(color = link, textDecoration = TextDecoration.Underline))
                            )
                        ) { append(label) }
                        i = paren + 1
                    } else { append(c); i++ }
                }

                c == '*' -> {
                    val end = text.indexOf(c, i + 1)
                    if (end > i + 1) {
                        withStyle(SpanStyle(fontStyle = FontStyle.Italic)) {
                            append(text.substring(i + 1, end))
                        }
                        i = end + 1
                    } else { append(c); i++ }
                }

                c == '_' -> {
                    val end = text.indexOf(c, i + 1)
                    val before = text.getOrNull(i - 1)
                    val after = text.getOrNull(end + 1)
                    val boundaryOk = (before == null || !before.isLetterOrDigit()) &&
                        (after == null || !after.isLetterOrDigit())
                    if (end > i + 1 && boundaryOk) {
                        withStyle(SpanStyle(fontStyle = FontStyle.Italic)) {
                            append(text.substring(i + 1, end))
                        }
                        i = end + 1
                    } else { append(c); i++ }
                }
                else -> { append(c); i++ }
            }
        }
    }
