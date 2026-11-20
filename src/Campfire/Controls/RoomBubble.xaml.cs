using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Campfire.Controls
{
    /// <summary>
    /// A small floating card for the active room, shown over the other pages. It can be dragged anywhere in its
    /// canvas, keeps its place as a share of the free space, and raises <see cref="Opened"/> when clicked.
    /// </summary>
    public partial class RoomBubble : UserControl
    {
        /// <summary>Gap kept between the bubble and the edges of its canvas.</summary>
        private const double Edge = 14;

        /// <summary>How far the pointer moves before a press becomes a drag.</summary>
        private const double DragDistance = 4;

        private const string PositionKey = "ui.roombubble";

        private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
        private static readonly IEasingFunction EaseIn = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
        private static readonly IEasingFunction Bounce = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });

        private double _shareX = 1;
        private double _shareY = 1;
        private bool _shown;
        private bool _pressed;
        private bool _dragging;
        private Point _pressPoint;
        private Point _pressOrigin;
        private Canvas _host;
        private Morph _morph;

        public RoomBubble()
        {
            InitializeComponent();
            LoadPosition();
            Loaded += (sender, e) => AttachHost();
            SizeChanged += (sender, e) => Place();
            MouseEnter += (sender, e) => Fade(PanHover, 1, 120);
            MouseLeave += (sender, e) => Fade(PanHover, 0, 160);
        }

        /// <summary>Raised when the bubble is clicked without being dragged.</summary>
        public event EventHandler Opened;

        /// <summary>Whether the bubble is shown or on its way in.</summary>
        public bool IsShown => _shown;

        /// <summary>Fills in the room name, a short status line and the color of the status dot.</summary>
        public void SetRoom(string title, string detail, string brushKey)
        {
            LabTitle.Text = title ?? "";
            LabDetail.Text = detail ?? "";
            DotRoom.SetResourceReference(Shape.FillProperty, brushKey);
        }

        /// <summary>
        /// Shows or hides the bubble. When <paramref name="from"/> is given, that picture of the page being left
        /// shrinks and travels into the bubble.
        /// </summary>
        public void SetShown(bool shown, ImageSource from = null)
        {
            if (shown == _shown)
                return;
            _shown = shown;
            _morph?.Stop();
            _morph = null;
            BeginAnimation(OpacityProperty, null);
            Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PanContent.BeginAnimation(OpacityProperty, null);
            PanRings.BeginAnimation(OpacityProperty, null);
            PanContent.Opacity = 1;
            PanRings.Opacity = 1;

            if (shown)
            {
                AttachHost();
                Visibility = Visibility.Visible;
                IsHitTestVisible = true;
                UpdateLayout();
                Place();
                if (!Motion.Enabled)
                {
                    Opacity = 1;
                    Zoom.ScaleX = Zoom.ScaleY = 1;
                }
                else if (from != null && _host != null && _host.ActualWidth > ActualWidth)
                {
                    Opacity = 0;
                    Zoom.ScaleX = Zoom.ScaleY = 1;
                    IsHitTestVisible = false;
                    _morph = new Morph(this, from);
                }
                else
                {
                    BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(160)) { EasingFunction = EaseOut });
                    ScaleTo(0.82, 1, 420, Bounce);
                }
                return;
            }

            IsHitTestVisible = false;
            _pressed = false;
            _dragging = false;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            if (!Motion.Enabled || Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Collapsed;
                return;
            }
            var fade = new DoubleAnimation(0, Ms(130)) { EasingFunction = EaseIn };
            fade.Completed += (sender, e) =>
            {
                if (!_shown)
                    Visibility = Visibility.Collapsed;
            };
            BeginAnimation(OpacityProperty, fade);
            ScaleTo(Zoom.ScaleX, 0.88, 130, EaseIn);
        }

        /// <summary>A picture of the element as it looks now, or <c>null</c> when it has no size.</summary>
        public static ImageSource Capture(FrameworkElement element)
        {
            double width = element.ActualWidth;
            double height = element.ActualHeight;
            if (width < 1 || height < 1 || element.Visibility != Visibility.Visible)
                return null;
            var dpi = VisualTreeHelper.GetDpi(element);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var brush = new VisualBrush(element)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, width, height),
                };
                context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ======================== Place ========================

        private void AttachHost()
        {
            var host = Parent as Canvas;
            if (host == _host)
                return;
            if (_host != null)
                _host.SizeChanged -= Host_SizeChanged;
            _host = host;
            if (_host != null)
                _host.SizeChanged += Host_SizeChanged;
        }

        private void Host_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Place();
        }

        /// <summary>Puts the bubble at its saved share of the free space.</summary>
        private void Place()
        {
            if (_host == null || _dragging)
                return;
            Canvas.SetLeft(this, Edge + _shareX * FreeWidth);
            Canvas.SetTop(this, Edge + _shareY * FreeHeight);
        }

        private double FreeWidth => Math.Max(0, (_host?.ActualWidth ?? 0) - 2 * Edge - ActualWidth);

        private double FreeHeight => Math.Max(0, (_host?.ActualHeight ?? 0) - 2 * Edge - ActualHeight);

        private void MoveTo(double left, double top)
        {
            left = Math.Max(Edge, Math.Min(Edge + FreeWidth, left));
            top = Math.Max(Edge, Math.Min(Edge + FreeHeight, top));
            Canvas.SetLeft(this, left);
            Canvas.SetTop(this, top);
            _shareX = FreeWidth > 0 ? (left - Edge) / FreeWidth : 1;
            _shareY = FreeHeight > 0 ? (top - Edge) / FreeHeight : 1;
        }

        private void LoadPosition()
        {
            string[] parts = (AppSettings.Get(PositionKey) ?? "").Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                && x >= 0 && x <= 1 && y >= 0 && y <= 1)
            {
                _shareX = x;
                _shareY = y;
            }
        }

        private void SavePosition()
        {
            AppSettings.Set(PositionKey, _shareX.ToString("0.###", CultureInfo.InvariantCulture) + ","
                                         + _shareY.ToString("0.###", CultureInfo.InvariantCulture));
        }

        // ======================== Pointer ========================

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_host == null || !_shown)
                return;
            _pressed = true;
            _dragging = false;
            _pressPoint = e.GetPosition(_host);
            _pressOrigin = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
            CaptureMouse();
            ScaleTo(Zoom.ScaleX, 0.97, 90, EaseOut);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_pressed || _host == null)
                return;
            var offset = e.GetPosition(_host) - _pressPoint;
            if (!_dragging)
            {
                if (offset.Length < DragDistance)
                    return;
                _dragging = true;
                Cursor = Cursors.SizeAll;
                ScaleTo(Zoom.ScaleX, 1.04, 160, EaseOut);
            }
            MoveTo(_pressOrigin.X + offset.X, _pressOrigin.Y + offset.Y);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_pressed)
                return;
            bool dragged = _dragging;
            EndPress();
            if (dragged)
                SavePosition();
            else if (IsMouseOver)
                Opened?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (!_pressed)
                return;
            bool dragged = _dragging;
            EndPress();
            if (dragged)
                SavePosition();
        }

        private void EndPress()
        {
            _pressed = false;
            _dragging = false;
            Cursor = Cursors.Hand;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            if (_shown)
                ScaleTo(Zoom.ScaleX, 1, 360, Bounce);
        }

        // ======================== Helpers ========================

        private void ScaleTo(double from, double to, int milliseconds, IEasingFunction easing)
        {
            if (!Motion.Enabled)
            {
                Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                Zoom.ScaleX = Zoom.ScaleY = to;
                return;
            }
            var animation = new DoubleAnimation(from, to, Ms(milliseconds)) { EasingFunction = easing };
            Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private static void Fade(UIElement element, double to, int milliseconds)
        {
            if (!Motion.Enabled)
            {
                element.BeginAnimation(OpacityProperty, null);
                element.Opacity = to;
                return;
            }
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Ms(milliseconds)) { EasingFunction = EaseOut });
        }

        /// <summary>Shows the text and rings of the bubble once the travelling picture has landed.</summary>
        private void Land()
        {
            _morph = null;
            Opacity = 1;
            IsHitTestVisible = true;
            PanContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(180)) { EasingFunction = EaseOut });
            PanRings.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(240)) { EasingFunction = EaseOut });
        }

        private static Duration Ms(int milliseconds) => new Duration(TimeSpan.FromMilliseconds(milliseconds));

        private static IEasingFunction Freeze(EasingFunctionBase easing)
        {
            easing.Freeze();
            return easing;
        }

        private static double Smooth(double from, double to, double value)
        {
            double t = Math.Max(0, Math.Min(1, (value - from) / (to - from)));
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// A picture of the page that shrinks into the bubble along a curve. Springs drive its center and size, the
        /// sideways one stiffer than the downward one. Its corners round off and the picture gives way to the
        /// bubble's own surface as it gets smaller.
        /// </summary>
        private sealed class Morph
        {
            private const double PageCorner = 6;

            private readonly RoomBubble _owner;
            private readonly Canvas _host;
            private readonly Border _ghost;
            private readonly Image _image;
            private readonly SolidColorBrush _surface;
            private readonly Spring _x = new Spring(360, 34);
            private readonly Spring _y = new Spring(210, 25);
            private readonly Spring _width = new Spring(300, 34.6);
            private readonly Spring _height = new Spring(300, 34.6);
            private readonly Rect _start;
            private readonly Rect _end;
            private TimeSpan _last = TimeSpan.MinValue;
            private double _elapsed;

            public Morph(RoomBubble owner, ImageSource picture)
            {
                _owner = owner;
                _host = owner._host;
                _start = new Rect(0, 0, _host.ActualWidth, _host.ActualHeight);
                _end = new Rect(Canvas.GetLeft(owner), Canvas.GetTop(owner), owner.ActualWidth, owner.ActualHeight);

                var surface = owner.TryFindResource("BrushSurface") as SolidColorBrush;
                _surface = new SolidColorBrush(surface?.Color ?? Colors.White) { Opacity = 0 };
                _image = new Image { Source = picture, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.LowQuality);
                _ghost = new Border { Background = _surface, Child = _image, IsHitTestVisible = false };
                _host.Children.Insert(Math.Max(0, _host.Children.IndexOf(owner)), _ghost);

                _x.Snap(_start.X + _start.Width / 2);
                _y.Snap(_start.Y + _start.Height / 2);
                _width.Snap(_start.Width);
                _height.Snap(_start.Height);
                _x.Target = _end.X + _end.Width / 2;
                _y.Target = _end.Y + _end.Height / 2;
                _width.Target = _end.Width;
                _height.Target = _end.Height;
                Apply();
                CompositionTarget.Rendering += OnFrame;
            }

            public void Stop()
            {
                CompositionTarget.Rendering -= OnFrame;
                _host.Children.Remove(_ghost);
            }

            private void OnFrame(object sender, EventArgs e)
            {
                var time = ((RenderingEventArgs)e).RenderingTime;
                if (time == _last)
                    return;
                double seconds = _last == TimeSpan.MinValue ? 1 / 120.0 : Math.Min((time - _last).TotalSeconds, 1 / 30.0);
                _last = time;
                _elapsed += seconds;

                _x.Step(seconds);
                _y.Step(seconds);
                _width.Step(seconds);
                _height.Step(seconds);
                Apply();

                if (_elapsed > 1.2 || (Settled(_x) && Settled(_y) && Settled(_width) && Settled(_height)))
                {
                    Stop();
                    _owner.Land();
                }
            }

            private static bool Settled(Spring spring)
            {
                return Math.Abs(spring.Value - spring.Target) < 0.4 && Math.Abs(spring.Velocity) < 8;
            }

            private void Apply()
            {
                double width = Math.Max(1, _width.Value);
                double height = Math.Max(1, _height.Value);
                double progress = Math.Max(0, Math.Min(1, (_start.Height - height) / Math.Max(1, _start.Height - _end.Height)));
                double radius = Math.Min(Math.Min(width, height) / 2, PageCorner + (_end.Height / 2 - PageCorner) * progress);

                _image.Opacity = 1 - Smooth(0, 0.6, progress);
                _surface.Opacity = Smooth(0.05, 0.5, progress);
                _ghost.Width = width;
                _ghost.Height = height;
                _ghost.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
                Canvas.SetLeft(_ghost, _x.Value - width / 2);
                Canvas.SetTop(_ghost, _y.Value - height / 2);
            }
        }
    }
}
