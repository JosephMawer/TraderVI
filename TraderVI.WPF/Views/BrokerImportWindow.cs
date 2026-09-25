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

public static class BrokerImportCommands
{
    public static RoutedUICommand Open { get; } = new("Import broker history", "ImportBrokerHistory", typeof(BrokerImportCommands));
}

public sealed class BrokerImportWindow : Window
{
    private readonly StackPanel body = new() { Margin = new Thickness(24) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Button refresh = new() { Content = "Refresh preview", Padding = new Thickness(16, 9, 16, 9) };
    private readonly Button apply = new() { Content = "Link TFSA and save history", Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(12, 0, 0, 0), IsEnabled = false };
    private readonly StackPanel details = new();
    private BrokerImportPlan? plan;
    private bool busy;
    public event EventHandler? RefreshRequested;
    public event EventHandler? ApplyRequested;
    public BrokerImportWindow(Window owner, string accountName)
    {
        Owner = owner; Title = "Import Wealthsimple account history"; Width = 1040; Height = 740;
        MinWidth = 680; MinHeight = 480; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontSize = 14;
        Background = new SolidColorBrush(Color.FromRgb(26, 32, 41)); Foreground = Brushes.White;
        body.Children.Add(new TextBlock { Text = "Connect your account history", FontSize = 25, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = $"Wealthsimple: {accountName}\nTraderVI account: TFSA", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
        body.Children.Add(new TextBlock { Text = "The first import proposes matches to your recorded trades. With trade synchronization installed, supported completed orders also update real positions and trade history atomically. Existing trade records and position identities are preserved. After linking, refresh checks for external trades automatically.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(status); body.Children.Add(details);
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) }; buttons.Children.Add(refresh); buttons.Children.Add(apply); body.Children.Add(buttons);
        body.Children.Add(new TextBlock { Text = "Saving history does not activate a strategy. The strategy engine continues using its existing account records until account risk baselines and position protection are connected. Broker orders are saved as cumulative totals; individual fill times are not invented.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0) });
        Content = new ScrollViewer { Content = body, Background = Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        apply.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);
    }
    public void SetBusy(bool value, string? message = null)
    {
        busy = value; if (message is not null) status.Text = message;
        refresh.IsEnabled = !busy; apply.IsEnabled = !busy && plan is { CanImport: true, PreviousRequestId: null };
    }
    public void Present(BrokerImportPlan value, BrokerImportReceipt? saved = null)
    {
        plan = value; details.Children.Clear();
        status.Text = saved is not null ? $"Account history saved at {Viewmodels.TradingAccountOverview.Local(saved.SavedAt.UtcDateTime)}.\n{value.Rows.Count} cumulative orders are available in the saved history."
            : value.CanImport ? "Review these proposed matches and additions before linking the account." : "Import needs attention. No changes were applied.";
        string holdings = string.Join(", ", value.Observation.Holdings.Where(h => h.BrokerQuantity > 0).Select(h => $"{h.Symbol} {h.BrokerQuantity:0.########}"));
        details.Children.Add(new TextBlock { Text = $"Observed {Viewmodels.TradingAccountOverview.Local(value.Observation.SnapshotAt.UtcDateTime)}\nCash: {Money(value.Observation.Cash)} CAD · Buying power: {Money(value.Observation.BuyingPower)} CAD · Account value: {Money(value.Observation.AccountValue)} CAD\nCurrent holdings: {(holdings.Length == 0 ? "None" : holdings)}",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        details.Children.Add(new TextBlock { Text = $"{value.Rows.Count(r => r.Action == "Match recorded trade")} proposed matches · {value.Rows.Count(r => r.Action == "Add broker order")} new broker records · {value.Rows.Count(r => r.Action == "Already saved")} already saved", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 90,
            MaxHeight = 290, ColumnHeaderHeight = 42, RowHeight = 32,
            ItemsSource = value.Rows.Select(r => {
                var order = value.Observation.Orders.Single(o => o.ExternalId == r.ExternalId);
                return new { r.Symbol, r.Side, r.Quantity, Gross = Money(r.Gross), Fees = Money(r.Fees), Tax = Money(r.Tax), Net = Money(r.Net), r.Action,
                Detail = order.LastFilledAt is { } at
                    ? $"{r.Symbol}: last fill {Viewmodels.TradingAccountOverview.Local(at.UtcDateTime)} · {r.Action.ToLowerInvariant()}" +
                        (r.RecordedSymbol is null ? "" : $" · recorded as {r.RecordedSymbol}; broker listing verified")
                    : order.FillQuantityWasNull
                        ? $"{r.Symbol}: {(order.Status == "EXPIRED" ? "expired" : "cancelled")} — no fills reported; broker quantity was blank · {r.Action.ToLowerInvariant()}"
                        : $"{r.Symbol}: no shares filled · {r.Action.ToLowerInvariant()}" }; }).ToArray() };
        foreach (string name in new[] { "Symbol", "Side", "Quantity", "Gross", "Fees", "Tax", "Net", "Action" })
            grid.Columns.Add(new DataGridTextColumn { Header = name, Binding = new Binding(name), Width = new DataGridLength(name == "Action" ? 2 : 1, DataGridLengthUnitType.Star) });
        details.Children.Add(grid);
        var selected = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        selected.SetBinding(TextBlock.TextProperty, new Binding("SelectedItem.Detail") { Source = grid });
        grid.SelectedIndex = value.Rows.Count == 0 ? -1 : 0; details.Children.Add(selected);
        details.Children.Add(new TextBlock { Text = "Amounts are CAD. Unfilled orders remain in history without adding shares. Expired or cancelled orders with no reported fills record that the broker's quantity was blank.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        foreach (string block in value.Blocks)
            details.Children.Add(new TextBlock { Text = "• " + block, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        foreach (var difference in value.Differences ?? [])
        {
            var recorded = difference.Recorded; var broker = difference.Broker;
            details.Children.Add(new TextBlock { Text = $"{recorded.Symbol} {recorded.Side} · {recorded.TradingDate:yyyy-MM-dd} · compare {string.Join(", ", difference.Fields)}",
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 6) });
            var comparison = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
                ColumnHeaderHeight = 36, RowHeight = 36, MinHeight = 112,
                ItemsSource = new[] {
                    new { Source = "TraderVI record", Symbol = recorded.Symbol, Shares = (decimal)recorded.Quantity,
                        Gross = Money(recorded.Gross), Fees = Money(recorded.Fees), Tax = "Not recorded", Net = Money(recorded.Net) },
                    new { Source = "Wealthsimple", Symbol = broker.Symbol, Shares = broker.FilledQuantity,
                        Gross = Money(broker.GrossValue), Fees = Money(broker.Fees), Tax = Money(broker.Tax), Net = Money(broker.NetValue) } } };
            foreach (string name in new[] { "Source", "Symbol", "Shares", "Gross", "Fees", "Tax", "Net" })
                comparison.Columns.Add(new DataGridTextColumn { Header = name, Binding = new Binding(name),
                    Width = new DataGridLength(name is "Source" or "Tax" ? 1.6 : 1, DataGridLengthUnitType.Star) });
            details.Children.Add(comparison);
            details.Children.Add(new TextBlock { Text = "Amounts are CAD. These are candidate records, not an approved match. Resolve the recorded difference before linking history; no amount has been replaced.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        }
        if (saved is not null) plan = null;
        refresh.Content = saved is not null || value.PreviousRequestId.HasValue ? "Refresh and update saved history" : "Refresh preview";
        SetBusy(busy);
    }
    public void Failed(string message) { plan = null; status.Text = message; SetBusy(busy); }
    private static string Money(decimal? value) => value?.ToString("0.00######") ?? "—";
}
