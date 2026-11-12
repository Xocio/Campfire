using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Campfire.Localization;

namespace Campfire
{
    /// <summary>Window frame: edge resizing, maximizing, full screen and the saved window size.</summary>
    public partial class MainWindow
    {
        private const string WidthKey = "window.width";
        private const string HeightKey = "window.height";
        private const string MaximizedKey = "window.maximized";

        /// <summary>Transparent margin around the surface, holding the shadow.</summary>
        private const double ShadowMargin = 12;
        /// <summary>Width of the resize band, measured from the window edge.</summary>
        private const double ResizeBand = ShadowMargin + 4;
        /// <summary>Length of the corner resize zones, measured from the window edge.</summary>
        private const double ResizeCorner = ShadowMargin + 14;
        private const double SurfaceRadius = 6;
        /// <summary>Distance from the top of the title bar to the cursor after a drag restores the window.</summary>
        private const double RestoreDragOffset = 24;

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_EXITSIZEMOVE = 0x0232;

        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private IntPtr _handle;
        private bool _fullScreen;
        private bool _normalBeforeFullScreen;
        private Point? _maximizedDragStart;
        private WindowState _lastState = WindowState.Normal;
        /// <summary>The state to return to from the minimized window.</summary>
        private WindowState _shownState = WindowState.Normal;

        /// <summary>Applies the saved size and state.</summary>
        private void LoadWindowFrame()
        {
            var area = SystemParameters.WorkArea;
            if (TryReadSize(WidthKey, out double width))
                Width = Math.Max(MinWidth, Math.Min(width, area.Width));
            if (TryReadSize(HeightKey, out double height))
                Height = Math.Max(MinHeight, Math.Min(height, area.Height));
            if (AppSettings.Get(MaximizedKey) == "1")
            {
                WindowState = WindowState.Maximized;
                _lastState = _shownState = WindowState.Maximized;
            }
            SourceInitialized += (sender, e) =>
            {
                _handle = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(_handle).AddHook(WndProc);
            };
            UpdateFrame();
        }

        private static bool TryReadSize(string key, out double size)
        {
            return double.TryParse(AppSettings.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out size) && size > 0;
        }

        private void SaveWindowSize()
        {
            if (WindowState != WindowState.Normal)
                return;
            string width = ActualWidth.ToString("0", CultureInfo.InvariantCulture);
            string height = ActualHeight.ToString("0", CultureInfo.InvariantCulture);
            if (AppSettings.Get(WidthKey) != width)
                AppSettings.Set(WidthKey, width);
            if (AppSettings.Get(HeightKey) != height)
                AppSettings.Set(HeightKey, height);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (_lastState == WindowState.Minimized && WindowState != WindowState.Minimized)
                PlayWindowEntrance();
            _lastState = WindowState;
            if (WindowState == WindowState.Minimized)
                return;

            _shownState = WindowState;
            if (WindowState == WindowState.Normal)
                _fullScreen = false;
            if (!_fullScreen)
            {
                string maximized = WindowState == WindowState.Maximized ? "1" : null;
                if (AppSettings.Get(MaximizedKey) != maximized)
                    AppSettings.Set(MaximizedKey, maximized);
            }
            UpdateFrame();
        }

        /// <summary>Square corners and no shadow while the window fills the screen, and the matching button.</summary>
        private void UpdateFrame()
        {
            bool filled = WindowState == WindowState.Maximized;
            double radius = filled ? 0 : SurfaceRadius;
            PanRoot.Margin = new Thickness(filled ? 0 : ShadowMargin);
            PanShadow.Visibility = filled ? Visibility.Collapsed : Visibility.Visible;
            PanSurface.CornerRadius = new CornerRadius(radius);
            PanDialog.CornerRadius = new CornerRadius(radius);
            PanTitle.CornerRadius = new CornerRadius(radius, radius, 0, 0);
            Tour.SurfaceRadius = radius;
            IconMaximize.Visibility = filled ? Visibility.Collapsed : Visibility.Visible;
            IconRestore.Visibility = filled ? Visibility.Visible : Visibility.Collapsed;
            BtnMaximize.ToolTip = Text.Get(_fullScreen ? "App.ExitFullScreen" : filled ? "App.Restore" : "App.Maximize");
        }

        private void ToggleMaximized()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void ToggleFullScreen()
        {
            if (_fullScreen)
            {
                _fullScreen = false;
                if (_normalBeforeFullScreen)
                    WindowState = WindowState.Normal;
                else
                    FitToMonitor();
            }
            else
            {
                _normalBeforeFullScreen = WindowState == WindowState.Normal;
                _fullScreen = true;
                if (_normalBeforeFullScreen)
                    WindowState = WindowState.Maximized;
                else
                    FitToMonitor();
            }
            UpdateFrame();
        }

