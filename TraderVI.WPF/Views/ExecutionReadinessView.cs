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
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool),
        typeof(ExecutionReadinessView), new PropertyMetadata(false, (d, _) => ((ExecutionReadinessView)d).Render()));
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(string),
        typeof(ExecutionReadinessView), new PropertyMetadata("", (d, _) => ((ExecutionReadinessView)d).Render()));
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    private readonly TextBlock heading = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(155, 168, 186)), Margin = new(0, 1, 10, 0) };
    private readonly TextBlock title = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
    private readonly TextBlock checkedAt = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), Foreground = Brushes.LightSteelBlue };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private BrokerExecutionHostStatus? status;

    public ExecutionReadinessView()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(heading); Grid.SetColumn(title, 1); header.Children.Add(title);
        var layout = new StackPanel(); layout.Children.Add(header); layout.Children.Add(detail); layout.Children.Add(checkedAt);
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
        heading.Text = Heading;
        heading.Visibility = Compact && !string.IsNullOrEmpty(Heading) ? Visibility.Visible : Visibility.Collapsed;
        title.Foreground = (Brush)new BrushConverter().ConvertFromString(display.Color)!;
        title.FontSize = Compact ? 14 : 17;
        detail.Visibility = Compact && display.Title is not ("Blocked" or "Unable to verify" or "Waiting for checks")
            ? Visibility.Collapsed : Visibility.Visible;
        ToolTip = Compact ? display.Detail : null;
        if (Content is Border border)
        {
            border.Padding = Compact ? new Thickness(0) : new Thickness(12);
            border.BorderThickness = Compact ? new Thickness(0) : new Thickness(1);
            border.Background = Compact ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(26, 34, 45));
        }
    }
}
