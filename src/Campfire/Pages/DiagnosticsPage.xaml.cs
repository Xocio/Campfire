using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Campfire.Controls;
using Campfire.Link;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// Runs the multiplayer checks and lists what each found. It starts when the page opens and stops when it closes.
    /// </summary>
    public partial class DiagnosticsPage : UserControl
    {
        private sealed class Row
        {
            public DiagCheck Check;
            public ContentControl Icon;
            public TextBlock Title;
            public TextBlock Detail;
            public DiagState State;
        }

        private readonly List<Row> _rows = new List<Row>();
        private CancellationTokenSource _run;

        public DiagnosticsPage()
        {
            InitializeComponent();
            foreach (var check in Diagnostics.Checks)
                _rows.Add(CreateRow(check));

            Text.LanguageChanged += (sender, e) => RenderTitles();
            IsVisibleChanged += (sender, e) =>
            {
                // The window draws every page once while idle, with hit testing off; that is not a visit.
                if (IsVisible && IsHitTestVisible)
                    Start();
                else if (!IsVisible)
                    Stop();
            };
            RenderTitles();
        }

        private Row CreateRow(DiagCheck check)
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new ContentControl { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), Focusable = false };
            var title = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            var detail = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray4");
            Grid.SetColumn(title, 1);
            Grid.SetColumn(detail, 2);
            grid.Children.Add(icon);
            grid.Children.Add(title);
            grid.Children.Add(detail);
            PanChecks.Children.Add(grid);

            var row = new Row { Check = check, Icon = icon, Title = title, Detail = detail };
            SetState(row, DiagState.Pending, "");
            return row;
        }

        private void RenderTitles()
        {
            foreach (var row in _rows)
                row.Title.Text = Text.Get("Diag." + row.Check.Key);
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            Start();
        }

        private void Stop()
        {
            _run?.Cancel();
            _run = null;
        }

        private async void Start()
        {
            Stop();
            var run = new CancellationTokenSource();
            _run = run;
            BtnRun.IsEnabled = false;
            HintResult.Visibility = Visibility.Collapsed;
            foreach (var row in _rows)
                SetState(row, DiagState.Pending, "");

            try
            {
                foreach (var row in _rows)
                {
                    SetState(row, DiagState.Running, "");
                    DiagResult result;
                    try
                    {
                        result = await row.Check.Run(run.Token);
                    }
                    catch (OperationCanceledException) when (run.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        LinkLog.Warn($"Diagnostic check {row.Check.Key} failed: {ex.Message}");
                        result = new DiagResult(DiagState.Info, Text.Get("Diag.Unknown"));
                    }
                    if (run != _run)
                        return;
                    SetState(row, result.State, result.Detail);
                }
                ShowSummary();
            }
            finally
            {
                if (run == _run)
                    BtnRun.IsEnabled = true;
            }
        }

        private void ShowSummary()
        {
            int fails = _rows.Count(r => r.State == DiagState.Fail);
            int warns = _rows.Count(r => r.State == DiagState.Warn);
            LabResult.Text = fails > 0 ? Text.Format("Diag.Result.Fail", fails)
                : warns > 0 ? Text.Format("Diag.Result.Warn", warns)
                : Text.Get("Diag.Result.Ok");
            HintResult.Style = (Style)FindResource(fails > 0 || warns > 0 ? "PclHintOrange" : "PclHintGreen");
            Motion.Show(HintResult, true);
        }

        private void SetState(Row row, DiagState state, string detail)
        {
            row.State = state;
            row.Detail.Text = detail;
            row.Detail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            row.Title.SetResourceReference(TextBlock.ForegroundProperty, state == DiagState.Pending ? "BrushGray4" : "Brush1");
            row.Icon.Content = CreateIcon(state);
        }

        private static UIElement CreateIcon(DiagState state)
        {
            switch (state)
            {
                case DiagState.Running:
                    var spinner = new Control();
                    spinner.SetResourceReference(StyleProperty, "PclSpinner");
                    return spinner;
                case DiagState.Ok:
                    var check = new Border { CornerRadius = new CornerRadius(8) };
                    check.SetResourceReference(Border.BackgroundProperty, "BrushGreen");
                    var mark = new Path
                    {
                        Data = Geometry.Parse("M4.6,8.2 L7,10.4 L11.4,5.8"),
                        StrokeThickness = 1.6,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Stretch = Stretch.None,
                        Width = 16,
                        Height = 16,
                    };
                    mark.SetResourceReference(Shape.StrokeProperty, "BrushOnAccent");
                    check.Child = mark;
                    return check;
                default:
                    var dot = new Ellipse { Width = 7, Height = 7 };
                    dot.SetResourceReference(Shape.FillProperty,
                        state == DiagState.Fail ? "BrushLampRed" : state == DiagState.Warn ? "BrushLampYellow"
                        : state == DiagState.Info ? "BrushGray4" : "BrushGray7");
                    return dot;
            }
        }
    }
}
