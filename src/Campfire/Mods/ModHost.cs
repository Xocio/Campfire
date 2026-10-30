using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Link.Minecraft;
using Campfire.Worlds;

namespace Campfire.Mods
{
    /// <summary>
    /// The host's side of mod sync: reads the mods of the game that runs the world and hands players the list,
    /// and the files when the host allows it.
    /// </summary>
    /// <remarks>
    /// A player can only ask for a file by the hash of a jar on that list. Paths never travel.
    /// </remarks>
    internal sealed class ModHost
    {
        /// <summary>Largest piece of a file sent in one answer; the room service limits answers to 1 MB.</summary>
        public const int ChunkSize = 512 * 1024;

        private readonly Dictionary<string, string> _paths;
        private readonly byte[] _listJson;

        private ModHost(ModList list, Dictionary<string, string> paths, bool servesFiles)
        {
            List = list;
            _paths = paths;
            ServesFiles = servesFiles;
            var json = list.ToJson();
            json["files"] = servesFiles;
            _listJson = Encoding.UTF8.GetBytes(Json.Serialize(json));
        }

        public ModList List { get; }

        /// <summary>Whether players may download the jars from this computer.</summary>
        public bool ServesFiles { get; }

        /// <summary>The list as the room service sends it.</summary>
        public byte[] ListJson => _listJson;

        /// <summary>
        /// Reads the mods of the game listening on <paramref name="gamePort"/>. Returns <c>null</c> when that is not a
        /// game started by a launcher, such as a dedicated server.
        /// </summary>
        public static Task<ModHost> PrepareAsync(int gamePort, bool servesFiles)
        {
            return Task.Run(() =>
            {
                var game = RunningGame.FromProcess(LocalWorldFinder.FindProcess(gamePort));
                if (game == null)
                {
                    LinkLog.Warn("The game of the LAN world was not found; mod sync is not offered.");
                    return null;
                }

                var version = game.FindVersion();
                string directory = Path.Combine(game.GameDirectory, "mods");
                var mods = ModList.ReadFolder(directory);
                var list = new ModList(version?.GameVersion, version?.Loader,
                    version?.LoaderVersion, mods);
                var paths = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var mod in mods)
                    paths[mod.Sha1] = Path.Combine(directory, mod.FileName);
                LinkLog.Info($"Offering the mod list of {version?.ToString() ?? "an unknown version"}: {mods.Count} mod(s).");
                return new ModHost(list, paths, servesFiles);
            });
        }

        /// <summary>
        /// A piece of the jar with hash <paramref name="sha1"/> starting at <paramref name="offset"/>; empty past
        /// the end. Returns <c>null</c> for a file that is not on the list or has changed since.
        /// </summary>
        public byte[] ReadChunk(string sha1, long offset)
        {
            if (!ServesFiles || !_paths.TryGetValue(sha1, out string path))
                return null;
            var mod = List.Mods.First(m => m.Sha1 == sha1);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != mod.Size || offset < 0)
                return null;
            if (offset >= file.Length)
                return Array.Empty<byte>();

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Position = offset;
                var buffer = new byte[(int)Math.Min(ChunkSize, file.Length - offset)];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n == 0)
                        break;
                    read += n;
                }
                if (read < buffer.Length)
                    Array.Resize(ref buffer, read);
                return buffer;
            }
        }
    }
}
