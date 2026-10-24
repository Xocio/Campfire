using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Campfire.Launcher;
using Campfire.Localization;
using Campfire.Patching;

namespace Campfire.Pages
{
    /// <summary>
    /// "Tools" tab: repairs the downloads of PCL 2.99 and restores the backup.
    /// </summary>
    public partial class RepairPage : UserControl
    {
        private enum HintKind
        {
            Info,
            Success,
            Error,
        }

        private LauncherReport _report;
        private bool _busy;
        private bool _fixing;

        // The hint text is produced on demand so it follows language changes.
        private HintKind _hintKind;
        private Func<string> _hintText;

        public RepairPage()
        {
            InitializeComponent();

            RenderPatches();
            Themes.ThemeManager.Changed += (sender, e) => RenderPatches();
            ShowHint(HintKind.Info, () => Text.Get("Status.Searching"));
            UpdateButtons();

            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Raised when the inspected launcher changes.</summary>
        public event EventHandler StatusChanged;

        /// <summary>Whether the selected launcher has its downloads switched off and can be repaired.</summary>
        public bool NeedsRepair => _report?.CanFix == true && _report.Status != LauncherStatus.Patched;

        /// <summary>Whether the selected launcher has been repaired by Campfire.</summary>
        public bool IsRepaired => _report?.Status == LauncherStatus.Patched;

        public bool IsBusy => _busy;

        /// <summary>Path of the selected launcher, or <c>null</c> when none is selected.</summary>
        public string LauncherPath => _report?.Path;

        // ======================== Launcher selection ========================

        /// <summary>Selects <paramref name="path"/>, or looks for a launcher next to Campfire when it is <c>null</c>.</summary>
        public async Task LoadInitialLauncherAsync(string path)
        {
            path = path ?? LauncherLocator.FindIn(AppDomain.CurrentDomain.BaseDirectory);
            if (path != null)
                await SelectLauncherAsync(path);
            else
                ShowHint(HintKind.Info, () => Text.Get("Status.NotFoundNearby"));
        }

        public async Task SelectLauncherAsync(string path)
        {
            SetBusy(true);
            ShowHint(HintKind.Info, () => Text.Get("Status.Inspecting"));
            try
            {
                Render(await Task.Run(() => LauncherInspector.Inspect(path)));
            }
            catch (Exception ex)
            {
                ReportError("Repair.InspectFailedTitle", () => Text.Format("Status.InspectFailed", ex.Message));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void Render(LauncherReport report)
        {
            _report = report;

            LabFileName.Text = Path.GetFileName(report.Path);
            LabFilePath.Text = report.Path;
            LabFilePath.ToolTip = report.Path;
            PanVersion.Visibility = report.Version == null ? Visibility.Collapsed : Visibility.Visible;
            LabVersion.Text = report.Version == null ? null : "v" + report.Version;

            switch (report.Status)
            {
                case LauncherStatus.Patched:
                case LauncherStatus.NotBlocked:
                    ShowHint(HintKind.Success, () => report.Message);
                    break;
                case LauncherStatus.Blocked:
                    ShowHint(HintKind.Info, () => report.Message);
                    break;
                default:
                    ShowHint(HintKind.Error, () => report.Message);
                    break;
            }

            RenderPatches();
            UpdateButtons();
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RenderPatches()
        {
            ListPatches.ItemsSource = _report != null && _report.Patches.Count > 0
                ? _report.Patches.Select(p => PatchRow.From(p, _report.Status)).ToList()
                : PatchCatalog.All.Select(PatchRow.Placeholder).ToList();
        }

        // ======================== Fix / restore ========================

        private async void BtnFix_Click(object sender, RoutedEventArgs e)
        {
            if (_report?.CanFix != true)
                return;

            string path = _report.Path;
            SetBusy(true, fixing: true);
            ShowHint(HintKind.Info, () => Text.Get("Status.Fixing"));
            try
            {
                Render(await Task.Run(() => LauncherFixer.Fix(path)));
                string backupName = Path.GetFileName(LauncherFixer.GetBackupPath(path));
                ShowHint(HintKind.Success, () => Text.Format("Status.FixDone", backupName));
                Online.Telemetry.Count("repair");
            }
            catch (LauncherException ex)
            {
                ReportError("Repair.FixFailedTitle", () => ex.Message);
            }
            catch (Exception ex)
            {
                ReportError("Repair.FixFailedTitle", () => Text.Format("Status.FixUnexpected", ex.Message));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null)
                return;

            string path = _report.Path;
            SetBusy(true);
            ShowHint(HintKind.Info, () => Text.Get("Status.Restoring"));
            try
            {
                Render(await Task.Run(() => LauncherFixer.Restore(path)));
                ShowHint(HintKind.Info, () => Text.Get("Status.Restored"));
            }
            catch (LauncherException ex)
            {
                ReportError("Repair.RestoreFailedTitle", () => ex.Message);
            }
            catch (Exception ex)
            {
                ReportError("Repair.RestoreFailedTitle", () => Text.Format("Status.Unexpected", ex.Message));
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ======================== Other actions ========================

        private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Text.Get("Dialog.SelectTitle"),
                Filter = Text.Get("Dialog.SelectFilter"),
                CheckFileExists = true,
            };
            if (_report != null && File.Exists(_report.Path))
                dialog.InitialDirectory = Path.GetDirectoryName(_report.Path);

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                await SelectLauncherAsync(dialog.FileName);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_report != null && File.Exists(_report.Path))
                Process.Start("explorer.exe", $"/select,\"{_report.Path}\"");
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null || !File.Exists(_report.Path))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(_report.Path)
                {
                    WorkingDirectory = Path.GetDirectoryName(_report.Path),
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                ReportError("Repair.LaunchFailedTitle", () => Text.Format("Status.LaunchFailed", ex.Message));
            }
        }

        // ======================== Helpers ========================

        /// <summary>XAML texts update through bindings; texts set from code are refreshed here.</summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            RenderPatches();
            UpdateButtons();
            ShowHint(_hintKind, _hintText);
        }

        private void SetBusy(bool busy, bool fixing = false)
        {
            _busy = busy;
            _fixing = busy && fixing;
            Cursor = busy ? Cursors.AppStarting : null;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasFile = _report != null && File.Exists(_report.Path);
            bool works = _report?.Status == LauncherStatus.Patched || _report?.Status == LauncherStatus.NotBlocked;

            BtnBrowse.IsEnabled = !_busy;
            BtnFix.IsEnabled = !_busy && _report?.CanFix == true;
            BtnRestore.IsEnabled = !_busy && hasFile && LauncherFixer.HasBackup(_report.Path);
            BtnOpenFolder.IsEnabled = hasFile;
            BtnLaunch.Visibility = hasFile && works ? Visibility.Visible : Visibility.Collapsed;
            BtnLaunch.IsEnabled = !_busy;

            if (_fixing)
                BtnFix.Content = Text.Get("Main.Fixing");
            else if (_report?.Status == LauncherStatus.Patched)
                BtnFix.Content = Text.Get("Main.Fixed");
            else
                BtnFix.Content = Text.Get("Main.Fix");
        }

        /// <summary>Shows a failure in the status bar and as an alert, so it cannot be missed.</summary>
        private void ReportError(string titleKey, Func<string> text)
        {
            ShowHint(HintKind.Error, text);
            _ = Dialogs.Of(this)?.ErrorAsync(Text.Get(titleKey), text());
        }

        private void ShowHint(HintKind kind, Func<string> text)
        {
            _hintKind = kind;
            _hintText = text;

            string styleKey;
            switch (kind)
            {
                case HintKind.Success:
                    styleKey = "PclHintGreen";
                    break;
                case HintKind.Error:
                    styleKey = "PclHintRed";
                    break;
                default:
                    styleKey = "PclHint";
                    break;
            }
            HintStatus.Style = (Style)FindResource(styleKey);
            LabStatus.Text = text();
        }
    }
}
