using System.Collections.Generic;
using PCLPlus.Patching;

namespace PCLPlus.Launcher
{
    /// <summary>
    /// Result of inspecting a launcher file.
    /// </summary>
    internal sealed class LauncherReport
    {
        public LauncherReport(string path, string version, LauncherStatus status, string message,
            IReadOnlyList<PatchReport> patches)
        {
            Path = path;
            Version = version;
            Status = status;
            Message = message;
            Patches = patches;
        }

        public string Path { get; }

        /// <summary>Assembly version, or <c>null</c> when the file is not a launcher.</summary>
        public string Version { get; }

        public LauncherStatus Status { get; }

        /// <summary>User-facing explanation of <see cref="Status"/>.</summary>
        public string Message { get; }

        public IReadOnlyList<PatchReport> Patches { get; }

        public bool CanFix => Status == LauncherStatus.Blocked;
    }

    /// <summary>
    /// State of one patch inside a <see cref="LauncherReport"/>.
    /// </summary>
    internal sealed class PatchReport
    {
        public PatchReport(Patch patch, PatchState state)
        {
            Title = patch.Title;
            Description = patch.Description;
            AppliedText = patch.AppliedText;
            IsRequired = patch.IsRequired;
            State = state;
        }

        public string Title { get; }

        public string Description { get; }

        public string AppliedText { get; }

        public bool IsRequired { get; }

        public PatchState State { get; }
    }
}
