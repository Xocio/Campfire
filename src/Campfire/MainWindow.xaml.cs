using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Campfire.Controls;
using Campfire.Link;
using Campfire.Localization;
using Campfire.Online;
using Campfire.Pages;

namespace Campfire
{
    public partial class MainWindow : Window, IDialogHost
    {
        /// <summary>Codes this Campfire created or already offered; they are never offered for quick join.</summary>
        private readonly HashSet<string> _knownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private FrameworkElement _shownPage;
        private FrameworkElement _tabPage;
        private readonly Stack<(FrameworkElement Page, string TitleKey)> _subPages = new Stack<(FrameworkElement Page, string TitleKey)>();
        private TaskCompletionSource<DialogChoice> _dialog;
        private Action _dialogLinkAction;
        private bool _dialogLinkCloses;
        private TrayIcon _tray;
        private TrayMenu _trayMenu;
        private bool _closeConfirmed;
        private bool _closeAnimated;
        private bool _exitRequested;
        private bool _trayNoticeShown;
        private string _restartPath;
        private bool _alreadyRunningShown;

        public MainWindow()
        {
            // Window resources, unlike application resources, stay changeable, so the theme can recolor them.
            Themes.ThemeManager.Initialize(Resources);
            InitializeComponent();
            _shownPage = PageHome;
            _tabPage = PageHome;
            PageHome.QuickJoinRequested += PageHome_QuickJoinRequested;
            PageSettings.IsRoomActive = () => PageLink.IsRoomActive;

            Text.LanguageChanged += OnLanguageChanged;
            Closed += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
            SingleInstance.Listen(() => Dispatcher.BeginInvoke(new Action(ShowAlreadyRunning)));
            Loaded += async (sender, e) =>
            {
                PlayWindowEntrance();
                UpdateTray();
                WarmUpPages();
                StartOnline();
                OfferTour();
                await LoadInitialLauncherAsync();
            };
        }

        // ======================== Tour ========================

        private const string TourPendingKey = "tour.pending";

        /// <summary>
        /// The first time Campfire runs, walks the user through it once the window has settled. The tour stays pending
        /// until it is finished or skipped, so closing Campfire halfway offers it again next time.
        /// </summary>
        private async void OfferTour()
        {
            if (AppSettings.IsFirstRun && AppSettings.Get(TourPendingKey) == null)
                AppSettings.Set(TourPendingKey, "1");
            if (AppSettings.Get(TourPendingKey) != "1" || Environment.GetCommandLineArgs().Skip(1).Any(File.Exists))
                return;

            await Task.Delay(1500);
            // An update or notice dialog comes first.
            while (_dialog != null)
                await _dialog.Task;
            if (IsVisible && WindowState != WindowState.Minimized)
                StartTour();
        }

        /// <summary>Starts the guided tour from the Home tab.</summary>
        public void StartTour()
        {
            if (Tour.IsActive)
                return;
            while (_subPages.Count > 0)
                CloseSubPage();

            void ShowTab(RadioButton tab)
            {
                if (tab.IsChecked != true)
                    tab.IsChecked = true;
            }

            var steps = new List<TourStep>
            {
                new TourStep { TitleKey = "Tour.1.Title", BodyKey = "Tour.1.Body", Target = () => LogoTitle, Padding = 7, Round = true, Enter = () => ShowTab(TabHome) },
                new TourStep { TitleKey = "Tour.2.Title", BodyKey = "Tour.2.Body", Target = () => PageHome.PanStart, Padding = 8, Enter = () => ShowTab(TabHome) },
                new TourStep { TitleKey = "Tour.3.Title", BodyKey = "Tour.3.Body", Target = () => PageHome.CardGuide, Padding = 6, Enter = () => ShowTab(TabHome) },
                new TourStep { TitleKey = "Tour.4.Title", BodyKey = "Tour.4.Body", Target = () => TabTools, Padding = 5, Round = true, Enter = () => ShowTab(TabTools) },
                new TourStep { TitleKey = "Tour.5.Title", BodyKey = "Tour.5.Body", Target = () => TabSettings, Padding = 5, Round = true, Enter = () => ShowTab(TabSettings) },
                new TourStep { TitleKey = "Tour.6.Title", BodyKey = "Tour.6.Body", Target = () => PageHome.TileCreate, Padding = 6, Enter = () => ShowTab(TabHome) },
            };
            ShowTab(TabHome);
            Tour.Start(steps, () => AppSettings.Set(TourPendingKey, null));
        }

