using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Campfire
{
    /// <summary>
    /// The Campfire icon in the notification area. It shows the room state in its tooltip and flickers like the
    /// logo while a room is open. A click opens the window; a right-click asks for the menu, which
    /// <see cref="TrayMenu"/> draws.
    /// </summary>
    /// <remarks>
    /// While the icon should be shown it is checked every few seconds and added again whenever it is missing.
    /// </remarks>
    internal sealed class TrayIcon : IDisposable
    {
        private const int FrameSize = 96;
        private const int IconId = 1;
        private const int CallbackMessage = 0x8001; // WM_APP + 1
        private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        private readonly HwndSource _window;
        private readonly List<Drawing.Icon> _frames = new List<Drawing.Icon>();
        private readonly DispatcherTimer _animation;
        private readonly DispatcherTimer _check;
        private int _frame;
        private string _text = "Campfire";
        private bool _visible;
        private bool _shown;

        public TrayIcon()
        {
            LoadFrames();

            // An invisible top-level window receives the clicks, and the broadcast sent when Explorer restarts.
            _window = new HwndSource(new HwndSourceParameters("Campfire notification area") { WindowStyle = 0 });
            _window.AddHook(WndProc);

            _animation = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            _animation.Tick += (sender, e) =>
            {
                _frame = (_frame + 1) % _frames.Count;
                Apply();
            };
            _check = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            _check.Tick += (sender, e) => Apply();
        }

        public event EventHandler OpenRequested;

        /// <summary>Raised on a right-click; the menu opens at the mouse pointer.</summary>
        public event EventHandler MenuRequested;

        // ======================== Settings ========================

        public static bool ShowIcon
        {
            get => AppSettings.Get("tray.show") != "0";
            set => AppSettings.Set("tray.show", value ? null : "0");
        }

        /// <summary>Whether the close button keeps Campfire running in the notification area.</summary>
        public static bool CloseToTray
        {
            get => AppSettings.Get("tray.close") == "1";
            set => AppSettings.Set("tray.close", value ? "1" : null);
        }

        // ======================== State ========================

        /// <summary>Shows or hides the icon and updates its tooltip.</summary>
        /// <param name="status">A short line about the room, or <c>null</c> when there is none.</param>
        public void Update(bool visible, string status, bool active)
        {
            // The tooltip of the notification area holds at most 127 characters.
            string text = status == null ? "Campfire" : "Campfire · " + status;
            _text = text.Length > 127 ? text.Substring(0, 127) : text;
            _visible = visible && _frames.Count > 0;

            if (active && _visible && _frames.Count > 1)
            {
                _animation.Start();
            }
            else
            {
                _animation.Stop();
                _frame = 0;
            }
            Apply();
        }

        public void ShowNotice(string title, string text)
        {
            if (!_shown)
                return;
            var data = Data(NifInfo);
            data.Info = text.Length > 255 ? text.Substring(0, 255) : text;
            data.InfoTitle = title.Length > 63 ? title.Substring(0, 63) : title;
            data.TimeoutOrVersion = 4000;
            Shell_NotifyIcon(NimModify, ref data);
        }

        public void Dispose()
        {
            _animation.Stop();
            _visible = false;
            Apply();
            _window.RemoveHook(WndProc);
            _window.Dispose();
            foreach (var frame in _frames)
            {
                DestroyIcon(frame.Handle);
                frame.Dispose();
            }
            _frames.Clear();
        }

        /// <summary>Cuts the logo frames into icons of the size the notification area uses.</summary>
        private void LoadFrames()
        {
            try
            {
                var strip = new BitmapImage();
                strip.BeginInit();
                strip.UriSource = new Uri("pack://application:,,,/Assets/CampfireFrames.png", UriKind.Absolute);
                strip.CacheOption = BitmapCacheOption.OnLoad;
                strip.EndInit();

                int size = Forms.SystemInformation.SmallIconSize.Width;
                int count = strip.PixelWidth / FrameSize;
                for (int i = 0; i < count; i++)
                {
                    var frame = new CroppedBitmap(strip, new Int32Rect(i * FrameSize, 0, FrameSize, FrameSize));
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(size / (double)FrameSize, size / (double)FrameSize));
                    var pixels = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                    int stride = pixels.PixelWidth * 4;
                    var buffer = new byte[stride * pixels.PixelHeight];
                    pixels.CopyPixels(buffer, stride, 0);

                    using (var bitmap = new Drawing.Bitmap(pixels.PixelWidth, pixels.PixelHeight, Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                            Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
                        for (int y = 0; y < bitmap.Height; y++)
                            Marshal.Copy(buffer, y * stride, data.Scan0 + y * data.Stride, stride);
                        bitmap.UnlockBits(data);
                        _frames.Add(Drawing.Icon.FromHandle(bitmap.GetHicon()));
                    }
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is ExternalException || ex is ArgumentException)
            {
                // Without frames the icon is simply not shown.
            }
        }

        // ======================== Notification area ========================

        /// <summary>Puts the icon in the notification area, updates it there, or removes it.</summary>
        private void Apply()
        {
            var data = Data(NifMessage | NifIcon | NifTip);
            if (!_visible)
            {
                _check.Stop();
                if (_shown)
                    Shell_NotifyIcon(NimDelete, ref data);
                _shown = false;
                return;
            }

            // An update fails when Explorer no longer has the icon; adding it again brings it back.
            _shown = Shell_NotifyIcon(NimModify, ref data) || Shell_NotifyIcon(NimAdd, ref data);
            _check.Start();
        }

        private NotifyIconData Data(int flags)
        {
            return new NotifyIconData
            {
                Size = Marshal.SizeOf(typeof(NotifyIconData)),
                Window = _window.Handle,
                Id = IconId,
                Flags = flags,
                CallbackMessage = CallbackMessage,
                Icon = _frames.Count > 0 ? _frames[_frame].Handle : IntPtr.Zero,
                Tip = _text,
            };
        }

        private IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == CallbackMessage)
            {
                switch (lParam.ToInt32() & 0xFFFF)
                {
                    case WmLButtonUp:
                        OpenRequested?.Invoke(this, EventArgs.Empty);
                        break;
                    case WmRButtonUp:
                        MenuRequested?.Invoke(this, EventArgs.Empty);
                        break;
                }
                handled = true;
            }
            else if (message == TaskbarCreated)
            {
                // Explorer started again and has forgotten every icon.
                _shown = false;
                Apply();
            }
            return IntPtr.Zero;
        }

        private const int NimAdd = 0;
        private const int NimModify = 1;
        private const int NimDelete = 2;
        private const int NifMessage = 0x1;
        private const int NifIcon = 0x2;
        private const int NifTip = 0x4;
        private const int NifInfo = 0x10;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonUp = 0x0205;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public int Size;
            public IntPtr Window;
            public int Id;
            public int Flags;
            public int CallbackMessage;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Tip;
            public int State;
            public int StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Info;
            public int TimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string InfoTitle;
            public int InfoFlags;
            public Guid Item;
            public IntPtr BalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
