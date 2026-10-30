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
using Campfire.Tools;

namespace Campfire.Pages
{
    /// <summary>
    /// "Tools" tab: lets PCL Community Edition start the game with a JRE.
    /// </summary>
    public partial class JrePage : UserControl
    {
        private enum HintKind
        {
            Info,
            Success,
            Error,
        }

        private JreReport _report;
        private bool _busy;
        private bool _working;

        // The hint text is produced on demand.
        private HintKind _hintKind;
        private Func<string> _hintText;

        public JrePage()
        {
            InitializeComponent();

            RenderRows();
            Themes.ThemeManager.Changed += (sender, e) => RenderRows();
            ShowHint(HintKind.Info, () => Text.Get("Status.Searching"));
            UpdateButtons();

            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        public bool IsBusy => _busy;

        // ======================== Launcher selection ========================

        /// <summary>
        /// Selects <paramref name="preferredPath"/>, or looks for a community launcher next to Campfire.
        /// Does nothing once a launcher has been chosen.
        /// </summary>
        public async Task LoadInitialLauncherAsync(string preferredPath)
        {
            if (_report != null || _busy)
                return;

            string path;
            SetBusy(true);
            try
            {
                path = await Task.Run(() => preferredPath != null && CommunityLauncher.IsCommunityLauncher(preferredPath)
                    ? preferredPath
                    : JreUnlocker.FindIn(AppDomain.CurrentDomain.BaseDirectory));
            }
            finally
            {
                SetBusy(false);
            }

            if (path != null)
                await SelectLauncherAsync(path);
            else
                ShowHint(HintKind.Info, () => Text.Get("Jre.NotFoundNearby"));
        }

        public async Task SelectLauncherAsync(string path)
        {
            SetBusy(true);
            ShowHint(HintKind.Info, () => Text.Get("Status.Inspecting"));
            try
            {
                Render(await Task.Run(() => JreUnlocker.Inspect(path)));
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

        private void Render(JreReport report)
        {
            _report = report;

            LabFileName.Text = Path.GetFileName(report.Path);
            LabFilePath.Text = report.Path;
            LabFilePath.ToolTip = report.Path;
            PanVersion.Visibility = report.Version == null ? Visibility.Collapsed : Visibility.Visible;
            LabVersion.Text = report.Version == null ? null : "v" + report.Version;

            switch (report.State)
            {
                case JreState.Unlocked:
                    ShowHint(HintKind.Success, () => report.Message);
                    break;
                case JreState.Locked:
                    ShowHint(HintKind.Info, () => report.Message);
                    break;
                default:
                    ShowHint(HintKind.Error, () => report.Message);
                    break;
            }

            RenderRows();
            UpdateButtons();
        }

        private void RenderRows()
        {
            PatchState state;
            string stateText;
            switch (_report?.State)
            {
                case JreState.Locked:
                    state = PatchState.Pending;
                    stateText = Text.Get("Jre.State.Locked");
                    break;
                case JreState.Unlocked:
                    state = PatchState.Applied;
                    stateText = Text.Get("Jre.State.Unlocked");
                    break;
                case JreState.NotCommunity:
                case JreState.Unsupported:
                    state = PatchState.NotFound;
                    stateText = Text.Get("PatchState.NotFound");
                    break;
                default:
                    state = PatchState.NotFound;
                    stateText = "—";
                    break;
            }

            ListRows.ItemsSource = new[]
            {
                PatchRow.Create(Text.Get("Jre.Item"), Text.Get("Jre.ItemDescription"), stateText, state),
            };
        }

        // ======================== Unlock / restore ========================

        private async void BtnUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (_report?.CanUnlock != true)
                return;

            string path = _report.Path;
            SetBusy(true, working: true);
            ShowHint(HintKind.Info, () => Text.Get("Jre.Status.Working"));
            try
            {
                Render(await Task.Run(() => JreUnlocker.Unlock(path)));
                string backupName = Path.GetFileName(JreUnlocker.GetBackupPath(path));
                ShowHint(HintKind.Success, () => Text.Format("Jre.Status.Done", backupName));
            }
            catch (LauncherException ex)
            {
                ReportError("Jre.FailedTitle", () => ex.Message);
            }
            catch (Exception ex)
            {
                ReportError("Jre.FailedTitle", () => Text.Format("Jre.Status.Unexpected", ex.Message));
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
                Render(await Task.Run(() => JreUnlocker.Restore(path)));
                ShowHint(HintKind.Info, () => Text.Get("Jre.Status.Restored"));
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
                Title = Text.Get("Jre.SelectTitle"),
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

        // ======================== Helpers ========================

        /// <summary>XAML texts update through bindings; texts set from code are refreshed here.</summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            RenderRows();
            UpdateButtons();
            ShowHint(_hintKind, _hintText);
        }

        private void SetBusy(bool busy, bool working = false)
        {
            _busy = busy;
            _working = busy && working;
            Cursor = busy ? Cursors.AppStarting : null;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasFile = _report != null && File.Exists(_report.Path);

            BtnBrowse.IsEnabled = !_busy;
            BtnUnlock.IsEnabled = !_busy && _report?.CanUnlock == true;
            BtnRestore.IsEnabled = !_busy && hasFile && JreUnlocker.HasBackup(_report.Path);
            BtnOpenFolder.IsEnabled = hasFile;

            if (_working)
                BtnUnlock.Content = Text.Get("Jre.Working");
            else if (_report?.IsDone == true)
                BtnUnlock.Content = Text.Get("Jre.Done");
            else
                BtnUnlock.Content = Text.Get("Jre.Unlock");
        }

        /// <summary>Shows a failure in the status bar and as an alert.</summary>
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