        // ======================== Navigation ========================

        private async Task LoadInitialLauncherAsync()
        {
            // A launcher passed on the command line (or dropped onto Campfire.exe) opens the repair page.
            string fromArguments = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
            if (fromArguments != null)
            {
                TabTools.IsChecked = true;
                OpenSubPage(PageRepair, "Page.Repair");
            }
            await PageRepair.LoadInitialLauncherAsync(fromArguments);
        }

        private int TabOrder(FrameworkElement page)
        {
            return page == PageTools ? 1 : page == PagePreferences ? 2 : 0;
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PageHome == null)
                return;
            FrameworkElement target = sender == TabTools ? PageTools : sender == TabSettings ? PagePreferences : (FrameworkElement)PageHome;
            int direction = Math.Sign(TabOrder(target) - TabOrder(_tabPage));
            _tabPage = target;
            ShowPage(_tabPage, direction == 0 ? 1 : direction);
        }

        /// <summary>Opens a feature page; the title bar shows a back button and the page name instead of the tabs.</summary>
        private void OpenSubPage(FrameworkElement page, string titleKey)
        {
            // Opening a page that is already in the trail goes back to it instead of adding it again.
            while (_subPages.Count > 0 && _subPages.Any(p => p.Page == page))
                _subPages.Pop();
            _subPages.Push((page, titleKey));

            LabTitleSub.Text = Text.Get(titleKey);
            if (PanTitleSub.Visibility != Visibility.Visible)
            {
                Motion.LeavePage(PanTitleMain, 1);
                Motion.LeavePage(PanTabs, 1);
                Motion.EnterPage(PanTitleSub, 1);
            }
            else
            {
                Motion.EnterPage(LabTitleSub, 1);
            }
            ShowPage(page, 1);
        }

        /// <summary>Goes back one page: to the previous feature page, or to the tab.</summary>
        private void CloseSubPage()
        {
            if (_subPages.Count == 0)
                return;
            _subPages.Pop();
            if (_subPages.Count > 0)
            {
                var previous = _subPages.Peek();
                LabTitleSub.Text = Text.Get(previous.TitleKey);
                Motion.EnterPage(LabTitleSub, -1);
                ShowPage(previous.Page, -1);
                return;
            }

            Motion.LeavePage(PanTitleSub, -1);
            Motion.EnterPage(PanTitleMain, -1);
            Motion.EnterPage(PanTabs, -1);
            ShowPage(_tabPage, -1);
        }

        private void ShowPage(FrameworkElement page, int direction)
        {
            if (page == _shownPage)
                return;

            Motion.LeavePage(_shownPage, direction);
            _shownPage = page;
            Motion.EnterPage(page, direction);
            CheckClipboardForCode();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            CloseSubPage();
        }

        private void PageHome_LinkRequested(object sender, EventArgs e)
        {
            OpenSubPage(PageLink, "Page.Link");
        }

        private void PageHome_CreateRequested(object sender, EventArgs e)
        {
            OpenSubPage(PageLink, "Page.Link");
            PageLink.StartCreate();
        }

        private void PageHome_JoinRequested(object sender, EventArgs e)
        {
            OpenSubPage(PageLink, "Page.Link");
            PageLink.StartJoin();
        }

        private void PageHome_QuickJoinRequested(object sender, RoomCode code)
        {
            OpenSubPage(PageLink, "Page.Link");
            PageLink.JoinWithCode(code);
        }

