using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Campfire.Link
{
    /// <summary>
    /// Minimal JSON reader and writer for the small messages multiplayer exchanges.
    /// </summary>
    /// <remarks>
    /// Objects become <see cref="Dictionary{TKey,TValue}"/> of string to object, arrays become <c>object[]</c>,
    /// numbers become <see cref="double"/>; strings, booleans and null map to themselves.
    /// </remarks>
    internal static class Json
    {
        private const int MaxDepth = 64;

        /// <summary>Parses <paramref name="text"/>; throws <see cref="FormatException"/> on malformed input.</summary>
        public static object Parse(string text)
        {
            int position = 0;
            object value = ReadValue(text, ref position, 0);
            SkipWhitespace(text, ref position);
            if (position != text.Length)
                throw new FormatException("Unexpected text after JSON value.");
            return value;
        }

        /// <summary>Parses <paramref name="text"/>, or returns <c>null</c> when it is not valid JSON.</summary>
        public static object TryParse(string text)
        {
            try
            {
                return text == null ? null : Parse(text);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static string Serialize(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        // ======================== Reading ========================

        private static object ReadValue(string text, ref int position, int depth)
        {
            if (depth > MaxDepth)
                throw new FormatException("JSON is nested too deeply.");

            SkipWhitespace(text, ref position);
            if (position >= text.Length)
                throw new FormatException("Unexpected end of JSON.");

            char c = text[position];
            switch (c)
            {
                case '{':
                    return ReadObject(text, ref position, depth);
                case '[':
                    return ReadArray(text, ref position, depth);
                case '"':
                    return ReadString(text, ref position);
                case 't':
                    Expect(text, ref position, "true");
                    return true;
                case 'f':
                    Expect(text, ref position, "false");
                    return false;
                case 'n':
                    Expect(text, ref position, "null");
                    return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ReadNumber(text, ref position);
                    throw new FormatException("Unexpected character '" + c + "' in JSON.");
            }
        }

        private static Dictionary<string, object> ReadObject(string text, ref int position, int depth)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            position++;
            SkipWhitespace(text, ref position);
            if (Peek(text, position) == '}')
            {
                position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(text, ref position);
                if (Peek(text, position) != '"')
                    throw new FormatException("Expected a property name in JSON.");
                string key = ReadString(text, ref position);
                SkipWhitespace(text, ref position);
                if (Peek(text, position) != ':')
                    throw new FormatException("Expected ':' in JSON.");
                position++;
                result[key] = ReadValue(text, ref position, depth + 1);

                SkipWhitespace(text, ref position);
                char next = Peek(text, position);
                position++;
                if (next == '}')
                    return result;
                if (next != ',')
                    throw new FormatException("Expected ',' or '}' in JSON.");
            }
        }

        private static object[] ReadArray(string text, ref int position, int depth)
        {
            var result = new List<object>();
            position++;
            SkipWhitespace(text, ref position);
            if (Peek(text, position) == ']')
            {
                position++;
                return result.ToArray();
            }

            while (true)
            {
                result.Add(ReadValue(text, ref position, depth + 1));
                SkipWhitespace(text, ref position);
                char next = Peek(text, position);
                position++;
                if (next == ']')
                    return result.ToArray();
                if (next != ',')
                    throw new FormatException("Expected ',' or ']' in JSON.");
            }
        }

        private static string ReadString(string text, ref int position)
        {
            var builder = new StringBuilder();
            position++;
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"')
                    return builder.ToString();
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (position >= text.Length)
                    break;
                char escape = text[position++];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escape);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (position + 4 > text.Length
                            || !int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw new FormatException("Invalid unicode escape in JSON.");
                        builder.Append((char)code);
                        position += 4;
                        break;
                    default:
                        throw new FormatException("Invalid escape in JSON.");
                }
            }
            throw new FormatException("Unterminated string in JSON.");
        }

        private static double ReadNumber(string text, ref int position)
        {
            int start = position;
            while (position < text.Length && "+-0123456789.eE".IndexOf(text[position]) >= 0)
                position++;
            if (!double.TryParse(text.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException("Invalid number in JSON.");
            return value;
        }

        private static void Expect(string text, ref int position, string literal)
        {
            if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
                throw new FormatException("Invalid literal in JSON.");
            position += literal.Length;
        }

        private static char Peek(string text, int position)
        {
            return position < text.Length ? text[position] : '\0';
        }

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
        }

        // ======================== Writing ========================

        private static void Write(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case IDictionary<string, object> map:
                    builder.Append('{');
                    bool first = true;
                    foreach (var pair in map)
                    {
                        if (!first)
                            builder.Append(',');
                        first = false;
                        WriteString(builder, pair.Key);
                        builder.Append(':');
                        Write(builder, pair.Value);
                    }
                    builder.Append('}');
                    break;
                case IEnumerable list:
                    builder.Append('[');
                    bool firstItem = true;
                    foreach (object item in list)
                    {
                        if (!firstItem)
                            builder.Append(',');
                        firstItem = false;
                        Write(builder, item);
                    }
                    builder.Append(']');
                    break;
                case IFormattable number:
                    builder.Append(number.ToString(null, CultureInfo.InvariantCulture));
                    break;
                default:
                    WriteString(builder, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
