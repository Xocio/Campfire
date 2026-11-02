using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Worlds;

namespace Campfire.Mods
{
    /// <summary>The host's voice chat as the room service hands it to players.</summary>
    internal sealed class VoiceOffer
    {
        public VoiceOffer(bool ready, int port, string gameVersion, VersionLoader? loader, ModInfo mod)
        {
            Ready = ready;
            Port = port;
            GameVersion = gameVersion;
            Loader = loader;
            Mod = mod;
        }

        /// <summary>Whether the voice server of the host's game is running.</summary>
        public bool Ready { get; }

        /// <summary>The UDP port of the voice server.</summary>
        public int Port { get; }

        /// <summary>The host's game version, or <c>null</c>.</summary>
        public string GameVersion { get; }

        /// <summary>The host's mod loader, or <c>null</c>.</summary>
        public VersionLoader? Loader { get; }

        /// <summary>The voice chat jar the host plays with, or <c>null</c> while it is not installed yet.</summary>
        public ModInfo Mod { get; }

        public Dictionary<string, object> ToJson()
        {
            var json = new Dictionary<string, object>
            {
                ["ready"] = Ready,
                ["port"] = (double)Port,
            };
            if (GameVersion != null)
                json["game"] = GameVersion;
            if (Loader != null)
                json["loader"] = Loader.Value.ToString().ToLowerInvariant();
            if (Mod != null)
                json["mod"] = Mod.ToJson();
            return json;
        }

        /// <summary>Reads what the host sent; returns <c>null</c> when it is malformed.</summary>
        public static VoiceOffer FromJson(object value)
        {
            if (!(value is Dictionary<string, object> json))
                return null;
            if (!(json.TryGetValue("port", out object rawPort) && rawPort is double port) || port < 1 || port > 65535 || port != Math.Floor(port))
                return null;
            bool ready = json.TryGetValue("ready", out object r) && r is bool b && b;
            string game = json.TryGetValue("game", out object g) && g is string text && text.Length > 0 && text.Length <= 64 ? text : null;
            VersionLoader? loader = json.TryGetValue("loader", out object l) && Enum.TryParse(l as string, true, out VersionLoader parsed)
                                   && Enum.IsDefined(typeof(VersionLoader), parsed)
                ? parsed
                : (VersionLoader?)null;
            var mod = json.TryGetValue("mod", out object m) ? ModInfo.FromJson(m) : null;
            return new VoiceOffer(ready, (int)port, game, loader, mod);
        }
    }

    /// <summary>What <see cref="VoiceMod.InstallAsync"/> did.</summary>
    internal enum VoiceInstall
    {
        /// <summary>The mod was already in the folder.</summary>
        Present,

        /// <summary>A disabled copy of the mod was enabled.</summary>
        Enabled,

        /// <summary>The mod was downloaded into the folder.</summary>
        Installed,
    }

    /// <summary>
    /// The voice chat mod Simple Voice Chat: finding it in a mods folder, installing it from Modrinth and letting it
    /// run in worlds opened to LAN.
    /// </summary>
    internal static class VoiceMod
    {
        public const string ModId = "voicechat";
        public const string Title = "Simple Voice Chat";

        /// <summary>The Modrinth project of Simple Voice Chat.</summary>
        private const string ProjectId = "9eGKb6K1";

        private const string DisabledSuffix = ".disabled";

        private static readonly Regex LocalServerOff = new Regex(@"(?m)^(\s*run_local_server\s*[=:]\s*)false(?=\s*$)", RegexOptions.CultureInvariant);

        /// <summary>The lines the voice server writes when it opens or moves its UDP port.</summary>
        private static readonly Regex ServerPortLine = new Regex(
            @"\[voicechat\] (?:Voice chat server started at (?:port )?(?:\S*:)?|Changing voice chat port to )(\d{1,5})\b",
            RegexOptions.CultureInvariant);

        /// <summary>How much of the end of the game log is read.</summary>
        private const int LogTail = 1024 * 1024;