        private void Page_SettingsRequested(object sender, EventArgs e)
        {
            OpenSubPage(PageSettings, "Page.Settings");
        }

        private void PageSettings_SettingsChanged(object sender, EventArgs e)
        {
            PageLink.ReloadSettings();
            if (!LinkSettings.DetectClipboard)
                PageHome.SetQuickJoin(null);
        }

        private void PageTools_RepairRequested(object sender, EventArgs e)
        {
            OpenSubPage(PageRepair, "Page.Repair");
        }

        private async void PageTools_JreRequested(object sender, EventArgs e)
        {
            OpenSubPage(PageJre, "Page.Jre");
            await PageJre.LoadInitialLauncherAsync(PageRepair.LauncherPath);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = !PageRepair.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (PageRepair.IsBusy || !(e.Data.GetData(DataFormats.FileDrop) is string[] files))
                return;

            string path = files.FirstOrDefault(File.Exists);
            if (path == null)
                return;

            if (_subPages.Count == 0)
                TabTools.IsChecked = true;
            OpenSubPage(PageRepair, "Page.Repair");
            await PageRepair.SelectLauncherAsync(path);
        }

        // ======================== Status ========================

        private void PageLink_StatusChanged(object sender, EventArgs e)
        {
            if (PageLink.Role == LinkRole.Host && PageLink.Code != null)
                _knownCodes.Add(PageLink.Code.Code);

            switch (PageLink.Stage)
            {
                case LinkStage.Working:
                    PageHome.SetRoom(RoomStatus(),
                        Text.Get(PageLink.Role == LinkRole.Host ? "Home.WorkingHost" : "Home.WorkingGuest"), "Brush3");
                    break;
                case LinkStage.Ready when PageLink.Role == LinkRole.Host:
                    PageHome.SetRoom(RoomStatus(), Text.Format("Home.RoomCode", PageLink.Code?.Code), "BrushGreen");
                    break;
                case LinkStage.Ready:
                    PageHome.SetRoom(RoomStatus(), Text.Get("Home.JoinedDetail"), "BrushGreen");
                    break;
                default:
                    PageHome.SetRoom(null, null, null);
                    break;
            }
            if (PageLink.IsRoomActive)
                PageHome.SetQuickJoin(null);
            UpdateTray();
        }

        /// <summary>One line about the room, or <c>null</c> when there is none.</summary>
        private string RoomStatus()
        {
            switch (PageLink.Stage)
            {
                case LinkStage.Working:
                    return Text.Get("Home.Status.Working");
                case LinkStage.Ready:
                    return PageLink.Role == LinkRole.Host
                        ? Text.Format("Home.Status.Hosting", PageLink.PlayerCount)
                        : Text.Get("Home.Status.Joined");
                default:
                    return null;
            }
        }

