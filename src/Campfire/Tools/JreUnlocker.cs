using System;
using System.IO;
using System.Linq;
using System.Reflection;
using dnlib.DotNet;
using Campfire.Launcher;
using Campfire.Localization;
using Campfire.Patching;

namespace Campfire.Tools
{
    /// <summary>
    /// Overall state of a launcher file as seen by the JRE tool.
    /// </summary>
    internal enum JreState
    {
        /// <summary>The file is missing, unreadable or not a community launcher.</summary>
        NotCommunity,

        /// <summary>The community launcher switches off JREs and the tool can allow them.</summary>
        Locked,

        /// <summary>JREs are allowed, either by this tool or because the release never had the rule.</summary>
        Unlocked,

        /// <summary>The release has the rule, but its code is not recognized.</summary>
        Unsupported,
    }

    /// <summary>
    /// Result of inspecting a launcher file for the JRE tool.
    /// </summary>
    internal sealed class JreReport
    {
        public JreReport(string path, string version, JreState state, string messageKey, params object[] messageArgs)
        {
            Path = path;
            Version = version;
            State = state;
            MessageKey = messageKey;
            MessageArgs = messageArgs;
        }

        public string Path { get; }

        /// <summary>Release number of the launcher, or <c>null</c> when the file is not a community launcher.</summary>
        public string Version { get; }

        public JreState State { get; }

        public bool CanUnlock => State == JreState.Locked;

        /// <summary>User-facing explanation of <see cref="State"/>, in the current display language.</summary>
        public string Message => Text.Format(MessageKey, MessageArgs);

        /// <summary>Whether the release has no JRE rule to remove, or the tool already removed it.</summary>
        public bool IsDone => State == JreState.Unlocked;

        internal string MessageKey { get; }

        internal object[] MessageArgs { get; }
    }

    /// <summary>
    /// Allows JREs in PCL Community Edition by rewriting the Java default check in place.
    /// </summary>
    /// <remarks>
    /// This is a tool of its own and does not take part in the launcher repair. Its backup lives next to the
    /// launcher under a different name, so the two can be restored independently.
    /// </remarks>
    internal static class JreUnlocker
    {
        private const string BackupExtension = ".jre.bak";

        public static string GetBackupPath(string launcherPath)
        {
            return launcherPath + BackupExtension;
        }

        public static bool HasBackup(string launcherPath)
        {
            return File.Exists(GetBackupPath(launcherPath));
        }

        /// <summary>Finds a community launcher in <paramref name="directory"/>, or returns <c>null</c>.</summary>
        public static string FindIn(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;

            string self = Assembly.GetExecutingAssembly().Location;
            try
            {
                return Directory.EnumerateFiles(directory, "*.exe")
                    .Where(f => !string.Equals(f, self, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(CommunityLauncher.IsCommunityLauncher);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static JreReport Inspect(string path)
        {
            if (!File.Exists(path))
                return NotCommunity(path, "Inspect.FileMissing");

            byte[] data;
            try
            {
                if (new FileInfo(path).Length > LauncherFile.MaxBundleSize)
                    return NotCommunity(path, "Jre.Message.NotCommunity");
                data = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                return NotCommunity(path, "Inspect.ReadFailed", ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return NotCommunity(path, "Inspect.AccessDenied");
            }

            return Inspect(path, data);
        }

        /// <summary>Allows JREs and returns the report of the patched file.</summary>
        public static JreReport Unlock(string path)
        {
            LauncherFile.EnsureNotRunning(path);
            byte[] data = LauncherFile.Read(path);

            byte[] patched;
            using (var image = BundleScanner.FindAssembly(data, JreRestriction.CoreAssemblyName))
            {
                if (image == null)
                    throw new LauncherException("Jre.Message.NotCommunity");

                var report = BuildReport(path, image);
                if (!report.CanUnlock)
                    throw new LauncherException(report.MessageKey, report.MessageArgs);

                patched = Apply(data, image);
            }

            // Read the patched bytes back before touching the file, so a failed edit never replaces the launcher.
            var verified = Inspect(path, patched);
            if (verified.State != JreState.Unlocked)
                throw new LauncherException("Fix.VerifyFailed");

            LauncherFile.Replace(path, GetBackupPath(path), patched);
            return verified;
        }

        /// <summary>Replaces the launcher with the backup made by <see cref="Unlock"/>.</summary>
        public static JreReport Restore(string path)
        {
            string backupPath = GetBackupPath(path);
            if (!File.Exists(backupPath))
                throw new LauncherException("Fix.NoBackup");

            if (Inspect(backupPath).State == JreState.NotCommunity)
                throw new LauncherException("Fix.BadBackup");

            LauncherFile.Restore(path, backupPath);
            return Inspect(path);
        }

        private static JreReport Inspect(string path, byte[] data)
        {
            using (var image = BundleScanner.FindAssembly(data, JreRestriction.CoreAssemblyName))
            {
                return image == null
                    ? NotCommunity(path, "Jre.Message.NotCommunity")
                    : BuildReport(path, image);
            }
        }

        private static JreReport BuildReport(string path, BundleImage image)
        {
            string version = LauncherFile.ReadBundleVersion(path, image);

            // Releases from before the Java manager never switched JREs off.
            if (!JreRestriction.HasJavaManager(image.Module))
                return new JreReport(path, version, JreState.Unlocked, "Jre.Message.NotNeeded");

            var target = JreRestriction.FindTarget(image.Module);
            switch (target == null ? RestrictionState.Unknown : JreRestriction.GetState(target))
            {
                case RestrictionState.Present:
                    return new JreReport(path, version, JreState.Locked, "Jre.Message.Locked");
                case RestrictionState.Removed:
                    return new JreReport(path, version, JreState.Unlocked, "Jre.Message.Unlocked");
                default:
                    return new JreReport(path, version, JreState.Unsupported, "Jre.Message.Unsupported");
            }
        }

        private static byte[] Apply(byte[] data, BundleImage image)
        {
            MethodDef target = JreRestriction.FindTarget(image.Module);
            var edits = target == null ? null : JreRestriction.FindEdits(target);
            if (edits == null)
                throw new LauncherException("Jre.Message.Unsupported");

            int bodyOffset = BundleScanner.MapRvaToOffset(data, image.Start, (int)target.RVA);
            var body = BundleScanner.LocateBody(data, bodyOffset);
            int codeStart = bodyOffset + body.HeaderSize;

            // Every edit keeps the size of the instruction it replaces, so the header and all offsets stay valid.
            var patched = (byte[])data.Clone();
            foreach (var edit in edits)
            {
                byte[] bytes = edit.Bytes;
                if (edit.Offset + bytes.Length > body.CodeSize)
                    throw new LauncherException("Jre.Message.Unsupported");
                Buffer.BlockCopy(bytes, 0, patched, codeStart + edit.Offset, bytes.Length);
            }
            return patched;
        }

        private static JreReport NotCommunity(string path, string messageKey, params object[] args)
        {
            return new JreReport(path, null, JreState.NotCommunity, messageKey, args);
        }
    }
}
