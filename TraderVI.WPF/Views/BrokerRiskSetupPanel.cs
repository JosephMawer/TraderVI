#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

/// <summary>Presentation only. Opening this panel performs no account read or reference save.</summary>
public sealed class BrokerRiskSetupPanel : UserControl
{
    public TextBlock Summary { get; } = new()
    {
        Text = "Click Refresh to read the current account and existing risk references. No initial references have been saved by opening this window.",
        TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new Thickness(0, 16, 0, 16)
    };
    public Button Refresh { get; } = new() { Content = "Refresh verified account and references" };
    public Button Save { get; } = new() { Content = "Save these reviewed initial references", IsEnabled = false };

    public BrokerRiskSetupPanel()
    {
        Background = new SolidColorBrush(Color.FromRgb(17, 22, 29));
        Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 246));
        FontSize = 14;
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "Establish continuous account risk tracking", FontSize = 24, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Setup preserves known references, holds and position protection. A new reference starts at the verified observation; it does not claim a historical opening or lifetime high. Strategy execution needs its separate review.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Summary);
        foreach (var button in new[] { Refresh, Save })
        {
            button.Padding = new Thickness(12, 9, 12, 9);
            button.Margin = new Thickness(0, 0, 0, 8);
            button.MinHeight = 42;
            if (TryFindResource("WorkspaceButton") is Style style) button.Style = style;
            body.Children.Add(button);
        }
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
