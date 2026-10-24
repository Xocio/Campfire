namespace Campfire.Launcher
{
    /// <summary>
    /// Which launcher family a file belongs to.
    /// </summary>
    internal enum LauncherKind
    {
        /// <summary>Official PCL: one managed executable that dnlib can rewrite as a whole.</summary>
        Official,

        /// <summary>PCL Community Edition: a .NET single-file bundle whose assemblies are patched in place.</summary>
        Community,
    }
}
