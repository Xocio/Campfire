using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Campfire.Controls
{
    /// <summary>
    /// The animated campfire logo. Frames come from a horizontal strip and play at ten frames per second.
    /// </summary>
    public sealed class CampfireLogo : Image
    {
        private const int FrameSize = 96;
        private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(100);
        private static BitmapSource[] _frames;

        private readonly DispatcherTimer _timer;
        private int _index;

        public CampfireLogo()
        {
            Stretch = Stretch.Uniform;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            Source = Frames[0];

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameTime };
            _timer.Tick += (sender, e) =>
            {
                _index = (_index + 1) % Frames.Length;
                Source = Frames[_index];
            };
            // IsVisible also turns false when the logo leaves the tree or its window is hidden.
            IsVisibleChanged += (sender, e) =>
            {
                if (IsVisible)
                    _timer.Start();
                else
                    _timer.Stop();
            };
        }

        private static BitmapSource[] Frames
        {
            get
            {
                if (_frames != null)
                    return _frames;

                var strip = new BitmapImage();
                strip.BeginInit();
                strip.UriSource = new Uri("pack://application:,,,/Assets/CampfireFrames.png", UriKind.Absolute);
                strip.CacheOption = BitmapCacheOption.OnLoad;
                strip.EndInit();
                strip.Freeze();

                int count = strip.PixelWidth / FrameSize;
                var frames = new BitmapSource[count];
                for (int i = 0; i < count; i++)
                {
                    var frame = new CroppedBitmap(strip, new Int32Rect(i * FrameSize, 0, FrameSize, FrameSize));
                    frame.Freeze();
                    frames[i] = frame;
                }
                _frames = frames;
                return _frames;
            }
        }
    }
}
