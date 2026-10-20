using System.Windows;
using System.Windows.Media;
using PCLPlus.Launcher;
using PCLPlus.Localization;
using PCLPlus.Patching;

namespace PCLPlus
{
    /// <summary>
    /// Display model of one patch in the "Repairs" card. Rebuilt whenever the display language changes.
    /// </summary>
    internal sealed class PatchRow
    {
        private static readonly Geometry CheckIcon = Geometry.Parse("M3,8.5 L6.5,12 L13,4.5");
        private static readonly Geometry PendingIcon = Geometry.Parse("M8,4 L8,9 M8,11.5 L8,12");
        private static readonly Geometry UnknownIcon = Geometry.Parse("M4.5,8 L11.5,8");

        private PatchRow(string title, string description, string stateText, Brush accent, Geometry icon)
        {
            Title = title;
            Description = description;
            StateText = stateText;
            Accent = accent;
            Icon = icon;
        }

        public string Title { get; }

        public string Description { get; }

        public string StateText { get; }

        public Brush Accent { get; }

        public Geometry Icon { get; }

        public static PatchRow From(PatchReport report, LauncherStatus launcherStatus)
        {
            switch (report.State)
            {
                case PatchState.Applied:
                    return new PatchRow(report.Title, report.Description,
                        launcherStatus == LauncherStatus.Patched ? report.AppliedText : Text.Get("PatchState.Normal"),
                        Resource("BrushGreen"), CheckIcon);
                case PatchState.Pending:
                    return new PatchRow(report.Title, report.Description,
                        Text.Get("PatchState.Pending"), Resource("Brush3"), PendingIcon);
                default:
                    return new PatchRow(report.Title, report.Description,
                        Text.Get(launcherStatus == LauncherStatus.NotBlocked ? "PatchState.NotNeeded" : "PatchState.NotFound"),
                        Resource("BrushGray4"), UnknownIcon);
            }
        }

        /// <summary>Placeholder rows shown before a launcher is selected.</summary>
        public static PatchRow Placeholder(Patch patch)
        {
            return new PatchRow(patch.Title, patch.Description, "—", Resource("BrushGray5"), UnknownIcon);
        }

        private static Brush Resource(string key)
        {
            return (Brush)Application.Current.FindResource(key);
        }
    }
}