        /// <summary>
        /// The UDP port the voice server of the game in <paramref name="gameDirectory"/> last reported in the game
        /// log, or <c>null</c> when the log does not tell.
        /// </summary>
        public static int? ReadServerPort(string gameDirectory)
        {
            string path = Path.Combine(gameDirectory, "logs", "latest.log");
            try
            {
                if (!File.Exists(path))
                    return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length > LogTail)
                        stream.Position = stream.Length - LogTail;
                    string text;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        text = reader.ReadToEnd();
                    var matches = ServerPortLine.Matches(text);
                    if (matches.Count == 0)
                        return null;
                    int port = int.Parse(matches[matches.Count - 1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return port > 0 && port < 65536 ? port : (int?)null;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static bool IsVoiceMod(ModInfo mod)
        {
            return mod != null && mod.Ids.Any(id => id.Equals(ModId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The enabled voice chat jar in <paramref name="modsDirectory"/>, or <c>null</c>.</summary>
        public static ModInfo Find(string modsDirectory)
        {
            return ModList.ReadFolder(modsDirectory).FirstOrDefault(IsVoiceMod);
        }

        /// <summary>
        /// Puts the voice chat mod into <paramref name="modsDirectory"/>: an enabled jar is kept, a disabled one is enabled,
        /// otherwise <paramref name="preferred"/> or the newest release for the game version and loader is downloaded.
        /// </summary>
        /// <param name="preferred">The jar to match, such as the host's, or <c>null</c>.</param>
        public static async Task<(VoiceInstall Result, ModInfo Mod)> InstallAsync(string modsDirectory, string gameVersion, VersionLoader? loader,
            ModInfo preferred, CancellationToken cancellation)
        {
            try
            {
                var present = await Task.Run(() => Find(modsDirectory), cancellation).ConfigureAwait(false);
                if (present != null)
                    return (VoiceInstall.Present, present);
                var enabled = await Task.Run(() => EnableDisabled(modsDirectory), cancellation).ConfigureAwait(false);
                if (enabled != null)
                {
                    LinkLog.Info("Enabled " + enabled.FileName + " in " + modsDirectory + ".");
                    return (VoiceInstall.Enabled, enabled);
                }

                OfficialFile file = null;
                ModInfo mod = null;
                if (preferred != null && (await ModrinthLookup.FindAsync(new[] { preferred.Sha1 }, cancellation).ConfigureAwait(false))
                        .TryGetValue(preferred.Sha1, out file))
                    mod = preferred;
                if (mod == null)
                {
                    if (string.IsNullOrEmpty(gameVersion) || loader == null)
                        throw new ModSyncException("VoiceNoVersion");
                    if (loader == VersionLoader.Vanilla)
                        throw new ModSyncException("VoiceNoLoader");
                    var found = await ModrinthLookup.FindNewestAsync(ProjectId, gameVersion, LoaderNames(loader.Value), new[] { ModId }, cancellation)
                        .ConfigureAwait(false);
                    if (found == null)
                        throw new ModSyncException("VoiceNotFound");
                    (file, mod) = found.Value;
                }

                Directory.CreateDirectory(ModSync.DownloadRoot);
                string cache = ModSync.CachePath(mod);
                if (!ModSync.IsCached(cache, mod))
                    await ModrinthLookup.DownloadAsync(file, mod, cache, new Progress<long>(), cancellation).ConfigureAwait(false);
                await Task.Run(() => Copy(cache, modsDirectory, mod.FileName), cancellation).ConfigureAwait(false);
                LinkLog.Info("Installed " + mod.FileName + " into " + modsDirectory + ".");
                return (VoiceInstall.Installed, mod);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Error("The voice chat mod could not be installed.", ex);
                throw new ModSyncException(ex is UnauthorizedAccessException ? "NoAccess" : "FolderBusy", ex);
            }
        }

        /// <summary>
        /// Lets the voice chat run in singleplayer and LAN worlds of the game in <paramref name="gameDirectory"/>.
        /// Returns whether its client settings were changed.
        /// </summary>
        public static bool EnableLocalServer(string gameDirectory)
        {
            string path = Path.Combine(gameDirectory, "config", "voicechat", "voicechat-client.properties");
            try
            {
                if (!File.Exists(path))
                    return false;
                // Latin-1 keeps every byte as it is.
                var latin1 = Encoding.GetEncoding(28591);
                string text = latin1.GetString(File.ReadAllBytes(path));
                if (!LocalServerOff.IsMatch(text))
                    return false;
                File.WriteAllBytes(path, latin1.GetBytes(LocalServerOff.Replace(text, "${1}true")));
                LinkLog.Info("Turned on the voice chat for singleplayer and LAN worlds in " + path + ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LinkLog.Warn("The voice chat settings could not be changed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Modrinth loader names that can run a build for <paramref name="loader"/>.</summary>
        private static IReadOnlyList<string> LoaderNames(VersionLoader loader)
        {
            switch (loader)
            {
                case VersionLoader.Quilt:
                    return new[] { "quilt", "fabric" };
                case VersionLoader.Forge:
                    return new[] { "forge" };
                case VersionLoader.NeoForge:
                    return new[] { "neoforge" };
                default:
                    return new[] { "fabric" };
            }
        }

        /// <summary>Renames a disabled voice chat jar back to a jar; returns it, or <c>null</c> when there is none.</summary>
        private static ModInfo EnableDisabled(string modsDirectory)
        {
            if (!Directory.Exists(modsDirectory))
                return null;
            foreach (var file in new DirectoryInfo(modsDirectory).GetFiles("*" + DisabledSuffix, SearchOption.TopDirectoryOnly))
            {
                string name = file.Name.Substring(0, file.Name.Length - DisabledSuffix.Length);
                if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    name += ".jar";
                string target = Path.Combine(modsDirectory, name);
                if (File.Exists(target) || !IsVoiceMod(ModReader.Read(file.FullName)))
                    continue;
                File.Move(file.FullName, target);
                return ModReader.Read(target);
            }
            return null;
        }

        private static void Copy(string source, string modsDirectory, string fileName)
        {
            Directory.CreateDirectory(modsDirectory);
            string target = Path.Combine(modsDirectory, fileName);
            for (int i = 2; File.Exists(target); i++)
                target = Path.Combine(modsDirectory, Path.GetFileNameWithoutExtension(fileName) + " (" + i + ")" + Path.GetExtension(fileName));
            File.Copy(source, target);
        }
    }
}
