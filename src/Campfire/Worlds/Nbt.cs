using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Campfire.Worlds
{
    /// <summary>
    /// Reads Minecraft's NBT format, in which Java Edition stores level.dat.
    /// </summary>
    /// <remarks>
    /// Compounds become <see cref="Dictionary{TKey,TValue}"/> of string to object and lists become <c>object[]</c>.
    /// Numbers keep their own type (sbyte, short, int, long, float, double); byte, int and long arrays become
    /// <c>byte[]</c>, <c>int[]</c> and <c>long[]</c>. Maps are downloaded from anywhere, so every length is checked
    /// against a budget instead of trusted.
    /// </remarks>
    internal static class Nbt
    {
        /// <summary>A level.dat is a few kilobytes; even heavily modded ones stay far below this.</summary>
        private const long MaxDecompressedSize = 32L * 1024 * 1024;

        private const int MaxDepth = 512;

        /// <summary>
        /// Reads a whole file that is gzip compressed, zlib compressed or not compressed at all, and returns its
        /// root compound. Throws <see cref="InvalidDataException"/> when the data is not valid NBT.
        /// </summary>
        public static Dictionary<string, object> Read(byte[] data)
        {
            if (data == null || data.Length < 3)
                throw new InvalidDataException("The file is too short to be NBT.");

            Stream stream;
            if (data[0] == 0x1F && data[1] == 0x8B)
                stream = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            else if (data[0] == 0x78 && (data[0] << 8 | data[1]) % 31 == 0)
                // zlib: a two-byte header in front of a raw deflate stream.
                stream = new DeflateStream(new MemoryStream(data, 2, data.Length - 2), CompressionMode.Decompress);
            else
                stream = new MemoryStream(data);

            using (stream)
            {
                var reader = new Reader(stream);
                int type = reader.ReadByte();
                if (type != TagCompound)
                    throw new InvalidDataException("The root of an NBT file must be a compound.");
                reader.ReadString();
                return (Dictionary<string, object>)reader.ReadPayload(type, 0);
            }
        }

        /// <summary>Follows <paramref name="path"/> through nested compounds; <c>null</c> when a step is missing.</summary>
        public static object Get(object value, params string[] path)
        {
            foreach (string key in path)
            {
                if (!(value is Dictionary<string, object> compound) || !compound.TryGetValue(key, out value))
                    return null;
            }
            return value;
        }

        public static string GetString(object value, params string[] path) => Get(value, path) as string;

        /// <summary>Any whole number at <paramref name="path"/>, whatever its width.</summary>
        public static long? GetNumber(object value, params string[] path)
        {
            switch (Get(value, path))
            {
                case sbyte b: return b;
                case short s: return s;
                case int i: return i;
                case long l: return l;
                default: return null;
            }
        }

        private const int TagCompound = 10;

        private sealed class Reader
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer = new byte[8];
            private long _remaining = MaxDecompressedSize;

            public Reader(Stream stream)
            {
                _stream = stream;
            }

            public int ReadByte()
            {
                Fill(1);
                return _buffer[0];
            }

            public object ReadPayload(int type, int depth)
            {
                if (depth > MaxDepth)
                    throw new InvalidDataException("NBT is nested too deeply.");

                switch (type)
                {
                    case 1: Fill(1); return unchecked((sbyte)_buffer[0]);
                    case 2: Fill(2); return (short)(_buffer[0] << 8 | _buffer[1]);
                    case 3: return ReadInt();
                    case 4: return ReadLong();
                    case 5: return BitConverter.ToSingle(BitConverter.GetBytes(ReadInt()), 0);
                    case 6: return BitConverter.Int64BitsToDouble(ReadLong());
                    case 7: return ReadBytes(ReadLength(1));
                    case 8: return ReadString();
                    case 9:
                    {
                        int elementType = ReadByte();
                        int count = ReadLength(elementType == 0 ? 0 : 1);
                        var list = new object[count];
                        for (int i = 0; i < count; i++)
                            list[i] = ReadPayload(elementType, depth + 1);
                        return list;
                    }
                    case 10:
                    {
                        var compound = new Dictionary<string, object>(StringComparer.Ordinal);
                        while (true)
                        {
                            int childType = ReadByte();
                            if (childType == 0)
                                return compound;
                            string name = ReadString();
                            compound[name] = ReadPayload(childType, depth + 1);
                        }
                    }
                    case 11:
                    {
                        var values = new int[ReadLength(4)];
                        for (int i = 0; i < values.Length; i++)
                            values[i] = ReadInt();
                        return values;
                    }
                    case 12:
                    {
                        var values = new long[ReadLength(8)];
                        for (int i = 0; i < values.Length; i++)
                            values[i] = ReadLong();
                        return values;
                    }
                    default:
                        throw new InvalidDataException("Unknown NBT tag type " + type + ".");
                }
            }

            public string ReadString()
            {
                Fill(2);
                int length = _buffer[0] << 8 | _buffer[1];
                // Java's modified UTF-8 only differs for NUL and characters outside the BMP, which names never use.
                return Encoding.UTF8.GetString(ReadBytes(length));
            }

            private int ReadInt()
            {
                Fill(4);
                return _buffer[0] << 24 | _buffer[1] << 16 | _buffer[2] << 8 | _buffer[3];
            }

            private long ReadLong()
            {
                long high = (uint)ReadInt();
                long low = (uint)ReadInt();
                return high << 32 | low;
            }

            /// <summary>Reads a length and makes sure that many elements could still fit in the budget.</summary>
            private int ReadLength(int elementSize)
            {
                int count = ReadInt();
                if (count < 0 || (long)count * Math.Max(elementSize, 1) > _remaining)
                    throw new InvalidDataException("NBT length out of range.");
                return count;
            }

            private byte[] ReadBytes(int count)
            {
                Take(count);
                var bytes = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int n = _stream.Read(bytes, read, count - read);
                    if (n <= 0)
                        throw new InvalidDataException("NBT data ends early.");
                    read += n;
                }
                return bytes;
            }

            private void Fill(int count)
            {
                Take(count);
                int read = 0;
                while (read < count)
                {
                    int n = _stream.Read(_buffer, read, count - read);
                    if (n <= 0)
                        throw new InvalidDataException("NBT data ends early.");
                    read += n;
                }
            }

            private void Take(int count)
            {
                _remaining -= count;
                if (_remaining < 0)
                    throw new InvalidDataException("NBT data is too large.");
            }
        }
    }
}
