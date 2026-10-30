using System;
using System.Collections.Generic;
using System.Text;
using dnlib.DotNet;

namespace Campfire.Patching
{
    /// <summary>
    /// One managed assembly embedded in a single-file launcher bundle.
    /// </summary>
    internal sealed class BundleImage : IDisposable
    {
        public BundleImage(int start, int length, ModuleDefMD module)
        {
            Start = start;
            Length = length;
            Module = module;
        }

        /// <summary>Offset of the assembly image inside the bundle file.</summary>
        public int Start { get; }

        /// <summary>Number of bytes from <see cref="Start"/> to the next image in the bundle.</summary>
        public int Length { get; }

        public ModuleDefMD Module { get; }

        public string AssemblyName => Module.Assembly?.Name?.String;

        public Version Version => Module.Assembly?.Version;

        public void Dispose()
        {
            Module.Dispose();
        }
    }

    /// <summary>
    /// Locates managed assemblies inside a .NET single-file launcher (PCL Community Edition).
    /// </summary>
    /// <remarks>
    /// A bundled launcher is a native host with the assemblies appended as separate PE images.
    /// Images are found by walking the MZ/PE headers of the file; assembly names are confirmed by loading the slice with dnlib.
    /// </remarks>
    internal static class BundleScanner
    {
        /// <summary>
        /// Returns the embedded assembly named <paramref name="assemblyName"/>, or <c>null</c>.
        /// </summary>
        public static BundleImage FindAssembly(byte[] file, string assemblyName)
        {
            var nameBytes = Encoding.ASCII.GetBytes(assemblyName);
            foreach (var range in EnumerateImageRanges(file))
            {
                // Cheap pre-filter: the assembly name only appears in metadata, which is plain text.
                if (!Contains(file, range.Start, range.Length, nameBytes))
                    continue;

                ModuleDefMD module;
                try
                {
                    module = ModuleDefMD.Load(Slice(file, range.Start, range.Length));
                }
                catch (Exception ex) when (IsLoadFailure(ex))
                {
                    continue;
                }

                if (module.Assembly != null && module.Assembly.Name == assemblyName)
                    return new BundleImage(range.Start, range.Length, module);

                module.Dispose();
            }

            return null;
        }

        /// <summary>Whether the file looks like a bundled launcher that contains <paramref name="assemblyName"/>.</summary>
        public static bool ContainsAssembly(byte[] file, string assemblyName)
        {
            using (var image = FindAssembly(file, assemblyName))
                return image != null;
        }

        /// <summary>Maps an RVA of the image starting at <paramref name="imageStart"/> to an offset in <paramref name="file"/>.</summary>
        public static int MapRvaToOffset(byte[] file, int imageStart, int rva)
        {
            int pe = imageStart + ReadInt32(file, imageStart + 0x3C);
            int optional = pe + 24;
            int sectionCount = ReadUInt16(file, pe + 6);
            int optionalSize = ReadUInt16(file, pe + 20);
            int sections = optional + optionalSize;

            for (int i = 0; i < sectionCount; i++)
            {
                int section = sections + i * 40;
                int virtualSize = ReadInt32(file, section + 8);
                int virtualAddress = ReadInt32(file, section + 12);
                int rawSize = ReadInt32(file, section + 16);
                int rawPointer = ReadInt32(file, section + 20);

                int span = Math.Max(virtualSize, rawSize);
                if (rva >= virtualAddress && rva < virtualAddress + span)
                    return imageStart + rawPointer + (rva - virtualAddress);
            }

            throw new PatchException($"RVA 0x{rva:X} is outside every section of the bundled image");
        }

        /// <summary>Reads the IL body header at <paramref name="bodyOffset"/>.</summary>
        public static MethodBodyLocation LocateBody(byte[] file, int bodyOffset)
        {
            byte first = file[bodyOffset];
            switch (first & 3)
            {
                case 2: // tiny: one-byte header, size in the upper six bits
                    return new MethodBodyLocation(1, first >> 2);
                case 3: // fat: two-byte flags, code size at offset 4
                {
                    int flags = ReadUInt16(file, bodyOffset);
                    int headerSize = (flags >> 12) * 4;
                    if (headerSize < 12)
                        throw new PatchException("unexpected fat method header size");
                    return new MethodBodyLocation(headerSize, ReadInt32(file, bodyOffset + 4));
                }
                default:
                    throw new PatchException("unsupported method body header");
            }
        }

        private static IEnumerable<(int Start, int Length)> EnumerateImageRanges(byte[] file)
        {
            var starts = new List<int>();
            for (int i = 0; i + 0x40 <= file.Length; i++)
            {
                if (file[i] != (byte)'M' || file[i + 1] != (byte)'Z')
                    continue;
                if (IsPeImage(file, i))
                    starts.Add(i);
            }

            for (int i = 0; i < starts.Count; i++)
            {
                int end = i + 1 < starts.Count ? starts[i + 1] : file.Length;
                yield return (starts[i], end - starts[i]);
            }
        }

        private static bool IsPeImage(byte[] file, int start)
        {
            int lfanew = ReadInt32(file, start + 0x3C);
            if (lfanew <= 0 || lfanew > 0x1000)
                return false;

            int pe = start + lfanew;
            if (pe + 24 > file.Length)
                return false;
            if (file[pe] != (byte)'P' || file[pe + 1] != (byte)'E' || file[pe + 2] != 0 || file[pe + 3] != 0)
                return false;

            int magic = ReadUInt16(file, pe + 24);
            return magic == 0x10B || magic == 0x20B;
        }

        private static byte[] Slice(byte[] file, int start, int length)
        {
            var slice = new byte[length];
            Buffer.BlockCopy(file, start, slice, 0, length);
            return slice;
        }

        private static bool Contains(byte[] file, int start, int length, byte[] pattern)
        {
            int end = start + length - pattern.Length;
            for (int i = start; i <= end; i++)
            {
                if (file[i] != pattern[0])
                    continue;
                int j = 1;
                while (j < pattern.Length && file[i + j] == pattern[j])
                    j++;
                if (j == pattern.Length)
                    return true;
            }
            return false;
        }

        private static bool IsLoadFailure(Exception ex)
        {
            return ex is BadImageFormatException || ex is ArgumentException || ex is InvalidOperationException;
        }

        private static int ReadInt32(byte[] file, int offset)
        {
            return file[offset] | (file[offset + 1] << 8) | (file[offset + 2] << 16) | (file[offset + 3] << 24);
        }

        private static int ReadUInt16(byte[] file, int offset)
        {
            return file[offset] | (file[offset + 1] << 8);
        }
    }

    /// <summary>Location of a method body inside a file.</summary>
    internal readonly struct MethodBodyLocation
    {
        public MethodBodyLocation(int headerSize, int codeSize)
        {
            HeaderSize = headerSize;
            CodeSize = codeSize;
        }

        /// <summary>Bytes between the body header and the first IL byte.</summary>
        public int HeaderSize { get; }

        /// <summary>Number of IL bytes.</summary>
        public int CodeSize { get; }
    }
}
