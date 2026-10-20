namespace PCLPlus.Launcher
{
    /// <summary>
    /// Overall state of a launcher file.
    /// </summary>
    internal enum LauncherStatus
    {
        /// <summary>The file is not the official Plain Craft Launcher 2.</summary>
        NotLauncher,

        /// <summary>The download feature is disabled and PCL++ can restore it.</summary>
        Blocked,

        /// <summary>The download feature has already been restored by PCL++.</summary>
        Patched,

        /// <summary>The download feature works in this version; nothing to do.</summary>
        NotBlocked,

        /// <summary>The download feature is disabled, but the launcher layout is not recognised.</summary>
        Unsupported,
    }
}
