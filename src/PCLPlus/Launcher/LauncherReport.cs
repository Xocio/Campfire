using System.Collections.Generic;
using PCLPlus.Localization;
using PCLPlus.Patching;

namespace PCLPlus.Launcher
{
    /// <summary>
    /// Result of inspecting a launcher file.
    /// </summary>
    internal sealed class LauncherReport
    {
        public LauncherReport(string path, string version, LauncherStatus status,
            IReadOnlyList<PatchReport> patches, string messageKey, params object[] messageArgs)
        {
            Path = path;
            Version = version;
            Status = status;
            Patches = patches;
            MessageKey = messageKey;
            MessageArgs = messageArgs;
        }

        public string Path { get; }

        /// <summary>Assembly version, or <c>null</c> when the file is not a launcher.</summary>
        public string Version { get; }

        public LauncherStatus Status { get; }

        public IReadOnlyList<PatchReport> Patches { get; }

        public bool CanFix => Status == LauncherStatus.Blocked;

        /// <summary>User-facing explanation of <see cref="Status"/>, in the current display language.</summary>
        public string Message => Text.Format(MessageKey, MessageArgs);

        internal string MessageKey { get; }

        internal object[] MessageArgs { get; }
    }

    /// <summary>
    /// State of one patch inside a <see cref="LauncherReport"/>. Texts follow the current display language.
    /// </summary>
    internal sealed class PatchReport
    {
        private readonly Patch _patch;

        public PatchReport(Patch patch, PatchState state)
        {
            _patch = patch;
            State = state;
        }

        public string Title => _patch.Title;

        public string Description => _patch.Description;

        public string AppliedText => _patch.AppliedText;

        public bool IsRequired => _patch.IsRequired;

        public PatchState State { get; }
    }
}
