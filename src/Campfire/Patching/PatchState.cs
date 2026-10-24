namespace Campfire.Patching
{
    /// <summary>
    /// State of a single patch point inside a launcher assembly.
    /// </summary>
    internal enum PatchState
    {
        /// <summary>The patch point could not be located; the launcher layout is unknown.</summary>
        NotFound,

        /// <summary>The feature is disabled and the patch can be applied.</summary>
        Pending,

        /// <summary>The feature is already enabled, either originally or by a previous patch.</summary>
        Applied,
    }
}
