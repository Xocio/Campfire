using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace PCLPlus
{
    /// <summary>
    /// Loads dependencies from a compressed archive embedded in the executable, so PCL++ ships as a single file.
    /// </summary>
    internal static class EmbeddedAssemblyLoader
    {
        private const string ArchiveResource = "PCLPlus.Dependencies.zip";

        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            lock (Loaded)
            {
                if (Loaded.TryGetValue(name, out var cached))
                    return cached;

                byte[] image = ReadEntry(name + ".dll");
                if (image == null)
                    return null;

                var assembly = Assembly.Load(image);
                Loaded[name] = assembly;
                return assembly;
            }
        }

        private static byte[] ReadEntry(string entryName)
        {
            using (var stream = typeof(EmbeddedAssemblyLoader).Assembly.GetManifestResourceStream(ArchiveResource))
            {
                if (stream == null)
                    return null;

                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry(entryName);
                    if (entry == null)
                        return null;

                    using (var input = entry.Open())
                    using (var buffer = new MemoryStream((int)entry.Length))
                    {
                        input.CopyTo(buffer);
                        return buffer.ToArray();
                    }
                }
            }
        }
    }
}
