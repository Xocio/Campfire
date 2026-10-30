using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Campfire.Controls
{
    /// <summary>
    /// Shared animations. A page arrives card by card: each card drops in from slightly above and settles with a
    /// small bounce, a moment after the one before. Leaving, the cards sink a little and fade. Every animation can
    /// be turned off in the settings; the end state is then applied at once.
    /// </summary>
    internal static class Motion
    {
        private static readonly IEasingFunction Fluent =Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        private static readonly IEasingFunction FluentIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });
        private static readonly IEasingFunction Soft = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseOut });
        private static readonly IEasingFunction Settle = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
        private static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });
        private static readonly ConditionalWeakTable<UIElement, Generation> Generations = new ConditionalWeakTable<UIElement, Generation>();
        private static bool? _enabled;

        public static bool Enabled
        {
            get => _enabled ?? (_enabled = AppSettings.Get("ui.motion") != "0").Value;
            set
            {
                _enabled = value;
                AppSettings.Set("ui.motion", value ? null : "0");
            }
        }

        // ======================== Pages ========================

        /// <summary>
        /// Shows a page. Its cards drop in one after another; a page without cards drops in as a whole.
        /// <paramref name="direction"/> is kept for elements without cards, which slide in from that side.
        /// </summary>
        public static void EnterPage(FrameworkElement page, int direction, bool animate = true)
        {
            Next(page);
            page.IsHitTestVisible = true;
            page.Visibility = Visibility.Visible;
            page.BeginAnimation(UIElement.OpacityProperty, null);
            page.Opacity = 1;
            animate &= Enabled;
            (page as IPageMotion)?.Entering(animate);
            if (!animate)
            {
                foreach (var item in Items(page) ?? new List<FrameworkElement> { page })
                    Reset(item);
                return;
            }

            var items = Items(page);
            if (items == null)
            {
                // Small things such as a title: a short slide from the side they come from.
                var shift = Translate(page);
                page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(160)) { EasingFunction = Soft });
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18 * direction, 0, Ms(380)) { EasingFunction = Settle });
                return;
            }

            // The cards stay hidden for two frames, then fall in.
            int generation = Current(page);
            foreach (var item in items)
            {
                item.BeginAnimation(UIElement.OpacityProperty, null);
                item.Opacity = 0;
            }
            AfterFrames(2, () =>
            {
                if (Current(page) != generation)
                    return;
                for (int i = 0; i < items.Count; i++)
                    Drop(items[i], TimeSpan.FromMilliseconds(i * 25));
            });
        }

        /// <summary>The cards of a page sink a little and fade, one shortly after another; then the page hides.</summary>
        public static void LeavePage(FrameworkElement page, int direction, bool animate = true)
        {
            int generation = Next(page);
            if (!animate || !Enabled || page.Visibility != Visibility.Visible)
            {
                page.Visibility = Visibility.Hidden;
                return;
            }
            (page as IPageMotion)?.Leaving();

            page.IsHitTestVisible = false;
            var items = Items(page);
            TimeSpan total;
            if (items == null)
            {
                var shift = Translate(page);
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-10 * direction, Ms(90)) { EasingFunction = FluentIn });
                total = TimeSpan.FromMilliseconds(90);
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var delay = TimeSpan.FromMilliseconds(i * 15);
                    items[i].BeginAnimation(UIElement.OpacityProperty, Hold(1, 0, delay, 70, FluentIn));
                    Translate(items[i]).BeginAnimation(TranslateTransform.YProperty, Hold(0, 6, delay, 70, FluentIn));
                }
                total = TimeSpan.FromMilliseconds((items.Count - 1) * 15 + 70);
            }

            // The page itself only hides once its last card is gone.
            After(total, () =>
            {
                if (Current(page) != generation)
                    return;
                page.Visibility = Visibility.Hidden;
                page.IsHitTestVisible = true;
                page.BeginAnimation(UIElement.OpacityProperty, null);
                page.Opacity = 1;
                foreach (var item in items ?? new List<FrameworkElement> { page })
                    Reset(item);
            });
        }

        /// <summary>
        /// Switches between two parts of a page that share one place. Without <paramref name="animate"/> the switch
        /// happens at once, for a page that is out of sight.
        /// </summary>
        public static void Swap(FrameworkElement from, FrameworkElement to, int direction = 1, bool animate = true)
        {
            if (from == to)
                return;
            if (from != null)
                LeavePage(from, direction, animate);
            if (to != null)
                EnterPage(to, direction, animate);
        }

        /// <summary>The items of a side navigation slide in from the left, one after another.</summary>
        public static void EnterNavigation(Panel navigation, bool animate = true)
        {
            int generation = Next(navigation);
            var items = navigation.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToList();
            if (!animate || !Enabled)
            {
                items.ForEach(Reset);
                return;
            }

            // Like cards, the items wait out the slow first frame of the page before they move.
            foreach (var item in items)
            {
                item.BeginAnimation(UIElement.OpacityProperty, null);
                item.Opacity = 0;
            }
            AfterFrames(2, () =>
            {
                if (Current(navigation) != generation)
                    return;
                for (int i = 0; i < items.Count; i++)
                {
                    var delay = TimeSpan.FromMilliseconds(i * 30);
                    items[i].BeginAnimation(UIElement.OpacityProperty, Hold(0, 1, delay, 100, Soft));
                    // A short glide and a longer slide that lands with a bounce, played together.
                    var (glide, land) = Layers(items[i]);
                    glide.BeginAnimation(TranslateTransform.XProperty, Hold(-5, 0, delay, 200, Fluent));
                    land.BeginAnimation(TranslateTransform.XProperty, Hold(-25, 0, delay, 300, Settle));
                }
            });
        }

        /// <summary>The items of a side navigation fade while sliding back to the left, all together.</summary>
        public static void LeaveNavigation(Panel navigation)
        {
            Next(navigation);
            if (!Enabled)
                return;
            foreach (var item in navigation.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible))
            {
                item.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, Ms(70)) { EasingFunction = FluentIn });
                var (glide, land) = Layers(item);
                glide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-8, Ms(70)) { EasingFunction = FluentIn });
                land.BeginAnimation(TranslateTransform.XProperty, null);
                land.X = 0;
            }
        }

        /// <summary>
        /// The background of a side bar grows out of the left edge. Leaving, it folds back into the edge.
        /// </summary>
        public static void EnterSideBar(FrameworkElement bar, bool animate = true)
        {
            int generation = Next(bar);
            var scale = SideScale(bar);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            if (!animate || !Enabled)
            {
                scale.ScaleX = 1;
                return;
            }

            scale.ScaleX = 0;
            AfterFrames(2, () =>
            {
                if (Current(bar) == generation)
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, Ms(260)) { EasingFunction = Fluent });
            });
        }

        /// <inheritdoc cref="EnterSideBar"/>
        public static void LeaveSideBar(FrameworkElement bar)
        {
            Next(bar);
            if (Enabled)
                SideScale(bar).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, Ms(70)) { EasingFunction = FluentIn });
        }

        private static ScaleTransform SideScale(FrameworkElement bar)
        {
            if (bar.RenderTransform is ScaleTransform scale && !scale.IsFrozen)
                return scale;
            bar.RenderTransformOrigin = new Point(0, 0.5);
            scale = new ScaleTransform();
            bar.RenderTransform = scale;
            return scale;
        }

        // ======================== Elements ========================

        /// <summary>Makes a collapsed element visible; it drops in like a card.</summary>
        public static void Reveal(FrameworkElement element)
        {
            if (element.Visibility == Visibility.Visible)
                return;
            element.Visibility = Visibility.Visible;
            if (Enabled)
                Drop(element, TimeSpan.Zero);
        }

        /// <summary>Shows an element with <see cref="Reveal"/>, or collapses it at once.</summary>
        public static void Show(FrameworkElement element, bool shown)
        {
            if (shown)
                Reveal(element);
            else
                element.Visibility = Visibility.Collapsed;
        }

        /// <summary>Moves a progress bar smoothly to a new value.</summary>
        public static void Progress(RangeBase bar, double value)
        {
            if (!Enabled || value < bar.Value)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Value = value;
                return;
            }
            bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(value, Ms(300)) { EasingFunction = Soft });
        }

        /// <summary>Plays a small bounce, for example when something is selected or completed.</summary>
        public static void Pop(FrameworkElement element, double from = 0.6)
        {
            if (!Enabled)
                return;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            if (!(element.RenderTransform is ScaleTransform scale) || scale.IsFrozen)
            {
                scale = new ScaleTransform();
                element.RenderTransform = scale;
            }
            var grow = new DoubleAnimation(from, 1, Ms(360)) { EasingFunction = Spring };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        // ======================== Card entrance ========================

        /// <summary>
        /// Drops one element in after <paramref name="delay"/>: it starts 16 pixels high and transparent, fades in
        /// quickly and comes down with a soft landing that overshoots a little.
        /// </summary>
        private static void Drop(FrameworkElement item, TimeSpan delay)
        {
            item.BeginAnimation(UIElement.OpacityProperty, Hold(0, 1, delay, 100, Soft));
            if (!CanTranslate(item))
                return;
            // Two movements run at the same time and add up: a short glide and a longer one that lands with a
            // small bounce. Together they give one continuous fall that never stops halfway.
            var (glide, land) = Layers(item);
            glide.BeginAnimation(TranslateTransform.YProperty, Hold(-5, 0, delay, 250, Fluent));
            land.BeginAnimation(TranslateTransform.YProperty, Hold(-11, 0, delay, 350, Settle));
        }

        /// <summary>Holds <paramref name="from"/> during <paramref name="delay"/>, then eases to <paramref name="to"/>.</summary>
        private static AnimationTimeline Hold(double from, double to, TimeSpan delay, int milliseconds, IEasingFunction easing)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(milliseconds)), easing));
            return animation;
        }

        /// <summary>
        /// The cards of a page: the visible siblings of its first card, followed by whatever sits next to that
        /// column (such as a row of buttons under it). <c>null</c> when the page has no card.
        /// </summary>
        private static List<FrameworkElement> Items(FrameworkElement page)
        {
            var card = FindFirstCard(page);
            if (card == null || !(VisualTreeHelper.GetParent(card) is Panel column))
                return null;
            var items = column.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToList();
            if (VisualTreeHelper.GetParent(column) is Panel outer)
            {
                items.AddRange(outer.Children.OfType<FrameworkElement>()
                    .Where(c => c != column && c.Visibility == Visibility.Visible));
            }
            return items;
        }

        private static HeaderedContentControl FindFirstCard(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is UIElement element && element.Visibility != Visibility.Visible)
                    continue;
                if (child is HeaderedContentControl card)
                    return card;
                var found = FindFirstCard(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        // ======================== Timing ========================

        /// <summary>Runs <paramref name="action"/> once <paramref name="frames"/> more frames have been drawn.</summary>
        private static void AfterFrames(int frames, Action action)
        {
            int seen = 0;
            EventHandler handler = null;
            handler = (sender, e) =>
            {
                if (++seen < frames)
                    return;
                CompositionTarget.Rendering -= handler;
                action();
            };
            CompositionTarget.Rendering += handler;
        }

        private static void After(TimeSpan delay, Action action)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
            timer.Tick += (sender, e) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }

        // ======================== Helpers ========================

        /// <summary>Elements that use their render transform for something else are only faded.</summary>
        private static bool CanTranslate(UIElement element)
        {
            return element.RenderTransform == null || element.RenderTransform == Transform.Identity
                   || element.RenderTransform is TranslateTransform || IsLayerGroup(element.RenderTransform);
        }

        private static bool IsLayerGroup(Transform transform)
        {
            return transform is TransformGroup group && !group.IsFrozen && group.Children.Count == 2
                   && group.Children[0] is TranslateTransform && group.Children[1] is TranslateTransform;
        }

        /// <summary>The element's two movement layers; their offsets add up.</summary>
        private static (TranslateTransform First, TranslateTransform Second) Layers(UIElement element)
        {
            if (IsLayerGroup(element.RenderTransform))
            {
                var children = ((TransformGroup)element.RenderTransform).Children;
                return ((TranslateTransform)children[0], (TranslateTransform)children[1]);
            }
            var first = new TranslateTransform();
            var second = new TranslateTransform();
            if (CanTranslate(element))
                element.RenderTransform = new TransformGroup { Children = { first, second } };
            return (first, second);
        }

        private static TranslateTransform Translate(UIElement element)
        {
            return Layers(element).First;
        }

        private static void Reset(UIElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            if (!IsLayerGroup(element.RenderTransform))
                return;
            foreach (TranslateTransform shift in ((TransformGroup)element.RenderTransform).Children)
            {
                shift.BeginAnimation(TranslateTransform.XProperty, null);
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.X = 0;
                shift.Y = 0;
            }
        }

        private static int Next(UIElement element)
        {
            return ++Generations.GetOrCreateValue(element).Value;
        }

        private static int Current(UIElement element)
        {
            return Generations.GetOrCreateValue(element).Value;
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

        private sealed class Generation
        {
            public int Value;
        }
    }

    /// <summary>A page with parts outside its cards, such as a side bar, that move along when it enters or leaves.</summary>
    internal interface IPageMotion
    {
        /// <summary>The page is being shown; <paramref name="animate"/> is false when it appears at once.</summary>
        void Entering(bool animate);

        /// <summary>The page starts leaving; it hides once its cards are gone.</summary>
        void Leaving();
    }
}
