#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

public static class BrokerReconciliationCommands
{
    public static RoutedUICommand Refresh { get; } = new("Reconcile account", "ReconcileAccount", typeof(BrokerReconciliationCommands));
}

public sealed class BrokerReconciliationWindow : Window
{
    private readonly StackPanel panel = new() { Margin = new Thickness(24) };
    private readonly string accountName;
    public BrokerReconciliationWindow(Window owner, string accountName = "Self-directed TFSA")
    {
        this.accountName = accountName;
        Owner = owner; Title = "Wealthsimple account reconciliation"; Width = 980; Height = 680; MinWidth = 640; MinHeight = 450;
        MaxHeight = SystemParameters.WorkArea.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(26, 32, 41)); Foreground = Brushes.White;
        FontSize = 14;
        Content = new ScrollViewer { Content = panel, Background = Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Message("Reading account history…", "Discovering orders and checking their fills against your current Wealthsimple holdings.");
    }
    public void Message(string title, string detail)
    {
        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) });
    }
    public void Present(BrokerAccountReconciliation result)
    {
        Message(result.HoldingsMatch ? "Holdings match confirmed order history" : "Account reconciliation needs attention",
            $"{accountName} · checked {Viewmodels.TradingAccountOverview.Local(result.CheckedAt.UtcDateTime)} · {result.Orders.Count} distinct orders\n" +
            $"Observed cash: {Money(result.Cash)} · Buying power: {Money(result.BuyingPower)}\n" +
            "Cash is Wealthsimple's current balance. Cash-flow history is not verified by this check.");
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 90, MaxHeight = 290, RowHeight = 32, ColumnHeaderHeight = 60,
            ItemsSource = result.Holdings.Select(h => new { h.Symbol, Broker = h.BrokerQuantity, History = h.ReconstructedQuantity,
                BrokerCost = Money(h.BrokerBookValue), HistoryCost = Money(h.ReconstructedBookValue),
                Status = h.Matches ? "Match" : "Review" }).ToArray() };
        foreach (var (heading, property) in new[] { ("Symbol", "Symbol"), ("Broker\nshares", "Broker"), ("History\nshares", "History"),
            ("Broker\nbook value", "BrokerCost"), ("History\nbook value", "HistoryCost"), ("Status", "Status") })
            grid.Columns.Add(new DataGridTextColumn { Header = heading, Binding = new Binding(property), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        panel.Children.Add(grid);
        foreach (string issue in result.HistoryConflicts.Concat(result.Blocks))
            panel.Children.Add(new TextBlock { Text = "• " + issue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Saved in your protected local account history. Tracked trading records have not changed. This check does not activate a strategy or submit orders; portfolio execution still requires the remaining account and activation checks.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) });
    }
    private static string Money(decimal? amount) => amount.HasValue ? amount.Value.ToString("N2") + " CAD" : "Unavailable";
}