        /// <summary>Resizes the maximized window to the screen or the work area.</summary>
        private void FitToMonitor()
        {
            if (!TryGetMonitor(out var info))
                return;
            var area = _fullScreen ? info.rcMonitor : info.rcWork;
            SetWindowPos(_handle, IntPtr.Zero, area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private bool TryGetMonitor(out MonitorInfo info)
        {
            info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
            IntPtr monitor = MonitorFromWindow(_handle, MONITOR_DEFAULTTONEAREST);
            return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info);
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (_fullScreen)
                ToggleFullScreen();
            else
                ToggleMaximized();
        }

        private void PanTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;
            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
                return;
            }
            if (WindowState == WindowState.Normal)
                DragMove();
            else if (!_fullScreen)
                _maximizedDragStart = e.GetPosition(this);
        }

        /// <summary>Dragging the title bar of the maximized window restores it under the cursor.</summary>
        private void PanTitle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_maximizedDragStart == null)
                return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _maximizedDragStart = null;
                return;
            }

            var position = e.GetPosition(this);
            var moved = position - _maximizedDragStart.Value;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _maximizedDragStart = null;
            double share = position.X / ActualWidth;
            double restoredWidth = RestoreBounds.Width;
            var cursor = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice.Transform(PointToScreen(position));
            WindowState = WindowState.Normal;
            Left = cursor.X - restoredWidth * share;
            Top = cursor.Y - ShadowMargin - RestoreDragOffset;
            DragMove();
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (message)
            {
                case WM_NCHITTEST:
                    int hit = ResizeHit(lParam);
                    if (hit != 0)
                    {
                        handled = true;
                        return new IntPtr(hit);
                    }
                    break;
                case WM_GETMINMAXINFO:
                    FillMinMaxInfo(lParam);
                    handled = true;
                    break;
                case WM_EXITSIZEMOVE:
                    SaveWindowSize();
                    break;
            }
            return IntPtr.Zero;
        }

        /// <summary>The resize zone under a cursor position given in screen pixels, or 0 for none.</summary>
        private int ResizeHit(IntPtr screenPosition)
        {
            if (WindowState != WindowState.Normal)
                return 0;

            long value = screenPosition.ToInt64();
            var point = PointFromScreen(new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF)));
            double fromRight = ActualWidth - point.X;
            double fromBottom = ActualHeight - point.Y;

            bool left = point.X < ResizeBand, right = fromRight <= ResizeBand;
            bool top = point.Y < ResizeBand, bottom = fromBottom <= ResizeBand;
            bool nearLeft = point.X < ResizeCorner, nearRight = fromRight <= ResizeCorner;
            bool nearTop = point.Y < ResizeCorner, nearBottom = fromBottom <= ResizeCorner;

            if ((top && nearLeft) || (left && nearTop))
                return HTTOPLEFT;
            if ((top && nearRight) || (right && nearTop))
                return HTTOPRIGHT;
            if ((bottom && nearLeft) || (left && nearBottom))
                return HTBOTTOMLEFT;
            if ((bottom && nearRight) || (right && nearBottom))
                return HTBOTTOMRIGHT;
            if (left)
                return HTLEFT;
            if (right)
                return HTRIGHT;
            if (top)
                return HTTOP;
            return bottom ? HTBOTTOM : 0;
        }

        /// <summary>Sets the maximized area to the work area, or the whole screen in full screen, and the smallest size.</summary>
        private void FillMinMaxInfo(IntPtr pointer)
        {
            var info = (MinMaxInfo)Marshal.PtrToStructure(pointer, typeof(MinMaxInfo));
            if (TryGetMonitor(out var monitor))
            {
                var area = _fullScreen ? monitor.rcMonitor : monitor.rcWork;
                info.ptMaxPosition.X = area.Left - monitor.rcMonitor.Left;
                info.ptMaxPosition.Y = area.Top - monitor.rcMonitor.Top;
                info.ptMaxSize.X = area.Right - area.Left;
                info.ptMaxSize.Y = area.Bottom - area.Top;
                info.ptMaxTrackSize.X = Math.Max(info.ptMaxTrackSize.X, info.ptMaxSize.X);
                info.ptMaxTrackSize.Y = Math.Max(info.ptMaxTrackSize.Y, info.ptMaxSize.Y);
            }
            var target = PresentationSource.FromVisual(this)?.CompositionTarget;
            double scaleX = target?.TransformToDevice.M11 ?? 1;
            double scaleY = target?.TransformToDevice.M22 ?? 1;
            info.ptMinTrackSize.X = (int)Math.Ceiling(MinWidth * scaleX);
            info.ptMinTrackSize.Y = (int)Math.Ceiling(MinHeight * scaleY);
            Marshal.StructureToPtr(info, pointer, false);
        }

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

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint ptReserved;
            public NativePoint ptMaxSize;
            public NativePoint ptMaxPosition;
            public NativePoint ptMinTrackSize;
            public NativePoint ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}
