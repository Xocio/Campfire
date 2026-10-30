using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Campfire.Localization;

namespace Campfire.Controls
{
    /// <summary>One stop of the tour: what to point at and what to say about it.</summary>
    public sealed class TourStep
    {
        public string TitleKey;
        public string BodyKey;

        /// <summary>The element to point at, or <c>null</c> to show the card alone in the middle.</summary>
        public Func<FrameworkElement> Target;

        public double Padding = 6;

        /// <summary>Round targets, such as the logo and tabs, get a hole as round as they are.</summary>
        public bool Round;

        /// <summary>Runs when the step is about to show, for example to switch to the tab the step is about.</summary>
        public Action Enter;
    }

    /// <summary>
    /// A guided tour over the window. A dimmed layer leaves a hole around the element being introduced; the hole and
    /// the card beside it are moved by springs. Position and size of the hole follow slightly different curves.
    /// </summary>
    public partial class TourOverlay : UserControl
    {
        private const double CardWidth = 360;
        private const double TextWidth = CardWidth - 44;
        private const double Chrome = 18 + 14 + 32 + 16;
        private const double Gap = 16;
        private const double EdgeMargin = 16;

        private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        private static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });
        private static readonly IEasingFunction Overshoot = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
        private static readonly IEasingFunction Bounce = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.9 });

        // The hole: position follows a firmer spring than size.
        private readonly Spring _holeX = new Spring(230, 24);
        private readonly Spring _holeY = new Spring(230, 24);
        private readonly Spring _holeW = new Spring(175, 19);
        private readonly Spring _holeH = new Spring(175, 19);
        private readonly Spring _holeRadius = new Spring(200, 26);

        // The card trails the hole with a softer spring, and grows or shrinks to fit the text.
        private readonly Spring _cardX = new Spring(140, 15);
        private readonly Spring _cardEdge = new Spring(140, 15);
        private readonly Spring _cardText = new Spring(210, 23);

        private readonly Stopwatch _clock = new Stopwatch();
        private readonly TransformGroup _cardTransform;
        private readonly TranslateTransform _arrowShift;
        private readonly StepView _viewA = new StepView();
        private readonly StepView _viewB = new StepView();
        private readonly List<Border> _dots = new List<Border>();

        private IList<TourStep> _steps;
        private Action _finished;
        private StepView _view;
        private int _index;
        private int _session;
        private double _last;
        private double _textHeight;
        private double _appliedTextHeight = -1;
        private bool _below = true;
        private bool _hasTarget;
        private bool _closing;

        public TourOverlay()
        {
            InitializeComponent();
            _cardTransform = (TransformGroup)Card.RenderTransform;
            _arrowShift = (TranslateTransform)((TransformGroup)Arrow.RenderTransform).Children[1];
            Host.Children.Add(_viewA);
            Host.Children.Add(_viewB);
            SizeChanged += (sender, e) =>
            {
                Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 6, 6);
                if (IsActive && !_closing)
                    Retarget(false);
            };
        }

        public bool IsActive { get; private set; }

        /// <summary>Starts the tour. <paramref name="finished"/> runs as soon as it ends, whether it was completed or skipped.</summary>
        public void Start(IList<TourStep> steps, Action finished)
        {
            if (IsActive || steps == null || steps.Count == 0)
                return;

            IsActive = true;
            _closing = false;
            _session++;
            _steps = steps;
            _finished = finished;
            _index = 0;

            Visibility = Visibility.Visible;
            UpdateLayout();
            Text.LanguageChanged += OnLanguageChanged;
            BuildDots();
            LabelButtons();

            _view = _viewA;
            _viewB.Opacity = 0;
            _viewA.Opacity = 1;
            Fill(_view, _steps[0]);
            _textHeight = Measure(_view);
            Enter(_steps[0]);

            double width = ActualWidth, height = ActualHeight;
            SnapHole(new Rect(-60, -60, width + 120, height + 120), 40);
            Retarget(true);
            if (!Motion.Enabled)
                SnapAll();

            Spot.Dim = Motion.Enabled ? 0 : 1;
            if (Motion.Enabled)
            {
                // The shade fades in while the hole closes in on the first target from beyond the window.
                Spot.BeginAnimation(TourSpotlight.DimProperty, new DoubleAnimation(0, 1, Ms(520)) { EasingFunction = EaseOut });
                _view.PlayIn(1, TimeSpan.FromMilliseconds(380));
                Card.BeginAnimation(OpacityProperty, Hold(0, 1, TimeSpan.FromMilliseconds(330), 260, EaseOut));
                _cardTransform.Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, Hold(0.86, 1, TimeSpan.FromMilliseconds(330), 560, Bounce));
                _cardTransform.Children[0].BeginAnimation(ScaleTransform.ScaleYProperty, Hold(0.86, 1, TimeSpan.FromMilliseconds(330), 560, Bounce));
            }
            else
            {
                Card.Opacity = 1;
            }

            _clock.Restart();
            _last = 0;
            CompositionTarget.Rendering += OnFrame;
            Apply();
            Focus();
            Keyboard.Focus(this);
            ScheduleRetarget();
        }

        // ======================== Steps ========================

        private void GoTo(int index, int direction)
        {
            if (_closing || index < 0 || index >= _steps.Count)
                return;

            var previous = _view;
            _index = index;
            var step = _steps[index];
            Enter(step);

            _view = ReferenceEquals(previous, _viewA) ? _viewB : _viewA;
            Panel.SetZIndex(_view, 1);
            Panel.SetZIndex(previous, 0);
            Fill(_view, step);
            _textHeight = Measure(_view);
            LabelButtons();
            UpdateDots();
            Retarget(false);

            if (Motion.Enabled)
            {
                previous.PlayOut(direction);
                _view.PlayIn(direction, TimeSpan.FromMilliseconds(140));
            }
            else
            {
                previous.Opacity = 0;
                _view.Reset();
            }
            ScheduleRetarget();
        }

        private void Enter(TourStep step)
        {
            step.Enter?.Invoke();
        }

        private void Next()
        {
            if (_index >= _steps.Count - 1)
                Finish();
            else
                GoTo(_index + 1, 1);
        }

        private void Previous()
        {
            if (_index > 0)
                GoTo(_index - 1, -1);
        }

        /// <summary>Ends the tour: the hole opens up past the window edges, the shade lifts and the card fades.</summary>
        private void Finish()
        {
            if (_closing || !IsActive)
                return;
            _closing = true;
            _finished?.Invoke();

            if (!Motion.Enabled)
            {
                Close();
                return;
            }

            _holeX.Target = -80;
            _holeY.Target = -80;
            _holeW.Target = ActualWidth + 160;
            _holeH.Target = ActualHeight + 160;
            _holeRadius.Target = 44;

            Spot.BeginAnimation(TourSpotlight.DimProperty, new DoubleAnimation(0, Ms(460)) { EasingFunction = EaseIn });
            Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(180)) { EasingFunction = EaseIn });
            var shrink = new DoubleAnimation(0.92, Ms(220)) { EasingFunction = EaseIn };
            _cardTransform.Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            _cardTransform.Children[0].BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

            int session = _session;
            After(500, () =>
            {
                if (session == _session)
                    Close();
            });
        }

        private void Close()
        {
            CompositionTarget.Rendering -= OnFrame;
            Text.LanguageChanged -= OnLanguageChanged;
            Spot.BeginAnimation(TourSpotlight.DimProperty, null);
            Card.BeginAnimation(OpacityProperty, null);
            foreach (Transform transform in _cardTransform.Children)
            {
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            Card.Opacity = 0;
            Spot.Dim = 0;
            Visibility = Visibility.Collapsed;
            IsActive = false;
            _closing = false;
            _appliedTextHeight = -1;
        }

        // ======================== Placement ========================

        /// <summary>Works out where the hole and the card belong for the current step and sets the springs towards them.</summary>
        private void Retarget(bool snapCard)
        {
            var step = _steps[_index];
            double areaWidth = ActualWidth, areaHeight = ActualHeight;
            _hasTarget = TryBounds(step, out Rect hole);
            if (!_hasTarget)
                hole = new Rect(areaWidth / 2, areaHeight / 2, 0, 0);

            _holeX.Target = hole.X;
            _holeY.Target = hole.Y;
            _holeW.Target = hole.Width;
            _holeH.Target = hole.Height;
            _holeRadius.Target = !_hasTarget ? 0 : step.Round ? Math.Min(hole.Width, hole.Height) / 2 : 10;

            double cardHeight = _textHeight + Chrome;
            double left = hole.X + hole.Width / 2 - CardWidth / 2;
            double edge;
            bool below = true;
            if (!_hasTarget)
            {
                left = (areaWidth - CardWidth) / 2;
                edge = (areaHeight - cardHeight) / 2;
            }
            else
            {
                double roomBelow = areaHeight - hole.Bottom - Gap - EdgeMargin;
                double roomAbove = hole.Top - Gap - EdgeMargin;
                below = roomBelow >= cardHeight || (roomAbove < cardHeight && roomBelow >= roomAbove);
                edge = below
                    ? Clamp(hole.Bottom + Gap, EdgeMargin, areaHeight - EdgeMargin - cardHeight)
                    : Clamp(hole.Top - Gap, EdgeMargin + cardHeight, areaHeight - EdgeMargin);
            }
            left = Clamp(left, EdgeMargin, areaWidth - EdgeMargin - CardWidth);

            if (below != _below && !snapCard)
            {
                // The spring follows the edge nearest to the hole. Swapping sides keeps the card where it is
                // for now by switching to the other edge of the same card, and the arrow reappears on the new side.
                double current = _cardText.Value + Chrome;
                _cardEdge.Value += below ? -current : current;
                FlipArrow(below);
            }
            else if (snapCard)
            {
                FlipArrow(below);
            }
            _below = below;

            _cardX.Target = left;
            _cardEdge.Target = edge;
            _cardText.Target = _textHeight;
            if (snapCard)
            {
                _cardX.Snap(left);
                _cardEdge.Snap(edge);
                _cardText.Snap(_textHeight);
            }
            if (!Motion.Enabled)
                SnapAll();
        }

        private bool TryBounds(TourStep step, out Rect bounds)
        {
            bounds = Rect.Empty;
            var element = step.Target?.Invoke();
            if (element == null || !element.IsVisible)
                return false;
            try
            {
                bounds = element.TransformToVisual(Root).TransformBounds(new Rect(element.RenderSize));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            bounds.Inflate(step.Padding, step.Padding);
            return true;
        }

        private void FlipArrow(bool below)
        {
            Arrow.VerticalAlignment = below ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            Arrow.Margin = below ? new Thickness(0, -6, 0, 0) : new Thickness(0, 0, 0, -6);
            if (Motion.Enabled)
                Arrow.BeginAnimation(OpacityProperty, Hold(0, 1, TimeSpan.FromMilliseconds(180), 260, EaseOut));
        }

        /// <summary>Pages that were just switched are still animating; look again once they have settled.</summary>
        private void ScheduleRetarget()
        {
            int session = _session;
            int index = _index;
            After(560, () =>
            {
                if (session == _session && index == _index && !_closing && IsActive)
                    Retarget(false);
            });
        }

        // ======================== Frames ========================

        private void OnFrame(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - _last, 0.033);
            _last = now;
            if (dt <= 0.0005)
                return;

            _holeX.Step(dt);
            _holeY.Step(dt);
            _holeW.Step(dt);
            _holeH.Step(dt);
            _holeRadius.Step(dt);
            _cardX.Step(dt);
            _cardEdge.Step(dt);
            _cardText.Step(dt);
            Apply();
        }

        private void Apply()
        {
            Spot.Hole = new Rect(_holeX.Value, _holeY.Value, Math.Max(0, _holeW.Value), Math.Max(0, _holeH.Value));
            Spot.Radius = Math.Max(0, _holeRadius.Value);
            // A ripple leaves the hole, then it rests for a moment before the next one.
            Spot.Pulse = Math.Min(1, (_clock.Elapsed.TotalSeconds % 2.4) / 1.6);
            Spot.InvalidateVisual();

            double text = Math.Max(0, _cardText.Value);
            if (Math.Abs(text - _appliedTextHeight) > 0.05)
            {
                _appliedTextHeight = text;
                Host.Height = text;
            }

            double cardHeight = text + Chrome;
            var shift = (TranslateTransform)_cardTransform.Children[1];
            shift.X = _cardX.Value;
            shift.Y = _below ? _cardEdge.Value : _cardEdge.Value - cardHeight;

            if (_hasTarget)
            {
                double center = _holeX.Value + _holeW.Value / 2 - _cardX.Value;
                _arrowShift.X = Clamp(center, 26, CardWidth - 26) - 6;
                Arrow.Visibility = Visibility.Visible;
            }
            else
            {
                Arrow.Visibility = Visibility.Collapsed;
            }
        }

        private void SnapHole(Rect rect, double radius)
        {
            _holeX.Snap(rect.X);
            _holeY.Snap(rect.Y);
            _holeW.Snap(rect.Width);
            _holeH.Snap(rect.Height);
            _holeRadius.Snap(radius);
        }

        private void SnapAll()
        {
            foreach (var spring in new[] { _holeX, _holeY, _holeW, _holeH, _holeRadius, _cardX, _cardEdge, _cardText })
                spring.Snap(spring.Target);
            if (IsActive)
                Apply();
        }

        // ======================== Card ========================

        private void Fill(StepView view, TourStep step)
        {
            view.Title.Text = Text.Get(step.TitleKey);
            view.Body.Text = Text.Get(step.BodyKey);
        }

        private static double Measure(StepView view)
        {
            view.Measure(new Size(TextWidth, double.PositiveInfinity));
            return Math.Ceiling(view.DesiredSize.Height);
        }

        private void LabelButtons()
        {
            bool last = _index >= _steps.Count - 1;
            BtnSkip.Content = Text.Get("Tour.Skip");
            BtnPrev.Content = Text.Get("Tour.Prev");
            BtnNext.Content = Text.Get(last ? "Tour.Done" : "Tour.Next");
            BtnSkip.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
            Motion.Show(BtnPrev, _index > 0);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (!IsActive || _closing)
                return;
            Fill(_view, _steps[_index]);
            _textHeight = Measure(_view);
            LabelButtons();
            Retarget(false);
        }

        private void BuildDots()
        {
            Dots.Children.Clear();
            _dots.Clear();
            for (int i = 0; i < _steps.Count; i++)
            {
                var dot = new Border { Width = i == 0 ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3), Opacity = i == 0 ? 1 : 0.3 };
                dot.Margin = new Thickness(0, 0, 5, 0);
                dot.SetResourceReference(Border.BackgroundProperty, "Brush3");
                _dots.Add(dot);
                Dots.Children.Add(dot);
            }
        }

        /// <summary>The current dot stretches into a pill with a small bounce while the one it replaces shrinks back.</summary>
        private void UpdateDots()
        {
            for (int i = 0; i < _dots.Count; i++)
            {
                bool active = i == _index;
                double width = active ? 18 : 6, opacity = active ? 1 : 0.3;
                if (!Motion.Enabled)
                {
                    _dots[i].Width = width;
                    _dots[i].Opacity = opacity;
                    continue;
                }
                _dots[i].BeginAnimation(WidthProperty, new DoubleAnimation(width, Ms(active ? 420 : 240)) { EasingFunction = active ? Bounce : EaseOut });
                _dots[i].BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, Ms(240)) { EasingFunction = EaseOut });
            }
        }

        // ======================== Input ========================

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            Next();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            Previous();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            Finish();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Finish();
                    break;
                case Key.Right:
                case Key.Enter:
                case Key.Space:
                    Next();
                    break;
                case Key.Left:
                    Previous();
                    break;
                default:
                    base.OnKeyDown(e);
                    return;
            }
            e.Handled = true;
        }

        // ======================== Helpers ========================

        private static AnimationTimeline Hold(double from, double to, TimeSpan delay, int milliseconds, IEasingFunction easing)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(milliseconds)), easing));
            return animation;
        }

        private static void After(int milliseconds, Action action)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (sender, e) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }

        private static double Clamp(double value, double min, double max)
        {
            return max < min ? min : Math.Max(min, Math.Min(max, value));
        }

        private static Duration Ms(int milliseconds)
        {
            return new Duration(TimeSpan.FromMilliseconds(milliseconds));
        }

        private static IEasingFunction Frozen(EasingFunctionBase easing)
        {
            easing.Freeze();
            return easing;
        }

        /// <summary>The words of one step. Two of them alternate.</summary>
        private sealed class StepView : StackPanel
        {
            public readonly TextBlock Title = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 36, 0),
                RenderTransform = new TranslateTransform(),
            };

            public readonly TextBlock Body = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                RenderTransform = new TranslateTransform(),
            };

            public StepView()
            {
                VerticalAlignment = VerticalAlignment.Top;
                Title.SetResourceReference(TextBlock.ForegroundProperty, "Brush3");
                Body.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray1");
                Children.Add(Title);
                Children.Add(Body);
            }

            /// <summary>The title and the text slide in from the side one after the other, landing with a small bounce.</summary>
            public void PlayIn(int direction, TimeSpan delay)
            {
                Opacity = 1;
                Enter(Title, (TranslateTransform)Title.RenderTransform, direction, delay);
                Enter(Body, (TranslateTransform)Body.RenderTransform, direction, delay + TimeSpan.FromMilliseconds(70));
            }

            public void PlayOut(int direction)
            {
                Leave(Title, (TranslateTransform)Title.RenderTransform, direction, TimeSpan.Zero);
                Leave(Body, (TranslateTransform)Body.RenderTransform, direction, TimeSpan.FromMilliseconds(30));
            }

            public void Reset()
            {
                Opacity = 1;
                foreach (var block in new[] { Title, Body })
                {
                    block.BeginAnimation(OpacityProperty, null);
                    block.Opacity = 1;
                    var shift = (TranslateTransform)block.RenderTransform;
                    shift.BeginAnimation(TranslateTransform.XProperty, null);
                    shift.X = 0;
                }
            }

            private static void Enter(UIElement block, TranslateTransform shift, int direction, TimeSpan delay)
            {
                block.BeginAnimation(OpacityProperty, Hold(0, 1, delay, 240, EaseOut));
                shift.BeginAnimation(TranslateTransform.XProperty, Hold(26 * direction, 0, delay, 520, Overshoot));
            }

            private static void Leave(UIElement block, TranslateTransform shift, int direction, TimeSpan delay)
            {
                block.BeginAnimation(OpacityProperty, Hold(1, 0, delay, 130, EaseIn));
                shift.BeginAnimation(TranslateTransform.XProperty, Hold(0, -16 * direction, delay, 150, EaseIn));
            }
        }
    }
}
