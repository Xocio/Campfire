using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// The parts of the Java Edition network protocol needed to admit or disconnect a joining player: packet
    /// framing, compression and the disconnect packet of each version.
    /// </summary>
    internal static class GameProtocol
    {
        /// <summary>Protocol 764 (1.20.2) added a configuration phase between login and play.</summary>
        public const int FirstWithConfiguration = 764;

        public const int LoginEncryptionRequest = 0x01;
        public const int LoginSuccess = 0x02;
        public const int LoginSetCompression = 0x03;

        /// <summary>
        /// Clientbound packet ids of release versions: "start configuration" (-1 if none), disconnect while playing,
        /// system chat while playing, and the player list header and footer.
        /// </summary>
        private static readonly Dictionary<int, (int StartConfiguration, int Disconnect, int SystemChat, int PlayerListHeader)> Packets =
            new Dictionary<int, (int, int, int, int)>
        {
            [47] = (-1, 0x40, 0x02, 0x47), // 1.8
            [107] = (-1, 0x1A, 0x0F, 0x48), // 1.9
            [109] = (-1, 0x1A, 0x0F, 0x48), // 1.9.2
            [110] = (-1, 0x1A, 0x0F, 0x47), // 1.9.4
            [210] = (-1, 0x1A, 0x0F, 0x47), // 1.10.2
            [315] = (-1, 0x1A, 0x0F, 0x47), // 1.11
            [316] = (-1, 0x1A, 0x0F, 0x47), // 1.11.2
            [335] = (-1, 0x1A, 0x0F, 0x49), // 1.12
            [338] = (-1, 0x1A, 0x0F, 0x4A), // 1.12.1
            [340] = (-1, 0x1A, 0x0F, 0x4A), // 1.12.2
            [393] = (-1, 0x1B, 0x0E, 0x4E), // 1.13
            [401] = (-1, 0x1B, 0x0E, 0x4E), // 1.13.1
            [404] = (-1, 0x1B, 0x0E, 0x4E), // 1.13.2
            [477] = (-1, 0x1A, 0x0E, 0x53), // 1.14
            [480] = (-1, 0x1A, 0x0E, 0x53), // 1.14.1
            [485] = (-1, 0x1A, 0x0E, 0x53), // 1.14.2
            [490] = (-1, 0x1A, 0x0E, 0x53), // 1.14.3
            [498] = (-1, 0x1A, 0x0E, 0x53), // 1.14.4
            [573] = (-1, 0x1B, 0x0F, 0x54), // 1.15
            [575] = (-1, 0x1B, 0x0F, 0x54), // 1.15.1
            [578] = (-1, 0x1B, 0x0F, 0x54), // 1.15.2
            [735] = (-1, 0x1A, 0x0E, 0x53), // 1.16
            [736] = (-1, 0x1A, 0x0E, 0x53), // 1.16.1
            [751] = (-1, 0x19, 0x0E, 0x53), // 1.16.2
            [753] = (-1, 0x19, 0x0E, 0x53), // 1.16.3
            [754] = (-1, 0x19, 0x0E, 0x53), // 1.16.4 and 1.16.5
            [755] = (-1, 0x1A, 0x0F, 0x5E), // 1.17
            [756] = (-1, 0x1A, 0x0F, 0x5E), // 1.17.1
            [757] = (-1, 0x1A, 0x0F, 0x5F), // 1.18 and 1.18.1
            [758] = (-1, 0x1A, 0x0F, 0x5F), // 1.18.2
            [759] = (-1, 0x17, 0x5F, 0x60), // 1.19
            [760] = (-1, 0x19, 0x62, 0x63), // 1.19.1 and 1.19.2
            [761] = (-1, 0x17, 0x60, 0x61), // 1.19.3
            [762] = (-1, 0x1A, 0x64, 0x65), // 1.19.4
            [763] = (-1, 0x1A, 0x64, 0x65), // 1.20 and 1.20.1
            [764] = (0x65, 0x1B, 0x67, 0x68), // 1.20.2
            [765] = (0x67, 0x1B, 0x69, 0x6A), // 1.20.3 and 1.20.4
            [766] = (0x69, 0x1D, 0x6C, 0x6D), // 1.20.5 and 1.20.6
            [767] = (0x69, 0x1D, 0x6C, 0x6D), // 1.21 and 1.21.1
            [768] = (0x70, 0x1D, 0x73, 0x74), // 1.21.2 and 1.21.3
            [769] = (0x70, 0x1D, 0x73, 0x74), // 1.21.4
            [770] = (0x6F, 0x1C, 0x72, 0x73), // 1.21.5
            [771] = (0x6F, 0x1C, 0x72, 0x73), // 1.21.6
            [772] = (0x6F, 0x1C, 0x72, 0x73), // 1.21.7 and 1.21.8
            [773] = (0x74, 0x20, 0x77, 0x78), // 1.21.9 and 1.21.10
            [774] = (0x74, 0x20, 0x77, 0x78), // 1.21.11
            [775] = (0x76, 0x20, 0x79, 0x7A), // 26.1
        };

        /// <summary>
        /// Clientbound "join game" packet id of release versions. The message and the Tab list header are only
        /// injected after this packet, once the client has really entered the world.
        /// </summary>
        private static readonly Dictionary<int, int> JoinGamePackets = new Dictionary<int, int>
        {
            [47] = 0x01, // 1.8
            [107] = 0x23, // 1.9
            [109] = 0x23, // 1.9.2
            [110] = 0x23, // 1.9.4
            [210] = 0x23, // 1.10.2
            [315] = 0x23, // 1.11
            [316] = 0x23, // 1.11.2
            [335] = 0x23, // 1.12
            [338] = 0x23, // 1.12.1
            [340] = 0x23, // 1.12.2
            [393] = 0x25, // 1.13
            [401] = 0x25, // 1.13.1
            [404] = 0x25, // 1.13.2
            [477] = 0x25, // 1.14
            [480] = 0x25, // 1.14.1
            [485] = 0x25, // 1.14.2
            [490] = 0x25, // 1.14.3
            [498] = 0x25, // 1.14.4
            [573] = 0x26, // 1.15
            [575] = 0x26, // 1.15.1
            [578] = 0x26, // 1.15.2
            [735] = 0x25, // 1.16
            [736] = 0x25, // 1.16.1
            [751] = 0x24, // 1.16.2
            [753] = 0x24, // 1.16.3
            [754] = 0x24, // 1.16.4 and 1.16.5
            [755] = 0x26, // 1.17
            [756] = 0x26, // 1.17.1
            [757] = 0x26, // 1.18 and 1.18.1
            [758] = 0x26, // 1.18.2
            [759] = 0x23, // 1.19
            [760] = 0x25, // 1.19.1 and 1.19.2
            [761] = 0x24, // 1.19.3
            [762] = 0x28, // 1.19.4
            [763] = 0x28, // 1.20 and 1.20.1
            [764] = 0x29, // 1.20.2
            [765] = 0x29, // 1.20.3 and 1.20.4
            [766] = 0x2B, // 1.20.5 and 1.20.6
            [767] = 0x2B, // 1.21 and 1.21.1
            [768] = 0x2C, // 1.21.2 and 1.21.3
            [769] = 0x2C, // 1.21.4
            [770] = 0x2B, // 1.21.5
            [771] = 0x2B, // 1.21.6
            [772] = 0x2B, // 1.21.7 and 1.21.8
            [773] = 0x30, // 1.21.9 and 1.21.10
            [774] = 0x30, // 1.21.11
            [775] = 0x31, // 26.1
        };

        /// <summary>Known "start configuration" packet id, or -1 when the version has none or it is unknown.</summary>
        public static int StartConfigurationPacket(int protocol)
        {
            return Packets.TryGetValue(protocol, out var ids) ? ids.StartConfiguration : -1;
        }

        public static int FinishConfigurationPacket(int protocol)
        {
            return protocol < 766 ? 0x02 : 0x03;
        }

        /// <summary>Known disconnect packet id while playing, or -1 when the version is unknown.</summary>
        public static int DisconnectPacket(int protocol)
        {
            return Packets.TryGetValue(protocol, out var ids) ? ids.Disconnect : -1;
        }

        /// <summary>Known system chat packet id while playing, or -1 when the version is unknown.</summary>
        public static int SystemChatPacket(int protocol)
        {
            return Packets.TryGetValue(protocol, out var ids) ? ids.SystemChat : -1;
        }

        /// <summary>Known player list header/footer packet id while playing, or -1 when the version is unknown.</summary>
        public static int PlayerListHeaderPacket(int protocol)
        {
            return Packets.TryGetValue(protocol, out var ids) ? ids.PlayerListHeader : -1;
        }

        /// <summary>Known "join game" packet id while playing, or -1 when the version is unknown.</summary>
        public static int JoinGamePacket(int protocol)
        {
            return JoinGamePackets.TryGetValue(protocol, out int id) ? id : -1;
        }

        public static int ConfigurationDisconnectPacket(int protocol)
        {
            return protocol < 766 ? 0x01 : 0x02;
        }

        // ======================== Login ========================

        /// <summary>The player name of a "login start" frame (length prefix included), or <c>null</c> if it is not one.</summary>
        public static string ReadLoginName(byte[] frame)
        {
            int offset = 0;
            ReadVarInt(frame, ref offset);
            if (ReadVarInt(frame, ref offset) != 0x00)
                return null;
            return ReadString(frame, ref offset);
        }

        /// <summary>The body of a "disconnect" during login, which always carries the reason as JSON text.</summary>
        public static byte[] BuildLoginDisconnect(string reason)
        {
            using (var body = new MemoryStream())
            {
                WriteVarInt(body, 0x00);
                WriteString(body, JsonText(reason));
                return body.ToArray();
            }
        }

        /// <summary>The body of a "disconnect" while configuring or playing; newer versions carry the reason as NBT.</summary>
        public static byte[] BuildDisconnect(int protocol, int packetId, string reason)
        {
            using (var body = new MemoryStream())
            {
                WriteVarInt(body, packetId);
                if (protocol >= 765)
                {
                    body.WriteByte(0x0A);
                    WriteNbtString(body, "text", reason);
                    body.WriteByte(0x00);
                }
                else
                {
                    WriteString(body, JsonText(reason));
                }
                return body.ToArray();
            }
        }

        private static string JsonText(string text)
        {
            return Json.Serialize(new Dictionary<string, object> { ["text"] = text });
        }

        /// <summary>
        /// The body of a system chat message, shown in the chat box. Returns <c>null</c> when the version is unknown.
        /// </summary>
        public static byte[] BuildSystemChat(int protocol, IReadOnlyList<GameText> text)
        {
            int id = SystemChatPacket(protocol);
            if (id < 0 || text == null || text.Count == 0)
                return null;

            using (var body = new MemoryStream())
            {
                WriteVarInt(body, id);
                WriteComponent(body, protocol, text);
                body.WriteByte(0); // chat box, not the action bar
                return body.ToArray();
            }
        }

        /// <summary>
        /// The body of a player list header and footer. Returns <c>null</c> when the version is unknown.
        /// </summary>
        public static byte[] BuildPlayerListHeaderFooter(int protocol, IReadOnlyList<GameText> header, IReadOnlyList<GameText> footer)
        {
            int id = PlayerListHeaderPacket(protocol);
            if (id < 0 || header == null || footer == null)
                return null;

            using (var body = new MemoryStream())
            {
                WriteVarInt(body, id);
                WriteComponent(body, protocol, header);
                WriteComponent(body, protocol, footer);
                return body.ToArray();
            }
        }

        private static void WriteComponent(Stream stream, int protocol, IReadOnlyList<GameText> text)
        {
            // 1.20.3 (765) replaced JSON chat components in play packets with network NBT.
            if (protocol >= 765)
                WriteNbtComponent(stream, text);
            else
                WriteJsonComponent(stream, text);
        }

        private static void WriteJsonComponent(Stream stream, IReadOnlyList<GameText> text)
        {
            var extra = new List<object>(text.Count);
            foreach (var part in text)
            {
                var item = new Dictionary<string, object> { ["text"] = part.Text ?? "" };
                if (!string.IsNullOrEmpty(part.Color))
                    item["color"] = part.Color;
                if (part.Bold)
                    item["bold"] = true;
                if (part.Underlined)
                    item["underlined"] = true;
                extra.Add(item);
            }

            WriteString(stream, Json.Serialize(new Dictionary<string, object>
            {
                ["text"] = "",
                ["extra"] = extra,
            }));
        }

        private static void WriteNbtComponent(Stream stream, IReadOnlyList<GameText> text)
        {
            stream.WriteByte(NbtCompound);
            WriteNbtString(stream, "text", "");
            stream.WriteByte(NbtList);
            WriteNbtName(stream, "extra");
            stream.WriteByte(NbtCompound);
            WriteNbtInt(stream, text.Count);
            foreach (var part in text)
                WriteNbtTextPayload(stream, part);
            stream.WriteByte(NbtEnd);
        }

        private static void WriteNbtTextPayload(Stream stream, GameText text)
        {
            WriteNbtString(stream, "text", text.Text ?? "");
            if (!string.IsNullOrEmpty(text.Color))
                WriteNbtString(stream, "color", text.Color);
            if (text.Bold)
                WriteNbtByte(stream, "bold", 1);
            if (text.Underlined)
                WriteNbtByte(stream, "underlined", 1);
            stream.WriteByte(NbtEnd);
        }

        private const byte NbtEnd = 0x00;
        private const byte NbtByte = 0x01;
        private const byte NbtString = 0x08;
        private const byte NbtList = 0x09;
        private const byte NbtCompound = 0x0A;

        private static void WriteNbtByte(Stream stream, string name, byte value)
        {
            stream.WriteByte(NbtByte);
            WriteNbtName(stream, name);
            stream.WriteByte(value);
        }

        private static void WriteNbtString(Stream stream, string name, string value)
        {
            stream.WriteByte(NbtString);
            WriteNbtName(stream, name);
            WriteNbtName(stream, value);
        }

        private static void WriteNbtInt(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        /// <summary>
        /// NBT strings are an unsigned 16-bit length followed by modified UTF-8, which encodes each UTF-16 unit on
        /// its own, so an emoji becomes two three-byte sequences, and writes the null character as two bytes.
        /// </summary>
        private static void WriteNbtName(Stream stream, string value)
        {
            var bytes = new List<byte>(value.Length);
            foreach (char c in value)
            {
                if (c != 0 && c < 0x80)
                {
                    bytes.Add((byte)c);
                }
                else if (c < 0x800)
                {
                    bytes.Add((byte)(0xC0 | (c >> 6)));
                    bytes.Add((byte)(0x80 | (c & 0x3F)));
                }
                else
                {
                    bytes.Add((byte)(0xE0 | (c >> 12)));
                    bytes.Add((byte)(0x80 | ((c >> 6) & 0x3F)));
                    bytes.Add((byte)(0x80 | (c & 0x3F)));
                }
            }
            stream.WriteByte((byte)(bytes.Count >> 8));
            stream.WriteByte((byte)bytes.Count);
            stream.Write(bytes.ToArray(), 0, bytes.Count);
        }

        // ======================== Framing ========================

        /// <summary>Wraps a packet body into a frame, compressing it like the server would.</summary>
        /// <param name="threshold">The connection's compression threshold, or -1 when compression is off.</param>
        public static byte[] Frame(byte[] body, int threshold)
        {
            using (var payload = new MemoryStream())
            {
                if (threshold < 0)
                {
                    payload.Write(body, 0, body.Length);
                }
                else if (body.Length < threshold)
                {
                    WriteVarInt(payload, 0);
                    payload.Write(body, 0, body.Length);
                }
                else
                {
                    WriteVarInt(payload, body.Length);
                    byte[] packed = Deflate(body);
                    payload.Write(packed, 0, packed.Length);
                }

                using (var frame = new MemoryStream())
                {
                    WriteVarInt(frame, (int)payload.Length);
                    payload.WriteTo(frame);
                    return frame.ToArray();
                }
            }
        }

        /// <summary>
        /// Returns the packet body of a frame payload, inflating it when needed. Returns <c>null</c> for compressed
        /// packets larger than <paramref name="maxSize"/>, which are never interesting here.
        /// </summary>
        public static byte[] Unwrap(byte[] payload, int threshold, int maxSize)
        {
            if (threshold < 0)
                return payload;

            int offset = 0;
            int size = ReadVarInt(payload, ref offset);
            if (size == 0)
            {
                var plain = new byte[payload.Length - offset];
                Buffer.BlockCopy(payload, offset, plain, 0, plain.Length);
                return plain;
            }
            if (size > maxSize)
                return null;

            // zlib: two header bytes, then a raw deflate stream.
            using (var input = new MemoryStream(payload, offset + 2, payload.Length - offset - 2))
            using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
            {
                var body = new byte[size];
                int read = 0;
                while (read < size)
                {
                    int n = inflater.Read(body, read, size - read);
                    if (n == 0)
                        throw new InvalidDataException("Truncated compressed packet.");
                    read += n;
                }
                return body;
            }
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflater = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    deflater.Write(data, 0, data.Length);

                // Adler-32 of the uncompressed data, big-endian.
                uint a = 1, b = 0;
                foreach (byte value in data)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }
                uint adler = (b << 16) | a;
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                return output.ToArray();
            }
        }

        // ======================== Primitive types ========================

        public static int ReadVarInt(byte[] data, ref int offset)
        {
            int value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (offset >= data.Length)
                    throw new InvalidDataException("Truncated VarInt.");
                byte current = data[offset++];
                value |= (current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                    return value;
            }
            throw new InvalidDataException("VarInt is too long.");
        }

        public static void WriteVarInt(Stream stream, int value)
        {
            uint remaining = (uint)value;
            while (remaining >= 0x80)
            {
                stream.WriteByte((byte)(remaining | 0x80));
                remaining >>= 7;
            }
            stream.WriteByte((byte)remaining);
        }

        public static string ReadString(byte[] data, ref int offset)
        {
            int length = ReadVarInt(data, ref offset);
            if (length < 0 || offset + length > data.Length)
                throw new InvalidDataException("Truncated string.");
            string value = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return value;
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

    }
}
