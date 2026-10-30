using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Link.Room;
using Campfire.Worlds;

namespace Campfire.Mods
{
    /// <summary>Where mod sync takes the host's jars from.</summary>
    internal enum ModSource
    {
        /// <summary>Modrinth where it has the exact file, the host for the rest.</summary>
        OfficialFirst,

        /// <summary>Modrinth only; the rest is left for the player to install.</summary>
        OfficialOnly,

        /// <summary>Every file from the host's computer.</summary>
        Host,
    }

    /// <summary>A failure the sync page explains with its own text.</summary>
    internal sealed class ModSyncException : Exception
    {
        public ModSyncException(string key, Exception inner = null)
            : base(key, inner)
        {
            Key = key;
        }

        /// <summary>The text key under "Mods.Error.".</summary>
        public string Key { get; }
    }

    internal sealed class ModSyncResult
    {
        public ModSyncResult(int installed, int removed, IReadOnlyList<ModInfo> unavailable, string backupDirectory)
        {
            Installed = installed;
            Removed = removed;
            Unavailable = unavailable;
            BackupDirectory = backupDirectory;
        }

        public int Installed { get; }

        /// <summary>Jars moved out of the mods folder into the backup.</summary>
        public int Removed { get; }

        /// <summary>The host's jars no allowed source had.</summary>
        public IReadOnlyList<ModInfo> Unavailable { get; }

        /// <summary>Where replaced and removed jars went, or <c>null</c> when nothing was moved.</summary>
        public string BackupDirectory { get; }
    }

    /// <summary>
    /// Makes a version's mods folder match the host: fetches the host's jars, moves the differing ones aside and
    /// copies the new ones in. Every file is fetched and checked before the folder is touched. Replaced jars go to a backup folder.
    /// </summary>
    internal static class ModSync
    {
        private static readonly TimeSpan KeepDownloads = TimeSpan.FromDays(14);

        private static string DownloadRoot => Path.Combine(AppSettings.DataDirectory, "downloads", "mods");

        private static string BackupRoot => Path.Combine(AppSettings.DataDirectory, "backups", "mods");

        /// <param name="connectHost">Opens a separate connection to the host's room service, used for transfers.</param>
        /// <param name="confirmHost">Asked with the number of jars that would come from the host; <c>false</c> stops the sync.</param>
        /// <param name="progress">Receives the share done, from 0 to 1, and the mod being fetched.</param>
        public static async Task<ModSyncResult> RunAsync(ModComparison comparison, InstalledVersion version, ModSource source,
            bool removeExtra, bool hostOffersFiles, Func<CancellationToken, Task<RoomClient>> connectHost, Func<int, Task<bool>> confirmHost,
            IProgress<(double Fraction, string Title)> progress, CancellationToken cancellation)
        {
            if (await Task.Run(() => IsRunning(version), cancellation).ConfigureAwait(false))
                throw new ModSyncException("GameRunning");

            var needed = comparison.Changes.Where(c => c.Host != null).Select(c => c.Host).ToList();
            var official = source == ModSource.Host || needed.Count == 0
                ? new Dictionary<string, OfficialFile>()
                : await ModrinthLookup.FindAsync(needed.Select(m => m.Sha1), cancellation).ConfigureAwait(false);
            // What Modrinth lacks is looked up on CurseForge by the fingerprints the host listed.
            var rest = needed.Where(m => !official.ContainsKey(m.Sha1)).ToList();
            if (source != ModSource.Host && rest.Count > 0)
            {
                foreach (var pair in await CurseForgeLookup.FindAsync(rest, cancellation).ConfigureAwait(false))
                    official[pair.Key] = pair.Value;
            }

            var fromHost = new List<ModInfo>();
            var unavailable = new List<ModInfo>();
            foreach (var mod in needed.Where(m => !official.ContainsKey(m.Sha1)))
            {
                if (source != ModSource.OfficialOnly && hostOffersFiles)
                    fromHost.Add(mod);
                else
                    unavailable.Add(mod);
            }
            // Returns null when the player declines: nothing has been fetched or changed yet.
            if (fromHost.Count > 0 && !await confirmHost(fromHost.Count).ConfigureAwait(false))
                return null;

            await Task.Run(PruneDownloads, cancellation).ConfigureAwait(false);
            Directory.CreateDirectory(DownloadRoot);
            long total = Math.Max(1, needed.Except(unavailable).Sum(m => m.Size));
            long done = 0;
            var fetched = new Dictionary<ModInfo, string>();
            RoomClient host = null;
            try
            {
                foreach (var mod in needed.Except(unavailable))
                {
                    long start = done;
                    var fileProgress = new Progress<long>(bytes => progress?.Report(((start + bytes) / (double)total, mod.Title)));
                    progress?.Report((start / (double)total, mod.Title));
                    string path = CachePath(mod);
                    if (!IsCached(path, mod))
                    {
                        if (official.TryGetValue(mod.Sha1, out var file))
                        {
                            await ModrinthLookup.DownloadAsync(file, mod, path, fileProgress, cancellation).ConfigureAwait(false);
                        }
                        else
                        {
                            host = host ?? await connectHost(cancellation).ConfigureAwait(false) ?? throw new ModSyncException("HostGone");
                            await DownloadFromHostAsync(host, mod, path, fileProgress, cancellation).ConfigureAwait(false);
                        }
                    }
                    fetched[mod] = path;
                    done += mod.Size;
                }
            }
            finally
            {
                host?.Dispose();
            }

            progress?.Report((1, null));
            return await Task.Run(() => Apply(comparison, version, fetched, removeExtra, unavailable), cancellation).ConfigureAwait(false);
        }

