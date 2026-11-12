using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Campfire.Controls;
using Campfire.Crash;
using Campfire.Localization;
using Campfire.Mods;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// "Crash analysis" page: reads the newest crash report, game log and Java error report of an installed version,
    /// or a file the user picks, and lists the known problems they show with the lines they were found in.
    /// </summary>
    public partial class CrashPage : UserControl
    {
        private static readonly Regex UserFolder = new Regex(@"(?<=[A-Za-z]:[\\/]+Users[\\/]+)[^\\/\r\n]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex SettingUser = new Regex(@"(?<=Setting user: )\S+", RegexOptions.CultureInvariant);

        private List<InstalledVersion> _versions = new List<InstalledVersion>();
        private Dictionary<string, DateTime?> _times = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        private IEnumerable<string> _launcherDirectories;
        private CrashAnalysis _analysis;
        private IReadOnlyList<ModIssue> _folderIssues = Array.Empty<ModIssue>();
        private InstalledVersion _analyzedVersion;
        private string _pickedFile;
        private bool _scanning;
        private bool _analyzing;
        private int _serial;

        public CrashPage()
        {
            InitializeComponent();
            Motion.SetCardScroll(ScrollPage, true);
            Text.LanguageChanged += (sender, e) =>
            {
                FillVersions(Selected);
                Render();
            };
            Render();
        }

        private InstalledVersion Selected
        {
            get
            {
                int index = CmbVersion.SelectedIndex;
                return index >= 0 && index < _versions.Count ? _versions[index] : null;
            }
        }

        /// <summary>Looks for installed versions and picks the one whose game wrote a log or crash report last.</summary>
        internal async Task ShowAsync(IEnumerable<string> launcherDirectories)
        {
            _launcherDirectories = launcherDirectories;
            await ScanVersionsAsync(null);
        }

        private async Task ScanVersionsAsync(InstalledVersion keep)
        {
            if (_scanning)
                return;
            _scanning = true;
            Render();
            try
            {
                var versions = await VersionChoices.ScanAsync(_launcherDirectories);
                var times = await Task.Run(() => versions.ToDictionary(v => v.Directory, v => CrashSources.LatestTime(VersionChoices.GameDirectory(v)),
                    StringComparer.OrdinalIgnoreCase));
                _times = times;
                _versions = versions
                    .OrderByDescending(v => times[v.Directory] ?? DateTime.MinValue)
                    .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            finally
            {
                _scanning = false;
            }
            _pickedFile = null;
            FillVersions(keep);
            await AnalyzeAsync();
        }

        private void FillVersions(InstalledVersion keep)
        {
            CmbVersion.SelectionChanged -= CmbVersion_SelectionChanged;
            CmbVersion.ItemsSource = _versions.Select(v =>
            {
                string label = VersionChoices.Label(v);
                return _times.TryGetValue(v.Directory, out var time) && time.HasValue
                    ? label + " · " + Text.Format("Crash.LastWritten", time.Value.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture))
                    : label;
            }).ToList();
            int index = keep == null ? -1 : _versions.FindIndex(v => string.Equals(v.Directory, keep.Directory, StringComparison.OrdinalIgnoreCase));
            CmbVersion.SelectedIndex = index >= 0 ? index : _versions.Count > 0 ? 0 : -1;
            CmbVersion.SelectionChanged += CmbVersion_SelectionChanged;
        }

        private async void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _pickedFile = null;
            await AnalyzeAsync();
        }

        private async void BtnRescan_Click(object sender, RoutedEventArgs e)
        {
            await ScanVersionsAsync(Selected);
        }

        private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeAsync();
        }

        private async void BtnPickFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Text.Get("Crash.PickFileTitle"),
                Filter = Text.Get("Crash.PickFileFilter") + "|*.txt;*.log|" + Text.Get("Crash.AllFiles") + "|*.*",
                CheckFileExists = true,
            };
            var game = Selected;
            if (game != null && Directory.Exists(Path.Combine(VersionChoices.GameDirectory(game), "crash-reports")))
                dialog.InitialDirectory = Path.Combine(VersionChoices.GameDirectory(game), "crash-reports");
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;
            _pickedFile = dialog.FileName;
            await AnalyzeAsync();
        }

        // ======================== Analysis ========================

        private async Task AnalyzeAsync()
        {
            int serial = ++_serial;
            var version = _pickedFile == null ? Selected : null;
            string picked = _pickedFile;
            _analysis = null;
            _folderIssues = Array.Empty<ModIssue>();
            _analyzedVersion = version;
            if (version == null && picked == null)
            {
                Render();
                return;
            }

            _analyzing = true;
            Render();
            try
            {
                var (analysis, issues) = await Task.Run(() =>
                {
                    IReadOnlyList<CrashFile> files;
                    if (picked != null)
                    {
                        var file = CrashSources.Open(picked);
                        files = file == null ? (IReadOnlyList<CrashFile>)Array.Empty<CrashFile>() : new[] { file };
                    }
                    else
                    {
                        files = CrashSources.Find(VersionChoices.GameDirectory(version), new[] { version.Folder.Path, version.Directory });
                    }
                    var result = CrashAnalyzer.Analyze(files, version);

                    IReadOnlyList<ModIssue> found = Array.Empty<ModIssue>();
                    if (version != null && version.Loader != VersionLoader.Vanilla && Directory.Exists(version.ModsDirectory))
                    {
                        var mods = Directory.EnumerateFiles(version.ModsDirectory, "*.jar", SearchOption.TopDirectoryOnly)
                            .Select(ModManifest.Read).Where(m => m != null).ToList();
                        found = ModAudit.Check(version, mods);
                    }
                    return (result, found);
                });
                if (serial != _serial)
                    return;
                _analysis = analysis;
                _folderIssues = issues;
                Link.LinkLog.Info($"Crash analysis read {analysis.Files.Count} file(s): {analysis.Findings.Count} finding(s), {issues.Count} mods folder finding(s).");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (serial == _serial)
                    LabStatus.Text = Text.Format("Crash.ReadFailed", ex.Message);
            }
            finally
            {
                if (serial == _serial)
                    _analyzing = false;
            }
            Render();
        }

        // ======================== Rendering ========================

        private void Render()
        {
            bool busy = _scanning || _analyzing;
            CmbVersion.IsEnabled = !busy && _versions.Count > 0;
            BtnRescan.IsEnabled = !busy;
            BtnPickFile.IsEnabled = !busy;
            BtnAnalyze.IsEnabled = !busy && (Selected != null || _pickedFile != null);
            BtnCopy.IsEnabled = !busy && _analysis != null && _analysis.Files.Count > 0;
            LabStatus.Text = _scanning ? Text.Get("ModCheck.Scanning") : _analyzing ? Text.Get("Crash.Analyzing") : "";

            RenderFiles();
            RenderResult();
        }

        private void RenderFiles()
        {
            var files = _analysis?.Files ?? Array.Empty<CrashFile>();
            ListFiles.ItemsSource = files.Select(f => new
            {
                Kind = Text.Get("Crash.File." + f.Kind),
                Detail = f.Name + " · " + f.Time.ToString(Text.Get("Backup.TimeFormat"), CultureInfo.InvariantCulture),
                f.Path,
            }).ToList();

            string none = null;
            if (_scanning || _analyzing)
                none = null;
            else if (_versions.Count == 0 && _pickedFile == null)
                none = Text.Get("ModCheck.NoVersions");
            else if (_analysis != null && files.Count == 0)
                none = Text.Get(_pickedFile != null ? "Crash.FileUnreadable" : "Crash.NoFiles");
            LabNoFiles.Text = none ?? "";
            LabNoFiles.Visibility = none == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RenderResult()
        {
            var analysis = _analysis;
            var summary = analysis?.Summary;
            GridSummary.Visibility = summary == null ? Visibility.Collapsed : Visibility.Visible;
            if (summary != null)
            {
                LabGameVersion.Text = summary.GameVersion ?? "—";
                LabJavaVersion.Text = summary.JavaVersion ?? "—";
                LabJavaVersion.ToolTip = summary.JavaVersion;
                LabMaxMemory.Text = summary.MaxMemory ?? "—";
            }

            var rows = new List<FindingRow>();
            if (analysis != null)
            {
                rows.AddRange(analysis.Findings.Select(f => new FindingRow(
                    Text.Format("Crash." + f.Key + ".Title", f.Args),
                    Text.Format("Crash." + f.Key + ".Advice", f.Args),
                    f.Evidence, f.Source ?? Text.Get("Crash.SourceFolder"), f.Mods,
                    Resource(f.Level == FindingLevel.Cause ? "BrushRed" : "BrushOrange"))));
            }
            var version = _analyzedVersion;
            foreach (var issue in _folderIssues.Where(i => i.IsError).Take(8))
            {
                rows.Add(new FindingRow(
                    Text.Format("Crash.Folder.Title", issue.Mod.Title),
                    ModManagerPage.Describe(issue, version, version?.Loader.ToString() ?? ""),
                    new[] { issue.Mod.FileName }, Text.Get("Crash.SourceFolder"), new[] { issue.Mod.FileName },
                    Resource("BrushRed")));
            }
            ListFindings.ItemsSource = rows;

            int causes = (analysis?.Findings.Count(f => f.Level == FindingLevel.Cause) ?? 0) + _folderIssues.Count(i => i.IsError);
            string hint;
            string style = "PclHint";
            if (_scanning || _analyzing)
                hint = Text.Get("Crash.Analyzing");
            else if (analysis == null)
                hint = Text.Get(_versions.Count == 0 ? "ModCheck.NoVersions" : "Crash.PickVersion");
            else if (analysis.Files.Count == 0 && rows.Count == 0)
                hint = Text.Get("Crash.NothingToRead");
            else if (rows.Count == 0)
            {
                hint = analysis.LastRunClean && !analysis.Files.Any(f => f.Kind == CrashFileKind.CrashReport && f.Time > LogTime(analysis))
                    ? Text.Get("Crash.LastRunClean")
                    : summary?.Headline != null
                        ? Text.Format("Crash.UnknownWithError", summary.RootCause == null ? summary.Headline : summary.Headline + "\n" + summary.RootCause)
                        : Text.Get("Crash.Unknown");
                style = analysis.LastRunClean ? "PclHintGreen" : "PclHint";
            }
            else if (causes > 0)
            {
                hint = Text.Format("Crash.Found", causes);
                style = "PclHintRed";
            }
            else
            {
                hint = Text.Get("Crash.OnlyHints");
                style = "PclHintOrange";
            }
            LabResult.Text = hint;
            HintResult.Style = (Style)FindResource(style);

            var stack = analysis?.StackMods ?? Array.Empty<string>();
            LabStackMods.Visibility = stack.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            LabStackMods.Text = stack.Count == 0 ? "" : Text.Format("Crash.StackMods", string.Join(Text.Get("ModCheck.Separator"), stack));
        }

        private static DateTime LogTime(CrashAnalysis analysis)
        {
            return analysis.Files.FirstOrDefault(f => f.Kind == CrashFileKind.GameLog)?.Time ?? DateTime.MinValue;
        }

        // ======================== Actions ========================

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is string path) || !File.Exists(path))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }

        /// <summary>Copies the result as plain text, with the Windows user name and the game account name left out.</summary>
        private async void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            var analysis = _analysis;
            if (analysis == null)
                return;
            var text = new StringBuilder();
            text.AppendLine(Text.Get("Crash.CopyHeader"));
            if (_analyzedVersion != null)
                text.AppendLine(Text.Get("Crash.Version") + ": " + VersionChoices.Label(_analyzedVersion));
            var summary = analysis.Summary;
            if (summary != null)
            {
                if (summary.Description != null)
                    text.AppendLine("Description: " + summary.Description);
                if (summary.Headline != null)
                    text.AppendLine(summary.Headline);
                if (summary.RootCause != null)
                    text.AppendLine(summary.RootCause);
                text.AppendLine(Text.Get("Crash.GameVersion") + ": " + (summary.GameVersion ?? "—") + " · "
                                + Text.Get("Crash.JavaVersion") + ": " + (summary.JavaVersion ?? "—") + " · "
                                + Text.Get("Crash.MaxMemory") + ": " + (summary.MaxMemory ?? "—"));
            }
            foreach (var file in analysis.Files)
                text.AppendLine(Text.Get("Crash.File." + file.Kind) + ": " + file.Name + " · " + file.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            text.AppendLine();

            foreach (FindingRow row in ListFindings.ItemsSource as IEnumerable<FindingRow> ?? Array.Empty<FindingRow>())
            {
                text.AppendLine("■ " + row.Title);
                text.AppendLine(row.Advice);
                if (row.Mods.Length > 0)
                    text.AppendLine(row.Mods);
                foreach (string line in row.Evidence.Split('\n'))
                    text.AppendLine("    " + line);
                text.AppendLine();
            }
            if (analysis.StackMods.Count > 0)
                text.AppendLine(LabStackMods.Text);
            if (ListFindings.Items.Count == 0)
                text.AppendLine(LabResult.Text);

            string masked = SettingUser.Replace(UserFolder.Replace(text.ToString(), "<user>"), "<player>");
            try
            {
                Clipboard.SetText(masked);
                LabStatus.Text = Text.Get("Crash.Copied");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("Link.CopyFailedTitle"), Text.Get("Link.CopyFailed"));
            }
        }

        private static Brush Resource(string key)
        {
            return (Brush)(Application.Current.MainWindow?.TryFindResource(key) ?? Application.Current.FindResource(key));
        }

        private sealed class FindingRow
        {
            public FindingRow(string title, string advice, IEnumerable<string> evidence, string source, IEnumerable<string> mods, Brush accent)
            {
                Title = title;
                Advice = advice;
                Evidence = string.Join("\n", (evidence ?? Array.Empty<string>()).Select(l => UserFolder.Replace(l, "<user>")));
                Source = Text.Format("Crash.FoundIn", source);
                var list = (mods ?? Array.Empty<string>()).ToList();
                Mods = list.Count == 0 ? "" : Text.Format("Crash.RelatedMods", string.Join(Text.Get("ModCheck.Separator"), list));
                ModsVisibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                Accent = accent;
            }

            public string Title { get; }

            public string Advice { get; }

            public string Evidence { get; }

            public string Source { get; }

            public string Mods { get; }

            public Visibility ModsVisibility { get; }

            public Brush Accent { get; }
        }
    }
}
