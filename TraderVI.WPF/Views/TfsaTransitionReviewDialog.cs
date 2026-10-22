using Core.WSTrade.Authentication;
#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

/// <summary>A read-only transition review. No assignment, local fill or broker order is written.</summary>
public sealed class TfsaTransitionReviewDialog : Window
{
    public TfsaTransitionReviewDialog(Window owner, WealthsimpleConnection connection, WealthsimpleAccountViewModel account,
        IReadOnlyList<TfsaLiveStrategyCandidate> candidates,
        Func<WealthsimpleTfsaAccount, long, CancellationToken, Task<BrokerAccountReconciliation>>? reconcile = null,
        Guid? selectedPortfolioId = null)
    {
        Owner = owner; Title = "Review TFSA strategy transition"; Width = 980; Height = 780; MinWidth = 700; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 22, 29)); Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 246));
        var body = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        TextBlock Text(string text, int size = 14)
        {
            var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            body.Children.Add(block); return block;
        }
        Text("Permanent TFSA · incoming strategy", 24);
        Text("Your TFSA keeps its actual financial history. The paper portfolio continues independently. Matching broker securities are retained at their actual quantity. The first execution version uses regular-session market orders; required exits must reconcile before new buys.");
        Text("Delphi Live portfolios · highest daily percentage first");
        var sources = new ComboBox { ItemsSource = candidates, MinHeight = 38, FontSize = 14, Margin = new Thickness(0, 0, 0, 14) }; body.Children.Add(sources);
        Text("Maximum CAD for new purchases (0 means no purchase budget)");
        var budget = new TextBox { Text = "0", Width = 160, MinHeight = 34, FontSize = 14, MaxLength = 32, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) }; body.Children.Add(budget);
        var refresh = new Button { Content = "Reconcile account and prepare review", HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)FindResource("WorkspaceButton"), Margin = new Thickness(0, 0, 0, 14) }; body.Children.Add(refresh);
        var status = Text("Choose a source and prepare the review.");
        var detail = Text("");
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 110, MaxHeight = 330, FontSize = 14, RowHeight = double.NaN };
        grid.Columns.Add(new DataGridTextColumn { Header = "Symbol", Binding = new System.Windows.Data.Binding("Symbol"), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Transition", Binding = new System.Windows.Data.Binding("Action"), Width = 160 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Actual shares", Binding = new System.Windows.Data.Binding("ExistingQuantity"), Width = 115 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Reason", Binding = new System.Windows.Data.Binding("Explanation"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var cell = new Style(typeof(TextBlock));
        cell.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        cell.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 8, 6, 8)));
        foreach (var column in grid.Columns.Cast<DataGridTextColumn>()) column.ElementStyle = cell;
        body.Children.Add(grid);
        Text("Preview only. Real trading and automatic TFSA monitoring are not enabled yet. This screen does not activate a strategy or send orders.").Margin = new Thickness(0, 18, 0, 14);
        var lifetime = new CancellationTokenSource(); bool closed = false;
        void Invalidate() { grid.ItemsSource = null; detail.Text = ""; status.Text = "Inputs changed. Prepare a fresh review."; }
        sources.SelectionChanged += (_, _) => Invalidate(); budget.TextChanged += (_, _) => Invalidate();
        long visibleRevision = connection.SessionRevision;
        System.ComponentModel.PropertyChangedEventHandler sessionChanged = (_, _) =>
        {
            if (!connection.IsConnected || visibleRevision != connection.SessionRevision)
            { visibleRevision = connection.SessionRevision; grid.ItemsSource = null; detail.Text = ""; status.Text = "Connection changed. Prepare a fresh review after signing in."; }
        };
        connection.PropertyChanged += sessionChanged;
        Closed += (_, _) => { closed = true; connection.PropertyChanged -= sessionChanged; lifetime.Cancel(); lifetime.Dispose(); };
        sources.SelectedItem = selectedPortfolioId is { } id ? candidates.FirstOrDefault(c => c.Snapshot.PortfolioId == id)
            : candidates.FirstOrDefault(c => c.SupportsTransitionReview);
        if (selectedPortfolioId.HasValue && sources.SelectedItem == null)
            status.Text = "The requested strategy is no longer available. Choose a current source; no replacement was selected automatically.";
        refresh.Click += async (_, _) =>
        {
            if (sources.SelectedItem is not TfsaLiveStrategyCandidate selected) return;
            if (!decimal.TryParse(budget.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var cap) || cap < 0)
            { status.Text = "Enter a nonnegative CAD purchase budget."; return; }
            refresh.IsEnabled = sources.IsEnabled = budget.IsEnabled = false;
            grid.ItemsSource = null; detail.Text = ""; status.Text = "Reading broker state and the exact selected strategy…";
            long revision = connection.SessionRevision;
            try
            {
                if (!selected.SupportsTransitionReview)
                { status.Text = "This source's entry profile is not supported for transition review. Normal can be reviewed; execution remains disabled."; return; }
                var ledger = await new DelphiLiveLedgerRepository().LoadPortfolioAsync(selected.Snapshot.PortfolioId, lifetime.Token)
                    ?? throw new InvalidOperationException();
                if (ledger.Revision != selected.Snapshot.Revision || ledger.PolicyVersionId != selected.Snapshot.PolicyVersionId)
                { status.Text = "The source portfolio changed. Close and reopen the review to rank the latest evidence."; return; }
                var exact = await new TradingPromotionRepository().ReviewBrokerAsync(ledger.PortfolioId, TradingStrategyFamily.DelphiLive, lifetime.Token);
                if (exact.Strategy.Strategy.PolicyVersionId != ledger.PolicyVersionId ||
                    exact.Strategy.Comparison != ledger.ComparisonAccount) throw new InvalidOperationException();
                await account.RefreshAsync();
                if (closed || revision != connection.SessionRevision || !connection.IsConnected || account.Snapshot == null) return;
                var incoming = new List<TfsaIncomingHolding>();
                foreach (var symbol in ledger.OpenPositions.Select(p => p.Symbol).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var found = await connection.FindCanadianSecuritiesAsync(symbol, lifetime.Token);
                    incoming.Add(found.Status == WealthsimpleReadStatus.Success && found.Data?.Count == 1
                        ? TfsaIncomingHolding.FromSecurity(found.Data[0]) : new(symbol, null));
                }
                if (closed || revision != connection.SessionRevision || !connection.IsConnected || account.Snapshot == null) return;
                if (reconcile == null) { status.Text = "Account reconciliation is not connected. Reopen this review from the main account."; return; }
                var reviewedAccount = account.Snapshot.Account;
                status.Text = "Reconciling confirmed order history before handover review…";
                var reconciliation = await reconcile(reviewedAccount, revision, lifetime.Token);
                await account.RefreshAsync();
                if (closed || revision != connection.SessionRevision || !connection.IsConnected || account.Snapshot == null ||
                    !ReferenceEquals(account.Snapshot.Account, reviewedAccount)) return;
                var currentSource = await new DelphiLiveLedgerRepository().LoadPortfolioAsync(ledger.PortfolioId, lifetime.Token);
                if (closed || revision != connection.SessionRevision || !connection.IsConnected) return;
                if (account.Snapshot == null || !ReferenceEquals(account.Snapshot.Account, reviewedAccount))
                { status.Text = "The selected TFSA changed. Prepare a fresh review for the current account."; return; }
                if (currentSource == null || currentSource.Revision != ledger.Revision || currentSource.PolicyVersionId != ledger.PolicyVersionId)
                { status.Text = "The source portfolio changed during reconciliation. Reopen the review to use its current intended holdings."; return; }
                var preview = TfsaTransitionPlanning.PreviewReconciled(account.Snapshot, account.PendingOrders, incoming, cap, DateTimeOffset.UtcNow, reconciliation);
                grid.ItemsSource = preview.Rows;
                detail.Text = $"{selected.DailySummary}\nComplete strategy version: {exact.Strategy.Strategy.VersionId:D}\n" +
                    $"Entry profile: {exact.Strategy.GateProfile} · source observation: {ledger.UpdatedUtc:yyyy-MM-dd HH:mm:ss} UTC · {ledger.OpenPositions.Count()} held securities\n" +
                    $"Purchase ceiling from budget, cash and buying power: {preview.AvailablePurchaseCash?.ToString("N2") ?? "Unavailable"} CAD. This is a ceiling, not an order allocation.";
                status.Text = "Preview prepared. " + (preview.Blocks.Count == 0 ? "Broker snapshot checks passed. " : string.Join(" ", preview.Blocks) + " ") +
                    (incoming.Count == 0 ? "This paper portfolio currently holds no securities. Promotion does not create a buy signal. " : "New positions still require a fresh strategy entry evaluation. ") +
                    "No strategy has been activated and no orders have been sent.";
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "Review cancelled."; }
            catch { if (!closed) status.Text = "The review could not be verified. Refresh the connection and reopen it; no partial review was accepted."; }
            finally { if (!closed) refresh.IsEnabled = sources.IsEnabled = budget.IsEnabled = true; }
        };
    }
}
