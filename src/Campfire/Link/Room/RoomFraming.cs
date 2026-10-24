using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Room
{
    /// <summary>
    /// Message framing of the room service, which runs over one TCP connection per player.
    /// </summary>
    /// <remarks>
    /// Request:  kind length (1 byte), kind ("namespace:path", UTF-8), body length (4 bytes, big-endian), body.
    /// Response: status (1 byte, 0 = success), body length (4 bytes, big-endian), body.
    /// </remarks>
    internal static class RoomFraming
    {
        public const byte StatusOk = 0;
        public const byte StatusNotReady = 32;
        public const byte StatusError = 255;

        /// <summary>Upper bound for a body; real messages are a few hundred bytes.</summary>
        private const int MaxBodySize = 1024 * 1024;

        public static async Task WriteRequestAsync(Stream stream, string kind, byte[] body, CancellationToken cancellation)
        {
            byte[] kindBytes = Encoding.UTF8.GetBytes(kind);
            if (kindBytes.Length > 255)
                throw new ArgumentException("Request kind is too long.", nameof(kind));

            body = body ?? Array.Empty<byte>();
            var frame = new byte[1 + kindBytes.Length + 4 + body.Length];
            frame[0] = (byte)kindBytes.Length;
            Buffer.BlockCopy(kindBytes, 0, frame, 1, kindBytes.Length);
            WriteInt32(frame, 1 + kindBytes.Length, body.Length);
            Buffer.BlockCopy(body, 0, frame, 5 + kindBytes.Length, body.Length);

            await stream.WriteAsync(frame, 0, frame.Length, cancellation).ConfigureAwait(false);
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
        }

        /// <summary>Reads one request; returns <c>null</c> when the peer closed the connection cleanly.</summary>
        public static async Task<Request> ReadRequestAsync(Stream stream, CancellationToken cancellation)
        {
            var sizeBuffer = new byte[1];
            if (await stream.ReadAsync(sizeBuffer, 0, 1, cancellation).ConfigureAwait(false) == 0)
                return null;

            byte[] kind = await ReadExactAsync(stream, sizeBuffer[0], cancellation).ConfigureAwait(false);
            byte[] body = await ReadBodyAsync(stream, cancellation).ConfigureAwait(false);
            return new Request(Encoding.UTF8.GetString(kind), body);
        }

        public static async Task WriteResponseAsync(Stream stream, byte status, byte[] body, CancellationToken cancellation)
        {
            body = body ?? Array.Empty<byte>();
            var frame = new byte[5 + body.Length];
            frame[0] = status;
            WriteInt32(frame, 1, body.Length);
            Buffer.BlockCopy(body, 0, frame, 5, body.Length);

            await stream.WriteAsync(frame, 0, frame.Length, cancellation).ConfigureAwait(false);
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
        }

        public static async Task<Response> ReadResponseAsync(Stream stream, CancellationToken cancellation)
        {
            byte[] status = await ReadExactAsync(stream, 1, cancellation).ConfigureAwait(false);
            byte[] body = await ReadBodyAsync(stream, cancellation).ConfigureAwait(false);
            return new Response(status[0], body);
        }

        private static async Task<byte[]> ReadBodyAsync(Stream stream, CancellationToken cancellation)
        {
            byte[] size = await ReadExactAsync(stream, 4, cancellation).ConfigureAwait(false);
            int length = (size[0] << 24) | (size[1] << 16) | (size[2] << 8) | size[3];
            if (length < 0 || length > MaxBodySize)
                throw new InvalidDataException("Message body is too large.");
            return await ReadExactAsync(stream, length, cancellation).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellation)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, cancellation).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("The connection was closed.");
                offset += read;
            }
            return buffer;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        internal sealed class Request
        {
            public Request(string kind, byte[] body)
            {
                Kind = kind;
                Body = body;
            }

            public string Kind { get; }

            public byte[] Body { get; }
        }

        internal sealed class Response
        {
            public Response(byte status, byte[] body)
            {
                Status = status;
                Body = body;
            }

            public byte Status { get; }

            public byte[] Body { get; }

            public bool IsOk => Status == StatusOk;
        }
    }
}