        private void PageRepair_StatusChanged(object sender, EventArgs e)
        {
            if (PageRepair.NeedsRepair)
                PageTools.SetRepairStatus(Text.Get("Tools.Status.NeedsRepair"), "BrushOrange");
            else if (PageRepair.IsRepaired)
                PageTools.SetRepairStatus(Text.Get("Tools.Status.Repaired"), "BrushGreen");
            else
                PageTools.SetRepairStatus(null, null);
            DotTools.Visibility = PageRepair.NeedsRepair ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_subPages.Count > 0)
                LabTitleSub.Text = Text.Get(_subPages.Peek().TitleKey);
            PageLink_StatusChanged(this, EventArgs.Empty);
            PageRepair_StatusChanged(this, EventArgs.Empty);
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            CheckClipboardForCode();
        }

        /// <summary>Offers a one-click join on the Home tab when the user has just copied an invite code.</summary>
        private void CheckClipboardForCode()
        {
            if (_shownPage != PageHome || PageLink.IsRoomActive || !LinkSettings.DetectClipboard)
                return;

            var code = RoomCode.Find(LinkPage.TryReadClipboard());
            if (code == null || !_knownCodes.Add(code.Code))
                return;
            PageHome.SetQuickJoin(code);
        }

        // ======================== Notification area ========================

        private void PagePreferences_TraySettingsChanged(object sender, EventArgs e)
        {
            UpdateTray();
        }

        private void UpdateTray()
        {
            if (_tray == null)
            {
                if (!TrayIcon.ShowIcon)
                    return;
                _tray = new TrayIcon();
                _tray.OpenRequested += (sender, e) => RestoreWindow();
                _tray.MenuRequested += (sender, e) => ShowTrayMenu();
            }

            _tray.Update(TrayIcon.ShowIcon || !IsVisible, RoomStatus(), PageLink.IsRoomActive);
        }

        private void ShowTrayMenu()
        {
            _trayMenu?.Close();
            _trayMenu = new TrayMenu(PageLink, Resources);
            _trayMenu.Closed += (sender, e) =>
            {
                if (sender == _trayMenu)
                    _trayMenu = null;
            };
            _trayMenu.OpenRequested += (sender, e) => RestoreWindow();
            _trayMenu.CreateRequested += (sender, e) =>
            {
                RestoreWindow();
                PageHome_CreateRequested(this, EventArgs.Empty);
            };
            _trayMenu.JoinRequested += (sender, e) =>
            {
                RestoreWindow();
                PageHome_JoinRequested(this, EventArgs.Empty);
            };
            _trayMenu.StopRequested += (sender, e) =>
            {
                // Closing an open room asks first, and the question is shown in the window.
                if (PageLink.Stage == LinkStage.Ready)
                    RestoreWindow();
                PageLink.RequestStop();
            };
            _trayMenu.ExitRequested += (sender, e) =>
            {
                _exitRequested = true;
                RestoreWindow();
                Close();
            };
            _trayMenu.ShowAtPointer();
        }

        private void RestoreWindow()
        {
            if (!IsVisible)
            {
                Show();
                PlayWindowEntrance();
            }
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            UpdateTray();
        }

        private void HideToTray()
        {
            PlayWindowExit(() =>
            {
                Hide();
                UpdateTray();
                if (!_trayNoticeShown)
                {
                    _trayNoticeShown = true;
                    _tray?.ShowNotice(Text.Get("Tray.StillRunningTitle"), Text.Get(PageLink.IsRoomActive
                        ? "Tray.StillRunningRoom" : "Tray.StillRunning"));
                }
            });
        }

        /// <summary>
        /// Draws every page once, almost invisibly, while Campfire is idle after starting. The first visit to a page
        /// then animates as smoothly as later ones instead of stalling to prepare its text and pictures.
        /// </summary>
        private async void WarmUpPages()
        {
            var pages = new FrameworkElement[] { PageTools, PagePreferences, PageLink, PageSettings, PageRepair, PageJre };
            foreach (var page in pages)
            {
                await Task.Delay(120);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (page == _shownPage || page.Visibility == Visibility.Visible)
                    continue;

                page.IsHitTestVisible = false;
                page.Opacity = 0.01;
                page.Visibility = Visibility.Visible;
                await Task.Delay(60);
                if (page != _shownPage)
                {
                    page.Visibility = Visibility.Hidden;
                    page.Opacity = 1;
                    page.IsHitTestVisible = true;
                }
            }
        }

        // ======================== Window animation ========================

        private void PlayWindowEntrance()
        {
            var scale = (ScaleTransform)PanRoot.RenderTransform;
            if (!Motion.Enabled)
            {
                PanRoot.BeginAnimation(OpacityProperty, null);
                PanRoot.Opacity = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PanRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            var grow = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        private void PlayWindowExit(Action done)
        {
            if (!Motion.Enabled)
            {
                done();
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
            fade.Completed += (sender, e) => done();
            var shrink = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
            var scale = (ScaleTransform)PanRoot.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            PanRoot.BeginAnimation(OpacityProperty, fade);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
                PlayWindowEntrance();
        }

        // ======================== Dialog ========================

        private Task WhenVisibleAsync()
        {
            var shown = new TaskCompletionSource<bool>();
            DependencyPropertyChangedEventHandler handler = null;
            handler = (sender, e) =>
            {
                if (!IsVisible)
                    return;
                IsVisibleChanged -= handler;
                shown.TrySetResult(true);
            };
            IsVisibleChanged += handler;
            return shown.Task;
        }

        async Task<DialogChoice> IDialogHost.ShowDialogAsync(DialogRequest request)
        {
            // One dialog at a time: later requests wait for the current one to close. A window kept in the
            // notification area is not pulled back for a message; the message waits until the user opens it.
            while (_dialog != null || !IsVisible)
            {
                if (_dialog != null)
                    await _dialog.Task;
                else
                    await WhenVisibleAsync();
            }

            _dialog = new TaskCompletionSource<DialogChoice>();
            _dialogLinkAction = request.LinkAction;
            _dialogLinkCloses = request.LinkCloses;

            LabDialogTitle.Text = request.Title;
            if (request.TitleColor.HasValue)
                LabDialogTitle.Foreground = new SolidColorBrush(request.TitleColor.Value);
            else
                LabDialogTitle.SetResourceReference(TextBlock.ForegroundProperty, request.IsError ? "BrushRed" : "Brush3");
            LabDialogMessage.Text = request.Message;
            if (request.MessageColor.HasValue)
                LabDialogMessage.Foreground = new SolidColorBrush(request.MessageColor.Value);
            else
                LabDialogMessage.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray1");
            ScrollDialogMessage.ScrollToTop();
            LabDialogDetail.Text = request.Detail;
            LabDialogDetail.Visibility = string.IsNullOrEmpty(request.Detail) ? Visibility.Collapsed : Visibility.Visible;
            BtnDialogConfirm.Content = request.PrimaryText;
            BtnDialogCancel.Content = request.SecondaryText;
            BtnDialogCancel.Visibility = request.SecondaryText == null ? Visibility.Collapsed : Visibility.Visible;
            BtnDialogLink.Content = request.LinkText;
            BtnDialogLink.Visibility = request.LinkText == null ? Visibility.Collapsed : Visibility.Visible;

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            PanDialog.IsHitTestVisible = true;
            PanDialog.Visibility = Visibility.Visible;
            var (tilt, lift) = DialogTransforms();
            if (Motion.Enabled)
            {
                // The dialog rises from below with a slight tilt and settles with a small bounce.
                var begin = TimeSpan.FromMilliseconds(60);
                PanDialog.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                CardDialog.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { BeginTime = begin });
                CardDialog.Opacity = 0;
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(60, 0, TimeSpan.FromMilliseconds(380))
                {
                    BeginTime = begin,
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
                });
                tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(380))
                {
                    BeginTime = begin,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
            else
            {
                PanDialog.BeginAnimation(OpacityProperty, null);
                CardDialog.BeginAnimation(OpacityProperty, null);
                CardDialog.Opacity = 1;
                lift.BeginAnimation(TranslateTransform.YProperty, null);
                tilt.BeginAnimation(RotateTransform.AngleProperty, null);
            }
            (request.SecondaryText == null ? BtnDialogConfirm : BtnDialogCancel).Focus();
            return await _dialog.Task;
        }

        private (RotateTransform Tilt, TranslateTransform Lift) DialogTransforms()
        {
            var group = (TransformGroup)CardDialog.RenderTransform;
            return ((RotateTransform)group.Children[0], (TranslateTransform)group.Children[1]);
        }

        private void BtnDialogConfirm_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog(DialogChoice.Primary);
        }

        private void BtnDialogCancel_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog(DialogChoice.Secondary);
        }

        private void BtnDialogLink_Click(object sender, RoutedEventArgs e)
        {
            if (_dialogLinkCloses)
                CloseDialog(DialogChoice.Link);
            else
                _dialogLinkAction?.Invoke();
        }

        private void CloseDialog(DialogChoice result)
        {
            var dialog = _dialog;
            _dialog = null;
            _dialogLinkAction = null;
            PanDialog.IsHitTestVisible = false;

            if (Motion.Enabled)
            {
                // Leaving, it drops a little, tilts the other way and fades; the shade follows.
                var (tilt, lift) = DialogTransforms();
                var fadeOut = new CubicEase { EasingMode = EasingMode.EaseIn };
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, TimeSpan.FromMilliseconds(150)) { EasingFunction = fadeOut });
                tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(6, TimeSpan.FromMilliseconds(150)) { EasingFunction = fadeOut });
                CardDialog.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(80))
                {
                    BeginTime = TimeSpan.FromMilliseconds(20),
                });
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
                fade.Completed += (sender, e) =>
                {
                    // A new dialog may have opened in the meantime.
                    if (_dialog == null)
                        PanDialog.Visibility = Visibility.Collapsed;
                };
                PanDialog.BeginAnimation(OpacityProperty, fade);
            }
            else
            {
                PanDialog.Visibility = Visibility.Collapsed;
            }
            dialog?.TrySetResult(result);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (PanDialog.Visibility == Visibility.Visible && _dialog != null && e.Key == Key.Escape)
            {
                CloseDialog(DialogChoice.Dismissed);
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }

        /// <summary>Campfire was opened again: bring this one back and say why no second window appeared.</summary>
        private async void ShowAlreadyRunning()
        {
            if (_exitRequested || _closeConfirmed)
                return;
            RestoreWindow();
            if (_alreadyRunningShown)
                return;
            _alreadyRunningShown = true;
            await this.AlertAsync(Text.Get("Instance.Title"), Text.Get("Instance.Message"));
            _alreadyRunningShown = false;
        }

        // ======================== Updates and notices ========================

        private void StartOnline()
        {
            new OnlineFlow(this, () => PageLink.IsRoomActive, PageHome.ShowNotice, ExitAndStart).Start();
        }

        /// <summary>Closes Campfire without asking again, then starts <paramref name="path"/> when given.</summary>
        private void ExitAndStart(string path)
        {
            _restartPath = path;
            _exitRequested = true;
            _closeConfirmed = true;
            Close();
        }

        private void StartAfterExit()
        {
            if (_restartPath == null)
                return;
            // The new Campfire must not find this one still holding the program file.
            SingleInstance.Release();
            try
            {
                Process.Start(new ProcessStartInfo(_restartPath) { UseShellExecute = false });
            }
            catch (Win32Exception)
            {
                // The new version is in place either way; it starts the next time the user opens Campfire.
            }
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
            PlayWindowExit(() => WindowState = WindowState.Minimized);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// The close button may only hide the window to the notification area. Really closing ends the room,
        /// so an open room asks first.
        /// </summary>
        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_exitRequested && !_closeConfirmed && _tray != null && TrayIcon.ShowIcon && TrayIcon.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            if (!_closeConfirmed && PageLink.IsRoomActive)
            {
                e.Cancel = true;
                _exitRequested = false;
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();

                bool confirmed = await this.ConfirmAsync(Text.Get("Dialog.ExitTitle"),
                    Text.Get(PageLink.HasOtherPlayers ? "Dialog.ExitWithPlayers" : "Dialog.ExitMessage"),
                    Text.Get("Dialog.Exit"));
                if (!confirmed)
                    return;
                _closeConfirmed = true;
                Close();
                return;
            }

            if (!_closeAnimated && Motion.Enabled && IsVisible)
            {
                e.Cancel = true;
                _closeConfirmed = true;
                PlayWindowExit(() =>
                {
                    _closeAnimated = true;
                    Close();
                });
                return;
            }

            PageLink.Shutdown();
            _trayMenu?.Close();
            _tray?.Dispose();
            _tray = null;
            StartAfterExit();
        }
    }
}
