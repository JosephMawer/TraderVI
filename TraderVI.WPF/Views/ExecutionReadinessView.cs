#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Trader.Promotion;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

/// <summary>Receives status only. The timer ages its display and performs no reads or commands.</summary>
public sealed class ExecutionReadinessView : UserControl
{
    private readonly TextBlock title = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
    private readonly TextBlock checkedAt = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), Foreground = Brushes.LightSteelBlue };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private BrokerExecutionHostStatus? status;

    public ExecutionReadinessView()
    {
        var layout = new StackPanel(); layout.Children.Add(title); layout.Children.Add(detail); layout.Children.Add(checkedAt);
        Content = new Border { Child = layout, Padding = new Thickness(12), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.SlateGray,
            Background = new SolidColorBrush(Color.FromRgb(26, 34, 45)) };
        Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 246));
        timer.Tick += (_, _) => Render();
        Loaded += (_, _) => { Render(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
        Render();
    }

    public void SetStatus(BrokerExecutionHostStatus value) { status = value; Render(); }
    private void Render()
    {
        var display = ExecutionReadiness.From(status, DateTimeOffset.UtcNow);
        title.Text = display.Title; detail.Text = display.Detail; checkedAt.Text = display.Checked;
    }
}
