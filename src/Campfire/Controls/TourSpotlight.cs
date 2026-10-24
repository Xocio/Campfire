using System;
using System.Windows;
using System.Windows.Media;

namespace Campfire.Controls
{
    /// <summary>
    /// The dimmed layer of the tour: everything is shaded except a rounded hole around the element being shown.
    /// A ring hugs the hole and a second ring spreads out from it and fades, over and over.
    /// </summary>
    internal sealed class TourSpotlight : FrameworkElement
    {
        private static readonly Brush Shade = Frozen(new SolidColorBrush(Color.FromArgb(0xC4, 0x1C, 0x10, 0x08)));

        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            nameof(Accent), typeof(Brush), typeof(TourSpotlight),
            new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DimProperty = DependencyProperty.Register(
            nameof(Dim), typeof(double), typeof(TourSpotlight),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Colour of the rings.</summary>
        public Brush Accent
        {
            get => (Brush)GetValue(AccentProperty);
            set => SetValue(AccentProperty, value);
        }

        /// <summary>How strongly the layer shades the window, from 0 to 1.</summary>
        public double Dim
        {
            get => (double)GetValue(DimProperty);
            set => SetValue(DimProperty, value);
        }

        public Rect Hole { get; set; }

        public double Radius { get; set; }

        /// <summary>Progress of the spreading ring, from 0 to 1.</summary>
        public double Pulse { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            var area = new Rect(RenderSize);
            if (area.IsEmpty || Dim <= 0.001)
                return;

            var hole = Hole;
            bool hasHole = hole.Width > 1 && hole.Height > 1;
            double radius = Math.Max(0, Math.Min(Radius, Math.Min(hole.Width, hole.Height) / 2));

            Geometry shade = new RectangleGeometry(area);
            if (hasHole)
                shade = new CombinedGeometry(GeometryCombineMode.Exclude, shade, new RectangleGeometry(hole, radius, radius));

            dc.PushOpacity(Dim);
            dc.DrawGeometry(Shade, null, shade);
            if (hasHole)
            {
                var accent = Accent;
                dc.DrawRoundedRectangle(null, new Pen(accent, 2), Inflate(hole, 2), radius + 2, radius + 2);

                // The spreading ring starts fast and slows down, like a ripple.
                double spread = 1 - Math.Pow(1 - Pulse, 3);
                double grow = 3 + 14 * spread;
                dc.PushOpacity(0.55 * Math.Pow(1 - Pulse, 1.4));
                dc.DrawRoundedRectangle(null, new Pen(accent, 1.5), Inflate(hole, grow), radius + grow, radius + grow);
                dc.Pop();
            }
            dc.Pop();
        }

        // The whole layer is modal: clicks on the shaded area and inside the hole both stop here.
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }

        private static Rect Inflate(Rect rect, double by)
        {
            rect.Inflate(by, by);
            return rect;
        }

        private static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
