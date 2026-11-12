using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Campfire.Controls;
using Campfire.Link;
using Campfire.Localization;
using Campfire.Mods;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// "Mod check" page: reads the mods folder of an installed version, lists what would stop the game from starting
    /// and finds newer versions of the mods on Modrinth.
    /// </summary>
    public partial class ModManagerPage : UserControl
    {
        private const string VersionKey = "modcheck.version";

        private List<InstalledVersion> _versions = new List<InstalledVersion>();
        private IEnumerable<string> _launcherDirectories;
        private IReadOnlyList<ModManifest> _mods = Array.Empty<ModManifest>();
        private IReadOnlyList<ModIssue> _issues = Array.Empty<ModIssue>();
        private List<UpdateRow> _updates;
        private bool _scanning;
        private bool _reading;
        private bool _checking;
        private bool _updating;
        private int _serial;
        private string _updateError;

        public ModManagerPage()
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

        /// <summary>Looks for installed versions again, also next to the given launcher folders.</summary>
        internal async Task ShowAsync(IEnumerable<string> launcherDirectories)
        {
            _launcherDirectories = launcherDirectories;
            await ScanVersionsAsync();
        }

        private async Task ScanVersionsAsync()
        {
            if (_scanning)
                return;
            _scanning = true;
            Render();
            var previous = Selected;
            try
            {
                _versions = (await VersionChoices.ScanAsync(_launcherDirectories))
                    .OrderBy(v => v.Loader == VersionLoader.Vanilla ? 1 : 0)
                    .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            finally
            {
                _scanning = false;
            }
            FillVersions(previous);
            await ReadModsAsync();
        }

        private void FillVersions(InstalledVersion keep)
        {
            _serial++;
            CmbVersion.SelectionChanged -= CmbVersion_SelectionChanged;
            CmbVersion.ItemsSource = _versions.Select(VersionChoices.Label).ToList();
            int index = keep == null ? -1 : _versions.FindIndex(v => string.Equals(v.Directory, keep.Directory, StringComparison.OrdinalIgnoreCase));
            CmbVersion.SelectedIndex = index >= 0 ? index : VersionChoices.Remembered(_versions, VersionKey, v => v.Loader != VersionLoader.Vanilla);
            CmbVersion.SelectionChanged += CmbVersion_SelectionChanged;
        }

        private async void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var version = Selected;
            if (version != null)
                AppSettings.Set(VersionKey, version.Directory);
            await ReadModsAsync();
        }

        private async void BtnRescan_Click(object sender, RoutedEventArgs e)
        {
            await ScanVersionsAsync();
        }

        private async void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            string picked = FolderPicker.Pick(new WindowInteropHelper(Window.GetWindow(this)).Handle, Text.Get("World.AddFolderTitle"));
            if (picked == null)
                return;
            string folder = GameFolders.Resolve(picked);
            if (folder == null)
            {
                _ = Dialogs.Of(this)?.AlertAsync(Text.Get("World.AddFolderInvalidTitle"), Text.Get("World.AddFolderInvalid"));
                return;
            }
            GameFolders.Add(folder);
            await ScanVersionsAsync();
        }

        // ======================== Checking ========================

        private async Task ReadModsAsync()
        {
            int serial = ++_serial;
            var version = Selected;
            _mods = Array.Empty<ModManifest>();
            _issues = Array.Empty<ModIssue>();
            _updates = null;
            _updateError = null;
            if (version == null || version.Loader == VersionLoader.Vanilla)
            {
                Render();
                return;
            }

            _reading = true;
            Render();
            try
            {
                var (mods, issues) = await Task.Run(() =>
                {
                    var list = ListJars(version.ModsDirectory).Select(ModManifest.Read).Where(m => m != null).ToList();
                    return ((IReadOnlyList<ModManifest>)list, ModAudit.Check(version, list));
                });
                if (serial != _serial)
                    return;
                _mods = mods;
                _issues = issues;
                LinkLog.Info($"Checked {mods.Count} mod file(s) of {version.Name}: {issues.Count} finding(s).");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (serial == _serial)
                    LabStatus.Text = Text.Format("ModCheck.ReadFailed", ex.Message);
            }
            finally
            {
                if (serial == _serial)
                    _reading = false;
            }
            Render();
        }

        /// <summary>The jars directly inside <paramref name="folder"/>; files turned off with another extension are left out.</summary>
        private static IEnumerable<string> ListJars(string folder)
        {
            if (!Directory.Exists(folder))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(folder, "*.jar", SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(Path.GetExtension(f), ".jar", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ======================== Rendering ========================

        private void Render()
        {
            var version = Selected;
            bool busy = _scanning || _reading || _checking || _updating;
            CmbVersion.IsEnabled = !busy && _versions.Count > 0;
            BtnRescan.IsEnabled = !busy;
            BtnAddFolder.IsEnabled = !busy;
            bool modded = version != null && version.Loader != VersionLoader.Vanilla;
            BtnOpenFolder.IsEnabled = version != null && !_scanning;
            BtnCheckUpdates.IsEnabled = !busy && modded && _mods.Any(m => m.IsReadable);

            LabFolder.Text = version == null ? "" : version.ModsDirectory;
            LabFolder.ToolTip = version?.ModsDirectory;

            if (_scanning)
                LabStatus.Text = Text.Get("ModCheck.Scanning");
            else if (_reading)
                LabStatus.Text = Text.Get("ModCheck.Reading");
            else if (_checking)
                LabStatus.Text = Text.Get("ModCheck.Checking");
            else if (_updating)
                LabStatus.Text = Text.Get("ModCheck.Updating");
            else if (version != null && modded)
                LabStatus.Text = Text.Format("ModCheck.Count", _mods.Count);
            else
                LabStatus.Text = "";

            RenderIssues(version);
            RenderUpdates();
        }

        private void RenderIssues(InstalledVersion version)
        {
            CardIssues.Header = _issues.Count == 0 ? Text.Get("ModCheck.Issues") : Text.Format("ModCheck.IssuesCount", _issues.Count);
            string hint;
            string style = "PclHint";
            if (_scanning || _reading)
                hint = Text.Get("ModCheck.Reading");
            else if (_versions.Count == 0)
                hint = Text.Get("ModCheck.NoVersions");
            else if (version == null)
                hint = Text.Get("ModCheck.PickVersion");
            else if (version.Loader == VersionLoader.Vanilla)
                hint = Text.Get("ModCheck.Vanilla");
            else if (_mods.Count == 0)
                hint = Text.Get("ModCheck.NoMods");
            else if (_issues.Count == 0)
            {
                hint = Text.Format("ModCheck.AllGood", _mods.Count);
                style = "PclHintGreen";
            }
            else
            {
                int errors = _issues.Count(i => i.IsError);
                hint = errors > 0 ? Text.Format("ModCheck.HasErrors", errors) : Text.Get("ModCheck.HasWarnings");
                style = errors > 0 ? "PclHintRed" : "PclHintOrange";
            }
            LabIssues.Text = hint;
            HintIssues.Style = (Style)FindResource(style);

            var loaderName = version == null ? "" : version.Loader.ToString();
            ListIssues.ItemsSource = _issues
                .OrderBy(i => i.IsError ? 0 : 1)
                .ThenBy(i => i.Mod.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(i => new IssueRow(i, Describe(i, version, loaderName), Resource(i.IsError ? "BrushRed" : "BrushOrange")))
                .ToList();
        }

        /// <summary>What the finding means and what to do about it.</summary>
        internal static string Describe(ModIssue issue, InstalledVersion version, string loaderName)
        {
            switch (issue.Kind)
            {
                case ModIssueKind.Corrupt:
                    return Text.Get("ModCheck.Issue.Corrupt");
                case ModIssueKind.WrongLoader:
                    return Text.Format("ModCheck.Issue.WrongLoader", issue.Detail, loaderName);
                case ModIssueKind.WrongGameVersion:
                    return Text.Format("ModCheck.Issue.WrongGame", issue.Detail, version?.GameVersion ?? "?");
                case ModIssueKind.WrongLoaderVersion:
                    return Text.Format("ModCheck.Issue.WrongLoaderVersion", loaderName, issue.Detail, version?.LoaderVersion ?? "?");
                case ModIssueKind.MissingDependency:
                    return issue.Detail == "*"
                        ? Text.Format("ModCheck.Issue.Missing", issue.Other)
                        : Text.Format("ModCheck.Issue.MissingVersion", issue.Other, issue.Detail);
                case ModIssueKind.DependencyVersion:
                    return Text.Format("ModCheck.Issue.DependencyVersion", issue.Other, issue.Detail, issue.Installed ?? "?");
                case ModIssueKind.Incompatible:
                    return Text.Format("ModCheck.Issue.Incompatible", issue.Other, issue.Detail ?? "");
                default:
                    return Text.Format("ModCheck.Issue.Duplicate", issue.Other);
            }
        }

        private void RenderUpdates()
        {
            CardUpdates.Visibility = _updates == null && _updateError == null ? Visibility.Collapsed : Visibility.Visible;
            if (_updateError != null)
            {
                LabUpdates.Text = _updateError;
                ListUpdates.ItemsSource = null;
                BtnUpdateAll.Visibility = Visibility.Collapsed;
                return;
            }
            if (_updates == null)
                return;
            int pending = _updates.Count(u => u.CanApply);
            LabUpdates.Text = _updates.Count == 0 ? Text.Get("ModCheck.NoUpdates") : Text.Format("ModCheck.UpdatesFound", _updates.Count);
            ListUpdates.ItemsSource = _updates;
            BtnUpdateAll.Visibility = pending > 1 ? Visibility.Visible : Visibility.Collapsed;
            BtnUpdateAll.IsEnabled = !_updating;
        }

        // ======================== Updates ========================

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var version = Selected;
            if (version == null || _checking)
                return;
            int serial = _serial;
            _checking = true;
            _updateError = null;
            Render();
            try
            {
                var readable = _mods.Where(m => m.IsReadable && m.Path != null).ToList();
                var titles = readable.ToDictionary(m => m.Path, m => m.Title, StringComparer.OrdinalIgnoreCase);
                var found = await ModUpdates.FindAsync(version, readable.Select(m => m.Path).ToList(), titles, CancellationToken.None);
                if (serial != _serial)
                    return;
                _updates = found.OrderBy(u => u.Title, StringComparer.OrdinalIgnoreCase).Select(u => new UpdateRow(u)).ToList();
            }
            catch (ModSyncException)
            {
                if (serial == _serial)
                    _updateError = Text.Get("ModCheck.LookupFailed");
            }
            finally
            {
                _checking = false;
            }
            Render();
        }

        private async void BtnUpdateOne_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is UpdateRow row && row.CanApply)
                await ApplyAsync(new[] { row });
        }

        private async void BtnUpdateAll_Click(object sender, RoutedEventArgs e)
        {
            if (_updates != null)
                await ApplyAsync(_updates.Where(u => u.CanApply).ToList());
        }

        private async Task ApplyAsync(IReadOnlyList<UpdateRow> rows)
        {
            var version = Selected;
            var dialogs = Dialogs.Of(this);
            if (version == null || dialogs == null || rows.Count == 0 || _updating)
                return;

            string gameDirectory = VersionChoices.GameDirectory(version);
            bool running = await Task.Run(() => RunningGame.Find().Any(g => !string.IsNullOrEmpty(g.GameDirectory)
                && string.Equals(Path.GetFullPath(g.GameDirectory).TrimEnd('\\'), Path.GetFullPath(gameDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));
            if (running)
            {
                await dialogs.ErrorAsync(Text.Get("ModCheck.UpdateTitle"), Text.Get("ModCheck.GameRunning"));
                return;
            }
            if (!await dialogs.ConfirmAsync(Text.Get("ModCheck.UpdateTitle"), Text.Format("ModCheck.UpdateConfirm", rows.Count), Text.Get("ModCheck.UpdateConfirmButton")))
                return;

            _updating = true;
            Render();
            string backup = ModUpdates.NewBackupDirectory(version);
            int done = 0;
            var failed = new List<string>();
            foreach (var row in rows)
            {
                row.SetState(Text.Get("ModCheck.UpdatingOne"), false);
                try
                {
                    await ModUpdates.ApplyAsync(row.Update, backup, CancellationToken.None);
                    row.SetState(Text.Get("ModCheck.Updated"), false);
                    done++;
                }
                catch (Exception ex) when (ex is ModSyncException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    LinkLog.Warn("Updating " + row.Update.FileName + " failed: " + ex.Message);
                    row.SetState(Text.Get("ModCheck.UpdateOne"), true);
                    failed.Add(row.Title);
                }
            }
            _updating = false;
            Render();

            if (failed.Count > 0)
                await dialogs.ErrorAsync(Text.Get("ModCheck.UpdateTitle"), Text.Format("ModCheck.UpdateSomeFailed", done, string.Join(Text.Get("ModCheck.Separator"), failed)));
            else
                await dialogs.AlertAsync(Text.Get("ModCheck.UpdateTitle"), Text.Format("ModCheck.UpdateDone", done, backup));

            // The folder changed: check it again, keeping the update list.
            var kept = _updates;
            await ReadModsAsync();
            _updates = kept;
            Render();
        }

        private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var version = Selected;
            if (version == null)
                return;
            try
            {
                Directory.CreateDirectory(version.ModsDirectory);
                Process.Start("explorer.exe", "\"" + version.ModsDirectory + "\"");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("ModCheck.OpenFolder"), Text.Format("Settings.ActionFailed", ex.Message));
            }
        }

        private static Brush Resource(string key)
        {
            return (Brush)(Application.Current.MainWindow?.TryFindResource(key) ?? Application.Current.FindResource(key));
        }

        // ======================== Rows ========================

        private sealed class IssueRow
        {
            public IssueRow(ModIssue issue, string detail, Brush accent)
            {
                Title = issue.Mod.Title;
                FileName = issue.Mod.FileName;
                Detail = detail;
                Accent = accent;
            }

            public string Title { get; }

            public string FileName { get; }

            public string Detail { get; }

            public Brush Accent { get; }
        }

        private sealed class UpdateRow : INotifyPropertyChanged
        {
            private string _action;
            private bool _canApply = true;

            public UpdateRow(ModUpdate update)
            {
                Update = update;
                _action = Text.Get("ModCheck.UpdateOne");
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public ModUpdate Update { get; }

            public string Title => Update.Title;

            public string Detail => Text.Format("ModCheck.UpdateDetail", Update.CurrentVersion ?? "?", Update.NewVersion ?? "?");

            public string Action
            {
                get => _action;
                private set => Set(ref _action, value);
            }

            public bool CanApply
            {
                get => _canApply;
                private set => Set(ref _canApply, value);
            }

            public void SetState(string action, bool canApply)
            {
                Action = action;
                CanApply = canApply;
            }

            private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
            {
                if (Equals(field, value))
                    return;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
