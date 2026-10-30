using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// Reads single files out of a zip archive on a web server with HTTP range requests.
    /// </summary>
    internal sealed class RemoteZip
    {
        private const uint EndOfDirectorySignature = 0x06054B50;
        private const uint DirectoryEntrySignature = 0x02014B50;
        private const uint LocalHeaderSignature = 0x04034B50;
        private const int TailSize = 64 * 1024;

        private readonly HttpClient _client;
        private readonly string _url;

        private RemoteZip(HttpClient client, string url, List<Entry> entries)
        {
            _client = client;
            _url = url;
            Entries = entries;
        }

        public IReadOnlyList<Entry> Entries { get; }

        /// <summary>Reads the central directory. <paramref name="length"/> is the archive size in bytes.</summary>
        public static async Task<RemoteZip> OpenAsync(HttpClient client, string url, long length, CancellationToken cancellation)
        {
            long tailStart = Math.Max(0, length - TailSize);
            byte[] tail = await FetchAsync(client, url, tailStart, length - 1, cancellation).ConfigureAwait(false);

            int end = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (ReadUInt32(tail, i) == EndOfDirectorySignature)
                {
                    end = i;
                    break;
                }
            }
            if (end < 0)
                throw new InvalidDataException("End of central directory not found.");

            int count = ReadUInt16(tail, end + 10);
            long directorySize = ReadUInt32(tail, end + 12);
            long directoryOffset = ReadUInt32(tail, end + 16);

            byte[] directory;
            int position;
            if (directoryOffset >= tailStart)
            {
                directory = tail;
                position = (int)(directoryOffset - tailStart);
            }
            else
            {
                directory = await FetchAsync(client, url, directoryOffset, directoryOffset + directorySize - 1, cancellation)
                    .ConfigureAwait(false);
                position = 0;
            }

            var entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                if (position + 46 > directory.Length || ReadUInt32(directory, position) != DirectoryEntrySignature)
                    throw new InvalidDataException("Corrupt central directory.");

                int nameLength = ReadUInt16(directory, position + 28);
                int extraLength = ReadUInt16(directory, position + 30);
                int commentLength = ReadUInt16(directory, position + 32);
                entries.Add(new Entry(
                    Encoding.UTF8.GetString(directory, position + 46, nameLength),
                    ReadUInt16(directory, position + 10),
                    ReadUInt32(directory, position + 20),
                    ReadUInt32(directory, position + 42)));
                position += 46 + nameLength + extraLength + commentLength;
            }
            return new RemoteZip(client, url, entries);
        }

        /// <summary>Decompresses <paramref name="entry"/> into <paramref name="output"/>.</summary>
        /// <param name="onProgress">Receives the number of compressed bytes transferred since the last call.</param>
        public async Task ExtractAsync(Entry entry, Stream output, Action<long> onProgress, CancellationToken cancellation)
        {
            byte[] header = await FetchAsync(_client, _url, entry.HeaderOffset, entry.HeaderOffset + 29, cancellation)
                .ConfigureAwait(false);
            if (ReadUInt32(header, 0) != LocalHeaderSignature)
                throw new InvalidDataException("Corrupt local header: " + entry.Name);

            long dataStart = entry.HeaderOffset + 30 + ReadUInt16(header, 26) + ReadUInt16(header, 28);
            if (entry.CompressedSize == 0)
                return;

            using (var response = await SendRangeAsync(_client, _url, dataStart, dataStart + entry.CompressedSize - 1, cancellation)
                .ConfigureAwait(false))
            using (var network = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                var counted = new ProgressStream(network, response, onProgress);
                switch (entry.Method)
                {
                    case 0:
                        await counted.CopyToAsync(output, 81920, cancellation).ConfigureAwait(false);
                        break;
                    case 8:
                        using (var inflater = new DeflateStream(counted, CompressionMode.Decompress, leaveOpen: true))
                            await inflater.CopyToAsync(output, 81920, cancellation).ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidDataException("Unsupported compression method " + entry.Method + ": " + entry.Name);
                }
            }
        }

        private static async Task<byte[]> FetchAsync(HttpClient client, string url, long from, long to, CancellationToken cancellation)
        {
            using (var response = await SendRangeAsync(client, url, from, to, cancellation).ConfigureAwait(false))
            using (var network = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream((int)(to - from + 1)))
            {
                await new ProgressStream(network, response, null).CopyToAsync(buffer, 81920, cancellation).ConfigureAwait(false);
                if (buffer.Length != to - from + 1)
                    throw new InvalidDataException("Short range response.");
                return buffer.ToArray();
            }
        }

        private static async Task<HttpResponseMessage> SendRangeAsync(HttpClient client, string url, long from, long to,
            CancellationToken cancellation)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(from, to);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException("Range request was answered with " + (int)status + ".");
            }
            return response;
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        internal sealed class Entry
        {
            public Entry(string name, int method, long compressedSize, long headerOffset)
            {
                Name = name;
                Method = method;
                CompressedSize = compressedSize;
                HeaderOffset = headerOffset;
            }

            public string Name { get; }

            public int Method { get; }

            public long CompressedSize { get; }

            public long HeaderOffset { get; }
        }
    }
}
