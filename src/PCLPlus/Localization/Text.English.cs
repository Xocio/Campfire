using System.Collections.Generic;

namespace PCLPlus.Localization
{
    public static partial class Text
    {
        private static readonly Dictionary<string, string> English = Table(
            // Title bar
            "App.Tag", "Repair Tool",
            "App.Minimize", "Minimize",
            "App.Close", "Close",
            "App.LanguageButton", "中文",
            "App.LanguageTooltip", "切换为中文",

            // Main page
            "Main.Launcher", "Launcher",
            "Main.NoLauncher", "No launcher selected",
            "Main.PickHint", "Click Browse to select Plain Craft Launcher 2.exe, or drag it into this window",
            "Main.Browse", "Browse",
            "Main.OpenFolder", "Open folder",
            "Main.LaunchPcl", "Launch PCL",
            "Main.Patches", "Repairs",
            "Main.About", "About",
            "Main.AboutTooltip", "Version, author and source code",
            "Main.Restore", "Restore",
            "Main.RestoreTooltip", "Restore the launcher from its backup",
            "Main.Fix", "Repair",
            "Main.Fixing", "Repairing…",
            "Main.Fixed", "Repaired",

            // Status messages
            "Status.Searching", "Looking for the launcher…",
            "Status.NotFoundNearby", "No launcher found in the PCL++ folder. Click Browse to select it.",
            "Status.Inspecting", "Checking the launcher…",
            "Status.InspectFailed", "Check failed: {0}",
            "Status.Fixing", "Repairing, please wait…",
            "Status.FixDone", "Done! Launch PCL to download the game. The original file was backed up as {0}.",
            "Status.FixUnexpected", "Unexpected error; the launcher was not changed: {0}",
            "Status.Restoring", "Restoring…",
            "Status.Restored", "The launcher has been restored to its original version.",
            "Status.Unexpected", "Unexpected error: {0}",
            "Status.LaunchFailed", "Failed to launch: {0}",

            // Dialogs
            "Dialog.SelectTitle", "Select the PCL launcher",
            "Dialog.SelectFilter", "PCL launcher (*.exe)|*.exe",
            "Dialog.BrowserFailed", "Could not open the browser. Please visit:\n{0}\n\n{1}",

            // About page
            "About.Header", "About",
            "About.Summary", "Enhancement and repair toolkit for the official PCL. Free and open source.",
            "About.AuthorRole", "Author · github.com/Xocio",
            "About.Homepage", "Profile",
            "About.RepoSummary", "Source code, issues and downloads",
            "About.Repository", "Repository",
            "About.Notes", "Notes",
            "About.Note1", "· PCL++ is a community project, not affiliated with PCL or its author.",
            "About.Note2", "· Repairs run locally and nothing is uploaded or downloaded. The original launcher is backed up and can be restored at any time.",
            "About.Note3", "· A repaired launcher no longer updates itself. Run PCL++ again after upgrading PCL.",
            "About.Back", "Back",

            // Patch states
            "PatchState.Pending", "Pending",
            "PatchState.Restored", "Restored",
            "PatchState.Disabled", "Disabled",
            "PatchState.Normal", "OK",
            "PatchState.NotNeeded", "Not needed",
            "PatchState.NotFound", "Not found",

            // Patches
            "Patch.VersionList.Title", "Minecraft version list",
            "Patch.VersionList.Description", "Restores the version list and game installation on the Download page",
            "Patch.ModpackPicker.Title", "Install modpack from file",
            "Patch.ModpackPicker.Description", "Restores file selection for the Install Modpack button",
            "Patch.ModpackDispatch.Title", "Modpack installation",
            "Patch.ModpackDispatch.Description", "Restores CurseForge, Modrinth, MCBBS and other modpack formats",
            "Patch.UpdateBlock.Title", "Disable auto-update",
            "Patch.UpdateBlock.Description", "Keeps an automatic update from disabling downloads again",

            // Launcher inspection
            "Inspect.FileMissing", "The file does not exist.",
            "Inspect.NotOfficial", "This is not the official PCL. Downloads work in the Community Edition and other launchers; no repair needed.",
            "Inspect.ReadFailed", "Cannot read the file: {0}",
            "Inspect.AccessDenied", "Access to the file is denied.",
            "Inspect.Patched", "Downloads have been restored by PCL++.",
            "Inspect.NotBlocked", "Downloads work in this version; no repair needed.",
            "Inspect.Blocked", "Downloads are disabled in this version. Click Repair to restore them.",
            "Inspect.Unsupported", "Downloads are disabled in this version, but its layout is not recognized yet. Repair is not supported.",

            // Repair and restore
            "Fix.PatchFailed", "Failed to apply \"{0}\": {1}",
            "Fix.WriteFailed", "Failed to write the file: {0}\nIf the launcher is in a protected folder such as Program Files, run PCL++ as administrator.",
            "Fix.NoBackup", "No backup found; nothing to restore.",
            "Fix.BadBackup", "The backup is not a PCL launcher; restore cancelled.",
            "Fix.RestoreFailed", "Restore failed: {0}",
            "Fix.ReadFailed", "Cannot read the launcher: {0}",
            "Fix.Running", "PCL is running. Close it and try again.",
            "Fix.BuildFailed", "Failed to build the repaired launcher: {0}",
            "Fix.VerifyFailed", "The repaired launcher failed verification; the original file was not changed.");
    }
}
