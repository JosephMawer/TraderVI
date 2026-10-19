#nullable enable
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

/// <summary>Selects existing verified records only; creates no order, financial reference or authority.</summary>
public sealed class BrokerOpeningSalesReview : UserControl
{
    private readonly ListBox list = new() { SelectionMode = SelectionMode.Multiple };
    public Button Confirm { get; } = new() { Content = "Use these two opening sales", IsEnabled = false, Padding = new Thickness(12, 8, 12, 8) };
    public ImmutableArray<Guid> SelectedIds => Selected().Select(s => s.Intent.RequestId).ToImmutableArray();
    private ImmutableArray<ManualSellRecord> Selected() => list.SelectedItems.Cast<ListBoxItem>()
        .Select(item => (ManualSellRecord)item.Tag).ToImmutableArray();
    public static ImmutableArray<Guid>? Select(Window owner, ImmutableArray<ManualSellRecord> candidates)
    {
        var dialog = new Window { Owner = owner, Title = "Review the two original opening sales",
            Width = 760, Height = 440, MinWidth = 560, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new BrokerOpeningSalesReview(candidates); dialog.Content = panel;
        panel.Confirm.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true ? panel.SelectedIds : null;
    }

    public BrokerOpeningSalesReview(ImmutableArray<ManualSellRecord> candidates)
    {
        Background = new SolidColorBrush(Color.FromRgb(17, 22, 29));
        Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 246));
        list.Background = Background; list.Foreground = Foreground; list.BorderThickness = new Thickness(0);
        var layout = new DockPanel { Margin = new Thickness(20) };
        var instructions = new TextBlock { Text = "Select the full sales of the two holdings you originally chose to liquidate. Exclude any later sale of a verification purchase. Match the quantity, time and original order ID in Manual sells. This selection sends no orders and saves no risk references or trading approval.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(instructions, Dock.Top); layout.Children.Add(instructions);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8) };
        foreach (var button in new[] { Confirm, cancel })
            if (TryFindResource("WorkspaceButton") is Style style) button.Style = style;
        actions.Children.Add(Confirm); actions.Children.Add(cancel); DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        foreach (var sale in candidates)
        {
            var row = new DockPanel();
            var marker = new CheckBox { IsHitTestVisible = false, Focusable = false,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            marker.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ListBoxItem.IsSelected)) {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1), Mode = BindingMode.OneWay });
            DockPanel.SetDock(marker, Dock.Left); row.Children.Add(marker);
            row.Children.Add(new TextBlock { Foreground = Foreground,
                Text = $"{sale.Intent.Symbol} — {sale.Intent.Quantity} shares — {sale.Intent.CreatedAt.ToLocalTime():g}\nOrder {sale.Intent.RequestId:D}" });
            list.Items.Add(new ListBoxItem { Tag = sale, Content = row,
                Padding = new Thickness(8), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        list.SelectionChanged += (_, _) => {
            var selected = Selected();
            Confirm.IsEnabled = selected.Length == 2 && selected.Select(s => s.Intent.SecurityId).Distinct().Count() == 2;
        };
        layout.Children.Add(list); Content = layout;
    }
}
