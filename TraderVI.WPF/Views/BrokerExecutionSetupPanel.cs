#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

/// <summary>Presentation only. Construction performs no account, journal, SQL or qualification read.</summary>
public sealed class BrokerExecutionSetupPanel : UserControl
{
    public TextBlock Summary { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    public Button Refresh { get; } = new() { Content = "Refresh readiness" };
    public Button LastDecision { get; } = new() { Content = "Last saved decision" };
    public Button PrepareFirst { get; } = new() { Content = "Prepare first execution review" };
    public Button CaptureNativeCheckpoint { get; } = new() { Content = "Capture native recovery checkpoint" };
    public Button ApproveFirst { get; } = new() { Content = "Approve reviewed assignment", IsEnabled = false };
    public Button EnableHost { get; } = new() { Content = "Enable reviewed host", IsEnabled = false };
    public Button StopHost { get; } = new() { Content = "Stop execution host" };
    public Button ReviewOngoing { get; } = new() { Content = "Review ongoing management", Visibility = Visibility.Collapsed };
    public Button ApproveOngoing { get; } = new() { Content = "Approve and start ongoing management", IsEnabled = false, Visibility = Visibility.Collapsed };
    public Button PauseBuying { get; } = new() { Content = "Pause new buys · keep protection", Visibility = Visibility.Collapsed };
    public Button ReviewMinute { get; } = new() { Content = "Review one-minute strategy", Visibility = Visibility.Collapsed };
    public Button ApproveMinute { get; } = new() { Content = "Approve reviewed one-minute strategy", IsEnabled = false, Visibility = Visibility.Collapsed };
    public Button ReviewSourceSwitch { get; } = new() { Content = "Review Portfolio rules only", Visibility = Visibility.Collapsed };
    public Button ApproveSourceSwitch { get; } = new() { Content = "Approve reviewed source switch", IsEnabled = false, Visibility = Visibility.Collapsed };
    public Button ReviewNextSession { get; } = new() { Content = "Review next session", Visibility = Visibility.Collapsed };
    public Button ApproveContinuation { get; } = new() { Content = "Approve October 21 continuation", IsEnabled = false, Visibility = Visibility.Collapsed };
    public Button PreviewCarry { get; } = new() { Content = "Preview October 20 carry" };
    public Button SaveCarry { get; } = new() { Content = "Save reviewed October 20 carry", IsEnabled = false };
    public CheckBox NoOutsideChanges { get; } = new() { Margin = new Thickness(0, 0, 0, 12), Height = double.NaN,
        MinHeight = 40, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Content = new TextBlock { Text = "I confirm no deposits, withdrawals, outside trades or other account changes since the original cash-only baseline, and will keep that scope through the reviewed October 20 close.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12 } };

    public BrokerExecutionSetupPanel()
    {
        Background = new SolidColorBrush(Color.FromRgb(17, 22, 29));
        Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 246));
        NoOutsideChanges.Foreground = Foreground;
        NoOutsideChanges.Style = new Style(typeof(CheckBox));
        var layout = new Grid { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Delphi Live execution", FontSize = 26 });
        header.Children.Add(new TextBlock { Text = "Review the selected strategy and actual account. Explicit ongoing approval starts its checked management until you pause it.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) });
        header.Children.Add(NoOutsideChanges);
        layout.Children.Add(header);
        var scroll = new ScrollViewer { Content = Summary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var actions = new WrapPanel();
        foreach (var button in new[] { Refresh, LastDecision, PreviewCarry, SaveCarry, CaptureNativeCheckpoint, PrepareFirst, ApproveFirst,
            ReviewOngoing, ApproveOngoing, PauseBuying, ReviewMinute, ApproveMinute, ReviewNextSession, ApproveContinuation, ReviewSourceSwitch, ApproveSourceSwitch, EnableHost, StopHost })
        {
            button.MinWidth = 215; button.MinHeight = 44; button.Padding = new Thickness(12, 9, 12, 9); button.Margin = new Thickness(0, 0, 10, 8);
            if (TryFindResource("WorkspaceButton") is Style style) button.Style = style;
            actions.Children.Add(button);
        }
        Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var note = new TextBlock { Text = "Stop new buys is separate and retains ordinary exit rules. Stop execution host stops all new cycles after a running cycle finishes; existing orders still require recovery.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(note, 3); layout.Children.Add(note); Content = layout;
    }
}
