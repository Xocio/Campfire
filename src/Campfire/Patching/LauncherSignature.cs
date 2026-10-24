namespace Campfire.Patching
{
    /// <summary>
    /// Well-known identifiers of the Plain Craft Launcher 2 assembly.
    /// </summary>
    internal static class LauncherSignature
    {
        /// <summary>Assembly name of the official launcher build.</summary>
        public const string AssemblyName = "Plain Craft Launcher 2";

        /// <summary>Default file name of the official launcher build.</summary>
        public const string DefaultFileName = "Plain Craft Launcher 2.exe";

        /// <summary>A fragment shared by every message the launcher shows when the download feature is disabled.</summary>
        public const string BlockMessageFragment = "不再提供 MC 下载功能";

        /// <summary>Name of the loader task the launcher uses in place of the Minecraft version list.</summary>
        public const string DisabledLoaderName = "关闭下载";

        /// <summary>
        /// Name of the resource Campfire embeds into a patched launcher. Launchers patched by earlier versions carry
        /// this name, so it must stay as it is.
        /// </summary>
        public const string PatchMarkerResource = "PCLPlus.PatchInfo";

        /// <summary>Returns whether <paramref name="text"/> is a "download disabled" message.</summary>
        /// <remarks>Changelog entries about removed features contain the fragment too, but are not block points.</remarks>
        public static bool IsBlockMessage(string text)
        {
            return text != null
                && text.Contains(BlockMessageFragment)
                && !text.StartsWith("删除：");
        }
    }
}
