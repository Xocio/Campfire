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

        public MainWindow()
        {
            InitializeComponent();

            LabAboutVersion.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            ListPatches.ItemsSource = PatchCatalog.All.Select(PatchRow.Placeholder).ToList();

            Loaded += async (sender, e) => await LoadInitialLauncherAsync();
        }

        // ======================== Launcher selection ========================

        private async Task LoadInitialLauncherAsync()
        {
            string fromArguments = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
            string path = fromArguments ?? LauncherLocator.FindIn(AppDomain.CurrentDomain.BaseDirectory);
            if (path != null)
            {
                await SelectLauncherAsync(path);
            }
            else
            {
                ShowHint(HintKind.Info, "没有在 PCL++ 所在的文件夹中找到启动器，请点击“选择文件”手动选择。");
                UpdateButtons();
            }
        }

        private async Task SelectLauncherAsync(string path)
        {
            SetBusy(true);
            ShowHint(HintKind.Info, "正在检测启动器……");
            try
            {
                var report = await Task.Run(() => LauncherInspector.Inspect(path));
                Render(report);
            }
            catch (Exception ex)
            {
                ShowHint(HintKind.Error, "检测失败：" + ex.Message);
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
                    ShowHint(HintKind.Success, report.Message);
                    break;
                case LauncherStatus.Blocked:
                    ShowHint(HintKind.Info, report.Message);
                    break;
                default:
                    ShowHint(HintKind.Error, report.Message);
                    break;
            }

            ListPatches.ItemsSource = report.Patches.Count > 0
                ? report.Patches.Select(p => PatchRow.From(p, report.Status)).ToList()
                : PatchCatalog.All.Select(PatchRow.Placeholder).ToList();

            UpdateButtons();
        }

        // ======================== Fix / restore ========================

        private async void BtnFix_Click(object sender, RoutedEventArgs e)
        {
            if (_report?.CanFix != true)
                return;

            string path = _report.Path;
            SetBusy(true, fixing: true);
            ShowHint(HintKind.Info, "正在修复，请稍候……");
            try
            {
                var report = await Task.Run(() => LauncherFixer.Fix(path));
                Render(report);
                ShowHint(HintKind.Success,
                    $"修复完成！现在可以启动 PCL 下载游戏了。原文件已备份为 {Path.GetFileName(LauncherFixer.GetBackupPath(path))}。");
            }
            catch (LauncherException ex)
            {
                ShowHint(HintKind.Error, ex.Message);
            }
            catch (Exception ex)
            {
                ShowHint(HintKind.Error, "出现意外错误，启动器未被修改：" + ex.Message);
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
            ShowHint(HintKind.Info, "正在还原……");
            try
            {
                var report = await Task.Run(() => LauncherFixer.Restore(path));
                Render(report);
                ShowHint(HintKind.Info, "已还原为修复前的启动器。");
            }
            catch (LauncherException ex)
            {
                ShowHint(HintKind.Error, ex.Message);
            }
            catch (Exception ex)
            {
                ShowHint(HintKind.Error, "出现意外错误：" + ex.Message);
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
                Title = "选择 PCL 启动器",
                Filter = "PCL 启动器 (*.exe)|*.exe",
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
                ShowHint(HintKind.Error, "启动失败：" + ex.Message);
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
                MessageBox.Show(this, $"无法打开浏览器，请手动访问：\n{url}\n\n{ex.Message}", "PCL++");
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

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ======================== Helpers ========================

        private void SetBusy(bool busy, bool fixing = false)
        {
            _busy = busy;
            Cursor = busy ? Cursors.AppStarting : null;
            BtnFix.Content = fixing ? "正在修复……" : "一键修复";
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

            if (!_busy && _report?.Status == LauncherStatus.Patched)
                BtnFix.Content = "已修复";
        }

        private void ShowHint(HintKind kind, string text)
        {
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
            LabStatus.Text = text;
        }
    }
}
