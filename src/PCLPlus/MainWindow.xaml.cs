using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using PCLPlus.Launcher;
using PCLPlus.Localization;
using PCLPlus.Patching;

namespace PCLPlus
{
    public partial class MainWindow : Window
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

        public MainWindow()
        {
            InitializeComponent();

            LabAboutVersion.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            RenderPatches();
            ShowHint(HintKind.Info, () => Text.Get("Status.Searching"));
            UpdateButtons();

            Text.LanguageChanged += OnLanguageChanged;
            Closed += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
            Loaded += async (sender, e) => await LoadInitialLauncherAsync();
        }

        // ======================== Launcher selection ========================

        private async Task LoadInitialLauncherAsync()
        {
            string fromArguments = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
            string path = fromArguments ?? LauncherLocator.FindIn(AppDomain.CurrentDomain.BaseDirectory);
            if (path != null)
                await SelectLauncherAsync(path);
            else
                ShowHint(HintKind.Info, () => Text.Get("Status.NotFoundNearby"));
        }

        private async Task SelectLauncherAsync(string path)
        {
            SetBusy(true);
            ShowHint(HintKind.Info, () => Text.Get("Status.Inspecting"));
            try
            {
                Render(await Task.Run(() => LauncherInspector.Inspect(path)));
            }
            catch (Exception ex)
            {
                ShowHint(HintKind.Error, () => Text.Format("Status.InspectFailed", ex.Message));
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
            }
            catch (LauncherException ex)
            {
                ShowHint(HintKind.Error, () => ex.Message);
            }
            catch (Exception ex)
            {
                ShowHint(HintKind.Error, () => Text.Format("Status.FixUnexpected", ex.Message));
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
                ShowHint(HintKind.Error, () => ex.Message);
            }
            catch (Exception ex)
            {
                ShowHint(HintKind.Error, () => Text.Format("Status.Unexpected", ex.Message));
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

            if (dialog.ShowDialog(this) == true)
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
                ShowHint(HintKind.Error, () => Text.Format("Status.LaunchFailed", ex.Message));
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (_busy || !(e.Data.GetData(DataFormats.FileDrop) is string[] files))
                return;

            string path = files.FirstOrDefault(File.Exists);
            if (path != null)
                await SelectLauncherAsync(path);
        }

        // ======================== About page ========================

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            SwitchPage(PanMain, PanAbout);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            SwitchPage(PanAbout, PanMain);
        }

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.Tag is string url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Text.Format("Dialog.BrowserFailed", url, ex.Message), "PCL++");
            }
        }

        private static void SwitchPage(UIElement from, UIElement to)
        {
            from.Visibility = Visibility.Hidden;
            to.Opacity = 0;
            to.Visibility = Visibility.Visible;
            to.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        }

        // ======================== Title bar ========================

        private void PanTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnLanguage_Click(object sender, RoutedEventArgs e)
        {
            Text.Language = Text.Language == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese;
            AppSettings.SaveLanguage(Text.Language);
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
            BtnLanguage.IsEnabled = !_busy;

            if (_fixing)
                BtnFix.Content = Text.Get("Main.Fixing");
            else if (_report?.Status == LauncherStatus.Patched)
                BtnFix.Content = Text.Get("Main.Fixed");
            else
                BtnFix.Content = Text.Get("Main.Fix");
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
