#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class TradingRecommendationsView : UserControl
{
    private readonly TradingAccountRepository accounts = new();
    private readonly TradingPromotionRepository promotions = new();
    private readonly TradingRecommendationRepository recommendations = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private TradingAccountView? snapshot;
    private TradingAccountView? displayed;
    private TradingAccountReportRequest? reportRequest;
    private TradingReportedBuyRequest? buyRequest;
    private WealthsimpleAccountViewModel? broker;
    private WealthsimpleConnection? brokerConnection;
    private Func<Core.WSTrade.Accounts.WealthsimpleTfsaAccount, long, CancellationToken, Task<Core.WSTrade.Accounts.BrokerAccountReconciliation>>? reconcileAccount;
    public void InitializeWealthsimple(WealthsimpleConnection connection,
        Func<Core.WSTrade.Accounts.WealthsimpleTfsaAccount, long, CancellationToken, Task<Core.WSTrade.Accounts.BrokerAccountReconciliation>>? reconcile = null,
        Func<Core.WSTrade.Accounts.WealthsimpleTfsaAccount, long, CancellationToken, Task<Core.WSTrade.Accounts.BrokerAccountSynchronization>>? synchronize = null,
        Func<bool>? canSynchronize = null)
    {
        broker?.Dispose();
        brokerConnection = connection;
        reconcileAccount = reconcile;
        broker = new WealthsimpleAccountViewModel(connection, synchronize: synchronize, canSynchronize: canSynchronize,
            managedStrategy: new(new BrokerManagedSessionControls(new BrokerManagedSessionRepository())));
        BrokerPanel.DataContext = broker;
        _ = broker.RefreshAsync();
    }
    public void StopWealthsimple() => broker?.Dispose();
    public Task RefreshRecordedAccountAsync() => Run(() => RefreshDisplayAsync(Account.Text));
    public Task RefreshWealthsimpleAsync() => broker?.RefreshAsync() ?? Task.CompletedTask;
    public Task PollWealthsimpleAsync() => broker?.PollAsync() ?? Task.CompletedTask;
    public bool IsCurrentManualSellSelection(Core.WSTrade.Orders.ManualSellSelection selection) => broker?.IsCurrentSellSelection(selection) == true;
    public bool IsCurrentBrokerAccount(Core.WSTrade.Accounts.WealthsimpleTfsaAccount account) =>
        broker?.CanRefresh == true && ReferenceEquals(broker.SelectedAccount, account);
    public TradingRecommendationsView()
    {
        InitializeComponent();
        BrokerPanel.ReviewTransitionRequested += async (_, _) => await Run(async () =>
        {
            if (broker == null || brokerConnection?.IsConnected != true) return;
            var portfolios = await new DelphiLiveLedgerRepository().GetPortfoliosForSessionAsync(DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow)));
            var candidates = TfsaLiveStrategySelection.Rank(portfolios);
            new TfsaTransitionReviewDialog(Window.GetWindow(this), brokerConnection, broker, candidates, reconcileAccount).ShowDialog();
        });
        Account.TextChanged += (_, _) => BrokerPanel.Visibility = string.Equals(Account.Text.Trim(), "TFSA", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_,_) => await Run(async () =>
        {
            if(snapshot is null) await LoadAsync();
            else await RefreshDisplayAsync(snapshot.AccountLabel);
        });
    }
    private async Task LoadAsync(CancellationToken token = default)
    {
        displayed = null;
        OverviewPanel.DataContext = null;
        Recommendations.ItemsSource = null;
        History.ItemsSource = null;
        Source.Text = "Loading the selected account…";
        snapshot = await accounts.ReadAsync(Account.Text, token);
        await PresentAsync(snapshot,token);
        HoldingsText.Text = snapshot.Holdings.IsEmpty ? "Recorded Real holdings: none." : "Recorded Real holdings: " + string.Join(", ", snapshot.Holdings.Select(h => $"{h.Symbol} {h.Quantity} shares"));
        if (snapshot.Report is { } report)
        {
            HoldingsText.Text += $"\nLast report: {report.ReportedUtc:yyyy-MM-dd HH:mm:ss} UTC by {report.ReportedBy}.";
            Cash.Text = report.AvailableCash?.ToString(CultureInfo.InvariantCulture); Nav.Text = report.NetAssetValue?.ToString(CultureInfo.InvariantCulture);
            Opening.Text = report.SessionOpeningValue?.ToString(CultureInfo.InvariantCulture); High.Text = report.HighestClosingValue?.ToString(CultureInfo.InvariantCulture);
            Pending.Text = report.PendingOrderCount?.ToString(CultureInfo.InvariantCulture);
        }
        else Cash.Text=Nav.Text=Opening.Text=High.Text=Pending.Text="";
    }
    public Task ShowAccountAsync(string account) => Run(async () => { Account.Text = account; await LoadAsync(); reportRequest = null; });
    private async Task RefreshDisplayAsync(string account,CancellationToken token=default) =>
        await PresentAsync(await accounts.ReadAsync(account,token),token);
    private async Task PresentAsync(TradingAccountView current,CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var marks = current.Installed ? await new ActivePositionRepository().GetActivePositions() : [];
        var local = PaperTradingMonitor.ToToronto(now);
        var trades = current.Installed ? await new TradeLogRepository().GetTradesByDateRange(local.Date.AddDays(-29), local) : null;
        OverviewPanel.DataContext = TradingAccountOverview.Create(current, marks, now, trades);
        HoldingsText.Text = current.Holdings.IsEmpty ? "Recorded Real holdings: none." : "Recorded Real holdings: " + string.Join(", ", current.Holdings.Select(h => $"{h.Symbol} {h.Quantity} shares"));
        if (current.Report is { } savedReport) HoldingsText.Text += $"\nLast report: {savedReport.ReportedUtc:yyyy-MM-dd HH:mm:ss} UTC by {savedReport.ReportedBy}.";
        displayed=current;
        Source.Text=current.Assignment is { } a?$"{a.AccountLabel}: {a.Strategy.Name}\nComplete version {a.Strategy.VersionId:D} · approved {a.EffectiveUtc:yyyy-MM-dd HH:mm:ss} UTC by {a.Reviewer}"
            :$"{current.AccountLabel}: no approved recommendation source. Review a paper strategy in Portfolios.";
        var batch=current.Latest;
        Status.Text=!current.Installed?"Promotion migration 029 has not been installed.":batch is null?"No recommendation evaluation has been saved.":
            $"{(batch.PromotionId!=current.Assignment?.PromotionId?"Previous assignment's result — evaluate the new source. ":"")}{batch.Result.Status} · evaluated {batch.EvaluatedUtc:yyyy-MM-dd HH:mm:ss} UTC\n"+string.Join("\n",batch.Result.Warnings);
        bool currentLedger = TradingAccountOverview.MatchesLedger(current);
        Recommendations.ItemsSource=currentLedger && batch?.PromotionId==current.Assignment?.PromotionId?batch?.Result.Recommendations:null;
        if (!currentLedger && batch is not null) Status.Text += "\nAccount facts changed or are unavailable. Previous recommendations are retained as history; evaluate again using the current holdings. New purchases also require reconciled account values.";
        History.ItemsSource=await promotions.HistoryAsync(current.AccountLabel,token);
        if(snapshot is not null && snapshot.LedgerFingerprint!=current.LedgerFingerprint)
            Status.Text+="\nFinancial facts changed since the account form was loaded. Load account again before confirming its state.";
    }
    private async Task Run(Func<Task> action)
    {
        if (!await gate.WaitAsync(0)) return;
        IsEnabled = false;
        try { await action(); }
        catch (Exception error) { Status.Text = error.Message; }
        finally { IsEnabled = true; gate.Release(); }
    }
    public async Task TickAsync(CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) return;
        try
        {
            var workflow = new TradingRecommendationWorkflow(recommendations, new TradingRecommendationQuoteSource());
            if(PaperTradingMonitor.IsAutomaticPollTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow)))
                foreach (var account in await recommendations.AssignedAccountsAsync(token)) await workflow.EvaluateAsync(account, token);
            // Do not overwrite account-report fields while the operator is editing them.
            if(IsVisible && snapshot is not null) await RefreshDisplayAsync(snapshot.AccountLabel,token);
        }
        catch (Exception error) when (error is not OperationCanceledException) { Status.Text = "Recommendation evaluation failed; the approved assignment remains active. Evaluate again to retry. " + error.Message; }
        finally { gate.Release(); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(async () => { await LoadAsync(); reportRequest = null; });
    private async void Evaluate_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (displayed is null || !string.Equals(displayed.AccountLabel, Account.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Load the selected account before evaluating its strategy.");
        await new TradingRecommendationWorkflow(recommendations, new TradingRecommendationQuoteSource()).EvaluateAsync(displayed.AccountLabel);
        await LoadAsync();
    });
    private static decimal Money(TextBox field) => decimal.TryParse(field.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException("Enter CAD values using a decimal point.");
    private async void Report_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (snapshot is null || !string.Equals(snapshot.AccountLabel, Account.Text.Trim(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Load this account before confirming its state.");
        if (!int.TryParse(Pending.Text, out var pending)) throw new ArgumentException("Enter the actual number of outstanding orders, including zero.");
        var request = new TradingAccountReportRequest(Guid.NewGuid(), snapshot.AccountLabel, snapshot.LedgerFingerprint, Money(Cash), Money(Nav), Money(Opening), Money(High), pending, Environment.UserName);
        // Reuse the request identity after an uncertain save, unless the entered facts actually changed.
        if (reportRequest is null || request with { RequestId = reportRequest.RequestId } != reportRequest) reportRequest = request;
        await accounts.ReportAsync(reportRequest); reportRequest = null; await LoadAsync(); Status.Text = "Account report saved. Evaluate to refresh recommendations.";
    });
    private async void Previous_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (History.SelectedItem is not TradingStrategyAssignment previous) throw new InvalidOperationException("Select an earlier approval.");
        var review = await promotions.ReviewPreviousAsync(previous.PromotionId, previous.AccountLabel);
        new TradingPromotionDialog(Window.GetWindow(this), review).ShowDialog(); await LoadAsync();
    });
    private async void Capital_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (snapshot?.Report is null || !string.Equals(snapshot.AccountLabel,Account.Text.Trim(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Load and reconcile this account first.");
        var dialog = new OperatorActionDialog(Window.GetWindow(this), "Review account capital hold",
            "Confirm the entered current account values after reviewing the drawdown. This clears the capital-review hold and rebases its closing high to the reported account value. The daily loss hold and Trading pause remain in effect.");
        if (dialog.ShowDialog() != true) return;
        if (!int.TryParse(Pending.Text, out var pending)) throw new ArgumentException("Report the outstanding order count.");
        var request = new TradingAccountReportRequest(Guid.NewGuid(), snapshot.AccountLabel, snapshot.LedgerFingerprint,
            Money(Cash), Money(Nav), Money(Opening), Money(High), pending, Environment.UserName)
        { CapitalReviewReason = dialog.Reason };
        await accounts.ReportAsync(request); await LoadAsync(); Status.Text = "Capital review saved. Trading pause and the daily loss hold were preserved.";
    });
    private async void Buy_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (buyRequest is null)
        {
            if (Recommendations.SelectedItem is not TradingRecommendation { Action: "Buy" or "Add" } row || displayed?.Latest is not { } batch ||
                !string.Equals(displayed.AccountLabel,Account.Text.Trim(),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select a purchase recommendation.");
            var dialog = new ReportedPurchaseDialog(Window.GetWindow(this), $"{row.Symbol} in {displayed.AccountLabel}");
            if (dialog.ShowDialog() != true) return;
            buyRequest = new(Guid.NewGuid(), batch.BatchId, displayed.AccountLabel, row.Symbol, row.Kind, dialog.Quantity, dialog.Price, dialog.FillUtc, Environment.UserName, dialog.Notes);
        }
        if(!string.Equals(buyRequest.AccountLabel,Account.Text.Trim(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"A purchase report for {buyRequest.Symbol} in {buyRequest.AccountLabel} is awaiting retry. Load that account to retry the same report.");
        await accounts.RecordBuyAsync(buyRequest); buyRequest = null; await LoadAsync();
        Status.Text = "Actual purchase recorded. Report the account's updated cash and state before considering new risk.";
    });
    private async void Sell_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Recommendations.SelectedItem is not TradingRecommendation { PositionId: Guid position } row || displayed is null ||
            !string.Equals(displayed.AccountLabel,Account.Text.Trim(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select a holding recommendation.");
        var dialog = new OperatorActionDialog(Window.GetWindow(this), "Report completed sale",
            $"Record one completed all-shares sale of {row.Symbol} with zero commission. This sends no order. Partial exits and fees remain unsupported.", true);
        if (dialog.ShowDialog() != true) return;
        await new TrackedPositionExecutionRepository().TryRecordManualRealExitAsync(position, dialog.FillPrice, dialog.FillTime,
            row.Action == "Sell" ? row.Reason : "OperatorRecordedExit", $"Recommendation {displayed.Latest?.BatchId:D}; {dialog.Reason}");
        await LoadAsync(); Status.Text = "Actual sale recorded. Report the account's updated cash and state.";
    });
}
