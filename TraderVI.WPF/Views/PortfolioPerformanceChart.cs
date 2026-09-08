#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

/// <summary>Plots only saved, normalized closes. Null observations break a line.</summary>
public sealed class PortfolioPerformanceChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource),
        typeof(IEnumerable<PortfolioComparisonRow>), typeof(PortfolioPerformanceChart), new FrameworkPropertyMetadata(null, Changed));
    public static readonly DependencyProperty SelectedCodeProperty = DependencyProperty.Register(nameof(SelectedCode),
        typeof(string), typeof(PortfolioPerformanceChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<PortfolioComparisonRow>? ItemsSource { get => (IEnumerable<PortfolioComparisonRow>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string? SelectedCode { get => (string?)GetValue(SelectedCodeProperty); set => SetValue(SelectedCodeProperty, value); }
    private static readonly Brush[] Colors = { Brush("#6DB2FF"), Brush("#57D0B3"), Brush("#B59AF4"), Brush("#F1BE69"), Brush("#F091B1"), Brush("#7DC8D8") };
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (PortfolioPerformanceChart)d;
        if (e.OldValue is INotifyCollectionChanged oldRows) CollectionChangedEventManager.RemoveHandler(oldRows, chart.RowsChanged);
        if (e.NewValue is INotifyCollectionChanged newRows) CollectionChangedEventManager.AddHandler(newRows, chart.RowsChanged);
        chart.InvalidateVisual();
    }
    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var rows = (ItemsSource ?? []).Where(r => r.Period.Points.Count(p => p.Index.HasValue) >= 2).ToArray();
        double width = ActualWidth, height = ActualHeight;
        if (width < 180 || height < 100) return;
        var muted = Brush("#A9B9CD");
        if (rows.Length == 0)
        {
            Text(dc, "No matching closing history yet", 20, height / 2 - 30, 17, muted, width - 40);
            Text(dc, "Saved start values and at least two closing dates are needed. Refresh after completed sessions.", 20, height / 2 + 1, 12, muted, width - 40);
            return;
        }
        var points = rows.SelectMany(r => r.Period.Points).ToArray();
        int first = points.Min(p => p.Date.DayNumber), last = points.Max(p => p.Date.DayNumber);
        double low = Math.Min(100, points.Where(p => p.Index.HasValue).Min(p => (double)p.Index!.Value));
        double high = Math.Max(100, points.Where(p => p.Index.HasValue).Max(p => (double)p.Index!.Value));
        double padding = Math.Max(1, (high - low) * .14); low -= padding; high += padding;
        const double left = 52, top = 14;
        double right = width - 18, bottom = height - 76;
        double X(int day) => left + (day - first) / (double)Math.Max(1, last - first) * (right - left);
        double Y(decimal value) => bottom - ((double)value - low) / (high - low) * (bottom - top);
        for (int i = 0; i <= 4; i++)
        {
            double y = top + (bottom - top) * i / 4;
            dc.DrawLine(new Pen(Brush("#2B3748"), 1), new(left, y), new(right, y));
            Text(dc, (high - (high - low) * i / 4).ToString("0.0", CultureInfo.CurrentCulture), 0, y - 8, 11, muted, 47);
        }
        int ticks = Math.Min(3, Math.Max(1, last - first));
        for (int i = 0; i <= ticks; i++)
        {
            int day = first + (last - first) * i / ticks;
            Text(dc, DateOnly.FromDayNumber(day).ToString("dd MMM", CultureInfo.CurrentCulture),
                Math.Clamp(X(day) - 20, left, right - 48), bottom + 10, 11, muted, 52);
        }
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i]; var color = Colors[i % Colors.Length];
            var pen = new Pen(color, row.StableCode == SelectedCode ? 3 : 1.5);
            Point? previous = null;
            foreach (var point in row.Period.Points)
            {
                if (point.Index is not decimal index) { previous = null; continue; }
                Point current = new(X(point.Date.DayNumber), Y(index));
                if (previous.HasValue) dc.DrawLine(pen, previous.Value, current);
                dc.DrawEllipse(color, null, current, 2.5, 2.5);
                previous = current;
            }
        }
        // A compact wrapping legend, with full account names available in the table/cards.
        int columns = Math.Max(1, (int)(width / 260));
        for (int i = 0; i < Math.Min(rows.Length, columns * 2); i++)
        {
            double x = (i % columns) * width / columns, y = height - 39 + i / columns * 19;
            dc.DrawLine(new Pen(Colors[i % Colors.Length], 3), new(x, y + 7), new(x + 14, y + 7));
            Text(dc, rows[i].StrategyName, x + 22, y, 11, muted, width / columns - 30);
        }
    }

    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, double maxWidth)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, maxWidth), Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 2 };
        dc.DrawText(formatted, new(x, y));
    }
}
