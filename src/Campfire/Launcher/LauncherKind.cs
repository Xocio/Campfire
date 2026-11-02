namespace Campfire.Launcher
{
    /// <summary>
    /// Which launcher family a file belongs to.
    /// </summary>
    internal enum LauncherKind
    {
        /// <summary>The official launcher: one managed executable that dnlib can rewrite as a whole.</summary>
        Official,

        /// <summary>The community launcher: a .NET single-file bundle whose assemblies are patched in place.</summary>
        Community,
    }
}