        /// <summary>Whether a game started from <paramref name="version"/> is running.</summary>
        public static bool IsRunning(InstalledVersion version)
        {
            string folder = Path.GetDirectoryName(version.ModsDirectory);
            return RunningGame.Find().Any(g => g.Runs(version) || string.Equals(g.GameDirectory, folder, StringComparison.OrdinalIgnoreCase));
        }

        private static ModSyncResult Apply(ModComparison comparison, InstalledVersion version, Dictionary<ModInfo, string> fetched,
            bool removeExtra, List<ModInfo> unavailable)
        {
            string mods = version.ModsDirectory;
            Directory.CreateDirectory(mods);
            string backup = null;
            int removed = 0;

            void MoveAside(string fileName)
            {
                string path = Path.Combine(mods, fileName);
                if (!File.Exists(path))
                    return;
                if (backup == null)
                {
                    backup = Path.Combine(BackupRoot, SafeFolderName(version.Name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    Directory.CreateDirectory(backup);
                }
                File.Move(path, Unused(Path.Combine(backup, fileName)));
                removed++;
            }

            try
            {
                foreach (var change in comparison.Changes)
                {
                    if (change.Kind == ModDifference.Different && fetched.ContainsKey(change.Host))
                        MoveAside(change.Local.FileName);
                    else if (change.Kind == ModDifference.Extra && removeExtra)
                        MoveAside(change.Local.FileName);
                }

                int installed = 0;
                foreach (var pair in fetched)
                {
                    string target = Path.Combine(mods, pair.Key.FileName);
                    // Another jar with the same name is in the way; it goes aside like a replaced one.
                    if (File.Exists(target))
                        MoveAside(pair.Key.FileName);
                    File.Copy(pair.Value, target);
                    installed++;
                }

                LinkLog.Info($"Mod sync for {version.Name}: {installed} installed, {removed} moved aside, {unavailable.Count} unavailable.");
                return new ModSyncResult(installed, removed, unavailable, backup);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Error("Mod sync could not change the mods folder.", ex);
                throw new ModSyncException(ex is UnauthorizedAccessException ? "NoAccess" : "FolderBusy", ex);
            }
        }

        private static async Task DownloadFromHostAsync(RoomClient host, ModInfo mod, string path, IProgress<long> progress,
            CancellationToken cancellation)
        {
            LinkLog.Info("Fetching " + mod.FileName + " from the host.");
            string temporary = path + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    while (output.Length < mod.Size)
                    {
                        byte[] chunk;
                        try
                        {
                            chunk = await host.GetModChunkAsync(mod.Sha1, output.Length, cancellation).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (RoomClient.IsConnectionError(ex) && !cancellation.IsCancellationRequested)
                        {
                            throw new ModSyncException(ex is InvalidDataException ? "HostRefused" : "HostGone", ex);
                        }
                        if (chunk.Length == 0)
                            break;
                        await output.WriteAsync(chunk, 0, chunk.Length, cancellation).ConfigureAwait(false);
                        progress.Report(output.Length);
                    }
                }
                Verify(temporary, mod);
                Replace(temporary, path);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        /// <summary>Throws unless the file is exactly the jar the host listed.</summary>
        internal static void Verify(string path, ModInfo mod)
        {
            if (new FileInfo(path).Length != mod.Size || ModReader.Sha1(path) != mod.Sha1)
                throw new ModSyncException("Checksum");
        }

        internal static void Replace(string temporary, string path)
        {
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static string CachePath(ModInfo mod) => Path.Combine(DownloadRoot, mod.Sha1 + ".jar");

        private static bool IsCached(string path, ModInfo mod)
        {
            try
            {
                if (!File.Exists(path))
                    return false;
                Verify(path, mod);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return true;
            }
            catch (Exception ex) when (ex is ModSyncException || ex is IOException || ex is UnauthorizedAccessException)
            {
                TryDelete(path);
                return false;
            }
        }

        private static void PruneDownloads()
        {
            try
            {
                if (!Directory.Exists(DownloadRoot))
                    return;
                foreach (var file in new DirectoryInfo(DownloadRoot).GetFiles())
                {
                    if (DateTime.UtcNow - file.LastWriteTimeUtc > KeepDownloads)
                        TryDelete(file.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static string Unused(string path)
        {
            string candidate = path;
            for (int i = 2; File.Exists(candidate); i++)
                candidate = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + " (" + i + ")" + Path.GetExtension(path));
            return candidate;
        }

        private static string SafeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
