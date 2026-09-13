using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MovieExplorer.Models;
using MovieExplorer.ViewModels;

namespace MovieExplorer.Views;

public partial class BoxOfficeAnalyticsView : UserControl
{
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(221, 246, 107));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(137, 146, 163));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(48, 54, 66));
    private static readonly Brush DecreaseBrush = new SolidColorBrush(Color.FromRgb(255, 111, 111));
    private MovieAnalyticsResult? analytics;

    public BoxOfficeAnalyticsView()
    {
        InitializeComponent();
        DataContextChanged += ViewModelChanged;
    }

    private void ViewModelChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BoxOfficeAnalyticsViewModel oldViewModel)
            oldViewModel.AnalyticsChanged -= AnalyticsChanged;
        if (e.NewValue is BoxOfficeAnalyticsViewModel newViewModel)
        {
            newViewModel.AnalyticsChanged += AnalyticsChanged;
            analytics = newViewModel.Analytics;
        }
    }

    private async void AnalyticsChanged(object? sender, EventArgs e)
    {
        analytics = (DataContext as BoxOfficeAnalyticsViewModel)?.Analytics;
        await Dispatcher.BeginInvoke(RenderCharts);
    }

    private void ChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (analytics is not null)
            RenderCharts();
    }

    private void RenderCharts()
    {
        if (analytics is null)
            return;

        DrawAudienceChart(analytics.WeeklyTrend);
        DrawRankChart(analytics.WeeklyTrend);
        DrawCumulativeAudienceChart(analytics.WeeklyTrend);
        DrawAudienceChangeChart(analytics.WeeklyTrend);
    }

    private void DrawAudienceChart(IReadOnlyList<MovieAnalyticsPoint> points)
    {
        AudienceChart.Children.Clear();
        double width = Math.Max(AudienceChart.ActualWidth, 420);
        const double height = 270;
        const double left = 58;
        const double top = 18;
        const double right = 18;
        const double bottom = 40;
        double plotWidth = width - left - right;
        double plotHeight = height - top - bottom;
        long maximum = Math.Max(points.Max(point => point.WeeklyAudience), 1);

        DrawAxes(AudienceChart, left, top, plotWidth, plotHeight);
        AddText(AudienceChart, FormatCompact(maximum), 0, top - 8, MutedBrush, 10);
        AddText(AudienceChart, "0", 38, top + plotHeight - 8, MutedBrush, 10);

        var line = new Polyline { Stroke = AccentBrush, StrokeThickness = 3 };
        for (int index = 0; index < points.Count; index++)
        {
            double x = GetPointX(index, points.Count, left, plotWidth);
            double y = top + plotHeight - plotHeight * points[index].WeeklyAudience / maximum;
            line.Points.Add(new Point(x, y));
            AddMarker(
                AudienceChart,
                x,
                y,
                $"{points[index].WeekEndDate:yyyy.MM.dd}\n" +
                $"주간 관객 {points[index].WeeklyAudience:N0}명\n" +
                $"직전 집계 대비 {points[index].WeeklyChangeLabel}\n" +
                $"누적 관객 {points[index].CumulativeAudience:N0}명\n" +
                $"박스오피스 {points[index].Rank}위");
            AddDateLabel(AudienceChart, points[index].WeekEndDate, index, points.Count, x, top + plotHeight);
        }

        AudienceChart.Children.Insert(0, line);
    }

    private void DrawRankChart(IReadOnlyList<MovieAnalyticsPoint> points)
    {
        RankChart.Children.Clear();
        double width = Math.Max(RankChart.ActualWidth, 420);
        const double height = 270;
        const double left = 48;
        const double top = 18;
        const double right = 18;
        const double bottom = 40;
        double plotWidth = width - left - right;
        double plotHeight = height - top - bottom;

        DrawAxes(RankChart, left, top, plotWidth, plotHeight);
        AddText(RankChart, "1위", 10, top - 8, MutedBrush, 10);
        AddText(RankChart, "10위", 4, top + plotHeight - 8, MutedBrush, 10);

        var line = new Polyline { Stroke = AccentBrush, StrokeThickness = 3 };
        for (int index = 0; index < points.Count; index++)
        {
            double x = GetPointX(index, points.Count, left, plotWidth);
            double y = top + plotHeight * (points[index].Rank - 1) / 9d;
            line.Points.Add(new Point(x, y));
            AddMarker(
                RankChart,
                x,
                y,
                $"{points[index].WeekEndDate:yyyy.MM.dd}\n" +
                $"박스오피스 {points[index].Rank}위\n" +
                $"주간 관객 {points[index].WeeklyAudience:N0}명\n" +
                $"직전 집계 대비 {points[index].WeeklyChangeLabel}\n" +
                $"누적 관객 {points[index].CumulativeAudience:N0}명");
            AddDateLabel(RankChart, points[index].WeekEndDate, index, points.Count, x, top + plotHeight);
        }

        RankChart.Children.Insert(0, line);
    }

    private void DrawCumulativeAudienceChart(IReadOnlyList<MovieAnalyticsPoint> points)
    {
        CumulativeAudienceChart.Children.Clear();
        double width = Math.Max(CumulativeAudienceChart.ActualWidth, 420);
        const double height = 270;
        const double left = 58;
        const double top = 18;
        const double right = 18;
        const double bottom = 40;
        double plotWidth = width - left - right;
        double plotHeight = height - top - bottom;
        long maximum = Math.Max(points.Max(point => point.CumulativeAudience), 1);

        DrawAxes(CumulativeAudienceChart, left, top, plotWidth, plotHeight);
        AddText(CumulativeAudienceChart, FormatCompact(maximum), 0, top - 8, MutedBrush, 10);
        AddText(CumulativeAudienceChart, "0", 38, top + plotHeight - 8, MutedBrush, 10);

        var line = new Polyline { Stroke = AccentBrush, StrokeThickness = 3 };
        for (int index = 0; index < points.Count; index++)
        {
            MovieAnalyticsPoint point = points[index];
            double x = GetPointX(index, points.Count, left, plotWidth);
            double y = top + plotHeight - plotHeight * point.CumulativeAudience / maximum;
            line.Points.Add(new Point(x, y));
            AddMarker(
                CumulativeAudienceChart,
                x,
                y,
                $"{point.WeekEndDate:yyyy.MM.dd}\n" +
                $"누적 관객 {point.CumulativeAudience:N0}명\n" +
                $"주간 관객 {point.WeeklyAudience:N0}명\n" +
                $"박스오피스 {point.Rank}위");
            AddDateLabel(CumulativeAudienceChart, point.WeekEndDate, index, points.Count, x, top + plotHeight);
        }

        CumulativeAudienceChart.Children.Insert(0, line);
    }

    private void DrawAudienceChangeChart(IReadOnlyList<MovieAnalyticsPoint> points)
    {
        AudienceChangeChart.Children.Clear();
        double width = Math.Max(AudienceChangeChart.ActualWidth, 420);
        const double height = 270;
        const double left = 58;
        const double top = 18;
        const double right = 18;
        const double bottom = 40;
        double plotWidth = width - left - right;
        double plotHeight = height - top - bottom;
        List<double> changeRates = points
            .Where(point => point.WeeklyChangeRate.HasValue)
            .Select(point => point.WeeklyChangeRate!.Value)
            .ToList();
        double maximumAbsoluteRate = Math.Max(changeRates.DefaultIfEmpty(0).Max(Math.Abs), 1);
        double zeroY = top + plotHeight / 2;

        AddLine(AudienceChangeChart, left, top, left, top + plotHeight, GridBrush);
        AddLine(AudienceChangeChart, left, zeroY, left + plotWidth, zeroY, GridBrush);
        AddText(AudienceChangeChart, $"+{maximumAbsoluteRate:N0}%", 0, top - 8, MutedBrush, 10);
        AddText(AudienceChangeChart, "0%", 28, zeroY - 8, MutedBrush, 10);
        AddText(AudienceChangeChart, $"-{maximumAbsoluteRate:N0}%", 0, top + plotHeight - 8, MutedBrush, 10);

        double slotWidth = plotWidth / Math.Max(points.Count, 1);
        double barWidth = Math.Clamp(slotWidth * 0.55, 5, 28);
        for (int index = 0; index < points.Count; index++)
        {
            MovieAnalyticsPoint point = points[index];
            double x = GetPointX(index, points.Count, left, plotWidth);
            if (point.WeeklyChangeRate is double changeRate)
            {
                double barHeight = Math.Abs(changeRate) / maximumAbsoluteRate * (plotHeight / 2);
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(barHeight, 2),
                    Fill = changeRate >= 0 ? AccentBrush : DecreaseBrush,
                    ToolTip = new ToolTip
                    {
                        Content = $"{point.WeekEndDate:yyyy.MM.dd}\n" +
                                  $"직전 집계 대비 {point.WeeklyChangeLabel}\n" +
                                  $"주간 관객 {point.WeeklyAudience:N0}명",
                        Padding = new Thickness(10, 7, 10, 7)
                    }
                };
                ToolTipService.SetInitialShowDelay(bar, 0);
                ToolTipService.SetShowDuration(bar, 60_000);
                Canvas.SetLeft(bar, x - barWidth / 2);
                Canvas.SetTop(bar, changeRate >= 0 ? zeroY - Math.Max(barHeight, 2) : zeroY);
                AudienceChangeChart.Children.Add(bar);
            }
            else
            {
                AddMarker(
                    AudienceChangeChart,
                    x,
                    zeroY,
                    $"{point.WeekEndDate:yyyy.MM.dd}\n{point.WeeklyChangeLabel}\n" +
                    $"주간 관객 {point.WeeklyAudience:N0}명");
            }

            AddDateLabel(AudienceChangeChart, point.WeekEndDate, index, points.Count, x, top + plotHeight);
        }
    }

    private static void DrawAxes(Canvas canvas, double left, double top, double width, double height)
    {
        AddLine(canvas, left, top, left, top + height, GridBrush);
        AddLine(canvas, left, top + height, left + width, top + height, GridBrush);
    }

    private static double GetPointX(int index, int count, double left, double width) =>
        count == 1 ? left + width / 2 : left + width * index / (count - 1);

    private static void AddMarker(Canvas canvas, double x, double y, string toolTip)
    {
        var hitArea = new Grid
        {
            Width = 40,
            Height = 40,
            Background = Brushes.Transparent,
            ToolTip = new ToolTip
            {
                Content = toolTip,
                Padding = new Thickness(10, 7, 10, 7)
            }
        };
        ToolTipService.SetInitialShowDelay(hitArea, 0);
        ToolTipService.SetBetweenShowDelay(hitArea, 0);
        ToolTipService.SetShowDuration(hitArea, 60_000);
        Panel.SetZIndex(hitArea, 10);
        hitArea.Children.Add(new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Canvas.SetLeft(hitArea, x - 20);
        Canvas.SetTop(hitArea, y - 20);
        canvas.Children.Add(hitArea);
    }

    private static void AddDateLabel(
        Canvas canvas, DateTime date, int index, int count, double x, double axisBottom)
    {
        int labelStep = Math.Max(1, (int)Math.Ceiling(count / 6d));
        if (index % labelStep == 0 || index == count - 1)
            AddText(canvas, date.ToString("MM.dd"), x - 17, axisBottom + 10, MutedBrush, 10);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush) =>
        canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1 });

    private static void AddText(
        Canvas canvas, string text, double left, double top, Brush brush, double fontSize)
    {
        var label = new TextBlock { Text = text, Foreground = brush, FontSize = fontSize };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
    }

    private static string FormatCompact(long value) => value switch
    {
        >= 100_000_000 => $"{value / 100_000_000d:N1}억",
        >= 10_000 => $"{value / 10_000d:N1}만",
        _ => value.ToString("N0")
    };
}
