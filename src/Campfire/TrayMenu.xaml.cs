using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Campfire.Controls;
using Campfire.Link;
using Campfire.Link.Minecraft;
using Campfire.Localization;
using Campfire.Pages;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Campfire
{
    /// <summary>
    /// The menu of the notification area icon: the room at a glance and the common actions. It opens next to the
    /// mouse pointer, follows the room while it is open and closes as soon as it loses focus.
    /// </summary>
    public partial class TrayMenu : Window
    {
        private const int MaxPlayers = 5;
        private static readonly TimeSpan CopiedFeedback = TimeSpan.FromMilliseconds(700);

        private readonly LinkPage _link;
        private readonly DispatcherTimer _clock;
        private Drawing.Point _anchor;
        private bool _above = true;
        private bool _placed;
        private bool _closing;
        private bool _copying;
        private string _playersSignature;

        internal TrayMenu(LinkPage link, ResourceDictionary theme)
        {
            InitializeComponent();
            // Theme colors live on the main window, where the theme can change them.
            Resources.MergedDictionaries.Add(theme);
            _link = link;

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (sender, e) => UpdateDuration();

            _link.StatusChanged += Link_StatusChanged;
            Text.LanguageChanged += Link_StatusChanged;
            Closed += (sender, e) =>
            {
                _clock.Stop();
                _link.StatusChanged -= Link_StatusChanged;
                Text.LanguageChanged -= Link_StatusChanged;
            };
            SizeChanged += (sender, e) => Place();
            DpiChanged += (sender, e) => Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded);
            Refresh();
        }

        public event EventHandler OpenRequested;

        public event EventHandler CreateRequested;

        public event EventHandler JoinRequested;

        public event EventHandler StopRequested;

        public event EventHandler ExitRequested;

        /// <summary>Opens the menu at the mouse pointer and gives it focus.</summary>
        public void ShowAtPointer()
        {
            GetCursorPos(out var pointer);
            _anchor = new Drawing.Point(pointer.X, pointer.Y);
            Show();
            UpdateLayout();
            _placed = true;
            Place();
            Activate();
            SetForegroundWindow(new WindowInteropHelper(this).Handle);
            PlayEntrance();
        }

        /// <summary>Closes the menu with a short fade.</summary>
        public void Dismiss()
        {
            if (_closing)
                return;
            _closing = true;
            if (!Motion.Enabled || Opacity <= 0)
            {
                Close();
                return;
            }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
            fade.Completed += (sender, e) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        // ======================== Content ========================

        private void Link_StatusChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        private void Refresh()
        {
            bool host = _link.Role == LinkRole.Host;
            bool working = _link.Stage == LinkStage.Working;
            bool ready = _link.Stage == LinkStage.Ready;

            // Header
            if (working)
                LabStatus.Text = Text.Get(host ? "Tray.Creating" : "Tray.Joining");
            else if (ready)
                LabStatus.Text = host ? Text.Format("Home.Status.Hosting", _link.PlayerCount) : Text.Get("Home.Status.Joined");
            else
                LabStatus.Text = Text.Get("Tray.NoRoom");
            DotStatus.SetResourceReference(Shape.FillProperty, ready ? "BrushGreen" : working ? "BrushOrange" : "BrushGray5");

            // Room card
            CardRoom.Visibility = VisibleIf(working || ready);
            PanWorking.Visibility = VisibleIf(working);
            PanHero.Visibility = VisibleIf(ready);
            LineStats.Visibility = VisibleIf(ready);
            GridStats.Visibility = VisibleIf(ready);
            PanPlayers.Visibility = VisibleIf(ready);
            if (working)
            {
                var step = _link.ActiveStep;
                LabStep.Text = step.Title;
                LabStepDetail.Text = step.Detail;
            }
            if (ready)
            {
                if (host)
                    RenderHost();
                else
                    RenderGuest();
                RenderPlayers();
                UpdateDuration();
                _clock.Start();
            }
            else
            {
                _clock.Stop();
            }

            // Actions
            BtnCreate.Visibility = VisibleIf(!working && !ready);
            BtnJoin.Visibility = VisibleIf(!working && !ready);
            BtnCopy.Visibility = VisibleIf(ready);
            if (!_copying)
                BtnCopy.Content = Text.Get(host ? "Link.CopyCode" : "Link.CopyAddress");
            BtnStop.Visibility = VisibleIf(working || ready);
            BtnStop.Content = Text.Get(working ? host ? "Tray.CancelCreate" : "Tray.CancelJoin"
                : host ? "Link.CloseRoom" : "Link.LeaveRoom");
        }

        private void RenderHost()
        {
            LabHeroCaption.Text = Text.Get("Link.InviteCode");
            LabHero.Text = _link.Code?.Code;
            LabHero.SetResourceReference(TextBlock.FontFamilyProperty, "FontCode");
            LabHero.SetResourceReference(TextBlock.ForegroundProperty, "Brush2");
            LabHeroSideCaption.Text = Text.Get("Tray.OpenFor");

            ClearStats();
            AddStat(Text.Get("Link.Connections"), _link.ConnectionsText);
            AddStat(Text.Get("Link.LocalNetwork"), _link.LocalNatText);
            AddStat(Text.Get("Link.GamePort"), _link.MinecraftPort.ToString(CultureInfo.InvariantCulture));
        }

        private void RenderGuest()
        {
            LabHeroCaption.Text = Text.Get("Link.Latency");
            LabHero.Text = _link.LatencyText;
            LabHero.SetResourceReference(TextBlock.FontFamilyProperty, "FontMain");
            double? latency = _link.HostLatency;
            LabHero.SetResourceReference(TextBlock.ForegroundProperty, latency == null ? "BrushGray4"
                : latency < 80 ? "BrushGreen" : latency < 160 ? "BrushOrange" : "BrushRed");
            LabHeroSideCaption.Text = Text.Get("Tray.JoinedFor");

            ClearStats();
            AddStat(Text.Get("Link.Path"), _link.HostPathText);
            AddStat(Text.Get("Link.Address"), _link.GameAddress);
            AddStat(Text.Get("Link.LocalNetwork"), _link.LocalNatText);
        }

        private void RenderPlayers()
        {
            var players = _link.Players;
            LabPlayers.Text = Text.Format("Link.Players", players.Count);
            LabMorePlayers.Visibility = VisibleIf(players.Count > MaxPlayers);
            LabMorePlayers.Text = Text.Format("Tray.MorePlayers", players.Count - MaxPlayers);

            bool skins = LinkSettings.ShowSkins;
            string signature = Text.Language + "|" + skins + "|" + string.Join("|", players.Take(MaxPlayers).Select(p =>
                p.MachineId + ":" + p.Name + ":" + p.Kind + ":" + (PlayerHeads.TryGetCached(p.Uuid) != null)));
            if (signature == _playersSignature)
                return;
            _playersSignature = signature;
            ListPlayers.ItemsSource = players.Take(MaxPlayers)
                .Select(p => new PlayerRow(p, skins ? PlayerHeads.TryGetCached(p.Uuid) : null))
                .ToList();
        }

        private void UpdateDuration()
        {
            var time = DateTime.UtcNow - _link.ReadySince;
            if (time < TimeSpan.Zero)
                time = TimeSpan.Zero;
            LabHeroSide.Text = time.TotalHours >= 1
                ? ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + time.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private void ClearStats()
        {
            GridStats.Children.Clear();
            GridStats.RowDefinitions.Clear();
        }

        private void AddStat(string label, string value)
        {
            int row = GridStats.RowDefinitions.Count;
            GridStats.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var caption = new TextBlock { Text = label, Style = (Style)FindResource("StatLabel"), Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(caption, row);
            GridStats.Children.Add(caption);

            var text = new TextBlock { Text = value, Style = (Style)FindResource("StatValue"), Margin = new Thickness(16, 3, 0, 3) };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            GridStats.Children.Add(text);
        }

        private static Visibility VisibleIf(bool visible)
        {
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // ======================== Actions ========================

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            Run(OpenRequested);
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            Run(CreateRequested);
        }

        private void BtnJoin_Click(object sender, RoutedEventArgs e)
        {
            Run(JoinRequested);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            Run(StopRequested);
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Run(ExitRequested);
        }

        /// <summary>Copies the invite code or the address, shows that it worked, then closes.</summary>
        private async void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_copying)
                return;
            _copying = true;
            bool copied = _link.Role == LinkRole.Host ? _link.CopyCode() : _link.CopyAddress();
            BtnCopy.Content = Text.Get(copied ? "Link.Copied" : "Link.CopyFailedTitle");
            await Task.Delay(CopiedFeedback);
            Dismiss();
        }

        private void Run(EventHandler action)
        {
            _closing = true;
            Close();
            action?.Invoke(this, EventArgs.Empty);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Dismiss();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Dismiss();
        }

        // ======================== Placement ========================

        /// <summary>
        /// Puts the menu next to the pointer: above it when the taskbar is at the bottom, below it otherwise, and
        /// always inside the work area. Works in physical pixels.
        /// </summary>
        private void Place()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (!_placed || _closing || handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
                return;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            var area = Forms.Screen.FromPoint(_anchor).WorkingArea;
            // The transparent shadow margin may stick out of the work area; the visible menu may not.
            int inset = (int)Math.Round(PanRoot.Margin.Left * VisualTreeHelper.GetDpi(this).DpiScaleX);

            int x = _anchor.X - inset;
            if (x + width - inset > area.Right)
                x = _anchor.X - width + inset;
            _above = _anchor.Y - height + inset >= area.Top;
            int y = _above ? _anchor.Y - height + inset : _anchor.Y - inset;

            x = Math.Max(area.Left - inset, Math.Min(x, area.Right - width + inset));
            y = Math.Max(area.Top - inset, Math.Min(y, area.Bottom - height + inset));
            if (x != rect.Left || y != rect.Top)
                SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }

        private void PlayEntrance()
        {
            if (!Motion.Enabled)
            {
                Opacity = 1;
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            ((TranslateTransform)PanRoot.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(_above ? 10 : -10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }

        // ======================== Native ========================

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
    }
}
